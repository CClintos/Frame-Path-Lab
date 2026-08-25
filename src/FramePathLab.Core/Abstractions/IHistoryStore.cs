using FramePathLab.Core.Models;

namespace FramePathLab.Core.Abstractions;

public interface IHistoryStore
{
    Task<IReadOnlyList<HistoryEntry>> ReadAsync(CancellationToken cancellationToken = default);

    Task AppendAsync(HistoryEntry entry, CancellationToken cancellationToken = default);

    Task DeleteAllAsync(CancellationToken cancellationToken = default);
}
