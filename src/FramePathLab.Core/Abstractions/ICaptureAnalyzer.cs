using FramePathLab.Core.Models;

namespace FramePathLab.Core.Abstractions;

public interface ICaptureAnalyzer
{
    Task<CaptureAnalysis> AnalyzeAsync(
        string path,
        CaptureAnalysisOptions options,
        CancellationToken cancellationToken = default);
}
