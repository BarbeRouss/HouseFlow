using System.ComponentModel.DataAnnotations;

namespace HouseFlow.Application.DTOs;

public record HouseMemberDto(
    Guid Id,
    Guid UserId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    bool CanLogMaintenance,
    DateTime CreatedAt
);

public record UpdateMemberRoleRequestDto(
    [Required(ErrorMessage = "Role is required")]
    string Role
);

public record UpdateMemberPermissionsRequestDto(
    [Required]
    bool CanLogMaintenance
);

public record InvitationDto(
    Guid Id,
    string Token,
    string Role,
    string Status,
    Guid HouseId,
    string HouseName,
    string CreatedByName,
    DateTime ExpiresAt,
    DateTime CreatedAt
);

public record CreateInvitationRequestDto(
    [Required(ErrorMessage = "Role is required")]
    string Role
);

public record InvitationInfoDto(
    Guid Id,
    string HouseName,
    string Role,
    string InvitedByName,
    DateTime ExpiresAt,
    bool IsExpired
);

public record AcceptInvitationResponseDto(
    Guid HouseId,
    string HouseName,
    string Role
);

public record HouseCollaboratorsDto(
    Guid HouseId,
    string HouseName,
    IEnumerable<HouseMemberDto> Members,
    IEnumerable<InvitationDto> PendingInvitations
);

public record AllCollaboratorsResponseDto(
    IEnumerable<HouseCollaboratorsDto> Houses
);
