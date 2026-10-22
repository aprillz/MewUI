namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>Anti-aliasing quality for curved strokes and fills on the GDI backend.</summary>
public enum GdiCurveQuality
{
    /// <summary>No supersampling.</summary>
    Fast = 0,
    /// <summary>2x2 supersampling.</summary>
    Supersample2x = 2,
    /// <summary>3x3 supersampling.</summary>
    Supersample3x = 3
}

