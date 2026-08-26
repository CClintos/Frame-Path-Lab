using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;
using FramePathLab.Core.Reporting;
using FramePathLab.Core.Services;
using FramePathLab.Windows.Scanning;

namespace FramePathLab.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ScanCoordinator _scanCoordinator;
    private readonly ICaptureAnalyzer _captureAnalyzer;
    private readonly IHistoryStore _historyStore;
    private readonly MarkdownReportWriter _reportWriter;
    private readonly PowerSessionCoordinator _powerSessionCoordinator;
    private readonly ExpertTweakEngine _expertEngine;
    private readonly ExpertScanCoordinator _expertScanCoordinator;
    private readonly long _processStartTimeUtcTicks;
    private ExpertScanContext? _expertContext;
    private string _expertSummary = "Run the expert scan to read CPU topology, display timing, GPU state and input delivery.";
    private string _expertHardware = "Not scanned";
    private bool _measureInput = true;
    private ScanReport? _currentScan;
    private CaptureAnalysis? _currentAnalysis;
    private PowerSessionOverview? _powerOverview;
    private string _statusText = "Ready. Scan first, then choose any system change explicitly.";
    private string _scanSummary = "Run a read-only compatibility scan to begin.";
    private string _selectedCapturePath = string.Empty;
    private string _captureSummary = "Import a PresentMon-style CSV for bounded, observational analysis.";
    private string _primaryDisplayText = "Not scanned";
    private string _cs2Text = "Not scanned";
    private string _platformText = "Not scanned";
    private string _powerPlanText = "Power plans have not been inspected.";
    private string _powerSessionText = "No power-plan experiment is active.";
    private string _powerEligibilityText = "Run the startup scan to check eligibility.";
    private string _readinessHeadline = "Scan this PC before you play";
    private string _readinessDetail = "FramePath Lab will separate detected settings from manual checks and excluded tweaks.";
    private string _lastScanText = "Not scanned yet";
    private string _hardwareSummary = "Hardware inventory pending";
    private int _enabledCount;
    private int _actionCount;
    private int _manualCount;
    private int _blockedCount;
    private bool _isBusy;

    public static readonly TimeSpan PowerExperimentDuration = TimeSpan.FromMinutes(15);

    public MainViewModel(
        ScanCoordinator scanCoordinator,
        ICaptureAnalyzer captureAnalyzer,
        IHistoryStore historyStore,
        MarkdownReportWriter reportWriter,
        PowerSessionCoordinator powerSessionCoordinator,
        ExpertTweakEngine expertEngine,
        ExpertScanCoordinator expertScanCoordinator,
        string dataDirectory)
    {
        _scanCoordinator = scanCoordinator;
        _captureAnalyzer = captureAnalyzer;
        _historyStore = historyStore;
        _reportWriter = reportWriter;
        _powerSessionCoordinator = powerSessionCoordinator;
        _expertEngine = expertEngine;
        _expertScanCoordinator = expertScanCoordinator;
        DataDirectory = dataDirectory;
        using (var process = Process.GetCurrentProcess())
        {
            _processStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
        }

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy, SetError);
        AnalyzeCommand = new AsyncRelayCommand(
            AnalyzeAsync,
            () => !IsBusy && !string.IsNullOrWhiteSpace(SelectedCapturePath),
            SetError);
        DeleteHistoryCommand = new AsyncRelayCommand(DeleteHistoryAsync, () => !IsBusy, SetError);
        RefreshHistoryCommand = new AsyncRelayCommand(RefreshHistoryAsync, () => !IsBusy, SetError);
        ExpertScanCommand = new AsyncRelayCommand(ExpertScanAsync, () => !IsBusy, SetError);
        RevertAllExpertCommand = new AsyncRelayCommand(
            RevertAllExpertAsync,
            () => !IsBusy && ExpertTransactions.Any(entry => entry.CanRevert),
            SetError);
    }

    public ObservableCollection<ExpertTweakDisplay> ExpertTweaks { get; } = [];

    public ObservableCollection<ExpertTransactionDisplay> ExpertTransactions { get; } = [];

    public AsyncRelayCommand ExpertScanCommand { get; }

    public AsyncRelayCommand RevertAllExpertCommand { get; }

    public string ExpertSummary
    {
        get => _expertSummary;
        private set => SetProperty(ref _expertSummary, value);
    }

    public string ExpertHardware
    {
        get => _expertHardware;
        private set => SetProperty(ref _expertHardware, value);
    }

    /// <summary>
    /// The report-rate measurement needs the user to keep moving the mouse, so it stays opt-in
    /// rather than silently producing a meaningless reading.
    /// </summary>
    public bool MeasureInput
    {
        get => _measureInput;
        set => SetProperty(ref _measureInput, value);
    }

    public string ExpertScanButtonText => IsBusy ? "Scanning…" : "Run expert scan";

    private async Task ExpertScanAsync()
    {
        IsBusy = true;
        StatusText = MeasureInput
            ? "Reading expert-tier state. Keep moving the mouse for the input measurement…"
            : "Reading expert-tier state…";
        try
        {
            var snapshot = _currentScan?.Snapshot ?? (await _scanCoordinator.RunAsync()).Snapshot;
            _expertContext = await _expertScanCoordinator.ScanAsync(
                snapshot,
                MeasureInput,
                measureScheduler: true,
                TimeSpan.FromSeconds(5));

            RebuildExpertCards();
            StatusText = "Expert scan complete. Every listed change shows the exact value it writes.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildExpertCards()
    {
        if (_expertContext is null)
        {
            return;
        }

        var cards = _expertEngine.Evaluate(_expertContext);
        ExpertTweaks.Clear();
        foreach (var card in cards)
        {
            ExpertTweaks.Add(new ExpertTweakDisplay(card));
        }

        var available = cards.Count(card => card.CanApply);
        var experiments = cards.Count(card => card.Definition.Disposition == TweakDisposition.OptInExperiment);
        var guided = cards.Count(card => card.Definition.Disposition == TweakDisposition.GuidedAction);
        var excluded = cards.Count(card => card.Definition.Disposition == TweakDisposition.Excluded);
        var unreadable = cards.Count(card => card.Reading.State == TweakState.Unknown);
        ExpertSummary = $"{available} benchmark-only change{(available == 1 ? string.Empty : "s")} available · "
                        + $"{experiments} experiments · {guided} guided · {excluded} excluded · "
                        + $"{unreadable} not readable · "
                        + $"{cards.Count} checked in total.";

        var cpu = _expertContext.Cpu;
        var timing = _expertContext.PrimaryTiming;
        ExpertHardware = $"{cpu.Brand} — {cpu.PhysicalCoreCount}C/{cpu.LogicalProcessorCount}T"
                         + $"{(cpu.IsHybrid ? ", hybrid" : string.Empty)}, {cpu.CoreGroups.Count} core group(s)"
                         + (timing is not null
                             ? $" · {timing.ExactRefreshHz:0.###} Hz exact refresh"
                             : string.Empty);

        RefreshExpertTransactions();
    }

    private void RefreshExpertTransactions()
    {
        ExpertTransactions.Clear();
        foreach (var transaction in _expertEngine.AllTransactions()
                     .OrderByDescending(entry => entry.AppliedAtUtc))
        {
            ExpertTransactions.Add(new ExpertTransactionDisplay(transaction));
        }

        RevertAllExpertCommand.RaiseCanExecuteChanged();
    }

    public async Task ApplyExpertTweakAsync(ExpertTweakDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        IsBusy = true;
        StatusText = $"Applying {display.Title} and recording the exact prior value…";
        try
        {
            var transaction = await Task.Run(() => _expertEngine.Apply(display.Card));
            StatusText = transaction.State == TweakTransaction.StateApplied
                ? $"{display.Title}: {transaction.LastObservation}"
                : $"{display.Title} did not fully apply. {transaction.LastObservation}";

            await RefreshExpertContextAfterMutationAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RevertExpertTransactionAsync(Guid transactionId)
    {
        IsBusy = true;
        StatusText = "Restoring the exact recorded prior value…";
        try
        {
            var transaction = await Task.Run(
                () => _expertEngine.Revert(transactionId, "reverted from the expert tab"));
            StatusText = transaction.LastObservation;
            if (_expertContext is not null)
            {
                await RefreshExpertContextAfterMutationAsync();
            }
            else
            {
                RefreshExpertTransactions();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshExpertContextAfterMutationAsync()
    {
        var snapshot = _currentScan?.Snapshot ?? (await _scanCoordinator.RunAsync()).Snapshot;
        _expertContext = await _expertScanCoordinator.ScanAsync(
            snapshot,
            measureInput: false,
            measureScheduler: false,
            TimeSpan.Zero);
        RebuildExpertCards();
    }

    private async Task RevertAllExpertAsync()
    {
        IsBusy = true;
        StatusText = "Reverting every outstanding expert tweak…";
        try
        {
            var reverted = await Task.Run(() => _expertEngine.RevertAll("revert all from the expert tab"));
            var failed = reverted.Count(entry => entry.State != TweakTransaction.StateReverted);
            StatusText = failed == 0
                ? $"Reverted {reverted.Count} transaction(s); every value was restored and verified."
                : $"Reverted {reverted.Count} transaction(s) with {failed} needing review.";

            if (_expertContext is not null)
            {
                RebuildExpertCards();
            }
            else
            {
                RefreshExpertTransactions();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public ObservableCollection<FindingCard> Findings { get; } = [];

    public ObservableCollection<TweakDisplay> Tweaks { get; } = [];

    public ObservableCollection<MetricDisplay> Metrics { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public ObservableCollection<HistoryEntry> History { get; } = [];

    public AsyncRelayCommand ScanCommand { get; }

    public AsyncRelayCommand AnalyzeCommand { get; }

    public AsyncRelayCommand DeleteHistoryCommand { get; }

    public AsyncRelayCommand RefreshHistoryCommand { get; }

    public string DataDirectory { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ScanSummary
    {
        get => _scanSummary;
        private set => SetProperty(ref _scanSummary, value);
    }

    public string SelectedCapturePath
    {
        get => _selectedCapturePath;
        set
        {
            if (SetProperty(ref _selectedCapturePath, value))
            {
                AnalyzeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CaptureSummary
    {
        get => _captureSummary;
        private set => SetProperty(ref _captureSummary, value);
    }

    public string PrimaryDisplayText
    {
        get => _primaryDisplayText;
        private set => SetProperty(ref _primaryDisplayText, value);
    }

    public string Cs2Text
    {
        get => _cs2Text;
        private set => SetProperty(ref _cs2Text, value);
    }

    public string PlatformText
    {
        get => _platformText;
        private set => SetProperty(ref _platformText, value);
    }

    public string PowerPlanText
    {
        get => _powerPlanText;
        private set => SetProperty(ref _powerPlanText, value);
    }

    public string PowerSessionText
    {
        get => _powerSessionText;
        private set => SetProperty(ref _powerSessionText, value);
    }

    public string PowerEligibilityText
    {
        get => _powerEligibilityText;
        private set => SetProperty(ref _powerEligibilityText, value);
    }

    public string ReadinessHeadline
    {
        get => _readinessHeadline;
        private set => SetProperty(ref _readinessHeadline, value);
    }

    public string ReadinessDetail
    {
        get => _readinessDetail;
        private set => SetProperty(ref _readinessDetail, value);
    }

    public string LastScanText
    {
        get => _lastScanText;
        private set => SetProperty(ref _lastScanText, value);
    }

    public string HardwareSummary
    {
        get => _hardwareSummary;
        private set => SetProperty(ref _hardwareSummary, value);
    }

    public int EnabledCount
    {
        get => _enabledCount;
        private set => SetProperty(ref _enabledCount, value);
    }

    public int ActionCount
    {
        get => _actionCount;
        private set => SetProperty(ref _actionCount, value);
    }

    public int ManualCount
    {
        get => _manualCount;
        private set => SetProperty(ref _manualCount, value);
    }

    public int BlockedCount
    {
        get => _blockedCount;
        private set => SetProperty(ref _blockedCount, value);
    }

    public string ScanButtonText => IsBusy ? "Scanning…" : "Scan this PC";

    public bool AreActionsEnabled => !IsBusy;

    public string PowerApprovalSummary
    {
        get
        {
            if (_powerOverview is null)
            {
                return "Power-plan state is unavailable.";
            }

            return $"Current plan: {_powerOverview.ActiveSchemeName} ({_powerOverview.ActiveSchemeId:D})\n"
                + $"Temporary target: High performance ({PowerSessionCoordinator.HighPerformanceSchemeId:D})\n"
                + $"Maximum duration: {PowerExperimentDuration.TotalMinutes:0} minutes";
        }
    }

    public Guid? CurrentPowerSchemeId => _powerOverview?.ActiveSchemeId;

    public bool CanStartPowerSession
        => !IsBusy && IsPowerEligibleForApply;

    public bool CanRestorePowerSession
        => !IsBusy
           && _powerOverview?.HasUnresolvedSession == true
           && _powerOverview.Journal is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ScanCommand.RaiseCanExecuteChanged();
                AnalyzeCommand.RaiseCanExecuteChanged();
                DeleteHistoryCommand.RaiseCanExecuteChanged();
                RefreshHistoryCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(ScanButtonText));
                OnPropertyChanged(nameof(ExpertScanButtonText));
                OnPropertyChanged(nameof(AreActionsEnabled));
                ExpertScanCommand.RaiseCanExecuteChanged();
                RevertAllExpertCommand.RaiseCanExecuteChanged();
                NotifyPowerCommandState();
            }
        }
    }

    public bool CanExport
        => (_currentScan is not null || _currentAnalysis is not null)
           && _powerOverview is { HasUnresolvedSession: false };

    public async Task InitializeAsync()
    {
        Exception? recoveryFailure = null;
        try
        {
            var recovery = await Task.Run(() => _powerSessionCoordinator.RecoverInterruptedSession(
                Environment.ProcessId,
                ProcessIdentityMatches));
            if (recovery is not null)
            {
                StatusText = recovery.Message;
            }
        }
        catch (Exception exception)
        {
            recoveryFailure = exception;
        }

        await RefreshHistoryAsync();
        await ScanAsync();
        RefreshExpertTransactions();

        if (recoveryFailure is not null)
        {
            StatusText = $"Power-plan recovery needs attention: {recoveryFailure.Message}";
            throw new InvalidOperationException(
                "A previous power-plan recovery could not be fully verified. No new experiment was started. "
                + "Review System changes and Windows Power Options.",
                recoveryFailure);
        }
    }

    public async Task StartPowerSessionAsync(Guid approvedOriginalSchemeId)
    {
        if (!CanStartPowerSession || _currentScan is null)
        {
            throw new InvalidOperationException(PowerEligibilityText);
        }

        IsBusy = true;
        StatusText = "Preparing a durable rollback record and arming the independent guardian…";
        try
        {
            var transition = await Task.Run(() => _powerSessionCoordinator.ApplyHighPerformance(
                Environment.ProcessId,
                _processStartTimeUtcTicks,
                approvedOriginalSchemeId,
                _currentScan.Snapshot.Power.IsOnAc,
                PowerExperimentDuration));
            StatusText = transition.Message;
            await RefreshPowerSessionAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RestorePowerSessionAsync(string reason)
    {
        var sessionId = _powerOverview?.Journal?.SessionId
            ?? throw new InvalidOperationException("No active FramePath Lab power-plan session was found.");
        IsBusy = true;
        StatusText = "Restoring the exact previous power plan and verifying it…";
        try
        {
            var transition = await Task.Run(() => _powerSessionCoordinator.Revert(sessionId, reason));
            StatusText = transition.Message;
            await RefreshPowerSessionAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshPowerSessionAsync()
    {
        var overview = await Task.Run(_powerSessionCoordinator.Inspect);
        if (overview.HasUnresolvedSession
            && overview.Journal is not null
            && overview.Journal.OwnerProcessId == Environment.ProcessId
            && overview.Journal.OwnerProcessStartTimeUtcTicks == _processStartTimeUtcTicks
            && !_powerSessionCoordinator.IsGuardianArmed(overview.Journal.SessionId))
        {
            StatusText = "The rollback guardian stopped unexpectedly; restoring the previous plan now…";
            await Task.Run(() => _powerSessionCoordinator.Revert(
                overview.Journal.SessionId,
                "rollback guardian liveness check failed"));
            overview = await Task.Run(_powerSessionCoordinator.Inspect);
        }

        ApplyPowerOverview(overview);
    }

    public void MarkManualVerificationPending(string title)
    {
        StatusText = $"{title} opened for review. Change only one setting, return here, then select Scan this PC to verify detected state.";
    }

    public bool TryRestorePowerSessionOnExit(out string message)
    {
        try
        {
            var overview = _powerSessionCoordinator.Inspect();
            if (!overview.HasUnresolvedSession || overview.Journal is null)
            {
                message = "No active power-plan session needed restoration.";
                return true;
            }

            if (overview.Journal.OwnerProcessId != Environment.ProcessId
                || overview.Journal.OwnerProcessStartTimeUtcTicks != _processStartTimeUtcTicks)
            {
                message = "The active session belongs to another running FramePath Lab instance.";
                return true;
            }

            var transition = _powerSessionCoordinator.Revert(
                overview.Journal.SessionId,
                "FramePath Lab closed");
            message = transition.Message;
            return !PowerSessionCoordinator.IsUnresolved(transition.Record.State);
        }
        catch (Exception exception)
        {
            message = exception.Message;
            return false;
        }
    }

    public async Task ExportAsync(string path)
    {
        IsBusy = true;
        StatusText = "Writing collision-safe local report…";
        try
        {
            await RefreshPowerSessionAsync();
            if (_powerOverview is null || _powerOverview.HasUnresolvedSession)
            {
                throw new InvalidOperationException(
                    "Reports are disabled while power-plan state is unresolved. Restore and verify it first.");
            }

            _currentScan = await _scanCoordinator.RunAsync();
            await _reportWriter.WriteAsync(path, _currentScan, _currentAnalysis);
            StatusText = $"Report written to {path}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusText = "Reading supported local state…";
        try
        {
            _currentScan = await _scanCoordinator.RunAsync();
            Findings.Clear();
            foreach (var finding in _currentScan.Findings)
            {
                Findings.Add(finding);
            }

            ScanSummary = _currentScan.Summary;
            PlatformText = _currentScan.Snapshot.DecisionGradeCapability.ToString();
            var primary = _currentScan.Snapshot.Displays.FirstOrDefault(display => display.IsPrimary)
                ?? _currentScan.Snapshot.Displays.FirstOrDefault();
            PrimaryDisplayText = primary is null
                ? "No attached display resolved"
                : $"{primary.Width}×{primary.Height} · {primary.CurrentRefreshHz:0.###} Hz · {primary.AdapterDescription}";
            Cs2Text = _currentScan.Snapshot.SteamGame.Cs2Installed
                ? $"Build {_currentScan.Snapshot.SteamGame.BuildId} · {(_currentScan.Snapshot.SteamGame.Cs2Running ? "running" : "not running")}" 
                : _currentScan.Snapshot.SteamGame.InstallState;
            LastScanText = $"Last scan: {_currentScan.Snapshot.CapturedAtUtc.ToLocalTime():g}";
            var adapters = _currentScan.Snapshot.Displays
                .Select(display => display.AdapterDescription)
                .Where(adapter => !string.IsNullOrWhiteSpace(adapter))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var memoryGiB = _currentScan.Snapshot.TotalPhysicalMemoryBytes / 1024d / 1024d / 1024d;
            HardwareSummary = $"{_currentScan.Snapshot.LogicalProcessorCount} logical processors · {memoryGiB:0.#} GiB RAM · "
                + (adapters.Length > 0
                    ? $"active display path: {string.Join(" / ", adapters)}"
                    : "active display path unresolved");

            try
            {
                await RefreshPowerSessionAsync();
                StatusText = "Pre-game scan complete. Review the items marked Change available or Check manually.";
            }
            catch (Exception exception)
            {
                _powerOverview = null;
                PowerSessionText = "Power-plan state could not be read safely.";
                PowerEligibilityText = "Blocked until the active plan and recovery journal can be verified.";
                StatusText = $"Scan completed, but power-plan verification is blocked: {exception.Message}";
                NotifyPowerCommandState();
            }

            RebuildTweakCards();
            OnPropertyChanged(nameof(CanExport));
            NotifyPowerCommandState();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AnalyzeAsync()
    {
        IsBusy = true;
        StatusText = "Hashing and analyzing the imported capture…";
        try
        {
            double? budget = null;
            var primary = _currentScan?.Snapshot.Displays.FirstOrDefault(display => display.IsPrimary)
                ?? _currentScan?.Snapshot.Displays.FirstOrDefault();
            if (primary?.CurrentRefreshHz > 0)
            {
                budget = 1000d / primary.CurrentRefreshHz;
            }

            _currentAnalysis = await _captureAnalyzer.AnalyzeAsync(
                SelectedCapturePath,
                new CaptureAnalysisOptions(budget));
            Metrics.Clear();
            foreach (var metric in _currentAnalysis.Metrics)
            {
                Metrics.Add(MetricDisplay.From(metric));
            }

            Warnings.Clear();
            foreach (var warning in _currentAnalysis.Warnings)
            {
                Warnings.Add(warning);
            }

            CaptureSummary = _currentAnalysis.Outcome == ResultOutcome.Invalid
                ? "Capture was rejected. Review the quality warning; no result was inferred."
                : $"Analyzed {_currentAnalysis.AcceptedRows:N0} {_currentAnalysis.SelectedApplication} rows. Outcome: {_currentAnalysis.Outcome}.";
            StatusText = _currentAnalysis.Outcome == ResultOutcome.Invalid
                ? "Capture rejected safely."
                : "Capture analysis complete. This is a baseline-only result.";

            var numeric = _currentAnalysis.Metrics
                .Where(metric => metric.Value.HasValue)
                .ToDictionary(metric => metric.Id, metric => metric.Value!.Value, StringComparer.Ordinal);
            await _historyStore.AppendAsync(new HistoryEntry(
                Guid.NewGuid(),
                DateTimeOffset.UtcNow,
                "capture-import",
                _currentAnalysis.SourceFileName,
                _currentAnalysis.Outcome.ToString(),
                _currentAnalysis.SourceFileName,
                _currentAnalysis.SourceSha256,
                numeric));
            await RefreshHistoryAsync();
            OnPropertyChanged(nameof(CanExport));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshHistoryAsync()
    {
        var entries = await _historyStore.ReadAsync();
        History.Clear();
        foreach (var entry in entries)
        {
            History.Add(entry);
        }
    }

    private async Task DeleteHistoryAsync()
    {
        IsBusy = true;
        try
        {
            await _historyStore.DeleteAllAsync();
            History.Clear();
            StatusText = "Local history deleted. Imported source files were never copied.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SetError(Exception exception)
    {
        StatusText = exception switch
        {
            InvalidDataException => $"Input rejected safely: {exception.Message}",
            UnauthorizedAccessException => "Access denied. No state was changed.",
            _ => $"Operation failed safely: {exception.Message}"
        };
    }

    private void ApplyPowerOverview(PowerSessionOverview overview)
    {
        _powerOverview = overview;
        PowerPlanText = $"Active: {overview.ActiveSchemeName} ({overview.ActiveSchemeId:D})";
        if (overview.HasUnresolvedSession && overview.Journal is not null)
        {
            PowerSessionText = $"TEMPORARY SESSION ACTIVE — target {overview.Journal.TargetSchemeName}; "
                + $"original {overview.Journal.OriginalSchemeName}; automatic expiry "
                + $"{overview.Journal.ExpiresAtUtc.ToLocalTime():g}.";
        }
        else if (overview.Journal is not null)
        {
            PowerSessionText = $"No active session. Last transaction: {overview.Journal.State}. "
                + overview.Journal.LastObservation;
        }
        else
        {
            PowerSessionText = "No power-plan experiment is active.";
        }

        PowerEligibilityText = BuildPowerEligibilityText(overview);
        OnPropertyChanged(nameof(PowerApprovalSummary));
        OnPropertyChanged(nameof(CurrentPowerSchemeId));
        OnPropertyChanged(nameof(CanExport));
        NotifyPowerCommandState();
        RebuildTweakCards();
    }

    private void RebuildTweakCards()
    {
        Tweaks.Clear();
        if (_currentScan is null)
        {
            EnabledCount = 0;
            ActionCount = 0;
            ManualCount = 0;
            BlockedCount = 0;
            ReadinessHeadline = "Scan this PC before you play";
            ReadinessDetail = "Detected settings, manual checks, and exclusions will remain clearly separated.";
            return;
        }

        var snapshot = _currentScan.Snapshot;
        var cards = new List<TweakDisplay>
        {
            BuildPowerPlanTweak(),
            BuildRefreshRateTweak(snapshot),
            BuildGameModeTweak(),
            BuildGraphicsSchedulingTweak(),
            BuildReflexTweak(snapshot),
            BuildCs2VideoTweak(snapshot),
            BuildOverlayTweak(snapshot),
            BuildTestEnvironmentTweak(snapshot)
        };

        foreach (var card in cards
                     .OrderBy(card => StatusSortOrder(card.Status))
                     .ThenBy(card => card.SortOrder)
                     .ThenBy(card => card.Title, StringComparer.OrdinalIgnoreCase))
        {
            Tweaks.Add(card);
        }

        EnabledCount = cards.Count(card => card.Status == TweakUiStatus.Enabled);
        ActionCount = cards.Count(card => card.Status == TweakUiStatus.ActionAvailable);
        ManualCount = cards.Count(card => card.Status == TweakUiStatus.ManualCheck);
        BlockedCount = cards.Count(card => card.Status == TweakUiStatus.Blocked);

        ReadinessHeadline = ActionCount > 0
            ? $"{ActionCount} supported change{(ActionCount == 1 ? string.Empty : "s")} available"
            : ManualCount > 0
                ? $"{ManualCount} setting{(ManualCount == 1 ? string.Empty : "s")} need confirmation"
                : BlockedCount > 0
                    ? "Scan complete — some checks are unavailable"
                    : "No automatic change is currently needed";
        ReadinessDetail = $"{EnabledCount} ready or already set · {ManualCount} manual check{(ManualCount == 1 ? string.Empty : "s")} · "
            + $"{BlockedCount} blocked. This is a readiness summary, not a synthetic latency score.";
    }

    private TweakDisplay BuildPowerPlanTweak()
    {
        if (_powerOverview?.HasUnresolvedSession == true && _powerOverview.Journal is not null)
        {
            return new TweakDisplay(
                "power-plan",
                "Windows",
                "Windows power plan",
                TweakUiStatus.Enabled,
                "TEMPORARILY ACTIVE",
                _powerOverview.ActiveSchemeName,
                $"Benchmark, then restore {_powerOverview.Journal.OriginalSchemeName}",
                "A bounded High performance experiment is active and being watched by the rollback guardian.",
                "Power policy can affect CPU power-state behaviour, but a latency benefit on this PC is not assumed.",
                "May increase idle power, heat, fan noise, and energy use.",
                "Moderate evidence · verified Windows API state",
                TweakActionKind.RestorePowerSession,
                "Restore previous plan",
                10);
        }

        if (_powerOverview is null)
        {
            return new TweakDisplay(
                "power-plan",
                "Windows",
                "Windows power plan",
                TweakUiStatus.Blocked,
                "STATE UNAVAILABLE",
                "Active plan could not be verified",
                "Resolve recovery state before changing anything",
                "FramePath Lab refuses to infer or overwrite an unknown power-plan state.",
                "An unverified write could overwrite a user or OEM selection.",
                "No automatic write is allowed while state is unknown.",
                "Strong safety rule",
                TweakActionKind.None,
                string.Empty,
                10);
        }

        if (_powerOverview.ActiveSchemeId == PowerSessionCoordinator.HighPerformanceSchemeId)
        {
            return new TweakDisplay(
                "power-plan",
                "Windows",
                "Windows power plan",
                TweakUiStatus.Enabled,
                "ALREADY ENABLED",
                _powerOverview.ActiveSchemeName,
                "No change needed",
                "Windows already reports the standard High performance plan as active.",
                "Switching to the same plan again cannot improve latency and would only create placebo activity.",
                "High performance can increase power use and temperature even when it provides no measurable benefit.",
                "Moderate evidence · verified Windows API state",
                TweakActionKind.None,
                string.Empty,
                10);
        }

        if (IsPowerEligibleForApply)
        {
            return new TweakDisplay(
                "power-plan",
                "Windows",
                "Windows power plan",
                TweakUiStatus.ActionAvailable,
                "CHANGE AVAILABLE",
                _powerOverview.ActiveSchemeName,
                "Temporarily test High performance",
                "FramePath Lab can run a 15-minute, verified High performance comparison and restore the exact prior plan.",
                "This can test whether Windows power policy affects frame-time consistency on this specific PC.",
                "Benefit may be zero; higher power, heat, and fan noise are possible.",
                "Moderate evidence · opt-in experiment",
                TweakActionKind.StartPowerSession,
                "Start 15-minute test",
                10);
        }

        return new TweakDisplay(
            "power-plan",
            "Windows",
            "Windows power plan",
            TweakUiStatus.Blocked,
            "NOT AVAILABLE",
            _powerOverview.ActiveSchemeName,
            "Leave unchanged",
            PowerEligibilityText,
            "The app only permits an installed, policy-allowed standard plan on AC power in a local session.",
            "FramePath Lab will not create plans, elevate itself, or bypass policy.",
            "Strong safety rule",
            TweakActionKind.None,
            string.Empty,
            10);
    }

    private static TweakDisplay BuildRefreshRateTweak(EnvironmentSnapshot snapshot)
    {
        var primary = snapshot.Displays.FirstOrDefault(display => display.IsPrimary)
            ?? snapshot.Displays.FirstOrDefault();
        if (primary is null || primary.CurrentRefreshHz <= 0)
        {
            return new TweakDisplay(
                "display-refresh",
                "Display",
                "Active monitor refresh rate",
                TweakUiStatus.Blocked,
                "NOT DETECTED",
                "No attached display mode was resolved",
                "Verify the physical display in Windows",
                "The app cannot recommend a refresh change without a resolved active mode.",
                "Refresh rate affects scan-out timing, but monitor topology must be preserved.",
                "Blind display changes can disrupt HDR, VRR, scaling, or multi-monitor layouts.",
                "Strong evidence · Windows display API",
                TweakActionKind.OpenAdvancedDisplay,
                "Open Advanced display",
                20);
        }

        var refreshDifference = primary.MaximumRefreshAtCurrentResolutionHz - primary.CurrentRefreshHz;
        var hasMeaningfullyHigherRate = refreshDifference >= 1.5;
        var hasTelevisionTimingPair = refreshDifference > 0.1 && refreshDifference < 1.5;
        var status = hasMeaningfullyHigherRate
            ? TweakUiStatus.ActionAvailable
            : hasTelevisionTimingPair
                ? TweakUiStatus.ManualCheck
                : TweakUiStatus.Enabled;
        return new TweakDisplay(
            "display-refresh",
            "Display",
            "Active monitor refresh rate",
            status,
            hasMeaningfullyHigherRate
                ? "HIGHER RATE AVAILABLE"
                : hasTelevisionTimingPair
                    ? "VERIFY 59 / 60 REPORTING"
                    : "ALREADY AT REPORTED MAX",
            $"{primary.Width}×{primary.Height} at {primary.CurrentRefreshHz:0.###} Hz · {primary.MonitorDescription}",
            hasMeaningfullyHigherRate
                ? $"Verify {primary.MaximumRefreshAtCurrentResolutionHz:0.###} Hz in Windows"
                : hasTelevisionTimingPair
                    ? "Open Windows to confirm the active rational refresh; do not assume a mismatch"
                    : "No higher compatible rate was enumerated at this resolution",
            hasMeaningfullyHigherRate
                ? "Windows reports a meaningfully higher refresh candidate at the current resolution. Change it manually, then rescan."
                : hasTelevisionTimingPair
                    ? "Windows can present TV-compatible 59.94 Hz timings as either 59 Hz or 60 Hz. This scan will not call that an optimization opportunity."
                    : "The active rate matches the highest compatible value reported at the current resolution.",
            "A higher verified refresh can shorten display scan-out intervals when the monitor and link support it.",
            "HDR, bit depth, chroma, VRR range, scaling, or link stability can change.",
            "Strong evidence · Windows display API",
            hasMeaningfullyHigherRate || hasTelevisionTimingPair
                ? TweakActionKind.OpenAdvancedDisplay
                : TweakActionKind.None,
            hasMeaningfullyHigherRate || hasTelevisionTimingPair
                ? "Open Advanced display"
                : string.Empty,
            20);
    }

    private static TweakDisplay BuildGameModeTweak()
        => new(
            "game-mode",
            "Windows",
            "Windows Game Mode",
            TweakUiStatus.ManualCheck,
            "CHECK MANUALLY",
            "No stable public API is used to infer the toggle",
            "Review Game Mode in Windows Settings",
            "Open the supported Windows page, record the current state, and change it only as an isolated experiment.",
            "Game Mode changes how Windows prioritizes gaming workloads, but results remain system-dependent.",
            "Can help, do nothing, or regress a specific setup; benchmark one state at a time.",
            "Moderate evidence · state not automatically verified",
            TweakActionKind.OpenGameMode,
            "Open Game Mode",
            30);

    private static TweakDisplay BuildGraphicsSchedulingTweak()
        => new(
            "graphics-defaults",
            "Windows",
            "Default graphics settings (HAGS / VRR)",
            TweakUiStatus.ManualCheck,
            "CHECK MANUALLY",
            "Supported toggle state is not exposed through a stable public app API",
            "Review current settings; test one reboot block at a time",
            "FramePath Lab opens the official Windows graphics settings surface instead of editing undocumented registry values.",
            "Hardware scheduling and variable-refresh behaviour can affect presentation paths and frame pacing.",
            "HAGS requires controlled reboot-separated testing and can help, do nothing, or regress.",
            "Moderate evidence · manual verification required",
            TweakActionKind.OpenGraphicsDefaults,
            "Open Graphics settings",
            40);

    private static TweakDisplay BuildReflexTweak(EnvironmentSnapshot snapshot)
    {
        var adapterText = string.Join(' ', snapshot.Displays.Select(display => display.AdapterDescription));
        var nvidiaReported = adapterText.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
        return new TweakDisplay(
            "reflex",
            "Game / GPU",
            "NVIDIA Reflex in supported games",
            TweakUiStatus.ManualCheck,
            "CHECK IN GAME",
            nvidiaReported
                ? "NVIDIA display path reported; Reflex state is not externally verified"
                : "NVIDIA display path not reported by this scan; verify the actual render GPU",
            "For CS2, test Reflex Enabled first; test Enabled + Boost separately",
            "The setting belongs inside the game. FramePath Lab deliberately does not edit CS2 files or opaque driver profiles.",
            "Reflex can reduce the render queue when the title supports it; Boost is a separate power trade-off.",
            "Boost may increase power/temperature and can slightly reduce FPS in some conditions.",
            "Strong vendor evidence · manual in-game state",
            TweakActionKind.ShowCs2Checklist,
            "Show CS2 checklist",
            50);
    }

    private static TweakDisplay BuildCs2VideoTweak(EnvironmentSnapshot snapshot)
        => new(
            "cs2-video",
            "Game",
            "CS2 frame cap, V-Sync and display mode",
            snapshot.SteamGame.Cs2Installed ? TweakUiStatus.ManualCheck : TweakUiStatus.Blocked,
            snapshot.SteamGame.Cs2Installed ? "CHECK IN GAME" : "CS2 NOT RESOLVED",
            snapshot.SteamGame.Cs2Installed
                ? $"CS2 build {snapshot.SteamGame.BuildId}; video state is not read from game files"
                : snapshot.SteamGame.InstallState,
            "Choose one documented latency/pacing strategy and benchmark it",
            "The correct cap/V-Sync/VRR combination depends on refresh rate, tearing tolerance, GPU headroom, and whether Reflex is active.",
            "Changing several controls together makes results impossible to attribute and can worsen pacing or add queueing.",
            "Game files are never written; current state requires in-game verification.",
            "Moderate evidence · configuration-dependent",
            snapshot.SteamGame.Cs2Installed ? TweakActionKind.ShowCs2Checklist : TweakActionKind.None,
            snapshot.SteamGame.Cs2Installed ? "Show CS2 checklist" : string.Empty,
            60);

    private static TweakDisplay BuildOverlayTweak(EnvironmentSnapshot snapshot)
    {
        var observed = snapshot.ObservedOptionalApplications;
        return new TweakDisplay(
            "overlays",
            "Background",
            "Optional overlays and recording tools",
            observed.Count == 0 ? TweakUiStatus.Enabled : TweakUiStatus.ManualCheck,
            observed.Count == 0 ? "NONE OBSERVED" : "REVIEW DETECTED APPS",
            observed.Count == 0 ? "No selected optional overlay/recording process was observed" : string.Join(", ", observed),
            observed.Count == 0 ? "No action needed" : "Close only nonessential apps you recognize, then rescan",
            "Process presence alone is not proof of latency. The app highlights candidates without killing processes or disabling services.",
            "A time-correlated overlay or recorder can contend for CPU/GPU resources; many have no measurable impact.",
            "Closing communications, capture, or accessibility software can disrupt your workflow.",
            "Moderate evidence · process presence only",
            observed.Count == 0 ? TweakActionKind.None : TweakActionKind.ShowOverlayReview,
            observed.Count == 0 ? string.Empty : "Review detected apps",
            70);
    }

    private static TweakDisplay BuildTestEnvironmentTweak(EnvironmentSnapshot snapshot)
    {
        var ready = snapshot.Power.IsOnAc && !snapshot.IsRemoteSession;
        return new TweakDisplay(
            "test-environment",
            "Session",
            "Local AC-powered test environment",
            ready ? TweakUiStatus.Enabled : TweakUiStatus.Blocked,
            ready ? "READY" : "TEST CONDITIONS BLOCKED",
            $"{(snapshot.IsRemoteSession ? "Remote Desktop" : "Local session")} · {(snapshot.Power.IsOnAc ? "AC power" : "battery or unknown power")}",
            "Use the same local, AC-powered conditions for every comparison",
            "Stable test conditions matter more than applying a large number of tweaks.",
            "Remote presentation and battery power can invalidate or distort performance comparisons.",
            "Changing test conditions between runs makes before/after results incomparable.",
            "Strong methodology requirement",
            TweakActionKind.None,
            string.Empty,
            80);
    }

    private static int StatusSortOrder(TweakUiStatus status)
        => status switch
        {
            TweakUiStatus.ActionAvailable => 0,
            TweakUiStatus.ManualCheck => 1,
            TweakUiStatus.Blocked => 2,
            TweakUiStatus.Enabled => 3,
            _ => 4
        };

    private bool IsPowerEligibleForApply
        => _currentScan is not null
           && !_currentScan.Snapshot.IsRemoteSession
           && _currentScan.Snapshot.Power.IsOnAc
           && _powerOverview is not null
           && _powerOverview.HighPerformanceAvailable
           && _powerOverview.HighPerformancePolicyAllowed
           && !_powerOverview.HasUnresolvedSession
           && _powerOverview.ActiveSchemeId != PowerSessionCoordinator.HighPerformanceSchemeId;

    private string BuildPowerEligibilityText(PowerSessionOverview overview)
    {
        if (_currentScan is null)
        {
            return "Run the scan before starting an experiment.";
        }

        if (_currentScan.Snapshot.IsRemoteSession)
        {
            return "Blocked in Remote Desktop sessions.";
        }

        if (!_currentScan.Snapshot.Power.IsOnAc)
        {
            return "Blocked because AC power was not positively detected.";
        }

        if (overview.HasUnresolvedSession)
        {
            return "An earlier transaction must be restored or recovered first.";
        }

        if (!overview.HighPerformanceAvailable)
        {
            return "High performance is not installed. FramePath Lab will not create or unhide it.";
        }

        if (!overview.HighPerformancePolicyAllowed)
        {
            return overview.PolicyStatus;
        }

        if (overview.ActiveSchemeId == PowerSessionCoordinator.HighPerformanceSchemeId)
        {
            return "High performance is already active, so there is nothing to apply.";
        }

        return "Eligible for an opt-in 15-minute experiment. Measurable benefit is not assumed.";
    }

    private void NotifyPowerCommandState()
    {
        OnPropertyChanged(nameof(CanStartPowerSession));
        OnPropertyChanged(nameof(CanRestorePowerSession));
    }

    private static bool ProcessIdentityMatches(int processId, long expectedStartTimeUtcTicks)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited
                && process.StartTime.ToUniversalTime().Ticks == expectedStartTimeUtcTicks;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
