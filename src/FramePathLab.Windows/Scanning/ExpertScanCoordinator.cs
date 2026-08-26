using FramePathLab.Core.Models;
using FramePathLab.Windows.Input;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Gathers every expert-tier reading into one context.
///
/// The input and scheduler probes are opt-in because they take real wall-clock time and the input
/// probe needs the user to be moving the mouse. A scan without them still produces a complete
/// context; the affected cards simply report Unknown rather than guessing.
/// </summary>
public sealed class ExpertScanCoordinator
{
    private readonly string _gameExecutableName;

    public ExpertScanCoordinator(string gameExecutableName = "cs2")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameExecutableName);
        _gameExecutableName = gameExecutableName;
    }

    public Task<ExpertScanContext> ScanAsync(
        EnvironmentSnapshot environment,
        bool measureInput,
        bool measureScheduler,
        TimeSpan inputDuration,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => Scan(environment, measureInput, measureScheduler, inputDuration, cancellationToken),
            cancellationToken);

    private ExpertScanContext Scan(
        EnvironmentSnapshot environment,
        bool measureInput,
        bool measureScheduler,
        TimeSpan inputDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        // The scanner deliberately does not open or inspect the running game process. Topology is
        // resolved against the current process/system mask only; game affinity/EcoQoS are excluded.
        int? gameProcessId = null;
        var cpu = CpuTopologyScanner.Scan(null);
        var timings = DisplayTimingScanner.Scan();
        var adapters = environment.Displays.Select(display => display.AdapterDescription).ToArray();
        var gpus = GpuTelemetryScanner.Scan(adapters);
        var network = NetworkAdapterScanner.Scan();

        var latency = measureScheduler
            ? SystemLatencyProbe.Measure(cancellationToken: cancellationToken)
            : null;

        var input = measureInput
            ? InputChainProbe.Measure(inputDuration, cancellationToken)
            : BuildUnmeasuredInputReport();

        var primary = timings.FirstOrDefault(timing => timing.IsPrimary) ?? timings.FirstOrDefault();
        var memory = SmbiosMemoryScanner.Scan();
        var steam = PlatformStateScanner.ReadSteamActivity();
        var (forcedClock, counterFrequency) = PlatformStateScanner.ReadPlatformTimer();
        bool? msiEnabled = null;
        string? msiPath = null;
        const string msiObservation =
            "Display-adapter interrupt registry state is not inferred; use ETW for DPC/ISR diagnosis.";

        return new ExpertScanContext(
            environment,
            cpu,
            gpus,
            primary,
            input,
            latency,
            network,
            gameProcessId,
            _gameExecutableName,
            memory,
            steam,
            forcedClock,
            counterFrequency,
            msiEnabled,
            msiPath,
            msiObservation);
    }

    /// <summary>
    /// Pointer behaviour is a cheap registry-backed read, so acceleration and speed are reported
    /// even when the timed report-rate measurement was not run.
    /// </summary>
    private static InputChainReport BuildUnmeasuredInputReport()
    {
        var (acceleration, speed) = InputChainProbe.ReadPointerBehaviour();
        return new InputChainReport(
            false, 0, 0, 0, 0, 0, 0, 0, 0,
            acceleration,
            speed,
            "Not measured",
            "Report-rate measurement was not run for this scan.");
    }

}
