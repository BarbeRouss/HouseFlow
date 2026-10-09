using System.Text.Json.Serialization;

namespace HouseFlow.Web.Api;

// HAND-WRITTEN client contracts, mirrored from specs/openapi.yaml (the backend's source of truth).
// scripts/generate-api.sh only regenerates the backend: any API change must be copied here by hand.
//
// System.Net.Http.Json uses web defaults (camelCase, case-insensitive), which
// matches the ASP.NET Core backend's JSON output, so PascalCase names bind fine.
// Dates are ISO strings; calendar dates (maintenance / due dates) are UTC midnights
// ("2027-03-01T00:00:00Z") of a Europe/Paris calendar day — read the date part only.

// ---------- Auth ----------
// ConsentAccepted : acceptation des Conditions générales d'utilisation (contrat, RGPD
// Art. 6(1)(b)) — obligatoire, le backend refuse l'inscription sans elle.
public sealed record RegisterRequest(string FirstName, string LastName, string Email, string Password, bool ConsentAccepted);
public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);

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

    /// <summary>Registration with an invitation token only: the house joined (P03 → P09). Null otherwise.</summary>
    public string? JoinedHouseId { get; set; }
}

// ---------- Status / permissions ----------

/// <summary>R1 status values (maintenance types, devices, houses, tasks).</summary>
public static class MaintenanceStatus
{
    public const string Overdue = "overdue";
    /// <summary>À faire: due within 30 days (Europe/Paris).</summary>
    public const string Pending = "pending";
    public const string UpToDate = "up_to_date";
    /// <summary>House / device without any maintenance type.</summary>
    public const string None = "none";
}

/// <summary>
/// House colour keys (API <c>HouseColorKey</c>), in palette/rotation order — see specs/ux/README.md
/// « Couleurs de maison »: indigo #6366f1, orange #ea580c, green #16a34a, sky #0284c7, yellow #ca8a04, pink #db2777.
/// </summary>
public static class HouseColorKeys
{
    public const string Indigo = "indigo";
    public const string Orange = "orange";
    public const string Green = "green";
    public const string Sky = "sky";
    public const string Yellow = "yellow";
    public const string Pink = "pink";

    public static readonly IReadOnlyList<string> All = [Indigo, Orange, Green, Sky, Yellow, Pink];
}

/// <summary>House roles as sent by the API.</summary>
public static class HouseRoles
{
    public const string Owner = "Owner";
    public const string CollaboratorRW = "CollaboratorRW";
    public const string CollaboratorRO = "CollaboratorRO";
    public const string Tenant = "Tenant";
}

/// <summary>
/// The current user's rights on a house (rule R5) — hide, never disable, what is not allowed.
/// Sent on <see cref="HouseDetail"/> and <see cref="DeviceDetail"/>.
/// </summary>
public sealed class Capabilities
{
    /// <summary>"C'est fait", log / edit a maintenance record (owner, RW, tenant).</summary>
    public bool CanLogMaintenance { get; set; }
    /// <summary>Add / edit a device or a maintenance type (owner, RW).</summary>
    public bool CanEditDevices { get; set; }
    /// <summary>Delete a device, a maintenance type or a record (owner, RW).</summary>
    public bool CanDelete { get; set; }
    /// <summary>Edit / delete the house (owner).</summary>
    public bool CanManageHouse { get; set; }
    /// <summary>Members (roles, removal) and every invitation (owner).</summary>
    public bool CanManageMembers { get; set; }
    /// <summary>Invite a tenant; see, re-send and cancel tenant invitations — opens M5 in restricted mode (owner, RW).</summary>
    public bool CanInviteTenants { get; set; }
    /// <summary>Costs and providers are visible (false for a tenant without that right: they come back null / 0).</summary>
    public bool CanViewCosts { get; set; }
}

// ---------- Houses ----------
public sealed class CreateHouseRequest
{
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    /// <summary>
    /// <see cref="HouseColorKeys"/> value. Create: null = rotation (the API picks <c>HousesListResponse.NextColorKey</c>).
    /// Update (PUT, same body): null keeps the current colour.
    /// </summary>
    public string? ColorKey { get; set; }
}

public sealed class HouseSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? CreatedAt { get; set; }
    /// <summary>Banner colour (<see cref="HouseColorKeys"/>).</summary>
    public string ColorKey { get; set; } = HouseColorKeys.Indigo;
    /// <summary>Obsolete (R3: no percentage on screen). Use UpToDateCount / MaintenanceTypesCount.</summary>
    public int Score { get; set; }
    public int DevicesCount { get; set; }
    /// <summary>Due within 30 days, overdue excluded.</summary>
    public int PendingCount { get; set; }
    public int OverdueCount { get; set; }
    public int UpToDateCount { get; set; }
    public int MaintenanceTypesCount { get; set; }
    /// <summary><see cref="MaintenanceStatus"/>: overdue | pending | up_to_date | none.</summary>
    public string Status { get; set; } = MaintenanceStatus.None;
    public string? UserRole { get; set; }
    /// <summary>
    /// Banner chips (C4): the <c>Device.Type</c> (catalog value) of every device, one per device, oldest first.
    /// Complete list (length = DevicesCount) — truncate (« +n ») when rendering.
    /// </summary>
    public List<string> DeviceTypes { get; set; } = new();
    /// <summary>Owner + accepted members (pending invitations excluded). « Partagée » badge when &gt; 1.</summary>
    public int MembersCount { get; set; } = 1;
}

public sealed class HousesListResponse
{
    public List<HouseSummary> Houses { get; set; } = new();
    /// <summary>Obsolete (R3: no percentage on screen).</summary>
    public int GlobalScore { get; set; }
    /// <summary>Colour the caller's next house will get (P05 tile) — <see cref="HouseColorKeys"/>.</summary>
    public string NextColorKey { get; set; } = HouseColorKeys.Indigo;
}

public sealed class HouseDetail
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? CreatedAt { get; set; }
    /// <summary>Banner colour (<see cref="HouseColorKeys"/>).</summary>
    public string ColorKey { get; set; } = HouseColorKeys.Indigo;
    /// <summary>Obsolete (R3: no percentage on screen).</summary>
    public int Score { get; set; }
    public int DevicesCount { get; set; }
    public int PendingCount { get; set; }
    public int OverdueCount { get; set; }
    public int UpToDateCount { get; set; }
    public int MaintenanceTypesCount { get; set; }
    public string Status { get; set; } = MaintenanceStatus.None;
    public string? UserRole { get; set; }
    /// <summary>
    /// Banner chips (C4): the <c>Device.Type</c> (catalog value) of every device, one per device, oldest first.
    /// Complete list (length = DevicesCount) — truncate (« +n ») when rendering.
    /// </summary>
    public List<string> DeviceTypes { get; set; } = new();
    /// <summary>Owner + accepted members (pending invitations excluded). « Partagée » badge when &gt; 1.</summary>
    public int MembersCount { get; set; } = 1;
    public Capabilities Capabilities { get; set; } = new();
    public List<DeviceSummary> Devices { get; set; } = new();
}

public sealed class HouseDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? CreatedAt { get; set; }
    /// <summary>Banner colour (<see cref="HouseColorKeys"/>).</summary>
    public string ColorKey { get; set; } = HouseColorKeys.Indigo;
}

// ---------- Devices ----------
public sealed class CreateDeviceRequest
{
    public string Name { get; set; } = "";
    /// <summary>DeviceCatalog value (e.g. "Chaudière Gaz") or "Autre".</summary>
    public string Type { get; set; } = "";
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? InstallDate { get; set; }

    /// <summary>
    /// Creation only (ignored by PUT): the catalogue's default maintenance type, created with the device,
    /// with its « Dernier entretien » choice (R2). Null for "Autre" (device without maintenance).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CreateMaintenanceTypeRequest? MaintenanceType { get; set; }
}

public sealed class DeviceSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? InstallDate { get; set; }
    public string HouseId { get; set; } = "";
    public string? CreatedAt { get; set; }
    /// <summary>Obsolete (R3: no percentage on screen).</summary>
    public int Score { get; set; }
    /// <summary><see cref="MaintenanceStatus"/>: overdue | pending | up_to_date | none.</summary>
    public string Status { get; set; } = MaintenanceStatus.None;
    /// <summary>Due within 30 days, overdue excluded.</summary>
    public int PendingCount { get; set; }
    public int OverdueCount { get; set; }
    public int UpToDateCount { get; set; }
    public int MaintenanceTypesCount { get; set; }
    /// <summary>Next due date of the most urgent maintenance (C4 row); null without maintenance type.</summary>
    public string? NextDueDate { get; set; }
    /// <summary>Name of the most urgent maintenance (C4 subtitle when overdue / due).</summary>
    public string? NextMaintenanceName { get; set; }
}

public sealed class MaintenanceTypeWithStatus
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Annual | Semestrial | Quarterly | Monthly | Biennial | Custom (see <see cref="Periodicities"/>).</summary>
    public string Periodicity { get; set; } = "";
    public int? CustomDays { get; set; }
    public int? CustomMonths { get; set; }
    public string DeviceId { get; set; } = "";
    public string? CreatedAt { get; set; }
    public string Status { get; set; } = "up_to_date"; // up_to_date | pending | overdue
    public string? LastMaintenanceDate { get; set; }
    /// <summary>Never null since the redesign (R2); kept nullable for binding safety.</summary>
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
    public string? CreatedAt { get; set; }
    /// <summary>Obsolete (R3: no percentage on screen).</summary>
    public int Score { get; set; }
    public string? Status { get; set; }
    public int PendingCount { get; set; }
    public int OverdueCount { get; set; }
    public int UpToDateCount { get; set; }
    public int MaintenanceTypesCount { get; set; }
    public string? UserRole { get; set; }
    public Capabilities Capabilities { get; set; } = new();
    public List<MaintenanceTypeWithStatus> MaintenanceTypes { get; set; } = new();
    /// <summary>0 when <see cref="Capabilities.CanViewCosts"/> is false.</summary>
    public decimal TotalSpent { get; set; }
    public int MaintenanceCount { get; set; }
}

public sealed class DeviceDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string HouseId { get; set; } = "";
}

// ---------- Maintenance ----------

/// <summary>Periodicity values accepted by the API.</summary>
public static class Periodicities
{
    public const string Annual = "Annual";          // 1 an
    public const string Semestrial = "Semestrial";  // 6 mois
    public const string Quarterly = "Quarterly";    // 3 mois
    public const string Monthly = "Monthly";
    public const string Biennial = "Biennial";      // 2 ans
    /// <summary>« Tous les n mois / ans »: set CustomMonths (n years = 12n months).</summary>
    public const string Custom = "Custom";
}

/// <summary>« Dernier entretien » choice at creation (R2).</summary>
public sealed class LastMaintenance
{
    /// <summary>« Je ne sais pas »: no record, due 30 days after creation.</summary>
    public const string KindUnknown = "Unknown";
    /// <summary>« Plus ancien »: no record, due on the creation date.</summary>
    public const string KindOlder = "Older";
    /// <summary>Month + year: a record on the 1st of that month, note « Date approximative (mois) ».</summary>
    public const string KindMonth = "Month";

    public string Kind { get; set; } = KindUnknown;
    public int? Year { get; set; }
    public int? Month { get; set; }

    public static LastMaintenance Unknown() => new() { Kind = KindUnknown };
    public static LastMaintenance Older() => new() { Kind = KindOlder };
    public static LastMaintenance InMonth(int year, int month) => new() { Kind = KindMonth, Year = year, Month = month };
}

public sealed class CreateMaintenanceTypeRequest
{
    public string Name { get; set; } = "";
    public string Periodicity { get; set; } = "Annual";
    public int? CustomDays { get; set; }
    /// <summary>Custom periodicity in months (1..120).</summary>
    public int? CustomMonths { get; set; }
    /// <summary>Null = « Je ne sais pas ».</summary>
    public LastMaintenance? LastMaintenance { get; set; }
}

/// <summary>Partial update (M4): null fields are kept. Changing the periodicity recalculates the due date.</summary>
public sealed class UpdateMaintenanceTypeRequest
{
    public string? Name { get; set; }
    public string? Periodicity { get; set; }
    public int? CustomDays { get; set; }
    public int? CustomMonths { get; set; }
}

public sealed class MaintenanceTypeDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Periodicity { get; set; } = "";
    public int? CustomDays { get; set; }
    public int? CustomMonths { get; set; }
    public string DeviceId { get; set; } = "";
}

public sealed class LogMaintenanceRequest
{
    public string Date { get; set; } = "";
    public decimal? Cost { get; set; }
    public string? Provider { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// M3 edit — replacement: Cost / Provider / Notes take the value sent (null clears them); a null Date
/// is kept. Cost and provider are ignored server side for a caller who cannot see costs.
/// </summary>
public sealed class UpdateMaintenanceInstanceRequest
{
    public string? Date { get; set; }
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
    public string? CreatedAt { get; set; }
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
    public string DeviceType { get; set; } = "";
    public string HouseId { get; set; } = "";
    public string HouseName { get; set; } = "";
    /// <summary>pending | overdue (up_to_date only for <see cref="Dashboard.NextTask"/>).</summary>
    public string Status { get; set; } = "pending";
    /// <summary>Never null since the redesign (R2).</summary>
    public string? NextDueDate { get; set; }
    public string? LastMaintenanceDate { get; set; }
    public string Periodicity { get; set; } = "";
    public int? CustomDays { get; set; }
    public int? CustomMonths { get; set; }
    /// <summary>The current user may press "C'est fait" on it (R5).</summary>
    public bool CanLogMaintenance { get; set; }
    /// <summary>The current user's R5 rights on the task's house (e.g. CanViewCosts for M3 from P07).</summary>
    public Capabilities? Capabilities { get; set; }
}

/// <summary>GET /dashboard — P07 home page, over every house the user can see.</summary>
public sealed class Dashboard
{
    /// <summary>All tasks to handle (overdue + due within 30 days), soonest first; no limit.</summary>
    public List<UpcomingTask> Tasks { get; set; } = new();
    /// <summary>R3 « À traiter » (title and nav badge).</summary>
    public int ToHandleCount { get; set; }
    public int OverdueCount { get; set; }
    public int PendingCount { get; set; }
    public int UpToDateCount { get; set; }
    /// <summary>All maintenance types: « {UpToDateCount}/{TotalCount} à jour ».</summary>
    public int TotalCount { get; set; }
    /// <summary>Soonest up-to-date task, for « Tout est à jour · Prochain : … » (null if none).</summary>
    public UpcomingTask? NextTask { get; set; }
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
    /// <summary>Effective right (R5): always true for the owner and RW, false for RO; a tenant's own setting.</summary>
    public bool CanLogMaintenance { get; set; }
    /// <summary>Effective right (R5): always true for the owner and collaborators; a tenant's own setting.</summary>
    public bool CanViewCosts { get; set; }

    /// <summary>Date the member joined — the API field is <c>createdAt</c>.</summary>
    [JsonPropertyName("createdAt")]
    public string? JoinedAt { get; set; }
}

public sealed class Invitation
{
    public string Id { get; set; } = "";
    /// <summary>Link token: /{locale}/invitations/{token}. No email is sent — the owner copies the link.</summary>
    public string Token { get; set; } = "";
    public string? Email { get; set; }
    public string Role { get; set; } = "";
    /// <summary>Pending | Expired (listed invitations).</summary>
    public string Status { get; set; } = "";
    /// <summary>7-day delay passed: offer « Renvoyer ».</summary>
    public bool IsExpired { get; set; }
    public string HouseId { get; set; } = "";
    public string HouseName { get; set; } = "";
    public string CreatedByName { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    public string? CreatedAt { get; set; }
}

/// <summary>Public view of an invitation (P04), readable without a session.</summary>
public sealed class InvitationInfo
{
    public string Id { get; set; } = "";
    public string HouseId { get; set; } = "";
    public string HouseName { get; set; } = "";
    /// <summary>House banner colour on P04 (<see cref="HouseColorKeys"/>).</summary>
    public string HouseColorKey { get; set; } = HouseColorKeys.Indigo;
    /// <summary>
    /// P04 banner chips: the <c>Device.Type</c> of every device of the house, one per device, oldest first.
    /// Empty once the invitation is no longer usable (minimisation, like <see cref="Email"/>).
    /// </summary>
    public List<string> HouseDeviceTypes { get; set; } = new();
    public string Role { get; set; } = "";
    /// <summary>Inviter's first and last name.</summary>
    public string InvitedByName { get; set; } = "";
    /// <summary>
    /// Invitee email: locks the email field of P03. Null for old invitations and whenever the
    /// invitation is no longer usable (answered, cancelled or expired — minimisation).
    /// </summary>
    public string? Email { get; set; }
    /// <summary>Pending | Accepted | Expired | Revoked | Declined.</summary>
    public string Status { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    /// <summary>Not usable any more (P04 state C).</summary>
    public bool IsExpired { get; set; }
    /// <summary>Authenticated call only: the user is already a member (P04 → P09). Null without a session.</summary>
    public bool? IsAlreadyMember { get; set; }
}

public sealed class AcceptInvitationResponse
{
    public string HouseId { get; set; } = "";
    public string HouseName { get; set; } = "";
    public string Role { get; set; } = "";
}

public sealed class CreateInvitationRequest
{
    public string Email { get; set; } = "";
    /// <summary>CollaboratorRW | CollaboratorRO | Tenant.</summary>
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

/// <summary>Rectification du profil (RGPD Art. 16). Limits: 100 / 100 / 255 characters. 409 <c>email_taken</c>.</summary>
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

// ---------- OAuth (#304): consent screen, P11 « Applications connectées » ----------
// Scope values: Auth/OAuthScopes (houses:read, houses:write; offline_access is never listed).

/// <summary>GET /oauth/clients/{clientId}: an application registered through DCR, as the consent screen shows it.</summary>
public sealed class OAuthClientInfo
{
    public string ClientId { get; set; } = "";

    /// <summary>Chosen by whoever registered the client (anonymous DCR): never proof of identity.</summary>
    public string ClientName { get; set; } = "";
    public string? ClientUri { get; set; }

    /// <summary>host[:port] of the registered redirect URIs: where the user is sent back.</summary>
    public List<string> RedirectHosts { get; set; } = new();

    /// <summary>Scopes this client may request.</summary>
    public List<string> Scopes { get; set; } = new();
}

/// <summary>A connected application: the user's valid authorization for a client (newest first).</summary>
public sealed class OAuthAuthorization
{
    public string Id { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public List<string> Scopes { get; set; } = new();
    public string? CreatedAt { get; set; }
}

/// <summary>POST /oauth/authorizations — « Autoriser »: the scopes the user ticked (400 validation_failed if empty or not allowed).</summary>
public sealed class GrantOAuthAuthorizationRequest
{
    public string ClientId { get; set; } = "";
    public List<string> Scopes { get; set; } = new();
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

// ---------- Errors ----------

/// <summary>
/// Thrown when an API call returns a non-success status. Branch on <see cref="StatusCode"/> and
/// <see cref="Code"/> (see <see cref="ApiErrorCodes"/>) — <see cref="Exception.Message"/> is the
/// server's technical English text, never meant for display. <see cref="StatusCode"/> 0 = network failure.
/// </summary>
public sealed class ApiException : Exception
{
    public int StatusCode { get; }

    /// <summary>Machine error code from the ProblemDetails <c>code</c> extension, when the API sent one.</summary>
    public string? Code { get; }

    /// <summary>
    /// Server-suggested delay before retrying (<c>Retry-After</c> header, sent with 429 and some 503),
    /// when present.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    public ApiException(int statusCode, string message, string? code = null, TimeSpan? retryAfter = null) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        RetryAfter = retryAfter;
    }

    public bool IsNotFound => StatusCode == 404;
    public bool IsForbidden => StatusCode == 403;
    /// <summary>404 or 403 — the UI shows P13.</summary>
    public bool IsNotFoundOrForbidden => IsNotFound || IsForbidden;

    /// <summary>
    /// 429: a rate limit (<see cref="ApiErrorCodes.RateLimited"/>) or a quota such as the export's.
    /// Transient — never a reason to drop the session.
    /// </summary>
    public bool IsTooManyRequests => StatusCode == 429;
}

/// <summary>Machine codes of <see cref="ApiException.Code"/> (mirror of the backend's ErrorCodes).</summary>
public static class ApiErrorCodes
{
    /// <summary>Login 401: wrong email or password.</summary>
    public const string InvalidCredentials = "invalid_credentials";
    /// <summary>Login / refresh 401: account restricted (GDPR art. 18).</summary>
    public const string AccountRestricted = "account_restricted";
    /// <summary>Refresh 401: missing, expired or reused refresh token.</summary>
    public const string InvalidRefreshToken = "invalid_refresh_token";
    /// <summary>Register / profile 409: email already used.</summary>
    public const string EmailTaken = "email_taken";
    /// <summary>Export 429: one export per hour.</summary>
    public const string ExportRateLimited = "export_rate_limited";
    /// <summary>Invitation expired, declined, cancelled or already used (register 400, accept / decline 400).</summary>
    public const string InvitationInvalid = "invitation_invalid";
    /// <summary>Register 400: email differs from the invitation's.</summary>
    public const string InvitationEmailMismatch = "invitation_email_mismatch";
    /// <summary>Create invitation 409.</summary>
    public const string InvitationAlreadyPending = "invitation_already_pending";
    /// <summary>Create / resend invitation 400: the house already has 20 pending invitations.</summary>
    public const string InvitationLimitReached = "invitation_limit_reached";
    /// <summary>Create invitation 409 / accept 400: already a member of the house.</summary>
    public const string AlreadyMember = "already_member";
    /// <summary>Accept / decline 400: the caller created this invitation.</summary>
    public const string OwnInvitation = "own_invitation";
    /// <summary>Account deletion 400: the password confirmation is wrong.</summary>
    public const string WrongPassword = "wrong_password";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    /// <summary>Generic 400 (model validation, business rule on the input without a dedicated code).</summary>
    public const string ValidationFailed = "validation_failed";
    /// <summary>
    /// Generic 401: access token missing/expired, or the caller's account no longer exists.
    /// A refresh failure carries <see cref="InvalidRefreshToken"/> or <see cref="AccountRestricted"/> instead.
    /// </summary>
    public const string Unauthorized = "unauthorized";
    /// <summary>
    /// 429 from the request rate limiter (login/register 5/min, refresh/logout/revoke 60/min, 200/min
    /// overall, per client), with <see cref="ApiException.RetryAfter"/>. Transient: retry after the delay —
    /// on the boot or mid-session refresh it must NOT clear the session (only a 401 does).
    /// </summary>
    public const string RateLimited = "rate_limited";
}

/// <summary>Error body: RFC 9457 ProblemDetails (+ <c>code</c>); legacy <c>{ error }</c> bodies still parse.</summary>
public sealed class ApiErrorBody
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("detail")] public string? Detail { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    /// <summary>429 only: seconds before retrying (copy of the <c>Retry-After</c> header).</summary>
    [JsonPropertyName("retryAfter")] public double? RetryAfter { get; set; }
}
