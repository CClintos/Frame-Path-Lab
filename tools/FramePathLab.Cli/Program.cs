using System.Globalization;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using FramePathLab.Core.Analysis;
using FramePathLab.Core.Evidence;
using FramePathLab.Core.Models;
using FramePathLab.Core.Persistence;
using FramePathLab.Core.Reporting;
using FramePathLab.Core.Services;
using FramePathLab.Windows.Mutation;
using FramePathLab.Windows.Scanning;

namespace FramePathLab.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            return args[0].ToLowerInvariant() switch
            {
                "scan" => await ScanAsync(),
                "analyze" => await AnalyzeAsync(args),
                "report" => await ReportAsync(args),
                "expert" => await ExpertScanAsync(args),
                "expert-apply" => await ExpertApplyAsync(args),
                "expert-revert" => await ExpertRevertAsync(args),
                "expert-history" => ExpertHistory(),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Failed safely: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> ScanAsync()
    {
        var coordinator = new ScanCoordinator(new WindowsEnvironmentScanner(), new DefaultEvidenceCatalog());
        var report = await coordinator.RunAsync();
        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return 0;
    }

    private static async Task<int> AnalyzeAsync(string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            PrintUsage();
            return 2;
        }

        double? budget = null;
        if (args.Length == 3
            && (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                || parsed <= 0))
        {
            Console.Error.WriteLine("frame-budget-ms must be a positive invariant-culture number.");
            return 2;
        }
        else if (args.Length == 3)
        {
            budget = double.Parse(args[2], CultureInfo.InvariantCulture);
        }

        var analysis = await new PresentMonCsvAnalyzer().AnalyzeAsync(
            args[1],
            new FramePathLab.Core.Models.CaptureAnalysisOptions(budget));
        Console.WriteLine(JsonSerializer.Serialize(analysis, JsonOptions));
        return analysis.Outcome == FramePathLab.Core.Models.ResultOutcome.Invalid ? 1 : 0;
    }

    private static async Task<int> ReportAsync(string[] args)
    {
        if (args.Length != 3)
        {
            PrintUsage();
            return 2;
        }

        var coordinator = new ScanCoordinator(new WindowsEnvironmentScanner(), new DefaultEvidenceCatalog());
        var scan = await coordinator.RunAsync();
        var analysis = await new PresentMonCsvAnalyzer().AnalyzeAsync(
            args[1],
            new FramePathLab.Core.Models.CaptureAnalysisOptions());
        await new MarkdownReportWriter().WriteAsync(args[2], scan, analysis);
        Console.WriteLine(Path.GetFullPath(args[2]));
        return analysis.Outcome == FramePathLab.Core.Models.ResultOutcome.Invalid ? 1 : 0;
    }

    private static string DataDirectory
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FramePathLab");

    private static ExpertTweakEngine BuildEngine()
        => new(
            new WindowsMutationExecutor(),
            new TweakJournalStore(DataDirectory),
            IsElevated());

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static async Task<ExpertScanContext> BuildContextAsync(bool measureInput)
    {
        var scan = await new ScanCoordinator(
            new WindowsEnvironmentScanner(), new DefaultEvidenceCatalog()).RunAsync();
        return await new ExpertScanCoordinator().ScanAsync(
            scan.Snapshot,
            measureInput,
            measureScheduler: true,
            TimeSpan.FromSeconds(5));
    }

    private static async Task<int> ExpertScanAsync(string[] args)
    {
        var measureInput = args.Contains("--measure-input", StringComparer.OrdinalIgnoreCase);
        if (measureInput)
        {
            Console.Error.WriteLine("Move the mouse continuously for the next 5 seconds...");
        }

        var context = await BuildContextAsync(measureInput);
        var cards = BuildEngine().Evaluate(context);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                context.Cpu,
                context.Gpus,
                context.PrimaryTiming,
                context.Input,
                context.Latency,
                context.Memory,
                context.Steam,
                context.ForcedPlatformClock,
                context.PerformanceCounterFrequency,
                context.GpuMessageSignalledInterrupts,
                context.GpuInterruptObservation,
                context.NetworkAdapters,
                context.Audio,
                context.NetworkPath,
                context.Panel,
                context.NvidiaProfile,
                context.FastStartupEnabled,
                context.InterruptAffinityObservation,
                context.DefenderObservation,
                Tweaks = cards
            },
            JsonOptions));
        return 0;
    }

    private static async Task<int> ExpertApplyAsync(string[] args)
    {
        if (args.Length != 2)
        {
            PrintUsage();
            return 2;
        }

        var engine = BuildEngine();
        var context = await BuildContextAsync(measureInput: false);
        var card = engine.Evaluate(context)
            .FirstOrDefault(entry => string.Equals(entry.Definition.Id, args[1], StringComparison.OrdinalIgnoreCase));
        if (card is null)
        {
            Console.Error.WriteLine($"No expert tweak with id '{args[1]}'.");
            return 2;
        }

        if (!card.CanApply)
        {
            Console.Error.WriteLine(
                card.BlockedReason ?? $"{card.Definition.Title} is already at its recommended value.");
            return 1;
        }

        var transaction = engine.Apply(card);
        Console.WriteLine(JsonSerializer.Serialize(transaction, JsonOptions));
        return transaction.State == TweakTransaction.StateApplied ? 0 : 1;
    }

    private static Task<int> ExpertRevertAsync(string[] args)
    {
        var engine = BuildEngine();
        if (args.Length == 2 && string.Equals(args[1], "all", StringComparison.OrdinalIgnoreCase))
        {
            var reverted = engine.RevertAll("CLI revert all");
            Console.WriteLine(JsonSerializer.Serialize(reverted, JsonOptions));
            return Task.FromResult(reverted.All(entry => entry.State == TweakTransaction.StateReverted) ? 0 : 1);
        }

        if (args.Length != 2 || !Guid.TryParse(args[1], out var transactionId))
        {
            PrintUsage();
            return Task.FromResult(2);
        }

        var transaction = engine.Revert(transactionId, "CLI revert");
        Console.WriteLine(JsonSerializer.Serialize(transaction, JsonOptions));
        return Task.FromResult(transaction.State == TweakTransaction.StateReverted ? 0 : 1);
    }

    private static int ExpertHistory()
    {
        Console.WriteLine(JsonSerializer.Serialize(BuildEngine().AllTransactions(), JsonOptions));
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("FramePath Lab research CLI");
        Console.WriteLine("  scan");
        Console.WriteLine("  analyze <capture.csv> [frame-budget-ms]");
        Console.WriteLine("  report <capture.csv> <new-report.md>");
        Console.WriteLine("  expert [--measure-input]       read-only expert-tier scan");
        Console.WriteLine("  expert-apply <tweak-id>        apply one tweak, journalled");
        Console.WriteLine("  expert-revert <id|all>         undo a recorded transaction");
        Console.WriteLine("  expert-history                 list every recorded transaction");
    }
}
