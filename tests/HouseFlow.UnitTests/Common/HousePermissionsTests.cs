using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Application.Interfaces;
using HouseFlow.Core.Enums;

namespace HouseFlow.UnitTests.Common;

/// <summary>Rule R5 — who may handle which invitation, and the matching capabilities.</summary>
public class HousePermissionsTests
{
    [Theory]
    [InlineData(HouseRole.Owner, HouseRole.CollaboratorRW, true)]
    [InlineData(HouseRole.Owner, HouseRole.CollaboratorRO, true)]
    [InlineData(HouseRole.Owner, HouseRole.Tenant, true)]
    [InlineData(HouseRole.CollaboratorRW, HouseRole.Tenant, true)]
    [InlineData(HouseRole.CollaboratorRW, HouseRole.CollaboratorRW, false)]
    [InlineData(HouseRole.CollaboratorRW, HouseRole.CollaboratorRO, false)]
    [InlineData(HouseRole.CollaboratorRO, HouseRole.Tenant, false)]
    [InlineData(HouseRole.Tenant, HouseRole.Tenant, false)]
    public void CanHandleInvitation_OwnerAnyRole_RWTenantOnly(HouseRole caller, HouseRole invited, bool expected)
    {
        HousePermissions.CanHandleInvitation(caller, invited).Should().Be(expected);
    }

    [Theory]
    [InlineData(HouseRole.Owner, true, true)]
    [InlineData(HouseRole.CollaboratorRW, false, true)]
    [InlineData(HouseRole.CollaboratorRO, false, false)]
    [InlineData(HouseRole.Tenant, false, false)]
    public void Capabilities_MembersOwnerOnly_TenantInvitesOwnerAndRW(HouseRole role, bool manageMembers, bool inviteTenants)
    {
        var caps = HousePermissions.Capabilities(new HouseAccessInfo(role, true, true));

        caps.CanManageMembers.Should().Be(manageMembers);
        caps.CanInviteTenants.Should().Be(inviteTenants);
    }
}
