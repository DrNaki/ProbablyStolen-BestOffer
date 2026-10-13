using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DeepSpaceBestOffer;

public sealed class InventoryRuleGroup
{
    public string Name { get; }
    public IReadOnlyList<TradeItem> Items { get; }
    public int Count => Items.Count;
    public int EligibleCount => Items.Count(i => i.Eligible);
    public long MinPrice => Items.Count == 0 ? 0 : Items.Min(i => i.Price);
    public long MaxPrice => Items.Count == 0 ? 0 : Items.Max(i => i.Price);
    public bool HasDifferences { get; }

    internal InventoryRuleGroup(string name, IReadOnlyList<TradeItem> items, bool hasDifferences)
    { Name = name; Items = items; HasDifferences = hasDifferences; }
}

public sealed partial class BestOfferMod
{
    List<InventoryRuleGroup> GroupInventoryRows(IEnumerable<TradeItem> inventory, int selectedFilter = 0)
    {
        // Filter whole groups, never their individual members. A forbidden-only
        // view must still permit changing ten forbidden copies down to three.
        return ItemRuleGroupLogic.GroupByDisplayName(inventory, i => i.Key, i => i.Name)
            .Select(g => new InventoryRuleGroup(g.Name, g.Items,
                g.Items.Select(ItemDifferenceSignature).Distinct(StringComparer.Ordinal).Skip(1).Any()))
            .Where(g => selectedFilter == 0 || g.Items.Any(i =>
                selectedFilter == 1 ? i.Eligible : selectedFilter == 2 ? (Rule(i) & 1) != 0 :
                selectedFilter == 3 && (Rule(i) & 2) != 0)).ToList();
    }

    int GroupOwnRuleCount(InventoryRuleGroup group, int rule) => group.Items.Count(i => OwnRule(i.Key) == rule);
    int GroupAvailableRuleCount(InventoryRuleGroup group, int rule) =>
        group.Count - GroupOwnRuleCount(group, rule == 1 ? 2 : 1);
    int GroupEffectiveRuleCount(InventoryRuleGroup group, int rule) => group.Items.Count(i => (Rule(i) & rule) != 0);
    int GroupConflictCount(InventoryRuleGroup group) => group.Items.Count(i => Rule(i) == 3);

    void SetGroupRule(InventoryRuleGroup group, int rule, int count)
    {
        var change = ItemRuleGroupLogic.SetCount(rules, group.Items.Select(i => i.Key), rule, count);
        if (change.ChangedCount > 0)
        { revision++; plans.Clear(); showConfirm = false; SaveRules(); }
        if (rule == 0)
        { status = $"「{group.Name}」的 {group.Count} 件单件规则已清除；容器内容规则保留。请重新计算。"; return; }
        string label = rule == 1 ? "必卖" : "禁卖";
        string opposite = rule == 1 ? "禁卖" : "必卖";
        status = $"「{group.Name}」{label} {change.AppliedCount} / {group.Count} 件" +
            (change.OppositeCount > 0 ? $"；保留 {change.OppositeCount} 件{opposite}" : "") + "。请重新计算。";
    }

    static string ItemDifferenceSignature(TradeItem item)
    {
        string data = $"{item.Group}\u001f{item.Price}\u001f{item.Eligible}\u001f{item.Reason}\u001f{item.DescendantKeys.Count}";
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
                    if (f != null) values.Add($"{f.identifier}:{f.GetCurrentValueMod().ToString(CultureInfo.InvariantCulture)}:{f.isDisabled}:{f.IsExposed()}");
                }
            values.Sort(StringComparer.Ordinal);
            return data + "\u001f" + native.unitCount + "\u001f" + string.Join("\u001e", values);
        }
        catch { return data; }
    }
}
