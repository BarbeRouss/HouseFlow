using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using HouseFlow.Application.DTOs;
using HouseFlow.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Houses;

/// <summary>
/// House colour (<c>House.colorKey</c>): rotation at creation, explicit choice, update, exposure on the
/// list / detail / public invitation, and the migration backfill.
/// </summary>
[Collection("Integration")]
public class HouseColorTests
{
    private readonly IntegrationTestFixture _fixture;

    public HouseColorTests(IntegrationTestFixture fixture) => _fixture = fixture;

    private async Task<(HttpClient client, Guid userId)> RegisterAsync()
    {
        var client = _fixture.CreateApiClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequestDto(
            email: $"color-{Guid.NewGuid():N}@example.com", firstName: "Color", lastName: "Test",
            password: "Password123!", consentAccepted: true));
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return (client, auth.User.Id);
    }

    private static async Task<HouseDto> CreateHouseAsync(HttpClient client, string name, object? colorKey = null)
    {
        object body = colorKey is null ? new { name } : new { name, colorKey };
        var response = await client.PostAsJsonAsync("/api/v1/houses", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadAsJsonAsync<HouseDto>())!;
    }

    private static async Task<HousesListResponseDto> GetHousesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<HousesListResponseDto>("/api/v1/houses", JsonOptions))!;

    [Fact]
    public async Task CreateHouse_WithoutColor_RotatesThroughThePalettePerOwner()
    {
        var (client, _) = await RegisterAsync();
        (await GetHousesAsync(client)).NextColorKey.Should().Be("indigo");

        var created = new List<string>();
        for (var i = 0; i < 7; i++)
            created.Add((await CreateHouseAsync(client, $"Maison {i}")).ColorKey);

        created.Should().Equal("indigo", "orange", "green", "sky", "yellow", "pink", "indigo");
        (await GetHousesAsync(client)).NextColorKey.Should().Be("orange");

        // Another owner starts from the first colour again.
        var (other, _) = await RegisterAsync();
        (await CreateHouseAsync(other, "Autre")).ColorKey.Should().Be("indigo");
    }

    [Fact]
    public async Task CreateHouse_ReusesTheColourFreedByADeletion()
    {
        var (client, _) = await RegisterAsync();
        await CreateHouseAsync(client, "A");
        var b = await CreateHouseAsync(client, "B");
        await CreateHouseAsync(client, "C");

        (await client.DeleteAsync($"/api/v1/houses/{b.Id}")).EnsureSuccessStatusCode();

        (await GetHousesAsync(client)).NextColorKey.Should().Be("orange");
        (await CreateHouseAsync(client, "D")).ColorKey.Should().Be("orange");
    }

    [Fact]
    public async Task CreateHouse_WithExplicitColor_UsesIt()
    {
        var (client, _) = await RegisterAsync();

        var house = await CreateHouseAsync(client, "Rose", "pink");

        house.ColorKey.Should().Be("pink");
        (await GetHousesAsync(client)).NextColorKey.Should().Be("indigo");
    }

    [Theory]
    [InlineData("\"purple\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    public async Task CreateHouse_WithUnknownColor_Returns400(string colorJson)
    {
        var (client, _) = await RegisterAsync();

        var response = await client.PostAsync("/api/v1/houses", new StringContent(
            $$"""{"name":"Maison","colorKey":{{colorJson}}}""", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateHouse_ChangesTheColor_AndOmittingItKeepsIt()
    {
        var (client, _) = await RegisterAsync();
        var house = await CreateHouseAsync(client, "Maison");

        var recolor = await client.PutAsJsonAsync($"/api/v1/houses/{house.Id}", new { colorKey = "sky" });
        recolor.StatusCode.Should().Be(HttpStatusCode.OK);
        (await recolor.Content.ReadAsJsonAsync<HouseDto>())!.ColorKey.Should().Be("sky");

        var rename = await client.PutAsJsonAsync($"/api/v1/houses/{house.Id}", new { name = "Renommée" });
        rename.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await client.GetFromJsonAsync<HouseDetailDto>($"/api/v1/houses/{house.Id}", JsonOptions);
        detail!.ColorKey.Should().Be("sky");
        detail.Name.Should().Be("Renommée");
        (await GetHousesAsync(client)).Houses.Single().ColorKey.Should().Be("sky");
    }

    [Fact]
    public async Task UpdateHouse_WithUnknownColor_Returns400()
    {
        var (client, _) = await RegisterAsync();
        var house = await CreateHouseAsync(client, "Maison");

        var response = await client.PutAsJsonAsync($"/api/v1/houses/{house.Id}", new { colorKey = "purple" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvitationInfo_ExposesTheHouseColor()
    {
        var (owner, _) = await RegisterAsync();
        await CreateHouseAsync(owner, "Première");
        var house = await CreateHouseAsync(owner, "Seconde"); // orange

        var create = await owner.PostAsJsonAsync($"/api/v1/houses/{house.Id}/invitations",
            new CreateInvitationRequestDto("CollaboratorRO", NewInviteeEmail()));
        create.EnsureSuccessStatusCode();
        var invitation = await create.Content.ReadAsJsonAsync<InvitationDto>();

        var info = await _fixture.CreateApiClient()
            .GetFromJsonAsync<InvitationInfoDto>($"/api/v1/invitations/{invitation!.Token}", JsonOptions);

        info!.HouseColorKey.Should().Be("orange");
    }

    [Fact]
    public async Task MigrationBackfill_AssignsTheRotationPerOwnerInCreationOrder()
    {
        var (client1, user1) = await RegisterAsync();
        var (client2, user2) = await RegisterAsync();
        var h1 = new List<Guid>();
        for (var i = 0; i < 7; i++) h1.Add((await CreateHouseAsync(client1, $"U1-{i}", "pink")).Id);
        var h2 = (await CreateHouseAsync(client2, "U2-0", "sky")).Id;

        await using (var db = await _fixture.CreateDbContextAsync())
        {
            // Pre-migration state: every row carries the column default; creation order = insertion order.
            var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < h1.Count; i++)
            {
                var id = h1[i];
                var createdAt = start.AddDays(i);
                await db.Houses.Where(h => h.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(h => h.ColorKey, "indigo")
                    .SetProperty(h => h.CreatedAt, createdAt));
            }
            await db.Houses.Where(h => h.Id == h2).ExecuteUpdateAsync(s => s.SetProperty(h => h.ColorKey, "indigo"));

            await db.Database.ExecuteSqlRawAsync(AddHouseColorKey.BackfillSql);
        }

        await using var check = await _fixture.CreateDbContextAsync();
        var colours = await check.Houses.Where(h => h.UserId == user1)
            .OrderBy(h => h.CreatedAt).Select(h => h.ColorKey).ToListAsync();
        colours.Should().Equal("indigo", "orange", "green", "sky", "yellow", "pink", "indigo");
        (await check.Houses.Where(h => h.UserId == user2).Select(h => h.ColorKey).SingleAsync()).Should().Be("indigo");
    }
}
