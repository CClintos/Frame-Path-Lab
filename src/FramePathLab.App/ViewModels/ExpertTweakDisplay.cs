using FramePathLab.Core.Models;

namespace FramePathLab.App.ViewModels;

/// <summary>View projection of one expert tweak, including the exact writes it would perform.</summary>
public sealed record ExpertTweakDisplay(ExpertTweakCard Card)
{
    public string Id => Card.Definition.Id;

    public string Category => Card.Definition.Category;

    public string Title => Card.Definition.Title;

    public string Mechanism => Card.Definition.Mechanism;

    public string Rationale => Card.Definition.Rationale;

    public string Tradeoff => Card.Definition.Tradeoff;

    public string CurrentValue => Card.Reading.CurrentValue;

    public string RecommendedValue => Card.Reading.RecommendedValue;

    public string Detail => Card.Reading.Detail;

    public bool CanApply => Card.CanApply;

    public string BlockedReason => Card.BlockedReason ?? string.Empty;

    public bool HasBlockedReason => Card.BlockedReason is not null;

    public string DispositionReason => Card.Definition.DispositionReason;

    public string DispositionLabel => Card.Definition.Disposition switch
    {
        TweakDisposition.RecommendDefault => "DEFAULT RECOMMENDATION",
        TweakDisposition.OptInExperiment => "A/B EXPERIMENT",
        TweakDisposition.GuidedAction => "GUIDED ACTION",
        TweakDisposition.DiagnosticOnly => "DIAGNOSTIC ONLY",
        TweakDisposition.Excluded => "EXCLUDED",
        _ => "UNCLASSIFIED"
    };

    public string StateLabel => Card.Definition.Disposition switch
    {
        TweakDisposition.Excluded => "EXCLUDED",
        TweakDisposition.DiagnosticOnly => "DIAGNOSTIC",
        TweakDisposition.GuidedAction => "CHECK MANUALLY",
        TweakDisposition.OptInExperiment when Card.Reading.State == TweakState.Suboptimal => "EXPERIMENT AVAILABLE",
        _ => Card.Reading.State switch
        {
            TweakState.Suboptimal => "CHANGE AVAILABLE",
            TweakState.Optimal => "ALREADY SET",
            TweakState.Unknown => "NOT READABLE",
            TweakState.NotApplicable => "NOT APPLICABLE",
            TweakState.Blocked => "BLOCKED",
            _ => "UNKNOWN"
        }
    };

    public string StateForeground => Card.Definition.Disposition switch
    {
        TweakDisposition.Excluded => "#FF9F9A",
        TweakDisposition.DiagnosticOnly or TweakDisposition.GuidedAction => "#9FB0C6",
        _ => Card.Reading.State switch
        {
            TweakState.Suboptimal => "#7EDBFF",
            TweakState.Optimal => "#82E6B1",
            TweakState.Blocked => "#FFD477",
            _ => "#9FB0C6"
        }
    };

    public string StateBackground => Card.Definition.Disposition switch
    {
        TweakDisposition.Excluded => "#401E22",
        TweakDisposition.DiagnosticOnly or TweakDisposition.GuidedAction => "#212C3B",
        _ => Card.Reading.State switch
        {
            TweakState.Suboptimal => "#12374A",
            TweakState.Optimal => "#12382A",
            TweakState.Blocked => "#3B2E13",
            _ => "#212C3B"
        }
    };

    public string RiskLabel => Card.Definition.Risk switch
    {
        TweakRisk.Low => "LOW RISK",
        TweakRisk.Moderate => "MODERATE RISK",
        TweakRisk.High => "HIGH RISK · REBOOT",
        TweakRisk.SecurityTradeOff => "SECURITY TRADE-OFF",
        _ => "UNRATED"
    };

    public string RiskForeground => Card.Definition.Risk switch
    {
        TweakRisk.Low => "#82E6B1",
        TweakRisk.Moderate => "#FFD477",
        _ => "#FF9F9A"
    };

    /// <summary>
    /// The exact writes, shown before the user commits. An expert audience should be able to read
    /// what the tool is about to do rather than trust a label.
    /// </summary>
    public string PlanText => Card.Plan.Count == 0
        ? "No system change is offered for this item."
        : string.Join(
            Environment.NewLine,
            Card.Plan.Select(plan => $"{plan.Target}  ▸  {plan.ValueName} = {plan.DesiredValue}"));

    public string Requirements
    {
        get
        {
            var parts = new List<string>();
            if (Card.Definition.RequiresElevation)
            {
                parts.Add("administrator");
            }

            if (Card.Definition.RequiresReboot)
            {
                parts.Add("restart");
            }

            if (Card.Definition.RequiresGameRestart)
            {
                parts.Add("game restart");
            }

            return parts.Count == 0 ? "Takes effect immediately" : "Requires " + string.Join(" + ", parts);
        }
    }
}

/// <summary>View projection of one recorded transaction that can still be reversed.</summary>
public sealed record ExpertTransactionDisplay(TweakTransaction Transaction)
{
    public Guid TransactionId => Transaction.TransactionId;

    public string Title => Transaction.TweakTitle;

    public string AppliedAt => Transaction.AppliedAtUtc.ToLocalTime().ToString("g");

    public string State => Transaction.State;

    public string Observation => Transaction.LastObservation;

    public string Detail => string.Join(
        Environment.NewLine,
        Transaction.Mutations.Select(record =>
            $"{record.ValueName}: {record.BeforeValue ?? "absent"} → {record.ObservedAfterValue ?? "absent"}"));

    public bool CanRevert => Transaction.IsOutstanding;
}
