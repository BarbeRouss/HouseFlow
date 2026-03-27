using FluentAssertions;
using HouseFlow.Application.DTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Collaboration;

[Collection("Integration")]
public class InvitationTests
{
    private readonly IntegrationTestFixture _fixture;

    public InvitationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient CreateClient() => _fixture.CreateApiClient();

    private async Task<(HttpClient client, Guid houseId)> CreateAuthenticatedClientWithHouseAsync()
    {
        var client = CreateClient();
        var email = $"test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto("Test", "User", email, "Password123!");

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
        var registerRequest = new RegisterRequestDto("Invited", "User", email, "Password123!");

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        response.EnsureSuccessStatusCode();

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        return client;
    }

    #region Create Invitation Tests

    [Fact]
    public async Task CreateInvitation_AsOwner_ReturnsInvitation()
    {
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        var request = new CreateInvitationRequestDto("CollaboratorRW");
        var response = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var invitation = await response.Content.ReadAsJsonAsync<InvitationDto>();
        invitation.Should().NotBeNull();
        invitation!.Token.Should().NotBeNullOrEmpty();
        invitation.Role.Should().Be("CollaboratorRW");
        invitation.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task CreateInvitation_Unauthenticated_Returns401()
    {
        var client = CreateClient();
        var request = new CreateInvitationRequestDto("CollaboratorRW");

        var response = await client.PostAsJsonAsync($"/api/v1/houses/{Guid.NewGuid()}/invitations", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateInvitation_AsOwner_CanCreateAllRoles()
    {
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        foreach (var role in new[] { "CollaboratorRW", "CollaboratorRO", "Tenant" })
        {
            var request = new CreateInvitationRequestDto(role);
            var response = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", request);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }
    }

    #endregion

    #region Get Invitation Info Tests

    [Fact]
    public async Task GetInvitationInfo_ValidToken_ReturnsInfo()
    {
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        // Create invitation
        var createRequest = new CreateInvitationRequestDto("CollaboratorRW");
        var createResponse = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", createRequest);
        var invitation = await createResponse.Content.ReadAsJsonAsync<InvitationDto>();

        // Get info (public endpoint - no auth needed)
        var publicClient = CreateClient();
        var response = await publicClient.GetAsync($"/api/v1/invitations/{invitation!.Token}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var info = await response.Content.ReadAsJsonAsync<InvitationInfoDto>();
        info.Should().NotBeNull();
        info!.HouseName.Should().NotBeNullOrEmpty();
        info.Role.Should().Be("CollaboratorRW");
        info.IsExpired.Should().BeFalse();
    }

    [Fact]
    public async Task GetInvitationInfo_InvalidToken_Returns404()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/api/v1/invitations/invalid-token");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Accept Invitation Tests

    [Fact]
    public async Task AcceptInvitation_ValidToken_JoinsHouse()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        // Owner creates invitation
        var createRequest = new CreateInvitationRequestDto("CollaboratorRW");
        var createResponse = await ownerClient.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", createRequest);
        var invitation = await createResponse.Content.ReadAsJsonAsync<InvitationDto>();

        // Second user accepts
        var user2Client = await CreateAuthenticatedClientAsync();
        var acceptResponse = await user2Client.PostAsync($"/api/v1/invitations/{invitation!.Token}/accept", null);

        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await acceptResponse.Content.ReadAsJsonAsync<AcceptInvitationResponseDto>();
        result.Should().NotBeNull();
        result!.HouseId.Should().Be(houseId);
        result.Role.Should().Be("CollaboratorRW");

        // Verify user2 can now see the house
        var housesResponse = await user2Client.GetAsync("/api/v1/houses");
        var houses = await housesResponse.Content.ReadAsJsonAsync<HousesListResponseDto>();
        houses!.Houses.Should().Contain(h => h.Id == houseId);
    }

    [Fact]
    public async Task AcceptInvitation_AlreadyMember_Returns400()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        // Owner creates invitation
        var createRequest = new CreateInvitationRequestDto("CollaboratorRW");
        var createResponse = await ownerClient.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", createRequest);
        var invitation = await createResponse.Content.ReadAsJsonAsync<InvitationDto>();

        // Second user accepts
        var user2Client = await CreateAuthenticatedClientAsync();
        await user2Client.PostAsync($"/api/v1/invitations/{invitation!.Token}/accept", null);

        // Create another invitation and try to accept again
        var createRequest2 = new CreateInvitationRequestDto("Tenant");
        var createResponse2 = await ownerClient.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", createRequest2);
        var invitation2 = await createResponse2.Content.ReadAsJsonAsync<InvitationDto>();

        var secondAccept = await user2Client.PostAsync($"/api/v1/invitations/{invitation2!.Token}/accept", null);
        secondAccept.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Revoke Invitation Tests

    [Fact]
    public async Task RevokeInvitation_AsOwner_Succeeds()
    {
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        // Create invitation
        var createRequest = new CreateInvitationRequestDto("CollaboratorRW");
        var createResponse = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", createRequest);
        var invitation = await createResponse.Content.ReadAsJsonAsync<InvitationDto>();

        // Revoke
        var revokeResponse = await client.DeleteAsync($"/api/v1/invitations/{invitation!.Id}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify token is marked as expired/revoked
        var publicClient = CreateClient();
        var infoResponse = await publicClient.GetAsync($"/api/v1/invitations/{invitation.Token}");
        infoResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var info = await infoResponse.Content.ReadAsJsonAsync<InvitationInfoDto>();
        info!.IsExpired.Should().BeTrue("revoked invitations should be marked as expired");
    }

    #endregion

    #region Get House Invitations Tests

    [Fact]
    public async Task GetHouseInvitations_AsOwner_ReturnsList()
    {
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        // Create 2 invitations
        await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", new CreateInvitationRequestDto("CollaboratorRW"));
        await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", new CreateInvitationRequestDto("Tenant"));

        var response = await client.GetAsync($"/api/v1/houses/{houseId}/invitations");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var invitations = await response.Content.ReadAsJsonAsync<InvitationDto[]>();
        invitations.Should().NotBeNull();
        invitations!.Length.Should().Be(2);
    }

    #endregion
}
