using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Aprillz.MewUI.TaskManager.Sample;

/// <summary>
/// Network adapters that are up, with their send and receive rates. .NET reports the byte counters on
/// every platform; only the Wi-Fi check needs the platform on Linux.
/// </summary>
internal sealed class NetworkReader
{
    private readonly Dictionary<string, (long Sent, long Received, long Timestamp)> _previous = [];
    // Read again only when the adapter list changes; the registry walk is too slow for every sample.
    private HashSet<string> _excluded = [];
    private string? _excludedFor;

    public IReadOnlyList<ResourceSample> Read()
    {
        NetworkInterface[] adapters;
        try { adapters = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { return []; }

        long now = Stopwatch.GetTimestamp();
        var result = new List<ResourceSample>();
        var seen = new HashSet<string>();
        var hidden = OperatingSystem.IsWindows() ? ExcludedOnWindows(adapters) : [];
        foreach (var adapter in adapters)
        {
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                hidden.Contains(adapter.Id))
                continue;

            long sent, received;
            try
            {
                var statistics = adapter.GetIPStatistics();
                sent = statistics.BytesSent;
                received = statistics.BytesReceived;
            }
            catch
            {
                continue;
            }

            // Linux and macOS list tunnels, peer-to-peer links and idle bridges as interfaces too; those
            // with an address someone can reach are the connections a user thinks of.
            var addresses = SafeAddresses(adapter);
            if (!OperatingSystem.IsWindows() && addresses.Count == 0) continue;

            string id = "net:" + adapter.Id;
            seen.Add(id);
            double sendRate = 0, receiveRate = 0;
            if (_previous.TryGetValue(id, out var previous))
            {
                double seconds = (now - previous.Timestamp) / (double)Stopwatch.Frequency;
                if (seconds > 0)
                {
                    sendRate = Math.Max(0, sent - previous.Sent) / seconds;
                    receiveRate = Math.Max(0, received - previous.Received) / seconds;
                }
            }
            _previous[id] = (sent, received, now);

            bool wireless = IsWireless(adapter);
            string kind = wireless ? "Wi-Fi" : adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet" : adapter.NetworkInterfaceType.ToString();
            var metrics = new List<Metric>
            {
                new("Send", Format.BitRate(sendRate)),
                new("Receive", Format.BitRate(receiveRate)),
            };
            var properties = new List<Metric>
            {
                new("Adapter name", adapter.Name),
                new("Connection type", kind),
            };
            if (adapter.Speed > 0) properties.Add(new Metric("Link speed", Format.BitRate(adapter.Speed / 8.0)));
            var ipv4 = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 != null) properties.Add(new Metric("IPv4 address", ipv4.ToString()));
            var ipv6 = addresses.FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetworkV6);
            if (ipv6 != null) properties.Add(new Metric("IPv6 address", ipv6.ToString()));

            result.Add(new ResourceSample(
                id,
                ResourceKind.Network,
                // Windows names the kind and gives the adapter below it; elsewhere the interface name is what people know.
                OperatingSystem.IsWindows() ? kind : adapter.Name,
                OperatingSystem.IsWindows() ? adapter.Name : kind,
                $"S: {Format.BitRate(sendRate)} R: {Format.BitRate(receiveRate)}",
                adapter.Description,
                new ChartSample("Throughput", receiveRate, sendRate, IsRate: true, PrimaryName: "Receive", SecondaryName: "Send"),
                null,
                metrics,
                properties));
        }

        foreach (var id in _previous.Keys.Where(id => !seen.Contains(id)).ToArray()) _previous.Remove(id);
        return result;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private HashSet<string> ExcludedOnWindows(NetworkInterface[] adapters)
    {
        string adapterIds = string.Join('|', adapters.Select(adapter => adapter.Id));
        if (adapterIds != _excludedFor)
        {
            _excluded = WindowsFilterInterfaces();
            _excludedFor = adapterIds;
        }
        return _excluded;
    }

    /// <summary>
    /// The interfaces Windows lists besides the adapters: a filter driver bound to an adapter shows as
    /// an interface of its own with the adapter's traffic, and Task Manager leaves those out.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static HashSet<string> WindowsFilterInterfaces()
    {
        // MIB_IF_ROW2 (64-bit): InterfaceGuid at 12, InterfaceAndOperStatusFlags at 1152, 1352 bytes per row.
        const int ROW_SIZE = 1352;
        const int GUID_OFFSET = 12;
        const int FLAGS_OFFSET = 1152;
        const byte FILTER_INTERFACE = 1 << 1;
        var result = WindowsHiddenAdapters();
        if (GetIfTable2(out nint table) != 0 || table == 0) return result;
        try
        {
            int count = System.Runtime.InteropServices.Marshal.ReadInt32(table);
            for (int index = 0; index < count; index++)
            {
                nint row = table + 8 + index * ROW_SIZE;
                if ((System.Runtime.InteropServices.Marshal.ReadByte(row, FLAGS_OFFSET) & FILTER_INTERFACE) == 0) continue;
                var guid = new byte[16];
                System.Runtime.InteropServices.Marshal.Copy(row + GUID_OFFSET, guid, 0, 16);
                result.Add(new Guid(guid).ToString("B"));
            }
        }
        finally
        {
            FreeMibTable(table);
        }
        return result;
    }

    /// <summary>
    /// Adapters whose driver marks them hidden (NCF_HIDDEN): WAN miniports, switch extensions. Get-NetAdapter
    /// and Task Manager leave them out.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static HashSet<string> WindowsHiddenAdapters()
    {
        const int NCF_HIDDEN = 0x8;
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var adapters = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}");
        if (adapters == null) return result;
        foreach (var name in adapters.GetSubKeyNames())
        {
            // Adapters are the numbered keys; the "Properties" key beside them denies read access.
            if (!name.All(char.IsAsciiDigit)) continue;
            try
            {
                using var adapter = adapters.OpenSubKey(name);
                if (adapter?.GetValue("NetCfgInstanceId") is string id &&
                    adapter.GetValue("Characteristics") is int characteristics &&
                    (characteristics & NCF_HIDDEN) != 0)
                    result.Add(id);
            }
            catch { }
        }
        return result;
    }

    [System.Runtime.InteropServices.DllImport("iphlpapi")]
    private static extern int GetIfTable2(out nint table);

    [System.Runtime.InteropServices.DllImport("iphlpapi")]
    private static extern void FreeMibTable(nint table);

    private static bool IsWireless(NetworkInterface adapter)
    {
        if (adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return true;
        // .NET reports a Linux Wi-Fi interface as Ethernet; the kernel marks it with a wireless directory.
        return OperatingSystem.IsLinux() && Directory.Exists($"/sys/class/net/{adapter.Name}/wireless");
    }

    private static List<System.Net.IPAddress> SafeAddresses(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().UnicastAddresses
                .Select(address => address.Address)
                .Where(address => !address.IsIPv6LinkLocal)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
