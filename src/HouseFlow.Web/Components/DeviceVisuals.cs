namespace HouseFlow.Web.Components;

/// <summary>
/// Device type → tint key + Lucide icon (specs/ux README « Couleurs par type d'appareil »). The tint
/// key is the <c>data-type</c> value read by <c>.hf-device-tile</c> (Styles/app.input.css):
/// heat · wood · air · water · safe · pac · other. Input = the stored <c>Device.Type</c>
/// (DeviceCatalog values, legacy types of the former catalogue included).
/// </summary>
public static class DeviceVisuals
{
    public sealed record Visual(string Tint, string Icon);

    public static readonly Visual Other = new("other", "wrench");

    private static readonly Dictionary<string, Visual> ByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Chaudière Gaz"] = new("heat", "flame"),
        ["Chaudière Fioul"] = new("heat", "flame"),   // legacy type, same family
        ["Poêle à Bois"] = new("wood", "flame-kindling"),
        ["VMC"] = new("air", "wind"),
        ["Chauffe-eau"] = new("water", "droplets"),
        ["Détecteur de Fumée"] = new("safe", "alarm-smoke"),
        ["Pompe à Chaleur"] = new("pac", "fan"),
    };

    /// <summary>Visual of a stored type; unknown / « Autre » / legacy types without a family → muted wrench.</summary>
    public static Visual For(string? type) =>
        type is not null && ByType.TryGetValue(type.Trim(), out var v) ? v : Other;

    public static string Tint(string? type) => For(type).Tint;

    public static string Icon(string? type) => For(type).Icon;
}
