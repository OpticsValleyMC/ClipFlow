using System.Collections;
using System.Globalization;
using System.Resources;

namespace ClipFlow.Core;

public static class LocalizedStrings
{
    private static readonly ResourceManager Resources = new("ClipFlow.Core.Strings", typeof(LocalizedStrings).Assembly);
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("zh-CN");

    public static CultureInfo ResolveCulture(AppLanguage language, IEnumerable<string> systemLanguages)
    {
        if (language != AppLanguage.System)
        {
            return CultureInfo.GetCultureInfo(language switch
            {
                AppLanguage.SimplifiedChinese => "zh-CN",
                AppLanguage.TraditionalChinese => "zh-TW",
                _ => "en-US"
            });
        }

        foreach (var tag in systemLanguages)
        {
            var normalized = tag.ToLowerInvariant();
            if (normalized is "en" || normalized.StartsWith("en-", StringComparison.Ordinal))
                return CultureInfo.GetCultureInfo("en-US");
            if (normalized is "zh" || normalized.StartsWith("zh-", StringComparison.Ordinal))
            {
                var traditional = normalized.StartsWith("zh-hant", StringComparison.Ordinal)
                    || normalized is "zh-tw" or "zh-hk" or "zh-mo";
                return CultureInfo.GetCultureInfo(traditional ? "zh-TW" : "zh-CN");
            }
        }

        return CultureInfo.GetCultureInfo("en-US");
    }

    public static void UseCulture(CultureInfo culture) => Culture = culture;

    public static string L(string key) => Resources.GetString(key, Culture)
        ?? throw new MissingManifestResourceException(key);

    public static string L(string key, params object?[] arguments) => string.Format(Culture, L(key), arguments);

    public static IEnumerable<KeyValuePair<string, string>> GetStrings()
    {
        var neutral = Resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        foreach (DictionaryEntry entry in neutral)
        {
            var key = (string)entry.Key;
            yield return new(key, L(key));
        }
    }
}
