namespace HouseFlow.Core;

/// <summary>
/// House colour palette (<c>House.ColorKey</c>, API schema <c>HouseColorKey</c>): the banner colour of the house
/// cards (C4), the house page (P09) and the invitation (P04). Order = palette order of specs/ux/README.md
/// (« Couleurs de maison »), which is also the rotation order.
/// </summary>
public static class HouseColors
{
    public const string Indigo = "indigo"; // #6366f1
    public const string Orange = "orange"; // #ea580c
    public const string Green = "green";   // #16a34a
    public const string Sky = "sky";       // #0284c7
    public const string Yellow = "yellow"; // #ca8a04
    public const string Pink = "pink";     // #db2777

    public const int MaxLength = 16;

    public static readonly IReadOnlyList<string> Palette = [Indigo, Orange, Green, Sky, Yellow, Pink];

    public static string Default => Indigo;

    public static bool IsValid(string? key) => key != null && Palette.Contains(key);

    /// <summary>
    /// Rotation rule: the least-used key among the owner's existing houses, ties broken by palette order.
    /// Without deletions this is plain rotation (indigo, orange, green, …, then indigo again); a colour
    /// freed by a deletion is reused first. Unknown keys are ignored.
    /// </summary>
    public static string Next(IEnumerable<string> ownerHouseKeys)
    {
        var counts = Palette.ToDictionary(k => k, _ => 0);
        foreach (var key in ownerHouseKeys)
            if (counts.ContainsKey(key)) counts[key]++;

        var min = counts.Values.Min();
        return Palette.First(k => counts[k] == min);
    }
}
