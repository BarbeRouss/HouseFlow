using HouseFlow.Application.DTOs;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Enums;

namespace HouseFlow.Application.Common;

/// <summary>
/// Rule R5 — who may do what on a house. Single source used by the services (enforcement) and by the
/// detail DTOs' <see cref="CapabilitiesDto"/> (so the UI hides what the API would refuse).
/// <code>
/// Action                                            Owner  RW  RO  Tenant
/// View                                                ✓    ✓   ✓    ✓
/// Log / edit a maintenance record                     ✓    ✓   —    ✓ (unless CanLogMaintenance was turned off)
/// Add / edit a device or maintenance type             ✓    ✓   —    —
/// Delete a device, maintenance type or record         ✓    ✓   —    —
/// Edit / delete the house                             ✓    —   —    —
/// Invite a tenant; re-send / cancel tenant invitations ✓    ✓   —    —
/// Invite RW / RO; change roles; remove members        ✓    —   —    —
/// </code>
/// </summary>
public static class HousePermissions
{
    public static readonly HouseRole[] Viewers =
        { HouseRole.Owner, HouseRole.CollaboratorRW, HouseRole.CollaboratorRO, HouseRole.Tenant };

    /// <summary>Add / edit / delete devices and maintenance types; delete maintenance records.</summary>
    public static readonly HouseRole[] Editors = { HouseRole.Owner, HouseRole.CollaboratorRW };

    /// <summary>Edit / delete the house, manage members (roles, removal) and every invitation.</summary>
    public static readonly HouseRole[] Owners = { HouseRole.Owner };

    /// <summary>May invite someone: the owner (any role) or a RW collaborator (a tenant only).</summary>
    public static readonly HouseRole[] Inviters = { HouseRole.Owner, HouseRole.CollaboratorRW };

    /// <summary>
    /// Whether <paramref name="callerRole"/> may create, see, re-send or cancel an invitation offering
    /// <paramref name="invitedRole"/>: the owner any of them, a RW collaborator tenant invitations only.
    /// </summary>
    public static bool CanHandleInvitation(HouseRole callerRole, HouseRole invitedRole) => callerRole switch
    {
        HouseRole.Owner => true,
        HouseRole.CollaboratorRW => invitedRole == HouseRole.Tenant,
        _ => false
    };

    public static bool CanLogMaintenance(HouseAccessInfo access) => access.Role switch
    {
        HouseRole.Owner or HouseRole.CollaboratorRW => true,
        HouseRole.Tenant => access.CanLogMaintenance,
        _ => false
    };

    public static bool CanViewCosts(HouseAccessInfo access) => access.Role switch
    {
        HouseRole.Owner or HouseRole.CollaboratorRW or HouseRole.CollaboratorRO => true,
        HouseRole.Tenant => access.CanViewCosts,
        _ => false
    };

    public static CapabilitiesDto Capabilities(HouseAccessInfo access)
    {
        var isEditor = access.Role is { } role && Editors.Contains(role);
        var isOwner = access.Role == HouseRole.Owner;
        return new CapabilitiesDto(
            CanLogMaintenance: CanLogMaintenance(access),
            CanEditDevices: isEditor,
            CanDelete: isEditor,
            CanManageHouse: isOwner,
            CanManageMembers: isOwner,
            CanInviteTenants: isEditor,
            CanViewCosts: CanViewCosts(access));
    }
}
