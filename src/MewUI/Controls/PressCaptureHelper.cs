namespace Aprillz.MewUI.Controls;

/// <summary>
/// Shared press/mouse-capture sequence for clickable controls (Button, ToggleButton, CheckBox,
/// RadioButton, ToggleSwitch, TabHeaderButton, SegmentButton). These controls derive from
/// different base classes, so this is composed (a field on each control) rather than a common
/// base class. Each control still owns its own guard conditions (button/enabled checks),
/// e.Handled assignment and click/toggle commit logic; this helper only wraps the identical
/// "set pressed, optionally focus, capture mouse" / "clear pressed, release capture" bookkeeping.
/// </summary>
internal sealed class PressCaptureHelper(UIElement owner, Action<bool> setPressed)
{
    /// <summary>
    /// Sets the pressed state, optionally runs a focus callback, then captures the mouse via the
    /// owning window. Call from OnMouseDown once the control's own button/enabled guard passes.
    /// </summary>
    public void BeginPress(Action? focus = null)
    {
        setPressed(true);
        focus?.Invoke();

        if (owner.FindVisualRoot() is Window window)
        {
            window.CaptureMouse(owner);
        }
    }

    /// <summary>
    /// Clears the pressed state and releases the mouse capture. Call from OnMouseUp once the
    /// control's own button/IsPressed guard passes. The caller decides whether the release
    /// commits a click/toggle (typically gated on IsEffectivelyEnabled and Bounds.Contains).
    /// </summary>
    public void EndPress()
    {
        setPressed(false);

        if (owner.FindVisualRoot() is Window window)
        {
            window.ReleaseMouseCapture();
        }
    }

    /// <summary>
    /// Clears the pressed state without touching mouse capture. Call from OnMouseLeave.
    /// </summary>
    public void CancelPress() => setPressed(false);
}
