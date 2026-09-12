using System.Text.Json.Serialization;

namespace HouseFlow.Web.Api;

// System.Net.Http.Json uses web defaults (camelCase, case-insensitive), which
// matches the ASP.NET Core backend's JSON output, so PascalCase names bind fine.

// ---------- Auth ----------
// ConsentAccepted : acceptation des Conditions générales d'utilisation (contrat, RGPD
// Art. 6(1)(b)) — obligatoire, le backend refuse l'inscription sans elle.
public sealed record RegisterRequest(string FirstName, string LastName, string Email, string Password, bool ConsentAccepted);
public sealed record LoginRequest(string Email, string Password);

public sealed class UserDto
{
    public string Id { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Theme { get; set; }
    public string? Language { get; set; }

    /// <summary>True si l'utilisateur doit (ré)accepter les CGU / la politique en vigueur.</summary>
    public bool ConsentRequired { get; set; }
    public bool IsAdmin { get; set; }
}

public sealed class AuthResponse
{
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public UserDto User { get; set; } = new();
}

// ---------- Houses ----------
public sealed class CreateHouseRequest
{
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
}

public sealed class HouseSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? CreatedAt { get; set; }
    public int Score { get; set; }
    public int DevicesCount { get; set; }
    public int PendingCount { get; set; }
    public int OverdueCount { get; set; }
    public string? UserRole { get; set; }
}

public sealed class HousesListResponse
{
    public List<HouseSummary> Houses { get; set; } = new();
    public int GlobalScore { get; set; }
}

public sealed class HouseDetail
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public int Score { get; set; }
    public int DevicesCount { get; set; }
    public int PendingCount { get; set; }
    public int OverdueCount { get; set; }
    public string? UserRole { get; set; }
    public List<DeviceSummary> Devices { get; set; } = new();
}

public sealed class HouseDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

// ---------- Devices ----------
public sealed class CreateDeviceRequest
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? InstallDate { get; set; }
}

public sealed class DeviceSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? InstallDate { get; set; }
    public int Score { get; set; }
    public int PendingCount { get; set; }
    public int OverdueCount { get; set; }
}

public sealed class MaintenanceTypeWithStatus
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Periodicity { get; set; } = "";
    public int? CustomDays { get; set; }
    public string DeviceId { get; set; } = "";
    public string Status { get; set; } = "up_to_date"; // up_to_date | pending | overdue
    public string? LastMaintenanceDate { get; set; }
    public string? NextDueDate { get; set; }
}

public sealed class DeviceDetail
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? InstallDate { get; set; }
    public string HouseId { get; set; } = "";
    public string? HouseName { get; set; }
    public int Score { get; set; }
    public string? Status { get; set; }
    public int PendingCount { get; set; }
    public int MaintenanceTypesCount { get; set; }
    public List<MaintenanceTypeWithStatus> MaintenanceTypes { get; set; } = new();
    public decimal TotalSpent { get; set; }
    public int MaintenanceCount { get; set; }
}

public sealed class DeviceDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string HouseId { get; set; } = "";
}

// ---------- Maintenance ----------
public sealed class CreateMaintenanceTypeRequest
{
    public string Name { get; set; } = "";
    public string Periodicity { get; set; } = "Annual";
    public int? CustomDays { get; set; }
}

public sealed class MaintenanceTypeDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Periodicity { get; set; } = "";
}

public sealed class LogMaintenanceRequest
{
    public string Date { get; set; } = "";
    public decimal? Cost { get; set; }
    public string? Provider { get; set; }
    public string? Notes { get; set; }
}

public sealed class MaintenanceInstance
{
    public string Id { get; set; } = "";
    public string Date { get; set; } = "";
    public decimal? Cost { get; set; }
    public string? Provider { get; set; }
    public string? Notes { get; set; }
    public string MaintenanceTypeId { get; set; } = "";
    public string MaintenanceTypeName { get; set; } = "";
}

public sealed class MaintenanceHistoryResponse
{
    public List<MaintenanceInstance> Instances { get; set; } = new();
    public decimal TotalSpent { get; set; }
    public int Count { get; set; }
}

public sealed class UpcomingTask
{
    public string MaintenanceTypeId { get; set; } = "";
    public string MaintenanceTypeName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string HouseId { get; set; } = "";
    public string HouseName { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? NextDueDate { get; set; }
}

public sealed class UpcomingTasksResponse
{
    public List<UpcomingTask> Tasks { get; set; } = new();
    public int OverdueCount { get; set; }
    public int PendingCount { get; set; }
}

// ---------- Members / Invitations ----------
public sealed class HouseMember
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public bool CanLogMaintenance { get; set; }
    public bool CanViewCosts { get; set; }
    public string? JoinedAt { get; set; }
}

public sealed class Invitation
{
    public string Id { get; set; } = "";
    public string Token { get; set; } = "";
    public string Role { get; set; } = "";
    public string Status { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
}

public sealed class InvitationInfo
{
    public string HouseName { get; set; } = "";
    public string Role { get; set; } = "";
    public string InvitedByName { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    public bool IsExpired { get; set; }
}

public sealed class AcceptInvitationResponse
{
    public string HouseId { get; set; } = "";
    public string HouseName { get; set; } = "";
    public string Role { get; set; } = "";
}

public sealed class CreateInvitationRequest
{
    public string Role { get; set; } = "";
}

// ---------- Settings / API keys ----------
public sealed class UserSettings
{
    public string Theme { get; set; } = "system";
    public string Language { get; set; } = "fr";
}

// ---------- Account (RGPD) ----------

/// <summary>Profil de l'utilisateur connecté (GET/PUT /users/me).</summary>
public sealed class UserProfile
{
    public string Id { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Theme { get; set; } = "system";
    public string Language { get; set; } = "fr";
    public string? CreatedAt { get; set; }
    public string? ConsentGivenAt { get; set; }
    public string? ConsentPolicyVersion { get; set; }
    public bool ConsentRequired { get; set; }
}

/// <summary>Rectification du profil (RGPD Art. 16).</summary>
public sealed class UpdateProfileRequest
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
}

/// <summary>Suppression du compte, confirmée par ressaisie du mot de passe (RGPD Art. 17).</summary>
public sealed class DeleteAccountRequest
{
    public string Password { get; set; } = "";
}

/// <summary>Fichier d'export renvoyé par GET /users/me/export (RGPD Art. 15 + 20).</summary>
public sealed class DataExportFile
{
    public byte[] Content { get; set; } = [];
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
}

public sealed class ApiKey
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Scope { get; set; } = "";
    public string? CreatedAt { get; set; }
    public string? LastUsedAt { get; set; }
}

public sealed class CreateApiKeyRequest
{
    public string Name { get; set; } = "";
    public string Scope { get; set; } = "ReadWrite";
}

public sealed class CreateApiKeyResponse
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public string Prefix { get; set; } = "";
    public string Scope { get; set; } = "";
}

// ---------- Consent / legal ----------

/// <summary>Acceptation des CGU en vigueur par un utilisateur existant (bannière de ré-acceptation).</summary>
public sealed record ConsentRequest(bool Accepted, string PolicyVersion);

/// <summary>Statut d'acceptation des CGU / de la politique renvoyé par /users/me/consent.</summary>
public sealed class ConsentStatus
{
    public string? ConsentGivenAt { get; set; }
    public string? ConsentPolicyVersion { get; set; }
    public bool ConsentRequired { get; set; }
    public string CurrentPolicyVersion { get; set; } = "";
}

// ---------- Admin ----------
public sealed class AdminStats
{
    public int Users { get; set; }
    public int Admins { get; set; }
    public int Houses { get; set; }
    public int Devices { get; set; }
    public int MaintenanceInstances { get; set; }
}

public sealed class AdminUser
{
    public string Id { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public bool IsAdmin { get; set; }
    public string? CreatedAt { get; set; }
    public int HousesCount { get; set; }
}

public sealed class AdminUsersPage
{
    public List<AdminUser> Users { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>Thrown when an API call returns a non-success status; carries the
/// server-provided error message when present (backend shape: { "error": "..." }).</summary>
public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public ApiException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}

public sealed class ApiErrorBody
{
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}
