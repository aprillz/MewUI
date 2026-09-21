namespace Aprillz.MewUI.Rendering.DirectWrite;

/// <summary>
/// Process-wide entry point the Win32 backends use to create DirectWrite fonts.
/// </summary>
/// <remarks>
/// Deliberately not a field on the graphics factories. A field typed as a DirectWrite class, or a
/// dispose call on one, stays reachable even when the trimmer folds the gate away, which keeps the
/// whole DirectWrite graph in a GDI-only publish. Reaching it only through calls inside the gated
/// branch is what lets the trimmer drop it.
/// </remarks>
internal static class Win32DirectWriteFonts
{
    private static DirectWriteFontFactory? _factory;

    internal static IFont CreateFont(string family, double size, FontWeight weight,
        bool italic, bool underline, bool strikethrough, uint dpi)
        => (_factory ??= new DirectWriteFontFactory())
            .CreateFont(family, size, weight, italic, underline, strikethrough, dpi);

    internal static void DisposeIfCreated()
    {
        _factory?.Dispose();
        _factory = null;
    }
}
