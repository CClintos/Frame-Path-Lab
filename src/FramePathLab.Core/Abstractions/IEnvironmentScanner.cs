using FramePathLab.Core.Models;

namespace FramePathLab.Core.Abstractions;

public interface IEnvironmentScanner
{
    Task<EnvironmentSnapshot> ScanAsync(CancellationToken cancellationToken = default);
}
