namespace Aprillz.MewUI.Controls;

/// <summary>
/// Shared press/mouse-capture sequence for clickable controls (Button, ToggleButton, CheckBox,
/// RadioButton, ToggleSwitch, TabHeaderButton, SegmentButton). These controls derive from
/// different base classes, so this is composed (a field on each control) rather than a common
/// base class. Each control still owns its own guard conditions (button/enabled checks),
/// e.Handled assignment and click/toggle commit logic; this helper owns the mouse press itself:
/// pressed while the pointer is over the control, and whether the release lands pressed.
/// </summary>
internal sealed class PressCaptureHelper(UIElement owner, Action<bool> setPressed)
{
    // True from a press that took the capture until its release or the capture's loss; a keyboard press never sets it.
    private bool _mousePressActive;

    // Whether the pointer is over the control during the mouse press.
    private bool _pointerInside;

    // A capture taken away (pointer cancel, drag session, platform revoke) never delivers a mouse-up, so the press ends here.
    private Action? _onCaptureLost;

    /// <summary>
    /// Sets the pressed state, optionally runs a focus callback, then captures the mouse via the owning window.
    /// Returns false, with the pressed state cleared, when the control may not hold the capture. Call from
    /// OnMouseDown once the control's own button/enabled guard passes.
    /// </summary>
    public bool BeginPress(Action? focus = null)
    {
        setPressed(true);
        focus?.Invoke();

        // The focus move re-evaluates commands, which can disable the control and so refuse its capture.
        if (owner.FindVisualRoot() is Window window && window.CaptureMouse(owner, _onCaptureLost ??= OnCaptureLost))
        {
            _mousePressActive = true;
            _pointerInside = true;
            return true;
        }

        setPressed(false);
        return false;
    }

    /// <summary>
    /// Ends the mouse press: clears the pressed state and releases the capture. Returns whether the press was
    /// still pressed, with the pointer over the control, so the caller may commit a click or toggle.
    /// </summary>
    public bool EndPress()
    {
        bool wasPressed = _mousePressActive && _pointerInside;
        _mousePressActive = false;
        setPressed(false);

        if (owner.IsMouseCaptured && owner.FindVisualRoot() is Window window)
        {
            window.ReleaseMouseCapture();
        }

        return wasPressed;
    }

    /// <summary>Clears the pressed look while a mouse press is away from the control. Call from OnMouseLeave.</summary>
    public void PointerLeft()
    {
        if (!_mousePressActive)
        {
            return;
        }

        _pointerInside = false;
        setPressed(false);
    }

    /// <summary>Restores the pressed look when a mouse press returns over the control. Call from OnMouseEnter.</summary>
    public void PointerEntered()
    {
        if (!_mousePressActive)
        {
            return;
        }

        _pointerInside = true;
        setPressed(true);
    }

    private void OnCaptureLost()
    {
        _mousePressActive = false;
        setPressed(false);
    }
}
