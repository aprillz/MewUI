namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>Flags selecting which anti-aliasing stages <see cref="GdiDebug"/> dumps to disk.</summary>
[Flags]
public enum GdiDumpStages
{
    /// <summary>Nothing is dumped.</summary>
    None = 0,
    /// <summary>The anti-aliased coverage surface.</summary>
    AaSurface = 1 << 0,
    /// <summary>The alpha channel of the coverage surface.</summary>
    AaSurfaceAlpha = 1 << 1,
    /// <summary>The destination before the blend.</summary>
    DestBeforeBlend = 1 << 2,
    /// <summary>The destination after the blend.</summary>
    DestAfterBlend = 1 << 3,
}

/// <summary>Diagnostic switches for the GDI backend's anti-aliasing path.</summary>
public static class GdiDebug
{
    /// <summary>Gets or sets the directory anti-aliasing dumps are written to; <see langword="null"/> disables dumping.</summary>
    public static string? DumpAaDirectory { get; set; }

    /// <summary>Gets or sets the maximum number of dump files written per process; the default is 50.</summary>
    public static int DumpAaMaxFiles { get; set; } = 50;

    /// <summary>Gets or sets which stages are dumped; the default is <see cref="GdiDumpStages.None"/>.</summary>
    public static GdiDumpStages DumpStages { get; set; } = GdiDumpStages.None;

    // When enabled, very thin non-axis-aligned lines can use a faster Wu AA path
    // instead of SSAA. Disabled by default because SSAA produces higher quality.
    /// <summary>
    /// Gets or sets whether very thin non-axis-aligned lines use the faster Wu anti-aliasing instead of
    /// supersampling; off by default because supersampling looks better.
    /// </summary>
    public static bool UseWuLineAa { get; set; }

    private static int _dumpAaCount;

    internal static bool TryGetNextAaDumpIndex(out int index)
    {
        index = 0;

        if (string.IsNullOrWhiteSpace(DumpAaDirectory))
        {
            return false;
        }

        if (_dumpAaCount >= DumpAaMaxFiles)
        {
            return false;
        }

        index = _dumpAaCount++;
        return true;
    }
}
