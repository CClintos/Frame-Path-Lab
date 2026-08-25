using FramePathLab.Core.Models;

namespace FramePathLab.Core.Abstractions;

public interface IEvidenceCatalog
{
    IReadOnlyList<FindingCard> Evaluate(EnvironmentSnapshot snapshot);
}
