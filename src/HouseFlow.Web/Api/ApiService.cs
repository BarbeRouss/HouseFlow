using System.Net;
using System.Net.Http.Json;

namespace HouseFlow.Web.Api;

/// <summary>Typed wrapper over the HouseFlow REST API (all paths under /api/v1).</summary>
public sealed class ApiService
{
    private readonly HttpClient _http;

    public ApiService(HttpClient http) => _http = http;

    // ---------- Auth ----------
    public Task<AuthResponse> RegisterAsync(RegisterRequest req, string? invitationToken = null)
    {
        var url = string.IsNullOrEmpty(invitationToken)
            ? "/api/v1/auth/register"
            : $"/api/v1/auth/register?invitationToken={Uri.EscapeDataString(invitationToken)}";
        return PostAsync<AuthResponse>(url, req);
    }

    public Task<AuthResponse> LoginAsync(LoginRequest req) => PostAsync<AuthResponse>("/api/v1/auth/login", req);

    public Task<AuthResponse> RefreshAsync() => PostAsync<AuthResponse>("/api/v1/auth/refresh", null);

    public async Task LogoutAsync()
    {
        try { using var _ = await _http.PostAsync("/api/v1/auth/logout", null); }
        catch { /* best-effort */ }
    }

    // ---------- Houses ----------
    public Task<HousesListResponse> GetHousesAsync() => GetAsync<HousesListResponse>("/api/v1/houses");
    public Task<HouseDetail> GetHouseAsync(string id) => GetAsync<HouseDetail>($"/api/v1/houses/{id}");
    public Task<HouseDto> CreateHouseAsync(CreateHouseRequest req) => PostAsync<HouseDto>("/api/v1/houses", req);
    public Task UpdateHouseAsync(string id, CreateHouseRequest req) => SendVoidAsync(HttpMethod.Put, $"/api/v1/houses/{id}", req);
    public Task DeleteHouseAsync(string id) => SendVoidAsync(HttpMethod.Delete, $"/api/v1/houses/{id}");

    // ---------- Devices ----------
    public Task<List<DeviceSummary>> GetDevicesAsync(string houseId) => GetAsync<List<DeviceSummary>>($"/api/v1/houses/{houseId}/devices");
    public Task<DeviceDetail> GetDeviceAsync(string id) => GetAsync<DeviceDetail>($"/api/v1/devices/{id}");
    public Task<DeviceDto> CreateDeviceAsync(string houseId, CreateDeviceRequest req) => PostAsync<DeviceDto>($"/api/v1/houses/{houseId}/devices", req);
    public Task UpdateDeviceAsync(string id, CreateDeviceRequest req) => SendVoidAsync(HttpMethod.Put, $"/api/v1/devices/{id}", req);
    public Task DeleteDeviceAsync(string id) => SendVoidAsync(HttpMethod.Delete, $"/api/v1/devices/{id}");

    // ---------- Maintenance ----------
    public Task<MaintenanceTypeDto> CreateMaintenanceTypeAsync(string deviceId, CreateMaintenanceTypeRequest req) =>
        PostAsync<MaintenanceTypeDto>($"/api/v1/devices/{deviceId}/maintenance-types", req);
    public Task<MaintenanceHistoryResponse> GetMaintenanceHistoryAsync(string deviceId) =>
        GetAsync<MaintenanceHistoryResponse>($"/api/v1/devices/{deviceId}/maintenance-history");
    public Task<MaintenanceInstance> LogMaintenanceAsync(string maintenanceTypeId, LogMaintenanceRequest req) =>
        PostAsync<MaintenanceInstance>($"/api/v1/maintenance-types/{maintenanceTypeId}/instances", req);
    public Task<UpcomingTasksResponse> GetUpcomingTasksAsync(int? limit = null) =>
        GetAsync<UpcomingTasksResponse>($"/api/v1/upcoming-tasks{(limit.HasValue ? $"?limit={limit}" : "")}");

    // ---------- Members / Invitations ----------
    public Task<List<HouseMember>> GetMembersAsync(string houseId) => GetAsync<List<HouseMember>>($"/api/v1/houses/{houseId}/members");
    public Task<List<Invitation>> GetInvitationsAsync(string houseId) => GetAsync<List<Invitation>>($"/api/v1/houses/{houseId}/invitations");
    public Task<Invitation> CreateInvitationAsync(string houseId, CreateInvitationRequest req) =>
        PostAsync<Invitation>($"/api/v1/houses/{houseId}/invitations", req);
    public Task<InvitationInfo> GetInvitationInfoAsync(string token) => GetAsync<InvitationInfo>($"/api/v1/invitations/{token}");
    public Task<AcceptInvitationResponse> AcceptInvitationAsync(string token) =>
        PostAsync<AcceptInvitationResponse>($"/api/v1/invitations/{token}/accept", null);
    public Task RevokeInvitationAsync(string invitationId) => SendVoidAsync(HttpMethod.Delete, $"/api/v1/invitations/{invitationId}");
    public Task UpdateMemberRoleAsync(string memberId, string role) => SendVoidAsync(HttpMethod.Put, $"/api/v1/members/{memberId}/role", new { role });
    public Task UpdateMemberPermissionsAsync(string memberId, bool? canLogMaintenance, bool? canViewCosts) =>
        SendVoidAsync(HttpMethod.Put, $"/api/v1/members/{memberId}/permissions", new { canLogMaintenance, canViewCosts });
    public Task RemoveMemberAsync(string memberId) => SendVoidAsync(HttpMethod.Delete, $"/api/v1/members/{memberId}");

    // ---------- Settings / API keys ----------
    public Task<UserSettings> GetUserSettingsAsync() => GetAsync<UserSettings>("/api/v1/users/settings");
    public Task<UserSettings> UpdateUserSettingsAsync(UserSettings settings) => PutAsync<UserSettings>("/api/v1/users/settings", settings);
    public Task<List<ApiKey>> GetApiKeysAsync() => GetAsync<List<ApiKey>>("/api/v1/users/api-keys");
    public Task<CreateApiKeyResponse> CreateApiKeyAsync(CreateApiKeyRequest req) => PostAsync<CreateApiKeyResponse>("/api/v1/users/api-keys", req);
    public Task RevokeApiKeyAsync(string id) => SendVoidAsync(HttpMethod.Delete, $"/api/v1/users/api-keys/{id}");

    // ---------- Admin ----------
    public Task<AdminStats> GetAdminStatsAsync() => GetAsync<AdminStats>("/api/v1/admin/stats");
    public Task<AdminUsersPage> GetAdminUsersAsync(string? search = null, int page = 1, int pageSize = 20)
    {
        var query = $"?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search.Trim())}";
        return GetAsync<AdminUsersPage>($"/api/v1/admin/users{query}");
    }
    public Task<AdminUser> SetUserAdminAsync(string userId, bool isAdmin) =>
        PutAsync<AdminUser>($"/api/v1/admin/users/{userId}/admin", new { isAdmin });

    // ---------- transport ----------
    private async Task<T> GetAsync<T>(string url)
    {
        using var resp = await _http.GetAsync(url);
        return await ReadAsync<T>(resp);
    }

    private async Task<T> PostAsync<T>(string url, object? body)
    {
        using var resp = await _http.PostAsJsonAsync(url, body);
        return await ReadAsync<T>(resp);
    }

    private async Task<T> PutAsync<T>(string url, object? body)
    {
        using var resp = await _http.PutAsJsonAsync(url, body);
        return await ReadAsync<T>(resp);
    }

    private async Task SendVoidAsync(HttpMethod method, string url, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        using var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) throw await ToExceptionAsync(resp);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage resp)
    {
        if (!resp.IsSuccessStatusCode) throw await ToExceptionAsync(resp);
        var value = await resp.Content.ReadFromJsonAsync<T>();
        return value ?? throw new ApiException((int)resp.StatusCode, "Empty response");
    }

    private static async Task<ApiException> ToExceptionAsync(HttpResponseMessage resp)
    {
        string message = resp.ReasonPhrase ?? "Request failed";
        try
        {
            var body = await resp.Content.ReadFromJsonAsync<ApiErrorBody>();
            if (!string.IsNullOrWhiteSpace(body?.Error)) message = body!.Error!;
            else if (!string.IsNullOrWhiteSpace(body?.Message)) message = body!.Message!;
        }
        catch { /* non-JSON error body */ }
        return new ApiException((int)resp.StatusCode, message);
    }
}
