using FramePathLab.Core.Models;

namespace FramePathLab.App.ViewModels;

/// <summary>View projection of one firmware tuning control.</summary>
public sealed record TuningControlDisplay(PlatformTuningControl Control)
{
    public string Name => Control.Name;

    public string Location => Control.Location;

    public string Recommendation => Control.Recommendation;

    public string Reasoning => Control.Reasoning;

    public string Availability => Control.AvailableOnThisPart
        ? "Available on this processor"
        : "Locked or absent on this processor";

    public string AvailabilityForeground => Control.AvailableOnThisPart ? "#82E6B1" : "#9FB0C6";

    public string RiskLabel => Control.Risk switch
    {
        TweakRisk.Low => "LOW RISK",
        TweakRisk.Moderate => "MODERATE RISK",
        TweakRisk.High => "HIGH RISK",
        _ => "SECURITY TRADE-OFF"
    };

    public string RiskForeground => Control.Risk switch
    {
        TweakRisk.Low => "#82E6B1",
        TweakRisk.Moderate => "#FFD477",
        _ => "#FF9F9A"
    };
}

/// <summary>View projection of one validation step.</summary>
public sealed record StabilityStepDisplay(StabilityStep Step)
{
    public string Order => $"{Step.Order}";

    public string Name => Step.Name;

    public string Tool => Step.Tool;

    public string Duration => Step.Duration;

    public string WhatItCatches => Step.WhatItCatches;

    public string WhatItMisses => Step.WhatItMisses;

    public string RegionLabel => Step.Region switch
    {
        StabilityRegion.SingleCoreBoost => "SINGLE-CORE BOOST",
        StabilityRegion.IdleAndTransient => "IDLE & TRANSIENT",
        StabilityRegion.AllCoreLoad => "ALL-CORE LOAD",
        _ => "UNKNOWN"
    };

    /// <summary>
    /// The all-core region is coloured as the weak test rather than the strong one, because
    /// treating a passed all-core run as validation is the mistake this sequence exists to prevent.
    /// </summary>
    public string RegionForeground => Step.Region switch
    {
        StabilityRegion.SingleCoreBoost => "#7EDBFF",
        StabilityRegion.IdleAndTransient => "#FFD477",
        _ => "#9FB0C6"
    };
}

/// <summary>The CPU and platform tuning view.</summary>
public sealed record CpuTuningDisplay(CpuTuningState State, CpuTopology Topology)
{
    public string ProcessorBrand => State.ProcessorBrand;

    public string Observation => State.Observation;

    public string TopologySummary
        => $"{Topology.PhysicalCoreCount} cores / {Topology.LogicalProcessorCount} threads"
           + $" · {Topology.CoreGroups.Count} core group(s)"
           + $" · {Topology.LargestCachePerCoreMiB:0.#} MiB last-level cache per core"
           + (Topology.IsHybrid ? " · hybrid" : string.Empty);

    public string ClockSummary
        => Topology.MaxMhz is > 0
            ? $"{Topology.CurrentMhz} MHz now, ceiling {Topology.MhzLimit} MHz of {Topology.MaxMhz} MHz rated"
              + (Topology.IsClockLimited ? " — a limit below the rating is being enforced" : string.Empty)
            : "Processor frequency was not reported";

    public string MultiplierNote => State.MultiplierLocked
        ? "Multiplier locked. The voltage curve is the tuning lever that remains."
        : "Multiplier and curve are both adjustable.";

    public string ErrorHeadline
    {
        get
        {
            var errors = State.HardwareErrors;
            if (!errors.Readable)
            {
                return "Hardware error history unavailable";
            }

            return errors.TotalEvents == 0
                ? "No hardware errors logged"
                : $"{errors.TotalEvents} hardware error(s): {errors.MachineCheckExceptions} uncorrected, "
                  + $"{errors.CorrectedErrors} corrected";
        }
    }

    public string ErrorForeground
    {
        get
        {
            var errors = State.HardwareErrors;
            if (!errors.Readable)
            {
                return "#9FB0C6";
            }

            return errors.HasUncorrectedErrors ? "#FF9F9A" : errors.HasCorrectedErrors ? "#FFD477" : "#82E6B1";
        }
    }

    public string ErrorDetail => State.HardwareErrors.Observation;

    public string UptimeSummary
    {
        get
        {
            var uptime = TimeSpan.FromSeconds(State.SystemUptimeSeconds);
            var text = uptime.TotalDays >= 1
                ? $"{uptime.TotalDays:0.#} days"
                : uptime.TotalHours >= 1
                    ? $"{uptime.TotalHours:0.#} hours"
                    : $"{uptime.TotalMinutes:0} minutes";

            // Uptime is what gives the error count its weight: a clean log after ten minutes
            // says nothing, and after a week it says a great deal.
            return uptime.TotalHours < 4
                ? $"Up {text} — too short for the error count to mean much yet"
                : $"Up {text}";
        }
    }

    public IReadOnlyList<TuningControlDisplay> Controls
        => State.Controls.Select(control => new TuningControlDisplay(control)).ToArray();

    public IReadOnlyList<StabilityStepDisplay> StabilityPlan
        => State.StabilityPlan.Select(step => new StabilityStepDisplay(step)).ToArray();
}
