using HouseFlow.Application.DTOs;
using HouseFlow.Core.Enums;

namespace HouseFlow.Application.Interfaces;

public interface IHouseMemberService
{
    // Member management
    Task<IEnumerable<HouseMemberDto>> GetHouseMembersAsync(Guid houseId, Guid userId);
    Task<HouseMemberDto?> UpdateMemberRoleAsync(Guid memberId, HouseRole newRole, Guid userId);
    Task<bool> UpdateMemberPermissionsAsync(Guid memberId, bool canLogMaintenance, Guid userId);
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
    Task<bool> HasAccessAsync(Guid houseId, Guid userId);
    Task<bool> CanLogMaintenanceAsync(Guid houseId, Guid userId);
}
