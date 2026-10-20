using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2Cpp;
using NativeFeatures = Il2CppSystem.Collections.Generic.List<Il2Cpp.ItemFeature>;

namespace DeepSpaceBestOffer;

public sealed class TradeItem
{
    public string Key { get; set; } = "";
    public string Group { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public long Price { get; set; }
    public bool PriceKnown { get; set; }
    public bool RiskBlocked { get; set; }
    public bool Eligible { get; set; }
    public string Reason { get; set; } = "";
    public string ParentKey { get; set; } = "";
    public bool RuleOnly { get; set; }
    public bool IsRetailPackage { get; set; }
    public int UnitCount { get; set; }
    public int ContainedUnits { get; set; }
    public bool MarketDiscounted { get; set; }
    public bool MarketMarkedUp { get; set; }
    // Stable identities of contents sold with this movable, top-level object.
    // Fixed shop fixtures must not claim the independent inventory they expose.
    public List<string> DescendantKeys { get; set; } = new();
    public GameItem Native { get; set; } = null!;
}

public sealed class TradeSnapshot
{
    public string ClientKey { get; set; } = "";
    public string ClientName { get; set; } = "";
    public long Budget { get; set; }
    public long SelectionBudget { get; set; }
    public TradeOptions Options { get; set; } = new();
    public bool RequireVariety { get; set; }
    public bool BuyerRefusesRiskyGoods { get; set; }
    public int MaxItems { get; set; } = int.MaxValue;
    public bool CanTrade { get; set; }
    public string Message { get; set; } = "";
    public int MarketDay { get; set; }
    public string MarketSignature { get; set; } = "";
    public string MarketSummary { get; set; } = "";
    public List<TradeItem> Items { get; set; } = new();
    // Include unexpanded contents as well: a same-name All rule must protect
    // newly acquired items even when they are sold inside a whole container.
    public Dictionary<string, InventoryRuleMember> RuleMembers { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> CategoryOverrides { get; set; } = new(StringComparer.Ordinal);
}

public sealed class TradeActionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public long Total { get; set; }
    public long NominalTotal { get; set; }
    public long UnpaidTotal { get; set; }
    public int MovedCount { get; set; }
    public int SoldCount { get; set; }
}

/// <summary>
/// All entry points must run synchronously on Unity's main thread. This adapter
/// calls the game's pricing and inventory APIs; it never changes money/budget.
/// </summary>
public sealed class GameTradeAdapter
{
    public TradeSnapshot Capture(TradeOptions? options = null)
    {
        var result = new TradeSnapshot { Options = (options ?? new TradeOptions()).Clone() };
        try
        {
            var store = PlayerStore.Instance;
            var entry = EmporiumEntry.Instance;
            var client = store?.currentClientInstance?.GetClientBlueprint();
            if (store == null || entry == null) return Unavailable(result, "请进入店铺后设置库存规则。");
            var marketEvents = ReadMarketEvents();
            result.MarketDay = StoreStation.Instance?.dayCounter ?? 0;
            result.MarketSignature = MarketSignature(marketEvents, result.MarketDay);
            var marketTypes = marketEvents.SelectMany(ev => ev.negociationDatas?.ToArray() ?? Array.Empty<NegociationData>()).Where(data => data != null).ToArray();
            result.MarketSummary = $"第 {result.MarketDay} 天  ·  打折 {marketTypes.Where(d => d.value < 0).Select(d => d.itemType).Distinct().Count()} 类 / 加价 {marketTypes.Where(d => d.value > 0).Select(d => d.itemType).Distinct().Count()} 类";
            var unavailableReason = "";
        if (store == null || client == null) unavailableReason = "等待买家到来；现在可以设置售卖规则。";
            else
            {
                result.ClientKey = ClientKey(client);
                result.ClientName = client.displayName ?? "买家";
                result.Budget = client.useClientBudget ? client.GetBudget() : int.MaxValue;
                if (result.Budget < 0) unavailableReason = "买家预算异常；库存规则仍可设置。";
                else if (!result.Options.TryGetSelectionBudget(result.Budget, out var selectionBudget, out var optionError))
                    unavailableReason = optionError;
                else result.SelectionBudget = selectionBudget;
                result.RequireVariety = client.isRequireVariety;
                result.BuyerRefusesRiskyGoods = client.noAcceptingContraband;
                result.MaxItems = client.isMultiBuyDisabled ? 1 : int.MaxValue;
                if (!client.IsClientBuying() || client.isBusinessComplete || client.isRegularSellDisable)
                    unavailableReason = "这位顾客当前不接受普通商品出售；库存规则仍可设置。";
                else if (entry.backInvinvElement == null) unavailableReason = "交易台尚未准备好；库存规则仍可设置。";
                else if (store.lockAllFlag > 0 || store.lockAllInventoryFlag > 0 || store.isClientBeingArrested || store.isHandlingShootout)
                    unavailableReason = "请等待当前对话或事件结束后再报价；库存规则仍可设置。";
            }
            result.CanTrade = unavailableReason.Length == 0;

            var identities = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
            var seenCandidates = new HashSet<IntPtr>();
            var containedKeys = new HashSet<string>(StringComparer.Ordinal);
            var descendantsCache = new Dictionary<IntPtr, List<GameItem>>();
            var removalCache = new Dictionary<IntPtr, string>();
            AddInventory(entry.invElement, "库存");
            AddInventory(entry.backInvinvElement, "交易台");
            AddInventory(entry.backInvinvElementCounter, "柜台");
            if (result.Options.IncludeShowcase) AddInventory(entry.showcaseElement, "展示柜");
            if (result.Options.IncludeHiddenStorage) AddInventory(entry.hiddenElement, "隐藏仓库");
            // Some storage interfaces expose the same physical object through
            // another inventory root. A movable container and its contents must
            // never both become independent choices in the solver.
            result.Items.RemoveAll(row => containedKeys.Contains(row.Key) && !row.RuleOnly);
            result.Message = !result.CanTrade ? unavailableReason : result.Items.Count == 0 ? "没有可供筛选的自有物品。" :
                "已按货源与当天行情筛选；机器内容默认保留，包装默认整件报价。";
            return result;

            void AddInventory(GameInventory? inventory, string source)
            {
                if (inventory == null || inventory.childItems == null) return;
                for (var i = 0; i < inventory.childItems.Count; i++)
                    AddItem(inventory.childItems[i], source);
            }

            void AddItem(GameItem item, string source, string parentKey = "", bool includeUnavailable = false)
            {
                if (item == null || (!includeUnavailable && !GeneralHelper.IsItemOwned(item))) return;
                var key = RegisterIdentity(item);
                if (!seenCandidates.Add(item.Pointer))
                {
                    // A native inventory alias may have exposed the child before
                    // its package. Retain its real hierarchy in the rule panel.
                    if (parentKey.Length > 0 && result.Items.FirstOrDefault(i => i.Key == key) is { } existing)
                    { existing.ParentKey = parentKey; existing.Source = source; }
                    return;
                }
                var row = new TradeItem { Key = key, Group = item.identifier ?? "", Source = source, Native = item,
                    ParentKey = parentKey, UnitCount = item.unitCount };
                var expanded = false;
                var directChildren = new List<GameItem>();
                try
                {
                    row.Name = item.GetDisplayName(false) ?? item.name ?? item.identifier ?? "物品";
                    var removalReason = GetRemovalReason(item);
                    if (removalReason.Length == 0) removalReason = NativeInventorySourcePolicy.BoundaryReason(item, result.Options);
                    var descendants = GetDescendants(item);
                    var containerKind = NativeInventorySourcePolicy.GetContainerKind(item);
                    row.IsRetailPackage = containerKind == InventoryContainerKind.RetailPackage;
                    if (row.IsRetailPackage) row.ContainedUnits = descendants.Sum(child => Math.Max(0, child.unitCount));
                    if (InventorySourcePolicy.MayExpand(containerKind, result.Options.IncludeContainerContents,
                        result.Options.IncludeMachineContents, result.Options.IncludeMachineProducts, result.Options.SplitRetailPackages))
                    {
                        // Find physical immediate children, not arbitrary graph
                        // aliases. Recursion retains a source path and allows
                        // siblings to be chosen independently.
                        bool CanTake(GameItem candidate) => GetRemovalReason(candidate).Length == 0 &&
                            NativeInventorySourcePolicy.BoundaryReason(candidate, result.Options).Length == 0;
                        bool anyExtractable = false;
                        foreach (var child in descendants)
                        {
                            if (!Same(child.parentInventory?.GetParentItem(), item)) continue;
                            var extractable = CanTake(child) || GetDescendants(child).Any(CanTake);
                            anyExtractable |= extractable;
                            // Unavailable pieces still belong under the package
                            // and must retain their required/forbidden rules.
                            if (row.IsRetailPackage || extractable)
                                directChildren.Add(child);
                        }
                        expanded = anyExtractable;
                    }
                    if (expanded) row.Reason = row.IsRetailPackage ? "已选择拆售包装内容，本体不参与出售" : "已展开允许的内容，本体不参与整件出售";
                    else if (removalReason.Length > 0) row.Reason = removalReason;
                    else if (NativeInventorySourcePolicy.ProtectsToolboxContents(descendants, result.Options.IncludeMachineContents))
                        row.Reason = "未启用机器货源：保留工具箱内的工具";
                    else if (!result.CanTrade) row.Reason = "等待交易";
                    else if (!client!.IsClientBuyingThisItem(item)) row.Reason = "买家不收此物品";
                    else if (client.isRequireVariety && client.IsItemInVarietyList(item)) row.Reason = "买家已经买过同种物品";
                    else if (client.CanClientExposeAnyFeature(item)) row.Reason = "需要先完成买家的物品鉴定";
                    if (!expanded && removalReason.Length == 0)
                    {
                        row.DescendantKeys = descendants.Select(RegisterIdentity).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
                        containedKeys.UnionWith(row.DescendantKeys);
                    }
                    // Evaluate every entity sold with the container separately.
                    // A demand for the box does not permit protected contents.
                    var riskClient = result.CanTrade ? client : null;
                    row.RiskBlocked = IsRiskBlocked(item, result.Options, riskClient) ||
                        (!expanded && removalReason.Length == 0 && descendants.Any(child => IsRiskBlocked(child, result.Options, riskClient)));
                    if (row.RiskBlocked) row.Reason = result.BuyerRefusesRiskyGoods
                        ? "买家拒收违禁品或赃物" : "自动禁卖保护";
                    var marketFacts = MarketFacts(item, marketEvents);
                    row.MarketDiscounted = marketFacts.Discounted;
                    row.MarketMarkedUp = marketFacts.MarkedUp;
                    if (!expanded && removalReason.Length == 0)
                        foreach (var child in descendants)
                        {
                            var facts = MarketFacts(child, marketEvents);
                            row.MarketDiscounted |= facts.Discounted;
                            row.MarketMarkedUp |= facts.MarkedUp;
                        }
                    if (result.Options.BlockMarketDiscounts && row.MarketDiscounted && !expanded && row.Reason.Length == 0)
                        row.Reason = "当天事件打折：已按交易设置保留";
                    if (result.CanTrade)
                    {
                        row.Price = Quote(item, store!, client!, entry, marketEvents);
                        row.PriceKnown = true;
                    }
                    if (row.PriceKnown && row.Price <= 0 && row.Reason.Length == 0) row.Reason = "当前报价为零";
                    if (row.Price > int.MaxValue && row.Reason.Length == 0) row.Reason = "报价超过游戏原生金额上限";
                    if (row.Price > result.SelectionBudget && row.Reason.Length == 0) row.Reason = "单件报价超过本次允许上限";
                    row.Eligible = row.Reason.Length == 0;
                }
                catch (Exception e) { row.Reason = "无法可靠估价：" + ShortError(e); row.Eligible = false; }
                result.Items.Add(row);
                if (expanded)
                    foreach (var child in directChildren) AddItem(child, source + " / " + row.Name,
                        row.IsRetailPackage ? row.Key : parentKey, row.IsRetailPackage);
                else if (row.IsRetailPackage)
                    foreach (var child in GetDescendants(item)) AddPackageRule(child, row, source + " / " + row.Name);
            }

            void AddPackageRule(GameItem item, TradeItem package, string source)
            {
                var key = RegisterIdentity(item);
                var row = result.Items.FirstOrDefault(i => i.Key == key);
                if (row == null)
                {
                    seenCandidates.Add(item.Pointer);
                    row = new TradeItem { Key = key, Group = item.identifier ?? "", Native = item };
                    result.Items.Add(row);
                }
                row.Name = item.GetDisplayName(false) ?? item.name ?? item.identifier ?? "内容物";
                row.ParentKey = package.Key; row.Source = source; row.RuleOnly = true;
                row.UnitCount = item.unitCount;
                row.DescendantKeys = GetDescendants(item).Select(RegisterIdentity).Distinct().ToList();
                row.Eligible = false; row.Reason = "随包装整件出售；在此设置内容规则";
                row.RiskBlocked = IsRiskBlocked(item, result.Options, result.CanTrade ? client : null);
                var facts = MarketFacts(item, marketEvents); row.MarketDiscounted = facts.Discounted; row.MarketMarkedUp = facts.MarkedUp;
                if (result.CanTrade) { row.Price = Quote(item, store!, client!, entry, marketEvents); row.PriceKnown = true; }
            }

            List<GameItem> GetDescendants(GameItem item)
            {
                if (descendantsCache.TryGetValue(item.Pointer, out var cached)) return cached;
                var descendants = GraphUtils.FindAllChildrenType<GameItem>(item.Cast<GraphNodeStorage>(), null, null);
                var pointers = new HashSet<IntPtr> { item.Pointer };
                var found = new List<GameItem>();
                for (var i = 0; i < descendants.Count; i++)
                {
                    var child = descendants[i];
                    if (child != null && pointers.Add(child.Pointer)) { RegisterIdentity(child); found.Add(child); }
                }
                descendantsCache[item.Pointer] = found;
                return found;
            }

            string GetRemovalReason(GameItem item)
            {
                if (!removalCache.TryGetValue(item.Pointer, out var reason))
                    removalCache[item.Pointer] = reason = RemovalReason(item);
                return reason;
            }

            string RegisterIdentity(GameItem item)
            {
                var key = ItemKey(item);
                if (identities.TryGetValue(key, out var pointer) && pointer != item.Pointer)
                    throw new InvalidOperationException("存档中存在重复物品 ID，无法安全区分必卖/禁卖规则。");
                identities[key] = item.Pointer;
                if (!result.RuleMembers.ContainsKey(key))
                {
                    string parentKey = "";
                    var parent = item.parentInventory?.GetParentItem();
                    var visited = new HashSet<IntPtr> { item.Pointer };
                    while (parent != null && visited.Add(parent.Pointer))
                    {
                        if (NativeInventorySourcePolicy.GetContainerKind(parent) == InventoryContainerKind.RetailPackage)
                        { parentKey = ItemKey(parent); break; }
                        parent = parent.parentInventory?.GetParentItem();
                    }
                    result.RuleMembers[key] = new InventoryRuleMember(key,
                        item.GetDisplayName(false) ?? item.name ?? item.identifier ?? "物品", parentKey);
                    bool demand = result.CanTrade && client != null && result.Options.CategoryRules.Any(c => c.AllowDemandException) && HasExplicitDemand(item, client);
                    int category = CategoryRuleLogic.Resolve(result.Options.CategoryRules,
                        id => NativeCategoryPolicy.Matches(item, id), demand, result.MarketDay);
                    if (category != 0) result.CategoryOverrides[key] = category;
                }
                return key;
            }
        }
        catch (Exception e) { return Unavailable(result, "暂时无法读取交易状态：" + ShortError(e)); }
    }

    public TradeActionResult Apply(TradeSnapshot snapshot, IEnumerable<string> keys) => Execute(snapshot, keys, false);

    public TradeActionResult Sell(TradeSnapshot snapshot, IEnumerable<string> keys) => Execute(snapshot, keys, true);

    // Canonical JSON avoids delimiter collisions in identifiers and captures
    // equal-price content swaps as well as changes to the pricing constraints.
    public static string SnapshotSignature(TradeSnapshot snapshot) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            snapshot.ClientKey, snapshot.Budget, snapshot.SelectionBudget, snapshot.CanTrade,
            snapshot.RequireVariety, snapshot.BuyerRefusesRiskyGoods, snapshot.MaxItems, snapshot.MarketDay, snapshot.MarketSignature,
            Options = new { snapshot.Options.IncludeShowcase, snapshot.Options.IncludeHiddenStorage,
                snapshot.Options.IncludeContainerContents, snapshot.Options.AllowOverBudget,
                snapshot.Options.IncludeMachineContents, snapshot.Options.IncludeMachineProducts, snapshot.Options.SplitRetailPackages,
                snapshot.Options.BlockMarketDiscounts, snapshot.Options.PreferMarketMarkups,
                snapshot.Options.BlockContraband, snapshot.Options.BlockStolen,
                snapshot.Options.AllowContrabandDemandExceptions, snapshot.Options.AllowStolenDemandExceptions,
                snapshot.Options.OverBudgetMode, snapshot.Options.OverBudgetValue, snapshot.Options.CategoryRules },
            RuleMembers = snapshot.RuleMembers.Values.OrderBy(i => i.Key, StringComparer.Ordinal).ToArray(),
            CategoryOverrides = snapshot.CategoryOverrides.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray(),
            Items = snapshot.Items.OrderBy(i => i.Key, StringComparer.Ordinal).Select(i => new
            {
                i.Key, i.Group, i.Name, i.Price, i.PriceKnown, i.RiskBlocked, i.Eligible, i.Reason, i.Source,
                i.ParentKey, i.RuleOnly, i.IsRetailPackage, i.UnitCount, i.ContainedUnits, i.MarketDiscounted, i.MarketMarkedUp,
                DescendantKeys = i.DescendantKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray()
            }).ToArray()
        });

    /// <summary>
    /// Explicit developer diagnostic, never called by normal UI. Run on the main
    /// thread and save the returned JSON. Warms persistent item IDs once, then
    /// verifies repeated pricing restores native references and leaves trading
    /// state unchanged. Does not move or sell any object.
    /// </summary>
    public string DevelopmentOnlyAuditCapture(int repeats = 3, TradeOptions? options = null)
    {
        var baseline = Capture(options);
        var store = PlayerStore.Instance;
        if (store == null || baseline.Items.Count == 0)
            return System.Text.Json.JsonSerializer.Serialize(new { Passed = false, baseline.Message });
        var client = store.currentClientInstance?.GetClientBlueprint();
        var watched = new Dictionary<long, GameItem>();
        foreach (var row in baseline.Items)
        {
            watched[row.Native.Pointer.ToInt64()] = row.Native;
            var children = GraphUtils.FindAllChildrenType<GameItem>(row.Native.Cast<GraphNodeStorage>(), null, null);
            for (var i = 0; i < children.Count; i++) watched[children[i].Pointer.ToInt64()] = children[i];
        }
        string State()
        {
            var values = new List<string> { store.playerCash.ToString(), client?.GetBudget().ToString() ?? "no-buyer",
                client == null ? "" : ClientKey(client), client?.acceptDealDialoguePlayed.ToString() ?? "", client?.isBusinessComplete.ToString() ?? "",
                store.lockAllFlag.ToString(), store.lockAllInventoryFlag.ToString() };
            foreach (var item in watched.Values.OrderBy(x => x.Pointer.ToInt64()))
            {
                values.Add($"{item.Pointer}:{item.parentInventory?.Pointer}:{item.itemFeatures?.Pointer}:{item.uniqueId}:{item.unitCount}:{GeneralHelper.IsItemOwned(item)}");
                if (item.itemFeatures == null) continue;
                for (var i = 0; i < item.itemFeatures.Count; i++)
                {
                    var f = item.itemFeatures[i];
                    values.Add(f == null ? "null" : $"{f.Pointer}:{f.identifier}:{f.GetCurrentValueMod()}:{f.isDisabled}:{f.IsExposed()}:{f.parentItemUniqueId}");
                }
            }
            return string.Join("|", values);
        }
        var before = State();
        var expected = SnapshotSignature(baseline);
        var checks = new List<object>();
        var passed = true;
        for (var i = 0; i < Math.Clamp(repeats, 1, 10); i++)
        {
            var capture = Capture(baseline.Options);
            var stateRestored = before == State();
            var quoteStable = expected == SnapshotSignature(capture);
            passed &= stateRestored && quoteStable;
            checks.Add(new { Iteration = i + 1, StateRestored = stateRestored, QuoteStable = quoteStable });
        }
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Passed = passed, baseline.CanTrade, baseline.ClientKey, baseline.ClientName, baseline.Budget, baseline.SelectionBudget, baseline.Options,
            baseline.MarketDay, baseline.MarketSummary, baseline.MarketSignature, baseline.BuyerRefusesRiskyGoods,
            Cash = store.playerCash, WatchedItemsIncludingContents = watched.Count, Checks = checks,
            BuyingIds = client?.clientBuyingIdList?.ToArray(), BuyingTags = client?.clientBuyingTagList?.ToArray(),
            Items = baseline.Items.Select(i => new { i.Key, i.Group, i.Name, i.Source, i.Price, i.PriceKnown,
                i.Eligible, i.RiskBlocked, i.Reason, i.DescendantKeys, i.ParentKey, i.RuleOnly, i.IsRetailPackage,
                i.UnitCount, i.ContainedUnits, i.MarketDiscounted, i.MarketMarkedUp })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private TradeActionResult Execute(TradeSnapshot snapshot, IEnumerable<string> keys, bool sell)
    {
        var result = new TradeActionResult();
        StoreClient? transactionClient = null;
        var nativeSaleInProgress = false;
        try
        {
            var selectedKeys = new HashSet<string>(keys, StringComparer.Ordinal);
            if (selectedKeys.Count == 0) return Failed(result, "请选择一个包含物品的方案。");
            var current = Capture(snapshot.Options);
            if (!current.CanTrade) return Failed(result, current.Message);
            if (current.ClientKey != snapshot.ClientKey) return Failed(result, "买家已经更换，请重新计算方案。");
            if (SnapshotSignature(snapshot) != SnapshotSignature(current))
                return Failed(result, "库存、容器内容、物品位置或交易条件已变化，请重新计算方案。");
            var original = snapshot.Items.ToDictionary(x => x.Key, StringComparer.Ordinal);
            var live = current.Items.ToDictionary(x => x.Key, StringComparer.Ordinal);
            var selection = new List<TradeItem>();
            foreach (var key in selectedKeys)
            {
                if (!original.TryGetValue(key, out var old) || !live.TryGetValue(key, out var item))
                    return Failed(result, "方案中的物品已经移动或不再属于你，请重新计算。");
                if (!item.Eligible) return Failed(result, item.Name + "：" + item.Reason);
                if (old.Price != item.Price) return Failed(result, item.Name + "的报价发生变化，请重新计算。");
                selection.Add(item);
            }
            var total = selection.Sum(x => x.Price);
            if (total > current.SelectionBudget) return Failed(result, "方案原报价超过当前允许上限，请重新计算。");
            if (selection.Count > current.MaxItems) return Failed(result, "该买家一次只接受一件商品。");
            if (current.RequireVariety && selection.GroupBy(x => x.Group, StringComparer.Ordinal).Any(g => g.Count() > 1))
                return Failed(result, "该买家要求不同种类，方案包含重复商品。");

            var entry = EmporiumEntry.Instance;
            var store = PlayerStore.Instance;
            var client = store.currentClientInstance.GetClientBlueprint();
            transactionClient = client;
            var table = entry.backInvinvElement;
            // Never silently move unrelated goods (especially a customer's goods).
            for (var i = 0; i < table.childItems.Count; i++)
                if (!selectedKeys.Contains(ItemKey(table.childItems[i])))
                    return Failed(result, "交易台上有未选中的物品，请先移回库存，再应用此方案。");

            // For manual staging, require all selected goods to fit. For selling,
            // items are staged one at a time so total area is not an artificial cap.
            foreach (var row in selection.OrderByDescending(x => x.Source == "交易台").ThenByDescending(x => x.Price))
            {
                var active = store.currentClientInstance?.GetClientBlueprint();
                if (active == null || ClientKey(active) != current.ClientKey || active.isBusinessComplete)
                    return Failed(result, "买家交易状态发生变化，已停止。" + Progress(result));
                if (!GeneralHelper.IsItemOwned(row.Native))
                    return Failed(result, row.Name + "已不再属于你，已停止。" + Progress(result));
                var boundaryReason = NativeInventorySourcePolicy.BoundaryReason(row.Native, current.Options);
                if (boundaryReason.Length > 0) return Failed(result, boundaryReason + "；已停止。" + Progress(result));
                if (MarketSignature(ReadMarketEvents(), StoreStation.Instance?.dayCounter ?? 0) != current.MarketSignature)
                    return Failed(result, "当天日历或市场事件已变化，请重新计算。" + Progress(result));
                if (IsSaleBundleRiskBlocked(row.Native, current.Options, active))
                    return Failed(result, "买家拒收或自动禁卖条件已变化，已停止后续操作。" + Progress(result));
                if (IsSaleBundleMarketBlocked(row.Native, current.Options))
                    return Failed(result, "商品受当天事件打折影响，已按交易设置保留。" + Progress(result));
                if (!Same(row.Native.parentInventory, table))
                {
                    var removalReason = RemovalReason(row.Native);
                    if (removalReason.Length > 0) return Failed(result, row.Name + "：" + removalReason + "。" + Progress(result));
                    var remainder = GraphUtils.TryAcceptOnce(table.Cast<GraphNodeStorage>(), row.Native, -1);
                    if (remainder != 0 || !Same(row.Native.parentInventory, table))
                        return Failed(result, "交易台无法完整放下「" + row.Name + "」。已放入的物品仍在台上；可清出台面或使用逐件卖出。" + Progress(result));
                    result.MovedCount++;
                }
                // Moving a native item runs placement callbacks. Those may
                // alter this same buyer's demand or the container contents.
                active = store.currentClientInstance?.GetClientBlueprint();
                if (active == null || ClientKey(active) != current.ClientKey || active.isBusinessComplete)
                    return Failed(result, "摆放后买家交易状态发生变化，已停止；物品保留在台上。" + Progress(result));
                if (IsSaleBundleRiskBlocked(row.Native, current.Options, active))
                    return Failed(result, "摆放后买家拒收或自动禁卖条件已变化，本件尚未成交，物品保留在台上。" + Progress(result));
                if (row.Native.unitCount != row.UnitCount)
                    return Failed(result, "摆放后商品数量发生变化，已停止；物品保留在台上。" + Progress(result));
                if (IsSaleBundleMarketBlocked(row.Native, current.Options))
                    return Failed(result, "摆放后商品受当天事件打折影响，已停止；物品保留在台上。" + Progress(result));
                var value = row.Native.GetNegociatedValue();
                if (value != row.Price)
                    return Failed(result, "「" + row.Name + "」实际报价为 " + value + "，与预览 " + row.Price + " 不同。已停止，请重新计算；物品保留在台上。" + Progress(result));
                // Staging may also exceed the budget within the user's selected
                // allowance. This only relaxes eligibility; it never sells.
                var allowOverBudget = current.Options.AllowOverBudget;
                if (!store.CanSellThisItem(row.Native, allowOverBudget))
                    return Failed(result, "游戏当前不允许出售「" + row.Name + "」，已停止；物品保留在台上。" + Progress(result));
                if (!sell) continue;
                var budgetBefore = active.GetBudget();
                if (value < 0 || value > int.MaxValue || (active.useClientBudget && budgetBefore < 0))
                    return Failed(result, "游戏原生报价或预算超出安全金额范围，已停止。" + Progress(result));
                if (!allowOverBudget && active.useClientBudget && value > budgetBefore)
                    return Failed(result, "买家剩余预算不足，已停止。" + Progress(result));
                var cashBefore = store.playerCash;
                var expectedPaid = active.useClientBudget ? Math.Min(value, budgetBefore) : value;
                if ((long)cashBefore + expectedPaid > int.MaxValue)
                    return Failed(result, "本次成交将超过游戏现金金额上限，已停止。" + Progress(result));
                active = store.currentClientInstance?.GetClientBlueprint();
                if (active == null || ClientKey(active) != current.ClientKey || active.isBusinessComplete)
                    return Failed(result, "卖出前买家交易状态发生变化，已停止；物品保留在台上。" + Progress(result));
                if (IsSaleBundleRiskBlocked(row.Native, current.Options, active))
                    return Failed(result, "卖出前买家拒收或自动禁卖条件已变化，本件尚未成交，物品保留在台上。" + Progress(result));
                if (MarketSignature(ReadMarketEvents(), StoreStation.Instance?.dayCounter ?? 0) != current.MarketSignature ||
                    IsSaleBundleMarketBlocked(row.Native, current.Options))
                    return Failed(result, "卖出前当天行情条件发生变化，请重新计算；物品保留在台上。" + Progress(result));
                // SellMultiple(one) would run the accepted-deal callback and
                // start its dialogue after EVERY object. Match its native loop
                // using SellItem, then run its UI/dialogue tail once in finally.
                nativeSaleInProgress = true;
                store.SellItem(row.Native);
                if (GeneralHelper.IsItemOwned(row.Native))
                    return Failed(result, "游戏没有完整完成「" + row.Name + "」的交易，已停止。请检查当前预算和物品；原生回调可能已经改变部分状态。");
                result.SoldCount++;
                var actualPaid = (long)store.playerCash - cashBefore;
                result.Total += actualPaid;
                result.NominalTotal += value;
                result.UnpaidTotal += value - actualPaid;
                nativeSaleInProgress = false;
                var expectedBudget = active.useClientBudget ? budgetBefore - expectedPaid : budgetBefore;
                if (actualPaid != expectedPaid || active.GetBudget() != expectedBudget)
                    return Failed(result, "「" + row.Name + "」已成交，但原生实收或预算变化与预期不一致，已停止后续交易。" + Progress(result));
                // Selling makes the item customer-owned. Clear it using the game's
                // normal sold-inventory destination before staging the next object.
                if (Same(row.Native.parentInventory, table) && entry.soldElement != null)
                    GraphUtils.TryAcceptOnce(entry.soldElement.Cast<GraphNodeStorage>(), row.Native, -1);
            }

            NegociationUIManager.Instance.RefreshUI();
            if (!sell)
            {
                var active = store.currentClientInstance?.GetClientBlueprint();
                if (active == null || ClientKey(active) != current.ClientKey || active.isBusinessComplete)
                    return Failed(result, "摆放期间买家状态发生变化，请检查交易台后重新计算。" + Progress(result));
                var liveBudget = active.useClientBudget ? active.GetBudget() : int.MaxValue;
                if (liveBudget != current.Budget)
                    return Failed(result, "摆放期间买家预算发生变化，请检查交易台后重新计算。" + Progress(result));
                var tableKeys = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < table.childItems.Count; i++) tableKeys.Add(ItemKey(table.childItems[i]));
                if (!tableKeys.SetEquals(selectedKeys))
                    return Failed(result, "实际台面物品与方案不一致，已停止；已移动物品保留在台上。" + Progress(result));
                long stagedTotal = 0;
                foreach (var row in selection)
                {
                    if (!Same(row.Native.parentInventory, table) || !GeneralHelper.IsItemOwned(row.Native) ||
                        !store.CanSellThisItem(row.Native, current.Options.AllowOverBudget))
                        return Failed(result, "「" + row.Name + "」在摆放后状态发生变化，请检查交易台后重新计算。" + Progress(result));
                    if (IsSaleBundleRiskBlocked(row.Native, current.Options, active))
                        return Failed(result, "摆放后买家拒收或自动禁卖条件已变化，尚未成交；物品保留在台上。" + Progress(result));
                    if (row.Native.unitCount != row.UnitCount || IsSaleBundleMarketBlocked(row.Native, current.Options))
                        return Failed(result, "摆放后商品数量或当天行情条件发生变化，请重新计算。" + Progress(result));
                    var stagedPrice = row.Native.GetNegociatedValue();
                    if (stagedPrice != row.Price)
                        return Failed(result, "「" + row.Name + "」在摆放后报价发生变化，请重新计算。" + Progress(result));
                    stagedTotal = checked(stagedTotal + stagedPrice);
                }
                if (stagedTotal != total)
                    return Failed(result, "实际台面总报价与方案不一致，请检查交易台后重新计算。" + Progress(result));
                if (MarketSignature(ReadMarketEvents(), StoreStation.Instance?.dayCounter ?? 0) != current.MarketSignature)
                    return Failed(result, "摆放期间当天日历或事件发生变化，请重新计算。" + Progress(result));
                // Native GetMultiSellValue does not filter by budget, but returns
                // int. Cross-check it where representable; otherwise the checked
                // long sum of native per-item prices above is authoritative.
                if (total <= int.MaxValue)
                {
                    var shown = NegociationUIManager.Instance.GetMultiSellValue();
                    if (shown != total) return Failed(result, "物品已经放上交易台，但游戏显示总价 " + shown + " 与方案 " + total + " 不一致，请检查台面后重新计算。");
                }
                result.Total = total;
                result.NominalTotal = total;
                var placement = result.MovedCount == 0 ? "所选方案已在交易台" : "方案已全部放上交易台";
                var expectedReceipt = active.useClientBudget ? Math.Min(total, liveBudget) : total;
                var expectedUnpaid = total - expectedReceipt;
                result.Message = expectedUnpaid > 0
                    ? placement + "，原价 " + total + "，预计实收 " + expectedReceipt + "，少收 " + expectedUnpaid +
                      "。尚未成交；请打开最佳报价面板并确认卖出，游戏原有接受按钮不支持此超预算方案。"
                    : placement + "，游戏总报价 " + total + "。尚未成交，请点击游戏原有接受按钮成交。";
            }
            result.Success = true;
            if (sell) result.Message = "已按方案卖出 " + result.SoldCount + " 件，实收 " + result.Total +
                (result.UnpaidTotal > 0 ? "，让利 " + result.UnpaidTotal : "") + "。";
            return result;
        }
        catch (Exception e)
        {
            return Failed(result, "操作停止：" + ShortError(e) + "。" +
                (nativeSaleInProgress
                    ? "原生交易可能已经部分改变预算或物品状态，请检查后再操作。已确认完成 " + result.SoldCount + " 件。"
                    : Progress(result)));
        }
        finally
        {
            if (sell && result.SoldCount > 0 && transactionClient != null)
            {
                try
                {
                    var ui = NegociationUIManager.Instance;
                    ui.audioSourceCashRegister?.Play();
                    ui.RefreshUI();
                    // The source is the one-shot tail of native SellMultiple.
                    // Do not attach the old client's dialogue to a new visitor
                    // if a custom onItemBought callback changed the customer.
                    var active = PlayerStore.Instance.currentClientInstance?.GetClientBlueprint();
                    if (Same(active, transactionClient))
                    {
                        if (!NegociationUIManager.PlayAcceptDealDialogueOnce(transactionClient))
                            transactionClient.OnDealAccepted(null);
                        ui.UpdateAcceptOfferButtonForSell();
                    }
                }
                catch (Exception e)
                {
                    result.Success = false;
                    result.Message += " 交易已经结算，但界面收尾失败：" + ShortError(e) + "。" + Progress(result);
                }
            }
        }
    }

    private static bool IsRiskBlocked(GameItem item, TradeOptions options, StoreClient? client)
    {
        // The native refusal flag already reflects client initialization and
        // acceptance overrides. Do not infer refusal from names or demand tags,
        // or reapply AcceptingContrabandOverride to this effective native fact.
        var refusesRiskyGoods = client?.noAcceptingContraband == true;
        if (!refusesRiskyGoods && !options.BlockContraband && !options.BlockStolen) return false;
        // Both tags are also used by the game's public risk tooltips. Do not
        // inspect hidden appraisal features or persistent item identities.
        var contraband = item.IsTag("CONTRABAND_ITEM_TAG");
        var stolen = item.IsTag("STOLEN_TAG");
        if (!contraband && !stolen) return false;
        var demand = client != null && (options.AllowContrabandDemandExceptions || options.AllowStolenDemandExceptions) && HasExplicitDemand(item, client);
        return RiskPolicy.GetBlockedCategories(options, contraband, stolen, demand, refusesRiskyGoods) != RiskCategory.None;
    }

    private static bool HasExplicitDemand(GameItem item, StoreClient client)
    {
        if (client.isBusinessComplete || client.isRegularSellDisable || !client.IsClientBuying()) return false;
        var ids = client.clientBuyingIdList?.ToArray() ?? Array.Empty<string>();
        var tags = client.clientBuyingTagList?.ToArray() ?? Array.Empty<string>();
        return RiskPolicy.MatchesExplicitDemand(item.identifier ?? "", ids, tags, tag => item.IsTag(tag));
    }

    private static bool IsSaleBundleRiskBlocked(GameItem item, TradeOptions options, StoreClient? client)
    {
        if (client?.noAcceptingContraband != true && !options.BlockContraband && !options.BlockStolen &&
            !options.CategoryRules.Any(c => c.Mode == 2)) return false;
        bool Blocked(GameItem entity) => IsRiskBlocked(entity, options, client) ||
            CategoryRuleLogic.Resolve(options.CategoryRules, id => NativeCategoryPolicy.Matches(entity, id),
                client != null && options.CategoryRules.Any(c => c.AllowDemandException) && HasExplicitDemand(entity, client),
                StoreStation.Instance?.dayCounter ?? 0) == 2;
        if (Blocked(item)) return true;
        // Re-read the physical graph after native callbacks. This traversal
        // never calls ItemKey/GetUniqueID, writes identities, or computes a quote.
        var descendants = GraphUtils.FindAllChildrenType<GameItem>(item.Cast<GraphNodeStorage>(), null, null);
        var seen = new HashSet<IntPtr> { item.Pointer };
        for (var n = 0; n < descendants.Count; n++)
        {
            var child = descendants[n];
            if (child != null && seen.Add(child.Pointer) && Blocked(child)) return true;
        }
        return false;
    }

    private static string RemovalReason(GameItem item)
    {
        if (!GeneralHelper.IsItemOwned(item)) return "物品不属于你";
        var inventory = item.parentInventory;
        if (inventory == null) return "物品不在可访问库存中";
        if (inventory.IsRemoveLocked()) return "所在容器已锁定";
        if (!item.MayRemove()) return "物品当前不可移动";
        if (item.unitCount <= 0 || item.MaxNumRemove() < item.unitCount)
            return "内建部件或数量限制：不能完整取出";
        if (item.IsTag("ITEM_BEHIND_LOCK_TAG")) return "物品位于锁定容器内";
        var seen = new HashSet<IntPtr> { item.Pointer };
        var ancestor = inventory.GetParentItem();
        while (ancestor != null)
        {
            if (!seen.Add(ancestor.Pointer)) return "容器关联异常，无法安全取出";
            if (!GeneralHelper.IsItemOwned(ancestor)) return "上级容器不属于你";
            if (LockHelper.IsCurrentlyLocked(ancestor)) return "上级容器已上锁";
            ancestor = ancestor.parentInventory?.GetParentItem();
        }
        return "";
    }

    private static List<StoreEvent> ReadMarketEvents()
    {
        var manager = StoreStation.Instance?.storeEventManager;
        if (manager == null) throw new InvalidOperationException("当天市场事件尚未准备好，请稍后重试。");
        var native = manager.GetActiveEvents();
        var events = new List<StoreEvent>();
        for (int i = 0; native != null && i < native.Count; i++)
            if (native[i] != null) events.Add(native[i]);
        return events;
    }

    private static string MarketSignature(List<StoreEvent> events, int day) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            Day = day,
            Events = events.OrderBy(ev => ev.identifier, StringComparer.Ordinal).Select(ev => new
            {
                ev.identifier, ev.startDay, ev.currentDay, ev.duration,
                Effects = (ev.negociationDatas?.ToArray() ?? Array.Empty<NegociationData>()).Where(d => d != null)
                    .Select(d => new { d.identifier, d.itemType, d.value }).OrderBy(d => d.identifier, StringComparer.Ordinal).ThenBy(d => d.itemType, StringComparer.Ordinal).ThenBy(d => d.value)
            })
        });

    private static (bool Discounted, bool MarkedUp) MarketFacts(GameItem item, List<StoreEvent> events)
    {
        bool discounted = false, markedUp = false;
        foreach (var ev in events)
        {
            if (ev.negociationDatas == null || !ev.IsItemAffectedByThisEvent(item)) continue;
            for (int i = 0; i < ev.negociationDatas.Count; i++)
            {
                var data = ev.negociationDatas[i];
                if (data == null || !data.IsNegociationDataAffectItem(item)) continue;
                // Native price composition applies 1 + value/100. Do not use
                // IsSurplus/IsShortage: their signs are reversed in this build.
                discounted |= data.value < 0;
                markedUp |= data.value > 0;
            }
        }
        return (discounted, markedUp);
    }

    private static bool IsSaleBundleMarketBlocked(GameItem item, TradeOptions options)
    {
        if (!options.BlockMarketDiscounts) return false;
        var events = ReadMarketEvents();
        if (MarketFacts(item, events).Discounted) return true;
        var children = GraphUtils.FindAllChildrenType<GameItem>(item.Cast<GraphNodeStorage>(), null, null);
        for (int i = 0; i < children.Count; i++)
            if (children[i] != null && MarketFacts(children[i], events).Discounted) return true;
        return false;
    }

    private static long Quote(GameItem item, PlayerStore store, StoreClient client, EmporiumEntry entry, List<StoreEvent> events)
    {
        var originalFeatures = item.itemFeatures;
        var originalParent = item.parentInventory;
        var features = new NativeFeatures();
        if (originalFeatures != null)
            for (var i = 0; i < originalFeatures.Count; i++) features.Add(originalFeatures[i]);
        try
        {
            item.itemFeatures = features;
            // This reference is only consulted by price traversal; no inventory
            // lists or placements are changed, and it is restored synchronously.
            item.parentInventory = entry.backInvinvElement;
            void Add(ItemFeature feature)
            {
                if (feature == null) return;
                for (var i = 0; i < features.Count; i++)
                    if (features[i] != null && features[i].identifier == feature.identifier) return;
                // Deliberately avoid AddItemFeature: it can execute condition actions.
                features.Add(feature);
            }

            if (store.IsCurrentClientAcceptMarkup()) Add(ItemFeatureList.RetailMarkUp());
            if (client.perfumeInfluence == 1) Add(ItemFeatureList.NeuroActivePerfumeSelling());
            if (client.perfumeInfluence == 2) Add(ItemFeatureList.PheromonePerfumeSelling());
            if (HealthData.HasAnyAddiction() && store.healthData != null && store.healthData.GetTotalAddictionPenalty() != 0)
                Add(ItemFeatureList.AddictionSellPenalty());
            if (client.clientFaction == StoreClient.FACTION_UPPER && StoreReputation.IsPerkUnlocked("UL_TIPS"))
                Add(ItemFeatureList.Pourboire());
            if (item.IsTag("CONTRABAND_ITEM_TAG"))
            {
                var level = item.GetTagReadonly("CONTRABAND_LEVEL")?.valueString;
                if (level == "CONTRABAND_LEVEL_LOW") Add(ItemFeatureList.ContrabandLowMarkUp());
                else if (level == "CONTRABAND_LEVEL_MID") Add(ItemFeatureList.ContrabandMidMarkUp());
                else if (level == "CONTRABAND_LEVEL_HIGH") Add(ItemFeatureList.ContrabandHighMarkUp());
                else if (level == "CONTRABAND_LEVEL_CRITICAL") Add(ItemFeatureList.ContrabandCriticalMarkUp());
            }
            if (item.IsGameItemType("RESTRICTED_1")) Add(ItemFeatureList.RestrictedLowMarkUp());
            else if (item.IsGameItemType("RESTRICTED_2")) Add(ItemFeatureList.RestrictedMidMarkUp());
            else if (item.IsGameItemType("RESTRICTED_3")) Add(ItemFeatureList.RestrictedHighMarkUp());
            if (client.isRequireVariety) Add(ItemFeatureList.VarietyBuyerBonus());
            if (HasCallback(client.onItemAddedToWeighted, "SetTourist")) Add(ItemFeatureList.TouristTax());

            var bargain = BargainUIManager.Instance;
            if (bargain != null && Same(ReadMember<StoreClient?>(bargain, "trackedClient", null), client))
            {
                if (bargain.DiscountOffered) Add(ItemFeatureList.BargainDiscount(bargain.DiscountPercent));
                if (ReadMember(bargain, "markupApplied", false)) Add(ItemFeatureList.BargainMarkup(bargain.MarkupPercent));
                if (ReadMember(bargain, "blackmailApplied", false)) Add(ItemFeatureList.BlackmailMarkup(20));
                if (ReadMember(bargain, "threatenApplied", false)) Add(ItemFeatureList.ThreatenMarkup(20));
            }
            for (var i = 0; i < events.Count; i++)
                {
                    var ev = events[i];
                    if (ev == null || !ev.IsItemAffectedByThisEvent(item) || ev.negociationDatas == null) continue;
                    for (var j = 0; j < ev.negociationDatas.Count; j++)
                    {
                        var data = ev.negociationDatas[j];
                        if (data != null && data.IsNegociationDataAffectItem(item)) Add(ItemFeature.CreateFeatureFromNegociationData(data));
                    }
                }
            return item.GetNegociatedValue();
        }
        finally
        {
            item.itemFeatures = originalFeatures;
            item.parentInventory = originalParent;
        }
    }

    private static bool HasCallback(Il2CppSystem.Action<GameItem> callback, string fragment)
    {
        if (callback == null) return false;
        foreach (var invocation in callback.GetInvocationList())
            if (invocation.Method?.Name?.Contains(fragment, StringComparison.Ordinal) == true) return true;
        return false;
    }

    private static T ReadMember<T>(object target, string name, T fallback)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = target.GetType();
        var value = type.GetProperty(name, flags)?.GetValue(target) ?? type.GetField(name, flags)?.GetValue(target);
        return value is T typed ? typed : fallback;
    }

    private static bool Same(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? a, Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? b)
        => a != null && b != null && a.Pointer == b.Pointer;
    private static string ClientKey(StoreClient c) => c.Pointer.ToInt64().ToString("X") + ":" + c.identifier;
    // GetUniqueID lazily initializes the game's own persistent ID if it is zero.
    // Native pointers are intentionally excluded: they change after a save reload.
    private static string ItemKey(GameItem i) => PlayerStore.Instance.runID + ":" + i.GetUniqueID() + ":" + i.identifier;
    private static string ShortError(Exception e) => (e.InnerException ?? e).Message.Split('\n')[0];
    private static TradeSnapshot Unavailable(TradeSnapshot s, string message) { s.Message = message; s.CanTrade = false; return s; }
    private static TradeActionResult Failed(TradeActionResult r, string message) { r.Success = false; r.Message = message; return r; }
    private static string Progress(TradeActionResult r) => r.SoldCount > 0 ? "已成交 " + r.SoldCount + " 件，实收 " + r.Total +
        (r.UnpaidTotal > 0 ? "，让利 " + r.UnpaidTotal : "") + "；其余尚未出售。" :
        r.MovedCount > 0 ? "已移动 " + r.MovedCount + " 件，尚未成交。" : "尚未成交。";
}
