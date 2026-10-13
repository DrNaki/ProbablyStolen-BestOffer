using System.Reflection;
using MelonLoader;
using UnityEngine;

namespace DeepSpaceModSupport;

// Optional integration: these mods still load when the manager is disabled/removed.
internal static class ManagerIntegration
{
    static FieldInfo? instanceField, panelField;
    static PropertyInfo? visibleProperty;
    static MethodInfo? setVisible;
    static bool searched;
    static object? Panel
    {
        get
        {
            if (!searched)
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("ModManager.ModManagerPlugin")).FirstOrDefault(t => t != null);
                if (type == null) return null;
                searched = true;
                instanceField = type.GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                panelField = type.GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic);
                visibleProperty = panelField?.FieldType.GetProperty("IsVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                setVisible = panelField?.FieldType.GetMethod("SetVisible", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(bool) }, null);
            }
            return panelField?.GetValue(instanceField?.GetValue(null));
        }
    }
    internal static bool Visible
    {
        get { try { var panel = Panel; return panel != null && visibleProperty?.GetValue(panel) is true; } catch { return false; } }
    }
    internal static bool Hide()
    {
        try
        {
            var panel = Panel;
            if (panel == null || setVisible == null) return !Visible;
            setVisible.Invoke(panel, new object[] { false });
            return !Visible;
        }
        catch { return false; }
    }
    // MelonLoader reconstructs categories from saved TOML, even when a mod no
    // longer registers entries. Remove this obsolete category from the live
    // registry so the manager cannot offer an ineffective duplicate editor.
    internal static void DetachLegacyCategory(string id)
    {
        MelonPreferences.Categories.RemoveAll(category => category.Identifier == id);
        // The manager also reconstructs unregistered rows directly from TOML.
        // The previous versions already mirrored applied settings into JSON.
        // Archive and remove only this mod's obsolete table, leaving other mods
        // byte-for-byte intact. Do this after importing the legacy hotkey.
        string path = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "MelonPreferences.cfg");
        if (!System.IO.File.Exists(path)) return;
        string original = System.IO.File.ReadAllText(path);
        string updated = System.Text.RegularExpressions.Regex.Replace(original,
            @"(?ms)^\[" + System.Text.RegularExpressions.Regex.Escape(id) + @"\]\r?\n.*?(?=^\[|\z)", "");
        if (updated == original) return;
        string archive = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "IndependentSettingsMigration");
        System.IO.Directory.CreateDirectory(archive);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original)))[..12];
        string backup = System.IO.Path.Combine(archive, id + "-" + hash + ".cfg");
        if (!System.IO.File.Exists(backup)) System.IO.File.Copy(path, backup);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        System.IO.File.WriteAllText(temporary, updated, new System.Text.UTF8Encoding(false));
        System.IO.File.Move(temporary, path, true);
    }
    internal static bool CaptureFunctionHotkey(Event evt, out string binding)
    {
        binding = "";
        for (int i = 0; i < 12; i++)
        {
            var key = (KeyCode)((int)KeyCode.F1 + i);
            if (evt.keyCode != key) continue;
            string prefix = (evt.control ? "Ctrl+" : "") + (evt.alt ? "Alt+" : "") + (evt.shift ? "Shift+" : "");
            binding = prefix + key; return true;
        }
        return false;
    }
    internal static string LegacyString(string category, string key, string fallback)
    {
        try
        {
            var path = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "MelonPreferences.cfg");
            if (!System.IO.File.Exists(path)) return fallback;
            bool inside = false;
            foreach (string raw in System.IO.File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("[")) { inside = line == "[" + category + "]"; continue; }
                if (!inside) continue;
                int equal = line.IndexOf('=');
                if (equal < 0 || line[..equal].Trim() != key) continue;
                return System.Text.Json.JsonSerializer.Deserialize<string>(line[(equal + 1)..].Trim()) ?? fallback;
            }
        }
        catch { }
        return fallback;
    }
    internal static bool Down(string binding)
    {
        if (!Application.isFocused || !KeyChord.TryParse(binding, out var chord) ||
            !Enum.TryParse<KeyCode>(chord.Key, true, out var key)) return false;
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        return ctrl == chord.Ctrl && alt == chord.Alt && shift == chord.Shift && Input.GetKeyDown(key);
    }
    internal static bool EventMatches(Event evt, string binding) => KeyChord.TryParse(binding, out var chord) &&
        Enum.TryParse<KeyCode>(chord.Key, true, out var key) && evt.keyCode == key &&
        evt.control == chord.Ctrl && evt.alt == chord.Alt && evt.shift == chord.Shift;
    internal static string NormalizeHotkey(string value, string fallback)
    {
        if (!KeyChord.TryParse(value, out var chord) || !Enum.TryParse<KeyCode>(chord.Key, true, out var key) ||
            !Enum.IsDefined(typeof(KeyCode), key) || key == KeyCode.None ||
            key is KeyCode.LeftControl or KeyCode.RightControl or KeyCode.LeftAlt or KeyCode.RightAlt or KeyCode.LeftShift or KeyCode.RightShift)
            return fallback;
        return (chord with { Key = key.ToString() }).ToString();
    }
}
