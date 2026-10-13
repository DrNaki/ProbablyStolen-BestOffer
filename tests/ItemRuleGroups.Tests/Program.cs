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
