using System.Globalization;
using System.Reflection;

#pragma warning disable CS0414 // Fields consumed by the production partial class and reflection.

// Controlled replay of the observed Unity API contract. These fixtures never
// load or execute game assemblies, Steam, Unity, other mods or player saves.
namespace UnityEngine { public static class Time { public static float unscaledTime; } }
namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }
    public readonly struct AsyncOperationHandle<T>(bool valid, bool done, AsyncOperationStatus status, T? result)
    {
        public bool IsValid() => valid;
        public bool IsDone => done;
        public AsyncOperationStatus Status => status;
        public T? Result => done ? result : throw new InvalidOperationException("Read unfinished operation result");
    }
}
namespace UnityEngine.Localization
{
    public sealed class Locale(string code)
    {
        public LocaleIdentifier Identifier { get; } = new(code);
    }
    public sealed class LocaleIdentifier(string code)
    {
        public string Code { get; } = code;
        public CultureInfo CultureInfo => CultureInfo.InvariantCulture;
    }
}
namespace UnityEngine.Localization.Tables
{
    public sealed class SharedEntry { public string Key = ""; public long Id; }
    public sealed class SharedData { public List<SharedEntry> Entries = new(); }
    public sealed class StringTableEntry(string value)
    {
        public string LocalizedValue = value;
        public string GetLocalizedString(IFormatProvider? culture, IList<object>? arguments, object? unused) =>
            arguments == null || arguments.Count == 0 ? LocalizedValue : string.Format(culture, LocalizedValue, arguments.ToArray());
    }
    public sealed class StringTable
    {
        public SharedData SharedData = new();
        readonly Dictionary<string, StringTableEntry> entries = new();
        public StringTableEntry? GetEntry(string key) => entries.GetValueOrDefault(key);
        public StringTableEntry? GetEntry(long id) => SharedData.Entries.FirstOrDefault(e => e.Id == id) is { } e ? GetEntry(e.Key) : null;
        public void Add(string key, string value)
        { entries[key] = new(value); SharedData.Entries.Add(new() { Key = key, Id = SharedData.Entries.Count + 1 }); }
    }
}
namespace UnityEngine.Localization.Settings
{
    using UnityEngine.Localization;
    using UnityEngine.Localization.Tables;
    using UnityEngine.ResourceManagement.AsyncOperations;
    public sealed class LocalesProvider
    {
        public List<Locale> Locales = new[] { "en", "zh-Hans", "zh", "ja", "ko", "de", "fr", "ru", "es", "pl", "ptBR", "it" }
            .Select(code => new Locale(code)).ToList();
        public Locale? GetLocale(string code) => Locales.FirstOrDefault(l => l.Identifier.Code == code);
    }
    public sealed class StringDatabaseFixture
    {
        public int Reads;
        public bool ThrowOnRead, MissingTable;
        readonly Dictionary<(string, string), StringTable> tables = new();
        public StringTable GetTable(string name, Locale locale)
        {
            Reads++;
            if (ThrowOnRead) throw new InvalidOperationException("Simulated unavailable Addressables table");
            if (MissingTable) return null!;
            return tables.TryGetValue((name, locale.Identifier.Code), out var t) ? t :
                tables[(name, locale.Identifier.Code)] = new();
        }
    }
    public sealed class LocalizationSettings
    {
        public static LocalizationSettings? Existing;
        public static int DefaultCreations, EagerSelections, Waits;
        AsyncOperationHandle<LocalizationSettings> m_InitializingOperationHandle;
        AsyncOperationHandle<Locale> m_SelectedLocaleAsync;
        public static readonly LocalesProvider AvailableLocales = new();
        public static readonly StringDatabaseFixture StringDatabase = new();
        public static LocalizationSettings? GetInstanceDontCreateDefault() => Existing;
        public static LocalizationSettings Instance
        {
            get { if (Existing == null) { DefaultCreations++; Existing = new(); } return Existing; }
        }
        public static Locale? SelectedLocale
        {
            get
            {
                var settings = Instance;
                if (!settings.m_SelectedLocaleAsync.IsValid())
                { EagerSelections++; settings.Select("en"); }
                if (!settings.m_SelectedLocaleAsync.IsDone) Waits++;
                return settings.m_SelectedLocaleAsync.Result;
            }
        }
        public void Initialize(bool valid = true, bool done = true, AsyncOperationStatus status = AsyncOperationStatus.Succeeded)
            => m_InitializingOperationHandle = new(valid, done, status, this);
        public void Select(string? code, bool valid = true, bool done = true, AsyncOperationStatus status = AsyncOperationStatus.Succeeded)
            => m_SelectedLocaleAsync = new(valid, done, status, code == null ? null : AvailableLocales.GetLocale(code));
    }
}
namespace HarmonyLib
{
    public sealed class Harmony { public void Patch(MethodInfo? method, HarmonyMethod prefix) { } }
    public sealed class HarmonyMethod { public HarmonyMethod(Type type, string name) { } }
    public static class AccessTools
    {
        public static MethodInfo? Method(Type type, string name, Type[]? args = null) => args == null
            ? type.GetMethod(name) : type.GetMethod(name, args);
    }
}
public static class LocHelper
{
    public static string GetCurrentLocaleCode() => UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale?.Identifier.Code ?? "";
    public static string GetLocalizedItem(string key, object[]? arguments) => "native item";
    public static string GetLocalizedMechanic(string key, object[]? arguments) => "native mechanic";
    public static string GetLocalizedUI(string key, object[]? arguments) => "native UI";
}
public static class TypeHelper { public static string GetTypeDisplayName(string type) => type; }
public sealed class GameItem { public string? identifier, name; public string? GetDisplayName(bool value) => name; }
public sealed class StoreClient { public string? displayName, identifier; }
namespace DeepSpaceBestOffer
{
    public sealed class SnapshotFixture { public bool CanTrade; public string? Message; }
    public sealed class AdapterFixture { public SnapshotFixture Capture(object options) => new(); }
    public sealed partial class BestOfferMod
    {
        int revision;
        readonly List<string> plans = new();
        bool showConfirm;
        string quickNotice = "", quickNoticeDetail = "", quickNoticeClientKey = "", status = "", settingsPath = "";
        float quickNoticeUntil, refreshAt;
        readonly Dictionary<string, int> groupQuantities = new(), groupModes = new();
        readonly AdapterFixture adapter = new();
        SnapshotFixture? snapshot;
        public bool IsOpen;
        public int UiDestructions;
        void ResetInventoryNavigation() { }
        void DestroyNativeUI() => UiDestructions++;
        void RegisterLocalDetails() { }
        object CurrentOptions() => new();
        public void RefreshForTest(bool force = false) => RefreshGameLanguage(force);
    }
}
