using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using FramePathLab.Core.Models;
using FramePathLab.Core.Persistence;
using FramePathLab.Core.Services;
using FramePathLab.Windows.Mutation;
using FramePathLab.Windows.Scanning;

namespace FramePathLab.App;

/// <summary>
/// The two operations that have to happen on the machine being tuned, without needing the window.
///
/// <para>
/// The tuning machine and the machine someone wants to sit at are usually different, and the
/// difference is not incidental: a competitive machine is kept deliberately clean, and the point of
/// tuning it is that it is about to be played on — not sat in front of with a mouse and a list of
/// checkboxes. So the two halves that must run there are reduced to one command each, and
/// everything in between happens wherever is convenient.
/// </para>
/// <para>
/// <c>--collect</c> reads the machine and writes a file. <c>--apply</c> reads a chosen set and
/// makes the changes. Nothing else needs to happen on the target at all.
/// </para>
/// </summary>
internal static class PortableCommandLine
{
    public const string CollectSwitch = "--collect";
    public const string ApplySwitch = "--apply";

    public static bool Handles(IReadOnlyList<string> arguments)
        => arguments.Count > 0
           && (string.Equals(arguments[0], CollectSwitch, StringComparison.Ordinal)
               || string.Equals(arguments[0], ApplySwitch, StringComparison.Ordinal));

    public static int Run(IReadOnlyList<string> arguments)
    {
        AttachToParentConsole();
        try
        {
            return string.Equals(arguments[0], CollectSwitch, StringComparison.Ordinal)
                ? Collect(arguments)
                : Apply(arguments);
        }
        catch (Exception exception)
        {
            Report($"Failed: {exception.Message}", isError: true);
            return 1;
        }
    }

    // ---- collect -------------------------------------------------------------------------------

    private static int Collect(IReadOnlyList<string> arguments)
    {
        var path = arguments.Count > 1 && !string.IsNullOrWhiteSpace(arguments[1])
            ? arguments[1]
            : DefaultSnapshotPath();

        Write($"Reading {Environment.MachineName}. This takes about a minute and changes nothing.");

        // Input latency needs a hand on the mouse. An unattended collection must not record a
        // figure nobody was there to produce, so it is skipped and the card reports it as unread.
        var snapshot = MachineSnapshotCollector
            .CollectAsync(measureInput: false, TimeSpan.Zero)
            .GetAwaiter()
            .GetResult();

        MachineSnapshotStore.WriteSnapshot(path, snapshot);

        var review = RemoteMachineReview.Review(snapshot);
        var builder = new StringBuilder()
            .AppendLine($"Snapshot written to {Path.GetFullPath(path)}")
            .AppendLine()
            .AppendLine(snapshot.Identity.Describe())
            .AppendLine(snapshot.CollectedElevated
                ? "Collected with administrator rights, so machine-scope state was readable."
                : "NOT elevated. Machine-scope state could not be read, so most of the catalogue "
                  + "will come back unreadable. Re-run this as administrator.")
            .AppendLine()
            .AppendLine(review.Summary)
            .AppendLine()
            .AppendLine("Copy this file to whichever machine you want to review it on, open it there "
                        + "with Open snapshot, choose what to change, and bring the plan file back.");

        Report(builder.ToString(), isError: false);
        return 0;
    }

    private static string DefaultSnapshotPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"{Environment.MachineName}-{DateTimeOffset.Now:yyyyMMdd-HHmm}{MachineSnapshotStore.SnapshotExtension}");

    // ---- apply ---------------------------------------------------------------------------------

    private static int Apply(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2 || string.IsNullOrWhiteSpace(arguments[1]))
        {
            Report(
                $"Usage: FramePathLab.exe {ApplySwitch} <plan{MachineSnapshotStore.PlanExtension}>",
                isError: true);
            return 2;
        }

        var planPath = Path.GetFullPath(arguments[1]);
        var plan = MachineSnapshotStore.ReadPlan(planPath);

        Write($"Re-reading this machine before applying {plan.TweakIds.Count} selected change(s).");

        // The plan is not trusted to describe this machine. Everything is re-derived from a fresh
        // scan, so a change that is no longer applicable — because the machine moved on, or because
        // the plan came from somewhere else entirely — is simply not found and not made.
        var snapshot = MachineSnapshotCollector
            .CollectAsync(measureInput: false, TimeSpan.Zero)
            .GetAwaiter()
            .GetResult();

        var mismatch = RemoteMachineReview.FindTargetMismatch(plan, snapshot.Identity);
        if (mismatch is not null)
        {
            Report(mismatch, isError: true);
            return 3;
        }

        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FramePathLab");
        var engine = new ExpertTweakEngine(
            new WindowsMutationExecutor(),
            new TweakJournalStore(dataDirectory),
            snapshot.CollectedElevated);

        var cards = engine.Evaluate(snapshot.Context)
            .ToDictionary(card => card.Definition.Id, StringComparer.Ordinal);

        var results = new List<PlanApplicationResult>();
        var rebootRequired = false;

        foreach (var id in plan.TweakIds)
        {
            if (!cards.TryGetValue(id, out var card))
            {
                results.Add(new PlanApplicationResult(
                    id, id, false, "Not offered on this machine by this build. Nothing was written."));
                continue;
            }

            if (!card.CanApply)
            {
                results.Add(new PlanApplicationResult(
                    id,
                    card.Definition.Title,
                    false,
                    card.BlockedReason
                    ?? (card.Reading.State == TweakState.Optimal
                        ? "Already at the target value."
                        : $"Not applicable now: {card.Reading.Detail}")));
                continue;
            }

            try
            {
                var transaction = engine.Apply(card);
                var applied = string.Equals(
                    transaction.State, TweakTransaction.StateApplied, StringComparison.Ordinal);
                rebootRequired |= applied && transaction.RequiresReboot;
                results.Add(new PlanApplicationResult(
                    id, card.Definition.Title, applied, transaction.LastObservation));
            }
            catch (Exception exception)
            {
                results.Add(new PlanApplicationResult(
                    id, card.Definition.Title, false, exception.Message));
            }
        }

        var appliedCount = results.Count(result => result.Applied);
        var report = new PlanApplicationReport(
            DateTimeOffset.UtcNow,
            snapshot.Identity,
            results,
            rebootRequired,
            $"{appliedCount} of {plan.TweakIds.Count} applied on {snapshot.Identity.MachineName}.");

        var reportPath = Path.ChangeExtension(planPath, ".result.json");
        MachineSnapshotStore.WriteReport(reportPath, report);

        var builder = new StringBuilder()
            .AppendLine(report.Summary)
            .AppendLine();
        foreach (var result in results)
        {
            builder
                .AppendLine($"  [{(result.Applied ? "applied" : "skipped")}] {result.Title}")
                .AppendLine($"      {result.Observation}");
        }

        builder
            .AppendLine()
            .AppendLine(rebootRequired
                ? "A restart is needed before some of these take effect."
                : "No restart needed.")
            .AppendLine($"Full record: {reportPath}")
            .AppendLine("Every change is in the ledger. Open FramePath Lab here to revert any of them.");

        Report(builder.ToString(), isError: appliedCount == 0 && plan.TweakIds.Count > 0);
        return 0;
    }

    // ---- output --------------------------------------------------------------------------------

    /// <summary>
    /// A WinExe started from a terminal has no console of its own, so it borrows the one that
    /// launched it. Double-clicked there is nothing to borrow, and the summary goes to a dialog
    /// instead: the same information either way, rather than a run that appears to do nothing.
    /// </summary>
    private static bool _hasConsole;

    private static void AttachToParentConsole()
    {
        try
        {
            _hasConsole = AttachConsole(AttachParentProcess);
        }
        catch (DllNotFoundException)
        {
            _hasConsole = false;
        }
    }

    private static void Write(string message)
    {
        if (_hasConsole)
        {
            Console.WriteLine(message);
        }
    }

    private static void Report(string message, bool isError)
    {
        if (_hasConsole)
        {
            (isError ? Console.Error : Console.Out).WriteLine(message);
            return;
        }

        MessageBox.Show(
            message,
            "FramePath Lab",
            MessageBoxButton.OK,
            isError ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}
