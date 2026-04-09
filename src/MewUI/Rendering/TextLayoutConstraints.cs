namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Layout constraints for text measurement and drawing.
/// Input constraint is expressed as a single <see cref="Rect"/>; derived values
/// (effective max width, etc.) are computed by the layout helper.
/// <para>
/// Convention:<br/>
/// • <c>DrawText(...)</c>: actual bounds.<br/>
/// • <c>MeasureText(text, font, maxWidth)</c>: <c>Rect(0, 0, maxWidth, 0)</c>.<br/>
/// • <c>MeasureText(text, font)</c>: <c>Rect(0, 0, PositiveInfinity, 0)</c>.
/// </para>
/// </summary>
public readonly record struct TextLayoutConstraints(Rect Bounds);
