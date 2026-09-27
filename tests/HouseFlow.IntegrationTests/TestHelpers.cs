using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HouseFlow.IntegrationTests;

/// <summary>
/// Provides shared utilities for integration tests.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// JSON serializer options configured to match the API's configuration.
    /// Includes JsonStringEnumConverter to properly deserialize enum values.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Reads JSON content from HTTP response with enum support.
    /// </summary>
    public static async Task<T?> ReadAsJsonAsync<T>(this HttpContent content)
    {
        return await content.ReadFromJsonAsync<T>(JsonOptions);
    }

    /// <summary>A unique invitee email (invitations require one since the UX redesign).</summary>
    public static string NewInviteeEmail() => $"invitee-{Guid.NewGuid():N}@example.com";

    /// <summary>
    /// Creates a house for the authenticated client (registration no longer creates « Ma maison »:
    /// the first house comes from onboarding, P05) and returns its id.
    /// </summary>
    public static async Task<Guid> CreateHouseAsync(this HttpClient client, string name = "Ma maison")
    {
        var response = await client.PostAsJsonAsync("/api/v1/houses",
            new HouseFlow.Contracts.CreateHouseRequest(address: null, city: null, name: name, zipCode: null));
        response.EnsureSuccessStatusCode();
        var house = await response.Content.ReadAsJsonAsync<HouseFlow.Application.DTOs.HouseDto>();
        return house!.Id;
    }

    /// <summary>Reads the machine <c>code</c> of a ProblemDetails error response (null if absent).</summary>
    public static async Task<string?> ReadErrorCodeAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body)) return null;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
