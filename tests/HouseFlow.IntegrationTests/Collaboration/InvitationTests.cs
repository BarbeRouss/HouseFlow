using Microsoft.EntityFrameworkCore;
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
        var registerRequest = new RegisterRequestDto(firstName: "Test", lastName: "User", email: email, password: "Password123!", consentAccepted: true);

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        response.EnsureSuccessStatusCode();

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        var houseId = await client.CreateHouseAsync();

        return (client, houseId);
    }

    /// <summary>A new account; pass the invitation's email to get the invitee (only they may accept or decline).</summary>
    private async Task<HttpClient> CreateAuthenticatedClientAsync(string? email = null)
    {
        var client = CreateClient();
        email ??= $"test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto(firstName: "Invited", lastName: "User", email: email, password: "Password123!", consentAccepted: true);

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

        var request = new CreateInvitationRequestDto("CollaboratorRW", NewInviteeEmail());
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
        var request = new CreateInvitationRequestDto("CollaboratorRW", NewInviteeEmail());

        var response = await client.PostAsJsonAsync($"/api/v1/houses/{Guid.NewGuid()}/invitations", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateInvitation_AsOwner_CanCreateAllRoles()
    {
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        foreach (var role in new[] { "CollaboratorRW", "CollaboratorRO", "Tenant" })
        {
            var request = new CreateInvitationRequestDto(role, NewInviteeEmail());
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
        var createRequest = new CreateInvitationRequestDto("CollaboratorRW", NewInviteeEmail());
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
        var inviteeEmail = NewInviteeEmail();
        var createRequest = new CreateInvitationRequestDto("CollaboratorRW", inviteeEmail);
        var createResponse = await ownerClient.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", createRequest);
        var invitation = await createResponse.Content.ReadAsJsonAsync<InvitationDto>();

        // The invitee accepts (email compared case-insensitively)
        var user2Client = await CreateAuthenticatedClientAsync(inviteeEmail.ToUpperInvariant());
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

        // Owner creates invitation, the invitee accepts
        var inviteeEmail = NewInviteeEmail();
        var invitation = await InviteAsync(ownerClient, houseId, "CollaboratorRW", inviteeEmail);
        var user2Client = await CreateAuthenticatedClientAsync(inviteeEmail);
        (await user2Client.PostAsync($"/api/v1/invitations/{invitation.Token}/accept", null)).EnsureSuccessStatusCode();

        // A legacy invitation (no email, predating the redesign) accepted by the same member
        var invitation2 = await InviteAsync(ownerClient, houseId, "Tenant");
        await ClearInvitationEmailAsync(invitation2.Id);

        var secondAccept = await user2Client.PostAsync($"/api/v1/invitations/{invitation2.Token}/accept", null);
        secondAccept.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await secondAccept.ReadErrorCodeAsync()).Should().Be("already_member");
    }

    /// <summary>Turns an invitation into a legacy one (created before the email field): no email check.</summary>
    private async Task ClearInvitationEmailAsync(Guid invitationId)
    {
        await using var db = await _fixture.CreateDbContextAsync();
        var entity = await db.Invitations.SingleAsync(i => i.Id == invitationId);
        entity.Email = null;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AcceptInvitation_ConcurrentAcceptancesOnDifferentHouses_AllSucceed()
    {
        // Regression test for #198: the Serializable transaction in AcceptInvitationAsync
        // can hit Postgres 40001 under concurrent writes to HouseMembers/Invitations/Users
        // even across unrelated houses. Every accept below targets a different house, so
        // none of them should fail on business rules - only the serialization retry decides
        // whether they all come back 200.
        const int concurrency = 8;

        var pending = new List<(string Token, HttpClient Acceptor)>();
        for (var i = 0; i < concurrency; i++)
        {
            var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
            var inviteeEmail = NewInviteeEmail();
            var createRequest = new CreateInvitationRequestDto("CollaboratorRW", inviteeEmail);
            var createResponse = await ownerClient.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", createRequest);
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
            var invitation = await createResponse.Content.ReadAsJsonAsync<InvitationDto>();

            var acceptor = await CreateAuthenticatedClientAsync(inviteeEmail);
            pending.Add((invitation!.Token, acceptor));
        }

        var acceptResponses = await Task.WhenAll(
            pending.Select(p => p.Acceptor.PostAsync($"/api/v1/invitations/{p.Token}/accept", null)));

        acceptResponses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK,
            "concurrent accepts on different houses have no reason to conflict once serialization failures are retried");
    }

    [Fact]
    public async Task AcceptInvitation_TwoUsersRaceForSameInvitation_OnlyOneJoins()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var createResponse = await ownerClient.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", new CreateInvitationRequestDto("CollaboratorRW", NewInviteeEmail()));
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var invitation = await createResponse.Content.ReadAsJsonAsync<InvitationDto>();
        // Two accounts cannot share the invitation email: the race is only possible on a legacy
        // invitation (no email), which is exactly what the Serializable transaction still guards.
        await ClearInvitationEmailAsync(invitation!.Id);

        var userA = await CreateAuthenticatedClientAsync();
        var userB = await CreateAuthenticatedClientAsync();

        var responses = await Task.WhenAll(
            userA.PostAsync($"/api/v1/invitations/{invitation!.Token}/accept", null),
            userB.PostAsync($"/api/v1/invitations/{invitation.Token}/accept", null));

        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(
            [HttpStatusCode.OK, HttpStatusCode.BadRequest],
            "a single-use invitation admits exactly one of two concurrent acceptors");

        var membersResponse = await ownerClient.GetAsync($"/api/v1/houses/{houseId}/members");
        var members = await membersResponse.Content.ReadAsJsonAsync<HouseMemberDto[]>();
        members!.Should().HaveCount(2, "the owner plus the single user who won the race");
    }

    [Fact]
    public async Task AcceptInvitation_SameUserTwice_IsIdempotent()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var inviteeEmail = NewInviteeEmail();
        var createResponse = await ownerClient.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", new CreateInvitationRequestDto("Tenant", inviteeEmail));
        var invitation = await createResponse.Content.ReadAsJsonAsync<InvitationDto>();
        var user = await CreateAuthenticatedClientAsync(inviteeEmail);

        var first = await user.PostAsync($"/api/v1/invitations/{invitation!.Token}/accept", null);
        var second = await user.PostAsync($"/api/v1/invitations/{invitation.Token}/accept", null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK,
            "re-accepting an invitation you already accepted (double submit, or a retry after a lost commit acknowledgement) is a no-op");
        (await second.Content.ReadAsJsonAsync<AcceptInvitationResponseDto>())!.HouseId.Should().Be(houseId);

        var members = await (await ownerClient.GetAsync($"/api/v1/houses/{houseId}/members")).Content.ReadAsJsonAsync<HouseMemberDto[]>();
        members!.Should().HaveCount(2);
    }

    #endregion

    #region Revoke Invitation Tests

    [Fact]
    public async Task RevokeInvitation_AsOwner_Succeeds()
    {
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        // Create invitation
        var createRequest = new CreateInvitationRequestDto("CollaboratorRW", NewInviteeEmail());
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
        await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", new CreateInvitationRequestDto("CollaboratorRW", NewInviteeEmail()));
        await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", new CreateInvitationRequestDto("Tenant", NewInviteeEmail()));

        var response = await client.GetAsync($"/api/v1/houses/{houseId}/invitations");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var invitations = await response.Content.ReadAsJsonAsync<InvitationDto[]>();
        invitations.Should().NotBeNull();
        invitations!.Length.Should().Be(2);
    }

    #endregion

    #region Redesign (M5 / P03 / P04): email, decline, resend, registration through an invitation

    private async Task<InvitationDto> InviteAsync(HttpClient ownerClient, Guid houseId, string role = "CollaboratorRW", string? email = null)
    {
        var response = await ownerClient.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations",
            new CreateInvitationRequestDto(role, email ?? NewInviteeEmail()));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadAsJsonAsync<InvitationDto>())!;
    }

    /// <summary>Invitation plus a client logged in as its invitee (account holding the invitation email).</summary>
    private async Task<(InvitationDto Invitation, HttpClient Invitee)> InviteNewAccountAsync(
        HttpClient ownerClient, Guid houseId, string role = "CollaboratorRW")
    {
        var email = NewInviteeEmail();
        var invitation = await InviteAsync(ownerClient, houseId, role, email);
        return (invitation, await CreateAuthenticatedClientAsync(email));
    }

    private async Task ExpireAsync(Guid invitationId)
    {
        await using var db = await _fixture.CreateDbContextAsync();
        var entity = await db.Invitations.SingleAsync(i => i.Id == invitationId);
        entity.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateInvitation_StoresEmail_AndInfoShowsInviterFullName()
    {
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var email = NewInviteeEmail();

        var invitation = await InviteAsync(client, houseId, "Tenant", email);
        invitation.Email.Should().Be(email);
        invitation.IsExpired.Should().BeFalse();

        var info = await (await CreateClient().GetAsync($"/api/v1/invitations/{invitation.Token}"))
            .Content.ReadAsJsonAsync<InvitationInfoDto>();
        info!.Email.Should().Be(email);
        info.HouseId.Should().Be(houseId);
        info.InvitedByName.Should().Be("Test User", "the inviter's name must be loaded (was always empty)");
        info.Status.Should().Be("Pending");
        info.IsAlreadyMember.Should().BeNull("anonymous callers get no membership information");
    }

    [Fact]
    public async Task CreateInvitation_WithoutEmail_Returns400()
    {
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations", new { role = "Tenant" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateInvitation_SameEmailTwice_Returns409InvitationAlreadyPending()
    {
        var (client, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var email = NewInviteeEmail();
        await InviteAsync(client, houseId, "Tenant", email);

        var response = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations",
            new CreateInvitationRequestDto("Tenant", email.ToUpperInvariant()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).Should().Be("invitation_already_pending");
    }

    [Fact]
    public async Task GetInvitationInfo_Authenticated_TellsWhetherAlreadyMember()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var invitation = await InviteAsync(ownerClient, houseId);

        var ownerView = await (await ownerClient.GetAsync($"/api/v1/invitations/{invitation.Token}"))
            .Content.ReadAsJsonAsync<InvitationInfoDto>();
        ownerView!.IsAlreadyMember.Should().BeTrue();

        var stranger = await CreateAuthenticatedClientAsync();
        var strangerView = await (await stranger.GetAsync($"/api/v1/invitations/{invitation.Token}"))
            .Content.ReadAsJsonAsync<InvitationInfoDto>();
        strangerView!.IsAlreadyMember.Should().BeFalse();
    }

    [Fact]
    public async Task DeclineInvitation_ByInvitee_MakesItUnusable()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (invitation, invitee) = await InviteNewAccountAsync(ownerClient, houseId);

        var decline = await invitee.PostAsync($"/api/v1/invitations/{invitation.Token}/decline", null);
        decline.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var info = await (await CreateClient().GetAsync($"/api/v1/invitations/{invitation.Token}"))
            .Content.ReadAsJsonAsync<InvitationInfoDto>();
        info!.Status.Should().Be("Declined");
        info.IsExpired.Should().BeTrue();

        var accept = await invitee.PostAsync($"/api/v1/invitations/{invitation.Token}/accept", null);
        accept.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await accept.ReadErrorCodeAsync()).Should().Be("invitation_invalid");

        var pending = await (await ownerClient.GetAsync($"/api/v1/houses/{houseId}/invitations")).Content.ReadAsJsonAsync<InvitationDto[]>();
        pending.Should().NotContain(i => i.Id == invitation.Id, "declined invitations leave the pending list");
    }

    [Fact]
    public async Task DeclineInvitation_UnknownToken_Returns404()
    {
        var client = await CreateAuthenticatedClientAsync();
        var response = await client.PostAsync("/api/v1/invitations/unknown-token/decline", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResendInvitation_RegeneratesTokenAndResetsExpiry()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var inviteeEmail = NewInviteeEmail();
        var invitation = await InviteAsync(ownerClient, houseId, email: inviteeEmail);

        // Simulate an invitation whose 7 days have passed.
        await ExpireAsync(invitation.Id);
        var listed = await (await ownerClient.GetAsync($"/api/v1/houses/{houseId}/invitations")).Content.ReadAsJsonAsync<InvitationDto[]>();
        listed!.Single(i => i.Id == invitation.Id).IsExpired.Should().BeTrue("expired invitations stay listed so they can be re-sent");

        var resend = await ownerClient.PostAsync($"/api/v1/invitations/{invitation.Id}/resend", null);
        resend.StatusCode.Should().Be(HttpStatusCode.OK);
        var resent = await resend.Content.ReadAsJsonAsync<InvitationDto>();
        resent!.Token.Should().NotBe(invitation.Token);
        resent.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(6));
        resent.IsExpired.Should().BeFalse();
        resent.Status.Should().Be("Pending");

        (await CreateClient().GetAsync($"/api/v1/invitations/{invitation.Token}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "the previous link stops working");

        var invitee = await CreateAuthenticatedClientAsync(inviteeEmail);
        (await invitee.PostAsync($"/api/v1/invitations/{resent.Token}/accept", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResendAndCancelInvitation_NonOwner_Returns403()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (rwInvitation, rwClient) = await InviteNewAccountAsync(ownerClient, houseId, "CollaboratorRW");
        (await rwClient.PostAsync($"/api/v1/invitations/{rwInvitation.Token}/accept", null)).EnsureSuccessStatusCode();

        var other = await InviteAsync(ownerClient, houseId, "Tenant");

        (await rwClient.PostAsync($"/api/v1/invitations/{other.Id}/resend", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await rwClient.DeleteAsync($"/api/v1/invitations/{other.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Register_WithInvitation_JoinsOnlyTheSharedHouse()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var email = NewInviteeEmail();
        var invitation = await InviteAsync(ownerClient, houseId, "Tenant", email);

        var client = CreateClient();
        var response = await client.PostAsJsonAsync($"/api/v1/auth/register?invitationToken={invitation.Token}",
            new RegisterRequestDto(firstName: "In", lastName: "Vitee", email: email, password: "Password123!", consentAccepted: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        auth!.JoinedHouseId.Should().Be(houseId);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var houses = await (await client.GetAsync("/api/v1/houses")).Content.ReadAsJsonAsync<HousesListResponseDto>();
        houses!.Houses.Should().ContainSingle().Which.Id.Should().Be(houseId);
        houses.Houses.Single().UserRole.Should().Be("Tenant");
    }

    [Fact]
    public async Task Register_WithInvitationForAnotherEmail_Returns400AndCreatesNoAccount()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var invitation = await InviteAsync(ownerClient, houseId, "Tenant", NewInviteeEmail());
        var otherEmail = NewInviteeEmail();

        var response = await CreateClient().PostAsJsonAsync($"/api/v1/auth/register?invitationToken={invitation.Token}",
            new RegisterRequestDto(firstName: "Other", lastName: "Person", email: otherEmail, password: "Password123!", consentAccepted: true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).Should().Be("invitation_email_mismatch");

        var login = await CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequestDto(email: otherEmail, password: "Password123!", rememberMe: false));
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "no account may be created by a refused registration");
    }

    [Fact]
    public async Task Register_WithUnknownInvitation_Returns400InvitationInvalid()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/v1/auth/register?invitationToken=nope",
            new RegisterRequestDto(firstName: "A", lastName: "B", email: NewInviteeEmail(), password: "Password123!", consentAccepted: true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).Should().Be("invitation_invalid");
    }

    [Fact]
    public async Task Members_ExposeJoinDate_OwnerFirst()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (invitation, member) = await InviteNewAccountAsync(ownerClient, houseId);
        (await member.PostAsync($"/api/v1/invitations/{invitation.Token}/accept", null)).EnsureSuccessStatusCode();

        var members = await (await member.GetAsync($"/api/v1/houses/{houseId}/members")).Content.ReadAsJsonAsync<HouseMemberDto[]>();

        members!.Should().HaveCount(2);
        members[0].Role.Should().Be("Owner");
        members.Should().OnlyContain(m => m.CreatedAt > DateTime.UtcNow.AddMinutes(-10));
    }

    #endregion

    #region Fix round: invitee email enforcement, codes, limits, disclosure

    [Fact]
    public async Task AcceptInvitation_ByAccountWithAnotherEmail_Returns400EmailMismatch_AndDoesNotJoin()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var invitation = await InviteAsync(ownerClient, houseId, "CollaboratorRW");
        var linkHolder = await CreateAuthenticatedClientAsync();

        var response = await linkHolder.PostAsync($"/api/v1/invitations/{invitation.Token}/accept", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).Should().Be("invitation_email_mismatch");
        (await linkHolder.GetAsync($"/api/v1/houses/{houseId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var info = await (await CreateClient().GetAsync($"/api/v1/invitations/{invitation.Token}")).Content.ReadAsJsonAsync<InvitationInfoDto>();
        info!.Status.Should().Be("Pending", "a refused attempt leaves the invitation usable by its invitee");
    }

    [Fact]
    public async Task DeclineInvitation_ByAccountWithAnotherEmail_Returns400EmailMismatch_AndStaysPending()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var invitation = await InviteAsync(ownerClient, houseId);
        var linkHolder = await CreateAuthenticatedClientAsync();

        var response = await linkHolder.PostAsync($"/api/v1/invitations/{invitation.Token}/decline", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).Should().Be("invitation_email_mismatch");
        var info = await (await CreateClient().GetAsync($"/api/v1/invitations/{invitation.Token}")).Content.ReadAsJsonAsync<InvitationInfoDto>();
        info!.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task AcceptAndDecline_ByCreator_Return400OwnInvitation()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var invitation = await InviteAsync(ownerClient, houseId);

        var accept = await ownerClient.PostAsync($"/api/v1/invitations/{invitation.Token}/accept", null);
        accept.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await accept.ReadErrorCodeAsync()).Should().Be("own_invitation");

        var decline = await ownerClient.PostAsync($"/api/v1/invitations/{invitation.Token}/decline", null);
        decline.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await decline.ReadErrorCodeAsync()).Should().Be("own_invitation");
    }

    [Fact]
    public async Task CreateInvitation_ForExistingMemberEmail_Returns409AlreadyMember()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var email = NewInviteeEmail();
        var invitation = await InviteAsync(ownerClient, houseId, "Tenant", email);
        (await (await CreateAuthenticatedClientAsync(email)).PostAsync($"/api/v1/invitations/{invitation.Token}/accept", null)).EnsureSuccessStatusCode();

        var response = await ownerClient.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations",
            new CreateInvitationRequestDto("CollaboratorRO", email.ToUpperInvariant()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).Should().Be("already_member");
    }

    [Fact]
    public async Task AcceptAndDecline_RevokedOrExpiredInvitation_Return400InvitationInvalid()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (revoked, revokedInvitee) = await InviteNewAccountAsync(ownerClient, houseId);
        (await ownerClient.DeleteAsync($"/api/v1/invitations/{revoked.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var (expired, expiredInvitee) = await InviteNewAccountAsync(ownerClient, houseId, "Tenant");
        await ExpireAsync(expired.Id);

        foreach (var (invitation, invitee) in new[] { (revoked, revokedInvitee), (expired, expiredInvitee) })
        {
            foreach (var action in new[] { "accept", "decline" })
            {
                var response = await invitee.PostAsync($"/api/v1/invitations/{invitation.Token}/{action}", null);
                response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{action} of a {invitation.Role} invitation no longer usable");
                (await response.ReadErrorCodeAsync()).Should().Be("invitation_invalid");
            }
        }
    }

    [Fact]
    public async Task GetInvitationInfo_OnceAnswered_NoLongerDisclosesTheEmail()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (accepted, acceptor) = await InviteNewAccountAsync(ownerClient, houseId);
        (await acceptor.PostAsync($"/api/v1/invitations/{accepted.Token}/accept", null)).EnsureSuccessStatusCode();
        var (declined, decliner) = await InviteNewAccountAsync(ownerClient, houseId, "Tenant");
        (await decliner.PostAsync($"/api/v1/invitations/{declined.Token}/decline", null)).EnsureSuccessStatusCode();
        var revoked = await InviteAsync(ownerClient, houseId, "CollaboratorRO");
        (await ownerClient.DeleteAsync($"/api/v1/invitations/{revoked.Id}")).EnsureSuccessStatusCode();
        var expired = await InviteAsync(ownerClient, houseId, "Tenant");
        await ExpireAsync(expired.Id);

        foreach (var (token, status) in new[] { (accepted.Token, "Accepted"), (declined.Token, "Declined"), (revoked.Token, "Revoked"), (expired.Token, "Expired") })
        {
            var info = await (await CreateClient().GetAsync($"/api/v1/invitations/{token}")).Content.ReadAsJsonAsync<InvitationInfoDto>();
            info!.Status.Should().Be(status);
            info.IsExpired.Should().BeTrue();
            info.Email.Should().BeNull($"a {status} invitation must not disclose the invitee's email to the link holder");
        }
    }

    [Fact]
    public async Task ResendInvitation_AcceptedOrDeclined_Returns400InvitationInvalid()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var (accepted, acceptor) = await InviteNewAccountAsync(ownerClient, houseId);
        (await acceptor.PostAsync($"/api/v1/invitations/{accepted.Token}/accept", null)).EnsureSuccessStatusCode();
        var (declined, decliner) = await InviteNewAccountAsync(ownerClient, houseId, "Tenant");
        (await decliner.PostAsync($"/api/v1/invitations/{declined.Token}/decline", null)).EnsureSuccessStatusCode();

        foreach (var invitation in new[] { accepted, declined })
        {
            var response = await ownerClient.PostAsync($"/api/v1/invitations/{invitation.Id}/resend", null);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.ReadErrorCodeAsync()).Should().Be("invitation_invalid");
        }
    }

    [Fact]
    public async Task ResendInvitation_Expired_RespectsThePendingLimit()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var expired = await InviteAsync(ownerClient, houseId);
        await ExpireAsync(expired.Id);

        // The expired one no longer counts: 20 new pending invitations fit.
        for (var i = 0; i < 20; i++)
            await InviteAsync(ownerClient, houseId, "Tenant");

        var response = await ownerClient.PostAsync($"/api/v1/invitations/{expired.Id}/resend", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "re-activating it would make 21 pending invitations");
        (await response.ReadErrorCodeAsync()).Should().Be("invitation_limit_reached");
        var listed = await (await ownerClient.GetAsync($"/api/v1/houses/{houseId}/invitations")).Content.ReadAsJsonAsync<InvitationDto[]>();
        listed!.Single(i => i.Id == expired.Id).IsExpired.Should().BeTrue("the refused resend changes nothing");
    }

    [Fact]
    public async Task CreateInvitation_BeyondThePendingLimit_Returns400InvitationLimitReached()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        for (var i = 0; i < 20; i++)
            await InviteAsync(ownerClient, houseId, "Tenant");

        var response = await ownerClient.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations",
            new { email = NewInviteeEmail(), role = "Tenant" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).Should().Be("invitation_limit_reached");
    }

    [Fact]
    public async Task ResendInvitation_Expired_WhileAnotherIsPendingForTheSameEmail_Returns409()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var email = NewInviteeEmail();
        var expired = await InviteAsync(ownerClient, houseId, "Tenant", email);
        await ExpireAsync(expired.Id);
        await InviteAsync(ownerClient, houseId, "Tenant", email);

        var response = await ownerClient.PostAsync($"/api/v1/invitations/{expired.Id}/resend", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).Should().Be("invitation_already_pending");
    }

    [Fact]
    public async Task CancelInvitation_NoLongerPending_Returns400WithCode_ExpiredCanBeCancelled()
    {
        var (ownerClient, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var invitation = await InviteAsync(ownerClient, houseId);
        (await ownerClient.DeleteAsync($"/api/v1/invitations/{invitation.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var again = await ownerClient.DeleteAsync($"/api/v1/invitations/{invitation.Id}");
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await again.ReadErrorCodeAsync()).Should().Be("invitation_invalid");

        var expired = await InviteAsync(ownerClient, houseId, "Tenant");
        await ExpireAsync(expired.Id);
        (await ownerClient.DeleteAsync($"/api/v1/invitations/{expired.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Invitations_UnknownHouse_Return404()
    {
        var client = await CreateAuthenticatedClientAsync();
        var unknownHouse = Guid.NewGuid();

        var create = await client.PostAsJsonAsync($"/api/v1/houses/{unknownHouse}/invitations", new CreateInvitationRequestDto("Tenant", NewInviteeEmail()));
        create.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await create.ReadErrorCodeAsync()).Should().Be("not_found");

        var list = await client.GetAsync($"/api/v1/houses/{unknownHouse}/invitations");
        list.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion
}
