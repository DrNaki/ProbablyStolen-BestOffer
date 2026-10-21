using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using DeepSpaceBestOffer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
bool HasChinese(string value) => value.Any(c => c is >= '\u4e00' and <= '\u9fff');
string Indices(string value) => string.Join(",", Regex.Matches(value, @"\{(\d+)(?:,-?\d+)?(?::[^{}]+)?\}")
    .Select(m => m.Groups[1].Value).OrderBy(v => v, StringComparer.Ordinal));

foreach (string code in new[] { "zh", "zh-Hans", "ZH-hans", "zh-Hant", "zh-CN", "zh-TW", "zh_HK" })
{
    L.SetGameLanguage(code);
    Check(L.Chinese && L.T("最佳报价") == "最佳报价", "Chinese selection: " + code);
    Check(L.F($"内含 {3} 片") == "内含 3 片", "Chinese interpolation unchanged");
}
foreach (string? code in new[] { "en", "en-US", "ja", "ko", "de", "fr", "es", "pt-BR", "zhx", "", null })
{
    L.SetGameLanguage(code);
    Check(!L.Chinese && L.T("最佳报价") == "Best Offer", "English fallback: " + code);
    Check(L.F($"内含 {3} 片") == "Contains 3 units", "English context");
}
Check(L.F($"「{"item {0} 中文"}」的 {3} 件单件规则已清除；容器内容规则保留。") ==
    "Cleared individual rules for 3 copies of item {0} 中文. Container contents rules kept.", "Never translate or reformat item names");
Check(L.T("✓ 全部") == "✓ All", "English translation preserves the selection mark");
L.SetGameLanguage("zh");
Check(L.T("✓ 全部") == "✓ 全部", "Chinese selection marker unchanged");
foreach (var pair in LocalizationCatalog.English)
{
    Check(!string.IsNullOrWhiteSpace(pair.Value), "Empty translation: " + pair.Key);
    Check(!HasChinese(pair.Value), "Chinese in English resource: " + pair.Key);
    Check(Indices(pair.Key) == Indices(pair.Value), "Placeholder mismatch: " + pair.Key);
    Check(CompositeFormat.Parse(pair.Key).MinimumArgumentCount == CompositeFormat.Parse(pair.Value).MinimumArgumentCount,
        "Invalid composite format: " + pair.Key);
    object[] values = Enumerable.Range(0, CompositeFormat.Parse(pair.Key).MinimumArgumentCount).Select(n => (object)(n + 1)).ToArray();
    L.SetGameLanguage("ja");
    string english = L.F(FormattableStringFactory.Create(pair.Key, values));
    Check(!HasChinese(english), "Untranslated English result: " + pair.Key);
    L.SetGameLanguage("zh-Hans");
    Check(L.F(FormattableStringFactory.Create(pair.Key, values)) == string.Format(CultureInfo.CurrentCulture, pair.Key, values),
        "Chinese content changed: " + pair.Key);
}

string root = AppContext.BaseDirectory;
while (!Directory.Exists(Path.Combine(root, "src", "BestOffer"))) root = Directory.GetParent(root)?.FullName ?? throw new Exception("Project root missing");
foreach (string path in Directory.GetFiles(Path.Combine(root, "src", "BestOffer"), "*.cs"))
{
    if (Path.GetFileName(path) is "LocalizationCatalog.cs" or "NativeLocalization.cs") continue;
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path));
    foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
    {
        if (call.Expression.ToString() is not ("L.T" or "L.F")) continue;
        var arg = call.ArgumentList.Arguments[0].Expression;
        string key;
        if (arg is LiteralExpressionSyntax literal) key = literal.Token.ValueText;
        else if (arg is InterpolatedStringExpressionSyntax interpolation)
        {
            int n = 0;
            key = string.Concat(interpolation.Contents.Select(c => c is InterpolatedStringTextSyntax t ? t.TextToken.ValueText :
                "{" + n++ + ((InterpolationSyntax)c).AlignmentClause + ((InterpolationSyntax)c).FormatClause + "}"));
        }
        else throw new Exception("Uninspectable localization call: " + call);
        Check(LocalizationCatalog.English.ContainsKey(key), $"Missing resource in {Path.GetFileName(path)}:{tree.GetLineSpan(call.Span).StartLinePosition.Line + 1}: {key}");
    }
}

L.SetGameLanguage("en");
Check(CategoryRuleLogic.ExpiryDescription(11, 10) == "Expires tomorrow (Day 11)", "English expiry context");
Check(CategoryRuleLogic.ExpiryMessage("Food", 2, 11).Contains("inventory rules"), "English report reminder");
L.SetGameLanguage("zh-Hans");
Check(CategoryRuleLogic.ExpiryDescription(11, 10) == "明天到期（第 11 天）", "Chinese expiry unchanged");
var members = new[] { new InventoryRuleMember("run:1:item", "一卷厕纸") };
var rules = new Dictionary<string, int>(); var all = new Dictionary<string, int>();
string key1 = ItemRuleGroupLogic.GroupKey("", members[0].Name, members[0].Key);
ItemRuleGroupLogic.SetAllRule(rules, all, key1, members, 2);
L.SetGameLanguage("ja");
Check(ItemRuleGroupLogic.OwnRule(rules, all, members[0]) == 2, "Existing Chinese All rule survives English UI");
var restocked = new InventoryRuleMember("run:99:item", "一卷厕纸");
Check(ItemRuleGroupLogic.OwnRule(rules, all, restocked) == 2, "All still applies after empty stock and restocking");
Check(ItemRuleGroupLogic.OwnRule(rules, all, restocked with { Key = "other-run:99:item" }) == 0, "Save separation survives language change");
Console.WriteLine($"PASS: {checks} localization checks, {LocalizationCatalog.English.Count} resources, Chinese preserved; all other locales use English.");
