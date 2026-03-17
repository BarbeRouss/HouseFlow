using FluentAssertions;
using HouseFlow.Application.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Collaboration;

public class MemberTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public MemberTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private async Task<(HttpClient client, Guid houseId)> CreateAuthenticatedClientWithHouseAsync()
    {
        var client = CreateClient();
        var email = $"test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto("Owner", "User", email, "Password123!");

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        response.EnsureSuccessStatusCode();

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        var housesResponse = await client.GetAsync("/api/v1/houses");
        var houses = await housesResponse.Content.ReadAsJsonAsync<HousesListResponseDto>();
        var houseId = houses!.Houses.First().Id;

        return (client, houseId);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var email = $"test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto("Member", "User", email, "Password123!");

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        response.EnsureSuccessStatusCode();

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        return client;
    }

    private async Task<(HttpClient memberClient, Guid memberId)> AddMemberToHouseAsync(
        HttpClient ownerClient, Guid houseId, string role)
    {
        // Owner creates invitation
        var createRequest = new CreateInvitationRequestDto(role);
        var createResponse = await ownerClient.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", createRequest);
        var invitation = await createResponse.Content.ReadAsJsonAsync<InvitationDto>();

        // New user accepts
        var memberClient = await CreateAuthenticatedClientAsync();
        await memberClient.PostAsync($"/api/v1/invitations/{invitation!.Token}/accept", null);

        // Get the member ID
        var membersResponse = await ownerClient.GetAsync($"/api/v1/houses/{houseId}/members");
        var members = await membersResponse.Content.ReadAsJsonAsync<HouseMemberDto[]>();
        var member = members!.First(m => m.Role == role);

        return (memberClient, member.Id);
    }

    #region Get House Members Tests

    [Fact]
    public async Task GetHouseMembers_AsOwner_ReturnsMembers()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        var response = await ownerClient.GetAsync($"/api/v1/houses/{houseId}/members");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var members = await response.Content.ReadAsJsonAsync<HouseMemberDto[]>();
        members.Should().NotBeNull();
        members!.Should().HaveCount(1); // Just the owner
        members[0].Role.Should().Be("Owner");
    }

    [Fact]
    public async Task GetHouseMembers_AfterInvitation_IncludesNewMember()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        await AddMemberToHouseAsync(ownerClient, houseId, "CollaboratorRW");

        var response = await ownerClient.GetAsync($"/api/v1/houses/{houseId}/members");
        var members = await response.Content.ReadAsJsonAsync<HouseMemberDto[]>();

        members.Should().HaveCount(2);
        members.Should().Contain(m => m.Role == "Owner");
        members.Should().Contain(m => m.Role == "CollaboratorRW");
    }

    #endregion

    #region Update Member Role Tests

    [Fact]
    public async Task UpdateMemberRole_AsOwner_ChangesRole()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (_, memberId) = await AddMemberToHouseAsync(ownerClient, houseId, "CollaboratorRW");

        var updateRequest = new UpdateMemberRoleRequestDto("Tenant");
        var response = await ownerClient.PutAsJsonAsync($"/api/v1/members/{memberId}/role", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadAsJsonAsync<HouseMemberDto>();
        updated!.Role.Should().Be("Tenant");
    }

    [Fact]
    public async Task UpdateMemberRole_ToOwner_Returns400()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (_, memberId) = await AddMemberToHouseAsync(ownerClient, houseId, "CollaboratorRW");

        var updateRequest = new UpdateMemberRoleRequestDto("Owner");
        var response = await ownerClient.PutAsJsonAsync($"/api/v1/members/{memberId}/role", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Update Member Permissions Tests

    [Fact]
    public async Task UpdateMemberPermissions_TenantCanLogMaintenance_Toggles()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (_, memberId) = await AddMemberToHouseAsync(ownerClient, houseId, "Tenant");

        // Disable canLogMaintenance
        var updateRequest = new UpdateMemberPermissionsRequestDto(false);
        var response = await ownerClient.PutAsJsonAsync($"/api/v1/members/{memberId}/permissions", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify
        var membersResponse = await ownerClient.GetAsync($"/api/v1/houses/{houseId}/members");
        var members = await membersResponse.Content.ReadAsJsonAsync<HouseMemberDto[]>();
        var tenant = members!.First(m => m.Role == "Tenant");
        tenant.CanLogMaintenance.Should().BeFalse();
    }

    #endregion

    #region Remove Member Tests

    [Fact]
    public async Task RemoveMember_AsOwner_RemovesMember()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (_, memberId) = await AddMemberToHouseAsync(ownerClient, houseId, "CollaboratorRW");

        var response = await ownerClient.DeleteAsync($"/api/v1/members/{memberId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify member is gone
        var membersResponse = await ownerClient.GetAsync($"/api/v1/houses/{houseId}/members");
        var members = await membersResponse.Content.ReadAsJsonAsync<HouseMemberDto[]>();
        members.Should().HaveCount(1); // Only owner remains
    }

    [Fact]
    public async Task RemoveMember_NonOwner_Returns403()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (memberClient, _) = await AddMemberToHouseAsync(ownerClient, houseId, "CollaboratorRW");

        // Add another member
        var (_, member2Id) = await AddMemberToHouseAsync(ownerClient, houseId, "Tenant");

        // CollaboratorRW tries to remove Tenant — should fail (only owner can)
        var response = await memberClient.DeleteAsync($"/api/v1/members/{member2Id}");

        // CollaboratorRW should not be allowed to remove members
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Get All Collaborators Tests

    [Fact]
    public async Task GetAllCollaborators_ReturnsOwnedHousesWithMembers()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        await AddMemberToHouseAsync(ownerClient, houseId, "CollaboratorRW");

        var response = await ownerClient.GetAsync("/api/v1/collaborators");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadAsJsonAsync<AllCollaboratorsResponseDto>();
        result.Should().NotBeNull();
        result!.Houses.Should().NotBeEmpty();

        var houseCollaborators = result.Houses.FirstOrDefault(h => h.HouseId == houseId);
        houseCollaborators.Should().NotBeNull("the owned house should appear in collaborators list");
        houseCollaborators!.Members.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    #endregion

    #region RBAC - Shared House Access Tests

    [Fact]
    public async Task SharedHouse_CollaboratorRW_CanSeeHouse()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (memberClient, _) = await AddMemberToHouseAsync(ownerClient, houseId, "CollaboratorRW");

        var response = await memberClient.GetAsync($"/api/v1/houses/{houseId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var house = await response.Content.ReadAsJsonAsync<HouseDetailDto>();
        house.Should().NotBeNull();
        house!.UserRole.Should().Be("CollaboratorRW");
    }

    [Fact]
    public async Task SharedHouse_CollaboratorRO_CanSeeHouseButNotEdit()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (memberClient, _) = await AddMemberToHouseAsync(ownerClient, houseId, "CollaboratorRO");

        // Can see house
        var getResponse = await memberClient.GetAsync($"/api/v1/houses/{houseId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Cannot edit house
        var updateRequest = new UpdateHouseRequestDto("Hacked Name", null, null, null);
        var updateResponse = await memberClient.PutAsJsonAsync($"/api/v1/houses/{houseId}", updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SharedHouse_Tenant_CanSeeHouse()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (tenantClient, _) = await AddMemberToHouseAsync(ownerClient, houseId, "Tenant");

        var response = await tenantClient.GetAsync($"/api/v1/houses/{houseId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var house = await response.Content.ReadAsJsonAsync<HouseDetailDto>();
        house!.UserRole.Should().Be("Tenant");
    }

    [Fact]
    public async Task HouseList_IncludesSharedHouses()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (memberClient, _) = await AddMemberToHouseAsync(ownerClient, houseId, "CollaboratorRW");

        var response = await memberClient.GetAsync("/api/v1/houses");
        var houses = await response.Content.ReadAsJsonAsync<HousesListResponseDto>();

        // Member should see their own auto-created house + the shared house
        houses!.Houses.Should().HaveCountGreaterThanOrEqualTo(2);
        houses.Houses.Should().Contain(h => h.Id == houseId);
    }

    #endregion
}
