using System.Security.Cryptography;
using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace HouseFlow.Application.Services;

public class HouseMemberService : IHouseMemberService
{
    private readonly IApplicationDbContext _context;

    /// <summary>Maximum number of pending invitations per house.</summary>
    private const int MaxPendingInvitationsPerHouse = 20;

    public HouseMemberService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<HouseMemberDto>> GetHouseMembersAsync(Guid houseId, Guid userId)
    {
        await EnsureAccessAsync(houseId, userId,
            HouseRole.Owner, HouseRole.CollaboratorRW, HouseRole.CollaboratorRO, HouseRole.Tenant);

        // Owner first (M5), then by arrival date.
        var members = await _context.HouseMembers
            .AsNoTracking()
            .Where(m => m.HouseId == houseId)
            .Include(m => m.User)
            .OrderBy(m => m.Role == HouseRole.Owner ? 0 : 1)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync();

        return members.Select(ToDto);
    }

    public async Task<HouseMemberDto?> UpdateMemberRoleAsync(Guid memberId, HouseRole newRole, Guid userId)
    {
        var member = await _context.HouseMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == memberId);

        if (member == null) return null;

        // Only owner can change roles
        await EnsureAccessAsync(member.HouseId, userId, HouseRole.Owner);

        // Cannot change owner's role
        if (member.Role == HouseRole.Owner)
            throw new InvalidOperationException("Cannot change the owner's role");

        // Cannot promote to Owner
        if (newRole == HouseRole.Owner)
            throw new InvalidOperationException("Cannot assign owner role");

        member.Role = newRole;
        member.UpdatedAt = DateTime.UtcNow;

        // Reset canLogMaintenance when changing away from Tenant
        if (newRole != HouseRole.Tenant)
            member.CanLogMaintenance = true;

        await _context.SaveChangesAsync();
        return ToDto(member);
    }

    public async Task<bool> UpdateMemberPermissionsAsync(Guid memberId, bool? canLogMaintenance, bool? canViewCosts, Guid userId)
    {
        var member = await _context.HouseMembers.FindAsync(memberId);
        if (member == null) return false;

        // Only owner can change permissions
        await EnsureAccessAsync(member.HouseId, userId, HouseRole.Owner);

        // These permissions are only relevant for tenants
        if (member.Role != HouseRole.Tenant)
            throw new InvalidOperationException("Permissions are only configurable for tenants");

        if (canLogMaintenance.HasValue)
            member.CanLogMaintenance = canLogMaintenance.Value;

        if (canViewCosts.HasValue)
            member.CanViewCosts = canViewCosts.Value;

        member.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RemoveMemberAsync(Guid memberId, Guid userId)
    {
        var member = await _context.HouseMembers.FindAsync(memberId);
        if (member == null) return false;

        // Only owner can remove members
        await EnsureAccessAsync(member.HouseId, userId, HouseRole.Owner);

        // Cannot remove self (owner)
        if (member.UserId == userId)
            throw new InvalidOperationException("Cannot remove yourself from the house");

        _context.HouseMembers.Remove(member);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<AllCollaboratorsResponseDto> GetAllCollaboratorsAsync(Guid userId)
    {
        // Get all houses owned by this user
        var ownedHouses = await _context.Houses
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .Include(h => h.Members)
                .ThenInclude(m => m.User)
            .Include(h => h.Invitations)
                .ThenInclude(i => i.CreatedByUser)
            .ToListAsync();

        var result = ownedHouses.Select(h => new HouseCollaboratorsDto(
            h.Id,
            h.Name,
            h.Members.Where(m => m.Role != HouseRole.Owner).Select(ToDto),
            h.Invitations
                .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt > DateTime.UtcNow)
                .Select(i => ToInvitationDto(i, h.Name))
        ));

        return new AllCollaboratorsResponseDto(result);
    }

    // --- Invitations ---

    /// <summary>Validity of a new or re-sent invitation.</summary>
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    public async Task<InvitationDto> CreateInvitationAsync(Guid houseId, HouseRole role, string email, Guid userId)
    {
        if (role == HouseRole.Owner)
            throw new InvalidOperationException("Cannot create invitation for owner role");

        // R5: the owner invites any role; a RW collaborator may invite a tenant only.
        var callerRole = await EnsureAccessAsync(houseId, userId, HousePermissions.Inviters);
        EnsureCanHandleInvitation(callerRole, role);

        var house = await _context.Houses.FindAsync(houseId)
            ?? throw new KeyNotFoundException("House not found");

        var normalizedEmail = email.Trim();
        var lowerEmail = normalizedEmail.ToLowerInvariant();

        var alreadyMember = await _context.HouseMembers
            .AnyAsync(m => m.HouseId == houseId && m.User!.Email.ToLower() == lowerEmail);
        if (alreadyMember)
            throw new ConflictException(ErrorCodes.AlreadyMember, "This person is already a member of this house");

        var now = DateTime.UtcNow;
        var alreadyPending = await _context.Invitations
            .AnyAsync(i => i.HouseId == houseId
                && i.Status == InvitationStatus.Pending
                && i.ExpiresAt > now
                && i.Email != null && i.Email.ToLower() == lowerEmail);
        if (alreadyPending)
            throw new ConflictException(ErrorCodes.InvitationAlreadyPending, "An invitation is already pending for this email");

        // H3: Enforce pending invitation limit per house
        var pendingCount = await _context.Invitations
            .CountAsync(i => i.HouseId == houseId
                && i.Status == InvitationStatus.Pending
                && i.ExpiresAt > now);

        if (pendingCount >= MaxPendingInvitationsPerHouse)
            throw new BusinessRuleException(ErrorCodes.InvitationLimitReached, $"Maximum of {MaxPendingInvitationsPerHouse} pending invitations per house reached");

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            Token = NewInvitationToken(),
            Email = normalizedEmail,
            Role = role,
            Status = InvitationStatus.Pending,
            HouseId = houseId,
            CreatedByUserId = userId,
            ExpiresAt = now.Add(InvitationLifetime),
            CreatedAt = now
        };

        _context.Invitations.Add(invitation);
        await _context.SaveChangesAsync();

        var creator = await _context.Users.FindAsync(userId);
        return ToInvitationDto(invitation, house.Name, creator);
    }

    public async Task<IEnumerable<InvitationDto>> GetHouseInvitationsAsync(Guid houseId, Guid userId)
    {
        // R5: the owner sees every invitation, a RW collaborator the tenant invitations only.
        var callerRole = await EnsureAccessAsync(houseId, userId, HousePermissions.Inviters);
        var tenantOnly = callerRole != HouseRole.Owner;

        var house = await _context.Houses.FindAsync(houseId)
            ?? throw new KeyNotFoundException("House not found");

        // Not yet answered: pending (even past their expiry, so the owner can re-send them) or already
        // flagged expired by the retention job. Accepted / declined / cancelled ones are not listed.
        var invitations = await _context.Invitations
            .AsNoTracking()
            .Where(i => i.HouseId == houseId
                && (i.Status == InvitationStatus.Pending || i.Status == InvitationStatus.Expired)
                && (!tenantOnly || i.Role == HouseRole.Tenant))
            .Include(i => i.CreatedByUser)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return invitations.Select(i => ToInvitationDto(i, house.Name));
    }

    // M3: limited info on an unauthenticated endpoint (no inviter email, no member list).
    public async Task<InvitationInfoDto?> GetInvitationInfoAsync(string token, Guid? userId = null)
    {
        var invitation = await _context.Invitations
            .AsNoTracking()
            .Include(i => i.House)
            .Include(i => i.CreatedByUser)
            .FirstOrDefaultAsync(i => i.Token == token);

        if (invitation == null) return null;

        bool? isAlreadyMember = null;
        if (userId is { } uid)
            isAlreadyMember = await GetUserRoleAsync(invitation.HouseId, uid) != null;

        var now = DateTime.UtcNow;
        var usable = IsUsable(invitation, now);

        // P04 banner chips: device types only (never names, brands, models or maintenance), and — like the
        // email — only while the invitation can still be used.
        IReadOnlyList<string> deviceTypes = [];
        if (usable)
        {
            var devices = await _context.Devices
                .AsNoTracking()
                .Where(d => d.HouseId == invitation.HouseId)
                .Select(d => new { d.CreatedAt, d.Id, d.Type })
                .ToListAsync();
            deviceTypes = DeviceChips.TypesInCreationOrder(devices.Select(d => (d.CreatedAt, d.Id, d.Type)));
        }

        return new InvitationInfoDto(
            invitation.Id,
            invitation.House?.Name ?? "",
            invitation.Role.ToString(),
            FormatInviterName(invitation.CreatedByUser),
            invitation.ExpiresAt,
            !usable,
            invitation.HouseId,
            // Minimisation (GDPR art. 5-1-c): the invitee's email is disclosed to the link holder only
            // while the invitation can still be used — never once answered, cancelled or expired.
            usable ? invitation.Email : null,
            EffectiveStatus(invitation, now).ToString(),
            isAlreadyMember,
            invitation.House?.ColorKey ?? HouseColors.Default,
            deviceTypes
        );
    }

    // C1: Fix race condition with proper transaction
    public async Task<AcceptInvitationResponseDto> AcceptInvitationAsync(string token, Guid userId)
    {
        // Serializable keeps the invitation single-use: two different users can race through
        // this read-then-write flow, and the HouseMembers unique index (keyed per accepting
        // user) would not stop both from succeeding.
        // The execution strategy re-runs this whole delegate on a transient failure, which
        // includes Postgres 40001 serialization failures under concurrent writes.
        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // A retried attempt must not see entities the rolled-back attempt left tracked
            // (invitation already marked Accepted, pending HouseMember): the re-query would
            // return those stale instances instead of the reverted database rows.
            _context.ChangeTracker.Clear();

            await using var transaction = await _context.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable);

            var invitation = await _context.Invitations
                .Include(i => i.House)
                .FirstOrDefaultAsync(i => i.Token == token);

            if (invitation == null)
                throw new KeyNotFoundException("Invitation not found");

            // Idempotent for the accepting user: a retry after a commit whose acknowledgement
            // was lost, or a double submit, finds the invitation already accepted by them.
            if (invitation.Status == InvitationStatus.Accepted && invitation.AcceptedByUserId == userId)
                return ToAcceptResponse(invitation);

            if (invitation.Status != InvitationStatus.Pending)
                throw new BusinessRuleException(ErrorCodes.InvitationInvalid, "This invitation is no longer valid");

            if (invitation.ExpiresAt <= DateTime.UtcNow)
            {
                invitation.Status = InvitationStatus.Expired;
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                throw new BusinessRuleException(ErrorCodes.InvitationInvalid, "This invitation has expired");
            }

            // M1: Prevent self-accept
            if (invitation.CreatedByUserId == userId)
                throw new BusinessRuleException(ErrorCodes.OwnInvitation, "You cannot accept your own invitation");

            // Check if user is already a member
            var existingMember = await _context.HouseMembers
                .AnyAsync(m => m.HouseId == invitation.HouseId && m.UserId == userId)
                || await _context.Houses.AnyAsync(h => h.Id == invitation.HouseId && h.UserId == userId);

            if (existingMember)
                throw new BusinessRuleException(ErrorCodes.AlreadyMember, "You are already a member of this house");

            await EnsureInviteeAsync(invitation, userId);

            _context.HouseMembers.Add(NewMembership(invitation, userId));
            MarkAccepted(invitation, userId);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return ToAcceptResponse(invitation);
        });
    }

    public async Task<bool> DeclineInvitationAsync(string token, Guid userId)
    {
        var invitation = await _context.Invitations.FirstOrDefaultAsync(i => i.Token == token);
        if (invitation == null) return false;

        if (!IsUsable(invitation, DateTime.UtcNow))
            throw new BusinessRuleException(ErrorCodes.InvitationInvalid, "This invitation is no longer valid");

        if (invitation.CreatedByUserId == userId)
            throw new BusinessRuleException(ErrorCodes.OwnInvitation, "You cannot decline your own invitation");

        await EnsureInviteeAsync(invitation, userId);

        invitation.Status = InvitationStatus.Declined;
        invitation.DeclinedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<InvitationDto?> ResendInvitationAsync(Guid invitationId, Guid userId)
    {
        var invitation = await _context.Invitations
            .Include(i => i.House)
            .Include(i => i.CreatedByUser)
            .FirstOrDefaultAsync(i => i.Id == invitationId);
        if (invitation == null) return null;

        EnsureCanHandleInvitation(
            await EnsureAccessAsync(invitation.HouseId, userId, HousePermissions.Inviters), invitation.Role);

        if (invitation.Status is not (InvitationStatus.Pending or InvitationStatus.Expired))
            throw new BusinessRuleException(ErrorCodes.InvitationInvalid, "Only pending invitations can be re-sent");

        // Re-activating an expired invitation must neither bypass the per-house limit (H3) nor
        // create a second usable invitation for the same email.
        var now = DateTime.UtcNow;
        var otherUsable = _context.Invitations.Where(i => i.HouseId == invitation.HouseId
            && i.Id != invitation.Id
            && i.Status == InvitationStatus.Pending
            && i.ExpiresAt > now);

        if (invitation.Email != null)
        {
            var lowerEmail = invitation.Email.ToLowerInvariant();
            if (await otherUsable.AnyAsync(i => i.Email != null && i.Email.ToLower() == lowerEmail))
                throw new ConflictException(ErrorCodes.InvitationAlreadyPending, "An invitation is already pending for this email");
        }

        if (await otherUsable.CountAsync() >= MaxPendingInvitationsPerHouse)
            throw new BusinessRuleException(ErrorCodes.InvitationLimitReached, $"Maximum of {MaxPendingInvitationsPerHouse} pending invitations per house reached");

        // A new token invalidates the previously shared link; the expiry restarts.
        invitation.Token = NewInvitationToken();
        invitation.Status = InvitationStatus.Pending;
        invitation.ExpiresAt = now.Add(InvitationLifetime);
        await _context.SaveChangesAsync();

        return ToInvitationDto(invitation, invitation.House?.Name ?? "");
    }

    public async Task<bool> RevokeInvitationAsync(Guid invitationId, Guid userId)
    {
        var invitation = await _context.Invitations.FindAsync(invitationId);
        if (invitation == null) return false;

        // R5: the owner cancels any invitation, a RW collaborator a tenant invitation only.
        EnsureCanHandleInvitation(
            await EnsureAccessAsync(invitation.HouseId, userId, HousePermissions.Inviters), invitation.Role);

        if (invitation.Status is not (InvitationStatus.Pending or InvitationStatus.Expired))
            throw new BusinessRuleException(ErrorCodes.InvitationInvalid, "Only pending invitations can be revoked");

        invitation.Status = InvitationStatus.Revoked;
        invitation.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>Membership created when an invitation is accepted (here or at registration).</summary>
    internal static HouseMember NewMembership(Invitation invitation, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        HouseId = invitation.HouseId,
        Role = invitation.Role,
        CanLogMaintenance = true,
        CreatedAt = DateTime.UtcNow
    };

    internal static void MarkAccepted(Invitation invitation, Guid userId)
    {
        invitation.Status = InvitationStatus.Accepted;
        invitation.AcceptedByUserId = userId;
        invitation.AcceptedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// An invitation is addressed to one email: only the account holding that email may accept or
    /// decline it. Invitations predating the email field carry none and are not checked.
    /// </summary>
    private async Task EnsureInviteeAsync(Invitation invitation, Guid userId)
    {
        if (invitation.Email == null) return;

        var accountEmail = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();

        if (!IsInvitee(invitation, accountEmail))
            throw new BusinessRuleException(ErrorCodes.InvitationEmailMismatch,
                "This invitation was sent to another email address");
    }

    /// <summary>
    /// True when <paramref name="email"/> is the invitation's email (trimmed, case-insensitive), or
    /// when the invitation predates the email field. Shared by accept, decline and registration.
    /// </summary>
    internal static bool IsInvitee(Invitation invitation, string? email) =>
        invitation.Email == null
        || (email != null && string.Equals(invitation.Email.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Pending and not past its expiry.</summary>
    internal static bool IsUsable(Invitation invitation, DateTime now) =>
        invitation.Status == InvitationStatus.Pending && invitation.ExpiresAt > now;

    /// <summary>Status as the user sees it: a pending invitation past its expiry is expired, even before the nightly job flags it.</summary>
    private static InvitationStatus EffectiveStatus(Invitation invitation, DateTime now) =>
        invitation.Status == InvitationStatus.Pending && invitation.ExpiresAt <= now
            ? InvitationStatus.Expired
            : invitation.Status;

    // C2: cryptographically secure random token instead of a GUID.
    private static string NewInvitationToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static AcceptInvitationResponseDto ToAcceptResponse(Invitation invitation) =>
        new(invitation.HouseId, invitation.House?.Name ?? "", invitation.Role.ToString());

    // --- Access checks ---

    public async Task<HouseRole?> GetUserRoleAsync(Guid houseId, Guid userId)
    {
        // Check direct ownership first (backward compat with House.UserId)
        var isOwner = await _context.Houses.AnyAsync(h => h.Id == houseId && h.UserId == userId);
        if (isOwner) return HouseRole.Owner;

        // Check HouseMember table
        var member = await _context.HouseMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.HouseId == houseId && m.UserId == userId);

        return member?.Role;
    }

    /// <summary>403 unless <paramref name="callerRole"/> may handle an invitation offering <paramref name="invitedRole"/> (R5).</summary>
    private static void EnsureCanHandleInvitation(HouseRole callerRole, HouseRole invitedRole)
    {
        if (!HousePermissions.CanHandleInvitation(callerRole, invitedRole))
            throw new UnauthorizedAccessException("Only the owner can handle invitations for this role");
    }

    public async Task<HouseRole> EnsureAccessAsync(Guid houseId, Guid userId, params HouseRole[] allowedRoles)
    {
        var role = await GetUserRoleAsync(houseId, userId);
        if (role == null)
        {
            // Contract: unknown id → 404, existing house without access → 403. The existence check
            // only runs on the refusal path, so the nominal case costs no extra query.
            if (!await _context.Houses.AnyAsync(h => h.Id == houseId))
                throw new KeyNotFoundException("House not found");
            throw new UnauthorizedAccessException("Access denied to this house");
        }
        if (!allowedRoles.Contains(role.Value))
            throw new UnauthorizedAccessException("Access denied to this house");
        return role.Value;
    }

    public async Task<bool> CanLogMaintenanceAsync(Guid houseId, Guid userId)
    {
        var role = await GetUserRoleAsync(houseId, userId);
        if (role == null) return false;

        return role.Value switch
        {
            HouseRole.Owner or HouseRole.CollaboratorRW => true,
            HouseRole.Tenant => await _context.HouseMembers
                .AnyAsync(m => m.HouseId == houseId && m.UserId == userId && m.CanLogMaintenance),
            _ => false
        };
    }

    public async Task<bool> ShouldHideCostsAsync(Guid houseId, Guid userId)
    {
        var role = await GetUserRoleAsync(houseId, userId);
        if (role == null) return true;

        // Owner and collaborators always see costs
        if (role.Value is HouseRole.Owner or HouseRole.CollaboratorRW or HouseRole.CollaboratorRO)
            return false;

        // Tenant: check canViewCosts permission
        var member = await _context.HouseMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.HouseId == houseId && m.UserId == userId);

        return member == null || !member.CanViewCosts;
    }

    public async Task<HouseAccessInfo> GetAccessInfoAsync(Guid houseId, Guid userId)
    {
        var row = await _context.Houses
            .AsNoTracking()
            .Where(h => h.Id == houseId)
            .Select(h => new
            {
                IsOwner = h.UserId == userId,
                MemberRole = h.Members.Where(m => m.UserId == userId).Select(m => (HouseRole?)m.Role).FirstOrDefault(),
                MemberCanViewCosts = h.Members.Where(m => m.UserId == userId).Select(m => (bool?)m.CanViewCosts).FirstOrDefault(),
                MemberCanLogMaintenance = h.Members.Where(m => m.UserId == userId).Select(m => (bool?)m.CanLogMaintenance).FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (row == null) return new HouseAccessInfo(null, false);
        if (row.IsOwner) return new HouseAccessInfo(HouseRole.Owner, true, true);
        if (row.MemberRole == null) return new HouseAccessInfo(null, false);

        return new HouseAccessInfo(row.MemberRole, row.MemberCanViewCosts ?? false, row.MemberCanLogMaintenance ?? false);
    }

    public void EnsureAccess(HouseAccessInfo access, params HouseRole[] allowedRoles)
    {
        if (access.Role == null || !allowedRoles.Contains(access.Role.Value))
            throw new UnauthorizedAccessException("Access denied to this house");
    }

    public bool ShouldHideCosts(HouseAccessInfo access) => !HousePermissions.CanViewCosts(access);

    public IQueryable<HouseWithRoleRow> ProjectHousesWithRole(IQueryable<House> houses, Guid userId)
    {
        return houses.Select(h => new HouseWithRoleRow(
            h.Id,
            h.Name,
            h.Address,
            h.ZipCode,
            h.City,
            h.CreatedAt,
            h.Devices.Count,
            h.UserId == userId
                ? HouseRole.Owner
                : h.Members.Where(m => m.UserId == userId).Select(m => (HouseRole?)m.Role).FirstOrDefault()
        ));
    }

    // --- Helpers ---

    private static HouseMemberDto ToDto(HouseMember m) => new(
        m.Id,
        m.UserId,
        m.User?.FirstName ?? "",
        m.User?.LastName ?? "",
        m.User?.Email ?? "",
        m.Role.ToString(),
        m.CanLogMaintenance,
        m.CanViewCosts,
        m.CreatedAt
    );

    private static InvitationDto ToInvitationDto(Invitation i, string houseName, User? creator = null)
    {
        var createdByUser = creator ?? i.CreatedByUser;
        var now = DateTime.UtcNow;
        var status = EffectiveStatus(i, now);
        return new InvitationDto(
            i.Id,
            i.Token,
            i.Role.ToString(),
            status.ToString(),
            i.HouseId,
            houseName,
            createdByUser != null ? $"{createdByUser.FirstName} {createdByUser.LastName}".Trim() : "",
            i.ExpiresAt,
            i.CreatedAt,
            i.Email,
            status == InvitationStatus.Expired
        );
    }

    /// <summary>
    /// Inviter's first and last name, shown to the invitee on P04 (« Marie Dubois vous invite »).
    /// Only name fields are exposed on this unauthenticated endpoint — never the inviter's email.
    /// </summary>
    private static string FormatInviterName(User? user) =>
        user == null ? "" : $"{user.FirstName} {user.LastName}".Trim();
}
