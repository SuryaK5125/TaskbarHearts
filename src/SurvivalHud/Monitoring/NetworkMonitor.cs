using System.Net.NetworkInformation;

namespace SurvivalHud.Monitoring;

/// <summary>
/// Live internet throughput from the byte counters of the physical network adapters (the same
/// counters Task Manager uses). Nothing is downloaded or uploaded to measure it. Virtual,
/// loopback and tunnel adapters are skipped so traffic routed through them is not counted twice.
/// </summary>
public sealed class NetworkMonitor
{
    private static readonly string[] VirtualHints =
        { "virtual", "hyper-v", "vethernet", "vmware", "virtualbox", "wsl", "loopback", "pseudo", "tap-", "wireguard", "vpn", "npcap", "bluetooth" };

    private long _lastRx = -1, _lastTx = -1, _lastTicks;

    /// <summary>True when a physical Ethernet adapter was connected at the last sample.</summary>
    public bool WiredUp { get; private set; }

    /// <summary>True when any physical adapter was connected at the last sample.</summary>
    public bool AnyUp { get; private set; }

    /// <summary>Bytes per second received and sent since the previous call; null on the first call or when unavailable.</summary>
    public (double Down, double Up)? Sample()
    {
        long rx = 0, tx = 0;
        bool wired = false, any = false;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel or NetworkInterfaceType.Unknown) continue;
                string d = (nic.Description + " " + nic.Name).ToLowerInvariant();
                if (Array.Exists(VirtualHints, d.Contains)) continue;
                any = true;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet) wired = true;
                var s = nic.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
        }
        catch { return null; }
        WiredUp = wired;
        AnyUp = any;

        long now = Environment.TickCount64;
        (double, double)? result = null;
        // Only a recent baseline gives a meaningful rate; after a pause the first call just restarts it.
        if (_lastRx >= 0 && now > _lastTicks && now - _lastTicks <= 5000 && rx >= _lastRx && tx >= _lastTx)
        {
            double sec = (now - _lastTicks) / 1000.0;
            result = ((rx - _lastRx) / sec, (tx - _lastTx) / sec);
        }
        (_lastRx, _lastTx, _lastTicks) = (rx, tx, now);
        return result;
    }
}
