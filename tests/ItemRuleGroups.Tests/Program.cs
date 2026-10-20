using System.Text.Json;
using DeepSpaceBestOffer;

int assertions = 0;
void Check(bool condition, string message)
{ assertions++; if (!condition) throw new Exception(message); }
int Own(Dictionary<string, int> rules, string key) => ItemRuleGroupLogic.OwnRule(rules, key);
string[] Keys(Dictionary<string, int> rules, int rule) => rules.Where(p => p.Value == rule)
    .Select(p => p.Key).Order(StringComparer.Ordinal).ToArray();

var copies = Enumerable.Range(1, 10).Select(n => new Item($"water-{n:00}", "净水药片", n % 2 == 0 ? "a" : "b")).ToArray();
var inventory = copies.Reverse().Append(copies[0]).Append(new Item("other", "针剂", "a")).ToArray();
var groups = ItemRuleGroupLogic.GroupByDisplayName(inventory, i => i.Key, i => i.Name);
var water = groups.Single(g => g.Name == "净水药片");
Check(groups.Count == 2 && water.Items.Count == 10, "Group by displayed name across distinct item types; ignore duplicate inventory references.");
Check(water.Items.Select(i => i.Key).SequenceEqual(copies.Select(i => i.Key)), "Member ordering must be stable, independent of inventory capture order.");
Check(ItemRuleGroupLogic.GroupByDisplayName(inventory.Reverse(), i => i.Key, i => i.Name)
    .Select(g => g.Name).SequenceEqual(groups.Select(g => g.Name)), "Group ordering must be stable.");

var rules = new Dictionary<string, int> { ["another-save-id"] = 2 };
var changed = ItemRuleGroupLogic.SetCount(rules, water.Items.Select(i => i.Key), 2, water.Items.Count);
Check(changed.AppliedCount == 10 && changed.ChangedCount == 10, "Default group selection must select all ten copies.");
changed = ItemRuleGroupLogic.SetCount(rules, water.Items.Select(i => i.Key), 2, 3);
Check(changed.AppliedCount == 3 && changed.ChangedCount == 7, "Decreasing all ten forbidden copies to three must remove the other seven rules.");
Check(Keys(rules, 2).SequenceEqual(new[] { "another-save-id", "water-01", "water-02", "water-03" }), "Decrease chooses stable identities and leaves unrelated save IDs alone.");
Check(ItemRuleGroupLogic.SetCount(rules, water.Items.Select(i => i.Key).Reverse(), 2, 3).ChangedCount == 0,
    "Reapplying the same quantity is idempotent.");

rules["water-10"] = 1;
changed = ItemRuleGroupLogic.SetCount(rules, water.Items.Select(i => i.Key), 2, 10);
Check(changed.AppliedCount == 9 && changed.OppositeCount == 1 && Own(rules, "water-10") == 1,
    "Forbid all retains an individually required copy, reports it, and caps the quantity at nine.");
changed = ItemRuleGroupLogic.SetCount(rules, water.Items.Select(i => i.Key), 2, 0);
Check(changed.AppliedCount == 0 && Own(rules, "water-10") == 1 && Own(rules, "another-save-id") == 2,
    "Turning off forbidden clears only forbidden group members, not required copies or unrelated rules.");
changed = ItemRuleGroupLogic.SetCount(rules, water.Items.Select(i => i.Key), 1, 2);
Check(changed.AppliedCount == 2 && Own(rules, "water-10") == 1 && Own(rules, "water-01") == 1,
    "Increasing a quantity preserves an explicitly selected individual before adding stable unruled identities.");
changed = ItemRuleGroupLogic.SetCount(rules, water.Items.Select(i => i.Key), 2, 100);
Check(changed.RequestedCount == 10 && changed.AppliedCount == 8 && Keys(rules, 1).Length == 2,
    "Out-of-range counts are safely bounded and never overwrite the opposite rule.");
changed = ItemRuleGroupLogic.SetCount(rules, water.Items.Select(i => i.Key), 1, -5);
Check(changed.AppliedCount == 0 && Keys(rules, 2).Length == 9, "Negative quantity clears only the requested rule.");

rules["box"] = 1;
rules["box-child"] = 2;
Check(ItemRuleGroupLogic.EffectiveRule(rules, "box", new[] { "box-child" }) == 3,
    "A required container holding a forbidden item remains a detectable conflict.");
ItemRuleGroupLogic.SetCount(rules, new[] { "box" }, 1, 0);
Check(Own(rules, "box-child") == 2 && ItemRuleGroupLogic.EffectiveRule(rules, "box", new[] { "box-child" }) == 2,
    "Clearing a container's own rule must retain forbidden contents and their protection.");
ItemRuleGroupLogic.SetCount(rules, new[] { "box" }, 1, 1);
ItemRuleGroupLogic.SetCount(rules, new[] { "box" }, 0, 0);
Check(!rules.ContainsKey("box") && Own(rules, "box-child") == 2, "Explicit group clear touches member IDs only, never descendant IDs.");

// Persisted rules keep the original Dictionary<string,int> format, so older
// releases can read them and another save receives no display-name rule.
var restored = JsonSerializer.Deserialize<Dictionary<string, int>>(JsonSerializer.Serialize(rules))!;
Check(restored.OrderBy(p => p.Key).SequenceEqual(rules.OrderBy(p => p.Key)), "Legacy rules round-trip without a format migration.");
Check(ItemRuleGroupLogic.EffectiveRule(restored, "new-save-water", Array.Empty<string>()) == 0,
    "A new copy or another save must not accidentally inherit a same-name rule.");
bool rejected = false;
try { ItemRuleGroupLogic.SetCount(rules, new[] { "box" }, 3, 1); }
catch (ArgumentOutOfRangeException) { rejected = true; }
Check(rejected, "Conflicting rule value is never persisted as a member rule.");

// Persistent All is a display-name preference, separate from legacy fixed IDs.
// Exercise the actual resolver with stock changes, serialization and both rules.
string waterKey = ItemRuleGroupLogic.GroupKey("", "集水器");
var five = Enumerable.Range(1, 5).Select(n => new InventoryRuleMember($"collector-{n}", "集水器")).ToArray();
var sixth = new InventoryRuleMember("collector-6", "集水器");
var six = five.Append(sixth).ToArray();
foreach (int mode in new[] { 1, 2 })
{
    var fixedRules = new Dictionary<string, int>();
    var allRules = new Dictionary<string, int>();
    int Resolve(InventoryRuleMember item) => ItemRuleGroupLogic.OwnRule(fixedRules, allRules, item);
    ItemRuleGroupLogic.SetAllRule(fixedRules, allRules, waterKey, five, mode);
    Check(five.All(i => Resolve(i) == mode) && Resolve(sixth) == mode,
        "Five copies plus a newly acquired sixth all inherit the chosen required/forbidden rule.");
    var candidates = six.Select(i => new Candidate { Key=i.Key, Group="collector", Price=10,
        Required=Resolve(i) == 1, Forbidden=Resolve(i) == 2 })
        .Append(new Candidate { Key="ordinary", Group="ordinary", Price=1 }).ToArray();
    var quote = OfferSolver.Solve(candidates, 100, false, int.MaxValue);
    Check(quote.Plans.Count > 0 && (mode == 2
        ? quote.Plans.All(p => p.Items.All(i => !i.Key.StartsWith("collector-", StringComparison.Ordinal)))
        : quote.Plans.All(p => six.All(i => p.Items.Any(c => c.Key == i.Key)))),
        "The real solver excludes all six forbidden copies or includes all six required copies.");
    Check(Resolve(new InventoryRuleMember("other-name", "针剂")) == 0,
        "All never affects a different display name.");
    Check(Resolve(new InventoryRuleMember("other-package", "集水器", "package-2")) == 0,
        "A package's content group remains independent of the top-level group.");
    Check(ItemRuleGroupLogic.SelectedQuantity(true, 2, 5, 0, 0) == 5 &&
        ItemRuleGroupLogic.SelectedQuantity(true, 2, 6, 0, 0) == 6 &&
        ItemRuleGroupLogic.SelectedQuantity(true, 2, 3, 0, 0) == 3,
        "All displays the live maximum on stock increases and decreases, ignoring an old quantity.");
    allRules = JsonSerializer.Deserialize<Dictionary<string, int>>(JsonSerializer.Serialize(allRules))!;
    Check(Resolve(sixth) == mode, "Reloaded All rules still cover newly acquired identities.");
    Check(Resolve(new InventoryRuleMember("restocked-after-zero", "集水器")) == mode,
        "The rule survives zero stock and applies when the item is restocked.");
    fixedRules[sixth.Key] = mode == 1 ? 2 : 1;
    Check(Resolve(sixth) == mode, "An explicit All rule overrides older individual exceptions.");
    ItemRuleGroupLogic.DisableAll(fixedRules, allRules, waterKey, six);
    Check(!allRules.ContainsKey(waterKey) && six.All(i => Resolve(i) == mode),
        "Unchecking All freezes the current maximum, including the new sixth copy.");
    Check(Resolve(new InventoryRuleMember("collector-7", "集水器")) == 0,
        "Future copies stop inheriting the rule after All is unchecked.");
    Check(ItemRuleGroupLogic.SelectedQuantity(false, six.Length, six.Length, mode == 1 ? 6 : 0, mode == 2 ? 6 : 0) == 6,
        "The editable quantity after unchecking All keeps today's maximum, not the old value two.");
    ItemRuleGroupLogic.SetCount(fixedRules, six.Select(i => i.Key), mode, 2);
    Check(six.Count(i => Resolve(i) == mode) == 2, "After unchecking All the quantity can be reduced normally.");
    ItemRuleGroupLogic.SetAllRule(fixedRules, allRules, waterKey, six, mode == 1 ? 2 : 1);
    Check(six.All(i => Resolve(i) == (mode == 1 ? 2 : 1)),
        "Switching required/forbidden while All is checked applies to every copy.");
    ItemRuleGroupLogic.SetAllRule(fixedRules, allRules, waterKey, six, 0);
    Check(allRules.ContainsKey(waterKey) && six.All(i => Resolve(i) == 0),
        "Clearing the active rule retains the All selection without hidden individual rules.");
}
var packageMembers = new[] { new InventoryRuleMember("pill-1", "药片", "bottle-a"),
    new InventoryRuleMember("pill-2", "药片", "bottle-b") };
var packageAll = new Dictionary<string, int>();
var packageFixed = new Dictionary<string, int>();
ItemRuleGroupLogic.SetAllRule(packageFixed, packageAll, ItemRuleGroupLogic.GroupKey("bottle-a", "药片"), packageMembers, 2);
Check(ItemRuleGroupLogic.OwnRule(packageFixed, packageAll, packageMembers[0]) == 2 &&
    ItemRuleGroupLogic.OwnRule(packageFixed, packageAll, packageMembers[1]) == 0 &&
    ItemRuleGroupLogic.OwnRule(packageFixed, packageAll, new InventoryRuleMember("pill-new", "药片", "bottle-a")) == 2,
    "All inside one package covers new contents but never a different package.");
var legacyOnly = new Dictionary<string, int>();
ItemRuleGroupLogic.SetCount(legacyOnly, five.Select(i => i.Key), 2, 5);
Check(ItemRuleGroupLogic.OwnRule(legacyOnly, new Dictionary<string, int>(), sixth) == 0,
    "Updating the mod does not silently promote old maximum-count rules into persistent All rules.");
Check(ItemRuleGroupLogic.SelectedQuantity(true, 2, 0, 0, 0) == 0 &&
    ItemRuleGroupLogic.SelectedQuantity(false, 8, 3, 0, 0) == 3,
    "Quantity display handles empty stock and bounds an editable quantity after a decrease.");
var saveMembers = new[] { new InventoryRuleMember("run-a:1:collector", "集水器"),
    new InventoryRuleMember("run-b:1:collector", "集水器") };
var saveAll = new Dictionary<string, int>();
var saveFixed = new Dictionary<string, int>();
ItemRuleGroupLogic.SetAllRule(saveFixed, saveAll, ItemRuleGroupLogic.GroupKey("", "集水器", saveMembers[0].Key), saveMembers, 2);
Check(ItemRuleGroupLogic.OwnRule(saveFixed, saveAll, saveMembers[0]) == 2 &&
    ItemRuleGroupLogic.OwnRule(saveFixed, saveAll, new InventoryRuleMember("run-a:9:collector", "集水器")) == 2 &&
    ItemRuleGroupLogic.OwnRule(saveFixed, saveAll, saveMembers[1]) == 0,
    "All survives reload/new identities in one run without leaking into another save.");

// Exhaust every small inventory state/count to verify both directions of the
// invariant: an opposite rule is unchanged; target count is exactly bounded.
for (int encoded = 0; encoded < 81; encoded++)
for (int target = 1; target <= 2; target++)
for (int count = 0; count <= 4; count++)
{
    var before = new Dictionary<string, int>();
    int value = encoded;
    var memberKeys = new[] { "a", "b", "c", "d" };
    foreach (string key in memberKeys)
    { int r = value % 3; value /= 3; if (r != 0) before[key] = r; }
    var after = new Dictionary<string, int>(before);
    var result = ItemRuleGroupLogic.SetCount(after, memberKeys.Reverse(), target, count);
    int opposite = target == 1 ? 2 : 1;
    Check(memberKeys.All(k => Own(before, k) != opposite || Own(after, k) == opposite), "Exhaustive opposite rule preservation.");
    Check(memberKeys.Count(k => Own(after, k) == target) == Math.Min(count, 4 - memberKeys.Count(k => Own(before, k) == opposite)),
        "Exhaustive target quantity replacement.");
    var second = new Dictionary<string, int>(before);
    ItemRuleGroupLogic.SetCount(second, memberKeys, target, count);
    Check(second.OrderBy(p => p.Key).SequenceEqual(after.OrderBy(p => p.Key)), "Exhaustive input-order independence.");
}
Console.WriteLine($"PASS: {assertions} grouping, quantity, rule-preservation, container, and save-compatibility assertions.");

sealed record Item(string Key, string Name, string Type);
