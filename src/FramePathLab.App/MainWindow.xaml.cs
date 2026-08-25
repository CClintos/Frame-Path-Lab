using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FramePathLab.App.ViewModels;
using FramePathLab.Core.Analysis;
using FramePathLab.Core.Evidence;
using FramePathLab.Core.Persistence;
using FramePathLab.Core.Reporting;
using FramePathLab.Core.Services;
using FramePathLab.Windows.Power;
using FramePathLab.Windows.Scanning;
using Microsoft.Win32;

namespace FramePathLab.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _powerSessionTimer;
    private bool _powerRefreshRunning;

    public MainWindow()
    {
        InitializeComponent();
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FramePathLab");
        var powerJournal = new PowerSessionJournalStore(dataDirectory);
        var guardianExecutable = Path.Combine(AppContext.BaseDirectory, "FramePathLab.exe");
        var powerCoordinator = new PowerSessionCoordinator(
            new WindowsPowerSchemeController(),
            powerJournal,
            new ProcessPowerSessionGuardian(guardianExecutable, powerJournal));
        _viewModel = new MainViewModel(
            new ScanCoordinator(new WindowsEnvironmentScanner(), new DefaultEvidenceCatalog()),
            new PresentMonCsvAnalyzer(),
            new JsonHistoryStore(dataDirectory),
            new MarkdownReportWriter(),
            powerCoordinator,
            dataDirectory);
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        _powerSessionTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, PowerSessionTimer_Tick, Dispatcher);
        _powerSessionTimer.Start();
    }

    private async void StartPowerSession_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanStartPowerSession)
        {
            MessageBox.Show(
                this,
                _viewModel.PowerEligibilityText,
                "Power-plan experiment unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var approvedOriginalSchemeId = _viewModel.CurrentPowerSchemeId;
        var approvalSummary = _viewModel.PowerApprovalSummary;
        if (!approvedOriginalSchemeId.HasValue)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            "Start this exact Windows-wide change?\n\n"
            + approvalSummary
            + "\n\nThis switches the active plan pointer only; it does not edit the plan. "
            + "It may increase power use, heat, and fan noise, and it may not improve CS2. "
            + "Keep FramePath Lab open during the experiment. Closing the app, losing AC power, "
            + "or reaching the time limit triggers a verified restoration attempt. External plan changes "
            + "are preserved rather than overwritten. An abnormal Windows restart can "
            + "leave the plan active until FramePath Lab is opened again.",
            "Approve temporary High performance session",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _viewModel.StartPowerSessionAsync(approvedOriginalSchemeId.Value);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"The power-plan experiment did not start safely.\n\n{exception.Message}",
                "FramePath Lab",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            await TryRefreshPowerStateAsync();
        }
    }

    private async void RestorePowerSession_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RestorePowerSessionAsync("User selected Restore previous plan");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Automatic restoration could not be verified. The rollback guardian will keep trying "
                + $"where safe.\n\n{exception.Message}",
                "Restoration needs attention",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await TryRefreshPowerStateAsync();
        }
    }

    private void TweakAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TweakDisplay tweak })
        {
            return;
        }

        switch (tweak.ActionKind)
        {
            case TweakActionKind.StartPowerSession:
                StartPowerSession_Click(sender, e);
                break;
            case TweakActionKind.RestorePowerSession:
                RestorePowerSession_Click(sender, e);
                break;
            case TweakActionKind.OpenAdvancedDisplay:
                OpenSettingsPage(
                    "ms-settings:display-advanced",
                    "Windows Advanced display",
                    tweak.Title);
                break;
            case TweakActionKind.OpenGameMode:
                OpenSettingsPage(
                    "ms-settings:gaming-gamemode",
                    "Windows Game Mode",
                    tweak.Title);
                break;
            case TweakActionKind.OpenGraphicsDefaults:
                OpenSettingsPage(
                    "ms-settings:display-advancedgraphics-default",
                    "Windows default graphics settings",
                    tweak.Title);
                break;
            case TweakActionKind.ShowCs2Checklist:
                ShowCs2Checklist();
                _viewModel.MarkManualVerificationPending(tweak.Title);
                break;
            case TweakActionKind.ShowOverlayReview:
                MessageBox.Show(
                    this,
                    $"Observed selected applications:\n\n{tweak.CurrentValue}\n\n"
                    + "Process presence is not proof of latency. Close only a nonessential app you recognize, "
                    + "using its own Exit command, then return to FramePath Lab and scan again. The app will not "
                    + "kill processes or disable services.",
                    "Review optional background applications",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _viewModel.MarkManualVerificationPending(tweak.Title);
                break;
            case TweakActionKind.None:
            default:
                break;
        }
    }

    private void OpenAdvancedDisplay_Click(object sender, RoutedEventArgs e)
        => OpenSettingsPage(
            "ms-settings:display-advanced",
            "Windows Advanced display",
            "Active monitor refresh rate");

    private void OpenSettingsPage(string settingsUri, string pageName, string tweakTitle)
    {
        try
        {
            Process.Start(new ProcessStartInfo(settingsUri)
            {
                UseShellExecute = true
            });
            _viewModel.MarkManualVerificationPending(tweakTitle);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"{pageName} could not be opened.\n\n{exception.Message}",
                "FramePath Lab",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowCs2Checklist()
    {
        MessageBox.Show(
            this,
            "In CS2, open Settings > Video and record the current values before changing one item.\n\n"
            + "1. Confirm the intended resolution, display mode, and refresh rate.\n"
            + "2. With an NVIDIA GPU, test NVIDIA Reflex Enabled first. Treat Enabled + Boost as a separate, higher-power test.\n"
            + "3. Choose the frame-cap, V-Sync, and VRR strategy together; the right choice depends on tearing tolerance and GPU headroom.\n"
            + "4. Avoid copied launch-option packs and unsupported config edits.\n"
            + "5. Repeat the same scenario before and after. Keep a change only when the state is verified and frame pacing improves repeatably.\n\n"
            + "FramePath Lab does not edit CS2 files or driver profiles and cannot verify these in-game toggles from outside the game.",
            "CS2 latency and frame-pacing checklist",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "Startup could not complete or verify a recovery step. No new experiment was started. "
                + $"Review Safety & data.\n\n{exception.Message}",
                "FramePath Lab",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BrowseCapture_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a PresentMon-style CSV capture",
            Filter = "CSV capture (*.csv)|*.csv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.SelectedCapturePath = dialog.FileName;
        }
    }

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanExport)
        {
            MessageBox.Show(this, "Run a scan or analyze a capture first.", "FramePath Lab", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export a local Markdown report",
            Filter = "Markdown report (*.md)|*.md",
            FileName = $"framepath-report-{DateTime.Now:yyyyMMdd-HHmmss}.md",
            AddExtension = true,
            OverwritePrompt = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var path = MakeCollisionSafe(dialog.FileName);
        try
        {
            await _viewModel.ExportAsync(path);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Report export failed safely.\n\n{exception.Message}", "FramePath Lab", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DeleteHistory_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "Delete FramePath Lab's derived local history? Imported source files were never copied and will not be touched.",
            "Delete local history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
        {
            _viewModel.DeleteHistoryCommand.Execute(null);
        }
    }

    private async void PowerSessionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_viewModel.CanRestorePowerSession || _viewModel.IsBusy || _powerRefreshRunning)
        {
            return;
        }

        await TryRefreshPowerStateAsync();
    }

    private async Task TryRefreshPowerStateAsync()
    {
        if (_powerRefreshRunning)
        {
            return;
        }

        _powerRefreshRunning = true;
        try
        {
            await _viewModel.RefreshPowerSessionAsync();
        }
        catch
        {
            // The visible transaction status remains intact; explicit restore and next launch can retry.
        }
        finally
        {
            _powerRefreshRunning = false;
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _powerSessionTimer.Stop();
        if (_viewModel.TryRestorePowerSessionOnExit(out var message))
        {
            return;
        }

        MessageBox.Show(
            this,
            "FramePath Lab could not verify restoration before closing. Its independent guardian will "
            + "attempt the same compare-and-restore after this window exits. Reopen the app to verify.\n\n"
            + message,
            "Power-plan recovery pending",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string MakeCollisionSafe(string requestedPath)
    {
        if (!File.Exists(requestedPath))
        {
            return requestedPath;
        }

        var directory = Path.GetDirectoryName(requestedPath) ?? Environment.CurrentDirectory;
        var baseName = Path.GetFileNameWithoutExtension(requestedPath);
        var extension = Path.GetExtension(requestedPath);
        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            var candidate = Path.Combine(directory, $"{baseName} ({suffix}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Could not allocate a collision-safe report filename.");
    }
}
