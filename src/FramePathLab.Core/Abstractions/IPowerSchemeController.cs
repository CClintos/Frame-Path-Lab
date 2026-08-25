using FramePathLab.Core.Models;

namespace FramePathLab.Core.Abstractions;

public interface IPowerSchemeController
{
    IReadOnlyList<PowerSchemeDescriptor> EnumerateSchemes();

    Guid GetActiveScheme();

    void EnsureCanSetActiveScheme(Guid schemeId);

    void SetActiveScheme(Guid schemeId);
}
