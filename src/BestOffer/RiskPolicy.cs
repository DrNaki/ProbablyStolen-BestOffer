using System;
using System.Collections.Generic;

namespace DeepSpaceBestOffer;

[Flags]
public enum RiskCategory { None = 0, Contraband = 1, Stolen = 2 }

/// <summary>Public native risk tags and this particular item's explicit demand match.</summary>
public readonly record struct RiskItemFacts(bool IsContraband, bool IsStolen, bool HasExplicitDemand);

/// <summary>
/// Additional selection restrictions. A demand exception removes only this
/// automatic restriction; the buyer's explicit refusal and manual bans still apply.
/// No hidden ItemFeature values are needed to determine either risk category.
/// </summary>
public static class RiskPolicy
{
    public static RiskCategory GetBlockedCategories(TradeOptions options,
        bool isContraband, bool isStolen, bool hasExplicitDemand, bool buyerRefusesRiskyGoods = false)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        // A buyer explicitly refusing contraband must never receive stolen or
        // contraband goods through automation, even when a requested category
        // matches or the optional automatic protections are switched off.
        if (buyerRefusesRiskyGoods)
            return (isContraband ? RiskCategory.Contraband : RiskCategory.None) |
                (isStolen ? RiskCategory.Stolen : RiskCategory.None);
        var blocked = RiskCategory.None;
        if (options.BlockContraband && isContraband && !(options.AllowContrabandDemandExceptions && hasExplicitDemand))
            blocked |= RiskCategory.Contraband;
        if (options.BlockStolen && isStolen && !(options.AllowStolenDemandExceptions && hasExplicitDemand))
            blocked |= RiskCategory.Stolen;
        return blocked;
    }

    /// <summary>
    /// Every component sold with a container is checked separately. Demand for
    /// the box never grants an exception to a protected item hidden inside it.
    /// The caller must use a generic UI reason for an inaccessible component.
    /// </summary>
    public static RiskCategory GetBundleBlockedCategories(TradeOptions options,
        IEnumerable<RiskItemFacts> components, bool buyerRefusesRiskyGoods = false)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (components == null) throw new ArgumentNullException(nameof(components));
        var blocked = RiskCategory.None;
        foreach (var item in components)
            blocked |= GetBlockedCategories(options, item.IsContraband, item.IsStolen,
                item.HasExplicitDemand, buyerRefusesRiskyGoods);
        return blocked;
    }

    /// <summary>
    /// Match the native client's requested IDs or requested tags, using the
    /// native IsTag method supplied by the adapter. Generic acceptance tokens
    /// are deliberately excluded; willingness to buy is not a specific demand.
    /// The client's price-feature list and contraband-acceptance override are
    /// not demand signals and must not be passed into this function.
    /// </summary>
    public static bool MatchesExplicitDemand(string? itemIdentifier,
        IEnumerable<string>? buyingIds, IEnumerable<string>? buyingTags,
        Func<string, bool>? itemHasTag)
    {
        if (!string.IsNullOrEmpty(itemIdentifier) && buyingIds != null)
            foreach (var id in buyingIds)
                if (!string.IsNullOrEmpty(id) && string.Equals(id, itemIdentifier, StringComparison.Ordinal))
                    return true;
        if (buyingTags != null && itemHasTag != null)
            foreach (var tag in buyingTags)
                if (!string.IsNullOrEmpty(tag) && !IsGenericAcceptanceTag(tag) && itemHasTag(tag))
                    return true;
        return false;
    }

    public static bool IsGenericAcceptanceTag(string? tag) =>
        string.Equals(tag, "ALL_TYPE", StringComparison.Ordinal) ||
        string.Equals(tag, "ALL_TYPE_EXCEPT_CONTRABAND", StringComparison.Ordinal);
}
