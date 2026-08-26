using FramePathLab.Core.Models;
using Microsoft.Win32;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Reads service start configuration and, critically, the reverse dependency graph.
///
/// The way a service change actually breaks a machine is not picking the wrong service — it is
/// picking one that something else silently requires. Windows records dependencies in one
/// direction only: each service lists what it needs. To answer "what would break if this stopped"
/// the whole set has to be walked and inverted. That is done once per scan and is what lets a
/// candidate be refused rather than merely warned about.
/// </summary>
public static class ServiceStateScanner
{
    private const string ServicesRoot = @"SYSTEM\CurrentControlSet\Services";

    /// <summary>Start type values as the service control manager stores them.</summary>
    public const int StartBoot = 0;
    public const int StartSystem = 1;
    public const int StartAutomatic = 2;
    public const int StartManual = 3;
    public const int StartDisabled = 4;

    public static ServiceInventory Scan()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(ServicesRoot, writable: false);
            if (root is null)
            {
                return ServiceInventory.Unavailable("The service configuration could not be read.");
            }

            var states = new Dictionary<string, ServiceState>(StringComparer.OrdinalIgnoreCase);
            var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name, writable: false);
                if (key is null)
                {
                    continue;
                }

                // A key without a start type is not a service, it is a driver stub or a parameter
                // container, and there are many of those under the same root.
                if (key.GetValue("Start") is not int start)
                {
                    continue;
                }

                var display = key.GetValue("DisplayName") as string ?? name;
                states[name] = new ServiceState(
                    name,
                    display.StartsWith('@') ? name : display,
                    start,
                    key.GetValue("Type") as int? ?? 0);

                // Invert the graph: this service's requirements become other services' dependents.
                if (key.GetValue("DependOnService") is string[] requires)
                {
                    foreach (var required in requires.Where(entry => !string.IsNullOrWhiteSpace(entry)))
                    {
                        if (!dependents.TryGetValue(required, out var list))
                        {
                            list = [];
                            dependents[required] = list;
                        }

                        list.Add(name);
                    }
                }
            }

            return new ServiceInventory(
                true,
                states,
                dependents.ToDictionary(
                    entry => entry.Key,
                    entry => (IReadOnlyList<string>)entry.Value,
                    StringComparer.OrdinalIgnoreCase),
                $"{states.Count} service(s) enumerated.");
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                             or System.Security.SecurityException or IOException)
        {
            return ServiceInventory.Unavailable($"Service configuration could not be read: {exception.Message}");
        }
    }
}
