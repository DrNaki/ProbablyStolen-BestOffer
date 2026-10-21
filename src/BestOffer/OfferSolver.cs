using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DeepSpaceBestOffer;

public sealed class Candidate
{
    public string Key { get; set; } = "";
    public string Group { get; set; } = "";
    public long Price { get; set; }
    public bool Required { get; set; }
    public bool Forbidden { get; set; }
    public bool Preferred { get; set; }
}

public enum OfferSolveStatus { Optimal, Infeasible, ResourceLimit, InvalidInput }

public sealed class OfferPlan
{
    public IReadOnlyList<Candidate> Items { get; }
    public long TotalPrice { get; }
    public int ItemCount => Items.Count;
    public long PreferredPrice => Items.Where(i => i.Preferred).Sum(i => i.Price);

    internal OfferPlan(IEnumerable<Candidate> items, long totalPrice)
    {
        Items = Array.AsReadOnly(items.ToArray());
        TotalPrice = totalPrice;
    }
}

public sealed class SolveResult
{
    public OfferSolveStatus Status { get; }
    public string Message { get; }
    public IReadOnlyList<OfferPlan> Plans { get; }
    public bool IsOptimal => Status == OfferSolveStatus.Optimal;

    internal SolveResult(OfferSolveStatus status, string message, IEnumerable<OfferPlan>? plans = null)
    {
        Status = status;
        Message = message;
        Plans = Array.AsReadOnly(plans?.ToArray() ?? Array.Empty<OfferPlan>());
    }
}

/// <summary>Hard limits stop difficult exact instances instead of returning an unproven optimum.</summary>
public sealed class SolverLimits
{
    public int MaxStates { get; set; } = 200_000;
    public int MaxStoredPaths { get; set; } = 600_000;
    public int MaxCreatedPaths { get; set; } = 2_000_000;
    public long MaxTransitions { get; set; } = 20_000_000;
    public int MaxMilliseconds { get; set; } = 4_000;
}

/// <summary>
/// Exact top-k grouped 0/1 knapsack. Prices and budget must be nonnegative.
/// With requireVariety, Group uses ordinal equality (including the empty string).
/// With no receipt cap, orders by total price descending then item count ascending.
/// With a receipt cap, orders by actual receipt descending, unpaid value ascending,
/// then item count ascending. Budget always limits nominal total value.
/// Equal-price, equal-count plans have a deterministic, otherwise unspecified order.
/// Empty selections are never sale plans. Nonempty zero-price selections are valid.
/// Required items are included in every plan.
/// </summary>
public static class OfferSolver
{
    private sealed class PathNode
    {
        public readonly PathNode? Previous;
        public readonly Candidate? Item;
        public readonly int Count;
        public readonly long PreferredPrice;
        public PathNode(PathNode? previous, Candidate? item, int count, long preferredPrice = 0)
        { Previous = previous; Item = item; Count = count; PreferredPrice = preferredPrice; }
    }

    private sealed class ProductNode
    {
        public ProductNode? Previous;
        public int ChangedGroup;
        public int Option;
        public long Total;
        public int Count;
    }

    private sealed class ResourceBudget
    {
        public readonly SolverLimits Limits;
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public long Transitions;
        public int CreatedPaths = 1;
        public ResourceBudget(SolverLimits limits) { Limits = limits; }
        public bool TryTransition()
        {
            if (++Transitions > Limits.MaxTransitions) return false;
            return (Transitions & 1023) != 0 || Clock.ElapsedMilliseconds <= Limits.MaxMilliseconds;
        }
        public bool TryCreate() => ++CreatedPaths <= Limits.MaxCreatedPaths;
        public bool InTime => Clock.ElapsedMilliseconds <= Limits.MaxMilliseconds;
    }

    public static SolveResult Solve(IReadOnlyList<Candidate> candidates, long budget,
        bool requireVariety, int maxItems = int.MaxValue, int maxPlans = 12,
        SolverLimits? limits = null, long? receiptCap = null, bool prioritizePreferred = false)
    {
        if (candidates is null) return Invalid(L.T("物品列表不能为空。"));
        if (budget < 0 || receiptCap < 0 || maxItems < 0 || maxPlans < 1 || maxPlans > 64)
            return Invalid(L.T("预算及最大件数必须非负，方案数量必须为 1 至 64。"));
        limits ??= new SolverLimits();
        if (limits.MaxStates < 1 || limits.MaxStoredPaths < 1 || limits.MaxCreatedPaths < 1
            || limits.MaxTransitions < 1 || limits.MaxMilliseconds < 1)
            return Invalid(L.T("搜索资源上限必须为正数。"));
        var work = new ResourceBudget(limits);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var required = new List<Candidate>();
        var requiredGroups = new HashSet<string>(StringComparer.Ordinal);
        long requiredTotal = 0;

        foreach (var item in candidates)
        {
            if (!work.TryTransition()) return Limited();
            if (item is null || string.IsNullOrEmpty(item.Key) || item.Group is null || item.Price < 0)
                return Invalid(L.T("物品需要唯一非空 Key、非空值 Group 和非负价格。"));
            if (!keys.Add(item.Key)) return Invalid(L.T("物品 Key 重复：") + item.Key);
            if (!item.Required) continue;
            if (item.Forbidden) return Infeasible(L.T("同一件物品同时设置为必卖和禁卖：") + item.Key);
            if (requireVariety && !requiredGroups.Add(item.Group))
                return Infeasible(L.T("必卖物品中有同组物品，无法满足种类限制。"));
            if (item.Price > budget - requiredTotal)
                return Infeasible(L.T("必卖物品总价超过预算。"));
            requiredTotal += item.Price;
            required.Add(item);
        }
        if (required.Count > maxItems) return Infeasible(L.T("必卖物品数量超过最大件数。"));
        long capacity = budget - requiredTotal;
        int slots = maxItems - required.Count;
        var groups = new List<List<Candidate>>();
        var groupIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in candidates)
        {
            if (!work.TryTransition()) return Limited();
            if (item.Required || item.Forbidden || item.Price > capacity || slots == 0
                || (requireVariety && requiredGroups.Contains(item.Group))) continue;
            if (!requireVariety)
                groups.Add(new List<Candidate> { item });
            else
            {
                if (!groupIndexes.TryGetValue(item.Group, out int index))
                {
                    index = groups.Count;
                    groupIndexes.Add(item.Group, index);
                    groups.Add(new List<Candidate>());
                }
                groups[index].Add(item);
            }
        }

        // A required preferred total is fixed for every solution. Once it
        // reaches the receipt cap (or no optional preferred choices remain),
        // use the ordinary fast paths instead of constructing redundant states.
        if (prioritizePreferred && required.Where(i => i.Preferred).Sum(i => i.Price) < (receiptCap ?? budget) &&
            groups.Any(group => group.Any(i => i.Preferred && i.Price > 0)))
            return SolvePreferred(groups, required, requiredTotal, capacity, slots, maxPlans,
                receiptCap ?? budget, work);

        // If the maximum choice from every independent group fits, all combinations fit.
        // Enumerate only the k best Cartesian-product points instead of all reachable sums.
        long maximumTotal = 0;
        bool allFit = slots >= groups.Count;
        if (allFit)
        {
            foreach (var group in groups)
            {
                long price = group.Max(x => x.Price);
                if (price > capacity - maximumTotal) { allFit = false; break; }
                maximumTotal += price;
            }
        }
        if (!work.InTime) return Limited();
        // The descending Cartesian frontier is valid only while receipts grow
        // with nominal price. Above a cap, LOWER nominal value is preferable.
        if (allFit && (!receiptCap.HasValue || requiredTotal + maximumTotal <= receiptCap.Value))
            return SolveProduct(groups, required, requiredTotal, maxPlans, work);

        var root = new PathNode(null, null, 0);
        var current = new Dictionary<long, List<PathNode>> { [0] = new() { root } };
        foreach (var group in groups)
        {
            if (!work.InTime) return Limited();
            // Clone buckets, since zero-price options can update an existing sum.
            var next = new Dictionary<long, List<PathNode>>(current.Count);
            int storedPaths = 0;
            foreach (var entry in current)
            {
                next.Add(entry.Key, new List<PathNode>(entry.Value));
                storedPaths += entry.Value.Count;
            }
            foreach (var item in group)
            foreach (var entry in current)
            {
                if (!work.TryTransition()) return Limited();
                if (item.Price > capacity - entry.Key) continue;
                long total = entry.Key + item.Price;
                foreach (var previous in entry.Value)
                {
                    if (!work.TryTransition()) return Limited();
                    if (previous.Count >= slots) continue;
                    int count = previous.Count + 1;
                    if (!next.TryGetValue(total, out var bucket))
                    {
                        if (next.Count >= limits.MaxStates) return Limited();
                        bucket = new List<PathNode>(maxPlans);
                        next.Add(total, bucket);
                    }
                    // For a fixed prefix sum, fewer items never restrict a future suffix.
                    // Keeping k smallest counts therefore preserves the global k best plans,
                    // even with maxItems. Any discarded prefix has k dominating substitutes.
                    // The empty prefix must remain available for later groups, but it is
                    // not a sale. Reserve one additional zero-sum path so it cannot evict
                    // the kth actual zero-price sale (especially when maxPlans is one).
                    int keep = maxPlans + (total == 0 && required.Count == 0 ? 1 : 0);
                    if (bucket.Count == keep && bucket[^1].Count <= count) continue;
                    if (!work.TryCreate()) return Limited();
                    if (bucket.Count < keep && ++storedPaths > limits.MaxStoredPaths) return Limited();
                    int position = bucket.Count;
                    while (position > 0 && bucket[position - 1].Count > count) position--;
                    bucket.Insert(position, new PathNode(previous, item, count));
                    if (bucket.Count > keep) bucket.RemoveAt(bucket.Count - 1);
                }
            }
            current = next;
        }
        if (!work.InTime) return Limited();
        var plans = new List<OfferPlan>(maxPlans);
        var ranked = receiptCap.HasValue
            ? current.OrderByDescending(x => Math.Min(requiredTotal + x.Key, receiptCap.Value))
                .ThenBy(x => Math.Max(0, requiredTotal + x.Key - receiptCap.Value))
            : current.OrderByDescending(x => x.Key);
        foreach (var entry in ranked)
        {
            foreach (var node in entry.Value)
            {
                if (node.Count + required.Count == 0) continue;
                var selected = new List<Candidate>(required);
                for (var cursor = node; cursor.Item is not null; cursor = cursor.Previous!)
                    selected.Add(cursor.Item);
                plans.Add(new OfferPlan(selected, requiredTotal + entry.Key));
                if (plans.Count == maxPlans) return Success(plans);
            }
        }
        return Success(plans);
    }

    private static SolveResult SolveProduct(List<List<Candidate>> groups, List<Candidate> required,
        long requiredTotal, int maxPlans, ResourceBudget work)
    {
        var options = new List<Candidate?>[groups.Count];
        long bestTotal = requiredTotal;
        int bestCount = required.Count;
        for (int i = 0; i < groups.Count; i++)
        {
            options[i] = groups[i].Cast<Candidate?>().Append(null)
                .OrderByDescending(x => x?.Price ?? 0).ThenBy(x => x is null ? 0 : 1).ToList();
            bestTotal += options[i][0]?.Price ?? 0;
            if (options[i][0] is not null) bestCount++;
        }
        var queue = new PriorityQueue<ProductNode, (long Price, int Count, long Order)>();
        long order = 0;
        var root = new ProductNode { ChangedGroup = -1, Total = bestTotal, Count = bestCount };
        queue.Enqueue(root, (-root.Total, root.Count, order++));
        var plans = new List<OfferPlan>(maxPlans);
        while (queue.TryDequeue(out var node, out _))
        {
            if (!work.InTime) return Limited();
            var choices = new int[groups.Count];
            var seen = new bool[groups.Count];
            for (var cursor = node; cursor.ChangedGroup >= 0; cursor = cursor.Previous!)
                if (!seen[cursor.ChangedGroup])
                {
                    choices[cursor.ChangedGroup] = cursor.Option;
                    seen[cursor.ChangedGroup] = true;
                }
            var selected = new List<Candidate>(required);
            for (int i = 0; i < groups.Count; i++)
                if (options[i][choices[i]] is Candidate item) selected.Add(item);
            if (selected.Count > 0) plans.Add(new OfferPlan(selected, node.Total));
            if (plans.Count == maxPlans) return Success(plans);
            // Only modify coordinates at/after the previous coordinate. Every product
            // point then has one canonical path; no exponential duplicate set is needed.
            for (int i = Math.Max(0, node.ChangedGroup); i < groups.Count; i++)
            {
                if (!work.TryTransition()) return Limited();
                int oldOption = i == node.ChangedGroup ? node.Option : 0;
                int nextOption = oldOption + 1;
                if (nextOption >= options[i].Count) continue;
                if (!work.TryCreate() || queue.Count >= work.Limits.MaxStoredPaths) return Limited();
                Candidate? oldItem = options[i][oldOption], newItem = options[i][nextOption];
                var child = new ProductNode
                {
                    Previous = node, ChangedGroup = i, Option = nextOption,
                    Total = node.Total - (oldItem?.Price ?? 0) + (newItem?.Price ?? 0),
                    Count = node.Count - (oldItem is null ? 0 : 1) + (newItem is null ? 0 : 1)
                };
                queue.Enqueue(child, (-child.Total, child.Count, order++));
            }
        }
        return Success(plans);
    }

    // Keeping only the highest preference at a price can discard a smaller
    // prefix needed to satisfy a later max-items constraint. Count is therefore
    // part of the state; the k best preferences at each (price, count) dominate
    // every discarded path for every possible future suffix.
    private static SolveResult SolvePreferred(List<List<Candidate>> groups, List<Candidate> required,
        long requiredTotal, long capacity, int slots, int maxPlans, long receiptCap, ResourceBudget work)
    {
        long requiredPreferred = required.Where(i => i.Preferred).Sum(i => i.Price);
        var root = new PathNode(null, null, 0, requiredPreferred);
        var current = new Dictionary<(long Price, int Count), List<PathNode>> { [(0, 0)] = new() { root } };
        foreach (var group in groups)
        {
            if (!work.InTime) return Limited();
            var next = current.ToDictionary(e => e.Key, e => new List<PathNode>(e.Value));
            int storedPaths = next.Values.Sum(v => v.Count);
            foreach (var item in group)
            foreach (var entry in current)
            {
                if (!work.TryTransition()) return Limited();
                if (entry.Key.Count >= slots || item.Price > capacity - entry.Key.Price) continue;
                var state = (Price: entry.Key.Price + item.Price, Count: entry.Key.Count + 1);
                foreach (var previous in entry.Value)
                {
                    if (!work.TryTransition()) return Limited();
                    long preferred = previous.PreferredPrice + (item.Preferred ? item.Price : 0);
                    long score = Math.Min(preferred, receiptCap);
                    if (!next.TryGetValue(state, out var bucket))
                    {
                        if (next.Count >= work.Limits.MaxStates) return Limited();
                        next[state] = bucket = new List<PathNode>();
                    }
                    if (bucket.Count >= maxPlans && Math.Min(bucket[^1].PreferredPrice, receiptCap) >= score) continue;
                    if (!work.TryCreate()) return Limited();
                    if (bucket.Count < maxPlans && ++storedPaths > work.Limits.MaxStoredPaths) return Limited();
                    int position = bucket.Count;
                    while (position > 0 && Math.Min(bucket[position - 1].PreferredPrice, receiptCap) < score) position--;
                    bucket.Insert(position, new PathNode(previous, item, state.Count, preferred));
                    if (bucket.Count > maxPlans) bucket.RemoveAt(bucket.Count - 1);
                }
            }
            current = next;
        }
        if (!work.InTime) return Limited();
        var ranked = current.SelectMany(e => e.Value.Select(path => new { e.Key.Price, e.Key.Count, Path = path }))
            .Where(e => e.Count + required.Count > 0)
            .OrderByDescending(e => Math.Min(e.Path.PreferredPrice, receiptCap))
            .ThenByDescending(e => Math.Min(requiredTotal + e.Price, receiptCap))
            .ThenBy(e => Math.Max(0, requiredTotal + e.Price - receiptCap))
            .ThenBy(e => e.Count + required.Count)
            .Take(maxPlans);
        var plans = new List<OfferPlan>();
        foreach (var entry in ranked)
        {
            var items = new List<Candidate>(required);
            for (var cursor = entry.Path; cursor.Item != null; cursor = cursor.Previous!) items.Add(cursor.Item);
            plans.Add(new OfferPlan(items, requiredTotal + entry.Price));
        }
        if (!work.InTime) return Limited();
        var result = Success(plans);
        return result.IsOptimal ? new SolveResult(OfferSolveStatus.Optimal,
            L.T("已优先选择当天加价商品，再求最高实收及可用备选方案。"), result.Plans) : result;
    }

    private static SolveResult Success(IEnumerable<OfferPlan> plans)
    {
        var array = plans.ToArray();
        return array.Length == 0 ? Infeasible(L.T("预算和筛选条件下没有可出售的非空物品组合。"))
            : new(OfferSolveStatus.Optimal, L.T("已精确求得最高报价及可用备选方案。"), array);
    }
    private static SolveResult Invalid(string message) => new(OfferSolveStatus.InvalidInput, message);
    private static SolveResult Infeasible(string message) => new(OfferSolveStatus.Infeasible, message);
    private static SolveResult Limited() => new(OfferSolveStatus.ResourceLimit,
        L.T("精确搜索达到时间或内存资源上限，未返回未经证实的最优方案。请减少候选物品、限制种类或件数后重试。"));
}
