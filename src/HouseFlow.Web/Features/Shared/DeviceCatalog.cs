using HouseFlow.Web.Api;

namespace HouseFlow.Web.Features.Shared;

/// <summary>
/// The device catalogue shared by P06 (setup) and M2 (add a device): single source of the types offered
/// and of the default maintenance each one creates (spec P06 « CATALOGUE »).
///
/// <see cref="Entry.Value"/> is what is stored in <c>Device.Type</c> (French label kept from the former
/// catalogue so existing rows and the demo seed still resolve). Labels go through i18n
/// (<c>catalog.types.*</c>, <c>catalog.tasks.*</c>); the maintenance name is written in the user's
/// locale at creation time, like any user-entered name.
/// </summary>
public static class DeviceCatalog
{
    /// <summary>A catalogue type and its default maintenance (name key + periodicity in months).</summary>
    public sealed record Entry(string Id, string Value, string LabelKey, string TaskKey, int Months)
    {
        /// <summary>API periodicity of the default maintenance (12 → Annual, 6 → Semestrial, 24 → Biennial).</summary>
        public string Periodicity => Months switch
        {
            12 => Periodicities.Annual,
            6 => Periodicities.Semestrial,
            24 => Periodicities.Biennial,
            3 => Periodicities.Quarterly,
            1 => Periodicities.Monthly,
            _ => Periodicities.Custom,
        };

        /// <summary>Request creating the default maintenance, with the « Dernier entretien » choice (R2).</summary>
        public CreateMaintenanceTypeRequest MaintenanceRequest(Func<string, string> translate, LastMaintenance? last) => new()
        {
            Name = translate(TaskKey),
            Periodicity = Periodicity,
            CustomMonths = Periodicity == Periodicities.Custom ? Months : null,
            LastMaintenance = last,
        };
    }

    /// <summary>« Autre »: device created without maintenance (M2 only, not offered in P06).</summary>
    public const string OtherValue = "Autre";
    public const string OtherLabelKey = "catalog.types.other";

    /// <summary>The catalogue types, in display order (P06 chips, M2 chips before « Autre »).</summary>
    public static readonly IReadOnlyList<Entry> Entries = new[]
    {
        new Entry("gasBoiler", "Chaudière Gaz", "catalog.types.gasBoiler", "catalog.tasks.gasBoiler", 12),
        new Entry("smokeDetector", "Détecteur de Fumée", "catalog.types.smokeDetector", "catalog.tasks.smokeDetector", 12),
        new Entry("woodStove", "Poêle à Bois", "catalog.types.woodStove", "catalog.tasks.woodStove", 12),
        new Entry("vmc", "VMC", "catalog.types.vmc", "catalog.tasks.vmc", 6),
        new Entry("heatPump", "Pompe à Chaleur", "catalog.types.heatPump", "catalog.tasks.heatPump", 24),
        new Entry("waterHeater", "Chauffe-eau", "catalog.types.waterHeater", "catalog.tasks.waterHeater", 24),
        new Entry("airConditioner", "Climatisation", "catalog.types.airConditioner", "catalog.tasks.airConditioner", 6),
        new Entry("alarm", "Alarme", "catalog.types.alarm", "catalog.tasks.alarm", 12),
        new Entry("pressurePump", "Pompe hydrophore", "catalog.types.pressurePump", "catalog.tasks.pressurePump", 12),
    };

    // Types of the former 12-entry catalogue still found in the database (no longer offered).
    private static readonly Dictionary<string, string> LegacyLabelKeys = new()
    {
        ["Chaudière Fioul"] = "devices.types.chaudiereFioul",
        ["Toiture"] = "devices.types.toiture",
    };

    public static Entry? Find(string? value) =>
        value is null ? null : Entries.FirstOrDefault(e => string.Equals(e.Value, value, StringComparison.OrdinalIgnoreCase));

    public static bool IsOther(string? value) => string.Equals(value, OtherValue, StringComparison.OrdinalIgnoreCase);

    /// <summary>Display label of a stored <c>Device.Type</c>; unknown values are shown as stored.</summary>
    public static string Label(string? value, Func<string, string> translate)
    {
        if (Find(value) is { } entry) return translate(entry.LabelKey);
        if (IsOther(value)) return translate(OtherLabelKey);
        if (value is not null && LegacyLabelKeys.TryGetValue(value, out var key)) return translate(key);
        return value ?? "";
    }

    /// <summary>
    /// Type label shown in place of an empty « {marque} {modèle} » (P09 rows, P10 header) — or ""
    /// when it only repeats the device name (« Détecteur de fumée » named « Détecteur de fumée »).
    /// </summary>
    public static string FallbackSubtitle(string? type, string? deviceName, Func<string, string> translate)
    {
        var label = Label(type, translate);
        return string.Equals(label.Trim(), deviceName?.Trim(), StringComparison.CurrentCultureIgnoreCase) ? "" : label;
    }
}
