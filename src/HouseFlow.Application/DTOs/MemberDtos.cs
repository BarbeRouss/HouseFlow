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
    bool CanViewCosts,
    DateTime CreatedAt // date the member joined the house
);

public record UpdateMemberRoleRequestDto(
    [Required(ErrorMessage = "Role is required")]
    string Role
);

public record UpdateMemberPermissionsRequestDto(
    bool? CanLogMaintenance,
    bool? CanViewCosts
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
    DateTime CreatedAt,
    string? Email = null,
    bool IsExpired = false
);

public record CreateInvitationRequestDto(
    [Required(ErrorMessage = "Role is required")]
    string Role,

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(255, MinimumLength = 1, ErrorMessage = "Email cannot exceed 255 characters")]
    string Email
);

/// <summary>Public view of an invitation (P04). <see cref="IsAlreadyMember"/> is null for anonymous callers.</summary>
public record InvitationInfoDto(
    Guid Id,
    string HouseName,
    string Role,
    string InvitedByName,
    DateTime ExpiresAt,
    bool IsExpired,
    Guid HouseId = default,
    string? Email = null,
    string Status = "Pending",
    bool? IsAlreadyMember = null,
    string HouseColorKey = HouseFlow.Core.HouseColors.Indigo, // P04 banner
    // P04 banner chips: one Device.Type per device while the invitation is usable, empty otherwise.
    IReadOnlyList<string>? HouseDeviceTypes = null
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
