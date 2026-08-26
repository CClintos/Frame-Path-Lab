using System.Net.NetworkInformation;
using FramePathLab.Core.Models;
using Microsoft.Win32;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Inventories latency-relevant properties on the physical interface carrying a default route.
/// The values remain diagnostic: vendor property names and effective-state semantics differ, and a
/// direct class-registry write is not a supported way to configure or verify a network adapter.
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
                var coalescing = ReadInt(adapterKey, "*RscIPv4");
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
                    coalescing,
                    BuildObservation(moderation, energy, flowControl, coalescing)));
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

    private static string BuildObservation(int? moderation, int? energy, int? flowControl, int? coalescing)
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

        if (coalescing.HasValue)
        {
            parts.Add(coalescing.Value == 0 ? "receive coalescing off" : "receive coalescing on");
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
                    || netInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback
                        or NetworkInterfaceType.Tunnel
                        or NetworkInterfaceType.Ppp)
                {
                    continue;
                }

                IPInterfaceProperties properties;
                try
                {
                    properties = netInterface.GetIPProperties();
                }
                catch (NetworkInformationException)
                {
                    continue;
                }

                // OperationalStatus.Up includes disconnected WAN miniports and virtual adapters.
                // A usable default gateway is a tighter, reproducible proxy for the route that can
                // actually carry the game connection without parsing localized route.exe output.
                var hasDefaultGateway = properties.GatewayAddresses.Any(gateway =>
                    !gateway.Address.Equals(System.Net.IPAddress.Any)
                    && !gateway.Address.Equals(System.Net.IPAddress.IPv6Any)
                    && !gateway.Address.Equals(System.Net.IPAddress.None));
                if (!hasDefaultGateway)
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
