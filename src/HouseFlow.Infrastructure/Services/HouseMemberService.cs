using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;
using HouseFlow.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HouseFlow.Infrastructure.Services;

public class HouseMemberService : IHouseMemberService
{
    private readonly HouseFlowDbContext _context;

    public HouseMemberService(HouseFlowDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<HouseMemberDto>> GetHouseMembersAsync(Guid houseId, Guid userId)
    {
        await EnsureAccessAsync(houseId, userId,
            HouseRole.Owner, HouseRole.CollaboratorRW, HouseRole.CollaboratorRO, HouseRole.Tenant);

        var members = await _context.HouseMembers
            .AsNoTracking()
            .Where(m => m.HouseId == houseId)
            .Include(m => m.User)
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

    public async Task<bool> UpdateMemberPermissionsAsync(Guid memberId, bool canLogMaintenance, Guid userId)
    {
        var member = await _context.HouseMembers.FindAsync(memberId);
        if (member == null) return false;

        // Only owner can change permissions
        await EnsureAccessAsync(member.HouseId, userId, HouseRole.Owner);

        // Only relevant for tenants
        if (member.Role != HouseRole.Tenant)
            throw new InvalidOperationException("canLogMaintenance is only configurable for tenants");

        member.CanLogMaintenance = canLogMaintenance;
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

    public async Task<InvitationDto> CreateInvitationAsync(Guid houseId, HouseRole role, Guid userId)
    {
        // Validate role
        if (role == HouseRole.Owner)
            throw new InvalidOperationException("Cannot create invitation for owner role");

        var callerRole = await GetUserRoleAsync(houseId, userId)
            ?? throw new UnauthorizedAccessException("Access denied to this house");

        // Owner can invite any role; CollaboratorRW can only invite Tenant
        if (callerRole == HouseRole.Owner)
        {
            // Owner can invite anyone
        }
        else if (callerRole == HouseRole.CollaboratorRW && role == HouseRole.Tenant)
        {
            // CollaboratorRW can invite tenants
        }
        else
        {
            throw new UnauthorizedAccessException("You don't have permission to create this invitation");
        }

        var house = await _context.Houses.FindAsync(houseId)
            ?? throw new KeyNotFoundException("House not found");

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            Token = Guid.NewGuid().ToString("N"),
            Role = role,
            Status = InvitationStatus.Pending,
            HouseId = houseId,
            CreatedByUserId = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };

        _context.Invitations.Add(invitation);
        await _context.SaveChangesAsync();

        // Load creator name
        var creator = await _context.Users.FindAsync(userId);

        return ToInvitationDto(invitation, house.Name, creator);
    }

    public async Task<IEnumerable<InvitationDto>> GetHouseInvitationsAsync(Guid houseId, Guid userId)
    {
        await EnsureAccessAsync(houseId, userId, HouseRole.Owner, HouseRole.CollaboratorRW);

        var house = await _context.Houses.FindAsync(houseId)
            ?? throw new KeyNotFoundException("House not found");

        var invitations = await _context.Invitations
            .AsNoTracking()
            .Where(i => i.HouseId == houseId)
            .Include(i => i.CreatedByUser)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        return invitations.Select(i => ToInvitationDto(i, house.Name));
    }

    public async Task<InvitationInfoDto?> GetInvitationInfoAsync(string token)
    {
        var invitation = await _context.Invitations
            .AsNoTracking()
            .Include(i => i.House)
            .Include(i => i.CreatedByUser)
            .FirstOrDefaultAsync(i => i.Token == token);

        if (invitation == null) return null;

        return new InvitationInfoDto(
            invitation.Id,
            invitation.House?.Name ?? "",
            invitation.Role.ToString(),
            $"{invitation.CreatedByUser?.FirstName} {invitation.CreatedByUser?.LastName}".Trim(),
            invitation.ExpiresAt,
            invitation.Status != InvitationStatus.Pending || invitation.ExpiresAt <= DateTime.UtcNow
        );
    }

    public async Task<AcceptInvitationResponseDto> AcceptInvitationAsync(string token, Guid userId)
    {
        var invitation = await _context.Invitations
            .Include(i => i.House)
            .FirstOrDefaultAsync(i => i.Token == token);

        if (invitation == null)
            throw new KeyNotFoundException("Invitation not found");

        if (invitation.Status != InvitationStatus.Pending)
            throw new InvalidOperationException("This invitation is no longer valid");

        if (invitation.ExpiresAt <= DateTime.UtcNow)
        {
            invitation.Status = InvitationStatus.Expired;
            await _context.SaveChangesAsync();
            throw new InvalidOperationException("This invitation has expired");
        }

        // Check if user is already a member
        var existingMember = await _context.HouseMembers
            .AnyAsync(m => m.HouseId == invitation.HouseId && m.UserId == userId);

        if (existingMember)
            throw new InvalidOperationException("You are already a member of this house");

        // Create membership
        var member = new HouseMember
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            HouseId = invitation.HouseId,
            Role = invitation.Role,
            CanLogMaintenance = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.HouseMembers.Add(member);

        // Mark invitation as accepted
        invitation.Status = InvitationStatus.Accepted;
        invitation.AcceptedByUserId = userId;
        invitation.AcceptedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new AcceptInvitationResponseDto(
            invitation.HouseId,
            invitation.House?.Name ?? "",
            invitation.Role.ToString()
        );
    }

    public async Task<bool> RevokeInvitationAsync(Guid invitationId, Guid userId)
    {
        var invitation = await _context.Invitations.FindAsync(invitationId);
        if (invitation == null) return false;

        if (invitation.Status != InvitationStatus.Pending)
            throw new InvalidOperationException("Only pending invitations can be revoked");

        // Creator or house owner can revoke
        var isCreator = invitation.CreatedByUserId == userId;
        var isOwner = await _context.HouseMembers
            .AnyAsync(m => m.HouseId == invitation.HouseId && m.UserId == userId && m.Role == HouseRole.Owner);

        // Also check direct ownership (for backward compatibility)
        if (!isOwner)
            isOwner = await _context.Houses.AnyAsync(h => h.Id == invitation.HouseId && h.UserId == userId);

        if (!isCreator && !isOwner)
            throw new UnauthorizedAccessException("Only the invitation creator or house owner can revoke");

        invitation.Status = InvitationStatus.Revoked;
        invitation.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

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

    public async Task EnsureAccessAsync(Guid houseId, Guid userId, params HouseRole[] allowedRoles)
    {
        var role = await GetUserRoleAsync(houseId, userId);
        if (role == null || !allowedRoles.Contains(role.Value))
            throw new UnauthorizedAccessException("Access denied to this house");
    }

    public async Task<bool> HasAccessAsync(Guid houseId, Guid userId)
    {
        var role = await GetUserRoleAsync(houseId, userId);
        return role != null;
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

    // --- Helpers ---

    private static HouseMemberDto ToDto(HouseMember m) => new(
        m.Id,
        m.UserId,
        m.User?.FirstName ?? "",
        m.User?.LastName ?? "",
        m.User?.Email ?? "",
        m.Role.ToString(),
        m.CanLogMaintenance,
        m.CreatedAt
    );

    private static InvitationDto ToInvitationDto(Invitation i, string houseName, User? creator = null)
    {
        var createdByUser = creator ?? i.CreatedByUser;
        return new InvitationDto(
            i.Id,
            i.Token,
            i.Role.ToString(),
            i.Status.ToString(),
            i.HouseId,
            houseName,
            createdByUser != null ? $"{createdByUser.FirstName} {createdByUser.LastName}".Trim() : "",
            i.ExpiresAt,
            i.CreatedAt
        );
    }
}
