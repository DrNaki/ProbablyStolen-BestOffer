using DeepSpaceBestOffer;
using Newtonsoft.Json;

const string modified = "type:MODIFIED_SEED";
var icon = CategoryIconCatalog.Icons[modified];
Check(icon.Atlas == "Items/items_hydroponic_plant" && icon.Sprite == "nutrifruit_modified_seed", "Native modified-seed icon");
Check(CategoryRuleLogic.IsCategoryId(modified), "New category can be persisted");
var rule = new CategorySaleRule(); rule.SetMode(2, 30); rule.SetPeriod(true, 3, 30);
var book = new CategoryRuleBook(); book.Runs["existing-run"] = new() { [modified] = rule };
var restored = JsonConvert.DeserializeObject<CategoryRuleBook>(JsonConvert.SerializeObject(book))!;
var saved = restored.Runs["existing-run"][modified];
Check(saved.EffectiveMode(30) == 2 && saved.EffectiveMode(32) == 2 && saved.EffectiveMode(33) == 0, "Same inclusive expiry contract");
var choices = new[] { new CategoryRuleSelection("type:SEED", 1, false), new CategoryRuleSelection(modified, saved.Mode, false, 30, 33) };
bool MatchesModified(string id) => id is "type:SEED" or modified;
Check(CategoryRuleLogic.Resolve(choices, MatchesModified, false, 30) == 2, "New category ban overrides Seed must-sell");
Check(CategoryRuleLogic.Resolve(choices, MatchesModified, true, 30) == 2, "Ordinary categories do not acquire risk demand exceptions");
Check(CategoryRuleLogic.Resolve(choices, MatchesModified, false, 33) == 1, "Expiry restores existing Seed rule");
Check(CategoryRuleLogic.Resolve(choices, id => id == "type:SEED", false, 30) == 1, "Unmodified seed is unaffected");
Check(!restored.Runs.ContainsKey("other-run"), "Run isolation retained");
Console.WriteLine("PASS: 8 Workshop modified-seed, overlap, expiry, JSON compatibility and icon checks.");
static void Check(bool value, string label) { if (!value) throw new Exception(label); }
