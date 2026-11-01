using DeepSpaceModSupport;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeepSpaceBestOffer;

public sealed partial class BestOfferMod
{
    internal const string WorkshopModId = "naki.bestoffer";
    ModConfig? workshopConfig;
    ConfigEntry<string>? workshopPanelKey, workshopAuditKey;
    CustomUIWindow? workshopSettingsWindow;
    bool syncingWorkshopKeys;

    internal bool WorkshopSettingsVisible => workshopSettingsWindow?.IsVisible == true &&
        workshopSettingsWindow.root.activeInHierarchy;

    static string NormalizeWorkshopKey(string value)
    {
        string normalized = ManagerIntegration.NormalizeHotkey(value, "");
        if (!KeyChord.TryParse(normalized, out var chord) || !Enum.TryParse<KeyCode>(chord.Key, true, out var key)) return "";
        return key is KeyCode.LeftWindows or KeyCode.RightWindows or KeyCode.LeftCommand or
            KeyCode.RightCommand or KeyCode.AltGr ? "" : normalized;
    }

    void InitializeLocalSettings()
    {
        workshopConfig = ModConfig.For(WorkshopModId);
        bool hasNativeConfig = File.Exists(workshopConfig.FilePath);
        panelHotkey = NormalizeWorkshopKey(panelHotkey); auditHotkey = NormalizeWorkshopKey(auditHotkey);
        if (!KeyChord.ValidHotkeyPair(panelHotkey, auditHotkey)) { panelHotkey = "F8"; auditHotkey = "F9"; }
        const string section = "快捷键 / Hotkeys";
        workshopPanelKey = workshopConfig.Define("PanelHotkey", "F8", section,
            () => L.Chinese ? "面板快捷键" : "Offer Settings hotkey",
            tooltip: () => L.Chinese ? "例如 F8 或 Ctrl+F10。点击应用后立即生效。" :
                "For example F8 or Ctrl+F10. Apply to use immediately.");
        workshopAuditKey = workshopConfig.Define("AuditHotkey", "F9", section,
            () => L.Chinese ? "只读诊断快捷键" : "Read-only diagnostics hotkey",
            tooltip: () => L.Chinese ? "报价面板打开时导出诊断。不能使用 Esc，也不能与面板快捷键重复。" :
                "Export diagnostics while Offer Settings is open. Escape and duplicate hotkeys are not allowed.");
        // Migrate the existing JSON pair once. Subsequent native settings,
        // including edits while the mod was disabled, use the native config.
        if (hasNativeConfig)
        {
            var panel = NormalizeWorkshopKey(workshopPanelKey.Value);
            var audit = NormalizeWorkshopKey(workshopAuditKey.Value);
            if (KeyChord.ValidHotkeyPair(panel, audit))
            { panelHotkey = panel; auditHotkey = audit; SaveSettings(); }
        }
        SyncManagerHotkeys();
        ModHook.OnModConfigApplied += WorkshopConfigApplied;
    }

    void RegisterLocalDetails() { }
    void ApplyManagerHotkeys() { } // The native Apply hook provides an atomic pair.

    void RefreshManagerVisibility()
    {
        if (!WorkshopSettingsVisible) return;
        managerWasVisible = true;
        if (IsOpen) Close();
    }

    void SyncManagerHotkeys()
    {
        if (workshopConfig == null || workshopPanelKey == null || workshopAuditKey == null) return;
        syncingWorkshopKeys = true;
        try
        {
            workshopPanelKey.Set(panelHotkey);
            workshopAuditKey.Set(auditHotkey);
            workshopConfig.Save();
        }
        finally { syncingWorkshopKeys = false; }
    }

    void WorkshopConfigApplied(string modId)
    {
        if (modId != WorkshopModId || syncingWorkshopKeys || workshopPanelKey == null || workshopAuditKey == null) return;
        var panel = NormalizeWorkshopKey(workshopPanelKey.Value);
        var audit = NormalizeWorkshopKey(workshopAuditKey.Value);
        bool valid = KeyChord.ValidHotkeyPair(panel, audit);
        if (valid)
        {
            panelHotkey = panel;
            auditHotkey = audit;
            pendingPanelToggle = pendingAudit = false;
            SaveSettings();
            uiDirty = true;
            SuppressTransitionInput();
        }
        SyncManagerHotkeys();
        workshopSettingsWindow?.Get("PanelHotkey")?.SetString(panelHotkey);
        workshopSettingsWindow?.Get("AuditHotkey")?.SetString(auditHotkey);
        ShowWorkshopSettingsNotice(valid
            ? (L.Chinese ? "快捷键已保存并立即生效，与店铺的交易设置同步。" : "Hotkeys saved and active. Trade Settings uses the same bindings.")
            : (L.Chinese ? "快捷键无效：不能使用 Esc、单独的修饰键或重复组合。已保留之前的设置。" :
                "Invalid hotkeys: Escape, modifiers alone and duplicates are not allowed. Previous settings retained."), !valid);
    }

    internal void WorkshopSettingsOpened(CustomUIWindow window)
    {
        workshopSettingsWindow = window;
        window.onClose += () =>
        {
            if (ReferenceEquals(workshopSettingsWindow, window)) workshopSettingsWindow = null;
            SuppressTransitionInput();
        };
        ShowWorkshopSettingsNotice(L.Chinese ? "输入 F8 或 Ctrl+F10 等组合，点击应用。也可在店铺的交易设置中改键。" :
            "Enter F8 or a chord such as Ctrl+F10, then Apply. You can also rebind in Trade Settings.", false);
    }

    void ShowWorkshopSettingsNotice(string message, bool error)
    {
        var note = workshopSettingsWindow?.Get("restart_note");
        if (note?.text == null) return;
        float width = Mathf.Max(280, workshopSettingsWindow!.ContentRoot.rect.width - 32);
        note.SetFixedWidth(width).SetText(message).SetTextColor(error ? TabletStyle.RED : TabletStyle.GREY_FACE).SetVisible(true);
        note.text.enableWordWrapping = true;
        note.text.overflowMode = TextOverflowModes.Overflow;
        var layout = note.root.GetComponent<LayoutElement>() ?? note.root.AddComponent<LayoutElement>();
        layout.minHeight = layout.preferredHeight = Mathf.Max(48, note.text.GetPreferredValues(message, width, 0).y + 8);
        LayoutRebuilder.MarkLayoutForRebuild(workshopSettingsWindow.ContentRoot);
    }

    internal bool HideWorkshopSettings()
    {
        workshopSettingsWindow?.Close();
        return !WorkshopSettingsVisible;
    }

    internal void DisposeWorkshopSettings()
    {
        ModHook.OnModConfigApplied -= WorkshopConfigApplied;
        workshopSettingsWindow?.Close();
        workshopSettingsWindow = null;
    }
}

[HarmonyPatch(typeof(ModConfigWindow), nameof(ModConfigWindow.Open))]
static class BestOfferConfigWindowPatch
{
    static void Postfix(LoadedMod mod, CustomUIWindow __result)
    {
        if (mod?.Manifest?.ID == BestOfferMod.WorkshopModId && __result != null)
            BestOfferMod.Current?.WorkshopSettingsOpened(__result);
    }
}
