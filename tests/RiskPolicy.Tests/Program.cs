using System.Text.Json;
using DeepSpaceBestOffer;

int assertions = 0;
void Check(bool condition, string message)
{ assertions++; if (!condition) throw new Exception(message); }

// All combinations of user protections, public risk labels and explicit demand.
for (int mask = 0; mask < 128; mask++)
{
    var options = new TradeOptions
    {
        BlockContraband = (mask & 1) != 0,
        BlockStolen = (mask & 2) != 0,
        AllowContrabandDemandExceptions = (mask & 4) != 0,
        AllowStolenDemandExceptions = (mask & 8) != 0
    };
    bool contraband = (mask & 16) != 0, stolen = (mask & 32) != 0, demand = (mask & 64) != 0;
    var result = RiskPolicy.GetBlockedCategories(options, contraband, stolen, demand);
    Check(result.HasFlag(RiskCategory.Contraband) ==
        (options.BlockContraband && contraband && !(options.AllowContrabandDemandExceptions && demand)),
        "Contraband protection or its demand exception was applied incorrectly.");
    Check(result.HasFlag(RiskCategory.Stolen) ==
        (options.BlockStolen && stolen && !(options.AllowStolenDemandExceptions && demand)),
        "Stolen protection or its demand exception was applied incorrectly.");
}

var protectedOptions = new TradeOptions
{
    BlockContraband = true, BlockStolen = true,
    AllowContrabandDemandExceptions = true, AllowStolenDemandExceptions = true
};
var asymmetricOptions = new TradeOptions
{
    BlockContraband = true, BlockStolen = true,
    AllowContrabandDemandExceptions = true, AllowStolenDemandExceptions = false
};
Check(RiskPolicy.GetBlockedCategories(asymmetricOptions, true, true, true) == RiskCategory.Stolen,
    "A contraband exception must never release the stolen category of a dual-risk item.");
Check(RiskPolicy.GetBlockedCategories(asymmetricOptions, false, true, true) == RiskCategory.Stolen,
    "Stolen goods must remain strictly blocked when only contraband exceptions are enabled.");
Check(RiskPolicy.GetBlockedCategories(asymmetricOptions, true, false, true) == RiskCategory.None,
    "A requested contraband-only item may use its own category exception.");
asymmetricOptions.AllowContrabandDemandExceptions = false;
asymmetricOptions.AllowStolenDemandExceptions = true;
Check(RiskPolicy.GetBlockedCategories(asymmetricOptions, true, true, true) == RiskCategory.Contraband,
    "A stolen exception must never release the contraband category of a dual-risk item.");
Check(RiskPolicy.GetBlockedCategories(asymmetricOptions, false, true, true) == RiskCategory.None,
    "A requested stolen-only item may use its own category exception.");
Check(RiskPolicy.GetBlockedCategories(asymmetricOptions, true, true, false) == (RiskCategory.Contraband | RiskCategory.Stolen),
    "Neither category receives an exception when the specific item is not requested.");
bool HasTag(string tag) => tag is "NARCOTIC" or "SUBSTANCE" or "ALL_TYPE" or "ALL_TYPE_EXCEPT_CONTRABAND";
Check(!RiskPolicy.MatchesExplicitDemand("dream_dust", null, new[] { "ALL_TYPE", "ALL_TYPE_EXCEPT_CONTRABAND" },
    tag => throw new Exception("A generic acceptance token must never reach the native matcher.")),
    "Generic buyers are not an explicit demand exception.");
Check(RiskPolicy.MatchesExplicitDemand("dream_dust", new[] { "dream_dust" }, null, null),
    "An exact requested item ID is explicit demand.");
Check(!RiskPolicy.MatchesExplicitDemand("dream_dust", new[] { "Dream_Dust", "dream_dust_extra" }, null, null),
    "ID matching must be ordinal and exact.");
Check(RiskPolicy.MatchesExplicitDemand("dream_dust", null, new[] { "NARCOTIC" }, HasTag),
    "An item matching a requested native tag is explicit demand.");
Check(RiskPolicy.MatchesExplicitDemand("dream_dust", null, new[] { "ALL_TYPE", "SUBSTANCE" }, HasTag),
    "A specific demand remains valid alongside generic acceptance.");
Check(!RiskPolicy.MatchesExplicitDemand("dream_dust", Array.Empty<string>(), new[] { "FOOD" }, HasTag),
    "Demand for a different category must not authorize risky goods.");
Check(!RiskPolicy.MatchesExplicitDemand(null, new[] { "" }, new[] { "", "ALL_TYPE" }, HasTag),
    "Empty identifiers and demand tags are not a match.");
Check(!RiskPolicy.MatchesExplicitDemand("dream_dust", null, null, null),
    "Missing demand data must fail closed.");

var bundle = new[]
{
    new RiskItemFacts(false, false, true), // A specifically requested box.
    new RiskItemFacts(true, false, false), // Its contents are not requested.
    new RiskItemFacts(false, true, true)
};
Check(RiskPolicy.GetBundleBlockedCategories(protectedOptions, bundle) == RiskCategory.Contraband,
    "Container demand must not grant an exception to unrequested contraband inside it.");
Check(RiskPolicy.GetBundleBlockedCategories(protectedOptions, bundle.Reverse()) == RiskCategory.Contraband,
    "Bundle protection must not depend on traversal order.");
var bothRisks = new[] { new RiskItemFacts(true, false, false), new RiskItemFacts(false, true, false) };
Check(RiskPolicy.GetBundleBlockedCategories(protectedOptions, bothRisks) == (RiskCategory.Contraband | RiskCategory.Stolen),
    "All independently blocked risk categories in a bundle must be retained.");
Check(RiskPolicy.GetBundleBlockedCategories(protectedOptions, bothRisks.Select(f => f with { HasExplicitDemand = true })) == RiskCategory.None,
    "Each protected component can receive its own explicit demand exception.");
Check(RiskPolicy.GetBundleBlockedCategories(asymmetricOptions, bothRisks.Select(f => f with { HasExplicitDemand = true })) == RiskCategory.Contraband,
    "Requested stolen contents cannot grant an exception to requested contraband contents when its exception is disabled.");
protectedOptions.AllowContrabandDemandExceptions = protectedOptions.AllowStolenDemandExceptions = false;
Check(RiskPolicy.GetBundleBlockedCategories(protectedOptions, bundle) == (RiskCategory.Contraband | RiskCategory.Stolen),
    "Strict protection must ignore demand even for some requested components.");
Check(RiskPolicy.GetBundleBlockedCategories(protectedOptions, Array.Empty<RiskItemFacts>()) == RiskCategory.None,
    "An empty bundle carries no inferred hidden risk.");

// End-to-end selection respects independent manual restrictions. Automatic
// exceptions may not overwrite a manually forbidden item or a descendant rule.
Candidate Make(string key, long price, bool manualForbidden, RiskCategory automaticBlock, bool required = false) =>
    new() { Key = key, Group = key, Price = price, Forbidden = manualForbidden || automaticBlock != RiskCategory.None, Required = required };
protectedOptions.AllowContrabandDemandExceptions = protectedOptions.AllowStolenDemandExceptions = true;
var demanded = RiskPolicy.GetBlockedCategories(protectedOptions, true, true, true);
var blocked = RiskPolicy.GetBlockedCategories(protectedOptions, true, false, false);
var selected = OfferSolver.Solve(new[]
{
    Make("manual-ban", 100, true, demanded),
    Make("risky-unwanted", 99, false, blocked),
    Make("risky-requested", 40, false, demanded),
    Make("ordinary", 10, false, RiskCategory.None)
}, 200, false);
Check(selected.IsOptimal && selected.Plans[0].Items.Select(i => i.Key).ToHashSet().SetEquals(new[] { "risky-requested", "ordinary" }),
    "A demand exception must retain manual bans while allowing specifically requested risky goods.");
var conflict = OfferSolver.Solve(new[] { Make("required-but-protected", 99, false, blocked, true) }, 200, false);
Check(conflict.Status == OfferSolveStatus.Infeasible,
    "An automatic prohibition conflicting with a required item must fail instead of silently omitting the requirement.");
var rules = new Dictionary<string, int> { ["child"] = 2 };
bool manualBundleBan = (ItemRuleGroupLogic.EffectiveRule(rules, "box", new[] { "child" }) & 2) != 0;
var manuallyProtectedBundle = OfferSolver.Solve(new[]
{
    Make("box", 80, manualBundleBan, RiskPolicy.GetBundleBlockedCategories(protectedOptions,
        new[] { new RiskItemFacts(true, false, true) })),
    Make("ordinary", 10, false, RiskCategory.None)
}, 200, false);
Check(manuallyProtectedBundle.IsOptimal && manuallyProtectedBundle.Plans.All(p => p.Items.All(i => i.Key != "box")),
    "Manual forbidden descendants remain protected when automatic demand exceptions allow the bundle.");

// Model native callbacks at the transaction boundaries with live public facts.
// This verifies the policy against changed demand/content, rather than reusing
// facts captured when the original offer was calculated. Native execution is
// separately checked in the two permitted game saves.
(int Moved, int Sold, string StoppedAt) SimulateTransaction(TradeOptions options,
    Func<RiskItemFacts[]>[] goods, Action<int>? afterPlacement = null,
    Action<int>? beforeSale = null, Action<int>? afterSale = null)
{
    int moved = 0, sold = 0;
    for (int n = 0; n < goods.Length; n++)
    {
        bool Blocked() => RiskPolicy.GetBundleBlockedCategories(options, goods[n]()) != RiskCategory.None;
        if (Blocked()) return (moved, sold, "before-placement");
        moved++; afterPlacement?.Invoke(n);
        if (Blocked()) return (moved, sold, "after-placement");
        beforeSale?.Invoke(n);
        if (Blocked()) return (moved, sold, "before-sale");
        sold++; afterSale?.Invoke(n);
    }
    return (moved, sold, "");
}
var changingTags = new HashSet<string>(StringComparer.Ordinal) { "SUBSTANCE" };
RiskItemFacts DrugFacts() => new(true, false,
    RiskPolicy.MatchesExplicitDemand("dream_dust", null, changingTags, tag => tag == "SUBSTANCE"));
Func<RiskItemFacts[]>[] drugPlan = { () => new[] { DrugFacts() }, () => new[] { DrugFacts() } };
var normalTransaction = SimulateTransaction(protectedOptions, drugPlan);
Check(normalTransaction == (2, 2, ""), "A stable explicit demand must allow all planned risky goods.");
var placementWithdrawal = SimulateTransaction(protectedOptions, drugPlan, afterPlacement: _ =>
{ changingTags.Clear(); changingTags.Add("ALL_TYPE"); });
Check(placementWithdrawal == (1, 0, "after-placement"),
    "A placement callback replacing explicit demand with generic acceptance must stop before any sale.");
changingTags.Clear(); changingTags.Add("SUBSTANCE");
var saleWithdrawal = SimulateTransaction(protectedOptions, drugPlan, afterSale: _ => changingTags.Clear());
Check(saleWithdrawal == (1, 1, "before-placement"),
    "Demand withdrawn by the first sale must stop before moving the next risky item, retaining completed progress.");
changingTags.Clear(); changingTags.Add("SUBSTANCE");
var lateWithdrawal = SimulateTransaction(protectedOptions, drugPlan, beforeSale: _ => changingTags.Clear());
Check(lateWithdrawal == (1, 0, "before-sale"),
    "Demand withdrawn immediately before sale must prevent the pending sale.");
changingTags.Clear(); changingTags.Add("SUBSTANCE");
bool extraProtectedContent = false;
Func<RiskItemFacts[]>[] changingContainer = { () => extraProtectedContent
    ? new[] { new RiskItemFacts(false, false, true), DrugFacts(), new RiskItemFacts(false, true, false) }
    : new[] { new RiskItemFacts(false, false, true), DrugFacts() } };
var changedContents = SimulateTransaction(protectedOptions, changingContainer,
    afterPlacement: _ => extraProtectedContent = true);
Check(changedContents == (1, 0, "after-placement"),
    "New protected container contents introduced by placement callbacks must prevent the whole container sale.");
extraProtectedContent = false;
var childWithdrawal = SimulateTransaction(protectedOptions, changingContainer,
    afterPlacement: _ => changingTags.Clear());
Check(childWithdrawal == (1, 0, "after-placement"),
    "Persistent demand for the box must not retain an expired demand exception for its risky contents.");
changingTags.Clear(); changingTags.Add("SUBSTANCE");
bool becameStolen = false;
Func<RiskItemFacts[]>[] newlyDualRisk = { () => new[] { DrugFacts() with { IsStolen = becameStolen } } };
asymmetricOptions.AllowContrabandDemandExceptions = true;
asymmetricOptions.AllowStolenDemandExceptions = false;
var newlyProtectedCategory = SimulateTransaction(asymmetricOptions, newlyDualRisk,
    afterPlacement: _ => becameStolen = true);
Check(newlyProtectedCategory == (1, 0, "after-placement"),
    "A still-requested contraband item that becomes stolen during placement must stop when stolen exceptions are disabled.");
asymmetricOptions.AllowStolenDemandExceptions = true;
var bothExceptions = SimulateTransaction(asymmetricOptions, newlyDualRisk);
Check(bothExceptions == (1, 1, ""),
    "A specifically requested dual-risk item is allowed only after both category exceptions are enabled.");
var disabledProtection = SimulateTransaction(new TradeOptions(), drugPlan,
    afterPlacement: _ => changingTags.Clear());
Check(disabledProtection == (2, 2, ""),
    "Transaction revalidation must preserve original behavior when both automatic protections are disabled.");

const string legacyJson = "{\"IncludeShowcase\":false,\"IncludeHiddenStorage\":true,\"IncludeContainerContents\":true,\"AllowOverBudget\":true,\"OverBudgetMode\":1,\"OverBudgetValue\":10,\"PanelHotkey\":\"F8\",\"AuditHotkey\":\"F9\"}";
var legacy = JsonSerializer.Deserialize<TradeOptions>(legacyJson)!;
Check(!legacy.BlockContraband && !legacy.BlockStolen &&
    !legacy.AllowContrabandDemandExceptions && !legacy.AllowStolenDemandExceptions,
    "Loading a 1.4.0 settings file must preserve the existing opt-in behavior.");
Check(legacy.IncludeContainerContents && legacy.AllowOverBudget && legacy.OverBudgetMode == OverBudgetMode.Credits,
    "Unrelated legacy settings must be retained.");
var persisted = JsonSerializer.Deserialize<TradeOptions>(JsonSerializer.Serialize(protectedOptions))!;
Check(persisted.BlockContraband && persisted.BlockStolen &&
    persisted.AllowContrabandDemandExceptions && persisted.AllowStolenDemandExceptions,
    "Both category protections and exceptions must survive a settings JSON round trip.");
for (int exceptions = 0; exceptions < 4; exceptions++)
{
    var originalOptions = new TradeOptions
    {
        BlockContraband = true, BlockStolen = true,
        AllowContrabandDemandExceptions = (exceptions & 1) != 0,
        AllowStolenDemandExceptions = (exceptions & 2) != 0
    };
    var json = JsonSerializer.Serialize(originalOptions);
    var restoredOptions = JsonSerializer.Deserialize<TradeOptions>(json)!;
    Check(restoredOptions.AllowContrabandDemandExceptions == originalOptions.AllowContrabandDemandExceptions &&
        restoredOptions.AllowStolenDemandExceptions == originalOptions.AllowStolenDemandExceptions,
        "Asymmetric exception settings must remain independent when persisted.");
    Check(!json.Contains("\"AllowDemandExceptions\"", StringComparison.Ordinal),
        "The removed generic exception must not appear in newly persisted settings.");
}
var captured = protectedOptions.Clone();
protectedOptions.BlockContraband = protectedOptions.BlockStolen = false;
protectedOptions.AllowContrabandDemandExceptions = protectedOptions.AllowStolenDemandExceptions = false;
Check(captured.BlockContraband && captured.BlockStolen &&
    captured.AllowContrabandDemandExceptions && captured.AllowStolenDemandExceptions,
    "Changing live settings must not mutate a captured plan's options.");
Check(RiskPolicy.GetBundleBlockedCategories(legacy, bothRisks) == RiskCategory.None,
    "An older configuration must not suddenly block existing inventory.");
Console.WriteLine($"PASS: {assertions} risk-policy, explicit-demand, container, solver-conflict, and legacy-settings assertions.");
