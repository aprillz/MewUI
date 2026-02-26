namespace Aprillz.MewUI;

/// <summary>
/// Runtime tuning switches intended for local debugging/profiling.
/// Avoid environment-variable toggles so apps can configure behavior in code.
/// </summary>
public static class GraphicsRuntimeOptions
{
    /// <summary>
    /// Enables diagnostic logging via <see cref="DiagLog"/>.
    /// </summary>
    public static bool DiagnosticsEnabled { get; set; }

    /// <summary>
    /// When enabled, diagnostic logs may include short excerpts of text content for cache-miss investigation.
    /// Keep disabled by default to avoid leaking user content into logs.
    /// </summary>
    public static bool DiagnosticsIncludeTextContent { get; set; }

    /// <summary>
    /// Maximum number of characters to include when <see cref="DiagnosticsIncludeTextContent"/> is enabled.
    /// </summary>
    public static int DiagnosticsTextMaxChars { get; set; } = 48;

    /// <summary>
    /// Preferred MSAA sample count for Win32 OpenGL pixel format selection.
    /// Use 0/1 to disable MSAA; typical values are 2/4/8.
    /// </summary>
    public static int PreferredMsaaSamples { get; set; } = 0;

    /// <summary>
    /// Preferred stencil bits for the MewVG Win32 backend pixel format selection.
    /// NanoVG typically uses stencil for AA and clipping; set to 0 to test memory impact (may reduce quality).
    /// </summary>
    public static int PreferredMewVGStencilBits { get; set; } = 8;

}
