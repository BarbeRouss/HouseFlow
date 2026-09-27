using FluentAssertions;
using HouseFlow.Application.DTOs;
using HouseFlow.Core.Entities;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Collaboration;

/// <summary>
/// Comprehensive RBAC tests: sets up an Owner house with 4 roles (Owner, CollaboratorRW, CollaboratorRO, Tenant)
/// and a device with maintenance, then validates every permission boundary.
/// </summary>
[Collection("Integration")]
public class RbacPermissionTests
{
    private readonly IntegrationTestFixture _fixture;

    public RbacPermissionTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient CreateClient() => _fixture.CreateApiClient();

    private async Task<(HttpClient client, string token)> RegisterUserAsync(string firstName, string lastName, string? email = null)
    {
        var client = CreateClient();
        email ??= $"test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto(email: email, firstName: firstName, lastName: lastName, password: "Password123!", consentAccepted: true);

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        response.EnsureSuccessStatusCode();

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        return (client, authResponse.AccessToken);
    }

    // Registration creates no house any more (onboarding P05 does): the owner creates it.
    private static Task<Guid> GetFirstHouseIdAsync(HttpClient client) => client.CreateHouseAsync();

    private async Task<HttpClient> InviteAndAcceptAsync(HttpClient ownerClient, Guid houseId, string role)
    {
        // Only the account holding the invitation email may accept it.
        var inviteeEmail = NewInviteeEmail();
        var createResponse = await ownerClient.PostAsJsonAsync(
            $"/api/v1/houses/{houseId}/invitations",
            new CreateInvitationRequestDto(role, inviteeEmail));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, $"Owner should be able to create {role} invitation");
        var invitation = await createResponse.Content.ReadAsJsonAsync<InvitationDto>();

        var (memberClient, _) = await RegisterUserAsync($"{role}First", $"{role}Last", inviteeEmail);
        var acceptResponse = await memberClient.PostAsync($"/api/v1/invitations/{invitation!.Token}/accept", null);
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"New user should be able to accept {role} invitation");

        return memberClient;
    }

    /// <summary>
    /// Helper: sets up a full house with Owner + device + maintenance + all 4 roles.
    /// Returns clients for each role and resource IDs.
    /// </summary>
    private async Task<TestHouseContext> SetupFullHouseAsync()
    {
        // 1. Register Owner
        var (ownerClient, _) = await RegisterUserAsync("Owner", "Boss");
        var houseId = await GetFirstHouseIdAsync(ownerClient);

        // 2. Create a device
        var deviceResponse = await ownerClient.PostAsJsonAsync(
            $"/api/v1/houses/{houseId}/devices",
            new CreateDeviceRequestDto(maintenanceType: null, name: "Chaudière Test", type: "Chaudière Gaz", brand: "Viessmann", model: "Vitodens 200", installDate: DateTime.UtcNow.AddYears(-2)));
        deviceResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var device = await deviceResponse.Content.ReadAsJsonAsync<DeviceDto>();

        // 3. Create a maintenance type
        var mtResponse = await ownerClient.PostAsJsonAsync(
            $"/api/v1/devices/{device!.Id}/maintenance-types",
            new CreateMaintenanceTypeRequestDto("Entretien annuel", Periodicity.Annual, null));
        mtResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var maintenanceType = await mtResponse.Content.ReadAsJsonAsync<MaintenanceTypeDto>();

        // 4. Log a maintenance instance (with cost data)
        var logResponse = await ownerClient.PostAsJsonAsync(
            $"/api/v1/maintenance-types/{maintenanceType!.Id}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow.AddMonths(-1), cost: 150.00m, provider: "TechniGaz", notes: "Entretien annuel effectué"));
        logResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var maintenanceInstance = await logResponse.Content.ReadAsJsonAsync<MaintenanceInstanceDto>();

        // 5. Invite all roles
        var collabRWClient = await InviteAndAcceptAsync(ownerClient, houseId, "CollaboratorRW");
        var collabROClient = await InviteAndAcceptAsync(ownerClient, houseId, "CollaboratorRO");
        var tenantClient = await InviteAndAcceptAsync(ownerClient, houseId, "Tenant");

        return new TestHouseContext
        {
            HouseId = houseId,
            DeviceId = device.Id,
            MaintenanceTypeId = maintenanceType.Id,
            MaintenanceInstanceId = maintenanceInstance!.Id,
            OwnerClient = ownerClient,
            CollabRWClient = collabRWClient,
            CollabROClient = collabROClient,
            TenantClient = tenantClient
        };
    }

    private record TestHouseContext
    {
        public Guid HouseId { get; init; }
        public Guid DeviceId { get; init; }
        public Guid MaintenanceTypeId { get; init; }
        public Guid MaintenanceInstanceId { get; init; }
        public required HttpClient OwnerClient { get; init; }
        public required HttpClient CollabRWClient { get; init; }
        public required HttpClient CollabROClient { get; init; }
        public required HttpClient TenantClient { get; init; }
    }

    // ========================================================================
    // HOUSE ACCESS
    // ========================================================================

    #region House Read Access (all roles can read)

    [Fact]
    public async Task House_AllRoles_CanViewHouseDetail()
    {
        var ctx = await SetupFullHouseAsync();

        foreach (var (client, role) in new[] {
            (ctx.OwnerClient, "Owner"),
            (ctx.CollabRWClient, "CollaboratorRW"),
            (ctx.CollabROClient, "CollaboratorRO"),
            (ctx.TenantClient, "Tenant") })
        {
            var response = await client.GetAsync($"/api/v1/houses/{ctx.HouseId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{role} should be able to view house");

            var house = await response.Content.ReadAsJsonAsync<HouseDetailDto>();
            house!.UserRole.Should().Be(role, $"UserRole should be {role}");
        }
    }

    [Fact]
    public async Task House_AllRoles_SeeSharedHouseInList()
    {
        var ctx = await SetupFullHouseAsync();

        foreach (var (client, role) in new[] {
            (ctx.CollabRWClient, "CollaboratorRW"),
            (ctx.CollabROClient, "CollaboratorRO"),
            (ctx.TenantClient, "Tenant") })
        {
            var response = await client.GetAsync("/api/v1/houses");
            var houses = await response.Content.ReadAsJsonAsync<HousesListResponseDto>();
            houses!.Houses.Should().Contain(h => h.Id == ctx.HouseId, $"{role} should see shared house in list");

            var sharedHouse = houses.Houses.First(h => h.Id == ctx.HouseId);
            sharedHouse.UserRole.Should().Be(role);
        }
    }

    #endregion

    #region House Write Access (only Owner and CollaboratorRW)

    [Fact]
    public async Task House_Owner_CanUpdateHouse()
    {
        var ctx = await SetupFullHouseAsync();
        var updateRequest = new UpdateHouseRequestDto(name: "Maison Modifiée", address: null, zipCode: null, city: null);
        var response = await ctx.OwnerClient.PutAsJsonAsync($"/api/v1/houses/{ctx.HouseId}", updateRequest);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task House_CollaboratorRW_CannotUpdateHouse()
    {
        var ctx = await SetupFullHouseAsync();
        var updateRequest = new UpdateHouseRequestDto(name: "Tentative Hack", address: null, zipCode: null, city: null);
        var response = await ctx.CollabRWClient.PutAsJsonAsync($"/api/v1/houses/{ctx.HouseId}", updateRequest);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task House_CollaboratorRO_CannotUpdateHouse()
    {
        var ctx = await SetupFullHouseAsync();
        var updateRequest = new UpdateHouseRequestDto(name: "Tentative Hack", address: null, zipCode: null, city: null);
        var response = await ctx.CollabROClient.PutAsJsonAsync($"/api/v1/houses/{ctx.HouseId}", updateRequest);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task House_Tenant_CannotUpdateHouse()
    {
        var ctx = await SetupFullHouseAsync();
        var updateRequest = new UpdateHouseRequestDto(name: "Tentative Hack", address: null, zipCode: null, city: null);
        var response = await ctx.TenantClient.PutAsJsonAsync($"/api/v1/houses/{ctx.HouseId}", updateRequest);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task House_OnlyOwner_CanDeleteHouse()
    {
        // Use a separate house for this destructive test
        var (ownerClient, _) = await RegisterUserAsync("DelOwner", "Test");
        var createResponse = await ownerClient.PostAsJsonAsync("/api/v1/houses",
            new CreateHouseRequestDto(name: "Maison à Supprimer", address: null, zipCode: null, city: null));
        var house = await createResponse.Content.ReadAsJsonAsync<HouseDto>();

        var collabClient = await InviteAndAcceptAsync(ownerClient, house!.Id, "CollaboratorRW");

        // CollabRW cannot delete
        var deleteResponse = await collabClient.DeleteAsync($"/api/v1/houses/{house.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Owner can delete
        var ownerDeleteResponse = await ownerClient.DeleteAsync($"/api/v1/houses/{house.Id}");
        ownerDeleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    // ========================================================================
    // DEVICE ACCESS
    // ========================================================================

    #region Device Read Access (all roles can read)

    [Fact]
    public async Task Device_AllRoles_CanViewDeviceDetail()
    {
        var ctx = await SetupFullHouseAsync();

        foreach (var (client, role) in new[] {
            (ctx.OwnerClient, "Owner"),
            (ctx.CollabRWClient, "CollaboratorRW"),
            (ctx.CollabROClient, "CollaboratorRO"),
            (ctx.TenantClient, "Tenant") })
        {
            var response = await client.GetAsync($"/api/v1/devices/{ctx.DeviceId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{role} should be able to view device");
        }
    }

    [Fact]
    public async Task Device_AllRoles_CanListHouseDevices()
    {
        var ctx = await SetupFullHouseAsync();

        foreach (var (client, role) in new[] {
            (ctx.OwnerClient, "Owner"),
            (ctx.CollabRWClient, "CollaboratorRW"),
            (ctx.CollabROClient, "CollaboratorRO"),
            (ctx.TenantClient, "Tenant") })
        {
            var response = await client.GetAsync($"/api/v1/houses/{ctx.HouseId}/devices");
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{role} should be able to list devices");
        }
    }

    #endregion

    #region Device Write Access (Owner + CollaboratorRW only)

    [Fact]
    public async Task Device_Owner_CanCreateDevice()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.OwnerClient.PostAsJsonAsync(
            $"/api/v1/houses/{ctx.HouseId}/devices",
            new CreateDeviceRequestDto(maintenanceType: null, name: "Nouveau Device Owner", type: "Alarme", brand: null, model: null, installDate: null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Device_CollaboratorRW_CanCreateDevice()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.CollabRWClient.PostAsJsonAsync(
            $"/api/v1/houses/{ctx.HouseId}/devices",
            new CreateDeviceRequestDto(maintenanceType: null, name: "Nouveau Device CollabRW", type: "Alarme", brand: null, model: null, installDate: null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Device_CollaboratorRO_CannotCreateDevice()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.CollabROClient.PostAsJsonAsync(
            $"/api/v1/houses/{ctx.HouseId}/devices",
            new CreateDeviceRequestDto(maintenanceType: null, name: "Tentative RO", type: "Alarme", brand: null, model: null, installDate: null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Device_Tenant_CannotCreateDevice()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.TenantClient.PostAsJsonAsync(
            $"/api/v1/houses/{ctx.HouseId}/devices",
            new CreateDeviceRequestDto(maintenanceType: null, name: "Tentative Tenant", type: "Alarme", brand: null, model: null, installDate: null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Device_CollaboratorRO_CannotDeleteDevice()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.CollabROClient.DeleteAsync($"/api/v1/devices/{ctx.DeviceId}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Device_Tenant_CannotDeleteDevice()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.TenantClient.DeleteAsync($"/api/v1/devices/{ctx.DeviceId}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    // ========================================================================
    // MAINTENANCE TYPE ACCESS
    // ========================================================================

    #region Maintenance Type (Owner + CollaboratorRW can create; RO and Tenant cannot)

    [Fact]
    public async Task MaintenanceType_Owner_CanCreate()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.OwnerClient.PostAsJsonAsync(
            $"/api/v1/devices/{ctx.DeviceId}/maintenance-types",
            new CreateMaintenanceTypeRequestDto("Ramonage Owner", Periodicity.Annual, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task MaintenanceType_CollaboratorRW_CanCreate()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.CollabRWClient.PostAsJsonAsync(
            $"/api/v1/devices/{ctx.DeviceId}/maintenance-types",
            new CreateMaintenanceTypeRequestDto("Ramonage CollabRW", Periodicity.Annual, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task MaintenanceType_CollaboratorRO_CannotCreate()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.CollabROClient.PostAsJsonAsync(
            $"/api/v1/devices/{ctx.DeviceId}/maintenance-types",
            new CreateMaintenanceTypeRequestDto("Tentative RO", Periodicity.Annual, null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MaintenanceType_Tenant_CannotCreate()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.TenantClient.PostAsJsonAsync(
            $"/api/v1/devices/{ctx.DeviceId}/maintenance-types",
            new CreateMaintenanceTypeRequestDto("Tentative Tenant", Periodicity.Annual, null));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MaintenanceType_UpdateAndDelete_OnlyEditors()
    {
        var ctx = await SetupFullHouseAsync();
        var update = new UpdateMaintenanceTypeRequestDto("Renamed", null, null);

        foreach (var (client, role) in new[] { (ctx.CollabROClient, "CollaboratorRO"), (ctx.TenantClient, "Tenant") })
        {
            (await client.PutAsJsonAsync($"/api/v1/maintenance-types/{ctx.MaintenanceTypeId}", update))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} cannot edit a maintenance type (R5)");
            (await client.DeleteAsync($"/api/v1/maintenance-types/{ctx.MaintenanceTypeId}"))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} cannot delete a maintenance type (R5)");
        }

        (await ctx.CollabRWClient.PutAsJsonAsync($"/api/v1/maintenance-types/{ctx.MaintenanceTypeId}", update))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await ctx.CollabRWClient.DeleteAsync($"/api/v1/maintenance-types/{ctx.MaintenanceTypeId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    // ========================================================================
    // MAINTENANCE LOG ACCESS
    // ========================================================================

    #region Log Maintenance (Owner + CollabRW always; Tenant only if canLogMaintenance; RO never)

    [Fact]
    public async Task LogMaintenance_Owner_CanLog()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.OwnerClient.PostAsJsonAsync(
            $"/api/v1/maintenance-types/{ctx.MaintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: 100m, provider: "TestProvider", notes: "By Owner"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task LogMaintenance_CollaboratorRW_CanLog()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.CollabRWClient.PostAsJsonAsync(
            $"/api/v1/maintenance-types/{ctx.MaintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: 100m, provider: "TestProvider", notes: "By CollabRW"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task LogMaintenance_CollaboratorRO_CannotLog()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.CollabROClient.PostAsJsonAsync(
            $"/api/v1/maintenance-types/{ctx.MaintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: 100m, provider: "TestProvider", notes: "By CollabRO"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LogMaintenance_Tenant_CanLogByDefault()
    {
        var ctx = await SetupFullHouseAsync();
        // Tenant has canLogMaintenance=true by default
        var response = await ctx.TenantClient.PostAsJsonAsync(
            $"/api/v1/maintenance-types/{ctx.MaintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: 100m, provider: "TestProvider", notes: "By Tenant"));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task LogMaintenance_Tenant_CannotLogWhenDisabled()
    {
        var ctx = await SetupFullHouseAsync();

        // Owner disables canLogMaintenance for tenant
        var membersResponse = await ctx.OwnerClient.GetAsync($"/api/v1/houses/{ctx.HouseId}/members");
        var members = await membersResponse.Content.ReadAsJsonAsync<HouseMemberDto[]>();
        var tenant = members!.First(m => m.Role == "Tenant");

        await ctx.OwnerClient.PutAsJsonAsync(
            $"/api/v1/members/{tenant.Id}/permissions",
            new UpdateMemberPermissionsRequestDto(false, null));

        // Tenant tries to log — should fail
        var response = await ctx.TenantClient.PostAsJsonAsync(
            $"/api/v1/maintenance-types/{ctx.MaintenanceTypeId}/instances",
            new LogMaintenanceRequestDto(date: DateTime.UtcNow, cost: 100m, provider: "TestProvider", notes: "Blocked Tenant"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Maintenance History Read Access

    [Fact]
    public async Task MaintenanceHistory_AllRoles_CanViewHistory()
    {
        var ctx = await SetupFullHouseAsync();

        foreach (var (client, role) in new[] {
            (ctx.OwnerClient, "Owner"),
            (ctx.CollabRWClient, "CollaboratorRW"),
            (ctx.CollabROClient, "CollaboratorRO"),
            (ctx.TenantClient, "Tenant") })
        {
            var response = await client.GetAsync($"/api/v1/devices/{ctx.DeviceId}/maintenance-history");
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{role} should be able to view maintenance history");
        }
    }

    #endregion

    #region Maintenance records (R5: tenant may edit, not delete; RO neither)

    [Fact]
    public async Task MaintenanceRecord_Tenant_CanEditButNotDelete()
    {
        var ctx = await SetupFullHouseAsync();

        var edit = await ctx.TenantClient.PutAsJsonAsync(
            $"/api/v1/maintenance-instances/{ctx.MaintenanceInstanceId}",
            new UpdateMaintenanceInstanceRequestDto(null, null, null, "Edited by tenant"));
        edit.StatusCode.Should().Be(HttpStatusCode.OK);

        var delete = await ctx.TenantClient.DeleteAsync($"/api/v1/maintenance-instances/{ctx.MaintenanceInstanceId}");
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MaintenanceRecord_TenantWithoutCostRight_EditKeepsCostAndProvider()
    {
        var ctx = await SetupFullHouseAsync();

        // The tenant (no canViewCosts by default) never sees cost / provider: sending null must not clear them.
        var edit = await ctx.TenantClient.PutAsJsonAsync(
            $"/api/v1/maintenance-instances/{ctx.MaintenanceInstanceId}",
            new UpdateMaintenanceInstanceRequestDto(null, null, null, "Edited by tenant"));
        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        var returned = await edit.Content.ReadAsJsonAsync<MaintenanceInstanceDto>();
        returned!.Cost.Should().BeNull("the response must not leak costs to the tenant");
        returned.Provider.Should().BeNull();

        var history = await (await ctx.OwnerClient.GetAsync($"/api/v1/devices/{ctx.DeviceId}/maintenance-history"))
            .Content.ReadAsJsonAsync<MaintenanceHistoryResponseDto>();
        var stored = history!.Instances.Single(i => i.Id == ctx.MaintenanceInstanceId);
        stored.Cost.Should().Be(150.00m);
        stored.Provider.Should().Be("TechniGaz");
        stored.Notes.Should().Be("Edited by tenant");
    }

    [Fact]
    public async Task MaintenanceRecord_CollaboratorRO_CannotEditNorDelete()
    {
        var ctx = await SetupFullHouseAsync();

        var edit = await ctx.CollabROClient.PutAsJsonAsync(
            $"/api/v1/maintenance-instances/{ctx.MaintenanceInstanceId}",
            new UpdateMaintenanceInstanceRequestDto(null, null, null, "Edited by RO"));
        edit.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var delete = await ctx.CollabROClient.DeleteAsync($"/api/v1/maintenance-instances/{ctx.MaintenanceInstanceId}");
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MaintenanceRecord_CollaboratorRW_CanDelete()
    {
        var ctx = await SetupFullHouseAsync();

        var delete = await ctx.CollabRWClient.DeleteAsync($"/api/v1/maintenance-instances/{ctx.MaintenanceInstanceId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task MaintenanceRecord_TenantWithLoggingRightOff_CannotEdit()
    {
        var ctx = await SetupFullHouseAsync();
        var members = await (await ctx.OwnerClient.GetAsync($"/api/v1/houses/{ctx.HouseId}/members")).Content.ReadAsJsonAsync<HouseMemberDto[]>();
        var tenant = members!.First(m => m.Role == "Tenant");
        (await ctx.OwnerClient.PutAsJsonAsync($"/api/v1/members/{tenant.Id}/permissions",
            new UpdateMemberPermissionsRequestDto(false, null))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var edit = await ctx.TenantClient.PutAsJsonAsync(
            $"/api/v1/maintenance-instances/{ctx.MaintenanceInstanceId}",
            new UpdateMaintenanceInstanceRequestDto(null, null, null, "Edited without the right"));

        edit.StatusCode.Should().Be(HttpStatusCode.Forbidden, "editing a record follows the same right as logging one");
    }

    [Fact]
    public async Task MaintenanceRecord_Stranger_CannotEditNorDelete()
    {
        var ctx = await SetupFullHouseAsync();
        var (stranger, _) = await RegisterUserAsync("Stranger", "Danger");

        (await stranger.PutAsJsonAsync($"/api/v1/maintenance-instances/{ctx.MaintenanceInstanceId}",
            new UpdateMaintenanceInstanceRequestDto(null, null, null, "Hijack"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.DeleteAsync($"/api/v1/maintenance-instances/{ctx.MaintenanceInstanceId}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Capabilities exposed to the UI (R5)

    [Fact]
    public async Task Capabilities_MatchR5_OnHouseAndDeviceDetail()
    {
        var ctx = await SetupFullHouseAsync();

        var expected = new (HttpClient Client, string Role, CapabilitiesDto Caps)[]
        {
            (ctx.OwnerClient, "Owner", new CapabilitiesDto(true, true, true, true, true, true)),
            (ctx.CollabRWClient, "CollaboratorRW", new CapabilitiesDto(true, true, true, false, false, true)),
            (ctx.CollabROClient, "CollaboratorRO", new CapabilitiesDto(false, false, false, false, false, true)),
            (ctx.TenantClient, "Tenant", new CapabilitiesDto(true, false, false, false, false, false)),
        };

        foreach (var (client, role, caps) in expected)
        {
            var house = await (await client.GetAsync($"/api/v1/houses/{ctx.HouseId}")).Content.ReadAsJsonAsync<HouseDetailDto>();
            house!.UserRole.Should().Be(role);
            house.Capabilities.Should().Be(caps, $"house capabilities of {role}");

            var device = await (await client.GetAsync($"/api/v1/devices/{ctx.DeviceId}")).Content.ReadAsJsonAsync<DeviceDetailDto>();
            device!.UserRole.Should().Be(role);
            device.HouseName.Should().NotBeNullOrEmpty();
            device.Capabilities.Should().Be(caps, $"device capabilities of {role}");
        }
    }

    [Fact]
    public async Task Dashboard_TaskCanLogMaintenance_FollowsRole()
    {
        var ctx = await SetupFullHouseAsync();
        // Make the task due so it is listed: « Plus ancien » type created now is due today.
        var device = await (await ctx.OwnerClient.PostAsJsonAsync($"/api/v1/houses/{ctx.HouseId}/devices",
            new CreateDeviceRequestDto(maintenanceType: new DeviceMaintenanceTypeRequestDto(
                customDays: null, customMonths: null,
                lastMaintenance: new LastMaintenanceDto(LastMaintenanceKind.Older, null, null),
                name: "Ramonage", periodicity: HouseFlow.Contracts.Periodicity.Annual),
                name: "Poêle", type: "Poêle à Bois", brand: null, model: null, installDate: null)))
            .Content.ReadAsJsonAsync<DeviceDto>();

        foreach (var (client, canLog) in new[] { (ctx.OwnerClient, true), (ctx.CollabRWClient, true), (ctx.CollabROClient, false), (ctx.TenantClient, true) })
        {
            var dashboard = await (await client.GetAsync("/api/v1/dashboard")).Content.ReadAsJsonAsync<DashboardDto>();
            dashboard!.Tasks.Should().Contain(t => t.DeviceId == device!.Id && t.CanLogMaintenance == canLog);
        }
    }

    [Fact]
    public async Task Dashboard_TaskCapabilities_FollowRoleAndTenantCostRight()
    {
        var ctx = await SetupFullHouseAsync();
        var device = await (await ctx.OwnerClient.PostAsJsonAsync($"/api/v1/houses/{ctx.HouseId}/devices",
            new CreateDeviceRequestDto(maintenanceType: new DeviceMaintenanceTypeRequestDto(
                customDays: null, customMonths: null,
                lastMaintenance: new LastMaintenanceDto(LastMaintenanceKind.Older, null, null),
                name: "Ramonage", periodicity: HouseFlow.Contracts.Periodicity.Annual),
                name: "Poêle", type: "Poêle à Bois", brand: null, model: null, installDate: null)))
            .Content.ReadAsJsonAsync<DeviceDto>();

        async Task<CapabilitiesDto> TaskCapsAsync(HttpClient client)
        {
            var dashboard = await (await client.GetAsync("/api/v1/dashboard")).Content.ReadAsJsonAsync<DashboardDto>();
            return dashboard!.Tasks.Single(t => t.DeviceId == device!.Id).Capabilities!;
        }

        (await TaskCapsAsync(ctx.OwnerClient)).Should().Be(new CapabilitiesDto(true, true, true, true, true, true));
        (await TaskCapsAsync(ctx.CollabRWClient)).Should().Be(new CapabilitiesDto(true, true, true, false, false, true));
        (await TaskCapsAsync(ctx.CollabROClient)).Should().Be(new CapabilitiesDto(false, false, false, false, false, true));
        (await TaskCapsAsync(ctx.TenantClient)).CanViewCosts.Should().BeFalse();

        // The owner grants the cost right to the tenant: the dashboard follows.
        var members = await (await ctx.OwnerClient.GetAsync($"/api/v1/houses/{ctx.HouseId}/members"))
            .Content.ReadAsJsonAsync<HouseMemberDto[]>();
        var tenant = members!.First(m => m.Role == "Tenant");
        (await ctx.OwnerClient.PutAsJsonAsync($"/api/v1/members/{tenant.Id}/permissions",
            new UpdateMemberPermissionsRequestDto(null, true))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var tenantCaps = await TaskCapsAsync(ctx.TenantClient);
        tenantCaps.Should().Be(new CapabilitiesDto(true, false, false, false, false, true));
    }

    #endregion

    // ========================================================================
    // INVITATION PERMISSIONS
    // ========================================================================

    #region Invitation Creation (Owner can invite all; CollabRW only Tenant; others cannot)

    [Fact]
    public async Task Invitation_Owner_CanInviteAllRoles()
    {
        var ctx = await SetupFullHouseAsync();

        foreach (var role in new[] { "CollaboratorRW", "CollaboratorRO", "Tenant" })
        {
            var response = await ctx.OwnerClient.PostAsJsonAsync(
                $"/api/v1/houses/{ctx.HouseId}/invitations",
                new CreateInvitationRequestDto(role, NewInviteeEmail()));
            response.StatusCode.Should().Be(HttpStatusCode.Created, $"Owner should invite {role}");
        }
    }

    [Fact]
    public async Task Invitation_CollaboratorRW_CannotInvite()
    {
        // R5: managing members and invitations is the owner's alone (RW could invite tenants before).
        var ctx = await SetupFullHouseAsync();

        foreach (var role in new[] { "Tenant", "CollaboratorRW", "CollaboratorRO" })
        {
            var response = await ctx.CollabRWClient.PostAsJsonAsync(
                $"/api/v1/houses/{ctx.HouseId}/invitations",
                new CreateInvitationRequestDto(role, NewInviteeEmail()));
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"RW must not invite {role}");
        }
    }

    [Fact]
    public async Task Invitation_OnlyOwner_CanListInvitations()
    {
        var ctx = await SetupFullHouseAsync();

        (await ctx.OwnerClient.GetAsync($"/api/v1/houses/{ctx.HouseId}/invitations")).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var client in new[] { ctx.CollabRWClient, ctx.CollabROClient, ctx.TenantClient })
        {
            (await client.GetAsync($"/api/v1/houses/{ctx.HouseId}/invitations")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Invitation_CollaboratorRO_CannotInvite()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.CollabROClient.PostAsJsonAsync(
            $"/api/v1/houses/{ctx.HouseId}/invitations",
            new CreateInvitationRequestDto("Tenant", NewInviteeEmail()));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invitation_Tenant_CannotInvite()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.TenantClient.PostAsJsonAsync(
            $"/api/v1/houses/{ctx.HouseId}/invitations",
            new CreateInvitationRequestDto("Tenant", NewInviteeEmail()));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invitation_CannotInviteOwnerRole()
    {
        var ctx = await SetupFullHouseAsync();
        var response = await ctx.OwnerClient.PostAsJsonAsync(
            $"/api/v1/houses/{ctx.HouseId}/invitations",
            new CreateInvitationRequestDto("Owner", NewInviteeEmail()));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    // ========================================================================
    // MEMBER MANAGEMENT
    // ========================================================================

    #region Member Management (only Owner can update roles, remove members)

    [Fact]
    public async Task MemberRole_OnlyOwner_CanChangeRoles()
    {
        var ctx = await SetupFullHouseAsync();

        // Get CollabRW member ID
        var membersResponse = await ctx.OwnerClient.GetAsync($"/api/v1/houses/{ctx.HouseId}/members");
        var members = await membersResponse.Content.ReadAsJsonAsync<HouseMemberDto[]>();
        var collabRW = members!.First(m => m.Role == "CollaboratorRW");

        // Owner can change role
        var ownerResponse = await ctx.OwnerClient.PutAsJsonAsync(
            $"/api/v1/members/{collabRW.Id}/role",
            new UpdateMemberRoleRequestDto("CollaboratorRO"));
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MemberRole_CollaboratorRW_CannotChangeRoles()
    {
        var ctx = await SetupFullHouseAsync();

        var membersResponse = await ctx.OwnerClient.GetAsync($"/api/v1/houses/{ctx.HouseId}/members");
        var members = await membersResponse.Content.ReadAsJsonAsync<HouseMemberDto[]>();
        var tenant = members!.First(m => m.Role == "Tenant");

        var response = await ctx.CollabRWClient.PutAsJsonAsync(
            $"/api/v1/members/{tenant.Id}/role",
            new UpdateMemberRoleRequestDto("CollaboratorRO"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemoveMember_OnlyOwner_CanRemove()
    {
        var ctx = await SetupFullHouseAsync();

        var membersResponse = await ctx.OwnerClient.GetAsync($"/api/v1/houses/{ctx.HouseId}/members");
        var members = await membersResponse.Content.ReadAsJsonAsync<HouseMemberDto[]>();
        var collabRO = members!.First(m => m.Role == "CollaboratorRO");

        // CollabRW tries to remove CollabRO — should fail
        var rwResponse = await ctx.CollabRWClient.DeleteAsync($"/api/v1/members/{collabRO.Id}");
        rwResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Owner removes CollabRO — should succeed
        var ownerResponse = await ctx.OwnerClient.DeleteAsync($"/api/v1/members/{collabRO.Id}");
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    // ========================================================================
    // CROSS-USER ISOLATION
    // ========================================================================

    #region Cross-User Isolation (stranger cannot access house/devices)

    [Fact]
    public async Task Isolation_StrangerUser_CannotAccessHouse()
    {
        var ctx = await SetupFullHouseAsync();
        var (strangerClient, _) = await RegisterUserAsync("Stranger", "Danger");

        var response = await strangerClient.GetAsync($"/api/v1/houses/{ctx.HouseId}");
        // Same convention across the API: 404 = unknown id, 403 = exists but not accessible.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Isolation_StrangerUser_CannotAccessDevice()
    {
        var ctx = await SetupFullHouseAsync();
        var (strangerClient, _) = await RegisterUserAsync("Stranger", "Danger");

        var response = await strangerClient.GetAsync($"/api/v1/devices/{ctx.DeviceId}");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Isolation_StrangerUser_CannotCreateInvitation()
    {
        var ctx = await SetupFullHouseAsync();
        var (strangerClient, _) = await RegisterUserAsync("Stranger", "Hacker");

        var response = await strangerClient.PostAsJsonAsync(
            $"/api/v1/houses/{ctx.HouseId}/invitations",
            new CreateInvitationRequestDto("CollaboratorRW", NewInviteeEmail()));
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }

    #endregion
}
