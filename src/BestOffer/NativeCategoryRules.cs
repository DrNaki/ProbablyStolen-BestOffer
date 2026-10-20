using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using Il2Cpp;
using MelonLoader.Utils;
using UnityEngine;

namespace DeepSpaceBestOffer;

public sealed record TradeCategory(string Id, string Name, bool HasDemandException = false);

public static class NativeCategoryPolicy
{
    public static IReadOnlyList<TradeCategory> Categories()
    {
        var result = new List<TradeCategory>
        {
            new(CategoryRuleLogic.Contraband, "违禁品", true),
            new(CategoryRuleLogic.Stolen, "赃物", true)
        };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var types = TypeHelper.ALL_TYPES_LIST;
        if (types != null)
            for (int i = 0; i < types.Count; i++)
            {
                string type = types[i];
                // The existing risk row already represents the native
                // CONTRABAND category, including its demand exception.
                if (string.IsNullOrEmpty(type) || type is "ALL_TYPE" or "ALL_TYPE_EXCEPT_CONTRABAND" or "CONTRABAND" || !seen.Add(type)) continue;
                string name = TypeHelper.GetTypeDisplayName(type);
                result.Add(new("type:" + type, string.IsNullOrWhiteSpace(name) ? type : name));
            }
        return result;
    }

    public static bool Matches(GameItem item, string category) => category switch
    {
        CategoryRuleLogic.Contraband => item.IsTag("CONTRABAND_ITEM_TAG") || item.IsGameItemType("CONTRABAND"),
        CategoryRuleLogic.Stolen => item.IsTag("STOLEN_TAG"),
        _ => category.StartsWith("type:", StringComparison.Ordinal) && item.IsGameItemType(category[5..])
    };
}

public sealed partial class BestOfferMod
{
    CategoryRuleBook categoryRuleBook = new();
    string categoryRulePath = "", categoryDateRun = "";
    int categoryDateDay = -1;
    string selectedCategoryId = "";
    bool categoryPickerOpen;
    float categoryPickerScroll = 1;
    float categoryDateCheckAt;
    readonly List<CategorySaleRule> categoryReportPending = new();

    void InitializeCategoryRules()
    {
        categoryRulePath = Path.Combine(MelonEnvironment.UserDataDirectory, "BestOffer-category-rules.json");
        try
        {
            if (File.Exists(categoryRulePath))
                categoryRuleBook = JsonSerializer.Deserialize<CategoryRuleBook>(File.ReadAllText(categoryRulePath)) ?? new();
            if (categoryRuleBook.Runs == null) throw new InvalidDataException("分类规则存档缺少 Runs。");
            foreach (var run in categoryRuleBook.Runs)
                if (string.IsNullOrWhiteSpace(run.Key) || run.Value == null || run.Value.Any(pair =>
                    pair.Value == null || !CategoryRuleLogic.IsCategoryId(pair.Key) || pair.Value.Mode is not (0 or 1 or 2) ||
                    pair.Value.DurationDays is < 1 or > CategoryRuleLogic.MaxDurationDays || pair.Value.StartsOnDay < 0 ||
                    pair.Value.Limited && pair.Value.Mode != 0 && (!pair.Value.ExpiresOnDay.HasValue || pair.Value.ExpiresOnDay <= pair.Value.StartsOnDay)))
                    throw new InvalidDataException("分类规则的类别、规则或有效期无效。");
        }
        catch (Exception e) { categoryRuleBook = new(); LoggerInstance.Warning("读取分类规则失败：" + e.Message); }
        HarmonyInstance.Patch(AccessTools.Method(typeof(StartOfDayUIManager), "InitPanel"),
            prefix: new HarmonyMethod(typeof(BestOfferMod), nameof(BeforeMorningReport)) { priority = HarmonyLib.Priority.Last },
            postfix: new HarmonyMethod(typeof(BestOfferMod), nameof(AfterMorningReport)));
    }

    static void BeforeMorningReport()
    {
        try { Current?.AppendCategoryExpirationNotices(); }
        catch (Exception e) { Current?.LoggerInstance.Warning("写入分类规则到期提醒失败：" + e.Message); }
    }

    static void AfterMorningReport()
    {
        var mod = Current;
        if (mod == null || mod.categoryReportPending.Count == 0) return;
        foreach (var rule in mod.categoryReportPending) rule.ExpiryNotified = true;
        mod.categoryReportPending.Clear();
        try { mod.SaveCategoryRules(); }
        catch (Exception e) { mod.LoggerInstance.Warning("保存分类规则到期提醒状态失败：" + e.Message); }
    }

    static (string Run, int Day) CategoryClock()
    {
        // PlayerStore.Instance lazily constructs inventories. Reading it on
        // the title screen would initialize them before native UI assets exist.
        if (EmporiumEntry.Instance == null || StoreStation.Instance == null || StoreStation.Instance.dayCounter < 1) return ("", 0);
        return (PlayerStore.Instance?.runID ?? "", StoreStation.Instance.dayCounter);
    }

    Dictionary<string, CategorySaleRule>? CurrentCategoryScope()
    {
        var clock = CategoryClock();
        if (string.IsNullOrEmpty(clock.Run) || clock.Day < 1) return null;
        if (!categoryRuleBook.Runs.TryGetValue(clock.Run, out var scope))
        {
            scope = new(StringComparer.Ordinal)
            {
                [CategoryRuleLogic.Contraband] = new() { Mode = blockContraband ? 2 : 0, AllowDemandException = allowContrabandDemandExceptions },
                [CategoryRuleLogic.Stolen] = new() { Mode = blockStolen ? 2 : 0, AllowDemandException = allowStolenDemandExceptions }
            };
            categoryRuleBook.Runs[clock.Run] = scope;
            SaveCategoryRules();
        }
        bool migrated = false;
        foreach (var pair in scope)
            if (pair.Key is CategoryRuleLogic.Contraband or CategoryRuleLogic.Stolen)
                migrated |= pair.Value.RemovePeriod(clock.Day);
            else if (pair.Value.Limited && !pair.Value.ExpiresOnDay.HasValue)
            {
                // Older inactive rules stored only a duration preset.
                pair.Value.SetPeriod(true, pair.Value.DurationDays, clock.Day);
                migrated = true;
            }
        if (migrated) SaveCategoryRules();
        return scope;
    }

    CategoryRuleSelection[] CurrentCategorySelections() => CurrentCategoryScope()?.Where(p => p.Value.Mode is 1 or 2)
        .OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new CategoryRuleSelection(p.Key, p.Value.Mode,
            p.Value.AllowDemandException, p.Value.StartsOnDay, p.Value.Limited ? p.Value.ExpiresOnDay : null)).ToArray()
        ?? Array.Empty<CategoryRuleSelection>();

    CategorySaleRule GetCategoryRule(string id)
    {
        var scope = CurrentCategoryScope() ?? throw new InvalidOperationException("请进入店铺后设置分类规则。");
        if (!scope.TryGetValue(id, out var rule)) scope[id] = rule = new();
        return rule;
    }

    void SaveCategoryRules()
    {
        if (categoryRulePath.Length == 0) return;
        string temporary = categoryRulePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(categoryRuleBook, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, categoryRulePath, true);
    }

    void CategoryRuleChanged(string name)
    {
        SaveCategoryRules();
        revision++; plans.Clear(); showConfirm = false; refreshAt = 0; uiDirty = true;
        status = $"「{name}」分类设置已保存。" + RuleNextStep();
    }

    void SetCategoryMode(TradeCategory category, int mode)
    {
        var rule = GetCategoryRule(category.Id);
        if (rule.EffectiveMode(CategoryClock().Day) == mode && !rule.IsExpired(CategoryClock().Day)) return;
        rule.SetMode(mode, CategoryClock().Day);
        CategoryRuleChanged(category.Name);
        if (mode == 0) status = $"已取消「{category.Name}」的分类规则，恢复库存规则。" + RuleNextStep();
    }

    void ToggleCategoryMode(TradeCategory category, int mode) =>
        SetCategoryMode(category, GetCategoryRule(category.Id).EffectiveMode(CategoryClock().Day) == mode ? 0 : mode);

    void CycleCategoryForbidden(TradeCategory category)
    {
        var rule = GetCategoryRule(category.Id);
        rule.CycleForbiddenWithDemandException(CategoryClock().Day);
        CategoryRuleChanged(category.Name);
        if (rule.Mode == 0) status = $"已取消「{category.Name}」的分类规则，恢复库存规则。" + RuleNextStep();
    }

    void SetCategoryPeriod(TradeCategory category, bool limited, int days)
    {
        if (category.HasDemandException) return;
        var rule = GetCategoryRule(category.Id);
        int day = CategoryClock().Day;
        if (rule.Limited == limited && CategoryRuleLogic.PeriodDaysForEditing(rule, day) == days) return;
        rule.SetPeriod(limited, days, day);
        CategoryRuleChanged(category.Name);
    }

    void ToggleCategoryPeriod(TradeCategory category)
    {
        var rule = GetCategoryRule(category.Id);
        SetCategoryPeriod(category, !rule.Limited, Math.Max(1, CategoryRuleLogic.PeriodDaysForEditing(rule, CategoryClock().Day)));
    }

    void ChangeCategoryPeriodDays(TradeCategory category, int delta)
    {
        var rule = GetCategoryRule(category.Id);
        int days = CategoryRuleLogic.PeriodDaysForEditing(rule, CategoryClock().Day);
        SetCategoryPeriod(category, true, Math.Clamp(days + delta, 1, CategoryRuleLogic.MaxDurationDays));
    }

    void UpdateCategoryRuleDate()
    {
        if (Time.unscaledTime < categoryDateCheckAt) return;
        categoryDateCheckAt = Time.unscaledTime + .5f;
        var clock = CategoryClock();
        if (clock.Run == categoryDateRun && clock.Day == categoryDateDay) return;
        bool changedRun = clock.Run != categoryDateRun;
        categoryDateRun = clock.Run; categoryDateDay = clock.Day;
        if (changedRun) { selectedCategoryId = ""; categoryPickerOpen = false; categoryPickerScroll = 1; }
        revision++; plans.Clear(); showConfirm = false; refreshAt = 0; uiDirty = true;
    }

    void AppendCategoryExpirationNotices()
    {
        var scope = CurrentCategoryScope();
        categoryReportPending.Clear();
        if (scope == null) return;
        var day = CategoryClock().Day;
        var names = NativeCategoryPolicy.Categories().ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
        foreach (var pair in scope.OrderBy(p => p.Value.ExpiresOnDay).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            var rule = pair.Value;
            if (!rule.IsExpired(day) || rule.ExpiryNotified) continue;
            string message = CategoryRuleLogic.ExpiryMessage(names.GetValueOrDefault(pair.Key, pair.Key), rule.Mode, rule.ExpiresOnDay!.Value);
            if (!PlayerStore.Instance.nightLogs.Contains(message)) PlayerStore.Instance.AddNightLog(message);
            categoryReportPending.Add(rule);
        }
    }
}
