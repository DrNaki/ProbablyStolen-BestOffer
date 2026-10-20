using System.Text.Json;
using DeepSpaceBestOffer;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
void Invalid(Action action)
{
    bool rejected = false;
    try { action(); } catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "Invalid period or mode was accepted.");
}
var rule = new CategorySaleRule();
Check(rule.EffectiveMode(8) == 0 && !rule.Limited && rule.ExpiresOnDay == null, "New categories must be opt-in and unlimited.");
foreach (int mode in new[] { 1, 2 })
{
    rule = new(); rule.SetMode(mode, 8);
    Check(rule.EffectiveMode(8) == mode && rule.EffectiveMode(10000) == mode, "Unlimited rule expired.");
    rule.SetPeriod(true, 3, 8);
    Check(rule.ExpiresOnDay == 11, "Duration must include the starting day.");
    for (int day = 8; day <= 10; day++) Check(rule.EffectiveMode(day) == mode, "Rule ended before its full period.");
    Check(rule.EffectiveMode(11) == 0 && rule.EffectiveMode(12) == 0 && rule.IsExpired(11), "Rule remained active on its expiry day.");
    Check(rule.EffectiveMode(7) == 0, "A timed rule leaked into a save before it was set.");
    rule.ExpiryNotified = true;
    rule.SetMode(mode, 11);
    Check(rule.ExpiresOnDay == 14 && !rule.ExpiryNotified && rule.EffectiveMode(11) == mode, "Renewed rule did not get a fresh schedule.");
    rule.SetPeriod(false, 3, 12);
    Check(rule.ExpiresOnDay == null && rule.EffectiveMode(10000) == mode, "Returning to unlimited retained expiry.");
    rule.SetPeriod(true, 1, 12);
    Check(rule.EffectiveMode(12) == mode && rule.EffectiveMode(13) == 0, "One-day rule must expire tomorrow.");
    rule.SetMode(0, 12);
    Check(rule.Mode == 0 && rule.ExpiresOnDay == 13 && !rule.IsExpired(13), "Clearing a sale rule must preserve the selected period without generating a notification.");
}
rule = new();
foreach (int days in new[] { 0, -1, 1000, int.MaxValue }) Invalid(() => rule.SetPeriod(true, days, 8));
foreach (int mode in new[] { -1, 3, 99 }) Invalid(() => rule.SetMode(mode, 8));
Invalid(() => rule.SetMode(2, 0));
Invalid(() => rule.SetPeriod(true, 3, int.MaxValue));
Check(rule.Mode == 0 && !rule.Limited, "Invalid editing mutated the current choice.");
rule.SetPeriod(true, 999, 8); rule.SetMode(2, 8);
Check(rule.ExpiresOnDay == 1007 && rule.EffectiveMode(1006) == 2 && rule.EffectiveMode(1007) == 0, "Maximum supported period is not exact.");

foreach (bool limited in new[] { false, true })
foreach (bool rememberedException in new[] { false, true })
{
    var cycling = new CategorySaleRule { AllowDemandException = rememberedException };
    cycling.SetPeriod(limited, 3, 6);
    cycling.CycleForbiddenWithDemandException(6);
    Check(cycling.EffectiveMode(6) == 2 && !cycling.AllowDemandException,
        "The first click must start a strict ban, including after a remembered legacy exception.");
    Check(cycling.StartsOnDay == 6 && cycling.ExpiresOnDay == (limited ? 9 : null), "A new ban did not start its selected period.");
    cycling.CycleForbiddenWithDemandException(7);
    Check(cycling.EffectiveMode(7) == 2 && cycling.AllowDemandException, "The second click did not enable the demand exception.");
    Check(cycling.StartsOnDay == 6 && cycling.ExpiresOnDay == (limited ? 9 : null),
        "Switching an active ban to demand exceptions extended its deadline.");
    Check(CategoryRuleLogic.PeriodDaysForEditing(cycling, 7) == (limited ? 2 : 3), "Cycling the ban reset its remaining days.");
    var reloaded = JsonSerializer.Deserialize<CategorySaleRule>(JsonSerializer.Serialize(cycling))!;
    foreach (string category in new[] { CategoryRuleLogic.Contraband, CategoryRuleLogic.Stolen })
    {
        var choice = new CategoryRuleSelection(category, reloaded.Mode, reloaded.AllowDemandException,
            reloaded.StartsOnDay, reloaded.ExpiresOnDay);
        Check(CategoryRuleLogic.Resolve(new[] { choice }, _ => true, true, 7) == 0, "Reloaded demand exception did not release an explicitly requested item.");
        Check(CategoryRuleLogic.Resolve(new[] { choice }, _ => true, false, 7) == 2, "Reloaded demand exception released an item without explicit demand.");
    }
    reloaded.CycleForbiddenWithDemandException(7);
    Check(reloaded.Mode == 0 && !reloaded.AllowDemandException && reloaded.ExpiresOnDay == (limited ? 9 : null),
        "The third click must clear the sale rule while preserving its separately selected period.");
    Check(!reloaded.IsExpired(9), "A cancelled ban produced an expiry notification.");
    reloaded.CycleForbiddenWithDemandException(8);
    Check(reloaded.Mode == 2 && !reloaded.AllowDemandException && reloaded.ExpiresOnDay == (limited ? 9 : null),
        "Re-enabling a strict ban extended its still-active period.");
}
var requiredRisk = new CategorySaleRule { AllowDemandException = true };
requiredRisk.SetMode(1, 8); requiredRisk.CycleForbiddenWithDemandException(8);
Check(requiredRisk.Mode == 2 && !requiredRisk.AllowDemandException, "Switching from required did not select strict forbidden exclusively.");
requiredRisk.CycleForbiddenWithDemandException(8); requiredRisk.SetMode(1, 8);
Check(requiredRisk.EffectiveMode(8) == 1, "Required did not replace the conditional ban.");
foreach (bool rememberedException in new[] { false, true })
{
    var expiredRisk = new CategorySaleRule { AllowDemandException = rememberedException };
    expiredRisk.SetPeriod(true, 1, 8); expiredRisk.SetMode(2, 8); expiredRisk.ExpiryNotified = true;
    expiredRisk.CycleForbiddenWithDemandException(9);
    Check(expiredRisk.EffectiveMode(9) == 2 && !expiredRisk.AllowDemandException && !expiredRisk.ExpiryNotified && expiredRisk.ExpiresOnDay == 10,
        "Clicking an expired rule must start a fresh strict ban rather than cycle the stale state.");
}
var invalidCycle = new CategorySaleRule { Mode = 2, AllowDemandException = true };
Invalid(() => invalidCycle.CycleForbiddenWithDemandException(0));
Check(invalidCycle.Mode == 2 && invalidCycle.AllowDemandException, "An invalid cycle changed the current rule.");

foreach (int mode in new[] { 1, 2 })
{
    var timed = new CategorySaleRule(); timed.SetPeriod(true, 3, 6); timed.SetMode(mode, 6);
    Check(CategoryRuleLogic.PeriodDescription(timed, 6) == "3 天后到期（第 9 天）", "Countdown did not begin at the chosen period.");
    Check(CategoryRuleLogic.PeriodDescription(timed, 7) == "后天到期（第 9 天）", "Countdown did not update on the following game day.");
    Check(CategoryRuleLogic.PeriodDescription(timed, 8) == "明天到期（第 9 天）", "A day-nine expiry must say tomorrow on day eight.");
    Check(CategoryRuleLogic.PeriodDescription(timed, 9) == "已到期（第 9 天）", "The expiry day must not show a future countdown.");
    Check(timed.StartsOnDay == 6 && timed.ExpiresOnDay == 9, "Rendering the countdown changed its schedule.");
    timed.SetPeriod(false, 3, 8);
    Check(CategoryRuleLogic.PeriodDescription(timed, 8) == "不限期" && timed.ExpiresOnDay == null, "Turning off the single period toggle did not remove the limit.");
}
Check(CategoryRuleLogic.PeriodDescription(new CategorySaleRule(), 8) == "不限期", "An unconfigured category displayed an active timer.");
Check(CategoryRuleLogic.PeriodDescription(new CategorySaleRule { Limited = true }, 8) == "3 天后到期（第 11 天）", "A duration preset did not immediately preview its expiry date.");
var pendingPeriod = new CategorySaleRule();
pendingPeriod.SetPeriod(true, 3, 6);
Check(pendingPeriod.Mode == 0 && pendingPeriod.ExpiresOnDay == 9 && CategoryRuleLogic.PeriodDescription(pendingPeriod, 6) == "3 天后到期（第 9 天）",
    "Choosing days before a sale rule did not immediately anchor and display the expiry date.");
Check(CategoryRuleLogic.PeriodDaysForEditing(pendingPeriod, 7) == 2 && !pendingPeriod.IsExpired(9),
    "An unused period must count down without generating a sale-rule notification.");
pendingPeriod.SetMode(2, 7);
Check(pendingPeriod.ExpiresOnDay == 9 && pendingPeriod.EffectiveMode(7) == 2 && pendingPeriod.EffectiveMode(6) == 0,
    "Selecting a rule later moved the expiry date or applied it before selection.");
pendingPeriod.SetMode(1, 8);
Check(pendingPeriod.ExpiresOnDay == 9 && CategoryRuleLogic.PeriodDaysForEditing(pendingPeriod, 8) == 1,
    "Changing the sale rule restarted the selected period.");
pendingPeriod.SetMode(0, 8);
Check(pendingPeriod.ExpiresOnDay == 9 && !pendingPeriod.IsExpired(9), "Clearing a sale rule lost the independently chosen period or generated a notice.");
pendingPeriod.SetMode(2, 9);
Check(pendingPeriod.ExpiresOnDay == 12 && pendingPeriod.EffectiveMode(9) == 2, "Re-enabling an expired period did not get a usable new schedule.");
pendingPeriod.SetPeriod(true, 5, 10);
Check(pendingPeriod.ExpiresOnDay == 15 && CategoryRuleLogic.ExpiryDescription(15, 10) == "5 天后到期（第 15 天）", "The edited-day preview differs from the committed expiry.");
var savedPeriod = JsonSerializer.Deserialize<CategorySaleRule>(JsonSerializer.Serialize(pendingPeriod))!;
Check(savedPeriod.ExpiresOnDay == 15 && CategoryRuleLogic.PeriodDaysForEditing(savedPeriod, 12) == 3, "Reloading a selected period restarted its countdown.");
foreach (bool limited in new[] { false, true })
{
    var expired = new CategorySaleRule(); expired.SetPeriod(true, 1, 8); expired.SetMode(2, 8);
    expired.SetPeriod(limited, 3, 9);
    Check(expired.EffectiveMode(9) == 0 && expired.Mode == 0 && expired.ExpiresOnDay == (limited ? 12 : null),
        "Editing an expired period silently reactivated its former category ban.");
}
foreach (int mode in new[] { 1, 2 })
{
    var countdown = new CategorySaleRule(); countdown.SetPeriod(true, 3, 6); countdown.SetMode(mode, 6);
    foreach (var (day, remaining) in new[] { (6,3), (7,2), (8,1), (9,0) })
        Check(CategoryRuleLogic.PeriodDaysForEditing(countdown, day) == remaining, "Editable days did not count down with the actual game date.");
    countdown.SetPeriod(true, 3, 7);
    Check(countdown.ExpiresOnDay == 10 && CategoryRuleLogic.PeriodDaysForEditing(countdown, 7) == 3,
        "Editing remaining days back to the original number did not extend the deadline from today.");
    countdown.SetPeriod(false, CategoryRuleLogic.PeriodDaysForEditing(countdown, 8), 8);
    Check(CategoryRuleLogic.PeriodDaysForEditing(countdown, 100) == 2 && countdown.EffectiveMode(100) == mode,
        "Disabling the period did not freeze its chosen days while preserving the unlimited sale rule.");
}
Check(CategoryRuleLogic.PeriodDaysForEditing(new CategorySaleRule { Limited = true }, 8) == 3,
    "A period chosen before a sale rule lost its editable preset.");

foreach (int category in new[] { 0, 1, 2 })
foreach (int own in new[] { 0, 1, 2 })
foreach (int child in new[] { 0, 1, 2 })
{
    int effective = CategoryRuleLogic.EffectiveRule(new[] { category }, new[] { own, child });
    Check(effective == (category == 0 ? own | child : category), "Category did not override inventory choices for a whole sale.");
}
for (int mask = 0; mask < 81; mask++)
{
    int n = mask;
    var choices = Enumerable.Range(0, 4).Select(_ => { int mode = n % 3; n /= 3; return mode; }).ToArray();
    int expected = choices.Contains(2) ? 2 : choices.Contains(1) ? 1 : 3;
    Check(CategoryRuleLogic.EffectiveRule(choices, new[] { 1, 2 }) == expected, "Category conflicts must consistently prefer forbidden.");
    Check(CategoryRuleLogic.EffectiveRule(choices.Reverse(), new[] { 2, 1 }) == expected, "Rule result changed with capture ordering.");
}

var food = new CategoryRuleSelection("type:FOOD", 1, false, 8, 11);
var stolen = new CategoryRuleSelection(CategoryRuleLogic.Stolen, 2, true, 8, 12);
Func<string, bool> both = _ => true;
Check(CategoryRuleLogic.Resolve(new[] { food, stolen }, both, false, 8) == 2, "Overlapping category ban did not win.");
Check(CategoryRuleLogic.Resolve(new[] { stolen, food }, both, false, 8) == 2, "Overlap depends on rule order.");
Check(CategoryRuleLogic.Resolve(new[] { food, stolen }, both, true, 8) == 1, "Explicit demand exception did not release only its own ban.");
Check(CategoryRuleLogic.Resolve(new[] { food, stolen with { AllowDemandException = false } }, both, true, 8) == 2, "Demand bypassed a category without an exception.");
Check(CategoryRuleLogic.Resolve(new[] { food }, _ => false, false, 8) == 0, "Unrelated category changed this item.");
Check(CategoryRuleLogic.Resolve(new[] { food }, both, false, 7) == 0, "Rule applied before its start day.");
Check(CategoryRuleLogic.Resolve(new[] { food }, both, false, 11) == 0, "Frozen capture choices did not recheck expiry against the current day.");
Check(CategoryRuleLogic.Resolve(new[] { food, stolen }, both, false, 11) == 2, "Expiry of one overlapping category released a different active ban.");
Check(CategoryRuleLogic.Resolve(new[] { food, stolen }, both, false, 12) == 0, "Expired overlap still suppressed inventory rules.");
Check(CategoryRuleLogic.Resolve(new[] { stolen }, both, true, 8) == 0, "Demand exception should fall back to inventory rules when no other category applies.");

var book = new CategoryRuleBook();
rule = new(); rule.SetPeriod(true, 3, 8); rule.SetMode(2, 8);
book.Runs["run-a"] = new() { ["type:FOOD"] = rule };
book.Runs["run-b"] = new() { ["type:FOOD"] = new() { Mode = 1 } };
var restored = JsonSerializer.Deserialize<CategoryRuleBook>(JsonSerializer.Serialize(book))!;
Check(restored.Runs["run-a"]["type:FOOD"].EffectiveMode(10) == 2 && restored.Runs["run-b"]["type:FOOD"].EffectiveMode(10) == 1, "Rules leaked across save run IDs.");
var pending = restored.Runs["run-a"]["type:FOOD"];
Check(pending.IsExpired(11) && !pending.ExpiryNotified, "Pending expiry was lost after reload.");
pending.ExpiryNotified = true;
restored = JsonSerializer.Deserialize<CategoryRuleBook>(JsonSerializer.Serialize(restored))!;
Check(restored.Runs["run-a"]["type:FOOD"].ExpiryNotified, "Notification completion was not persisted.");
Check(CategoryRuleLogic.ExpiryMessage("食物", 2, 11).Contains("可交易此分类") && CategoryRuleLogic.ExpiryMessage("食物", 1, 11).Contains("不再强制出售"), "Expiry notifications do not distinguish forbidden and required rules.");

var options = new TradeOptions { CategoryRules = new[] { food } };
var copy = options.Clone(); options.CategoryRules[0] = stolen;
Check(copy.CategoryRules[0] == food, "Captured category choices were changed through the UI's mutable array.");
Check(!JsonSerializer.Serialize(options).Contains("CategoryRules"), "A run's rules leaked into global preferences.");

foreach (int mode in new[] { 0, 1, 2 })
foreach (int day in new[] { 7, 9 })
{
    var legacyRisk = new CategorySaleRule { AllowDemandException = true };
    legacyRisk.SetPeriod(true, 3, 6); legacyRisk.SetMode(mode, 6);
    legacyRisk.ExpiryNotified = true;
    int expected = legacyRisk.EffectiveMode(day);
    Check(legacyRisk.RemovePeriod(day), "A legacy risk period was not migrated.");
    Check(!legacyRisk.Limited && legacyRisk.ExpiresOnDay == null && !legacyRisk.ExpiryNotified,
        "Risk migration retained its period or a stale notification.");
    Check(legacyRisk.EffectiveMode(day) == expected && legacyRisk.EffectiveMode(10000) == expected,
        "Removing a risk timer changed its current rule or left an active risk rule limited.");
    Check(legacyRisk.AllowDemandException, "Risk migration discarded the selected explicit-demand exception.");
    Check(!legacyRisk.RemovePeriod(day), "Removing an already migrated risk period should be idempotent.");
}
var futureRisk = new CategorySaleRule(); futureRisk.SetPeriod(true, 3, 10); futureRisk.SetMode(2, 10);
futureRisk.RemovePeriod(8);
Check(futureRisk.EffectiveMode(8) == 0 && futureRisk.EffectiveMode(10) == 2, "Migrating a future risk period activated it before its original start day.");
var invalidRemoval = new CategorySaleRule(); invalidRemoval.SetPeriod(true, 3, 8);
Invalid(() => invalidRemoval.RemovePeriod(0));
Check(invalidRemoval.Limited && invalidRemoval.ExpiresOnDay == 11, "Invalid risk migration mutated the saved timer.");

foreach (var (categoryMode, inventoryMode, expected) in new[] { (2, 1, false), (1, 2, true), (0, 2, false) })
{
    int effective = CategoryRuleLogic.EffectiveRule(new[] { categoryMode }, new[] { inventoryMode });
    var answer = OfferSolver.Solve(new[] {
        new Candidate { Key = "item", Group = "food", Price = 10, Required = effective == 1, Forbidden = effective == 2 },
        new Candidate { Key = "other", Group = "other", Price = 5 }
    }, 10, false);
    Check(answer.Plans.Count > 0 && answer.Plans[0].Items.Any(i => i.Key == "item") == expected, "Solver did not honor category precedence.");
}
Console.WriteLine($"PASS: {checks} category rule, expiry, save isolation and solver assertions.");
