using DeepSpaceBestOffer;
using System.Diagnostics;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
Candidate C(string key, long price, string? group = null, bool required = false, bool forbidden = false)
    => new() { Key = key, Group = group ?? key, Price = price, Required = required, Forbidden = forbidden };

void Oracle(Candidate[] items, long budget, bool variety, int maxItems, int maxPlans, long? receiptCap = null)
{
    var expected = new List<(long Price, int Count, string Keys)>();
    for (int mask = 0; mask < (1 << items.Length); mask++)
    {
        long total = 0;
        int count = 0;
        bool valid = true;
        var groups = new HashSet<string>(StringComparer.Ordinal);
        var keys = new List<string>();
        for (int i = 0; i < items.Length; i++)
        {
            bool included = (mask & (1 << i)) != 0;
            var item = items[i];
            if (item.Required && !included) { valid = false; break; }
            if (!included) continue;
            if (item.Forbidden || (variety && !groups.Add(item.Group)) || item.Price > budget - total)
            { valid = false; break; }
            total += item.Price;
            count++;
            keys.Add(item.Key);
        }
        if (valid && count > 0 && count <= maxItems)
            expected.Add((total, count, string.Join("|", keys.OrderBy(x => x, StringComparer.Ordinal))));
    }
    expected = receiptCap.HasValue
        ? expected.OrderByDescending(x => Math.Min(x.Price, receiptCap.Value))
            .ThenBy(x => Math.Max(0, x.Price - receiptCap.Value)).ThenBy(x => x.Count).ToList()
        : expected.OrderByDescending(x => x.Price).ThenBy(x => x.Count).ToList();
    var actual = OfferSolver.Solve(items, budget, variety, maxItems, maxPlans, receiptCap: receiptCap);
    Check(actual.Status == (expected.Count == 0 ? OfferSolveStatus.Infeasible : OfferSolveStatus.Optimal),
        $"Wrong status {actual.Status}, expected combinations {expected.Count}: {actual.Message}");
    Check(actual.Plans.Count == Math.Min(maxPlans, expected.Count), "Wrong number of plans");
    var validKeys = expected.Select(x => x.Keys).ToHashSet(StringComparer.Ordinal);
    var actualKeys = new HashSet<string>(StringComparer.Ordinal);
    for (int i = 0; i < actual.Plans.Count; i++)
    {
        var plan = actual.Plans[i];
        var key = string.Join("|", plan.Items.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal));
        Check(plan.TotalPrice == expected[i].Price && plan.ItemCount == expected[i].Count,
            $"Wrong rank {i}: actual {plan.TotalPrice}/{plan.ItemCount}, expected {expected[i].Price}/{expected[i].Count}");
        Check(validKeys.Contains(key), "Infeasible selection returned");
        Check(actualKeys.Add(key), "Duplicate plan returned");
        Check(plan.Items.Sum(x => x.Price) == plan.TotalPrice, "Plan total mismatch");
    }
}

Oracle(Array.Empty<Candidate>(), 0, false, int.MaxValue, 12);
Oracle(new[] { C("zero1", 0), C("zero2", 0), C("expensive", 1) }, 0, false, 2, 12);
Oracle(new[] { C("a", 8, "g"), C("b", 8, "g"), C("c", 4) }, 12, true, 2, 12);
Oracle(new[] { C("a", 8, "g"), C("b", 8, "g"), C("c", 4) }, 20, false, 3, 12);
Oracle(new[] { C("a", 3, "g", true), C("b", 2, "g", true) }, 8, true, 8, 12);
Oracle(new[] { C("a", 3, required: true, forbidden: true) }, 8, false, 8, 12);
Oracle(new[] { C("a", 9, required: true) }, 8, false, 8, 12);
Oracle(new[] { C("a", 1, required: true), C("b", 1, required: true) }, 8, false, 1, 12);
Oracle(new[] { C("a", 10), C("b", 6), C("c", 4) }, 10, false, 2, 12);
Oracle(new[] { C("a", long.MaxValue), C("b", long.MaxValue - 1), C("c", 1) }, long.MaxValue, false, 3, 12);
Oracle(new[] { C("a", long.MaxValue - 4, required: true), C("b", 4), C("c", 5) }, long.MaxValue, false, 3, 12);
Oracle(new[] { C("a", long.MaxValue, required: true), C("b", 1, required: true) }, long.MaxValue, false, 3, 12);
Oracle(new[] { C("a", 1, ""), C("b", 2, ""), C("c", 0, "") }, 10, true, 3, 12);
Oracle(new[] { C("zero1", 0), C("zero2", 0), C("big", 9) }, 0, false, 1, 1);
Oracle(new[] { C("zero1", 0), C("zero2", 0), C("big", 9) }, 0, false, 1, 12);
Oracle(new[] { C("zero1", 0), C("zero2", 0), C("a", 1), C("b", 2) }, 2, false, 2, 1);
Oracle(new[] { C("requiredZero", 0, required: true) }, 0, true, 1, 1);
Oracle(new[] { C("a", 3), C("b", 4) }, 0, false, 2, 12);

var rng = new Random(7192026);
for (int trial = 0; trial < 1200; trial++)
{
    int length = rng.Next(0, 12);
    var items = Enumerable.Range(0, length).Select(i => C("item" + i, rng.Next(0, 24),
        "group" + rng.Next(0, Math.Max(1, length / 2)), rng.Next(14) == 0, rng.Next(11) == 0)).ToArray();
    Oracle(items, rng.Next(0, 130), rng.Next(2) == 0, rng.Next(0, length + 2), rng.Next(1, 20));
}

// Actual receipts, not nominal price, determine liquidation rankings. Do not
// give away additional goods when an equally profitable smaller loss exists.
Oracle(new[] { C("exact", 100), C("slight", 101), C("large", 120), C("under", 99) }, 120, false, 3, 12, 100);
Oracle(new[] { C("must", 105, required: true), C("small", 3), C("large", 10) }, 120, false, 3, 12, 100);
Oracle(new[] { C("zero", 0), C("one", 1), C("two", 2) }, 3, false, 3, 12, 0);
Oracle(new[] { C("a", long.MaxValue - 4, required: true), C("b", 4) }, long.MaxValue, false, 3, 12, long.MaxValue - 3);
for (int trial = 0; trial < 1200; trial++)
{
    int length = rng.Next(0, 12);
    var items = Enumerable.Range(0, length).Select(i => C("clear" + i, rng.Next(0, 24),
        "group" + rng.Next(0, Math.Max(1, length / 2)), rng.Next(14) == 0, rng.Next(11) == 0)).ToArray();
    long receipt = rng.Next(0, 100);
    Oracle(items, receipt + rng.Next(0, 70), rng.Next(2) == 0, rng.Next(0, length + 2), rng.Next(1, 20), receipt);
}

var options = new TradeOptions { AllowOverBudget = true, OverBudgetValue = 10 };
Check(options.TryGetSelectionBudget(89, out var limit, out _) && limit == 97, "Percentage allowance must round down");
options.OverBudgetValue = 2.5m;
Check(options.TryGetSelectionBudget(87, out limit, out _) && limit == 89, "Fractional percentage allowance wrong");
options.OverBudgetMode = OverBudgetMode.Credits; options.OverBudgetValue = 25.9m;
Check(options.TryGetSelectionBudget(87, out limit, out _) && limit == 112, "Absolute allowance must round down");
var copiedOptions = options.Clone(); copiedOptions.OverBudgetValue = 1;
Check(options.OverBudgetValue == 25.9m, "Capture options must be independent");
options.OverBudgetValue = -1;
Check(!options.TryGetSelectionBudget(87, out _, out _), "Negative allowance rejected");
options.OverBudgetValue = decimal.MaxValue; options.OverBudgetMode = OverBudgetMode.Percent;
Check(!options.TryGetSelectionBudget(long.MaxValue, out _, out _), "Allowance overflow rejected");
options.AllowOverBudget = false;
Check(options.TryGetSelectionBudget(87, out limit, out _) && limit == 87, "Disabled allowance must use true budget");
Check(OfferSolver.Solve(new[] { C("x", 1) }, 2, false, receiptCap: -1).Status == OfferSolveStatus.InvalidInput,
    "Negative receipt cap rejected");

Check(OfferSolver.Solve(new[] { C("duplicate", 1), C("duplicate", 2) }, 2, false).Status == OfferSolveStatus.InvalidInput,
    "Duplicate key should be rejected");
Check(OfferSolver.Solve(new[] { C("negative", -1) }, 2, false).Status == OfferSolveStatus.InvalidInput,
    "Negative prices should be rejected");
Check(OfferSolver.Solve(Array.Empty<Candidate>(), -1, false).Status == OfferSolveStatus.InvalidInput,
    "Negative budget should be rejected");

var many = Enumerable.Range(0, 240).Select(i => C("large" + i, 10_000_000_000L + i)).ToArray();
var timer = Stopwatch.StartNew();
var large = OfferSolver.Solve(many, long.MaxValue, false);
Check(large.Status == OfferSolveStatus.Optimal && large.Plans.Count == 12, "Large-budget fast path failed");
Check(large.Plans[0].TotalPrice == many.Sum(x => x.Price) && large.Plans[0].ItemCount == 240, "Large optimum wrong");
Check(large.Plans[1].TotalPrice == many.Sum(x => x.Price) - many[0].Price, "Large second-best wrong");
Console.WriteLine($"240 long-price items / unbounded budget: {timer.ElapsedMilliseconds} ms");

var constrained = Enumerable.Range(0, 220).Select(i => C("small" + i, i % 35 + 1)).ToArray();
timer.Restart();
var bounded = OfferSolver.Solve(constrained, 700, false);
Check(bounded.Status == OfferSolveStatus.Optimal && bounded.Plans[0].TotalPrice == 700, "220-item exact DP failed");
Check(bounded.Plans[0].ItemCount == 21, "Minimum-count tie break failed in large DP");
Console.WriteLine($"220 items / budget 700: {timer.ElapsedMilliseconds} ms");

var limited = OfferSolver.Solve(new[] { C("a", 1), C("b", 2), C("c", 4), C("d", 8) }, 10, false,
    limits: new SolverLimits { MaxStates = 2 });
Check(limited.Status == OfferSolveStatus.ResourceLimit && limited.Plans.Count == 0, "Resource limit must not return unproven plans");
var productLimited = OfferSolver.Solve(many, long.MaxValue, false, limits: new SolverLimits { MaxCreatedPaths = 2 });
Check(productLimited.Status == OfferSolveStatus.ResourceLimit && productLimited.Plans.Count == 0, "Product resource limit should be explicit");

var tied = Enumerable.Range(0, 14).Select(i => C("tie" + i, i % 3)).ToArray();
string Signature(SolveResult result) => string.Join(";", result.Plans.Select(p =>
    string.Join(",", p.Items.Select(i => i.Key).OrderBy(x => x, StringComparer.Ordinal))));
string stable = Signature(OfferSolver.Solve(tied, 8, false, 7));
for (int i = 0; i < 20; i++) Check(Signature(OfferSolver.Solve(tied, 8, false, 7)) == stable, "Tie order changed");

var hardRng = new Random(32003);
var hard = Enumerable.Range(0, 250).Select(i => C("hard" + i, hardRng.NextInt64(1_000_000_000, 20_000_000_000))).ToArray();
timer.Restart();
var capped = OfferSolver.Solve(hard, hard.Sum(i => i.Price) / 2, false);
Check(capped.Status == OfferSolveStatus.ResourceLimit && capped.Plans.Count == 0, "Hard large-budget search must report its limit");
Check(timer.ElapsedMilliseconds < 8_000, "Default exact search exceeded bounded runtime");
Console.WriteLine($"250 hard long-price items / half-total budget: {capped.Status}, {timer.ElapsedMilliseconds} ms");

Console.WriteLine($"PASS: {checks} assertions, 2400 randomized exhaustive-oracle cases.");
