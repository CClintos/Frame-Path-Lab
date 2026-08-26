using System.Globalization;
using System.Security.Cryptography;
using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;
using FramePathLab.Core.Statistics;

namespace FramePathLab.Core.Analysis;

public sealed class PresentMonCsvAnalyzer : ICaptureAnalyzer
{
    public const string SchemaVersion = "presentmon-import-v2";

    private static readonly string[] FrameTimeCandidates =
    [
        "FrameTime",
        "MsBetweenPresents",
        "msBetweenPresents"
    ];

    public async Task<CaptureAnalysis> AnalyzeAsync(
        string path,
        CaptureAnalysisOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("Capture file was not found.", path);
        }

        if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException("Reparse-point and symbolic-link capture paths are not accepted.");
        }

        if (file.Length == 0)
        {
            throw new InvalidDataException("Capture file is empty.");
        }

        if (file.Length > options.MaximumFileBytes)
        {
            throw new InvalidDataException($"Capture exceeds the {options.MaximumFileBytes:N0}-byte limit.");
        }

        await using var stream = new FileStream(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65_536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        // Hash and analyze the same open handle. Hashing by path and reopening allowed the file to
        // be replaced between provenance capture and analysis.
        var hash = Convert.ToHexStringLower(
            await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        stream.Position = 0;
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Capture does not contain a header row.");
        var headers = BoundedCsvReader.ParseLine(
            headerLine,
            options.MaximumColumns,
            options.MaximumCellCharacters);
        var headerMap = headers
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        var frameTimeColumn = FrameTimeCandidates.FirstOrDefault(headerMap.ContainsKey)
            ?? throw new InvalidDataException(
                $"No supported frame-time column was found. Expected one of: {string.Join(", ", FrameTimeCandidates)}.");

        var applicationIndex = FindIndex(headerMap, "Application");
        var frameTimeIndex = headerMap[frameTimeColumn];
        var cpuBusyIndex = FindIndex(headerMap, "CPUBusy", "MsCPUBusy");
        var gpuBusyIndex = FindIndex(headerMap, "GPUBusy", "MsGPUBusy", "GPUTime");
        var presentModeIndex = FindIndex(headerMap, "PresentMode");
        var allowsTearingIndex = FindIndex(headerMap, "AllowsTearing");
        var syncIntervalIndex = FindIndex(headerMap, "SyncInterval");
        var droppedIndex = FindIndex(headerMap, "Dropped", "AllowsTearingDropped");
        var displayedTimeIndex = FindIndex(headerMap, "DisplayedTime");
        var untilDisplayedIndex = FindIndex(headerMap, "MsUntilDisplayed", "UntilDisplayed");
        var renderPresentIndex = FindIndex(headerMap, "MsRenderPresentLatency", "RenderPresentLatency");

        var all = new CaptureAccumulator();
        var cs2 = new CaptureAccumulator();
        var applicationCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long totalRows = 0;
        long rejectedRows = 0;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            totalRows++;
            if (totalRows > options.MaximumRows)
            {
                throw new InvalidDataException($"Capture exceeds the {options.MaximumRows:N0}-row limit.");
            }

            IReadOnlyList<string> cells;
            try
            {
                cells = BoundedCsvReader.ParseLine(
                    line,
                    options.MaximumColumns,
                    options.MaximumCellCharacters);
            }
            catch (InvalidDataException)
            {
                rejectedRows++;
                continue;
            }

            if (frameTimeIndex >= cells.Count
                || !TryParseFinitePositive(cells[frameTimeIndex], out var frameTime)
                || frameTime > 10_000)
            {
                rejectedRows++;
                continue;
            }

            var application = applicationIndex >= 0 && applicationIndex < cells.Count
                ? NormalizeApplication(cells[applicationIndex])
                : "unspecified";
            applicationCounts[application] = applicationCounts.GetValueOrDefault(application) + 1;

            var cpuBusy = ReadOptionalDouble(cells, cpuBusyIndex);
            var gpuBusy = ReadOptionalDouble(cells, gpuBusyIndex);
            var presentMode = ReadOptionalText(cells, presentModeIndex);
            var allowsTearing = ReadOptionalBoolean(cells, allowsTearingIndex);
            var dropped = ReadOptionalBoolean(cells, droppedIndex)
                          ?? ReadDroppedFromDisplayedTime(cells, displayedTimeIndex);
            var delivery = new DeliverySample(
                ReadOptionalDouble(cells, syncIntervalIndex),
                dropped,
                ReadOptionalDouble(cells, untilDisplayedIndex),
                ReadOptionalDouble(cells, renderPresentIndex));
            all.Add(frameTime, cpuBusy, gpuBusy, presentMode, allowsTearing, delivery);
            if (IsCs2(application))
            {
                cs2.Add(frameTime, cpuBusy, gpuBusy, presentMode, allowsTearing, delivery);
            }
        }

        if (all.FrameTimes.Count == 0)
        {
            return Invalid(file, hash, frameTimeColumn, totalRows, rejectedRows,
                "No valid positive frame-time rows were found.");
        }

        CaptureAccumulator selected;
        string selectedApplication;
        var warnings = new List<string>();
        if (cs2.FrameTimes.Count > 0)
        {
            selected = cs2;
            selectedApplication = "cs2.exe";
            if (all.FrameTimes.Count != cs2.FrameTimes.Count)
            {
                warnings.Add("Rows for other applications were ignored; only CS2 rows were analyzed.");
            }
        }
        else if (applicationCounts.Count <= 1)
        {
            selected = all;
            selectedApplication = applicationCounts.Keys.FirstOrDefault() ?? "unspecified";
            warnings.Add("The capture did not identify CS2. Results are an observational single-application import.");
        }
        else
        {
            return Invalid(file, hash, frameTimeColumn, totalRows, rejectedRows,
                "Capture contains multiple applications and no CS2 rows; target identity is ambiguous.");
        }

        if (selected.FrameTimes.Count < 120)
        {
            warnings.Add("Fewer than 120 valid frames were available; tail metrics are descriptive only.");
        }

        if (rejectedRows > 0)
        {
            warnings.Add($"{rejectedRows:N0} malformed or invalid rows were rejected and retained in the quality summary.");
        }

        warnings.Add("One imported capture is a run summary only. It cannot produce a causal Keep/Revert decision.");
        warnings.Add("Software timing fields are not a physical mouse-to-photon measurement.");

        var metrics = BuildMetrics(selected, options.FrameBudgetMs);
        return new CaptureAnalysis(
            DateTimeOffset.UtcNow,
            file.Name,
            hash,
            file.Length,
            SchemaVersion,
            selectedApplication,
            frameTimeColumn,
            totalRows,
            selected.FrameTimes.Count,
            rejectedRows,
            ResultOutcome.BaselineOnly,
            metrics,
            new Dictionary<string, long>(selected.PresentModes, StringComparer.OrdinalIgnoreCase),
            warnings,
            FrameDeliveryAnalyzer.Analyze(
                selected.PresentModes,
                selected.FrameTimes,
                selected.CpuBusy,
                selected.GpuBusy,
                selected.SyncIntervals,
                selected.UntilDisplayed,
                selected.RenderPresentLatency,
                selected.DroppedPresents));
    }

    private static IReadOnlyList<MetricSummary> BuildMetrics(
        CaptureAccumulator selected,
        double? frameBudgetMs)
    {
        var sorted = selected.FrameTimes.Order().ToArray();
        var mean = DescriptiveStatistics.Mean(sorted);
        var metrics = new List<MetricSummary>
        {
            new("frames", "Valid frames", sorted.Length, "frames", "Count of accepted target rows.", "Available"),
            new("mean_frame_ms", "Mean frame time", mean, "ms", "Arithmetic mean over accepted rows.", "Available"),
            new("median_frame_ms", "Median frame time", DescriptiveStatistics.QuantileR7(sorted, 0.5), "ms", "R-7 50th percentile.", "Available"),
            new("p95_frame_ms", "P95 frame time", DescriptiveStatistics.QuantileR7(sorted, 0.95), "ms", "R-7 95th percentile.", "Available"),
            new("p99_frame_ms", "P99 frame time", DescriptiveStatistics.QuantileR7(sorted, 0.99), "ms", "R-7 99th percentile.", "Available"),
            new("p999_frame_ms", "P99.9 frame time", sorted.Length >= 10_000 ? DescriptiveStatistics.QuantileR7(sorted, 0.999) : null, "ms", "R-7 99.9th percentile; suppressed below 10,000 frames.", sorted.Length >= 10_000 ? "Available" : "Suppressed: fewer than 10,000 frames"),
            new("mean_fps", "Mean frame-rate equivalent", 1000d / mean, "FPS", "1000 divided by mean frame time; not an end-to-end latency metric.", "Available"),
            new("frame_stddev", "Frame-time standard deviation", DescriptiveStatistics.SampleStandardDeviation(sorted), "ms", "Sample standard deviation over accepted rows.", "Available")
        };

        if (frameBudgetMs is > 0)
        {
            var overBudget = sorted.Count(value => value > frameBudgetMs.Value);
            metrics.Add(new MetricSummary(
                "over_budget_pct",
                $"Frames over {frameBudgetMs.Value:0.###} ms",
                100d * overBudget / sorted.Length,
                "%",
                "Accepted target frames above the explicitly selected frame budget.",
                "Available"));
        }

        AddOptionalMedian(metrics, "cpu_busy_median", "Median CPU busy", selected.CpuBusy, "ms");
        AddOptionalMedian(metrics, "gpu_busy_median", "Median GPU busy", selected.GpuBusy, "ms");
        if (selected.AllowsTearingObserved > 0)
        {
            metrics.Add(new MetricSummary(
                "allows_tearing_pct",
                "Rows allowing tearing",
                100d * selected.AllowsTearingTrue / selected.AllowsTearingObserved,
                "%",
                "Share of rows whose collector field reported AllowsTearing=true. This does not prove VRR engagement.",
                "Available"));
        }

        return metrics;
    }

    private static void AddOptionalMedian(
        ICollection<MetricSummary> metrics,
        string id,
        string label,
        IReadOnlyCollection<double> values,
        string unit)
    {
        if (values.Count == 0)
        {
            metrics.Add(new MetricSummary(id, label, null, unit, "Median of available collector rows.", "Unavailable in source"));
            return;
        }

        var sorted = values.Order().ToArray();
        metrics.Add(new MetricSummary(
            id,
            label,
            DescriptiveStatistics.QuantileR7(sorted, 0.5),
            unit,
            "R-7 median over available collector rows.",
            "Available"));
    }

    private static CaptureAnalysis Invalid(
        FileInfo file,
        string hash,
        string frameTimeColumn,
        long totalRows,
        long rejectedRows,
        string warning)
        => new(
            DateTimeOffset.UtcNow,
            file.Name,
            hash,
            file.Length,
            SchemaVersion,
            "unresolved",
            frameTimeColumn,
            totalRows,
            0,
            rejectedRows,
            ResultOutcome.Invalid,
            [],
            new Dictionary<string, long>(),
            [warning]);

    private static int FindIndex(IReadOnlyDictionary<string, int> map, params string[] names)
    {
        foreach (var name in names)
        {
            if (map.TryGetValue(name, out var index))
            {
                return index;
            }
        }

        return -1;
    }

    private static string ReadOptionalText(IReadOnlyList<string> cells, int index)
        => index >= 0 && index < cells.Count ? cells[index].Trim() : string.Empty;

    private static double? ReadOptionalDouble(IReadOnlyList<string> cells, int index)
        => index >= 0 && index < cells.Count && TryParseFiniteNonNegative(cells[index], out var value)
            ? value
            : null;

    private static bool? ReadOptionalBoolean(IReadOnlyList<string> cells, int index)
    {
        if (index < 0 || index >= cells.Count)
        {
            return null;
        }

        var text = cells[index].Trim();
        if (bool.TryParse(text, out var boolean))
        {
            return boolean;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer != 0;
        }

        return null;
    }

    private static bool? ReadDroppedFromDisplayedTime(IReadOnlyList<string> cells, int index)
    {
        if (index < 0 || index >= cells.Count)
        {
            return null;
        }

        var text = cells[index].Trim();
        if (text.Equals("NA", StringComparison.OrdinalIgnoreCase)
            || text.Equals("N/A", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryParseFiniteNonNegative(text, out _) ? false : null;
    }

    private static bool TryParseFinitePositive(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           && double.IsFinite(value)
           && value > 0;

    private static bool TryParseFiniteNonNegative(string text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
           && double.IsFinite(value)
           && value >= 0;

    private static string NormalizeApplication(string application)
    {
        var trimmed = application.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "unspecified";
        }

        return Path.GetFileName(trimmed);
    }

    private static bool IsCs2(string application)
        => application.Equals("cs2.exe", StringComparison.OrdinalIgnoreCase)
           || application.Equals("cs2", StringComparison.OrdinalIgnoreCase);

    /// <summary>Optional per-row delivery fields, absent in older collector schemas.</summary>
    private readonly record struct DeliverySample(
        double? SyncInterval,
        bool? Dropped,
        double? UntilDisplayed,
        double? RenderPresentLatency);

    private sealed class CaptureAccumulator
    {
        public List<double> FrameTimes { get; } = [];

        public List<double> CpuBusy { get; } = [];

        public List<double> GpuBusy { get; } = [];

        public Dictionary<string, long> PresentModes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public long AllowsTearingObserved { get; private set; }

        public long AllowsTearingTrue { get; private set; }

        public List<double> SyncIntervals { get; } = [];

        public List<double> UntilDisplayed { get; } = [];

        public List<double> RenderPresentLatency { get; } = [];

        public long DroppedPresents { get; private set; }

        public void Add(
            double frameTime,
            double? cpuBusy,
            double? gpuBusy,
            string presentMode,
            bool? allowsTearing,
            DeliverySample delivery)
        {
            FrameTimes.Add(frameTime);
            if (delivery.SyncInterval.HasValue)
            {
                SyncIntervals.Add(delivery.SyncInterval.Value);
            }

            if (delivery.UntilDisplayed is > 0)
            {
                UntilDisplayed.Add(delivery.UntilDisplayed.Value);
            }

            if (delivery.RenderPresentLatency is > 0)
            {
                RenderPresentLatency.Add(delivery.RenderPresentLatency.Value);
            }

            if (delivery.Dropped == true)
            {
                DroppedPresents++;
            }

            if (cpuBusy.HasValue)
            {
                CpuBusy.Add(cpuBusy.Value);
            }

            if (gpuBusy.HasValue)
            {
                GpuBusy.Add(gpuBusy.Value);
            }

            if (!string.IsNullOrWhiteSpace(presentMode))
            {
                PresentModes[presentMode] = PresentModes.GetValueOrDefault(presentMode) + 1;
            }

            if (allowsTearing.HasValue)
            {
                AllowsTearingObserved++;
                if (allowsTearing.Value)
                {
                    AllowsTearingTrue++;
                }
            }
        }
    }
}
