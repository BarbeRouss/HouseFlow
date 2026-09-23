namespace HouseFlow.Web.Features.Devices;

public static class DeviceTypeCatalog
{
    public sealed record Entry(string Value, string LabelKey, string Emoji);

    public static readonly IReadOnlyList<Entry> Entries = new[]
    {
        new Entry("Chaudière Gaz", "devices.types.chaudiereGaz", "🔥"),
        new Entry("Chaudière Fioul", "devices.types.chaudiereFioul", "🔥"),
        new Entry("Pompe à Chaleur", "devices.types.pompeAChaleur", "❄️"),
        new Entry("Climatisation", "devices.types.climatisation", "❄️"),
        new Entry("Poêle à Bois", "devices.types.poeleABois", "🪵"),
        new Entry("Chauffe-eau", "devices.types.chauffeEau", "🚿"),
        new Entry("VMC", "devices.types.vmc", "💨"),
        new Entry("Toiture", "devices.types.toiture", "🏠"),
        new Entry("Détecteur de Fumée", "devices.types.detecteurDeFumee", "🚨"),
        new Entry("Alarme", "devices.types.alarme", "🚨"),
        new Entry("Pompe hydrophore", "devices.types.pompeHydrophore", "🚰"),
        new Entry("Autre", "devices.types.autre", "🔧"),
    };

    public static string Emoji(string? type) =>
        Entries.FirstOrDefault(e => e.Value == type)?.Emoji ?? "🔧";

    public static string Label(string? type, Func<string, string> translate) =>
        Entries.FirstOrDefault(e => e.Value == type) is { } entry ? translate(entry.LabelKey) : (type ?? "");
}
