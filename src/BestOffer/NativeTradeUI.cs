using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Il2Cpp;
using DeepSpaceModSupport;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UIEventTrigger = UnityEngine.EventSystems.EventTrigger;
using UnityEngine.UI;

namespace DeepSpaceBestOffer;

public sealed partial class BestOfferMod
{
    // All controls use the game's uGUI event system and its Chinese TMP font.
    // The original trade screen owns the launchers, so they follow its visibility.
    GameObject? nativeLaunchers, nativePage;
    GameObject? hoveredSettingTooltip, hoveredSettingTarget;
    float settingTooltipShowAt;
    TMP_FontAsset? nativeFont;
    Sprite? nativeButtonSprite, nativeTabletSprite;
    TextMeshProUGUI? nativeStatus, launcherNotice;
    Button? quickButton;
    string uiSignature = "", expandedGroup = "";
    string inventoryParentKey = "";
    sealed record InventoryNavigation(string ParentKey, string Group, int ListPage, int DetailPage);
    readonly Stack<InventoryNavigation> inventoryNavigation = new();
    bool uiDirty = true;
    int uiTab, groupDetailPage;
    int nativeUiRebuildHeldThroughFrame = -1;
    readonly Dictionary<string, int> groupQuantities = new(StringComparer.Ordinal);
    readonly Dictionary<string, int> groupModes = new(StringComparer.Ordinal);
    static readonly Color Ink = new(.24f, .19f, .19f), Paper = new(.85f, .77f, .53f);
    static readonly Color Rust = new(.58f, .22f, .10f), Muted = new(.47f, .39f, .27f);
    static readonly Color Green = new(.33f, .43f, .23f), Line = new(.70f, .55f, .35f);
    const float NativeWidth = 1120, NativeHeight = 736;
    const float CategoryPageHeight = 688;
    const int GroupRows = 5;
    const float SettingTooltipDelaySeconds = .5f;

    void UpdateNativeUI()
    {
        // A focused InputField commits on pointer-down elsewhere. Keep the
        // pressed page alive through the release frame so EventSystem can still
        // deliver its pointer-up/click to that same button.
        if (Input.GetMouseButton(0)) nativeUiRebuildHeldThroughFrame = Time.frameCount + 1;
        if (Input.GetMouseButtonUp(0)) nativeUiRebuildHeldThroughFrame = Time.frameCount;
        var neg = NegociationUIManager.Instance;
        if (neg == null || EmporiumEntry.Instance == null) { ResetSettingTooltip(); return; }
        if (nativeFont == null) nativeFont = FontLoader.Instance?.simplifiedChineseFont ?? neg.acceptOfferButtonButtonTMP?.font ?? neg.titleTMP?.font;
        if (nativeButtonSprite == null) nativeButtonSprite = neg.acceptOfferButtonButton?.GetComponent<Image>()?.sprite;
        if (nativeLaunchers == null && neg.uiObject != null && nativeFont != null)
        {
            nativeTabletSprite = neg.uiObject.GetComponent<Image>()?.sprite;
            nativeLaunchers = RectObject("BestOffer.TradeActions", neg.uiObject.transform, 0, 0, 278, 30);
            var rt = nativeLaunchers.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, 0);
            rt.pivot = new Vector2(.5f, 0); rt.anchoredPosition = new Vector2(0, 10);
            // A small action strip in the tablet's lower bezel keeps native trade
            // information and the original accept buttons unobstructed.
            MakeIconButton(nativeLaunchers.transform, 0, 0, 136, 30, "报价配置", () => { uiTab = 0; Open(); }, Rust, true);
            quickButton = MakeIconButton(nativeLaunchers.transform, 142, 0, 136, 30, "一键摆货", StartQuickApply, Green, false);
            // The header remains free even when all three native trade actions
            // (single, multiple, wholesale) are visible along the bottom.
            launcherNotice = MakeText(neg.uiObject.transform, 34, 5, 300, 40, "", 12, Ink);
            LoggerInstance.Msg("交易 iPad 已接入最佳报价按钮。");
            ExportNativeLayout(neg);
        }
        if (nativeLaunchers != null)
        {
            nativeLaunchers.SetActive(!IsOpen && !ManagerIntegration.Visible);
            if (launcherNotice != null) launcherNotice.gameObject.SetActive(!IsOpen && !ManagerIntegration.Visible);
            if (quickButton != null) quickButton.interactable = !busy && CanQuickApplyFromUI();
            if (launcherNotice != null) launcherNotice.text = QuickApplying ? "正在计算……\nEsc 取消" :
                Time.unscaledTime < quickNoticeUntil ? quickNotice : $"{panelHotkey}\n打开报价配置";
        }
        if (!IsOpen) { ResetSettingTooltip(); if (blocker != null) blocker.SetActive(false); return; }
        EnsureNativePanel();
        string signature = System.Text.Json.JsonSerializer.Serialize(new { revision, status, busy, selected, filter,
            itemPage, planPage, detailPage, showConfirm, uiTab, expandedGroup, inventoryParentKey, groupDetailPage, captureHotkey,
            selectedCategoryId, categoryPickerOpen,
            planCount = plans.Count, items = snapshot?.Items.Count });
        if ((uiDirty || signature != uiSignature) && Time.frameCount > nativeUiRebuildHeldThroughFrame)
        { uiDirty = false; uiSignature = signature; RebuildNativePage(); }
        UpdateSettingTooltip();
    }

    void EnsureNativePanel()
    {
        if (blocker != null) return;
        var neg = NegociationUIManager.Instance;
        nativeFont ??= FontLoader.Instance?.simplifiedChineseFont ?? neg?.acceptOfferButtonButtonTMP?.font ?? neg?.titleTMP?.font;
        if (nativeFont == null) throw new InvalidOperationException("交易终端字体尚未载入，请稍后打开。");
        blocker = new GameObject("BestOffer.TradeTerminal");
        var canvas = blocker.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
        var scaler = blocker.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1200, 800); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        blocker.AddComponent<GraphicRaycaster>();
        var shade = RectObject("Shade", blocker.transform, 0, 0, 0, 0);
        var sr = shade.GetComponent<RectTransform>(); sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one;
        sr.offsetMin = Vector2.zero; sr.offsetMax = Vector2.zero;
        shade.AddComponent<Image>().color = new Color(.09f, .06f, .08f, .64f);
        uiDirty = true;
    }

    void DestroyNativeUI()
    {
        ResetSettingTooltip();
        if (blocker != null) UnityEngine.Object.Destroy(blocker);
        if (nativeLaunchers != null) UnityEngine.Object.Destroy(nativeLaunchers);
        if (launcherNotice != null) UnityEngine.Object.Destroy(launcherNotice.gameObject);
        launcherNotice = nativeStatus = null; quickButton = null;
        blocker = nativeLaunchers = nativePage = null; nativeFont = null; nativeTabletSprite = nativeButtonSprite = null;
        uiSignature = ""; uiDirty = true;
        categoryPickerOpen = false; categoryIconSprites.Clear(); categoryIconAtlases.Clear();
        foreach (var sprite in fittedCategorySprites) if (sprite != null) UnityEngine.Object.Destroy(sprite);
        fittedCategorySprites.Clear();
        ResetInventoryNavigation();
    }

    void RebuildNativePage()
    {
        if (blocker == null) return;
        ResetSettingTooltip();
        if (nativePage != null) { nativePage.SetActive(false); UnityEngine.Object.Destroy(nativePage); }
        float pageHeight = uiTab == 1 ? CategoryPageHeight : NativeHeight;
        nativePage = RectObject("Tablet", blocker.transform, 0, 0, NativeWidth, pageHeight);
        // Configure the whole page before Selectable.OnEnable runs. Enabling a
        // completed page applies its final disabled tints immediately instead
        // of fading newly created controls from their default enabled state.
        nativePage.SetActive(false);
        var rt = nativePage.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
        rt.pivot = new Vector2(.5f, .5f); rt.anchoredPosition = Vector2.zero;
        var shell = nativePage.AddComponent<Image>(); shell.color = new Color(.39f, .33f, .31f);
        // Use the real tablet frame when available. The fallback preserves its palette.
        if (nativeTabletSprite != null) { shell.sprite = nativeTabletSprite; shell.type = Image.Type.Sliced; shell.color = Color.white; }
        var screen = MakeBox(nativePage.transform, 24, 34, 1072, pageHeight - 60, Line);
        MakeBox(screen.transform, 3, 3, 1066, pageHeight - 66, Paper);
        var root = nativePage.transform;
        MakeText(root, 48, 50, 670, 34, "最佳报价", 27, Rust);
        MakeText(root, 49, 87, 770, 24, snapshot == null || !snapshot.CanTrade ?
            snapshot == null || string.IsNullOrEmpty(snapshot.ClientKey) ? "等待买家  ·  可先设置库存规则" :
            $"{snapshot.ClientName}  /  当前不可交易  ·  可先设置库存规则" :
            $"{snapshot.ClientName}  /  剩余预算 {snapshot.Budget:N0} 信用点" + (snapshot.RequireVariety ? "  /  每种限一件" : ""), 14, Muted);
        MakeButton(root, 1016, 50, 56, 30, "×", Close, Rust);
        string[] tabs = { "库存规则", "分类规则", "报价方案", "交易设置" };
        for (int n = 0; n < tabs.Length; n++)
        {
            int tab = n;
            MakeButton(root, 48 + n * 172, 124, 160, 35, tabs[n], () => { uiTab = tab; uiDirty = true; }, uiTab == n ? Rust : Muted);
        }
        MakeText(root, 792, 130, 280, 24, "商店终端  /  自动选货", 13, Muted, TextAlignmentOptions.Right);
        MakeBox(root, 48, 174, 1024, 1, Line);
        if (uiTab == 0) BuildInventoryPage(root);
        else if (uiTab == 1) BuildCategoryRulesPage(root);
        else if (uiTab == 2) BuildPlansPage(root);
        else BuildSettingsPage(root);
        var footerLine = MakeBox(root, 48, pageHeight - 97, 1024, 1, Line);
        nativeStatus = MakeText(root, 48, pageHeight - 86, 806, 46, status, 13, Ink);
        var footerHelp = MakeText(root, 874, pageHeight - 73, 198, 22, "Esc 返回交易  ·  1.5.0", 12, Muted, TextAlignmentOptions.Right);
        bool showFooter = uiTab != 1 || !categoryPickerOpen;
        footerLine.SetActive(showFooter); nativeStatus.gameObject.SetActive(showFooter); footerHelp.gameObject.SetActive(showFooter);
        if (uiTab == 1 && categoryPickerOpen) BuildCategoryPicker(root);
        nativePage.SetActive(true);
        blocker.SetActive(IsOpen);
    }

    void BuildInventoryPage(Transform root)
    {
        if (snapshot == null) return;
        var package = inventoryParentKey.Length == 0 ? null : snapshot.Items.FirstOrDefault(i => i.Key == inventoryParentKey);
        if (inventoryParentKey.Length > 0 && package == null) { ResetInventoryNavigation(); uiDirty = true; return; }
        if (expandedGroup.Length > 0) { BuildGroupDetail(root); return; }
        if (package == null)
        {
            MakeText(root, 48, 188, 790, 26, "勾选「全部」可持续必卖／禁卖同名商品（含新增）；取消后可编辑数量。", 14, Ink);
            string[] filters = { "全部", "可卖", "必卖", "禁卖" };
            for (int f = 0; f < filters.Length; f++)
            { int v = f; MakeButton(root, 48 + f * 85, 225, 77, 28, filters[f], () => { filter = v; itemPage = 0; uiDirty = true; }, filter == f ? Rust : Muted); }
            MakeButton(root, 406, 225, 124, 28, "清除手动规则", ClearCurrentRules, Muted);
            MakeButton(root, 838, 218, 234, 37, busy ? "正在计算……" : "计算最佳报价 →", () => { uiTab = 2; Calculate(); }, Green, !busy && snapshot.CanTrade);
        }
        else
        {
            MakeButton(root, 48, 188, 140, 30, "← 包装单件", ReturnFromPackage, Muted);
            MakeText(root, 205, 188, 867, 30, $"{package.Name}  /  {RetailQuantityLabel(package)}  /  内容规则", 18, Ink);
            MakeText(root, 48, 226, 1024, 35,
                "禁卖任意内容会保护整件包装；内容必卖在整件模式下要求整盒／整瓶。\n开启拆售后，按包内单片规则选货；同名数量与单片设置共用。", 13, Muted);
        }
        var groups = GroupInventoryRows(snapshot.Items, package == null ? filter : 0, inventoryParentKey);
        int pages = Math.Max(1, (groups.Count + GroupRows - 1) / GroupRows);
        itemPage = Math.Clamp(itemPage, 0, pages - 1);
        for (int row = 0; row < GroupRows && itemPage * GroupRows + row < groups.Count; row++)
        {
            var group = groups[itemPage * GroupRows + row]; float y = 267 + row * 62;
            MakeBox(root, 48, y + 57, 1024, 1, Line);
            int req = GroupOwnRuleCount(group, 1), ban = GroupOwnRuleCount(group, 2);
            MakeText(root, 49, y, 322, 27, $"{group.Name}  ×{group.Count}", 18, group.EligibleCount > 0 ? Ink : Muted);
            string price = group.Items.Any(i => !i.PriceKnown) ? "待报价" :
                group.MinPrice == group.MaxPrice ? $"单件 {group.MinPrice}" : $"报价 {group.MinPrice}–{group.MaxPrice}";
            string quantityNote = group.Items.All(i => i.IsRetailPackage) ? $"  ·  内含 {group.Items.Sum(i => (long)i.ContainedUnits)} 片" :
                package != null && group.Items.Any(i => i.UnitCount > 1) ? $"  ·  共 {group.Items.Sum(i => (long)Math.Max(1, i.UnitCount))} 片" : "";
            MakeText(root, 49, y + 29, 322, 24, $"{price}  ·  必卖 {req} / 禁卖 {ban}" + quantityNote +
                (group.Items.Any(i => i.RiskBlocked) ? $"  ·  自动禁卖 {group.Items.Count(i => i.RiskBlocked)}" :
                group.EligibleCount == 0 && group.Items.All(i => i.PriceKnown) ? "  ·  暂不可卖" : ""), 12, Muted);
            int count = CurrentGroupQuantityFromUI(group);
            bool all = GroupSelectsAll(group);
            MakeButton(root, 388, y + 10, 34, 32, "−", () => ChangeGroupQuantity(group, CurrentGroupQuantityFromUI(group) - 1), Muted, !busy && !all);
            var quantityField = MakeQuantityField(root, 429, y + 10, 105, 32, group, count);
            MakeButton(root, 541, y + 10, 34, 32, "+", () => ChangeGroupQuantity(group, CurrentGroupQuantityFromUI(group) + 1), Muted, !busy && !all);
            var allButton = MakeButton(root, 590, y + 10, 68, 32, all ? "✓ 全部" : "全部", () => ToggleGroupAllFromUI(group), all ? Green : Muted, !busy);
            MakeButton(root, 672, y + 10, 120, 32, req > 0 ? $"必卖 {req} ✓" : "必卖", () =>
            ToggleGroupRuleFromUI(group, 1), req > 0 ? Green : Muted, !busy);
            MakeButton(root, 804, y + 10, 120, 32, ban > 0 ? $"禁卖 {ban} ✓" : "禁卖", () =>
            ToggleGroupRuleFromUI(group, 2), ban > 0 ? Rust : Muted, !busy);
            MakeButton(root, 940, y + 10, 132, 32, "查看单件 →", () => { expandedGroup = group.Name; groupDetailPage = 0; uiDirty = true; }, Muted);
            string allExplanation = all ? "已勾选「全部」，数量随库存变化，不能手动编辑。\n取消勾选即可编辑，数量保留为当时的最大值。" :
                "勾选后，必卖／禁卖会覆盖今后新增的同名商品。\n数量始终显示当前最大值；取消勾选后可编辑。";
            AddSettingTooltip(allButton, root, allExplanation, 388, y + 44, 536, 52);
            if (all) AddSettingTooltip(quantityField.gameObject, root, allExplanation, 388, y + 44, 536, 52);
        }
        if (groups.Count == 0) MakeText(root, 48, 295, 1024, 100, snapshot.CanTrade ? "此分类没有商品。" :
            snapshot.Items.Count > 0 ? "此分类没有商品，可切换「全部」提前设置库存规则。" : snapshot.Message, 18, Muted);
        MakeButton(root, 48, 597, 96, 28, "← 上一页", () => { itemPage--; uiDirty = true; }, Muted, itemPage > 0);
        MakeText(root, 158, 601, 720, 24, $"{itemPage + 1} / {pages} 页  ·  {groups.Count} 种商品 / {groups.Sum(g => g.Count)} 件" +
            (package == null ? "" : "  ·  包内规则独立保存"), 13, Muted);
        MakeButton(root, 976, 597, 96, 28, "下一页 →", () => { itemPage++; uiDirty = true; }, Muted, itemPage < pages - 1);
    }

    void MakeCategoryDayField(Transform parent, float x, float y, float w, float h, TradeCategory category, CategorySaleRule rule, TextMeshProUGUI expiry)
    {
        var go = MakeBox(parent, x, y, w, h, rule.Limited ? new Color(.93f, .85f, .65f) : new Color(.70f, .69f, .65f));
        var label = RectObject("Days", go.transform, 5, 2, w - 10, h - 4).AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); label.fontSize = 16; label.color = Ink;
        label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
        var input = go.AddComponent<InputField>(); input.textComponent = label; input.targetGraphic = go.GetComponent<Image>();
        input.contentType = InputField.ContentType.IntegerNumber; input.characterLimit = 3;
        string shown = CategoryRuleLogic.PeriodDaysForEditing(rule, CategoryClock().Day).ToString(CultureInfo.InvariantCulture);
        input.text = shown; input.interactable = !busy && rule.Limited;
        var colors = input.colors; colors.disabledColor = Color.white; input.colors = colors;
        input.onValueChanged.AddListener((UnityAction<string>)(value =>
        {
            // Update the preview in place so typing keeps its focus and caret.
            int day = CategoryClock().Day;
            expiry.text = int.TryParse(value, out int days) && days is >= 1 and <= CategoryRuleLogic.MaxDurationDays
                ? CategoryRuleLogic.ExpiryDescription(checked(day + days), day)
                : $"请输入 1–{CategoryRuleLogic.MaxDurationDays} 天";
        }));
        input.onEndEdit.AddListener((UnityAction<string>)(value =>
        {
            if (!rule.Limited || value == shown) return;
            if (int.TryParse(value, out int days) && days is >= 1 and <= CategoryRuleLogic.MaxDurationDays) SetCategoryPeriod(category, true, days);
            else { status = $"有效期需为 1–{CategoryRuleLogic.MaxDurationDays} 天，已保留原设置。"; uiDirty = true; }
        }));
    }

    int CurrentGroupQuantityFromUI(InventoryRuleGroup group)
    {
        int req = GroupOwnRuleCount(group, 1), ban = GroupOwnRuleCount(group, 2);
        return ItemRuleGroupLogic.SelectedQuantity(GroupSelectsAll(group),
            groupQuantities.TryGetValue(GroupStateKey(group), out var q) ? q : null, group.Count, req, ban);
    }

    int CurrentGroupModeFromUI(InventoryRuleGroup group)
    {
        string key = GroupStateKey(group);
        if (allGroupRules.TryGetValue(key, out int all) && all is 1 or 2) return all;
        if (groupModes.TryGetValue(key, out int mode) && mode is 1 or 2 && GroupOwnRuleCount(group, mode) > 0) return mode;
        int req = GroupOwnRuleCount(group, 1), ban = GroupOwnRuleCount(group, 2);
        return ban > 0 && req == 0 ? 2 : req > 0 && ban == 0 ? 1 : 0;
    }

    void ToggleGroupRuleFromUI(InventoryRuleGroup group, int rule)
    {
        if (GroupSelectsAll(group))
        {
            string key = GroupStateKey(group);
            int next = allGroupRules[key] == rule ? 0 : rule;
            ItemRuleGroupLogic.SetAllRule(rules, allGroupRules, key, CurrentRuleMembers, next);
            if (next == 0) groupModes.Remove(key); else groupModes[key] = next;
            status = next == 0 ? $"「{group.Name}」已取消{(rule == 1 ? "必卖" : "禁卖")}，仍勾选全部。" :
                $"「{group.Name}」全部{(next == 1 ? "必卖" : "禁卖")}，今后新增的同名商品也会应用。" + RuleNextStep();
            AllGroupRuleChanged();
            return;
        }
        int quantity = CurrentGroupQuantityFromUI(group);
        int ownCount = GroupOwnRuleCount(group, rule);
        int target = Math.Min(quantity, GroupAvailableRuleCount(group, rule));
        bool clear = ownCount > 0 && ownCount == target;
        SetGroupRule(group, rule, clear ? 0 : quantity);
        if (GroupOwnRuleCount(group, rule) > 0) groupModes[GroupStateKey(group)] = rule;
        else groupModes.Remove(GroupStateKey(group));
        uiDirty = true;
    }

    void ChangeGroupQuantity(InventoryRuleGroup group, int quantity)
    {
        if (GroupSelectsAll(group)) return;
        quantity = Math.Clamp(quantity, 1, group.Count); groupQuantities[GroupStateKey(group)] = quantity;
        int mode = CurrentGroupModeFromUI(group);
        if (mode != 0) { groupModes[GroupStateKey(group)] = mode; SetGroupRule(group, mode, quantity); }
        else groupModes.Remove(GroupStateKey(group));
        uiDirty = true;
    }

    void ResetInventoryNavigation()
    {
        inventoryParentKey = expandedGroup = "";
        inventoryNavigation.Clear(); groupDetailPage = 0;
    }

    void OpenPackageContents(TradeItem package)
    {
        inventoryNavigation.Push(new InventoryNavigation(inventoryParentKey, expandedGroup, itemPage, groupDetailPage));
        inventoryParentKey = package.Key; expandedGroup = ""; itemPage = groupDetailPage = 0; uiDirty = true;
    }

    void ReturnFromPackage()
    {
        if (inventoryNavigation.Count == 0) ResetInventoryNavigation();
        else
        {
            var previous = inventoryNavigation.Pop();
            inventoryParentKey = previous.ParentKey; expandedGroup = previous.Group;
            itemPage = previous.ListPage; groupDetailPage = previous.DetailPage;
        }
        uiDirty = true;
    }

    static string RetailQuantityLabel(TradeItem item) => item.IsRetailPackage ?
        item.ContainedUnits > 0 ? $"内含 {item.ContainedUnits} 片" : "空包装" :
        item.ParentKey.Length > 0 && item.UnitCount > 1 ? $"本件 {item.UnitCount} 片" : "";

    void BuildGroupDetail(Transform root)
    {
        var group = GroupInventoryRows(snapshot!.Items, 0, inventoryParentKey).FirstOrDefault(g => g.Name == expandedGroup);
        if (group == null) { expandedGroup = ""; uiDirty = true; return; }
        MakeButton(root, 48, 188, 140, 30, inventoryParentKey.Length == 0 ? "← 同名商品" : "← 包内同名", () => { expandedGroup = ""; uiDirty = true; }, Muted);
        MakeText(root, 205, 188, 867, 32, $"{group.Name}  /  {group.Count} 件  /  单独设置", 18, Ink);
        int pages = Math.Max(1, (group.Count + 5) / 6); groupDetailPage = Math.Clamp(groupDetailPage, 0, pages - 1);
        for (int n = 0; n < 6 && groupDetailPage * 6 + n < group.Count; n++)
        {
            var item = group.Items[groupDetailPage * 6 + n]; float y = 235 + n * 57;
            string price = item.PriceKnown ? $"{item.Price} 信用点" : "待报价";
            bool hasPackageContents = item.IsRetailPackage && snapshot.Items.Any(i => i.ParentKey == item.Key);
            float textWidth = item.IsRetailPackage ? 567 : 716;
            MakeText(root, 48, y, textWidth, 25, $"第 {groupDetailPage * 6 + n + 1} 件  ·  {price}  ·  {item.Source}", 15, item.Eligible || item.RuleOnly ? Ink : Muted);
            string quantity = RetailQuantityLabel(item);
            MakeText(root, 48, y + 25, textWidth, 25, (quantity.Length == 0 ? "" : quantity + "  ·  ") +
                (item.Eligible ? ItemFeatureSummary(item) : item.RuleOnly ? "仅设置包装内容规则" : item.Reason) + RuleNote(item), 12, Muted);
            if (item.IsRetailPackage) MakeButton(root, 632, y + 5, 144, 32, "内容规则 →", () => OpenPackageContents(item), Muted, !busy && hasPackageContents);
            int r = OwnRule(item.Key);
            MakeButton(root, 792, y + 5, 128, 32, r == 1 ? "必卖 ✓" : "必卖", () => { SetRule(item, r == 1 ? 0 : 1); groupModes.Remove(GroupStateKey(group)); uiDirty = true; }, r == 1 ? Green : Muted, !busy);
            MakeButton(root, 936, y + 5, 136, 32, r == 2 ? "禁卖 ✓" : "禁卖", () => { SetRule(item, r == 2 ? 0 : 2); groupModes.Remove(GroupStateKey(group)); uiDirty = true; }, r == 2 ? Rust : Muted, !busy);
            MakeBox(root, 48, y + 53, 1024, 1, Line);
        }
        MakeButton(root, 48, 597, 96, 28, "← 上一页", () => { groupDetailPage--; uiDirty = true; }, Muted, groupDetailPage > 0);
        MakeText(root, 168, 601, 600, 24, $"{groupDetailPage + 1} / {pages} 页  ·  单件规则与同名数量规则共用", 13, Muted);
        MakeButton(root, 976, 597, 96, 28, "下一页 →", () => { groupDetailPage++; uiDirty = true; }, Muted, groupDetailPage < pages - 1);
    }

    static string ItemFeatureSummary(TradeItem item)
    {
        try
        {
            var features = item.Native.itemFeatures;
            var list = new List<string>();
            for (int n = 0; features != null && n < features.Count; n++)
                if (features[n] != null && features[n].IsExposed() && !features[n].isDisabled)
                { string label = features[n].GetBaseCurrentDisplay(); if (!string.IsNullOrWhiteSpace(label)) list.Add(label); }
            return list.Count > 0 ? string.Join(" · ", list.Distinct().Take(5)) : "同名普通商品，可按数量一起设置";
        }
        catch { return "可按这件物品单独设置"; }
    }

    void BuildPlansPage(Transform root)
    {
        if (snapshot == null) return;
        MakeText(root, 48, 189, 700, 28, "选定方案后摆上交易台，核对报价后成交。", 14, Ink);
        MakeButton(root, 838, 188, 234, 37, busy ? "正在计算……" : "重新计算最佳报价", CalculateFromUI, Green, !busy && snapshot.CanTrade);
        if (plans.Count == 0)
        {
            MakeText(root, 100, 325, 920, 50, busy ? "正在寻找最高实收方案……" : !snapshot.CanTrade ? "等待可交易的买家" : "等待计算报价", 25, Rust, TextAlignmentOptions.Center);
            MakeText(root, 150, 389, 820, 76, !snapshot.CanTrade ?
                "现在可在「库存规则」提前设置必卖和禁卖。\n买家到来后，报价与摆货功能会自动开放。" :
                "先在「库存规则」保留不想出售的商品。\n货源范围和清货上限可在「交易设置」中修改。", 16, Muted, TextAlignmentOptions.Center);
            return;
        }
        int pages = (plans.Count + 3) / 4; planPage = Math.Clamp(planPage, 0, pages - 1);
        for (int n = 0; n < 4 && planPage * 4 + n < plans.Count; n++)
        {
            int index = planPage * 4 + n; var p = plans[index];
            MakeButton(root, 48, 241 + n * 66, 420, 56,
                $"方案 {index + 1}   /   {p.ItemCount} 件" + (IsBestPlan(p) ? "   /   最优" : "") + $"\n实收 {Receipts(p, snapshot)}   ·   少收 {Uncollected(p, snapshot)}",
                () => { selected = index; detailPage = 0; showConfirm = false; uiDirty = true; }, index == selected ? Rust : Muted);
        }
        MakeButton(root, 48, 516, 96, 28, "← 上一组", () => { planPage--; uiDirty = true; }, Muted, planPage > 0);
        MakeText(root, 172, 520, 174, 24, $"{planPage + 1} / {pages} 组", 13, Muted, TextAlignmentOptions.Center);
        MakeButton(root, 372, 516, 96, 28, "下一组 →", () => { planPage++; uiDirty = true; }, Muted, planPage < pages - 1);
        var chosen = plans[selected]; var keys = chosen.Items.Select(i => i.Key).ToHashSet();
        var items = snapshot.Items.Where(i => keys.Contains(i.Key)).GroupBy(i => new { i.Name, i.IsRetailPackage, IsContent = i.ParentKey.Length > 0 })
            .Select(g => $"{g.Key.Name}  ×{g.Count()}" + (g.Key.IsRetailPackage ? $"（内含 {g.Sum(i => (long)i.ContainedUnits)} 片）" :
                g.Key.IsContent ? $"（共 {g.Sum(i => (long)Math.Max(1, i.UnitCount))} 片）" : "") + $"     {g.Sum(i => i.Price)}").ToArray();
        MakeText(root, 504, 241, 568, 32, $"预计实收 {Receipts(chosen, snapshot):N0} 信用点", 23, Green);
        MakeText(root, 504, 278, 568, 26, $"名义报价 {chosen.TotalPrice}  ·  放弃收取 {Uncollected(chosen, snapshot)}", 14, Muted);
        MakeBox(root, 504, 318, 568, 1, Line);
        int detailPages = Math.Max(1, (items.Length + 6) / 7); detailPage = Math.Clamp(detailPage, 0, detailPages - 1);
        for (int n = 0; n < 7 && detailPage * 7 + n < items.Length; n++)
            MakeText(root, 504, 329 + n * 27, 568, 25, items[detailPage * 7 + n], 15, Ink);
        if (detailPages > 1) MakeButton(root, 504, 523, 568, 26, $"明细 {detailPage + 1}/{detailPages}  ·  点击翻页", () => { detailPage = (detailPage + 1) % detailPages; uiDirty = true; }, Muted);
        MakeButton(root, 504, 571, 272, 43, "摆上交易台", () => { if (ApplySelected(false)) { ShowQuickNotice(status, "摆货已完成\n等待确认成交"); Close(); } }, Green, !busy);
        MakeButton(root, 792, 571, 280, 43, showConfirm ? "确认卖出此方案" : "按此方案卖出", ConfirmNativeSell, showConfirm ? Rust : Muted, !busy);
    }

    void CalculateFromUI() { uiTab = 2; Calculate(); uiDirty = true; }
    void ConfirmNativeSell()
    {
        if (snapshot == null || plans.Count == 0) return;
        if (showConfirm) { ApplySelected(true); uiDirty = true; return; }
        var p = plans[selected]; showConfirm = true;
        status = $"再次点击确认：出售 {p.ItemCount} 件，实收 {Receipts(p, snapshot)} 信用点" +
            (Uncollected(p, snapshot) > 0 ? $"，将放弃收取 {Uncollected(p, snapshot)} 信用点。" : "。");
        uiDirty = true;
    }

    void BuildSettingsPage(Transform root)
    {
        const float left = 48, right = 584, column = 488, cell = 235;
        MakeText(root, left, 188, 1024, 28, "设置自动保存。库存与交易台始终参与，其他货源按需加入。", 14, Muted);
        MakeText(root, left, 227, column, 30, "报价货源", 21, Rust);
        MakeText(root, right, 227, column, 30, "当天行情", 21, Rust);
        MakeBox(root, 560, 227, 1, 210, Line).GetComponent<Image>().raycastTarget = false;
        var showcase = MakeButton(root, left, 263, cell, 38, (includeShowcase ? "✓ " : "") + "展示柜", () => { includeShowcase = !includeShowcase; OptionsChanged(); }, includeShowcase ? Green : Muted, !busy);
        var hiddenStorage = MakeButton(root, left + 253, 263, cell, 38, (includeHiddenStorage ? "✓ " : "") + "隐藏仓库", () => { includeHiddenStorage = !includeHiddenStorage; OptionsChanged(); }, includeHiddenStorage ? Green : Muted, !busy);
        var containers = MakeButton(root, left, 307, cell, 38, (includeContainerContents ? "✓ " : "") + "储藏箱内容", () => { includeContainerContents = !includeContainerContents; OptionsChanged(); }, includeContainerContents ? Green : Muted, !busy);
        var machine = MakeButton(root, left + 253, 307, cell, 38, (includeMachineContents ? "✓ " : "") + "机器", () => { includeMachineContents = !includeMachineContents; OptionsChanged(); }, includeMachineContents ? Green : Muted, !busy);
        var products = MakeButton(root, left, 351, cell, 38, (includeMachineProducts ? "✓ " : "") + "产物", () => { includeMachineProducts = !includeMachineProducts; OptionsChanged(); }, includeMachineProducts ? Green : Muted, !busy);
        var packages = MakeButton(root, left + 253, 351, cell, 38, (splitRetailPackages ? "✓ " : "") + "允许拆售包装耗材", () => { splitRetailPackages = !splitRetailPackages; OptionsChanged(); }, splitRetailPackages ? Green : Muted, !busy);
        MakeText(root, left, 399, column, 44,
            (includeContainerContents ? "储藏箱内容单独报价，保留展开的箱体。" : "储藏箱整体报价；内容禁卖会保护整箱。") + "\n" +
            (splitRetailPackages ? "包装耗材按单片选货，可在包装下设置规则。" : "包装耗材默认整盒／整瓶出售。"), 13, Ink);
        MakeButton(root, right, 263, column, 38, (blockMarketDiscounts ? "✓ " : "") + "不出售打折商品", () => { blockMarketDiscounts = !blockMarketDiscounts; OptionsChanged(); }, blockMarketDiscounts ? Rust : Muted, !busy);
        MakeButton(root, right, 307, column, 38, (preferMarketMarkups ? "✓ " : "") + "优先出售加价商品", () => { preferMarketMarkups = !preferMarketMarkups; OptionsChanged(); }, preferMarketMarkups ? Green : Muted, !busy);
        MakeText(root, right, 354, column, 22, snapshot?.MarketDay > 0 ? $"第 {snapshot.MarketDay} 天  ·  日历事件" : "日历事件", 13, Rust);
        MakeText(root, right, 379, column, 38, string.IsNullOrEmpty(snapshot?.MarketSummary) ? "当天没有影响商品价格的事件。" : snapshot!.MarketSummary, 13, Ink);
        MakeText(root, right, 421, column, 20, preferMarketMarkups ? "加价商品先选，剩余预算继续选择其他商品。" : "开启优先后，加价商品先选，其他商品补足预算。", 12, Muted);
        MakeBox(root, left, 454, 1024, 1, Line);
        MakeText(root, left, 471, 168, 30, "超预算清货", 21, Rust);
        MakeButton(root, 234, 466, 198, 38, allowOverBudget ? "✓ 允许超预算" : "允许超预算", () => { allowOverBudget = !allowOverBudget; OptionsChanged(); }, allowOverBudget ? Rust : Muted, !busy);
        MakeButton(root, 449, 466, 162, 38, overBudgetPercent ? "✓ 百分比 %" : "百分比 %", () => { overBudgetPercent = true; OptionsChanged(); }, overBudgetPercent ? Green : Muted, !busy && allowOverBudget);
        MakeButton(root, 625, 466, 192, 38, !overBudgetPercent ? "✓ 固定信用点" : "固定信用点", () => { overBudgetPercent = false; OptionsChanged(); }, !overBudgetPercent ? Green : Muted, !busy && allowOverBudget);
        MakeText(root, 835, 473, 58, 28, "上限", 14, Ink);
        MakeNumberField(root, 902, 466, 170, 38);
        MakeText(root, left, 512, 733, 28, "超出的金额不收取，实际卖出前会再次确认。", 13, Ink);
        MakeText(root, 798, 512, 274, 28, overBudgetPercent ? "%  /  以剩余预算计算" : "信用点", 13, Muted, TextAlignmentOptions.Right);
        MakeBox(root, left, 551, 1024, 1, Line);
        MakeText(root, left, 574, 168, 30, "快捷键", 21, Rust);
        MakeButton(root, 234, 568, 396, 38, captureHotkey == 1 ? "按功能键，Esc 取消" : "打开面板  /  " + panelHotkey, () => { captureHotkey = 1; uiDirty = true; }, Muted, !busy);
        MakeButton(root, 648, 568, 424, 38, captureHotkey == 2 ? "按功能键，Esc 取消" : "只读诊断  /  " + auditHotkey, () => { captureHotkey = 2; uiDirty = true; }, Muted, !busy);
        MakeText(root, 234, 608, 838, 20, "支持 Ctrl / Alt / Shift + 功能键；请避免与其他模组重复。", 12, Muted);
        AddSettingTooltip(showcase, root, "店铺展示柜里摆放的商品。", left, 394, column, 54);
        AddSettingTooltip(hiddenStorage, root, "店铺隐藏仓库中存放的商品。", left, 394, column, 54);
        AddSettingTooltip(containers, root, "背包、储藏箱等普通容器里的物品。\n工具箱和机器需单独勾选。", left, 394, column, 54);
        AddSettingTooltip(machine, root, "机器内的电池、模块、原材料，\n以及工具箱内的工具。", left, 394, column, 54);
        AddSettingTooltip(products, root, "机器输出位置的成品。\n不含电池、模块和原材料。", left, 394, column, 54);
        AddSettingTooltip(packages, root, "将药片瓶、试纸盒等包装内的耗材\n拆成单片选货。", left, 394, column, 54);
    }

    void AddSettingTooltip(Button button, Transform root, string explanation, float x, float y, float width, float height)
        => AddSettingTooltip(button.gameObject, root, explanation, x, y, width, height);

    void AddSettingTooltip(GameObject target, Transform root, string explanation, float x, float y, float width, float height)
    {
        // Keep the hover text in the description area beneath the source grid.
        // Its entire graphic tree ignores raycasts, so controls stay reachable.
        var tooltip = MakeBox(root, x, y, width, height, Line);
        tooltip.GetComponent<Image>().raycastTarget = false;
        var fill = MakeBox(tooltip.transform, 1, 1, width - 2, height - 2, new Color(.96f, .89f, .70f));
        fill.GetComponent<Image>().raycastTarget = false;
        MakeText(tooltip.transform, 10, 6, width - 20, height - 12, explanation, 14, Ink);
        tooltip.SetActive(false);
        var trigger = target.AddComponent<UIEventTrigger>();
        void Register(EventTriggerType eventType, bool visible)
        {
            var entry = new UIEventTrigger.Entry { eventID = eventType };
            entry.callback.AddListener((UnityAction<BaseEventData>)(_ =>
            {
                if (visible)
                {
                    ResetSettingTooltip();
                    if (IsOpen && tooltip != null && target != null && target.activeInHierarchy)
                    {
                        hoveredSettingTooltip = tooltip; hoveredSettingTarget = target;
                        settingTooltipShowAt = Time.unscaledTime + SettingTooltipDelaySeconds;
                    }
                }
                else if (hoveredSettingTooltip == tooltip) ResetSettingTooltip();
            }));
            trigger.triggers.Add(entry);
        }
        Register(EventTriggerType.PointerEnter, true);
        Register(EventTriggerType.PointerExit, false);
    }

    void UpdateSettingTooltip()
    {
        if (hoveredSettingTooltip == null) return;
        if (!IsOpen || hoveredSettingTarget == null || !hoveredSettingTarget.activeInHierarchy)
        { ResetSettingTooltip(); return; }
        if (!hoveredSettingTooltip.activeSelf && Time.unscaledTime >= settingTooltipShowAt)
        {
            hoveredSettingTooltip.transform.SetAsLastSibling();
            hoveredSettingTooltip.SetActive(true);
        }
    }

    void ResetSettingTooltip()
    {
        if (hoveredSettingTooltip != null) hoveredSettingTooltip.SetActive(false);
        hoveredSettingTooltip = hoveredSettingTarget = null; settingTooltipShowAt = 0;
    }

    void MakeNumberField(Transform parent, float x, float y, float w, float h)
    {
        var go = MakeBox(parent, x, y, w, h, new Color(.93f, .85f, .65f));
        var textObject = RectObject("Value", go.transform, 8, 2, w - 16, h - 4);
        var text = textObject.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("Arial.ttf"); text.fontSize = 18; text.color = Ink; text.alignment = TextAnchor.MiddleCenter;
        text.supportRichText = false; text.raycastTarget = false;
        var input = go.AddComponent<InputField>(); input.textComponent = text; input.targetGraphic = go.GetComponent<Image>();
        input.contentType = InputField.ContentType.DecimalNumber; input.characterLimit = 15; input.text = overBudgetInput; input.interactable = !busy && allowOverBudget;
        input.onEndEdit.AddListener((UnityAction<string>)(value =>
        {
            if (value == overBudgetInput) return;
            overBudgetInput = value;
            if (!TryOverBudgetNumber(out _)) { overBudgetInput = lastValidOverBudgetValue.ToString(CultureInfo.InvariantCulture); status = "请输入非负数字，上限已恢复为上次有效值。"; uiDirty = true; }
            else OptionsChanged();
        }));
    }

    InputField MakeQuantityField(Transform parent, float x, float y, float w, float h, InventoryRuleGroup group, int quantity)
    {
        bool all = GroupSelectsAll(group);
        var go = MakeBox(parent, x, y, w, h, all ? new Color(.70f, .69f, .65f) : new Color(.93f, .85f, .65f));
        var textObject = RectObject("Quantity", go.transform, 6, 2, w - 12, h - 4);
        var text = textObject.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 16; text.color = all ? new Color(.32f, .32f, .30f) : Ink; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; text.supportRichText = false;
        var input = go.AddComponent<InputField>(); input.textComponent = text; input.targetGraphic = go.GetComponent<Image>();
        input.contentType = InputField.ContentType.IntegerNumber; input.characterLimit = 6; input.text = quantity.ToString(CultureInfo.InvariantCulture); input.interactable = !busy && !all;
        if (all)
        {
            // Unity's default disabled tint is translucent and mixes the gray
            // with the yellow panel. Keep the disabled field visibly gray.
            var colors = input.colors; colors.disabledColor = Color.white; input.colors = colors;
        }
        input.onEndEdit.AddListener((UnityAction<string>)(value =>
        {
            if (GroupSelectsAll(group) || value == quantity.ToString(CultureInfo.InvariantCulture)) return;
            if (int.TryParse(value, out int count) && count > 0) ChangeGroupQuantity(group, count);
            else { status = "数量需为正整数，已保留原数量。"; uiDirty = true; }
        }));
        return input;
    }

    static GameObject RectObject(string name, Transform parent, float x, float y, float w, float h)
    {
        var go = new GameObject(name); var rt = go.AddComponent<RectTransform>(); rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); return go;
    }
    static GameObject MakeBox(Transform parent, float x, float y, float w, float h, Color color)
    { var go = RectObject("Panel", parent, x, y, w, h); go.AddComponent<Image>().color = color; return go; }
    TextMeshProUGUI MakeText(Transform parent, float x, float y, float w, float h, string content, float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        var go = RectObject("Label", parent, x, y, w, h); var text = go.AddComponent<TextMeshProUGUI>();
        text.font = nativeFont; text.fontSize = size; text.color = color; text.text = content; text.alignment = alignment;
        text.enableAutoSizing = true; text.fontSizeMin = size * .72f; text.fontSizeMax = size;
        text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false; text.richText = false; return text;
    }

    static bool CanQuickApplyFromUI()
    {
        try
        {
            var store = PlayerStore.Instance;
            var entry = EmporiumEntry.Instance;
            var client = store?.currentClientInstance?.GetClientBlueprint();
            return store != null && entry != null && entry.backInvinvElement != null && client != null &&
                client.IsClientBuying() && !client.isBusinessComplete && !client.isRegularSellDisable &&
                store.lockAllFlag <= 0 && store.lockAllInventoryFlag <= 0 &&
                !store.isClientBeingArrested && !store.isHandlingShootout;
        }
        catch { return false; }
    }

    Button MakeIconButton(Transform parent, float x, float y, float w, float h, string title, Action action, Color color, bool configuration)
    {
        var button = MakeButton(parent, x, y, w, h, "", action, color);
        var icon = RectObject(configuration ? "ConfigurationIcon" : "CargoIcon", button.transform, 10, (h - 18) / 2, 18, 18);
        // Small pixel shapes match the tablet's artwork and never depend on
        // optional font glyphs. Their transparent centres retain hover colours.
        void Piece(float px, float py, float pw, float ph)
        { MakeBox(icon.transform, px, py, pw, ph, Paper).GetComponent<Image>().raycastTarget = false; }
        if (configuration)
        {
            Piece(4, 4, 10, 2); Piece(4, 12, 10, 2); Piece(4, 6, 2, 6); Piece(12, 6, 2, 6);
            Piece(7, 0, 4, 4); Piece(7, 14, 4, 4); Piece(0, 7, 4, 4); Piece(14, 7, 4, 4);
            Piece(2, 2, 4, 4); Piece(12, 2, 4, 4); Piece(2, 12, 4, 4); Piece(12, 12, 4, 4);
        }
        else
        {
            Piece(3, 0, 12, 2); Piece(0, 2, 18, 2); Piece(0, 4, 2, 12);
            Piece(16, 4, 2, 12); Piece(0, 16, 18, 2); Piece(8, 4, 2, 12);
        }
        MakeText(button.transform, 32, 2, w - 38, h - 4, title, 14, Paper, TextAlignmentOptions.Center);
        return button;
    }

    Button MakeButton(Transform parent, float x, float y, float w, float h, string title, Action action, Color color, bool enabled = true)
    {
        var go = MakeBox(parent, x, y, w, h, color); var image = go.GetComponent<Image>();
        if (nativeButtonSprite != null) { image.sprite = nativeButtonSprite; image.type = Image.Type.Sliced; }
        var button = go.AddComponent<Button>(); button.targetGraphic = image;
        button.interactable = enabled; MakeText(go.transform, 7, 2, w - 14, h - 4, title, h > 45 ? 15 : 14, Paper, TextAlignmentOptions.Center);
        button.onClick.AddListener((UnityAction)(() =>
        {
            SuppressTransitionInput();
            try { action(); } catch (Exception e) { status = "操作失败：" + e.Message; LoggerInstance.Warning(e.ToString()); }
            uiDirty = true;
        })); return button;
    }

    void ExportNativeLayout(NegociationUIManager neg)
    {
        try
        {
            var entries = new List<object>();
            void Walk(Transform t, string path)
            {
                var rt = t.TryCast<RectTransform>(); var image = t.GetComponent<Image>();
                entries.Add(new { path, active = t.gameObject.activeSelf, size = rt?.sizeDelta.ToString(), anchor = rt?.anchorMin.ToString(),
                    position = rt?.anchoredPosition.ToString(), sprite = image?.sprite?.name });
                if (nativeTabletSprite == null && image?.sprite != null && rt != null && rt.sizeDelta.x > 500 && rt.sizeDelta.y > 300)
                    nativeTabletSprite = image.sprite;
                for (int n = 0; n < t.childCount; n++) { var child = t.GetChild(n); Walk(child, path + "/" + child.name); }
            }
            Walk(neg.uiObject.transform, neg.uiObject.name);
            System.IO.File.WriteAllText(System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "BestOffer-native-layout.json"),
                System.Text.Json.JsonSerializer.Serialize(entries, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { LoggerInstance.Warning("终端布局诊断：" + e.Message); }
    }
}
