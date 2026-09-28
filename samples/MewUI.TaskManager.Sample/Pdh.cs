using System.Runtime.InteropServices;

namespace Aprillz.MewUI.TaskManager.Sample;

/// <summary>
/// Windows performance counters (PDH). A rate counter needs two collections, so the first read after
/// adding a counter returns nothing and the values follow from the next sample on.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint FORMAT_DOUBLE = 0x00000200;
    private const uint FORMAT_NO_CAP_100 = 0x00008000;
    private const uint MORE_DATA = 0x800007D2;

    private nint _query;
    private readonly Dictionary<string, nint> _counters = [];

    public PdhQuery()
    {
        if (PdhOpenQueryW(null, 0, out _query) != 0) _query = 0;
    }

    public bool IsOpen => _query != 0;

    /// <summary>Adds a counter by its English path; false when this machine does not have it.</summary>
    public bool Add(string path)
    {
        if (_query == 0) return false;
        if (_counters.ContainsKey(path)) return true;
        if (PdhAddEnglishCounterW(_query, path, 0, out var counter) != 0) return false;
        _counters[path] = counter;
        return true;
    }

    public void Collect()
    {
        if (_query != 0) PdhCollectQueryData(_query);
    }

    public double? Read(string path)
    {
        if (!_counters.TryGetValue(path, out var counter)) return null;
        return PdhGetFormattedCounterValue(counter, FORMAT_DOUBLE | FORMAT_NO_CAP_100, out _, out var value) == 0 && value.Status == 0
            ? value.Value
            : null;
    }

    /// <summary>Reads every instance of a wildcard counter, by instance name.</summary>
    public IReadOnlyList<(string Instance, double Value)> ReadArray(string path)
    {
        if (!_counters.TryGetValue(path, out var counter)) return [];
        uint size = 0;
        uint status = PdhGetFormattedCounterArrayW(counter, FORMAT_DOUBLE | FORMAT_NO_CAP_100, ref size, out uint count, 0);
        if (status != MORE_DATA || size == 0) return [];

        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, FORMAT_DOUBLE | FORMAT_NO_CAP_100, ref size, out count, buffer) != 0) return [];
            int itemSize = Marshal.SizeOf<CounterValueItem>();
            var result = new List<(string, double)>((int)count);
            for (int index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<CounterValueItem>(buffer + index * itemSize);
                if (item.Value.Status == 0)
                    result.Add((Marshal.PtrToStringUni(item.Name) ?? string.Empty, item.Value.Value));
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_query != 0) PdhCloseQuery(_query);
        _query = 0;
        _counters.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValue
    {
        public uint Status;
        public double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValueItem
    {
        public nint Name;
        public CounterValue Value;
    }

    [DllImport("pdh", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [DllImport("pdh", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(nint query, string path, nint userData, out nint counter);

    [DllImport("pdh")]
    private static extern uint PdhCollectQueryData(nint query);

    [DllImport("pdh")]
    private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out CounterValue value);

    [DllImport("pdh", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, out uint itemCount, nint itemBuffer);

    [DllImport("pdh")]
    private static extern uint PdhCloseQuery(nint query);
}
