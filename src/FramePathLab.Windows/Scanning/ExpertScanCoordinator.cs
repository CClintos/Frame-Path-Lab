using System.Diagnostics;
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

        var gameProcessId = ResolveGameProcessId();
        var cpu = CpuTopologyScanner.Scan(gameProcessId);
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

        return new ExpertScanContext(
            environment,
            cpu,
            gpus,
            primary,
            input,
            latency,
            network,
            gameProcessId,
            _gameExecutableName);
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

    private int? ResolveGameProcessId()
    {
        var processes = Process.GetProcessesByName(_gameExecutableName);
        try
        {
            return processes.Length > 0 ? processes[0].Id : null;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
