using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DeepSpaceBestOffer;

public sealed class InventoryRuleGroup
{
    public string Name { get; }
    public string RuleName { get; }
    public IReadOnlyList<TradeItem> Items { get; }
    public int Count => Items.Count;
    public int EligibleCount => Items.Count(i => i.Eligible);
    public long MinPrice => Items.Count == 0 ? 0 : Items.Min(i => i.Price);
    public long MaxPrice => Items.Count == 0 ? 0 : Items.Max(i => i.Price);
    public bool HasDifferences { get; }

    internal InventoryRuleGroup(string ruleName, IReadOnlyList<TradeItem> items, bool hasDifferences)
    { RuleName = ruleName; Name = items.FirstOrDefault()?.Name ?? ruleName; Items = items; HasDifferences = hasDifferences; }
}

public sealed partial class BestOfferMod
{
    List<InventoryRuleGroup> GroupInventoryRows(IEnumerable<TradeItem> inventory, int selectedFilter = 0, string parentKey = "")
    {
        // Filter whole groups, never their individual members. A forbidden-only
        // view must still permit changing ten forbidden copies down to three.
        return ItemRuleGroupLogic.GroupByDisplayName(inventory.Where(i => i.ParentKey == parentKey), i => i.Key, i => i.RuleName)
            .Select(g => new InventoryRuleGroup(g.Name, g.Items,
                g.Items.Select(ItemDifferenceSignature).Distinct(StringComparer.Ordinal).Skip(1).Any()))
            .Where(g => selectedFilter == 0 || g.Items.Any(i =>
                selectedFilter == 1 ? i.Eligible : selectedFilter == 2 ? (Rule(i) & 1) != 0 :
                selectedFilter == 3 && ((Rule(i) & 2) != 0 || i.RiskBlocked))).OrderBy(g => g.Name, StringComparer.Ordinal).ToList();
    }

    int GroupOwnRuleCount(InventoryRuleGroup group, int rule) => group.Items.Count(i => OwnRule(i.Key) == rule);
    int GroupAvailableRuleCount(InventoryRuleGroup group, int rule) =>
        group.Count - GroupOwnRuleCount(group, rule == 1 ? 2 : 1);
    int GroupEffectiveRuleCount(InventoryRuleGroup group, int rule) => group.Items.Count(i => (Rule(i) & rule) != 0);
    int GroupConflictCount(InventoryRuleGroup group) => group.Items.Count(i => Rule(i) == 3);

    // A quantity chosen inside one box belongs to that box, even when another
    // package contains medicine with the same display name.
    static string GroupStateKey(InventoryRuleGroup group) =>
        ItemRuleGroupLogic.GroupKey(group.Items.FirstOrDefault()?.ParentKey ?? "", group.RuleName,
            group.Items.FirstOrDefault()?.Key ?? "");

    bool GroupSelectsAll(InventoryRuleGroup group) => allGroupRules.ContainsKey(GroupStateKey(group));

    IEnumerable<InventoryRuleMember> CurrentRuleMembers =>
        snapshot?.RuleMembers.Values ?? Enumerable.Empty<InventoryRuleMember>();

    void AllGroupRuleChanged()
    { revision++; plans.Clear(); showConfirm = false; SaveRules(); uiDirty = true; }

    void ToggleGroupAllFromUI(InventoryRuleGroup group)
    {
        string key = GroupStateKey(group);
        int mode = CurrentGroupModeFromUI(group);
        if (GroupSelectsAll(group))
        {
            ItemRuleGroupLogic.DisableAll(rules, allGroupRules, key, CurrentRuleMembers);
            groupQuantities[key] = group.Count;
            status = L.F($"「{group.Name}」已取消全部，数量保留为当前最大值 {group.Count}，现在可编辑。");
        }
        else
        {
            if (mode == 0) allGroupRules[key] = 0;
            else ItemRuleGroupLogic.SetAllRule(rules, allGroupRules, key, CurrentRuleMembers, mode);
            groupQuantities.Remove(key);
            status = L.F($"「{group.Name}」已勾选全部，数量随库存变化；") +
                (mode == 0 ? L.T("请选择必卖或禁卖。") : L.F($"今后新增的同名商品也会{(mode == 1 ? L.T("必卖") : L.T("禁卖"))}。") + RuleNextStep());
        }
        if (mode != 0) groupModes[key] = mode;
        AllGroupRuleChanged();
    }

    void SetGroupRule(InventoryRuleGroup group, int rule, int count)
    {
        var change = ItemRuleGroupLogic.SetCount(rules, group.Items.Select(i => i.Key), rule, count);
        if (change.ChangedCount > 0)
        { revision++; plans.Clear(); showConfirm = false; SaveRules(); }
        if (rule == 0)
        { status = L.F($"「{group.Name}」的 {group.Count} 件单件规则已清除；容器内容规则保留。") + RuleNextStep(); return; }
        string label = rule == 1 ? L.T("必卖") : L.T("禁卖");
        string opposite = rule == 1 ? L.T("禁卖") : L.T("必卖");
        status = L.F($"「{group.Name}」{label} {change.AppliedCount} / {group.Count} 件") +
            (change.OppositeCount > 0 ? L.F($"；保留 {change.OppositeCount} 件{opposite}") : "") + L.T("。") + RuleNextStep();
    }

    string RuleNextStep() => snapshot?.CanTrade == true ? L.T("请重新计算。") : L.T("已保存，买家到来后应用。");

    static string ItemDifferenceSignature(TradeItem item)
    {
        string data = $"{item.Group}\u001f{item.Price}\u001f{item.Eligible}\u001f{item.Reason}\u001f{item.DescendantKeys.Count}\u001f{item.UnitCount}\u001f{item.ContainedUnits}";
        // Native feature identities have unique parent IDs/pointers, which must
        // not make equivalent copies appear different. Compare actual feature
        // values instead. This is read-only and safely falls back to snapshot
        // fields if a native object disappeared between capture and drawing.
        try
        {
            var native = item.Native;
            if (native == null) return data;
            var features = native.itemFeatures;
            var values = new List<string>();
            if (features != null)
                for (int n = 0; n < features.Count; n++)
                {
                    var f = features[n];
                    if (f != null && f.IsExposed() && !f.isDisabled)
                        values.Add($"{f.identifier}:{f.GetCurrentValueMod().ToString(CultureInfo.InvariantCulture)}");
                }
            values.Sort(StringComparer.Ordinal);
            return data + "\u001f" + native.unitCount + "\u001f" + string.Join("\u001e", values);
        }
        catch { return data; }
    }
}
