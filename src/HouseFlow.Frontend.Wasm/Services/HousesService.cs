using System.Net.Http.Json;
using HouseFlow.Frontend.Wasm.Models;

namespace HouseFlow.Frontend.Wasm.Services;

public sealed class HousesService(HttpClient http)
{
    /// <summary>GET /houses — dashboard list with progression scores.</summary>
    public async Task<HousesListResponse?> GetHousesAsync()
        => await http.GetFromJsonAsync<HousesListResponse>("houses");
}
