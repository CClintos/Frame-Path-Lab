using System.Net.NetworkInformation;
using FramePathLab.Core.Models;
using Microsoft.Win32;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Reads the NIC settings that add delay to a tick rather than the ones that change throughput.
/// Interrupt moderation deliberately batches receive interrupts to save CPU, and Energy Efficient
/// Ethernet parks the link between bursts; both trade a small amount of latency for power, which
/// is the wrong side of the trade for a competitive server tick.
/// </summary>
public static class NetworkAdapterScanner
{
    private const string NetworkClassPath =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    // Vendors do not agree on a single name for the energy-saving property.
    private static readonly string[] EnergyEfficiencyNames =
    [
        "*EEE",
        "EnableGreenEthernet",
        "AdvancedEEE",
        "EnableSavePowerNow",
        "*EnergyEfficientEthernet"
    ];

    public static IReadOnlyList<NetworkAdapterState> Scan()
    {
        var activeIds = ResolveActiveInterfaceIds();
        var results = new List<NetworkAdapterState>();

        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(NetworkClassPath, writable: false);
            if (classKey is null)
            {
                return results;
            }

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (!int.TryParse(subKeyName, out _))
                {
                    continue;
                }

                using var adapterKey = classKey.OpenSubKey(subKeyName, writable: false);
                if (adapterKey?.GetValue("NetCfgInstanceId") is not string instanceId)
                {
                    continue;
                }

                var isActive = activeIds.TryGetValue(instanceId, out var netInterface);
                if (!isActive)
                {
                    // Only adapters carrying live traffic are worth acting on.
                    continue;
                }

                var description = adapterKey.GetValue("DriverDesc") as string ?? netInterface!.Description;
                var moderation = ReadInt(adapterKey, "*InterruptModeration");
                var flowControl = ReadInt(adapterKey, "*FlowControl");
                var energy = EnergyEfficiencyNames
                    .Select(name => ReadInt(adapterKey, name))
                    .FirstOrDefault(value => value.HasValue);

                results.Add(new NetworkAdapterState(
                    netInterface!.Name,
                    description,
                    $@"HKLM\{NetworkClassPath}\{subKeyName}",
                    netInterface.NetworkInterfaceType is NetworkInterfaceType.Wireless80211,
                    true,
                    moderation,
                    energy,
                    flowControl,
                    BuildObservation(moderation, energy, flowControl)));
            }
        }
        catch (UnauthorizedAccessException)
        {
            return results;
        }
        catch (IOException)
        {
            return results;
        }

        return results;
    }

    private static string BuildObservation(int? moderation, int? energy, int? flowControl)
    {
        var parts = new List<string>
        {
            moderation switch
            {
                0 => "interrupt moderation disabled",
                null => "interrupt moderation not exposed",
                _ => "interrupt moderation enabled"
            }
        };

        if (energy.HasValue)
        {
            parts.Add(energy.Value == 0 ? "energy-efficient link off" : "energy-efficient link on");
        }

        if (flowControl.HasValue)
        {
            parts.Add(flowControl.Value == 0 ? "flow control off" : "flow control on");
        }

        return string.Join("; ", parts) + ".";
    }

    private static Dictionary<string, NetworkInterface> ResolveActiveInterfaceIds()
    {
        var map = new Dictionary<string, NetworkInterface>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (netInterface.OperationalStatus != OperationalStatus.Up
                    || netInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                map[netInterface.Id] = netInterface;
            }
        }
        catch (NetworkInformationException)
        {
            return map;
        }

        return map;
    }

    private static int? ReadInt(RegistryKey key, string name)
    {
        var raw = key.GetValue(name);
        return raw switch
        {
            int value => value,
            // Network class properties are frequently stored as REG_SZ decimal strings.
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => null
        };
    }
}
