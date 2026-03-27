using FluentAssertions;
using HouseFlow.Application.DTOs;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using static HouseFlow.IntegrationTests.TestHelpers;

namespace HouseFlow.IntegrationTests.Devices;

[Collection("Integration")]
public class DevicesTests
{
    private readonly IntegrationTestFixture _fixture;

    public DevicesTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient CreateClient() => _fixture.CreateApiClient();

    private async Task<(HttpClient client, string token, Guid houseId)> CreateAuthenticatedClientWithHouseAsync()
    {
        var client = CreateClient();
        var email = $"test-{Guid.NewGuid()}@example.com";
        var registerRequest = new RegisterRequestDto("Test", "User", email, "Password123!");

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        response.EnsureSuccessStatusCode();

        var authResponse = await response.Content.ReadAsJsonAsync<AuthResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse!.AccessToken);

        // Get the auto-created house
        var housesResponse = await client.GetAsync("/api/v1/houses");
        var houses = await housesResponse.Content.ReadAsJsonAsync<HousesListResponseDto>();
        var houseId = houses!.Houses.First().Id;

        return (client, authResponse.AccessToken, houseId);
    }

    private static CreateDeviceRequestDto CreateValidDeviceRequest(string? name = null) => new(
        Name: name ?? $"Appareil Test {Guid.NewGuid().ToString("N")[..8]}",
        Type: "Chaudiere Gaz",
        Brand: "Viessmann",
        Model: "Vitodens 200",
        InstallDate: DateTime.UtcNow.AddYears(-2)
    );

    #region Create Device Tests

    [Fact]
    public async Task CreateDevice_InOwnHouse_ReturnsDevice()
    {
        // Arrange
        var (client, _, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var request = CreateValidDeviceRequest("Ma Chaudiere");

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var device = await response.Content.ReadAsJsonAsync<DeviceDto>();
        device.Should().NotBeNull();
        device!.Id.Should().NotBeEmpty();
        device.Name.Should().Be("Ma Chaudiere");
        device.Type.Should().Be("Chaudiere Gaz");
        device.Brand.Should().Be("Viessmann");
        device.Model.Should().Be("Vitodens 200");
        device.HouseId.Should().Be(houseId);

        // Verify Location header
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain(device.Id.ToString());
    }

    [Fact]
    public async Task CreateDevice_InOtherUserHouse_Returns403Forbidden()
    {
        // Arrange - Create User 1 with house
        var (client1, _, houseId1) = await CreateAuthenticatedClientWithHouseAsync();

        // Create User 2
        var (client2, _, _) = await CreateAuthenticatedClientWithHouseAsync();

        var request = CreateValidDeviceRequest();

        // Act - User 2 tries to create device in User 1's house
        var response = await client2.PostAsJsonAsync($"/api/v1/houses/{houseId1}/devices", request);

        // Assert - Returns Forbidden (403) - user doesn't have access to this house
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateDevice_WithoutAuth_Returns401Unauthorized()
    {
        // Arrange
        var client = CreateClient(); // No authentication
        var request = CreateValidDeviceRequest();
        var fakeHouseId = Guid.NewGuid();

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/houses/{fakeHouseId}/devices", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateDevice_WithEmptyName_Returns400BadRequest()
    {
        // Arrange
        var (client, _, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var request = new CreateDeviceRequestDto(
            Name: "",
            Type: "Chaudiere",
            Brand: null,
            Model: null,
            InstallDate: null
        );

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateDevice_WithEmptyType_Returns400BadRequest()
    {
        // Arrange
        var (client, _, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var request = new CreateDeviceRequestDto(
            Name: "Test Device",
            Type: "",
            Brand: null,
            Model: null,
            InstallDate: null
        );

        // Act
        var response = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Get Device Tests

    [Fact]
    public async Task GetDevice_AsOwner_ReturnsDeviceWithMaintenanceTypes()
    {
        // Arrange
        var (client, _, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var createRequest = CreateValidDeviceRequest("Mon Appareil Detail");

        var createResponse = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", createRequest);
        var createdDevice = await createResponse.Content.ReadAsJsonAsync<DeviceDto>();

        // Act
        var response = await client.GetAsync($"/api/v1/devices/{createdDevice!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var deviceDetail = await response.Content.ReadAsJsonAsync<DeviceDetailDto>();
        deviceDetail.Should().NotBeNull();
        deviceDetail!.Id.Should().Be(createdDevice.Id);
        deviceDetail.Name.Should().Be("Mon Appareil Detail");
        deviceDetail.MaintenanceTypes.Should().NotBeNull();
        deviceDetail.Score.Should().Be(100); // No maintenance types = 100%
    }

    [Fact]
    public async Task GetDevice_NotOwner_Returns403Forbidden()
    {
        // Arrange - Create device with User 1
        var (client1, _, houseId1) = await CreateAuthenticatedClientWithHouseAsync();
        var createResponse = await client1.PostAsJsonAsync($"/api/v1/houses/{houseId1}/devices", CreateValidDeviceRequest());
        var createdDevice = await createResponse.Content.ReadAsJsonAsync<DeviceDto>();

        // Create User 2
        var (client2, _, _) = await CreateAuthenticatedClientWithHouseAsync();

        // Act - User 2 tries to access User 1's device
        var response = await client2.GetAsync($"/api/v1/devices/{createdDevice!.Id}");

        // Assert - API returns Forbidden for unauthorized access
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetDevices_ForOwnHouse_ReturnsDevicesList()
    {
        // Arrange
        var (client, _, houseId) = await CreateAuthenticatedClientWithHouseAsync();

        // Create 2 devices
        await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", CreateValidDeviceRequest("Device 1"));
        await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", CreateValidDeviceRequest("Device 2"));

        // Act
        var response = await client.GetAsync($"/api/v1/houses/{houseId}/devices");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var devices = await response.Content.ReadAsJsonAsync<IEnumerable<DeviceSummaryDto>>();
        devices.Should().NotBeNull();
        devices.Should().HaveCount(2);
        devices.Should().Contain(d => d.Name == "Device 1");
        devices.Should().Contain(d => d.Name == "Device 2");
    }

    #endregion

    #region Update Device Tests

    [Fact]
    public async Task UpdateDevice_AsOwner_UpdatesSuccessfully()
    {
        // Arrange
        var (client, _, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var createResponse = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", CreateValidDeviceRequest("Old Name"));
        var createdDevice = await createResponse.Content.ReadAsJsonAsync<DeviceDto>();

        var updateRequest = new UpdateDeviceRequestDto(
            Name: "New Name",
            Type: "Pompe a Chaleur",
            Brand: "Daikin",
            Model: "Altherma 3",
            InstallDate: DateTime.UtcNow.AddYears(-1)
        );

        // Act
        var response = await client.PutAsJsonAsync($"/api/v1/devices/{createdDevice!.Id}", updateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedDevice = await response.Content.ReadAsJsonAsync<DeviceDto>();
        updatedDevice.Should().NotBeNull();
        updatedDevice!.Name.Should().Be("New Name");
        updatedDevice.Type.Should().Be("Pompe a Chaleur");
        updatedDevice.Brand.Should().Be("Daikin");
        updatedDevice.Model.Should().Be("Altherma 3");
    }

    [Fact]
    public async Task UpdateDevice_NotOwner_Returns403Forbidden()
    {
        // Arrange - Create device with User 1
        var (client1, _, houseId1) = await CreateAuthenticatedClientWithHouseAsync();
        var createResponse = await client1.PostAsJsonAsync($"/api/v1/houses/{houseId1}/devices", CreateValidDeviceRequest());
        var createdDevice = await createResponse.Content.ReadAsJsonAsync<DeviceDto>();

        // Create User 2
        var (client2, _, _) = await CreateAuthenticatedClientWithHouseAsync();

        var updateRequest = new UpdateDeviceRequestDto(Name: "Hacked Name", null, null, null, null);

        // Act - User 2 tries to update User 1's device
        var response = await client2.PutAsJsonAsync($"/api/v1/devices/{createdDevice!.Id}", updateRequest);

        // Assert - API returns Forbidden for unauthorized access
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #endregion

    #region Delete Device Tests

    [Fact]
    public async Task DeleteDevice_AsOwner_DeletesCascade()
    {
        // Arrange
        var (client, _, houseId) = await CreateAuthenticatedClientWithHouseAsync();
        var createResponse = await client.PostAsJsonAsync($"/api/v1/houses/{houseId}/devices", CreateValidDeviceRequest());
        var createdDevice = await createResponse.Content.ReadAsJsonAsync<DeviceDto>();

        // Act
        var deleteResponse = await client.DeleteAsync($"/api/v1/devices/{createdDevice!.Id}");

        // Assert
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify device no longer exists
        var getResponse = await client.GetAsync($"/api/v1/devices/{createdDevice.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteDevice_NotOwner_Returns403Forbidden()
    {
        // Arrange - Create device with User 1
        var (client1, _, houseId1) = await CreateAuthenticatedClientWithHouseAsync();
        var createResponse = await client1.PostAsJsonAsync($"/api/v1/houses/{houseId1}/devices", CreateValidDeviceRequest());
        var createdDevice = await createResponse.Content.ReadAsJsonAsync<DeviceDto>();

        // Create User 2
        var (client2, _, _) = await CreateAuthenticatedClientWithHouseAsync();

        // Act - User 2 tries to delete User 1's device
        var response = await client2.DeleteAsync($"/api/v1/devices/{createdDevice!.Id}");

        // Assert - API returns Forbidden for unauthorized access
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Verify device still exists for User 1
        var getResponse = await client1.GetAsync($"/api/v1/devices/{createdDevice.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion
}
