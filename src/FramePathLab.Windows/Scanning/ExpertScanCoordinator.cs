using FramePathLab.Core.Evidence;
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
        => ScanAsync(environment, measureInput, measureScheduler, measureNetwork: true, inputDuration, cancellationToken);

    public Task<ExpertScanContext> ScanAsync(
        EnvironmentSnapshot environment,
        bool measureInput,
        bool measureScheduler,
        bool measureNetwork,
        TimeSpan inputDuration,
        CancellationToken cancellationToken = default)
        => Task.Run(
            () => Scan(environment, measureInput, measureScheduler, measureNetwork, inputDuration, cancellationToken),
            cancellationToken);

    private ExpertScanContext Scan(
        EnvironmentSnapshot environment,
        bool measureInput,
        bool measureScheduler,
        bool measureNetwork,
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
        var audio = AudioEndpointScanner.Scan();
        var panel = DisplayEdidScanner.Scan();
        var nvidia = NvidiaProfileScanner.Scan(_gameExecutableName);
        var fastStartup = PlatformStateScanner.ReadFastStartup();
        var (hasAffinityPolicy, affinityObservation) = PlatformStateScanner.ReadInterruptAffinityPolicy();
        var (defenderReadable, defenderPaths, defenderObservation) = PlatformStateScanner.ReadDefenderExclusions();
        var bootTiming = PlatformStateScanner.ReadBootTiming();
        var (nicMsi, nicMsiObservation) = PlatformStateScanner.ReadNetworkInterruptMode();
        var (mitigationsOverridden, mitigationObservation) = PlatformStateScanner.ReadSpeculativeMitigations();
        var (reservedMask, reservedObservation) = PlatformStateScanner.ReadReservedCpuSets();
        var (_, usbControllers, moderatedUsb, usbObservation) = PlatformStateScanner.ReadUsbInterruptModeration();
        var services = ServiceStateScanner.Scan();
        var devices = DeviceInventoryScanner.Scan(audio, network);

        // A week covers enough real idle time for a marginal voltage offset to announce itself.
        var hardwareErrors = HardwareErrorScanner.Scan(TimeSpan.FromDays(7));
        var cpuTuning = CpuTuningAdvisor.Build(cpu, hardwareErrors, (long)(Environment.TickCount64 / 1000));

        var networkPath = measureNetwork
            ? NetworkPathProbe.Measure(cancellationToken: cancellationToken)
            : new NetworkPathQuality(false, "not measured", 0, 0, 0, 0, 0, 0,
                "The local network path was not measured for this scan.");

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
            msiObservation,
            audio,
            networkPath,
            panel,
            nvidia,
            fastStartup,
            hasAffinityPolicy,
            affinityObservation,
            defenderReadable,
            defenderPaths,
            defenderObservation,
            environment.ObservedOptionalApplications.Count > 0,
            bootTiming,
            nicMsi,
            nicMsiObservation,
            mitigationsOverridden,
            mitigationObservation,
            cpuTuning,
            reservedMask,
            reservedObservation,
            services,
            devices,
            usbControllers,
            moderatedUsb,
            usbObservation);
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
