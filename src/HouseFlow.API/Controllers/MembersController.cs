using System.Security.Claims;
using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HouseFlow.API.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
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
        try
        {
            var userId = GetUserId();
            var members = await _memberService.GetHouseMembersAsync(houseId, userId);
            return Ok(members);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Update a member's role
    /// </summary>
    [HttpPut("api/v1/members/{memberId}/role")]
    [ProducesResponseType(typeof(HouseMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMemberRole(Guid memberId, [FromBody] UpdateMemberRoleRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            if (!Enum.TryParse<HouseRole>(request.Role, true, out var role))
                return BadRequest(new { error = "Invalid role. Must be CollaboratorRW, CollaboratorRO, or Tenant" });

            var result = await _memberService.UpdateMemberRoleAsync(memberId, role, userId);
            return result == null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update tenant permissions (canLogMaintenance)
    /// </summary>
    [HttpPut("api/v1/members/{memberId}/permissions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMemberPermissions(Guid memberId, [FromBody] UpdateMemberPermissionsRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            var result = await _memberService.UpdateMemberPermissionsAsync(memberId, request.CanLogMaintenance, userId);
            return result ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Remove a member from a house
    /// </summary>
    [HttpDelete("api/v1/members/{memberId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(Guid memberId)
    {
        try
        {
            var userId = GetUserId();
            var result = await _memberService.RemoveMemberAsync(memberId, userId);
            return result ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

[ApiController]
[Produces("application/json")]
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

    /// <summary>
    /// Create an invitation for a house
    /// </summary>
    [Authorize]
    [HttpPost("api/v1/houses/{houseId}/invitations")]
    [ProducesResponseType(typeof(InvitationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateInvitation(Guid houseId, [FromBody] CreateInvitationRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            if (!Enum.TryParse<HouseRole>(request.Role, true, out var role))
                return BadRequest(new { error = "Invalid role. Must be CollaboratorRW, CollaboratorRO, or Tenant" });

            var invitation = await _memberService.CreateInvitationAsync(houseId, role, userId);
            return CreatedAtAction(nameof(GetInvitationInfo), new { token = invitation.Token }, invitation);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get pending invitations for a house
    /// </summary>
    [Authorize]
    [HttpGet("api/v1/houses/{houseId}/invitations")]
    [ProducesResponseType(typeof(IEnumerable<InvitationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHouseInvitations(Guid houseId)
    {
        try
        {
            var userId = GetUserId();
            var invitations = await _memberService.GetHouseInvitationsAsync(houseId, userId);
            return Ok(invitations);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Get invitation info by token (public - no auth required)
    /// </summary>
    [HttpGet("api/v1/invitations/{token}")]
    [ProducesResponseType(typeof(InvitationInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetInvitationInfo(string token)
    {
        var info = await _memberService.GetInvitationInfoAsync(token);
        return info == null ? NotFound() : Ok(info);
    }

    /// <summary>
    /// Accept an invitation
    /// </summary>
    [Authorize]
    [HttpPost("api/v1/invitations/{token}/accept")]
    [ProducesResponseType(typeof(AcceptInvitationResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AcceptInvitation(string token)
    {
        try
        {
            var userId = GetUserId();
            var result = await _memberService.AcceptInvitationAsync(token, userId);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Invitation not found" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Revoke an invitation
    /// </summary>
    [Authorize]
    [HttpDelete("api/v1/invitations/{invitationId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeInvitation(Guid invitationId)
    {
        try
        {
            var userId = GetUserId();
            var result = await _memberService.RevokeInvitationAsync(invitationId, userId);
            return result ? NoContent() : NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
