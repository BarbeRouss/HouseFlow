using FluentAssertions;
using HouseFlow.Application.DTOs;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Houses;

public class HousesTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HousesTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    private async Task<(HttpClient client, string token)> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var email = $"test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto("Test", "User", email, "Password123!");

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        response.EnsureSuccessStatusCode();

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        return (client, authResponse.AccessToken);
    }

    private static CreateHouseRequestDto CreateValidHouseRequest(string? name = null) => new(
        Name: name ?? $"Maison Test {Guid.NewGuid().ToString("N")[..8]}",
        Address: "123 Rue de Test",
        ZipCode: "75001",
        City: "Paris"
    );

    #region Create House Tests

    [Fact]
    public async Task CreateHouse_WithValidData_ReturnsHouse()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedClientAsync();
        var request = CreateValidHouseRequest("Ma Maison");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/houses", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var house = await response.Content.ReadAsJsonAsync<HouseDto>();
        house.Should().NotBeNull();
        house!.Id.Should().NotBeEmpty();
        house.Name.Should().Be("Ma Maison");
        house.Address.Should().Be("123 Rue de Test");
        house.ZipCode.Should().Be("75001");
        house.City.Should().Be("Paris");

        // Verify Location header
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain(house.Id.ToString());
    }

    [Fact]
    public async Task CreateHouse_WithoutAuth_Returns401Unauthorized()
    {
        // Arrange
        var client = CreateClient(); // No authentication
        var request = CreateValidHouseRequest();

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/houses", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateHouse_WithEmptyName_Returns400BadRequest()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedClientAsync();
        var request = new CreateHouseRequestDto(
            Name: "",
            Address: null,
            ZipCode: null,
            City: null
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/houses", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Get Houses Tests

    [Fact]
    public async Task GetHouses_ReturnsOnlyUserHouses()
    {
        // Arrange - Create two users with their own houses
        var (client1, _) = await CreateAuthenticatedClientAsync();
        var (client2, _) = await CreateAuthenticatedClientAsync();

        // User 1 creates 2 additional houses (note: a default "Ma maison" is auto-created on registration)
        await client1.PostAsJsonAsync("/api/v1/houses", CreateValidHouseRequest("User1 House1"));
        await client1.PostAsJsonAsync("/api/v1/houses", CreateValidHouseRequest("User1 House2"));

        // User 2 creates 1 house
        await client2.PostAsJsonAsync("/api/v1/houses", CreateValidHouseRequest("User2 House1"));

        // Act - User 1 gets their houses
        var response = await client1.GetAsync("/api/v1/houses");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var housesResponse = await response.Content.ReadAsJsonAsync<HousesListResponseDto>();
        housesResponse.Should().NotBeNull();

        // User 1 should see 3 houses (auto-created "Ma maison" + 2 created manually)
        var houses = housesResponse!.Houses.ToList();
        houses.Should().HaveCount(3);

        // User 1 should NOT see User 2's houses
        houses.Should().NotContain(h => h.Name.StartsWith("User2"));

        // User 1 should see their created houses
        houses.Should().Contain(h => h.Name == "User1 House1");
        houses.Should().Contain(h => h.Name == "User1 House2");
    }

    [Fact]
    public async Task GetHouse_OwnerAccess_ReturnsHouseWithDevices()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedClientAsync();
        var createRequest = CreateValidHouseRequest("Ma Maison Detaillee");

        var createResponse = await client.PostAsJsonAsync("/api/v1/houses", createRequest);
        var createdHouse = await createResponse.Content.ReadAsJsonAsync<HouseDto>();

        // Act
        var response = await client.GetAsync($"/api/v1/houses/{createdHouse!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var houseDetail = await response.Content.ReadAsJsonAsync<HouseDetailDto>();
        houseDetail.Should().NotBeNull();
        houseDetail!.Id.Should().Be(createdHouse.Id);
        houseDetail.Name.Should().Be("Ma Maison Detaillee");
        houseDetail.Devices.Should().NotBeNull();
        houseDetail.Score.Should().Be(100); // No devices = 100% score
    }

    [Fact]
    public async Task GetHouse_NotOwner_Returns404NotFound()
    {
        // Arrange - Create house with User 1
        var (client1, _) = await CreateAuthenticatedClientAsync();
        var createResponse = await client1.PostAsJsonAsync("/api/v1/houses", CreateValidHouseRequest());
        var createdHouse = await createResponse.Content.ReadAsJsonAsync<HouseDto>();

        // Create User 2
        var (client2, _) = await CreateAuthenticatedClientAsync();

        // Act - User 2 tries to access User 1's house
        var response = await client2.GetAsync($"/api/v1/houses/{createdHouse!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Update House Tests

    [Fact]
    public async Task UpdateHouse_AsOwner_UpdatesSuccessfully()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/v1/houses", CreateValidHouseRequest("Old Name"));
        var createdHouse = await createResponse.Content.ReadAsJsonAsync<HouseDto>();

        var updateRequest = new UpdateHouseRequestDto(
            Name: "New Name",
            Address: "456 New Address",
            ZipCode: "69001",
            City: "Lyon"
        );

        // Act
        var response = await client.PutAsJsonAsync($"/api/v1/houses/{createdHouse!.Id}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedHouse = await response.Content.ReadAsJsonAsync<HouseDto>();
        updatedHouse.Should().NotBeNull();
        updatedHouse!.Name.Should().Be("New Name");
        updatedHouse.Address.Should().Be("456 New Address");
        updatedHouse.ZipCode.Should().Be("69001");
        updatedHouse.City.Should().Be("Lyon");
    }

    [Fact]
    public async Task UpdateHouse_NotOwner_Returns403Forbidden()
    {
        // Arrange - Create house with User 1
        var (client1, _) = await CreateAuthenticatedClientAsync();
        var createResponse = await client1.PostAsJsonAsync("/api/v1/houses", CreateValidHouseRequest());
        var createdHouse = await createResponse.Content.ReadAsJsonAsync<HouseDto>();

        // Create User 2
        var (client2, _) = await CreateAuthenticatedClientAsync();

        var updateRequest = new UpdateHouseRequestDto(Name: "Hacked Name", null, null, null);

        // Act - User 2 tries to update User 1's house
        var response = await client2.PutAsJsonAsync($"/api/v1/houses/{createdHouse!.Id}", updateRequest);

        // Assert - User 2 has no access, returns 403
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Delete House Tests

    [Fact]
    public async Task DeleteHouse_AsOwner_DeletesCascade()
    {
        // Arrange
        var (client, _) = await CreateAuthenticatedClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/v1/houses", CreateValidHouseRequest());
        var createdHouse = await createResponse.Content.ReadAsJsonAsync<HouseDto>();

        // Act
        var deleteResponse = await client.DeleteAsync($"/api/v1/houses/{createdHouse!.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify house no longer exists
        var getResponse = await client.GetAsync($"/api/v1/houses/{createdHouse.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteHouse_NotOwner_Returns403Forbidden()
    {
        // Arrange - Create house with User 1
        var (client1, _) = await CreateAuthenticatedClientAsync();
        var createResponse = await client1.PostAsJsonAsync("/api/v1/houses", CreateValidHouseRequest());
        var createdHouse = await createResponse.Content.ReadAsJsonAsync<HouseDto>();

        // Create User 2
        var (client2, _) = await CreateAuthenticatedClientAsync();

        // Act - User 2 tries to delete User 1's house
        var response = await client2.DeleteAsync($"/api/v1/houses/{createdHouse!.Id}");

        // Assert - User 2 has no access, returns 403
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Verify house still exists for User 1
        var getResponse = await client1.GetAsync($"/api/v1/houses/{createdHouse.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}
