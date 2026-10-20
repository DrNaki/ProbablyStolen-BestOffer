using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceBestOffer;

/// <summary>A display-name group; identities remain individual save-file keys.</summary>
public sealed record NamedItemGroup<T>(string Name, IReadOnlyList<T> Items);

public sealed record GroupRuleChange(int RequestedCount, int AppliedCount, int ChangedCount, int OppositeCount);

public sealed record InventoryRuleMember(string Key, string Name, string ParentKey = "");

/// <summary>
/// Unity-independent inventory grouping and rule updates. A count replaces the
/// count for one rule, preserving the opposite rule and all container contents.
/// </summary>
public static class ItemRuleGroupLogic
{
    public static string GroupKey(string parentKey, string name, string memberKey = "")
    {
        // Native item keys start with the persistent run ID. Keep preferences
        // separate between saves, including an empty/restocked top-level group.
        int separator = memberKey.IndexOf(':');
        string scope = separator > 0 ? memberKey[..separator] + "\u001e" : "";
        return scope + parentKey + "\u001f" + name;
    }

    // The presence of a group entry means that "All" is checked. Zero keeps
    // that selection without a required/forbidden rule. Never infer All from a
    // count: an older fixed rule covering today's stock must stay fixed.
    public static int OwnRule(IReadOnlyDictionary<string, int> rules,
        IReadOnlyDictionary<string, int> allGroups, InventoryRuleMember member) =>
        allGroups.TryGetValue(GroupKey(member.ParentKey, member.Name, member.Key), out int rule) && rule is 1 or 2
            ? rule : OwnRule(rules, member.Key);

    public static int SelectedQuantity(bool all, int? selected, int total, int required, int forbidden) =>
        total <= 0 ? 0 : all ? total : selected.HasValue ? Math.Clamp(selected.Value, 1, total) :
        forbidden > 0 && required == 0 ? forbidden : required > 0 && forbidden == 0 ? required : total;

    public static void SetAllRule(IDictionary<string, int> rules, IDictionary<string, int> allGroups,
        string groupKey, IEnumerable<InventoryRuleMember> members, int rule)
    {
        if (rule is not (0 or 1 or 2)) throw new ArgumentOutOfRangeException(nameof(rule));
        allGroups[groupKey] = rule;
        ReplaceMembers(rules, members.Where(m => GroupKey(m.ParentKey, m.Name, m.Key) == groupKey), rule);
    }

    public static void DisableAll(IDictionary<string, int> rules, IDictionary<string, int> allGroups,
        string groupKey, IEnumerable<InventoryRuleMember> members)
    {
        // Freeze the CURRENT maximum, including copies acquired since All was
        // selected. Removing All never restores an earlier quantity or rule.
        if (allGroups.TryGetValue(groupKey, out int rule) && rule is 1 or 2)
            ReplaceMembers(rules, members.Where(m => GroupKey(m.ParentKey, m.Name, m.Key) == groupKey), rule);
        allGroups.Remove(groupKey);
    }

    static void ReplaceMembers(IDictionary<string, int> rules, IEnumerable<InventoryRuleMember> members, int rule)
    {
        foreach (var member in members)
            if (rule == 0) rules.Remove(member.Key); else rules[member.Key] = rule;
    }

    public static IReadOnlyList<NamedItemGroup<T>> GroupByDisplayName<T>(IEnumerable<T> items,
        Func<T, string> key, Func<T, string> name)
    {
        return items.GroupBy(key, StringComparer.Ordinal).Select(g => g.First())
            .GroupBy(name, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new NamedItemGroup<T>(g.Key, g.OrderBy(key, StringComparer.Ordinal).ToArray())).ToArray();
    }

    public static int OwnRule(IReadOnlyDictionary<string, int> rules, string key) =>
        rules.TryGetValue(key, out int rule) ? rule : 0;

    public static int EffectiveRule(IReadOnlyDictionary<string, int> rules, string key,
        IEnumerable<string> descendantKeys)
    {
        int rule = OwnRule(rules, key);
        foreach (string child in descendantKeys) rule |= OwnRule(rules, child);
        return rule;
    }

    public static GroupRuleChange SetCount(IDictionary<string, int> rules, IEnumerable<string> memberKeys,
        int rule, int count)
    {
        if (rule is not (0 or 1 or 2)) throw new ArgumentOutOfRangeException(nameof(rule));
        string[] keys = memberKeys.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        if (rule == 0)
        {
            int removed = 0;
            foreach (string key in keys) if (rules.Remove(key)) removed++;
            return new GroupRuleChange(0, 0, removed, 0);
        }

        int opposite = rule == 1 ? 2 : 1;
        int RuleFor(string key) => rules.TryGetValue(key, out int own) ? own : 0;
        int oppositeCount = keys.Count(k => RuleFor(k) == opposite);
        int requested = Math.Clamp(count, 0, keys.Length);
        int applied = Math.Min(requested, keys.Length - oppositeCount);
        // Keep explicitly chosen individuals where possible, then fill from
        // unruled identities. Both sets use stable key ordering, so inventory
        // movement or different capture order never changes the choice.
        var chosen = keys.Where(k => RuleFor(k) != opposite)
            .OrderBy(k => RuleFor(k) == rule ? 0 : 1).ThenBy(k => k, StringComparer.Ordinal)
            .Take(applied).ToHashSet(StringComparer.Ordinal);
        int changed = 0;
        foreach (string key in keys)
        {
            int before = RuleFor(key);
            if (before == opposite) continue;
            if (chosen.Contains(key))
            {
                if (before != rule) { rules[key] = rule; changed++; }
            }
            else if (before == rule)
            {
                rules.Remove(key);
                changed++;
            }
        }
        return new GroupRuleChange(requested, applied, changed, oppositeCount);
    }
}
