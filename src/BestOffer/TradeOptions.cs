using System;

namespace DeepSpaceBestOffer;

public enum OverBudgetMode { Percent, Credits }

/// <summary>Persisted user choices. Captures hold a clone, never the live UI instance.</summary>
public sealed class TradeOptions
{
    public bool IncludeShowcase { get; set; } = true;
    public bool IncludeHiddenStorage { get; set; } = true;
    public bool IncludeContainerContents { get; set; }
    // Opt-in defaults preserve behavior when loading settings from 1.4.0.
    public bool BlockContraband { get; set; }
    public bool BlockStolen { get; set; }
    public bool AllowContrabandDemandExceptions { get; set; }
    public bool AllowStolenDemandExceptions { get; set; }
    public bool AllowOverBudget { get; set; }
    public OverBudgetMode OverBudgetMode { get; set; } = OverBudgetMode.Percent;
    public decimal OverBudgetValue { get; set; } = 10m;
    public string PanelHotkey { get; set; } = "F8";
    public string AuditHotkey { get; set; } = "F9";
    public float ToolbarX { get; set; } = -1;
    public float ToolbarY { get; set; } = 18;

    public TradeOptions Clone() => (TradeOptions)MemberwiseClone();

    public bool TryGetSelectionBudget(long budget, out long limit, out string error)
    {
        limit = budget;
        error = "";
        if (budget < 0) { error = "买家预算无效。"; return false; }
        if (!AllowOverBudget) return true;
        if (!Enum.IsDefined(typeof(OverBudgetMode), OverBudgetMode) || OverBudgetValue < 0)
        { error = "允许超出的数值必须为非负数，且单位有效。"; return false; }
        try
        {
            decimal extra = OverBudgetMode == OverBudgetMode.Percent
                ? checked((decimal)budget * OverBudgetValue / 100m)
                : OverBudgetValue;
            // Game currency is integral. Round the allowance down so a fractional
            // percentage can never authorize more unpaid value than the user set.
            decimal ceiling = checked((decimal)budget + decimal.Floor(extra));
            if (ceiling > long.MaxValue) { error = "允许超出的数值过大，请调小。"; return false; }
            limit = (long)ceiling;
            return true;
        }
        catch (OverflowException) { error = "允许超出的数值过大，请调小。"; return false; }
    }
}
