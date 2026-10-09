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

    // POST /auth/refresh is not here: Auth/SessionRefresher owns it (single-flight, retry on 429/5xx/network).

    /// <summary>
    /// Best-effort server-side revocation: the caller clears the local session whatever happens, so a
    /// network failure here is deliberately not surfaced (the refresh cookie expires on its own).
    /// </summary>
    public async Task LogoutAsync()
    {
        try { using var _ = await _http.PostAsync("/api/v1/auth/logout", null); }
        catch (HttpRequestException) { /* best-effort: session is cleared locally anyway */ }
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
    /// <summary>P07: every task to handle (no limit) + R3 counters + next up-to-date task.</summary>
    public Task<Dashboard> GetDashboardAsync() => GetAsync<Dashboard>("/api/v1/dashboard");

    public Task<List<MaintenanceTypeWithStatus>> GetMaintenanceTypesAsync(string deviceId) =>
        GetAsync<List<MaintenanceTypeWithStatus>>($"/api/v1/devices/{deviceId}/maintenance-types");

    /// <summary>M4 edit (partial). Changing the periodicity recalculates the next due date.</summary>
    public Task<MaintenanceTypeDto> UpdateMaintenanceTypeAsync(string maintenanceTypeId, UpdateMaintenanceTypeRequest req) =>
        PutAsync<MaintenanceTypeDto>($"/api/v1/maintenance-types/{maintenanceTypeId}", req);

    public Task DeleteMaintenanceTypeAsync(string maintenanceTypeId) =>
        SendVoidAsync(HttpMethod.Delete, $"/api/v1/maintenance-types/{maintenanceTypeId}");

    /// <summary>M3 edit (owner, RW, tenant).</summary>
    public Task<MaintenanceInstance> UpdateMaintenanceInstanceAsync(string instanceId, UpdateMaintenanceInstanceRequest req) =>
        PutAsync<MaintenanceInstance>($"/api/v1/maintenance-instances/{instanceId}", req);

    /// <summary>M3 « Supprimer » / C5 « Annuler » (owner, RW). The due date is recalculated (R2).</summary>
    public Task DeleteMaintenanceInstanceAsync(string instanceId) =>
        SendVoidAsync(HttpMethod.Delete, $"/api/v1/maintenance-instances/{instanceId}");

    // ---------- Members / Invitations ----------
    public Task<List<HouseMember>> GetMembersAsync(string houseId) => GetAsync<List<HouseMember>>($"/api/v1/houses/{houseId}/members");
    public Task<List<Invitation>> GetInvitationsAsync(string houseId) => GetAsync<List<Invitation>>($"/api/v1/houses/{houseId}/invitations");
    public Task<Invitation> CreateInvitationAsync(string houseId, CreateInvitationRequest req) =>
        PostAsync<Invitation>($"/api/v1/houses/{houseId}/invitations", req);
    public Task<InvitationInfo> GetInvitationInfoAsync(string token) => GetAsync<InvitationInfo>($"/api/v1/invitations/{token}");
    public Task<AcceptInvitationResponse> AcceptInvitationAsync(string token) =>
        PostAsync<AcceptInvitationResponse>($"/api/v1/invitations/{token}/accept", null);
    /// <summary>P04 « Refuser » (the invitee).</summary>
    public Task DeclineInvitationAsync(string token) =>
        SendVoidAsync(HttpMethod.Post, $"/api/v1/invitations/{token}/decline");
    /// <summary>M5 « Renvoyer » (owner): new token + expiry reset — the previous link stops working.</summary>
    public Task<Invitation> ResendInvitationAsync(string invitationId) =>
        PostAsync<Invitation>($"/api/v1/invitations/{invitationId}/resend", null);
    /// <summary>M5 « Annuler l'invitation » (owner).</summary>
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

    // ---------- Account (RGPD) ----------
    /// <summary>RGPD Art. 15 — profil de l'utilisateur connecté.</summary>
    public Task<UserProfile> GetMyProfileAsync() => GetAsync<UserProfile>("/api/v1/users/me");

    /// <summary>RGPD Art. 16 — rectification du prénom, du nom et de l'email.</summary>
    public Task<UserProfile> UpdateMyProfileAsync(UpdateProfileRequest req) => PutAsync<UserProfile>("/api/v1/users/me", req);

    /// <summary>RGPD Art. 17 — suppression définitive du compte.</summary>
    public Task DeleteMyAccountAsync(string password) =>
        SendVoidAsync(HttpMethod.Delete, "/api/v1/users/me", new DeleteAccountRequest { Password = password });

    /// <summary>
    /// RGPD Art. 15 + 20 — télécharge l'export des données. <paramref name="format"/>
    /// vaut <c>json</c> ou <c>csv</c> (archive ZIP). Le nom de fichier est lu dans
    /// l'en-tête Content-Disposition renvoyé par l'API.
    /// </summary>
    public async Task<DataExportFile> ExportMyDataAsync(string format)
    {
        using var resp = await _http.GetAsync($"/api/v1/users/me/export?format={Uri.EscapeDataString(format)}");
        if (!resp.IsSuccessStatusCode) throw await ToExceptionAsync(resp);

        var contentType = resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var fileName = resp.Content.Headers.ContentDisposition?.FileNameStar
            ?? resp.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? $"houseflow-data-export.{(format == "csv" ? "zip" : "json")}";

        return new DataExportFile
        {
            Content = await resp.Content.ReadAsByteArrayAsync(),
            FileName = fileName,
            ContentType = contentType
        };
    }

    // ---------- OAuth (#304): /oauth pages, P11 « Applications connectées » ----------
    // Credentials are sent like every call (AuthMessageHandler: BrowserRequestCredentials.Include),
    // so the browser stores the oauthSession cookie these answers set (Path=/connect, HttpOnly).

    /// <summary>Sets the oauthSession cookie that identifies the user on /connect/authorize (session cookie, valid 10 min).</summary>
    public Task CreateOAuthSessionAsync() => SendVoidAsync(HttpMethod.Post, "/api/v1/oauth/session");

    /// <summary>The application asking for access; 404 not_found if unknown. The id comes from the URL: escaped.</summary>
    public Task<OAuthClientInfo> GetOAuthClientAsync(string clientId) =>
        GetAsync<OAuthClientInfo>($"/api/v1/oauth/clients/{Uri.EscapeDataString(clientId)}");

    public Task<List<OAuthAuthorization>> GetOAuthAuthorizationsAsync() =>
        GetAsync<List<OAuthAuthorization>>("/api/v1/oauth/authorizations");

    /// <summary>
    /// « Autoriser »: records the consent — the ticked scopes replace those granted before (a scope
    /// dropped revokes the application's tokens) — and refreshes the oauthSession cookie.
    /// </summary>
    public Task<OAuthAuthorization> GrantOAuthAuthorizationAsync(string clientId, IEnumerable<string> scopes) =>
        PostAsync<OAuthAuthorization>("/api/v1/oauth/authorizations",
            new GrantOAuthAuthorizationRequest { ClientId = clientId, Scopes = scopes.ToList() });

    /// <summary>« Révoquer »: the authorization and every token issued under it stop working at once.</summary>
    public Task RevokeOAuthAuthorizationAsync(string id) =>
        SendVoidAsync(HttpMethod.Delete, $"/api/v1/oauth/authorizations/{Uri.EscapeDataString(id)}");

    // ---------- Consent / legal ----------
    public Task<ConsentStatus> GetConsentStatusAsync() => GetAsync<ConsentStatus>("/api/v1/users/me/consent");
    public Task<ConsentStatus> RecordConsentAsync(ConsentRequest req) => PostAsync<ConsentStatus>("/api/v1/users/me/consent", req);

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

    private async Task<T> PostAsync<T>(string url, object? body, CancellationToken ct = default)
    {
        using var resp = await _http.PostAsJsonAsync(url, body, ct);
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

    /// <summary>
    /// Maps an error response to an <see cref="ApiException"/> carrying the HTTP status and the
    /// ProblemDetails <c>code</c> (see <see cref="ApiErrorCodes"/>) so pages can branch on them.
    /// </summary>
    private static async Task<ApiException> ToExceptionAsync(HttpResponseMessage resp)
    {
        var status = (int)resp.StatusCode;
        string message = resp.ReasonPhrase ?? "Request failed";
        string? code = null;
        double? bodyRetryAfter = null;

        if (resp.Content.Headers.ContentLength != 0 && IsJson(resp.Content.Headers.ContentType?.MediaType))
        {
            try
            {
                var body = await resp.Content.ReadFromJsonAsync<ApiErrorBody>();
                code = string.IsNullOrWhiteSpace(body?.Code) ? null : body!.Code;
                message = FirstNonBlank(body?.Detail, body?.Error, body?.Message, body?.Title) ?? message;
                bodyRetryAfter = body?.RetryAfter;
            }
            catch (System.Text.Json.JsonException)
            {
                // Malformed JSON error body: the status code alone still drives the UI.
            }
        }

        return new ApiException(status, message, code, RetryAfterOf(resp, bodyRetryAfter));
    }

    /// <summary>
    /// <c>Retry-After</c> as a delay (seconds form or HTTP date); else the 429 body's <c>retryAfter</c>
    /// (seconds); null when neither is present.
    /// </summary>
    private static TimeSpan? RetryAfterOf(HttpResponseMessage resp, double? bodyRetryAfterSeconds)
    {
        var retryAfter = resp.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta) return delta;
        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return bodyRetryAfterSeconds is double seconds && seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
    }

    private static bool IsJson(string? mediaType) =>
        mediaType is not null && (mediaType.EndsWith("/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
