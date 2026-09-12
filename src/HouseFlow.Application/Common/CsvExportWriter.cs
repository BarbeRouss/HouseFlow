using System.Globalization;
using System.IO.Compression;
using System.Text;
using HouseFlow.Application.DTOs;

namespace HouseFlow.Application.Common;

/// <summary>
/// Rend un <see cref="UserDataExportDto"/> sous forme d'archive ZIP contenant un CSV
/// par catégorie plus un <c>README.txt</c> (dictionnaire des colonnes + informations
/// de l'Art. 15(1)). JSON et CSV satisfont tous deux le critère « format structuré,
/// couramment utilisé et lisible par machine » de l'Art. 20(1) — contrairement au PDF
/// (lignes directrices WP242).
/// </summary>
public static class CsvExportWriter
{
    /// <summary>Sépare les champs (RFC 4180) ; les champs sont échappés par des guillemets.</summary>
    private const char Separator = ',';

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static byte[] CreateZipArchive(UserDataExportDto export)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "profile.csv", BuildProfileCsv(export));
            AddEntry(archive, "houses.csv", BuildHousesCsv(export));
            AddEntry(archive, "devices.csv", BuildDevicesCsv(export));
            AddEntry(archive, "maintenance_types.csv", BuildMaintenanceTypesCsv(export));
            AddEntry(archive, "maintenance_instances.csv", BuildMaintenanceInstancesCsv(export));
            AddEntry(archive, "memberships.csv", BuildMembershipsCsv(export));
            AddEntry(archive, "invitations.csv", BuildInvitationsCsv(export));
            AddEntry(archive, "api_keys.csv", BuildApiKeysCsv(export));
            AddEntry(archive, "sessions.csv", BuildSessionsCsv(export));
            AddEntry(archive, "audit_logs.csv", BuildAuditLogsCsv(export));
            AddEntry(archive, "README.txt", BuildReadme(export));
        }

        return buffer.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Utf8WithBom.GetPreamble().Concat(Utf8WithBom.GetBytes(content)).ToArray();
        stream.Write(bytes, 0, bytes.Length);
    }

    // ------------------------------------------------------------------ CSVs

    private static string BuildProfileCsv(UserDataExportDto e)
    {
        var sb = new StringBuilder();
        WriteRow(sb, "id", "email", "firstName", "lastName", "createdAt", "updatedAt", "lastLoginAt",
            "theme", "language", "consentGivenAt", "consentPolicyVersion", "exportedAt", "formatVersion");
        WriteRow(sb,
            F(e.Profile.Id), e.Profile.Email, e.Profile.FirstName, e.Profile.LastName,
            F(e.Profile.CreatedAt), F(e.Profile.UpdatedAt), F(e.Profile.LastLoginAt),
            e.Preferences.Theme, e.Preferences.Language,
            F(e.Consent.ConsentGivenAt), e.Consent.ConsentPolicyVersion,
            F(e.ExportedAt), e.FormatVersion);
        return sb.ToString();
    }

    private static string BuildHousesCsv(UserDataExportDto e)
    {
        var sb = new StringBuilder();
        WriteRow(sb, "houseId", "name", "address", "zipCode", "city", "country", "createdAt");
        foreach (var h in e.Houses)
        {
            WriteRow(sb, F(h.Id), h.Name, h.Address, h.ZipCode, h.City, h.Country, F(h.CreatedAt));
        }
        return sb.ToString();
    }

    private static string BuildDevicesCsv(UserDataExportDto e)
    {
        var sb = new StringBuilder();
        WriteRow(sb, "deviceId", "houseId", "houseName", "name", "type", "brand", "model", "installDate", "createdAt");
        foreach (var h in e.Houses)
        {
            foreach (var d in h.Devices)
            {
                WriteRow(sb, F(d.Id), F(h.Id), h.Name, d.Name, d.Type, d.Brand, d.Model, F(d.InstallDate), F(d.CreatedAt));
            }
        }
        return sb.ToString();
    }

    private static string BuildMaintenanceTypesCsv(UserDataExportDto e)
    {
        var sb = new StringBuilder();
        WriteRow(sb, "maintenanceTypeId", "deviceId", "deviceName", "houseId", "name", "periodicity", "customDays", "status");
        foreach (var h in e.Houses)
        {
            foreach (var d in h.Devices)
            {
                foreach (var t in d.MaintenanceTypes)
                {
                    WriteRow(sb, F(t.Id), F(d.Id), d.Name, F(h.Id), t.Name, t.Periodicity, F(t.CustomDays), t.Status);
                }
            }
        }
        return sb.ToString();
    }

    private static string BuildMaintenanceInstancesCsv(UserDataExportDto e)
    {
        var sb = new StringBuilder();
        WriteRow(sb, "maintenanceInstanceId", "maintenanceTypeId", "maintenanceTypeName", "deviceId", "houseId",
            "date", "cost", "provider", "notes", "createdAt");
        foreach (var h in e.Houses)
        {
            foreach (var d in h.Devices)
            {
                foreach (var t in d.MaintenanceTypes)
                {
                    foreach (var i in t.Instances)
                    {
                        WriteRow(sb, F(i.Id), F(t.Id), t.Name, F(d.Id), F(h.Id),
                            F(i.Date), F(i.Cost), i.Provider, i.Notes, F(i.CreatedAt));
                    }
                }
            }
        }
        return sb.ToString();
    }

    private static string BuildMembershipsCsv(UserDataExportDto e)
    {
        var sb = new StringBuilder();
        WriteRow(sb, "houseId", "houseName", "role", "canLogMaintenance", "canViewCosts", "since");
        foreach (var m in e.Memberships)
        {
            WriteRow(sb, F(m.HouseId), m.HouseName, m.Role, F(m.CanLogMaintenance), F(m.CanViewCosts), F(m.Since));
        }
        return sb.ToString();
    }

    private static string BuildInvitationsCsv(UserDataExportDto e)
    {
        var sb = new StringBuilder();
        WriteRow(sb, "invitationId", "direction", "houseName", "role", "status", "createdAt", "expiresAt", "acceptedAt");
        foreach (var i in e.InvitationsSent)
        {
            WriteRow(sb, F(i.Id), "sent", i.HouseName, i.Role, i.Status, F(i.CreatedAt), F(i.ExpiresAt), F(i.AcceptedAt));
        }
        foreach (var i in e.InvitationsReceived)
        {
            WriteRow(sb, F(i.Id), "received", i.HouseName, i.Role, "Accepted", null, null, F(i.AcceptedAt));
        }
        return sb.ToString();
    }

    private static string BuildApiKeysCsv(UserDataExportDto e)
    {
        var sb = new StringBuilder();
        WriteRow(sb, "name", "prefix", "scope", "createdAt", "lastUsedAt", "revokedAt");
        foreach (var k in e.ApiKeys)
        {
            WriteRow(sb, k.Name, k.Prefix, k.Scope, F(k.CreatedAt), F(k.LastUsedAt), F(k.RevokedAt));
        }
        return sb.ToString();
    }

    private static string BuildSessionsCsv(UserDataExportDto e)
    {
        var sb = new StringBuilder();
        WriteRow(sb, "createdAt", "expiresAt", "createdByIp", "revokedAt", "revokedByIp", "reasonRevoked");
        foreach (var s in e.Sessions)
        {
            WriteRow(sb, F(s.CreatedAt), F(s.ExpiresAt), s.CreatedByIp, F(s.RevokedAt), s.RevokedByIp, s.ReasonRevoked);
        }
        return sb.ToString();
    }

    private static string BuildAuditLogsCsv(UserDataExportDto e)
    {
        var sb = new StringBuilder();
        WriteRow(sb, "timestamp", "action", "entityType", "entityId", "changedProperties", "ipAddress", "userAgent");
        foreach (var a in e.AuditLogs)
        {
            WriteRow(sb, F(a.Timestamp), a.Action, a.EntityType, a.EntityId, a.ChangedProperties, a.IpAddress, a.UserAgent);
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- README

    private static string BuildReadme(UserDataExportDto e)
    {
        var info = e.Information;
        var sb = new StringBuilder();

        sb.AppendLine("HouseFlow — export de vos données personnelles / personal data export");
        sb.AppendLine("=====================================================================");
        sb.AppendLine();
        sb.AppendLine(Invariant($"Exporté le / exported at : {F(e.ExportedAt)}"));
        sb.AppendLine(Invariant($"Version du format / format version : {e.FormatVersion}"));
        sb.AppendLine(Invariant($"Version de la politique / policy version : {info.PolicyVersion}"));
        sb.AppendLine(Invariant($"Responsable de traitement / controller : {info.Controller}"));
        sb.AppendLine(Invariant($"Contact vie privée / privacy contact : {info.ContactEmail}"));
        sb.AppendLine();
        sb.AppendLine("Encodage : UTF-8 (avec BOM). Séparateur : « , ». Échappement : RFC 4180.");
        sb.AppendLine("Dates : ISO 8601 UTC. Nombres décimaux : point décimal.");
        sb.AppendLine();

        sb.AppendLine("1. FICHIERS ET COLONNES / FILES AND COLUMNS");
        sb.AppendLine("-------------------------------------------");
        AppendDictionary(sb, "profile.csv", "Votre profil, vos préférences et votre acceptation de la politique.",
            "id, email, firstName, lastName, createdAt, updatedAt, lastLoginAt, theme, language, consentGivenAt, consentPolicyVersion, exportedAt, formatVersion");
        sb.AppendLine("Note : une valeur commençant par =, +, -, @ est préfixée d'une apostrophe (protection contre l'injection de formule dans un tableur) / values starting with =, +, -, @ are prefixed with an apostrophe (spreadsheet formula-injection protection).");
        AppendDictionary(sb, "houses.csv", "Les maisons dont vous êtes propriétaire.",
            "houseId, name, address, zipCode, city, country, createdAt");
        AppendDictionary(sb, "devices.csv", "Les appareils de ces maisons.",
            "deviceId, houseId, houseName, name, type, brand, model, installDate, createdAt");
        AppendDictionary(sb, "maintenance_types.csv", "Les entretiens récurrents définis par appareil. « status » est calculé par HouseFlow (donnée dérivée, hors portabilité).",
            "maintenanceTypeId, deviceId, deviceName, houseId, name, periodicity, customDays, status");
        AppendDictionary(sb, "maintenance_instances.csv", "L'historique des entretiens réalisés.",
            "maintenanceInstanceId, maintenanceTypeId, maintenanceTypeName, deviceId, houseId, date, cost, provider, notes, createdAt");
        AppendDictionary(sb, "memberships.csv", "Les maisons d'autres utilisateurs auxquelles vous avez accès. Les noms et emails des autres membres ne sont pas exportés (Art. 15(4)).",
            "houseId, houseName, role, canLogMaintenance, canViewCosts, since");
        AppendDictionary(sb, "invitations.csv", "Les invitations que vous avez émises (« sent ») ou acceptées (« received »). Les jetons d'invitation et l'identité des tiers ne sont pas exportés.",
            "invitationId, direction, houseName, role, status, createdAt, expiresAt, acceptedAt");
        AppendDictionary(sb, "api_keys.csv", "Vos clés API. L'empreinte de la clé n'est jamais exportée.",
            "name, prefix, scope, createdAt, lastUsedAt, revokedAt");
        AppendDictionary(sb, "sessions.csv", "Vos sessions (refresh tokens). La valeur des jetons n'est jamais exportée.",
            "createdAt, expiresAt, createdByIp, revokedAt, revokedByIp, reasonRevoked");
        AppendDictionary(sb, "audit_logs.csv", "Les actions que vous avez effectuées. Les valeurs avant/après ne sont pas exportées car elles peuvent contenir des données de tiers.",
            "timestamp, action, entityType, entityId, changedProperties, ipAddress, userAgent");
        sb.AppendLine();

        sb.AppendLine("2. INFORMATIONS ART. 15(1) DU RGPD / GDPR ART. 15(1) INFORMATION");
        sb.AppendLine("---------------------------------------------------------------");
        AppendSection(sb, "Finalités / purposes", info.Purposes);
        AppendSection(sb, "Catégories de données / data categories", info.DataCategories);
        AppendSection(sb, "Bases légales / legal bases", info.LegalBases);
        AppendSection(sb, "Destinataires / recipients", info.Recipients);
        AppendSection(sb, "Durées de conservation / retention", info.Retention);
        AppendSection(sb, "Vos droits / your rights", info.Rights);
        AppendSection(sb, "Source des données / source of the data", [info.DataSource]);
        AppendSection(sb, "Transferts hors UE / international transfers", [info.InternationalTransfers]);
        AppendSection(sb, "Décision automatisée / automated decision-making", [info.AutomatedDecisionMaking]);

        sb.AppendLine("Autorités de contrôle / supervisory authorities :");
        foreach (var a in info.SupervisoryAuthorities)
        {
            sb.AppendLine(Invariant($"  - {a.Name} ({a.Country}) — {a.Url}"));
        }
        sb.AppendLine();

        sb.AppendLine(Invariant(
            $"Portabilité (Art. 20) / portability scope : {string.Join(", ", info.PortabilityScope)}"));
        sb.AppendLine("Les sessions, clés API et journaux d'audit relèvent du droit d'accès (Art. 15)");
        sb.AppendLine("mais pas du droit à la portabilité (Art. 20), qui ne couvre que les données");
        sb.AppendLine("fournies par vous et traitées sur la base du contrat ou du consentement.");

        return sb.ToString();
    }

    private static void AppendDictionary(StringBuilder sb, string file, string description, string columns)
    {
        sb.AppendLine(Invariant($"* {file}"));
        sb.AppendLine(Invariant($"  {description}"));
        sb.AppendLine(Invariant($"  Colonnes : {columns}"));
        sb.AppendLine();
    }

    private static void AppendSection(StringBuilder sb, string title, IReadOnlyList<LocalizedTextDto> items)
    {
        sb.AppendLine(Invariant($"{title} :"));
        foreach (var item in items)
        {
            sb.AppendLine(Invariant($"  - FR: {item.Fr}"));
            sb.AppendLine(Invariant($"    EN: {item.En}"));
        }
        sb.AppendLine();
    }

    // --------------------------------------------------------------- Helpers

    private static void WriteRow(StringBuilder sb, params string?[] fields)
    {
        sb.Append(string.Join(Separator, fields.Select(Escape)));
        sb.Append("\r\n"); // RFC 4180
    }

    /// <summary>Échappement RFC 4180 : guillemets doublés, champ encadré si nécessaire.</summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        // Neutralisation des formules (CSV injection, OWASP) : un tableur interprète une
        // cellule commençant par =, +, -, @, TAB ou CR comme une formule — une note saisie
        // par un collaborateur ne doit pas pouvoir s'exécuter dans le tableur du propriétaire.
        if ("=+-@\t\r".IndexOf(value[0]) >= 0)
            value = "'" + value;

        var needsQuotes = value.IndexOfAny([Separator, '"', '\r', '\n']) >= 0
            || value[0] == ' '
            || value[^1] == ' ';

        return needsQuotes ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static string F(Guid value) => value.ToString();
    private static string? F(DateTime? value) => value?.ToString("O", CultureInfo.InvariantCulture);
    private static string F(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static string? F(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static string? F(int? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static string F(bool value) => value ? "true" : "false";

    private static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
