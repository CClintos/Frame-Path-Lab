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
using FramePathLab.Windows.Benchmark;
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
                "expert-apply-all" => await ExpertApplyAllAsync(),
                "expert-verify" => await ExpertVerifyAsync(args),
                "expert-revert" => await ExpertRevertAsync(args),
                "expert-history" => ExpertHistory(),
                "benchmark" => Benchmark(args),
                "autotune" => await AutoTuneAsync(args),
                "abtest" => await AbTestAsync(args),
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
                context.CpuTuning,
                context.ReservedCpuSetObservation,
                context.UsbModerationObservation,
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

    private static async Task<int> ExpertApplyAllAsync()
    {
        var engine = BuildEngine();
        var context = await BuildContextAsync(measureInput: false);
        var cards = engine.Evaluate(context);
        var applied = engine.ApplyRecommendedDefaults(cards);

        if (applied.Count == 0)
        {
            Console.Error.WriteLine(
                "Nothing to apply: every recommended default is already set, or is blocked. Run 'expert' to see why.");
            return 1;
        }

        Console.WriteLine(JsonSerializer.Serialize(applied, JsonOptions));
        return applied.All(entry => entry.State == TweakTransaction.StateApplied) ? 0 : 1;
    }

    private static async Task<int> ExpertVerifyAsync(string[] args)
    {
        // expert-verify <transaction-id|any> <before.csv> <after.csv> [--revert-on-failure]
        if (args.Length is < 4 or > 5)
        {
            PrintUsage();
            return 2;
        }

        var analyzer = new PresentMonCsvAnalyzer();
        var before = await analyzer.AnalyzeAsync(args[2], new CaptureAnalysisOptions());
        var after = await analyzer.AnalyzeAsync(args[3], new CaptureAnalysisOptions());
        var revertOnFailure = args.Contains("--revert-on-failure", StringComparer.OrdinalIgnoreCase);

        if (string.Equals(args[1], "any", StringComparison.OrdinalIgnoreCase))
        {
            // Comparing two captures without attributing the difference to a recorded change.
            var standalone = TweakVerifier.Compare(before, after);
            Console.WriteLine(JsonSerializer.Serialize(standalone, JsonOptions));
            return standalone.Verdict == VerificationVerdict.NotComparable ? 2 : 0;
        }

        if (!Guid.TryParse(args[1], out var transactionId))
        {
            Console.Error.WriteLine("The first argument must be a transaction id or the word 'any'.");
            return 2;
        }

        var (verification, reverted) = BuildEngine().Verify(transactionId, before, after, revertOnFailure);
        Console.WriteLine(JsonSerializer.Serialize(new { verification, reverted }, JsonOptions));
        return verification.Verdict switch
        {
            VerificationVerdict.NotComparable => 2,
            VerificationVerdict.Regressed or VerificationVerdict.NoMeasuredChange => 1,
            _ => 0
        };
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

    private static async Task<int> AutoTuneAsync(string[] args)
    {
        var level = args.Contains("--aggressive", StringComparer.OrdinalIgnoreCase)
            ? AutoTuneLevel.Aggressive
            : args.Contains("--conservative", StringComparer.OrdinalIgnoreCase)
                ? AutoTuneLevel.Conservative
                : AutoTuneLevel.Balanced;

        var mode = args.Contains("--isolate", StringComparer.OrdinalIgnoreCase)
            ? AutoTuneMode.Isolate
            : AutoTuneMode.Bundle;

        var engine = BuildEngine();
        var context = await BuildContextAsync(measureInput: false);
        var cards = engine.Evaluate(context);

        var candidates = AutoTuneCoordinator.SelectCandidates(cards, level);
        Console.Error.WriteLine(
            $"Level {level}, mode {mode}: {candidates.Count} candidate(s).");
        if (mode == AutoTuneMode.Isolate && candidates.Count > 0)
        {
            Console.Error.WriteLine(
                $"Isolate mode measures each change separately: {candidates.Count + 1} benchmark runs.");
        }

        var progress = new Progress<string>(message => Console.Error.WriteLine($"  {message}"));
        var coordinator = new AutoTuneCoordinator(engine, new SyntheticBenchmarkRunner());
        var report = coordinator.Run(cards, level, mode, progress);

        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return report.Applied > 0 || report.CandidatesConsidered == 0 ? 0 : 1;
    }

    private static async Task<int> AbTestAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: abtest <tweak-id> [--pairs N]");
            return 2;
        }

        var pairs = 5;
        var pairsIndex = Array.FindIndex(args, a => a.Equals("--pairs", StringComparison.OrdinalIgnoreCase));
        if (pairsIndex >= 0 && pairsIndex + 1 < args.Length
            && int.TryParse(args[pairsIndex + 1], out var parsedPairs))
        {
            pairs = Math.Clamp(parsedPairs, 2, 12);
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

        Console.Error.WriteLine(
            $"Paired A/B on {card.Definition.Id}, up to {pairs} pairs ({pairs * 2} benchmark runs).");
        var progress = new Progress<string>(message => Console.Error.WriteLine($"  {message}"));
        var report = new AbTestRunner(engine, new SyntheticBenchmarkRunner()).Run(card, pairs, progress);

        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        return report.Verdict == "Abandoned" ? 1 : 0;
    }

    private static int Benchmark(string[] args)
    {
        var quick = args.Contains("--quick", StringComparer.OrdinalIgnoreCase);
        var options = quick ? BenchmarkOptions.Quick : BenchmarkOptions.Default;
        Console.Error.WriteLine($"Running a {options.TotalDuration.TotalSeconds:0} second benchmark...");

        var result = new SyntheticBenchmark().Run(options);
        if (!result.Succeeded)
        {
            Console.Error.WriteLine(result.Observation);
            return 1;
        }

        var analysis = BenchmarkAnalysis.ToAnalysis(result, quick ? "benchmark-quick" : "benchmark");
        Console.WriteLine(JsonSerializer.Serialize(analysis, JsonOptions));
        return 0;
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
        Console.WriteLine("  expert-apply-all               apply every recommended default this PC needs");
        Console.WriteLine("  expert-revert <id|all>         undo a recorded transaction");
        Console.WriteLine("  expert-history                 list every recorded transaction");
        Console.WriteLine("  benchmark [--quick]            run the self-contained frame benchmark");
        Console.WriteLine("  abtest <tweak-id> [--pairs N]  interleaved paired A/B on one change");
        Console.WriteLine("  autotune [--conservative|--aggressive] [--isolate]");
        Console.WriteLine("                                 measure, apply, re-measure, keep what earned it");
        Console.WriteLine("  expert-verify <id|any> <before.csv> <after.csv> [--revert-on-failure]");
        Console.WriteLine("                                 measure a change against two captures");
    }
}
