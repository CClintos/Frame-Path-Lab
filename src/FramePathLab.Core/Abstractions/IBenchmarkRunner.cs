using FramePathLab.Core.Models;

namespace FramePathLab.Core.Abstractions;

/// <summary>
/// Produces a frame-delivery measurement the application can take itself.
///
/// Abstracted so the orchestration does not depend on how the frames are produced: a self-run
/// benchmark and an imported capture describe the same thing, and both go through one comparison
/// path rather than two.
/// </summary>
public interface IBenchmarkRunner
{
    CaptureAnalysis Run(CancellationToken cancellationToken = default);
}
