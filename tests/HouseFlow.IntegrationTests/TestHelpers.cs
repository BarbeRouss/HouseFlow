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
}
