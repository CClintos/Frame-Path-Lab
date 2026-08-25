using FramePathLab.Core.Models;

namespace FramePathLab.Core.Abstractions;

public interface IPowerSessionJournal
{
    PowerSessionRecord? Read();

    void Write(PowerSessionRecord record);
}
