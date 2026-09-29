using HouseFlow.Web.Api;

namespace HouseFlow.Web.Rules;

/// <summary>Display labels of the house roles (M5 member list, invitations).</summary>
public static class HouseRoleLabels
{
    /// <summary>i18n key of a house role (« Propriétaire », « Collaborateur », « Lecture seule », « Locataire »).</summary>
    public static string Key(string? role) => role switch
    {
        HouseRoles.Owner => "houses.owner",
        HouseRoles.CollaboratorRW => "houses.collaboratorRW",
        HouseRoles.Tenant => "houses.tenant",
        _ => "houses.collaboratorRO",
    };
}
