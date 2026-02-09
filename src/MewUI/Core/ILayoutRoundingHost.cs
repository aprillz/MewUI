namespace Aprillz.MewUI;

/// <summary>
/// Provides layout rounding settings for controls that align geometry and layout to device pixels.
/// </summary>
internal interface ILayoutRoundingHost
{
    /// <summary>
    /// Gets whether layout rounding is enabled.
    /// </summary>
    bool UseLayoutRounding { get; }

    /// <summary>
    /// Gets the DPI scale factor (96 DPI = 1.0).
    /// </summary>
    double DpiScale { get; }
}
