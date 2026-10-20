using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceBestOffer;

/// <summary>One category choice, stored separately for each native run ID.</summary>
public sealed class CategorySaleRule
{
    public int Mode { get; set; }
    public bool Limited { get; set; }
    public int DurationDays { get; set; } = 3;
    public int StartsOnDay { get; set; }
    public int? ExpiresOnDay { get; set; }
    public bool AllowDemandException { get; set; }
    public bool ExpiryNotified { get; set; }

    public bool IsExpired(int day) => Mode is 1 or 2 && Limited && ExpiresOnDay.HasValue && day >= ExpiresOnDay.Value;
    public int EffectiveMode(int day) => day > 0 && day >= StartsOnDay && !IsExpired(day) ? Mode : 0;

    public void SetMode(int mode, int day)
    {
        if (mode is not (0 or 1 or 2)) throw new ArgumentOutOfRangeException(nameof(mode));
        ValidateDay(day);
        bool keepPeriod = Limited && ExpiresOnDay.HasValue && day >= StartsOnDay && day < ExpiresOnDay.Value;
        Mode = mode;
        if (keepPeriod) { StartsOnDay = day; ExpiryNotified = false; }
        else Reschedule(day);
    }

    public void CycleForbiddenWithDemandException(int day)
    {
        ValidateDay(day);
        if (EffectiveMode(day) != 2)
        {
            SetMode(2, day);
            AllowDemandException = false;
        }
        else if (!AllowDemandException)
        {
            // Relaxing an active ban must keep its original expiry date.
            AllowDemandException = true;
        }
        else
        {
            SetMode(0, day);
            AllowDemandException = false;
        }
    }

    public void SetPeriod(bool limited, int days, int day)
    {
        if (days < 1 || days > CategoryRuleLogic.MaxDurationDays) throw new ArgumentOutOfRangeException(nameof(days));
        ValidateDay(day);
        // Editing an expired timer must not silently reactivate its former
        // required/forbidden choice, which the UI now correctly shows unchecked.
        if (IsExpired(day)) Mode = 0;
        Limited = limited;
        DurationDays = days;
        Reschedule(day);
    }

    public bool RemovePeriod(int day)
    {
        if (!Limited && !ExpiresOnDay.HasValue) return false;
        ValidateDay(day);
        // Removing a retired risk timer must not revive a rule that expired.
        if (IsExpired(day)) Mode = 0;
        Limited = false;
        ExpiresOnDay = null;
        ExpiryNotified = false;
        return true;
    }

    void Reschedule(int day)
    {
        StartsOnDay = day;
        ExpiresOnDay = Limited ? checked(day + DurationDays) : null;
        ExpiryNotified = false;
    }

    static void ValidateDay(int day)
    {
        if (day < 1 || day > int.MaxValue - CategoryRuleLogic.MaxDurationDays)
            throw new ArgumentOutOfRangeException(nameof(day));
    }
}

public sealed class CategoryRuleBook
{
    public Dictionary<string, Dictionary<string, CategorySaleRule>> Runs { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Immutable choices copied into a capture; expiry is rechecked against its actual game day.</summary>
public sealed record CategoryRuleSelection(string Category, int Mode, bool AllowDemandException,
    int StartsOnDay = 0, int? ExpiresOnDay = null);

public static class CategoryRuleLogic
{
    public const string Contraband = "risk:contraband", Stolen = "risk:stolen";
    public const int MaxDurationDays = 999;

    public static bool IsCategoryId(string id) => id == Contraband || id == Stolen ||
        (id.StartsWith("type:", StringComparison.Ordinal) && id.Length > 5);

    public static int Resolve(IEnumerable<CategoryRuleSelection> choices, Func<string, bool> matches,
        bool hasExplicitDemand, int day)
    {
        int result = 0;
        foreach (var choice in choices)
        {
            if (choice.Mode is not (1 or 2) || day < choice.StartsOnDay ||
                choice.ExpiresOnDay.HasValue && day >= choice.ExpiresOnDay.Value || !matches(choice.Category)) continue;
            if (choice.Mode == 2 && !(choice.AllowDemandException && hasExplicitDemand)) return 2;
            if (choice.Mode == 1) result = 1;
        }
        return result;
    }

    // A whole-container sale cannot bypass a category ban on its contents.
    // Category choices take precedence over all inventory choices for that sale.
    public static int EffectiveRule(IEnumerable<int> categoryModes, IEnumerable<int> inventoryModes)
    {
        int category = 0;
        foreach (int mode in categoryModes)
        {
            if (mode == 2) return 2;
            if (mode == 1) category = 1;
        }
        return category != 0 ? category : inventoryModes.Aggregate(0, (mode, own) => mode | own);
    }

    public static string PeriodDescription(CategorySaleRule rule, int day)
    {
        if (!rule.Limited) return "不限期";
        return ExpiryDescription(rule.ExpiresOnDay ?? checked(day + rule.DurationDays), day);
    }

    public static string ExpiryDescription(int expiresOnDay, int day)
    {
        string relative = (expiresOnDay - day) switch
        {
            <= 0 => "已到期",
            1 => "明天到期",
            2 => "后天到期",
            var remaining => $"{remaining} 天后到期"
        };
        return $"{relative}（第 {expiresOnDay} 天）";
    }

    public static int PeriodDaysForEditing(CategorySaleRule rule, int day) =>
        rule.Limited && rule.ExpiresOnDay.HasValue && day >= rule.StartsOnDay
            ? Math.Clamp(rule.ExpiresOnDay.Value - day, 0, MaxDurationDays)
            : rule.DurationDays;

    public static string ExpiryMessage(string categoryName, int mode, int expiredDay) => mode == 2
        ? $"「{categoryName}」的分类禁卖已到期（第 {expiredDay} 天）。今日起最佳报价可交易此分类，库存规则仍然生效。"
        : $"「{categoryName}」的分类必卖已到期（第 {expiredDay} 天）。今日起恢复按库存规则选货，不再强制出售此分类。";
}
