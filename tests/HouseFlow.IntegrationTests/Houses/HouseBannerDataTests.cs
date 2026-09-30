using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseFlow.Application.DTOs;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Houses;

/// <summary>
/// Data behind the house banners (specs/ux): device chips (<c>deviceTypes</c>, one per device in creation
/// order) and the « Partagée » badge (<c>membersCount</c>) of the C4 cards, and the device chips of the public
/// invitation (P04, <c>houseDeviceTypes</c>, only while the invitation is usable).
/// </summary>
[Collection("Integration")]
public class HouseBannerDataTests
{
    private readonly IntegrationTestFixture _fixture;

    public HouseBannerDataTests(IntegrationTestFixture fixture) => _fixture = fixture;

    private async Task<HttpClient> RegisterAsync(string? email = null)
    {
        var client = _fixture.CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequestDto(
            email: email ?? $"banner-{Guid.NewGuid():N}@example.com", firstName: "Banner", lastName: "Test",
            password: "Password123!", consentAccepted: true));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }

    private static async Task AddDeviceAsync(HttpClient client, Guid houseId, string name, string type)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", new { name, type });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<InvitationDto> InviteAsync(HttpClient owner, Guid houseId, string email)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/houses/{houseId}/invitations",
            new CreateInvitationRequestDto("CollaboratorRW", email));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsJsonAsync<InvitationDto>())!;
    }

    private static async Task<HouseSummaryDto> GetSummaryAsync(HttpClient client, Guid houseId) =>
        (await client.GetFromJsonAsync<HousesListResponseDto>("/api/v1/houses", JsonOptions))!
            .Houses.Single(h => h.Id == houseId);

    private static async Task<HouseDetailDto> GetDetailAsync(HttpClient client, Guid houseId) =>
        (await client.GetFromJsonAsync<HouseDetailDto>($"/api/v1/houses/{houseId}", JsonOptions))!;

    [Fact]
    public async Task DeviceTypes_OnePerDeviceInCreationOrder_OnListAndDetail()
    {
        var client = await RegisterAsync();
        var houseId = await client.CreateHouseAsync("Maison de Namur");
        var empty = await client.CreateHouseAsync("Vide");

        await AddDeviceAsync(client, houseId, "Chaudière", "Chaudière Gaz");
        await AddDeviceAsync(client, houseId, "Salon", "Détecteur de fumée");
        await AddDeviceAsync(client, houseId, "Étage", "Détecteur de fumée");
        await AddDeviceAsync(client, houseId, "VMC", "VMC");

        string[] expected = ["Chaudière Gaz", "Détecteur de fumée", "Détecteur de fumée", "VMC"];
        var summary = await GetSummaryAsync(client, houseId);
        summary.DeviceTypes.Should().Equal(expected);
        summary.DevicesCount.Should().Be(expected.Length);
        (await GetDetailAsync(client, houseId)).DeviceTypes.Should().Equal(expected);

        (await GetSummaryAsync(client, empty)).DeviceTypes.Should().BeEmpty();
        (await GetDetailAsync(client, empty)).DeviceTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task MembersCount_CountsOwnerAndAcceptedMembers_NotPendingInvitations()
    {
        var owner = await RegisterAsync();
        var houseId = await owner.CreateHouseAsync("Chalet de Spa");

        (await GetSummaryAsync(owner, houseId)).MembersCount.Should().Be(1);
        (await GetDetailAsync(owner, houseId)).MembersCount.Should().Be(1);

        var inviteeEmail = NewInviteeEmail();
        var invitation = await InviteAsync(owner, houseId, inviteeEmail);
        await InviteAsync(owner, houseId, NewInviteeEmail()); // stays pending
        (await GetSummaryAsync(owner, houseId)).MembersCount.Should().Be(1);

        var member = await RegisterAsync(inviteeEmail);
        (await member.PostAsync($"/api/v1/invitations/{invitation.Token}/accept", null)).EnsureSuccessStatusCode();

        (await GetSummaryAsync(owner, houseId)).MembersCount.Should().Be(2);
        (await GetDetailAsync(owner, houseId)).MembersCount.Should().Be(2);
        (await GetSummaryAsync(member, houseId)).MembersCount.Should().Be(2);
        (await GetDetailAsync(member, houseId)).MembersCount.Should().Be(2);
    }

    [Fact]
    public async Task InvitationInfo_ExposesDeviceTypesOnly_WhileUsable()
    {
        var owner = await RegisterAsync();
        var houseId = await owner.CreateHouseAsync("Chalet de Spa");
        await AddDeviceAsync(owner, houseId, "Poêle du salon Jøtul F100", "Poêle à bois");
        await AddDeviceAsync(owner, houseId, "Ballon", "Chauffe-eau");
        var invitation = await InviteAsync(owner, houseId, NewInviteeEmail());
        var anonymous = _fixture.CreateApiClient();

        var response = await anonymous.GetAsync($"/api/v1/invitations/{invitation.Token}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("Jøtul", "only device types are public, never device names");
        (await response.Content.ReadAsJsonAsync<InvitationInfoDto>())!
            .HouseDeviceTypes.Should().Equal("Poêle à bois", "Chauffe-eau");

        (await owner.DeleteAsync($"/api/v1/invitations/{invitation.Id}")).EnsureSuccessStatusCode();

        var cancelled = await anonymous.GetFromJsonAsync<InvitationInfoDto>(
            $"/api/v1/invitations/{invitation.Token}", JsonOptions);
        cancelled!.IsExpired.Should().BeTrue();
        cancelled.HouseDeviceTypes.Should().BeEmpty();
    }
}
