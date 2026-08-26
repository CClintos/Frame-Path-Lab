using System.Reflection;
using FramePathLab.Core.Models;
using FramePathLab.Core.Evidence;
using FramePathLab.Core.Persistence;
using FramePathLab.Core.Services;
using FramePathLab.Windows.Mutation;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Produces a portable reading of the machine this is running on.
///
/// Collection is deliberately a single pass with no prompts and no writes. It has to be something
/// a person will actually run on the computer they care about — copy one file across, double-click,
/// wait, copy one file back — rather than a second installation to maintain.
/// </summary>
public static class MachineSnapshotCollector
{
    /// <summary>
    /// Scans, then evaluates the whole catalogue through a recording reader so that every question
    /// the catalogue asks is answered inside the file.
    /// </summary>
    /// <param name="measureInput">
    /// Input latency needs someone moving the mouse while it measures. A collection run
    /// unattended should skip it rather than record a fabricated figure.
    /// </param>
    public static async Task<MachineSnapshot> CollectAsync(
        bool measureInput,
        TimeSpan inputDuration,
        CancellationToken cancellationToken = default)
    {
        var environment = (await new ScanCoordinator(
                new WindowsEnvironmentScanner(),
                new DefaultEvidenceCatalog())
            .RunAsync(cancellationToken).ConfigureAwait(false)).Snapshot;

        var context = await new ExpertScanCoordinator().ScanAsync(
            environment,
            measureInput,
            measureScheduler: true,
            measureNetwork: true,
            inputDuration,
            cancellationToken).ConfigureAwait(false);

        // Evaluating through the recorder is what makes the snapshot self-sufficient. The result is
        // discarded here; the value is the transcript of reads it leaves behind.
        var recorder = new RecordingStateReader(new WindowsMutationExecutor());
        _ = ExpertTweakCatalog.Evaluate(context, recorder);

        return new MachineSnapshot(
            MachineSnapshot.CurrentFormatVersion,
            DateTimeOffset.UtcNow,
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            environment.IsProcessElevated,
            MachineSnapshotStore.IdentityFor(context),
            context,
            recorder.Recorded);
    }
}
