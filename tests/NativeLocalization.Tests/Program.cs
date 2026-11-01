using System.Reflection;
using DeepSpaceBestOffer;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
void NoStartupSideEffects(string scene)
{
    Check(LocalizationSettings.DefaultCreations == 0, scene + ": created default settings");
    Check(LocalizationSettings.EagerSelections == 0, scene + ": selected a locale before the game");
    Check(LocalizationSettings.Waits == 0, scene + ": waited for unfinished localization");
    Check(LocalizationSettings.StringDatabase.Reads == 0, scene + ": loaded a text table");
}
var mod = new BestOfferMod();
L.SetGameLanguage("zh-Hans");
Check(NativeLocalization.GameLocaleCode() == null, "Cold boot: a language probe initialized default English settings");
mod.RefreshForTest(true);
Check(L.Chinese && mod.UiDestructions == 0, "Cold boot forced English or rebuilt UI");
NoStartupSideEffects("No settings asset loaded");
L.SetGameLanguage("en");
using (NativeLocalization.UseModLanguage()) Check(NativeLocalization.GameLocaleCode() == null, "Early English text scope initialized localization");
L.SetGameLanguage("zh-Hans");
NoStartupSideEffects("Early third-party request for mod text");
bool earlyNameDeferred = false;
try { NativeLocalization.ItemName(new GameItem { identifier = "early", name = "loading" }); }
catch (InvalidOperationException) { earlyNameDeferred = true; }
Check(earlyNameDeferred, "Early direct item-name request must defer instead of loading tables");
bool earlyCategoryDeferred = false;
try { NativeLocalization.CategoryName("FOOD"); }
catch (InvalidOperationException) { earlyCategoryDeferred = true; }
Check(earlyCategoryDeferred, "Early direct category request must defer instead of loading tables");
NoStartupSideEffects("Early direct native name/category lookup");
var settings = LocalizationSettings.Existing = new();
mod.RefreshForTest(true);
Check(L.Chinese, "Settings asset without game-owned operations changed language");
NoStartupSideEffects("Before game initialization");
settings.Initialize(done: false); settings.Select("en");
Check(NativeLocalization.GameLocaleCode() == null, "Loading initialization was treated as ready");
mod.RefreshForTest(true);
Check(L.Chinese && mod.UiDestructions == 0, "Pending initialization committed default English");
NoStartupSideEffects("Slow disk / Addressables preload / other-mod delay");
settings.Initialize(status: AsyncOperationStatus.Failed);
Check(NativeLocalization.GameLocaleCode() == null, "Failed initialization was used");
settings.Initialize(valid: false);
Check(NativeLocalization.GameLocaleCode() == null, "Released initialization handle was used");
settings.Initialize(); settings.Select("zh-Hans", done: false);
Check(NativeLocalization.GameLocaleCode() == null, "Pending locale selection was read synchronously");
settings.Select("zh-Hans", status: AsyncOperationStatus.Failed);
Check(NativeLocalization.GameLocaleCode() == null, "Failed locale selection was used");
settings.Select(null);
Check(NativeLocalization.GameLocaleCode() == null, "Null completed locale was accepted");
settings.Select("en", valid: false);
Check(NativeLocalization.GameLocaleCode() == null, "Released locale handle was used");
NoStartupSideEffects("Incomplete/failed/released selections");
settings = LocalizationSettings.Existing = new();
Check(NativeLocalization.GameLocaleCode() == null, "Replacement settings inherited a stale ready state");
settings.Initialize(); settings.Select("zh-Hans");
Check(NativeLocalization.GameLocaleCode() == "zh-Hans", "Replacement settings did not recover after initialization");

foreach (var code in new[] { "zh-Hans", "en", "ja", "ko", "de", "fr", "ru", "es", "pl", "ptBR", "it", "zh", "en" })
{
    settings.Select(code);
    Check(NativeLocalization.GameLocaleCode() == code, "Wrong selected locale: " + code);
    mod.RefreshForTest(true);
    Check(L.Chinese == code.StartsWith("zh"), "Mod failed to follow completed game selection: " + code);
    Check(ReferenceEquals(NativeLocaleReader.SelectedLocaleIfReady(), LocalizationSettings.AvailableLocales.GetLocale(code)), "Probe changed selected locale");
}
settings.Select("zh-Hans"); mod.RefreshForTest(true);
int before = mod.UiDestructions;
settings.Select("en");
UnityEngine.Time.unscaledTime = .1f; mod.RefreshForTest();
Check(L.Chinese, "Language monitor ignored its polling interval");
UnityEngine.Time.unscaledTime = .6f; mod.RefreshForTest();
Check(!L.Chinese && mod.UiDestructions == before + 1, "Language monitor missed completed change");
settings.Select("zh-Hans", done: false);
UnityEngine.Time.unscaledTime = 1.2f; mod.RefreshForTest();
Check(!L.Chinese && mod.UiDestructions == before + 1, "Switching locales prematurely rebuilt UI");
settings.Select("zh-Hans");
UnityEngine.Time.unscaledTime = 1.8f; mod.RefreshForTest();
Check(L.Chinese && mod.UiDestructions == before + 2, "Completed Chinese switch did not recover");
NoStartupSideEffects("Completed language switching without reading game text");

// Field access on Mono and field-proxy property access on Il2CppInterop.
Check(new NativeLocaleReader.ExistingMember(typeof(FieldFixture), "value").Read<int>(new FieldFixture()) == 23, "Mono field reader");
Check(new NativeLocaleReader.ExistingMember(typeof(PropertyFixture), "value").Read<int>(new PropertyFixture()) == 42, "IL2CPP field-proxy property reader");
Check(new NativeLocaleReader.ExistingMember(typeof(FieldFixture), "missing").Read<int>(new FieldFixture()) == 0, "Changed game metadata must defer safely");

// Exercise the actual scoped prefixes: a non-English game uses English only
// inside mod text scopes. Exception unwind and nested scopes cannot leak it.
settings.Select("ja"); L.SetGameLanguage("ja");
var en = LocalizationSettings.AvailableLocales.GetLocale("en")!;
LocalizationSettings.StringDatabase.GetTable("UI", en).Add("test", "Offer {0}");
var scopeCode = typeof(NativeLocalization).GetMethod("GetScopedLocaleCode", BindingFlags.Static | BindingFlags.NonPublic)!;
var scopeUi = typeof(NativeLocalization).GetMethod("GetUI", BindingFlags.Static | BindingFlags.NonPublic)!;
bool Prefix(out string text)
{ object?[] args = { "original" }; bool run = (bool)scopeCode.Invoke(null, args)!; text = (string)args[0]!; return run; }
Check(Prefix(out _), "Outside scope must run native locale getter");
try
{
    using var outer = NativeLocalization.UseModLanguage();
    Check(!Prefix(out var scoped) && scoped == "en", "Mod scope did not override its own text");
    Check(Task.Run(() => Prefix(out _)).GetAwaiter().GetResult(), "Mod text scope leaked to another thread");
    object?[] formatArguments = { "test", new object[] { 12 }, "original" };
    Check(!(bool)scopeUi.Invoke(null, formatArguments)! && (string)formatArguments[2]! == "Offer 12", "Scoped string formatting");
    object?[] missingArguments = { "missing", null, "original" };
    Check((bool)scopeUi.Invoke(null, missingArguments)! && (string)missingArguments[2]! == "original", "Missing translation must defer to native fallback");
    object?[] invalidArguments = { "test", Array.Empty<object>(), "original" };
    LocalizationSettings.StringDatabase.GetTable("UI", en).Add("bad-format", "{1}");
    invalidArguments[0] = "bad-format"; invalidArguments[1] = new object[] { 12 };
    Check((bool)scopeUi.Invoke(null, invalidArguments)! && (string)invalidArguments[2]! == "original", "Formatting failure must not escape into native UI");
    LocalizationSettings.StringDatabase.MissingTable = true;
    Check((bool)scopeUi.Invoke(null, formatArguments)!, "Missing table must defer to native fallback");
    LocalizationSettings.StringDatabase.MissingTable = false;
    LocalizationSettings.StringDatabase.ThrowOnRead = true;
    Check((bool)scopeUi.Invoke(null, formatArguments)!, "Table-loading failure must defer to native fallback");
    LocalizationSettings.StringDatabase.ThrowOnRead = false;
    using (NativeLocalization.UseModLanguage()) Check(!Prefix(out _), "Nested scope lost override");
    Check(!Prefix(out _), "Nested scope did not restore outer override");
    throw new InvalidOperationException("Simulated third-party display failure");
}
catch (InvalidOperationException) { }
Check(Prefix(out _), "Exception leaked mod English into native UI");
Check(NativeLocalization.GameLocaleCode() == "ja", "Scope mutated global game language");
L.SetGameLanguage("zh-Hans");
using (NativeLocalization.UseModLanguage()) Check(Prefix(out _), "Chinese must retain native localization");
Check(LocalizationSettings.DefaultCreations == 0 && LocalizationSettings.Waits == 0, "Unexpected global initialization side effect");

// Locale refresh must discard name-cache fallbacks and preserve the Chinese
// identity used by legacy All rules, including when the UI is English.
var item = new GameItem { identifier = "sample", name = "原始商品" };
LocalizationSettings.StringDatabase.GetTable("Item", LocalizationSettings.AvailableLocales.GetLocale("zh-Hans")!).Add("item_sample_name", "原始商品");
LocalizationSettings.StringDatabase.GetTable("Item", en).Add("item_sample_name", "Original Item");
settings.Select("en"); mod.RefreshForTest(true);
Check(NativeLocalization.ItemName(item) == "Original Item", "English official item name");
Check(NativeLocalization.RuleName(item) == "原始商品", "English UI changed persistent Chinese rule identity");
LocalizationSettings.StringDatabase.GetTable("Item", en).Add("item_sample_name", "Updated Item");
settings.Select("zh-Hans"); mod.RefreshForTest(true);
Check(NativeLocalization.ItemName(item) == "原始商品", "Chinese official item name");
settings.Select("en"); mod.RefreshForTest(true);
Check(NativeLocalization.ItemName(item) == "Updated Item", "Language refresh kept a stale official name cache");
Check(NativeLocalization.RuleName(item) == "原始商品", "Language refresh changed legacy rule identity");
Console.WriteLine($"PASS: {checks} native localization startup, timing, language-switch and scope-isolation checks; no Unity/game process executed.");

#pragma warning disable CS0414 // The field is deliberately read through reflection.
sealed class FieldFixture { readonly int value = 23; }
sealed class PropertyFixture { public int value => 42; }
