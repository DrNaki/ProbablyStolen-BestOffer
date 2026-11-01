using System;
using System.Reflection;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DeepSpaceBestOffer;

/// <summary>Observe completed game-owned operations without starting localization.</summary>
internal static class NativeLocaleReader
{
    // Mono exposes these as private fields; Il2CppInterop exposes field-read
    // properties. Never fall back to InitializationOperation, SelectedLocale or
    // SelectedLocaleAsync: all three may create/start an operation or block.
    static readonly ExistingMember initialization = new(typeof(LocalizationSettings), "m_InitializingOperationHandle");
    static readonly ExistingMember selection = new(typeof(LocalizationSettings), "m_SelectedLocaleAsync");

    internal static Locale? SelectedLocaleIfReady()
    {
        try
        {
            var settings = LocalizationSettings.GetInstanceDontCreateDefault();
            if (settings == null) return null;
            var init = initialization.Read<AsyncOperationHandle<LocalizationSettings>>(settings);
            if (!init.IsValid() || !init.IsDone || init.Status != AsyncOperationStatus.Succeeded) return null;
            var selected = selection.Read<AsyncOperationHandle<Locale>>(settings);
            return selected.IsValid() && selected.IsDone && selected.Status == AsyncOperationStatus.Succeeded
                ? selected.Result : null;
        }
        // Missing members, an unloaded settings asset or a released handle must
        // defer detection, never initialize a replacement or affect native UI.
        catch { return null; }
    }

    internal sealed class ExistingMember
    {
        readonly FieldInfo? field;
        readonly PropertyInfo? property;
        internal ExistingMember(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            field = type.GetField(name, flags);
            if (field == null) property = type.GetProperty(name, flags);
        }
        internal T Read<T>(object owner) => (field?.GetValue(owner) ?? property?.GetValue(owner)) is T value
            ? value : default!;
    }
}
