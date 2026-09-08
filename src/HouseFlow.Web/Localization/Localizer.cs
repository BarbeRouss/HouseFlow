using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HouseFlow.Web.Localization;

/// <summary>
/// Reads the embedded message catalogs (fr.json / en.json, copied verbatim from
/// the former next-intl frontend) and resolves dotted keys such as
/// "houses.addHouse" with ICU-style interpolation ({name}) and the simple
/// plural form ({count, plural, one {..} other {..}}) used by those catalogs.
/// </summary>
public sealed class Localizer
{
    public const string DefaultLocale = "fr";
    private static readonly string[] Supported = { "fr", "en" };

    private readonly Dictionary<string, JsonElement> _catalogs = new();

    private static readonly Regex PluralRegex = new(
        @"\{(\w+),\s*plural,\s*one\s*\{([^{}]*)\}\s*other\s*\{([^{}]*)\}\}",
        RegexOptions.Compiled);

    public Localizer()
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var locale in Supported)
        {
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($"Resources.{locale}.json", StringComparison.OrdinalIgnoreCase));
            if (resourceName is null) continue;
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var doc = JsonDocument.Parse(stream);
            _catalogs[locale] = doc.RootElement.Clone();
        }
    }

    public static bool IsSupported(string? locale) =>
        !string.IsNullOrEmpty(locale) && Array.Exists(Supported, l => l == locale);

    public string Translate(string locale, string key, object? args = null)
    {
        if (!_catalogs.ContainsKey(locale)) locale = DefaultLocale;
        var raw = Lookup(locale, key) ?? Lookup(DefaultLocale, key) ?? key;
        return Format(raw, locale, ToDictionary(args));
    }

    private string? Lookup(string locale, string key)
    {
        if (!_catalogs.TryGetValue(locale, out var root)) return null;
        var current = root;
        foreach (var part in key.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(part, out var next))
                return null;
            current = next;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string Format(string template, string locale, IReadOnlyDictionary<string, object?> args)
    {
        // Resolve plural blocks first (they contain nested braces).
        var result = PluralRegex.Replace(template, m =>
        {
            var varName = m.Groups[1].Value;
            var one = m.Groups[2].Value;
            var other = m.Groups[3].Value;
            var n = GetNumber(args, varName);
            var useOne = locale == "fr" ? (n == 0 || n == 1) : n == 1;
            return useOne ? one : other;
        });

        // Then plain {var} interpolation.
        if (args.Count > 0)
        {
            result = Regex.Replace(result, @"\{(\w+)\}", m =>
            {
                var name = m.Groups[1].Value;
                return args.TryGetValue(name, out var value) && value is not null
                    ? value.ToString() ?? string.Empty
                    : m.Value;
            });
        }

        return result;
    }

    private static double GetNumber(IReadOnlyDictionary<string, object?> args, string name)
    {
        if (args.TryGetValue(name, out var value) && value is not null)
        {
            if (value is IConvertible)
            {
                try { return Convert.ToDouble(value); } catch { }
            }
        }
        return 0;
    }

    private static IReadOnlyDictionary<string, object?> ToDictionary(object? args)
    {
        if (args is null) return EmptyDict;
        if (args is IReadOnlyDictionary<string, object?> ro) return ro;
        if (args is IDictionary<string, object?> d)
            return new Dictionary<string, object?>(d);

        var dict = new Dictionary<string, object?>();
        foreach (var prop in args.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            dict[prop.Name] = prop.GetValue(args);
        return dict;
    }

    private static readonly Dictionary<string, object?> EmptyDict = new();
}
