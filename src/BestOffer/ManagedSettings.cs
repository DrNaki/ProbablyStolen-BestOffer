using System.Globalization;
using DeepSpaceModSupport;
using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace DeepSpaceBestOffer;

public sealed partial class BestOfferMod
{
    string panelHotkey = "F8", auditHotkey = "F9";
    string placeHotkey = "Ctrl+F8", sellHotkey = "Ctrl+F9";
    float toolbarX = -1, toolbarY = 18;
    bool managerWasVisible;
    MelonPreferences_Category? hotkeyCategory;
    MelonPreferences_Entry<string>? panelHotkeyPreference, auditHotkeyPreference, placeHotkeyPreference, sellHotkeyPreference;
    bool syncingHotkeys, managerHotkeysDirty;
    string pendingCapturedHotkey = "";
    int pendingCapturedFrame;
    int captureFinishedThroughFrame = -1;
    bool pendingPanelToggle, pendingAudit, pendingPlace, pendingSell;
    int pendingActionFrame;
    float lastPanelActionAt = -1, lastAuditActionAt = -1, lastTradeActionAt = -1;
    KeyCode heldTradeKey = KeyCode.None;

    void InitializeLocalSettings()
    {
        ManagerIntegration.DetachLegacyCategory("BestOffer");
        hotkeyCategory = MelonPreferences.CreateCategory("BestOffer", "最佳报价 / Best Offer");
        panelHotkeyPreference = hotkeyCategory.CreateEntry("PanelHotkey", panelHotkey,
            "面板快捷键 / Panel Hotkey", "打开或关闭报价配置。支持 Ctrl / Alt / Shift 组合键。 / Toggle Offer Settings; modifiers supported.");
        auditHotkeyPreference = hotkeyCategory.CreateEntry("AuditHotkey", auditHotkey,
            "只读诊断快捷键 / Diagnostics Hotkey", "在报价配置中导出只读诊断。 / Export read-only diagnostics while Offer Settings is open.");
        placeHotkeyPreference = hotkeyCategory.CreateEntry("PlaceHotkey", placeHotkey,
            "一键摆货快捷键 / Auto-fill Hotkey", "按规则计算并摆货，不自动成交。留空禁用。 / Find and place an offer without selling. Empty disables.");
        sellHotkeyPreference = hotkeyCategory.CreateEntry("SellHotkey", sellHotkey,
            "一键售卖快捷键 / Quick Sell Hotkey", "直接计算并出售最佳方案，无需再次确认。留空禁用。 / Find and sell the best offer immediately. Empty disables.");
        placeHotkeyPreference.OnEntryValueChanged.Subscribe((_, _) => { if (!syncingHotkeys) managerHotkeysDirty = true; });
        sellHotkeyPreference.OnEntryValueChanged.Subscribe((_, _) => { if (!syncingHotkeys) managerHotkeysDirty = true; });
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
        if (hotkeyCategory == null || panelHotkeyPreference == null || auditHotkeyPreference == null || placeHotkeyPreference == null || sellHotkeyPreference == null) return;
        syncingHotkeys = true;
        try
        {
            panelHotkeyPreference.Value = panelHotkey;
            panelHotkeyPreference.EditedValue = panelHotkey;
            auditHotkeyPreference.Value = auditHotkey;
            auditHotkeyPreference.EditedValue = auditHotkey;
            placeHotkeyPreference.Value = placeHotkey; placeHotkeyPreference.EditedValue = placeHotkey;
            sellHotkeyPreference.Value = sellHotkey; sellHotkeyPreference.EditedValue = sellHotkey;
            hotkeyCategory.SaveToFile(false);
            ManagerIntegration.SyncHotkeyBindings(panelHotkey, auditHotkey, placeHotkey, sellHotkey);
        }
        finally { syncingHotkeys = false; }
    }
    void ApplyManagerHotkeys()
    {
        if (!managerHotkeysDirty || panelHotkeyPreference == null || auditHotkeyPreference == null || placeHotkeyPreference == null || sellHotkeyPreference == null) return;
        managerHotkeysDirty = false;
        bool panelValid = TryNormalizeOptionalHotkey(panelHotkeyPreference.Value, out var panel);
        bool auditValid = TryNormalizeOptionalHotkey(auditHotkeyPreference.Value, out var audit);
        bool placeValid = TryNormalizeOptionalHotkey(placeHotkeyPreference.Value, out var place);
        bool sellValid = TryNormalizeOptionalHotkey(sellHotkeyPreference.Value, out var sell);
        if (!panelValid || !auditValid || !placeValid || !sellValid || !KeyChord.ValidHotkeySet(panel, audit, place, sell))
        {
            SyncManagerHotkeys();
            LoggerInstance.Warning(L.T("快捷键无效、使用了 Esc 或与另一快捷键重复，已保留原设置。"));
            return;
        }
        panelHotkey = panel; auditHotkey = audit; placeHotkey = place; sellHotkey = sell;
        ClearPendingHotkeyActions();
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
        ClearPendingHotkeyActions();
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
    void ClearPendingHotkeyActions() => pendingPanelToggle = pendingAudit = pendingPlace = pendingSell = false;
    void RepairActionHotkeys()
    {
        // Preserve old custom panel/diagnostic chords. Disable only new actions
        // if their defaults conflict with bindings that predate this version.
        if (!KeyChord.ValidHotkeySet(panelHotkey, auditHotkey, placeHotkey, "")) placeHotkey = "";
        if (!KeyChord.ValidHotkeySet(panelHotkey, auditHotkey, placeHotkey, sellHotkey)) sellHotkey = "";
    }
    static bool TryNormalizeOptionalHotkey(string raw, out string normalized)
    {
        normalized = ManagerIntegration.NormalizeHotkey(raw, "");
        return string.IsNullOrWhiteSpace(raw) || normalized.Length > 0;
    }
    bool CanUseTradeHotkey()
    {
        if (!Application.isFocused || captureHotkey != 0 || ManagerIntegration.Visible || busy ||
            EmporiumEntry.Instance == null || !CanQuickApplyFromUI()) return false;
        var neg = NegociationUIManager.Instance;
        if (neg?.uiObject == null || !neg.uiObject.activeInHierarchy) return false;
        var focused = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
        if (focused != null && (focused.GetComponent<UnityEngine.UI.InputField>()?.isFocused == true ||
            focused.GetComponent<Il2CppTMPro.TMP_InputField>()?.isFocused == true)) return false;
        foreach (var escape in Resources.FindObjectsOfTypeAll<EscapeUIManager>())
            if (escape != null && escape.menuPanel != null && escape.menuPanel.activeInHierarchy) return false;
        return true;
    }
    void UpdateHotkeyActions()
    {
        if (heldTradeKey != KeyCode.None && !Input.GetKey(heldTradeKey) && !Input.GetKeyDown(heldTradeKey)) heldTradeKey = KeyCode.None;
        if (!Application.isFocused) { ClearPendingHotkeyActions(); return; }
        if (Time.frameCount > pendingActionFrame)
        {
            bool panel = pendingPanelToggle, audit = pendingAudit, place = pendingPlace, sell = pendingSell;
            ClearPendingHotkeyActions();
            if (panel) ToggleFromHotkey();
            if (audit && IsOpen) AuditFromHotkey();
            if (sell && CanUseTradeHotkey()) TradeFromHotkey(true);
            else if (place && CanUseTradeHotkey()) TradeFromHotkey(false);
        }
        if (ManagerIntegration.Down(panelHotkey)) pendingPanelToggle = true;
        if (IsOpen && ManagerIntegration.Down(auditHotkey)) pendingAudit = true;
        bool placeDown = ManagerIntegration.Down(placeHotkey), sellDown = ManagerIntegration.Down(sellHotkey);
        if ((placeDown || sellDown) && CanUseTradeHotkey())
        {
            pendingPlace |= placeDown; pendingSell |= sellDown;
        }
        if (pendingPanelToggle || pendingAudit || pendingPlace || pendingSell) pendingActionFrame = Time.frameCount;
    }
    void HandleHotkeyEvent(Event evt)
    {
        if (ManagerIntegration.EventKeyMatches(evt, panelHotkey))
        {
            pendingPanelToggle = false;
            if (ManagerIntegration.EventMatches(evt, panelHotkey)) { ToggleFromHotkey(); evt.Use(); return; }
        }
        if (ManagerIntegration.EventKeyMatches(evt, auditHotkey))
        {
            pendingAudit = false;
            if (IsOpen && ManagerIntegration.EventMatches(evt, auditHotkey)) { AuditFromHotkey(); evt.Use(); return; }
        }
        if (ManagerIntegration.EventKeyMatches(evt, placeHotkey))
        {
            pendingPlace = false;
            if (CanUseTradeHotkey() && ManagerIntegration.EventMatches(evt, placeHotkey)) { TradeFromHotkey(false); evt.Use(); return; }
        }
        if (ManagerIntegration.EventKeyMatches(evt, sellHotkey))
        {
            pendingSell = false;
            if (CanUseTradeHotkey() && ManagerIntegration.EventMatches(evt, sellHotkey)) { TradeFromHotkey(true); evt.Use(); }
        }
    }
    void TradeFromHotkey(bool sell)
    {
        if (!CanUseTradeHotkey() || heldTradeKey != KeyCode.None || Time.unscaledTime - lastTradeActionAt < .12f) return;
        if (KeyChord.TryParse(sell ? sellHotkey : placeHotkey, out var chord) && Enum.TryParse<KeyCode>(chord.Key, true, out var key)) heldTradeKey = key;
        lastTradeActionAt = Time.unscaledTime;
        SuppressTransitionInput();
        Calculate(true, sell);
        if (!busy) ShowQuickNotice(status);
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
    void ClearHotkey(int action)
    {
        switch (action)
        {
            case 1: panelHotkey = ""; break;
            case 2: auditHotkey = ""; break;
            case 3: placeHotkey = ""; break;
            case 4: sellHotkey = ""; break;
            default: return;
        }
        FinishHotkeyCapture(); SaveSettings(); SyncManagerHotkeys();
    }
    void ApplyCapturedHotkey(string binding)
    {
        pendingCapturedHotkey = "";
        string panel = captureHotkey == 1 ? binding : panelHotkey;
        string audit = captureHotkey == 2 ? binding : auditHotkey;
        string place = captureHotkey == 3 ? binding : placeHotkey;
        string sell = captureHotkey == 4 ? binding : sellHotkey;
        if (!KeyChord.ValidHotkeySet(panel, audit, place, sell))
        { status = L.T("快捷键不能重复，请重新选择。"); uiDirty = true; return; }
        panelHotkey = panel; auditHotkey = audit; placeHotkey = place; sellHotkey = sell;
        FinishHotkeyCapture(); SaveSettings(); SyncManagerHotkeys();
        status = L.T("快捷键已保存。F5/F6 用于第三方 Mod，请避免与其他 Mod 的快捷键重复。");
    }
}
