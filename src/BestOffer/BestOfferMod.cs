using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using DeepSpaceModSupport;

[assembly: MelonInfo(typeof(DeepSpaceBestOffer.BestOfferMod), "最佳报价 / Best Offer", "1.4.3", "Naki & Codex")]
[assembly: MelonGame("Questing Goose Studio", "Probably Stolen")]

namespace DeepSpaceBestOffer;

public sealed partial class BestOfferMod : MelonMod
{
    public static BestOfferMod? Current;
    public bool IsOpen { get; private set; }
    TradeSnapshot? snapshot;
    readonly GameTradeAdapter adapter = new();
    readonly Dictionary<string, int> rules = new();
    List<OfferPlan> plans = new();
    int selected, itemPage, planPage, revision, filter, detailPage;
    string status = "点击「计算最佳报价」开始。";
    bool showConfirm;
    GameObject? blocker;
    sealed record PendingCalculation(Task<SolveResult> Work, int Revision, bool QuickApply,
        TradeSnapshot Snapshot, string Signature);
    PendingCalculation? pending;
    bool busy => pending != null;
    bool QuickApplying => pending is { QuickApply: true } && pending.Revision == revision;
    string quickNotice = "";
    string quickNoticeDetail = "", quickNoticeClientKey = "";
    float quickNoticeUntil;
    string rulePath = "";
    string settingsPath = "";
    float refreshAt;
    int suppressNativeInputThroughFrame = -1;
    bool suppressMouseUntilReleased;
    bool includeShowcase = true, includeHiddenStorage = true, includeContainerContents;
    bool includeMachineContents, includeMachineProducts, splitRetailPackages;
    bool blockMarketDiscounts, preferMarketMarkups;
    bool allowOverBudget, overBudgetPercent = true;
    bool blockContraband, blockStolen;
    bool allowContrabandDemandExceptions, allowStolenDemandExceptions;
    int captureHotkey;
    string overBudgetInput = "10";
    decimal lastValidOverBudgetValue = 10m;

    public override void OnInitializeMelon()
    {
        Current = this;
        rulePath = Path.Combine(MelonEnvironment.UserDataDirectory, "BestOffer-rules.json");
        settingsPath = Path.Combine(MelonEnvironment.UserDataDirectory, "BestOffer-settings.json");
        try
        {
            if (File.Exists(rulePath))
                foreach (var p in System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(rulePath)) ?? new())
                    if (p.Value is 1 or 2) rules[p.Key] = p.Value;
        }
        catch (Exception e) { LoggerInstance.Warning("读取偏好失败: " + e.Message); }
        try
        {
            if (File.Exists(settingsPath))
            {
                var saved = System.Text.Json.JsonSerializer.Deserialize<TradeOptions>(File.ReadAllText(settingsPath));
                if (saved == null || saved.OverBudgetValue < 0 || !Enum.IsDefined(typeof(OverBudgetMode), saved.OverBudgetMode))
                    throw new InvalidDataException("报价选项无效，使用默认设置。");
                includeShowcase = saved.IncludeShowcase; includeHiddenStorage = saved.IncludeHiddenStorage;
                includeContainerContents = saved.IncludeContainerContents; allowOverBudget = saved.AllowOverBudget;
                includeMachineContents = saved.IncludeMachineContents; includeMachineProducts = saved.IncludeMachineProducts;
                splitRetailPackages = saved.SplitRetailPackages;
                blockMarketDiscounts = saved.BlockMarketDiscounts; preferMarketMarkups = saved.PreferMarketMarkups;
                blockContraband = saved.BlockContraband; blockStolen = saved.BlockStolen;
                allowContrabandDemandExceptions = saved.AllowContrabandDemandExceptions;
                allowStolenDemandExceptions = saved.AllowStolenDemandExceptions;
                overBudgetPercent = saved.OverBudgetMode == OverBudgetMode.Percent;
                overBudgetInput = saved.OverBudgetValue.ToString(CultureInfo.InvariantCulture);
                lastValidOverBudgetValue = saved.OverBudgetValue;
                panelHotkey = ManagerIntegration.NormalizeHotkey(saved.PanelHotkey, "F8");
                auditHotkey = ManagerIntegration.NormalizeHotkey(saved.AuditHotkey, "F9");
                toolbarX = float.IsFinite(saved.ToolbarX) ? saved.ToolbarX : -1;
                toolbarY = float.IsFinite(saved.ToolbarY) ? saved.ToolbarY : 18;
            }
        }
        catch (Exception e) { LoggerInstance.Warning("读取报价选项失败: " + e.Message); }
        InitializeLocalSettings();
        HarmonyInstance.Patch(AccessTools.Method(typeof(InputActionManager), "Update"),
            prefix: new HarmonyMethod(typeof(BestOfferMod), nameof(AllowGameInput)));
        // EventSystem remains active for the native uGUI panel; its raycast blocker isolates clicks.
        HarmonyInstance.Patch(AccessTools.Method(typeof(WindowsHandler), "Update"),
            prefix: new HarmonyMethod(typeof(BestOfferMod), nameof(AllowGameInput)));
        HarmonyInstance.Patch(AccessTools.Method(typeof(StoreUIManager), "CheckRaycast"),
            prefix: new HarmonyMethod(typeof(BestOfferMod), nameof(AllowGameInput)));
        HarmonyInstance.Patch(AccessTools.Method(typeof(StoreUIManager), "ResolveEscape"),
            prefix: new HarmonyMethod(typeof(BestOfferMod), nameof(ResolveNativeEscape)));
        HarmonyInstance.Patch(AccessTools.Method(typeof(EscapeUIManager), "OpenEscapeMenu"),
            prefix: new HarmonyMethod(typeof(BestOfferMod), nameof(AllowGameInput)));
        LoggerInstance.Msg($"最佳报价 v1.4.3 已载入；{panelHotkey} 打开报价配置，可设置当天行情、货源与禁卖保护。");
    }

    static bool AllowGameInput() => Current?.ShouldBlockNativeInput() != true;

    static bool ResolveNativeEscape(ref bool __result)
    {
        if (Current?.ShouldBlockNativeInput() != true) return true;
        __result = true;
        return false;
    }

    static bool AnyMouseButtonHeld() => Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);

    bool TryOverBudgetNumber(out decimal number) =>
        decimal.TryParse(overBudgetInput, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number) && number >= 0;

    TradeOptions CurrentOptions()
    {
        if (TryOverBudgetNumber(out decimal limit)) lastValidOverBudgetValue = limit;
        else limit = lastValidOverBudgetValue;
        return new TradeOptions
        {
            IncludeShowcase = includeShowcase, IncludeHiddenStorage = includeHiddenStorage,
            IncludeContainerContents = includeContainerContents, AllowOverBudget = allowOverBudget,
            IncludeMachineContents = includeMachineContents, IncludeMachineProducts = includeMachineProducts,
            SplitRetailPackages = splitRetailPackages, BlockMarketDiscounts = blockMarketDiscounts,
            PreferMarketMarkups = preferMarketMarkups,
            BlockContraband = blockContraband, BlockStolen = blockStolen,
            AllowContrabandDemandExceptions = allowContrabandDemandExceptions,
            AllowStolenDemandExceptions = allowStolenDemandExceptions,
            OverBudgetMode = overBudgetPercent ? OverBudgetMode.Percent : OverBudgetMode.Credits,
            OverBudgetValue = limit, PanelHotkey = panelHotkey, AuditHotkey = auditHotkey,
            ToolbarX = toolbarX, ToolbarY = toolbarY
        };
    }

    static long Receipts(OfferPlan plan, TradeSnapshot quote) => Math.Min(plan.TotalPrice, quote.Budget);
    static long Uncollected(OfferPlan plan, TradeSnapshot quote) => Math.Max(0, plan.TotalPrice - Receipts(plan, quote));
    bool IsBestPlan(OfferPlan plan) => snapshot != null && plans.Count > 0 &&
        (!snapshot.Options.PreferMarketMarkups || Math.Min(plan.PreferredPrice, snapshot.Budget) == Math.Min(plans[0].PreferredPrice, snapshot.Budget)) &&
        Receipts(plan, snapshot) == Receipts(plans[0], snapshot) && Uncollected(plan, snapshot) == Uncollected(plans[0], snapshot);

    void OptionsChanged()
    {
        revision++; plans.Clear(); showConfirm = false; itemPage = 0; refreshAt = 0;
        status = snapshot?.CanTrade == true ? "报价选项已更改，请重新计算。" : "设置已保存；买家到来后按规则筛选。";
        SaveSettings();
    }

    bool ShouldBlockNativeInput()
    {
        if (IsOpen || QuickApplying || Time.frameCount <= suppressNativeInputThroughFrame) return true;
        if (suppressMouseUntilReleased)
        {
            // The release that closes the panel also belongs to the panel. Do not
            // forward it to an older native press/drag when gameplay resumes.
            if (!AnyMouseButtonHeld())
            {
                suppressMouseUntilReleased = false;
                suppressNativeInputThroughFrame = Time.frameCount;
            }
            return true;
        }
        if (EmporiumEntry.Instance == null) return false;
        // These checks run in each native Update prefix, so the opening click is
        // intercepted even if Unity invokes the native Update before MelonUpdate.
        // UnityEngine.Input itself remains untouched; F8 and HitButton still work.
        if (ManagerIntegration.Visible) return false;
        if (ManagerIntegration.Down(panelHotkey))
        {
            SuppressTransitionInput();
            return true;
        }
        return false;
    }

    void SuppressTransitionInput()
    {
        suppressNativeInputThroughFrame = Math.Max(suppressNativeInputThroughFrame, Time.frameCount + 1);
        suppressMouseUntilReleased |= AnyMouseButtonHeld();
    }

    public override void OnUpdate()
    {
        try
        {
            RefreshManagerVisibility();
            bool managerVisible = ManagerIntegration.Visible;
            if (managerVisible)
            {
                if (nativeLaunchers != null) nativeLaunchers.SetActive(false);
                if (launcherNotice != null) launcherNotice.gameObject.SetActive(false);
                return;
            }
            if (managerWasVisible) { SuppressTransitionInput(); managerWasVisible = false; return; }
            UpdateNativeUI();
            if (captureHotkey != 0) return;
            // Native InputField handles numeric editing.
            if (ManagerIntegration.Down(panelHotkey)) { Toggle(); }
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) { Close(); }
            else if (QuickApplying && Input.GetKeyDown(KeyCode.Escape))
            { CancelQuick(); }
            if (IsOpen && ManagerIntegration.Down(auditHotkey))
            {
                var audit = adapter.DevelopmentOnlyAuditCapture(5, CurrentOptions());
                File.WriteAllText(Path.Combine(MelonEnvironment.UserDataDirectory, "BestOffer-capture-audit.json"), audit);
                LoggerInstance.Msg("只读报价状态审计已保存。");
            }
            // Validate the live input before accepting a completed asynchronous
            // result, including when it finishes between the periodic refreshes.
            if (IsOpen && (Time.unscaledTime >= refreshAt || pending?.Work.IsCompleted == true))
            {
                refreshAt = Time.unscaledTime + 1;
                var current = adapter.Capture(CurrentOptions());
                if (snapshot != null && GameTradeAdapter.SnapshotSignature(current) != GameTradeAdapter.SnapshotSignature(snapshot))
                {
                    snapshot = current; plans.Clear(); revision++; showConfirm = false;
                    status = allowOverBudget && !TryOverBudgetNumber(out _)
                        ? "请输入有效的非负上限；点击数字框后可用 Ctrl+A 全选替换。"
                        : current.CanTrade ? "顾客、价格、货源或库存已变化，请重新计算。" : current.Message;
                }
            }
            if (pending != null && pending.Work.IsCompleted)
            {
                var work = pending; pending = null;
                if (work.Revision == revision && (IsOpen || work.QuickApply))
                {
                    if (work.Work.IsFaulted)
                    {
                        status = "计算失败：" + work.Work.Exception?.GetBaseException().Message;
                        if (work.QuickApply) ShowQuickNotice(status);
                        return;
                    }
                    // The input snapshot belongs to this request. Never apply a
                    // completed background answer against a newer visitor/stock.
                    var live = adapter.Capture(work.Snapshot.Options);
                    if (GameTradeAdapter.SnapshotSignature(live) != work.Signature)
                    {
                        revision++; plans.Clear(); snapshot = live;
                        status = "顾客、价格或库存已变化，本次未摆货，请重新点击。";
                        if (work.QuickApply) ShowQuickNotice(status);
                        return;
                    }
                    snapshot = work.Snapshot;
                    var result = work.Work.Result;
                    plans = result.Plans.ToList(); selected = 0; planPage = 0;
                    status = plans.Count > 0
                        ? (snapshot!.Options.PreferMarketMarkups ? "加价优先 · 实收 " : "最高实收 ") + $"{Receipts(plans[0], snapshot!)} · 名义报价 {plans[0].TotalPrice} · 不收取 {Uncollected(plans[0], snapshot!)} · {plans.Count} 个方案"
                        : (result.Status == OfferSolveStatus.ResourceLimit ? "搜索未完成：" : "没有可行方案：") + result.Message;
                    LoggerInstance.Msg(status);
                    WriteDiagnostics();
                    if (work.QuickApply)
                    {
                        bool applied = plans.Count > 0 && ApplySelected(false);
                        ShowQuickNotice(status, applied ? "摆货已完成\n等待确认成交" : null);
                    }
                }
            }
            if (blocker != null) blocker.SetActive(IsOpen);
        }
        catch (Exception e)
        {
            bool wasQuick = QuickApplying;
            status = "读取游戏状态失败：" + e.Message; LoggerInstance.Warning(e.ToString());
            Close();
            if (wasQuick) ShowQuickNotice(status);
        }
    }

    void ShowQuickNotice(string message, string? compact = null)
    {
        quickNotice = compact ?? "未完成摆货\nF8 查看原因"; quickNoticeUntil = Time.unscaledTime + 12;
        quickNoticeDetail = message; quickNoticeClientKey = snapshot?.ClientKey ?? "";
        if (compact == null) LoggerInstance.Msg(message);
        SuppressTransitionInput();
    }
    void CancelQuick()
    {
        if (!QuickApplying) return;
        revision++;
        ShowQuickNotice("已取消摆货，物品未移动。", "摆货已取消\n物品未移动");
    }
    void StartQuickApply()
    {
        SuppressTransitionInput();
        if (busy) return;
        Calculate(true);
        if (!busy) ShowQuickNotice(status);
    }

    void Toggle() { if (IsOpen) Close(); else Open(); }
    void Open()
    {
        if (EmporiumEntry.Instance == null) return;
        CancelQuick();
        SuppressTransitionInput();
        snapshot = adapter.Capture(CurrentOptions()); IsOpen = true;
        groupQuantities.Clear(); groupModes.Clear(); expandedGroup = ""; ResetInventoryNavigation(); uiDirty = true;
        revision++; plans.Clear(); showConfirm = false; itemPage = 0; captureHotkey = 0;
        status = Time.unscaledTime < quickNoticeUntil && quickNoticeClientKey == snapshot.ClientKey && !string.IsNullOrEmpty(quickNoticeDetail)
            ? quickNoticeDetail : snapshot.CanTrade ? "选择货源并设置必卖 / 禁卖，再计算最高实收方案。" : snapshot.Message;
        EnsureBlocker(); if (blocker != null) blocker.SetActive(true); refreshAt = Time.unscaledTime + 1;
    }
    void Close()
    {
        CancelQuick();
        if (IsOpen) SuppressTransitionInput();
        IsOpen = false; captureHotkey = 0;
        revision++; showConfirm = false;
        // Unity destroys scene objects before this callback; its overloaded null
        // check also recognizes an invalid native object, unlike ?. on the wrapper.
        if (blocker != null) blocker.SetActive(false);
        SaveRules(); SaveSettings();
    }
    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        try { Close(); }
        finally { snapshot = null; DestroyNativeUI(); }
    }
    public override void OnDeinitializeMelon()
    {
        try { Close(); }
        finally { DestroyNativeUI(); Current = null; }
    }
    void EnsureBlocker() => EnsureNativePanel();
    public override void OnGUI()
    {
        if (captureHotkey != 0 && Event.current != null && Event.current.rawType == EventType.KeyDown)
        { CaptureLocalHotkey(Event.current); Event.current.Use(); uiDirty = true; }
    }
    void SaveRules()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(rulePath)!); File.WriteAllText(rulePath, System.Text.Json.JsonSerializer.Serialize(rules)); }
        catch (Exception e) { LoggerInstance.Warning("保存偏好失败: " + e.Message); }
    }
    void SaveSettings()
    {
        // Preserve the last valid numeric value during an empty edit, while still
        // saving source changes and an explicit switch back to no-overbudget mode.
        if (settingsPath.Length == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(CurrentOptions(),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { LoggerInstance.Warning("保存报价选项失败: " + e.Message); }
    }
    int OwnRule(string key) => ItemRuleGroupLogic.OwnRule(rules, key);
    int Rule(TradeItem i)
    {
        return ItemRuleGroupLogic.EffectiveRule(rules, i.Key, i.DescendantKeys);
    }
    string RuleNote(TradeItem i)
    {
        int contents = 0;
        foreach (var key in i.DescendantKeys) contents |= OwnRule(key);
        if (Rule(i) == 3) return " · 必卖/禁卖冲突";
        return contents == 1 ? " · 内容物必卖" : contents == 2 ? " · 内容物禁卖" : "";
    }
    void ClearCurrentRules()
    {
        if (snapshot == null) return;
        groupQuantities.Clear(); groupModes.Clear();
        foreach (var item in snapshot.Items)
        {
            rules.Remove(item.Key);
            foreach (var key in item.DescendantKeys) rules.Remove(key);
        }
        revision++; plans.Clear(); showConfirm = false; SaveRules();
        status = "当前库存及容器内容物的规则已清除。";
    }
    void SetRule(TradeItem i, int rule)
    {
        if (rule == 0) rules.Remove(i.Key); else rules[i.Key] = rule;
        revision++; plans.Clear(); showConfirm = false;
        status = snapshot?.CanTrade == true ? "筛选条件已更改，请重新计算。" : "规则已保存，买家到来后应用。";
        SaveRules();
    }
    void Calculate(bool quickApply = false)
    {
        if (busy) return;
        if (allowOverBudget && !TryOverBudgetNumber(out _)) { status = "请输入有效的非负超出上限。"; return; }
        snapshot = adapter.Capture(CurrentOptions()); revision++; plans.Clear(); showConfirm = false;
        if (!snapshot.CanTrade) { status = snapshot.Message; return; }
        var includedKeys = snapshot.Items.SelectMany(i => i.DescendantKeys.Append(i.Key)).ToHashSet(StringComparer.Ordinal);
        var requiredOutsideScope = rules.Where(p => p.Value == 1 && !includedKeys.Contains(p.Key))
            .Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        if (requiredOutsideScope.Count > 0 && (!includeShowcase || !includeHiddenStorage || !includeContainerContents ||
            !includeMachineContents || !includeMachineProducts || !splitRetailPackages))
        {
            // Check only when explicitly calculating. Old rules for sold objects
            // or another run are harmless, but an owned required object must not
            // disappear merely because its storage source was unchecked.
            var allSources = CurrentOptions();
            allSources.IncludeShowcase = true; allSources.IncludeHiddenStorage = true;
            allSources.IncludeContainerContents = allSources.IncludeMachineContents = allSources.IncludeMachineProducts = allSources.SplitRetailPackages = true;
            var fullScope = adapter.Capture(allSources);
            if (!fullScope.CanTrade) { status = "无法确认必卖物品范围：" + fullScope.Message; return; }
            var excludedRequired = fullScope.Items.Where(i => requiredOutsideScope.Contains(i.Key) ||
                i.DescendantKeys.Any(requiredOutsideScope.Contains)).ToList();
            if (excludedRequired.Count > 0)
            {
                status = "必卖物品在未包含范围：" + string.Join("；", excludedRequired.Take(2).Select(i => i.Name + "（" + i.Source + "）")) +
                    (excludedRequired.Count > 2 ? "等" : "") + "。请勾选相关范围或取消必卖。";
                return;
            }
        }
        var conflicts = snapshot.Items.Where(i => !i.RuleOnly && Rule(i) == 3).ToList();
        if (conflicts.Count > 0)
        {
            status = "必卖/禁卖规则冲突：" + string.Join("；", conflicts.Select(i => i.Name)) + "。请调整容器或内容物规则，或点击清除规则。";
            return;
        }
        var rejected = snapshot.Items.Where(i => !i.RuleOnly && Rule(i) == 1 && !i.Eligible).ToList();
        if (rejected.Count > 0) { status = "必卖条件无法满足：" + string.Join("；", rejected.Select(i => i.Name + "（" + i.Reason + "）")); return; }
        var candidates = snapshot.Items.Where(i => i.Eligible).OrderBy(i => i.Key, StringComparer.Ordinal).Select(i => new Candidate
        { Key = i.Key, Group = i.Group, Price = i.Price, Required = Rule(i) == 1, Forbidden = Rule(i) == 2,
            Preferred = i.MarketMarkedUp }).ToArray();
        long budget = snapshot.SelectionBudget, receiptCap = snapshot.Budget;
        bool variety = snapshot.RequireVariety, prefer = snapshot.Options.PreferMarketMarkups; int max = snapshot.MaxItems;
        status = quickApply ? "正在计算并摆货……按 Esc 可取消。" : "正在精确计算……";
        pending = new PendingCalculation(
            Task.Run(() => OfferSolver.Solve(candidates, budget, variety, max, quickApply ? 1 : 12, receiptCap: receiptCap,
                prioritizePreferred: prefer)),
            revision, quickApply, snapshot, GameTradeAdapter.SnapshotSignature(snapshot));
    }
    void WriteDiagnostics()
    {
        try
        {
            File.WriteAllText(Path.Combine(MelonEnvironment.UserDataDirectory, "BestOffer-last-quote.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                snapshot!.ClientKey, snapshot.ClientName, snapshot.Budget, snapshot.SelectionBudget, snapshot.Options, snapshot.RequireVariety, snapshot.MaxItems,
                snapshot.MarketDay, snapshot.MarketSummary, snapshot.MarketSignature,
                items = snapshot.Items.Select(i => new { i.Key, i.Group, i.Name, i.Price, i.Source, i.Eligible, i.Reason,
                    i.ParentKey, i.RuleOnly, i.IsRetailPackage, i.UnitCount, i.ContainedUnits, i.MarketDiscounted, i.MarketMarkedUp,
                    i.DescendantKeys, rule = Rule(i), ownRule = OwnRule(i.Key) }),
                plans = plans.Select(p => new { p.TotalPrice, receipts = Receipts(p, snapshot), uncollected = Uncollected(p, snapshot), keys = p.Items.Select(i => i.Key) })
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { LoggerInstance.Warning(e.Message); }
    }

    bool ApplySelected(bool sell)
    {
        if (snapshot == null || selected >= plans.Count) return false;
        try
        {
            var keys = plans[selected].Items.Select(i => i.Key).ToArray();
            var result = sell ? adapter.Sell(snapshot, keys) : adapter.Apply(snapshot, keys);
            status = result.Message;
            LoggerInstance.Msg(status); plans.Clear(); revision++; showConfirm = false; snapshot = adapter.Capture(CurrentOptions());
            return result.Success;
        }
        catch (Exception e) { status = "应用失败：" + e.Message; LoggerInstance.Warning(e.ToString()); return false; }
    }
}
