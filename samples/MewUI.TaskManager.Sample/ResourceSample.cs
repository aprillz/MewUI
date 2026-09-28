using System.Globalization;

namespace Aprillz.MewUI.TaskManager.Sample;

internal enum ResourceKind
{
    Cpu,
    Memory,
    Disk,
    Network,
    Gpu,
}

/// <summary>A labeled value shown under a resource graph.</summary>
internal readonly record struct Metric(string Label, string Value);

/// <summary>
/// What one graph plots for a sample. A percentage runs on a fixed 0-100 scale; a rate (bytes per
/// second) scales to the largest value in view, and <paramref name="Secondary"/> is drawn dashed.
/// </summary>
internal sealed record ChartSample(
    string Label,
    double Primary,
    double? Secondary = null,
    bool IsRate = false,
    string? PrimaryName = null,
    string? SecondaryName = null);

/// <summary>A share of a whole, as the memory composition bar shows it.</summary>
internal readonly record struct CompositionPart(string Label, long Bytes, byte Alpha);

/// <summary>
/// Everything the performance page shows for one resource at one sample. The labels are the same from
/// sample to sample on one machine; the values change.
/// </summary>
internal sealed record ResourceSample(
    string Id,
    ResourceKind Kind,
    string Title,
    string Subtitle,
    string Summary,
    string Heading,
    ChartSample Chart,
    ChartSample? SecondChart,
    IReadOnlyList<Metric> Metrics,
    IReadOnlyList<Metric> Properties,
    IReadOnlyList<CompositionPart>? Composition = null,
    long CompositionTotal = 0);

internal static class Format
{
    private static readonly string[] _byteUnits = ["B", "KB", "MB", "GB", "TB"];
    private static readonly string[] _bitRateUnits = ["bps", "Kbps", "Mbps", "Gbps"];

    public static string Bytes(long bytes, int decimals = 1)
    {
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < _byteUnits.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{value:0} {_byteUnits[unit]}"
            : value.ToString("F" + decimals, CultureInfo.CurrentCulture) + " " + _byteUnits[unit];
    }

    public static string ByteRate(double bytesPerSecond) => Bytes((long)bytesPerSecond) + "/s";

    /// <summary>Network throughput in bits per second, as network monitors report it.</summary>
    public static string BitRate(double bytesPerSecond)
    {
        double value = Math.Max(0, bytesPerSecond * 8);
        int unit = 0;
        while (value >= 1000 && unit < _bitRateUnits.Length - 1)
        {
            value /= 1000;
            unit++;
        }
        return $"{value:0.#} {_bitRateUnits[unit]}";
    }

    public static string Gigabytes(long bytes) => (bytes / (1024d * 1024 * 1024)).ToString("0.0", CultureInfo.CurrentCulture);

    public static string Percent(double percent) => $"{percent:0}%";

    public static string Megahertz(double megahertz) => megahertz >= 1000
        ? $"{megahertz / 1000:0.00} GHz"
        : $"{megahertz:0} MHz";

    public static string Uptime(TimeSpan uptime) => $"{(int)uptime.TotalDays}:{uptime:hh\\:mm\\:ss}";

    public static string Count(long count) => count.ToString("N0", CultureInfo.CurrentCulture);
}
