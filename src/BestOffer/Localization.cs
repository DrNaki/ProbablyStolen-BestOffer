using System;
using System.Globalization;

namespace DeepSpaceBestOffer;

/// <summary>Mod text only. Game nouns are resolved through the native string tables.</summary>
internal static class L
{
    // Standalone policy tests retain their original Chinese expectations.
    // The mod sets this from the game's selected locale during initialization.
    static volatile bool chinese = true;
    internal static bool Chinese => chinese;
    internal static string Language => chinese ? "zh" : "en";

    internal static bool IsChinese(string? localeCode) => !string.IsNullOrWhiteSpace(localeCode) &&
        (localeCode.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
         localeCode.StartsWith("zh-", StringComparison.OrdinalIgnoreCase) ||
         localeCode.StartsWith("zh_", StringComparison.OrdinalIgnoreCase));

    internal static bool SetGameLanguage(string? localeCode)
    {
        bool next = IsChinese(localeCode);
        if (next == chinese) return false;
        chinese = next;
        return true;
    }

    internal static string T(string original) => chinese ? original :
        LocalizationCatalog.English.TryGetValue(original, out string? translated) ? translated : original;

    // Keeping interpolation arguments separate prevents item names, feature text,
    // numbers and player supplied data from becoming translation lookup keys.
    internal static string F(FormattableString original) => string.Format(
        chinese ? CultureInfo.CurrentCulture : CultureInfo.InvariantCulture,
        T(original.Format), original.GetArguments());
}
