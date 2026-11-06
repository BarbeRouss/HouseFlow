using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseFlow.Application.Common;
using HouseFlow.Application.DTOs;
using Microsoft.EntityFrameworkCore;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Authentication;

/// <summary>
/// Session hardening on real PostgreSQL: concurrency of the refresh-token rotation (xmin + the
/// unique index, which the in-memory provider of the unit tests enforces neither), logout of the
/// whole token family, and case-insensitive emails.
/// </summary>
[Collection("Integration")]
public class SessionHardeningTests
{
    private const string Password = "Password123!";
    private readonly IntegrationTestFixture _fixture;

    public SessionHardeningTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    // ---------------------------------------------------------------- helpers

    private static string NewEmail() => $"session-{Guid.NewGuid():N}@example.com";

    private static string CookieOf(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("refreshToken="))
            .Split(';')[0]["refreshToken=".Length..];

    private async Task<(HttpClient Client, string AccessToken, string Cookie)> RegisterAsync(string email)
    {
        var client = _fixture.CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequestDto(email: email, firstName: "Session", lastName: "User", password: Password, consentAccepted: true));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        return (client, auth!.AccessToken, CookieOf(response));
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"refreshToken={cookie}");
        return client.SendAsync(request);
    }

    private async Task<int> ActiveTokensInFamilyOfAsync(string cookie)
    {
        await using var db = await _fixture.CreateDbContextAsync();
        // The Set-Cookie value is URL-encoded (base64 '+', '/', '='): the server hashes the decoded token.
        var hash = TokenHasher.Hash(Uri.UnescapeDataString(cookie));
        var familyId = await db.RefreshTokens.Where(t => t.Token == hash).Select(t => t.FamilyId).SingleAsync();
        return await db.RefreshTokens.CountAsync(t => t.FamilyId == familyId && t.RevokedAt == null);
    }

    // ---------------------------------------------------------------- rotation races

    [Fact]
    public async Task Refresh_ConcurrentPresentationsOfAnActiveToken_OpenASingleChain()
    {
        // Two tabs (or a thief and the victim) present the same active token at the same instant.
        // Without a concurrency check both rotated it: two independent live chains in the family,
        // out of reach of reuse detection. Now one rotates it, the others get the grace sibling.
        var (client, _, t0) = await RegisterAsync(NewEmail());

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RefreshAsync(client, t0)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        var issued = responses.Select(CookieOf).Distinct().ToList();
        issued.Should().HaveCountLessThanOrEqualTo(2, "the replacement and, at most, the single grace sibling");
        (await ActiveTokensInFamilyOfAsync(t0)).Should().Be(issued.Count);
        foreach (var token in issued)
            (await RefreshAsync(client, token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_ConcurrentReplaysOfARotatedToken_AllGetTheSameSibling()
    {
        // The unique-index / xmin race of the grace path: every parallel replay of the rotated
        // parent must receive the one deterministic sibling, none be taken for a theft.
        var (client, _, t0) = await RegisterAsync(NewEmail());
        (await RefreshAsync(client, t0)).StatusCode.Should().Be(HttpStatusCode.OK);

        var replays = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RefreshAsync(client, t0)));

        replays.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        replays.Select(CookieOf).Distinct().Should().ContainSingle();
        (await ActiveTokensInFamilyOfAsync(t0)).Should().Be(2, "the replacement and the sibling, nothing more");
    }

    // ---------------------------------------------------------------- logout

    [Fact]
    public async Task Logout_RevokesTheWholeFamily_EvenFromACookieRotatedByALostResponse()
    {
        var email = NewEmail();
        var (client, accessToken, t0) = await RegisterAsync(email);
        var otherDevice = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequestDto(email: email, password: Password, rememberMe: true));
        var lostReplacement = CookieOf(await RefreshAsync(client, t0));
        var sibling = CookieOf(await RefreshAsync(client, t0));

        // Both refresh responses were lost: the browser still sends t0 when the user logs out.
        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        logout.Headers.Add("Cookie", $"refreshToken={t0}");
        (await client.SendAsync(logout)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RefreshAsync(client, lostReplacement)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(client, sibling)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(client, CookieOf(otherDevice))).StatusCode
            .Should().Be(HttpStatusCode.OK, "other devices keep their session");
    }

    // ---------------------------------------------------------------- emails

    [Fact]
    public async Task Login_IgnoresTheCaseAndSurroundingSpacesOfTheEmail()
    {
        var email = NewEmail();
        var (client, _, _) = await RegisterAsync("  " + email.ToUpperInvariant() + " ");

        foreach (var typed in new[] { email, email.ToUpperInvariant(), char.ToUpperInvariant(email[0]) + email[1..] })
        {
            var login = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequestDto(email: typed, password: Password, rememberMe: false));
            login.StatusCode.Should().Be(HttpStatusCode.OK, $"« {typed} » is the same account");
            (await login.Content.ReadAsJsonAsync<AuthResponseDto>())!.User.Email.Should().Be(email);
        }

        var duplicate = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequestDto(email: email.ToUpperInvariant(), firstName: "Dup", lastName: "User", password: Password, consentAccepted: true));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.ReadErrorCodeAsync()).Should().Be(ErrorCodes.EmailTaken);
    }

    [Fact]
    public async Task InvitationTypedInUpperCase_CanBeUsedAndLoggedInWithAnyCase()
    {
        // The inviter types the address as they like: the invitee registers through the link
        // (email locked to the invitation's) and later logs in with a lower-case address.
        var (owner, ownerToken, _) = await RegisterAsync(NewEmail());
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var houseId = await owner.CreateHouseAsync();
        var inviteeEmail = NewInviteeEmail();
        var created = await owner.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations",
            new CreateInvitationRequestDto("Tenant", inviteeEmail.ToUpperInvariant()));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var invitation = await created.Content.ReadAsJsonAsync<InvitationDto>();
        invitation!.Email.Should().Be(inviteeEmail);

        var invitee = _fixture.CreateApiClient();
        var register = await invitee.PostAsJsonAsync($"/api/v1/auth/register?invitationToken={invitation.Token}",
            new RegisterRequestDto(email: inviteeEmail.ToUpperInvariant(), firstName: "In", lastName: "Vitee", password: Password, consentAccepted: true));
        register.StatusCode.Should().Be(HttpStatusCode.OK);
        (await register.Content.ReadAsJsonAsync<AuthResponseDto>())!.JoinedHouseId.Should().Be(houseId);

        var login = await invitee.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequestDto(email: inviteeEmail, password: Password, rememberMe: false));
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        // Same address in another case: still the same person, already pending/member.
        var again = await owner.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations",
            new CreateInvitationRequestDto("Tenant", inviteeEmail.ToUpperInvariant()));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.ReadErrorCodeAsync()).Should().Be(ErrorCodes.AlreadyMember);
    }

    [Fact]
    public async Task RegisterWithInvitation_RacingAnExistingAccountAccept_ConsumesTheInvitationOnce()
    {
        // Register-with-invitation and accept run in the same Serializable transaction: when a
        // (legacy, email-less) invitation is raced by a new registration and an existing account,
        // at most one of them joins.
        var (owner, ownerToken, _) = await RegisterAsync(NewEmail());
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var houseId = await owner.CreateHouseAsync();
        var created = await owner.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations",
            new CreateInvitationRequestDto("Tenant", NewInviteeEmail()));
        var invitation = await created.Content.ReadAsJsonAsync<InvitationDto>();

        // Invitations predating the email field carry none: anyone holding the link may use it.
        await using (var db = await _fixture.CreateDbContextAsync())
        {
            await db.Invitations.Where(i => i.Id == invitation!.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Email, (string?)null));
        }

        var (existing, existingToken, _) = await RegisterAsync(NewEmail());
        existing.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", existingToken);
        var newcomer = _fixture.CreateApiClient();

        var results = await Task.WhenAll(
            existing.PostAsync($"/api/v1/invitations/{invitation!.Token}/accept", null),
            newcomer.PostAsJsonAsync($"/api/v1/auth/register?invitationToken={invitation.Token}",
                new RegisterRequestDto(email: NewEmail(), firstName: "New", lastName: "Comer", password: Password, consentAccepted: true)));

        results.Count(r => r.IsSuccessStatusCode).Should().Be(1, "an invitation is single-use");
        await using var check = await _fixture.CreateDbContextAsync();
        (await check.HouseMembers.CountAsync(m => m.HouseId == houseId && m.Role == Core.Enums.HouseRole.Tenant))
            .Should().Be(1);
    }
}
