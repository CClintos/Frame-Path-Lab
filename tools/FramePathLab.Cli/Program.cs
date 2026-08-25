using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FramePathLab.Core.Analysis;
using FramePathLab.Core.Evidence;
using FramePathLab.Core.Reporting;
using FramePathLab.Core.Services;
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

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("FramePath Lab research CLI (read-only)");
        Console.WriteLine("  scan");
        Console.WriteLine("  analyze <capture.csv> [frame-budget-ms]");
        Console.WriteLine("  report <capture.csv> <new-report.md>");
    }
}
