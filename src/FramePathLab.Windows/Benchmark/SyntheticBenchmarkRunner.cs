using FramePathLab.Core.Abstractions;
using FramePathLab.Core.Models;

namespace FramePathLab.Windows.Benchmark;

/// <summary>Adapts the self-run benchmark to the orchestration's measurement contract.</summary>
public sealed class SyntheticBenchmarkRunner : IBenchmarkRunner
{
    private readonly BenchmarkOptions _options;
    private readonly SyntheticBenchmark _benchmark = new();
    private int _run;

    public SyntheticBenchmarkRunner(BenchmarkOptions? options = null)
        => _options = options ?? BenchmarkOptions.Default;

    public CaptureAnalysis Run(CancellationToken cancellationToken = default)
    {
        // Each run is labelled distinctly so two measurements are never mistaken for the same one,
        // which the comparison refuses outright.
        var label = $"benchmark-{Interlocked.Increment(ref _run):00}";
        return BenchmarkAnalysis.ToAnalysis(_benchmark.Run(_options, cancellationToken), label);
    }
}
