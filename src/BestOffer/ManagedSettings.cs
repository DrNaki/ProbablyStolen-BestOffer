using System.Globalization;
using DeepSpaceModSupport;
using MelonLoader;
using UnityEngine;

namespace DeepSpaceBestOffer;

public sealed partial class BestOfferMod
{
    string panelHotkey = "F8", auditHotkey = "F9";
    float toolbarX = -1, toolbarY = 18;
    bool managerWasVisible;
    MelonPreferences_Category? hotkeyCategory;
    MelonPreferences_Entry<string>? panelHotkeyPreference, auditHotkeyPreference;
    bool syncingHotkeys, managerHotkeysDirty;
    string pendingCapturedHotkey = "";
    int pendingCapturedFrame;
    int captureFinishedThroughFrame = -1;
    bool pendingPanelToggle, pendingAudit;
    int pendingActionFrame;
    float lastPanelActionAt = -1, lastAuditActionAt = -1;

    void InitializeLocalSettings()
    {
        ManagerIntegration.DetachLegacyCategory("BestOffer");
        hotkeyCategory = MelonPreferences.CreateCategory("BestOffer", "最佳报价 / Best Offer");
        panelHotkeyPreference = hotkeyCategory.CreateEntry("PanelHotkey", panelHotkey,
            "面板快捷键 / Panel Hotkey", "打开或关闭报价配置。支持 Ctrl / Alt / Shift 组合键。 / Toggle Offer Settings; modifiers supported.");
        auditHotkeyPreference = hotkeyCategory.CreateEntry("AuditHotkey", auditHotkey,
            "只读诊断快捷键 / Diagnostics Hotkey", "在报价配置中导出只读诊断。 / Export read-only diagnostics while Offer Settings is open.");
        panelHotkeyPreference.OnEntryValueChanged.Subscribe((_, _) => { if (!syncingHotkeys) managerHotkeysDirty = true; });
        auditHotkeyPreference.OnEntryValueChanged.Subscribe((_, _) => { if (!syncingHotkeys) managerHotkeysDirty = true; });
        SyncManagerHotkeys();
        RegisterLocalDetails();
    }
    void RegisterLocalDetails()
    {
        LocalModDetails.Register("最佳报价 / Best Offer", "最佳报价", "Best Offer",
            "点击交易 iPad 的齿轮「报价配置」或按面板快捷键（默认 F8），设置库存、分类、行情和货源。可在「交易设置」或本管理器中修改快捷键，两处同步并自动保存。机器与产物默认保留，包装默认整体出售；需求例外不能绕过买家拒收。",
            "Open Offer Settings on the trade tablet or use the panel hotkey (default F8) to configure inventory, categories, market preferences and sources. Change hotkeys in Trade Settings or this manager; both stay in sync and save automatically. Machine contents and output are opt-in; packages are sold whole by default. Request exceptions never override buyer restrictions.", L.Chinese);
    }
    void RefreshManagerVisibility()
    {
        if (!ManagerIntegration.Visible) return;
        managerWasVisible = true;
        if (IsOpen) Close();
    }
    void SyncManagerHotkeys()
    {
        if (hotkeyCategory == null || panelHotkeyPreference == null || auditHotkeyPreference == null) return;
        syncingHotkeys = true;
        try
        {
            panelHotkeyPreference.Value = panelHotkey;
            panelHotkeyPreference.EditedValue = panelHotkey;
            auditHotkeyPreference.Value = auditHotkey;
            auditHotkeyPreference.EditedValue = auditHotkey;
            hotkeyCategory.SaveToFile(false);
            ManagerIntegration.SyncHotkeyBindings(panelHotkey, auditHotkey);
        }
        finally { syncingHotkeys = false; }
    }
    void ApplyManagerHotkeys()
    {
        if (!managerHotkeysDirty || panelHotkeyPreference == null || auditHotkeyPreference == null) return;
        managerHotkeysDirty = false;
        string panel = ManagerIntegration.NormalizeHotkey(panelHotkeyPreference.Value, "");
        string audit = ManagerIntegration.NormalizeHotkey(auditHotkeyPreference.Value, "");
        if (!KeyChord.ValidHotkeyPair(panel, audit))
        {
            SyncManagerHotkeys();
            LoggerInstance.Warning(L.T("快捷键无效、使用了 Esc 或与另一快捷键重复，已保留原设置。"));
            return;
        }
        panelHotkey = panel; auditHotkey = audit;
        SaveSettings(); SyncManagerHotkeys(); uiDirty = true; SuppressTransitionInput();
    }
    void CaptureLocalHotkey()
    {
        if (!Application.isFocused) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        { CancelHotkeyCapture(); return; }
        if (pendingCapturedHotkey.Length > 0 && Time.frameCount > pendingCapturedFrame)
        { ApplyCapturedHotkey(pendingCapturedHotkey); return; }
        string binding = "";
        for (int i = 0; i < 12; i++)
        {
            var key = (KeyCode)((int)KeyCode.F1 + i);
            if (!Input.GetKeyDown(key)) continue;
            binding = new KeyChord(key.ToString(),
                ManagerIntegration.ModifierActive(KeyCode.LeftControl, KeyCode.RightControl),
                ManagerIntegration.ModifierActive(KeyCode.LeftAlt, KeyCode.RightAlt),
                ManagerIntegration.ModifierActive(KeyCode.LeftShift, KeyCode.RightShift)).ToString();
            break;
        }
        if (binding.Length == 0) return;
        // Give OnGUI this frame to supply the key event's exact modifiers.
        // Polling is a fallback for hosts which do not deliver GUI key events.
        pendingCapturedHotkey = binding; pendingCapturedFrame = Time.frameCount;
    }
    void CancelHotkeyCapture()
    {
        FinishHotkeyCapture();
    }
    void FinishHotkeyCapture()
    {
        captureHotkey = 0; pendingCapturedHotkey = "";
        pendingPanelToggle = pendingAudit = false;
        captureFinishedThroughFrame = Time.frameCount + 1;
        uiDirty = true; SuppressTransitionInput();
    }
    public override void OnGUI()
    {
        var evt = Event.current;
        if (!Application.isFocused || evt == null || evt.rawType != EventType.KeyDown || ManagerIntegration.Visible) return;
        if (captureHotkey == 0)
        { if (Time.frameCount > captureFinishedThroughFrame) HandleHotkeyEvent(evt); return; }
        if (evt.keyCode == KeyCode.Escape) CancelHotkeyCapture();
        else if (ManagerIntegration.CaptureFunctionHotkey(evt, out string binding)) ApplyCapturedHotkey(binding);
        else return;
        evt.Use();
    }
    void UpdateHotkeyActions()
    {
        // Defer polling one frame so the matching GUI event can correct quick
        // modifier taps, or reject a main key pressed with extra modifiers.
        if (Time.frameCount > pendingActionFrame)
        {
            if (pendingPanelToggle) ToggleFromHotkey();
            if (pendingAudit && IsOpen) AuditFromHotkey();
            pendingPanelToggle = pendingAudit = false;
        }
        if (ManagerIntegration.Down(panelHotkey)) pendingPanelToggle = true;
        if (IsOpen && ManagerIntegration.Down(auditHotkey)) pendingAudit = true;
        if (pendingPanelToggle || pendingAudit) pendingActionFrame = Time.frameCount;
    }
    void HandleHotkeyEvent(Event evt)
    {
        if (ManagerIntegration.EventKeyMatches(evt, panelHotkey))
        {
            pendingPanelToggle = false;
            if (ManagerIntegration.EventMatches(evt, panelHotkey))
            { ToggleFromHotkey(); evt.Use(); }
        }
        if (ManagerIntegration.EventKeyMatches(evt, auditHotkey))
        {
            pendingAudit = false;
            if (IsOpen && ManagerIntegration.EventMatches(evt, auditHotkey))
            { AuditFromHotkey(); evt.Use(); }
        }
    }
    // GUI and polled input can report one physical press on adjacent frames.
    void ToggleFromHotkey()
    {
        if (Time.unscaledTime - lastPanelActionAt < .12f) return;
        lastPanelActionAt = Time.unscaledTime; Toggle();
    }
    void AuditFromHotkey()
    {
        if (Time.unscaledTime - lastAuditActionAt < .12f) return;
        lastAuditActionAt = Time.unscaledTime; ExportHotkeyAudit();
    }
    void ExportHotkeyAudit()
    {
        var audit = adapter.DevelopmentOnlyAuditCapture(5, CurrentOptions());
        System.IO.File.WriteAllText(System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory,
            "BestOffer-capture-audit.json"), audit);
        LoggerInstance.Msg(L.T("只读报价状态审计已保存。"));
    }
    void ApplyCapturedHotkey(string binding)
    {
        pendingCapturedHotkey = "";
        if (binding == (captureHotkey == 1 ? auditHotkey : panelHotkey))
        { status = L.T("面板与诊断快捷键不能相同，请重新选择。"); uiDirty = true; return; }
        if (captureHotkey == 1) panelHotkey = binding; else auditHotkey = binding;
        FinishHotkeyCapture(); SaveSettings(); SyncManagerHotkeys();
        status = L.T("快捷键已保存。F5/F6 用于第三方 Mod，请避免与其他 Mod 的快捷键重复。");
    }
}
