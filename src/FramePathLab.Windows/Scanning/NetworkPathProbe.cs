using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FramePathLab.Core.Models;
using FramePathLab.Core.Statistics;

namespace FramePathLab.Windows.Scanning;

/// <summary>
/// Measures the stability of the first network hop.
///
/// Bandwidth is almost never what limits a competitive session; jitter is. A link that averages a
/// fine round-trip but scatters it moves the arrival time of every server tick, and players
/// experience that as shots not registering rather than as a network problem.
///
/// This deliberately measures the local gateway rather than a game server. The gateway hop is
/// where a wireless link, a failing cable, a powerline adapter or a congested uplink actually
/// shows up, and it is the part the player can do something about. It is not a measurement of the
/// route to any game server, and it is reported as such.
/// </summary>
public static class NetworkPathProbe
{
    private const int DefaultSampleCount = 30;
    private const int TimeoutMilliseconds = 500;

    /// <summary>Spacing between probes, short enough to finish quickly without flooding the hop.</summary>
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMilliseconds(60);

    public static NetworkPathQuality Measure(
        int sampleCount = DefaultSampleCount,
        CancellationToken cancellationToken = default)
    {
        var gateway = ResolveDefaultGateway();
        if (gateway is null)
        {
            return new NetworkPathQuality(
                false, "none", 0, 0, 0, 0, 0, 0,
                "No default gateway was resolved, so the local path could not be measured.");
        }

        var samples = new List<double>(sampleCount);
        var sent = 0;
        var received = 0;

        try
        {
            using var ping = new Ping();
            for (var index = 0; index < sampleCount; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                sent++;
                try
                {
                    var reply = ping.Send(gateway, TimeoutMilliseconds);
                    if (reply.Status == IPStatus.Success)
                    {
                        received++;
                        samples.Add(reply.RoundtripTime);
                    }
                }
                catch (PingException)
                {
                    // A single failed probe is data, not an error; it counts as loss.
                }

                if (index < sampleCount - 1)
                {
                    Thread.Sleep(ProbeInterval);
                }
            }
        }
        catch (Exception exception) when (exception is PingException or SocketException or PlatformNotSupportedException)
        {
            return new NetworkPathQuality(
                false, gateway.ToString(), sent, received, 0, 0, 0, 0,
                $"The local path could not be measured: {exception.Message}");
        }

        if (samples.Count < 5)
        {
            return new NetworkPathQuality(
                false, gateway.ToString(), sent, received, 0, 0, 0, 0,
                $"Only {samples.Count} of {sent} probes returned; the gateway may not answer them.");
        }

        var sorted = samples.Order().ToArray();
        var median = DescriptiveStatistics.QuantileR7(sorted, 0.5);
        var jitter = DescriptiveStatistics.SampleStandardDeviation(sorted);
        var p99 = DescriptiveStatistics.QuantileR7(sorted, 0.99);
        var loss = 100d * (sent - received) / sent;

        return new NetworkPathQuality(
            true,
            gateway.ToString(),
            sent,
            received,
            median,
            jitter,
            p99,
            sorted[^1],
            $"{received} of {sent} probes to the gateway; median {median:0.#} ms, "
            + $"jitter {jitter:0.##} ms, worst {sorted[^1]:0.#} ms, loss {loss:0.#}%.");
    }

    private static IPAddress? ResolveDefaultGateway()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up
                                  && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(adapter => adapter.GetIPProperties().GatewayAddresses)
                .Select(gateway => gateway.Address)
                .FirstOrDefault(address =>
                    address.AddressFamily == AddressFamily.InterNetwork
                    && !address.Equals(IPAddress.Any));
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }
}
