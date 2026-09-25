using HouseFlow.Application.DTOs;
using HouseFlow.Core.Entities;
using HouseFlow.Core.Enums;

namespace HouseFlow.Application.Interfaces;


/// <summary>One row of <see cref="IHouseMemberService.ProjectHousesWithRole"/>: a house's own scalar fields,
/// its device count, and the calling user's role on it (null if the user has no access — callers should not
/// normally see such rows, since they compose this on top of an already access-filtered house query).</summary>
public sealed record HouseWithRoleRow(
    Guid Id, string Name, string? Address, string? ZipCode, string? City, DateTime CreatedAt,
    int DeviceCount, HouseRole? Role);

/// <summary>A user's resolved access to a house: role (null = no access) and whether they are allowed to
/// see cost data. Fetched in a single query by <see cref="IHouseMemberService.GetAccessInfoAsync"/> instead
/// of the role and the cost-visibility flag each running their own round trip.</summary>
public sealed record HouseAccessInfo(HouseRole? Role, bool CanViewCosts);

public interface IHouseMemberService
{
    // Member management
    Task<IEnumerable<HouseMemberDto>> GetHouseMembersAsync(Guid houseId, Guid userId);
    Task<HouseMemberDto?> UpdateMemberRoleAsync(Guid memberId, HouseRole newRole, Guid userId);
    Task<bool> UpdateMemberPermissionsAsync(Guid memberId, bool? canLogMaintenance, bool? canViewCosts, Guid userId);
    Task<bool> RemoveMemberAsync(Guid memberId, Guid userId);

    // Collaborator overview (all houses for an owner)
    Task<AllCollaboratorsResponseDto> GetAllCollaboratorsAsync(Guid userId);

    // Invitations
    Task<InvitationDto> CreateInvitationAsync(Guid houseId, HouseRole role, Guid userId);
    Task<IEnumerable<InvitationDto>> GetHouseInvitationsAsync(Guid houseId, Guid userId);
    Task<InvitationInfoDto?> GetInvitationInfoAsync(string token);
    Task<AcceptInvitationResponseDto> AcceptInvitationAsync(string token, Guid userId);
    Task<bool> RevokeInvitationAsync(Guid invitationId, Guid userId);

    // Access checks
    Task<HouseRole?> GetUserRoleAsync(Guid houseId, Guid userId);
    Task EnsureAccessAsync(Guid houseId, Guid userId, params HouseRole[] allowedRoles);
    Task<bool> CanLogMaintenanceAsync(Guid houseId, Guid userId);
    Task<bool> ShouldHideCostsAsync(Guid houseId, Guid userId);

    /// <summary>
    /// Single-query equivalent of calling <see cref="GetUserRoleAsync"/> then <see cref="ShouldHideCostsAsync"/>:
    /// both need the same role/membership row, so callers that need both should fetch it once here instead.
    /// </summary>
    Task<HouseAccessInfo> GetAccessInfoAsync(Guid houseId, Guid userId);

    /// <summary>Pure check on an already-fetched <see cref="HouseAccessInfo"/> — throws the same
    /// <see cref="UnauthorizedAccessException"/> as <see cref="EnsureAccessAsync"/> without a DB round trip.</summary>
    void EnsureAccess(HouseAccessInfo access, params HouseRole[] allowedRoles);

    /// <summary>Pure derivation of <see cref="ShouldHideCostsAsync"/> from an already-fetched
    /// <see cref="HouseAccessInfo"/>, without a DB round trip.</summary>
    bool ShouldHideCosts(HouseAccessInfo access);

    /// <summary>
    /// Projects each house of <paramref name="houses"/> to its scalar fields plus the caller's role and device
    /// count, resolved by a correlated subquery per house (not a per-house round trip). Callers that need to
    /// flatten further (e.g. onto that house's devices) should correlate on <see cref="HouseWithRoleRow.Id"/> —
    /// a plain scalar column — rather than re-querying <see cref="House"/>: EF Core cannot reliably translate a
    /// further subquery correlated through a nested entity reference sitting inside an already-projected shape.
    /// So read endpoints that list several houses can resolve role without an N+1 loop, with the role-resolution
    /// rule (ownership first, then membership) defined once here instead of re-derived by every projection that
    /// needs it.
    /// </summary>
    IQueryable<HouseWithRoleRow> ProjectHousesWithRole(IQueryable<House> houses, Guid userId);
}
