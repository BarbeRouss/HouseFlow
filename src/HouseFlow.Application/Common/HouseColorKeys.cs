using HouseFlow.Contracts;
using HouseFlow.Core;

namespace HouseFlow.Application.Common;

/// <summary>
/// Maps the generated request enum <see cref="HouseColorKey"/> onto the stored palette key
/// (<see cref="HouseColors"/>): <c>HouseColorKey.Sky</c> → <c>"sky"</c>.
/// </summary>
public static class HouseColorKeys
{
    public static bool IsDefined(HouseColorKey? key) => key is null || Enum.IsDefined(key.Value);

    public static string ToKey(HouseColorKey key)
    {
        var value = key.ToString().ToLowerInvariant();
        return HouseColors.IsValid(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown house colour");
    }
}
