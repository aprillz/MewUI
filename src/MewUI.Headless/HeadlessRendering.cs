namespace Aprillz.MewUI.Headless;

/// <summary>
/// What a headless session does with the drawing of its windows.
/// </summary>
public enum HeadlessRendering
{
    /// <summary>Fonts are measured by the graphics backend and layout runs; drawing calls are discarded.</summary>
    MeasureOnly,

    /// <summary>Every frame is drawn into an offscreen surface of the graphics backend, readable through <see cref="HeadlessWindowExtensions.CaptureFrame"/>.</summary>
    Offscreen,
}
