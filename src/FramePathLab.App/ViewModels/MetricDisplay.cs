using System.Globalization;
using FramePathLab.Core.Models;

namespace FramePathLab.App.ViewModels;

public sealed record MetricDisplay(
    string Label,
    string Value,
    string Availability,
    string Formula)
{
    public static MetricDisplay From(MetricSummary metric)
        => new(
            metric.Label,
            metric.Value.HasValue
                ? metric.Value.Value.ToString("0.###", CultureInfo.InvariantCulture) + " " + metric.Unit
                : "N/A",
            metric.Availability,
            metric.Formula);
}
