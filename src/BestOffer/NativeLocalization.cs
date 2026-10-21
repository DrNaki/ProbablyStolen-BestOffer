using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using Locale = UnityEngine.Localization.Locale;

namespace DeepSpaceBestOffer;

/// <summary>
/// Resolve the game's own names in a scoped locale. SelectedLocale is never
/// changed: the surrounding game and other mods keep the player's language.
/// </summary>
internal static class NativeLocalization
{
    [ThreadStatic] static Locale? textLocale;
    static readonly Dictionary<string, Locale> locales = new(StringComparer.Ordinal);
    static readonly Dictionary<string, Dictionary<string, long>> nameKeys = new(StringComparer.Ordinal);
    static readonly Dictionary<(string Table, string Raw, string Key, string Language), string> translatedNames = new();
    // Verified native factory aliases. These names do not follow item_{id}_name.
    static readonly IReadOnlyDictionary<string, (string Table, string Key)> itemNameKeys =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["gene_scanner"] = ("Item", "item_genetic_scanner_name"),
            ["nutrient_tablet"] = ("Item", "item_hydroponic_nutrient_tablet_name"),
            ["beerCase"] = ("Item", "item_beer_case_name"),
            ["feces_small"] = ("Item", "husbandry_feces_small_name"),
            ["feces_medium"] = ("Item", "husbandry_feces_medium_name"),
            ["feces_large"] = ("Item", "husbandry_feces_large_name"),
            ["vacuum_robot"] = ("Item", "husbandry_vacuum_robot_name"),
            ["rat"] = ("Item", "husbandry_rat_name"),
            ["bandage_item"] = ("Item", "item_bandage_name"),
            ["hemostatic_bandage_item"] = ("Item", "item_hemostatic_bandage_name"),
            ["topical_bandage_item"] = ("Item", "item_topical_bandage_name"),
            ["hand_guide"] = ("UI", "book_hand_name"),
            ["aug_id_guide"] = ("UI", "ui_aug_guide_name"),
            ["hand"] = ("Item", "item_hand"),
            ["serum_red"] = ("Mechanic", "liquid_livestock_steroid"),
            ["serum_green"] = ("Mechanic", "liquid_livestock_immunity"),
            ["rev_codeword"] = ("UI", "intel_rev_codeword_title")
        };

    internal static void Initialize(HarmonyLib.Harmony harmony)
    {
        foreach (string table in new[] { "Item", "Mechanic", "UI" })
            harmony.Patch(AccessTools.Method(typeof(LocHelper), "GetLocalized" + table,
                new[] { typeof(string), typeof(Il2CppReferenceArray<Il2CppSystem.Object>) }),
                prefix: new HarmonyMethod(typeof(NativeLocalization), "Get" + table));
        harmony.Patch(AccessTools.Method(typeof(LocHelper), nameof(LocHelper.GetCurrentLocaleCode)),
            prefix: new HarmonyMethod(typeof(NativeLocalization), nameof(GetScopedLocaleCode)));
    }

    static bool GetItem(string key, Il2CppReferenceArray<Il2CppSystem.Object>? arguments, ref string __result) =>
        GetScoped("Item", key, arguments, ref __result);
    static bool GetMechanic(string key, Il2CppReferenceArray<Il2CppSystem.Object>? arguments, ref string __result) =>
        GetScoped("Mechanic", key, arguments, ref __result);
    static bool GetUI(string key, Il2CppReferenceArray<Il2CppSystem.Object>? arguments, ref string __result) =>
        GetScoped("UI", key, arguments, ref __result);

    static bool GetScoped(string table, string key, Il2CppReferenceArray<Il2CppSystem.Object>? arguments, ref string result)
    {
        if (textLocale == null) return true;
        var entry = LocalizationSettings.StringDatabase.GetTable(table, textLocale).GetEntry(key);
        result = entry == null ? key : entry.GetLocalizedString(textLocale.Identifier.CultureInfo?.Cast<Il2CppSystem.IFormatProvider>(),
            arguments?.Cast<Il2CppSystem.Collections.Generic.IList<Il2CppSystem.Object>>(), null);
        return false;
    }

    static bool GetScopedLocaleCode(ref string __result)
    {
        if (textLocale == null) return true;
        __result = textLocale.Identifier.Code;
        return false;
    }

    internal static string? GameLocaleCode()
    {
        // This is called outside text scopes, only from Unity's main thread.
        try { return LocalizationSettings.SelectedLocale?.Identifier.Code; }
        catch { return null; }
    }

    static Locale Resolve(string language)
    {
        if (locales.TryGetValue(language, out var cached) && cached != null) return cached;
        var available = LocalizationSettings.AvailableLocales;
        var locale = available.GetLocale(language == "zh" ? "zh-Hans" : "en");
        if (locale == null)
            foreach (var candidate in available.Locales)
                if (candidate != null && (language == "zh" ? L.IsChinese(candidate.Identifier.Code) :
                    candidate.Identifier.Code.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Identifier.Code.StartsWith("en-", StringComparison.OrdinalIgnoreCase)))
                { locale = candidate; break; }
        if (locale == null) throw new InvalidOperationException(language == "zh"
            ? "The game's Chinese name table is not ready."
            : "The game's English name table is not ready.");
        locales[language] = locale;
        return locale;
    }

    internal readonly struct TextScope : IDisposable
    {
        readonly Locale? previous;
        internal TextScope(Locale? locale) { previous = textLocale; textLocale = locale; }
        public void Dispose() => textLocale = previous;
    }

    internal static TextScope UseModLanguage() => new(L.Chinese ? null : Resolve("en"));

    static StringTable Table(string name, string language) =>
        LocalizationSettings.StringDatabase.GetTable(name, Resolve(language));

    static string CachedName(string tableName, string raw, string preferredKey, string language)
    {
        var key = (tableName, raw, preferredKey, language);
        if (translatedNames.TryGetValue(key, out string? cached)) return cached;
        return translatedNames[key] = ResolveCachedName(tableName, raw, preferredKey, language);
    }

    static string ResolveCachedName(string tableName, string raw, string preferredKey, string language)
    {
        var target = Table(tableName, language);
        var preferred = preferredKey.Length == 0 ? null : target.GetEntry(preferredKey);
        if (preferred?.LocalizedValue == raw) return raw;

        // Names are cached by the native item/client factories. Resolve their
        // original shared table ID, then read that same entry in the target
        // language. This also covers aliases whose key differs from identifier.
        var available = LocalizationSettings.AvailableLocales.Locales;
        var ordered = new List<Locale>();
        var current = LocalizationSettings.SelectedLocale;
        if (current != null) ordered.Add(current);
        var zh = Resolve("zh"); var en = Resolve("en");
        if (!ordered.Any(l => l.Identifier.Code == zh.Identifier.Code)) ordered.Add(zh);
        if (!ordered.Any(l => l.Identifier.Code == en.Identifier.Code)) ordered.Add(en);
        foreach (var locale in available)
            if (locale != null && !ordered.Any(l => l.Identifier.Code == locale.Identifier.Code)) ordered.Add(locale);
        foreach (var locale in ordered)
        {
            var source = LocalizationSettings.StringDatabase.GetTable(tableName, locale);
            var expected = preferredKey.Length == 0 ? null : source.GetEntry(preferredKey);
            if (expected?.LocalizedValue == raw && preferred != null) return preferred.LocalizedValue;
        }
        // Prefer a verified identity match in any source language before a
        // reverse name lookup: different English names can share one translation.
        foreach (var locale in ordered)
        {
            var source = LocalizationSettings.StringDatabase.GetTable(tableName, locale);
            string cacheKey = tableName + ":" + locale.Identifier.Code;
            if (!nameKeys.TryGetValue(cacheKey, out var index))
            {
                index = new(StringComparer.Ordinal);
                foreach (var shared in source.SharedData.Entries)
                {
                    if (tableName == "Item" && !shared.Key.EndsWith("_name", StringComparison.Ordinal)) continue;
                    var entry = source.GetEntry(shared.Id);
                    if (entry != null && !string.IsNullOrWhiteSpace(entry.LocalizedValue)) index.TryAdd(entry.LocalizedValue, shared.Id);
                }
                nameKeys[cacheKey] = index;
            }
            if (index.TryGetValue(raw, out long id) && target.GetEntry(id) is { } translated)
                return translated.LocalizedValue;
        }
        // User-defined and third-party item names have no official translation.
        // Keep them verbatim instead of inventing a game item name.
        return raw;
    }

    static string ItemNameIn(GameItem item, string language)
    {
        var key = itemNameKeys.TryGetValue(item.identifier ?? "", out var alias) ? alias :
            (Table: "Item", Key: "item_" + item.identifier + "_name");
        return CachedName(key.Table, item.GetDisplayName(false) ?? item.name ?? item.identifier ?? L.T("物品"), key.Key, language);
    }

    internal static string ItemName(GameItem item) => ItemNameIn(item, L.Chinese ? "zh" : "en");

    internal static string ClientName(StoreClient client) => CachedName("ClientName",
        client.displayName ?? L.T("买家"), "name_" + client.identifier, L.Chinese ? "zh" : "en");

    internal static string CategoryName(string type) => Table("Mechanic", L.Chinese ? "zh" : "en")
        .GetEntry("type_" + type.ToLowerInvariant())?.LocalizedValue ?? TypeHelper.GetTypeDisplayName(type);

    internal static string RuleName(GameItem item)
    {
        // 1.5.0 persisted All rules under their Chinese display names. Continue
        // using those exact names in either UI language, including empty groups
        // that are restocked later. No destructive or one-shot migration needed.
        return ItemNameIn(item, "zh");
    }
}

public sealed partial class BestOfferMod
{
    float languageCheckAt;
    string gameLocaleCode = "";

    void RefreshGameLanguage(bool force = false)
    {
        if (!force && UnityEngine.Time.unscaledTime < languageCheckAt) return;
        languageCheckAt = UnityEngine.Time.unscaledTime + .5f;
        string? code = NativeLocalization.GameLocaleCode();
        if (code == null && !force) return;
        code ??= "";
        if (!force && code == gameLocaleCode) return;
        gameLocaleCode = code;
        L.SetGameLanguage(code);
        // A background answer and every displayed name belong to the old
        // language snapshot. Discard it before accepting further input.
        revision++; plans.Clear(); showConfirm = false;
        quickNotice = quickNoticeDetail = quickNoticeClientKey = ""; quickNoticeUntil = 0;
        groupQuantities.Clear(); groupModes.Clear(); ResetInventoryNavigation();
        DestroyNativeUI(); refreshAt = 0;
        snapshot = IsOpen ? adapter.Capture(CurrentOptions()) : null;
        status = snapshot?.CanTrade == true ? L.T("选择货源并设置必卖 / 禁卖，再计算最高实收方案。") :
            snapshot?.Message ?? L.T("点击「计算最佳报价」开始。");
        if (settingsPath.Length > 0) RegisterLocalDetails();
    }
}
