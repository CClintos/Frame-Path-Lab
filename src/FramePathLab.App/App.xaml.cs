using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using FramePathLab.Windows.Power;

namespace FramePathLab.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0
            && string.Equals(e.Args[0], "--power-guardian", StringComparison.Ordinal))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(RunPowerGuardian(e.Args));
            return;
        }

        // Collecting from, and applying to, a machine other than the one being sat at. Both run
        // without the window so the target only ever needs one command.
        if (PortableCommandLine.Handles(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(PortableCommandLine.Run(e.Args));
            return;
        }

        DispatcherUnhandledException += HandleUnhandledException;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static int RunPowerGuardian(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 4
            || !Guid.TryParseExact(arguments[1], "D", out var sessionId)
            || sessionId == Guid.Empty
            || !Guid.TryParseExact(arguments[2], "D", out var nonce)
            || nonce == Guid.Empty
            || !int.TryParse(
                arguments[3],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var ownerProcessId)
            || ownerProcessId <= 0)
        {
            return 2;
        }

        try
        {
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FramePathLab");
            return PowerSessionGuardianRunner.Run(
                sessionId,
                nonce,
                ownerProcessId,
                new PowerSessionJournalStore(dataDirectory),
                new WindowsPowerSchemeController());
        }
        catch
        {
            return 3;
        }
    }

    private static void HandleUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "FramePath Lab encountered an unexpected error and must close. If a power-plan session "
            + "was active, the close path and rollback guardian will attempt to restore the previous "
            + "plan. Reopen FramePath Lab to verify recovery.\n\n"
            + e.Exception.Message,
            "FramePath Lab",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(-1);
    }
}
