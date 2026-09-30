using System.Security.Claims;
using HouseFlow.API.Filters;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HouseFlow.API.Controllers;

[ApiController]
[Authorize]
public class MembersController : ControllerBase
{
    private readonly IHouseMemberService _memberService;

    public MembersController(IHouseMemberService memberService)
    {
        _memberService = memberService;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim ?? throw new UnauthorizedAccessException());
    }

    /// <summary>
    /// Get all collaborators across all owned houses
    /// </summary>
    [HttpGet("api/v1/collaborators")]
    [ProducesResponseType(typeof(AllCollaboratorsResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllCollaborators()
    {
        var userId = GetUserId();
        var result = await _memberService.GetAllCollaboratorsAsync(userId);
        return Ok(result);
    }

    /// <summary>
    /// Get members of a house
    /// </summary>
    [HttpGet("api/v1/houses/{houseId}/members")]
    [ProducesResponseType(typeof(IEnumerable<HouseMemberDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHouseMembers(Guid houseId)
    {
        var userId = GetUserId();
        var members = await _memberService.GetHouseMembersAsync(houseId, userId);
        return Ok(members);
    }

    /// <summary>
    /// Update a member's role
    /// </summary>
    [HttpPut("api/v1/members/{memberId}/role")]
    [ProducesResponseType(typeof(HouseMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMemberRole(Guid memberId, [FromBody] UpdateMemberRoleRequestDto request)
    {
        var userId = GetUserId();
        // IsDefined: TryParse accepts any number ("7"), which would store an unknown HouseRole.
        if (!Enum.TryParse<HouseRole>(request.Role, true, out var role) || !Enum.IsDefined(role))
            return ApiProblem.Create(HttpContext, StatusCodes.Status400BadRequest, InvitationsController.InvalidRoleMessage);

        var result = await _memberService.UpdateMemberRoleAsync(memberId, role, userId);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Update tenant permissions (canLogMaintenance)
    /// </summary>
    [HttpPut("api/v1/members/{memberId}/permissions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMemberPermissions(Guid memberId, [FromBody] UpdateMemberPermissionsRequestDto request)
    {
        var userId = GetUserId();
        var result = await _memberService.UpdateMemberPermissionsAsync(memberId, request.CanLogMaintenance, request.CanViewCosts, userId);
        return result ? NoContent() : NotFound();
    }

    /// <summary>
    /// Remove a member from a house
    /// </summary>
    [HttpDelete("api/v1/members/{memberId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(Guid memberId)
    {
        var userId = GetUserId();
        var result = await _memberService.RemoveMemberAsync(memberId, userId);
        return result ? NoContent() : NotFound();
    }
}

[ApiController]
public class InvitationsController : ControllerBase
{
    private readonly IHouseMemberService _memberService;

    public InvitationsController(IHouseMemberService memberService)
    {
        _memberService = memberService;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim ?? throw new UnauthorizedAccessException());
    }

    internal const string InvalidRoleMessage = "Invalid role. Must be CollaboratorRW, CollaboratorRO, or Tenant";

    /// <summary>
    /// Create an invitation for a house (owner: any role; RW collaborator: a tenant only). No email is sent: the inviter shares the link.
    /// </summary>
    [Authorize]
    [HttpPost("api/v1/houses/{houseId}/invitations")]
    [ProducesResponseType(typeof(InvitationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateInvitation(Guid houseId, [FromBody] CreateInvitationRequestDto request)
    {
        var userId = GetUserId();
        if (!Enum.TryParse<HouseRole>(request.Role, true, out var role) || !Enum.IsDefined(role))
            return ApiProblem.Create(HttpContext, StatusCodes.Status400BadRequest, InvalidRoleMessage);

        var invitation = await _memberService.CreateInvitationAsync(houseId, role, request.Email, userId);
        return CreatedAtAction(nameof(GetInvitationInfo), new { token = invitation.Token }, invitation);
    }

    /// <summary>
    /// Invitations not answered yet for a house (owner: all; RW collaborator: tenant invitations)
    /// </summary>
    [Authorize]
    [HttpGet("api/v1/houses/{houseId}/invitations")]
    [ProducesResponseType(typeof(IEnumerable<InvitationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHouseInvitations(Guid houseId)
    {
        var userId = GetUserId();
        var invitations = await _memberService.GetHouseInvitationsAsync(houseId, userId);
        return Ok(invitations);
    }

    /// <summary>
    /// Get invitation info by token (public - no auth required). With a JWT, also tells whether the
    /// caller is already a member of the house.
    /// </summary>
    [HttpGet("api/v1/invitations/{token}")]
    [ProducesResponseType(typeof(InvitationInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvitationInfo(string token)
    {
        var userIdClaim = User.Identity?.IsAuthenticated == true
            ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            : null;
        Guid? userId = Guid.TryParse(userIdClaim, out var parsed) ? parsed : null;

        var info = await _memberService.GetInvitationInfoAsync(token, userId);
        return info == null ? NotFound() : Ok(info);
    }

    /// <summary>
    /// Accept an invitation
    /// </summary>
    [Authorize]
    [HttpPost("api/v1/invitations/{token}/accept")]
    [ProducesResponseType(typeof(AcceptInvitationResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> AcceptInvitation(string token)
    {
        var userId = GetUserId();
        var result = await _memberService.AcceptInvitationAsync(token, userId);
        return Ok(result);
    }

    /// <summary>
    /// Decline an invitation (the invitee, P04 « Refuser »)
    /// </summary>
    [Authorize]
    [HttpPost("api/v1/invitations/{token}/decline")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeclineInvitation(string token)
    {
        var userId = GetUserId();
        var declined = await _memberService.DeclineInvitationAsync(token, userId);
        return declined ? NoContent() : NotFound();
    }

    /// <summary>
    /// Re-send an invitation (owner; RW collaborator for a tenant invitation): new token, expiry reset
    /// </summary>
    [Authorize]
    [HttpPost("api/v1/invitations/{invitationId:guid}/resend")]
    [ProducesResponseType(typeof(InvitationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResendInvitation(Guid invitationId)
    {
        var userId = GetUserId();
        var invitation = await _memberService.ResendInvitationAsync(invitationId, userId);
        return invitation == null ? NotFound() : Ok(invitation);
    }

    /// <summary>
    /// Cancel a pending invitation (owner; RW collaborator for a tenant invitation)
    /// </summary>
    [Authorize]
    [HttpDelete("api/v1/invitations/{invitationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeInvitation(Guid invitationId)
    {
        var userId = GetUserId();
        var result = await _memberService.RevokeInvitationAsync(invitationId, userId);
        return result ? NoContent() : NotFound();
    }
}
