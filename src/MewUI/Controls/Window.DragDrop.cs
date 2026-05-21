namespace Aprillz.MewUI;

public partial class Window
{
    /// <summary>
    /// Acquires backend-level mouse capture for the duration of a drag session.
    /// Does not affect the element-level <see cref="CapturedElement"/> state, since drag routing
    /// uses its own resolution (global cursor + cross-window registry in Phase 4).
    /// </summary>
    internal void CaptureMouseForDrag()
    {
        EnsureBackend();
        if (Backend?.Handle == 0) return;
        Backend?.CaptureMouse();
    }

    /// <summary>Releases the backend-level mouse capture acquired by <see cref="CaptureMouseForDrag"/>.</summary>
    internal void ReleaseMouseAfterDrag()
    {
        Backend?.ReleaseMouseCapture();
    }

    /// <summary>
    /// Pushes the current <see cref="Controls.UIElement.AllowDrop"/> value down to the backend so the
    /// platform registers/unregisters its native drop target. Called whenever AllowDrop changes and
    /// once after the backend is attached.
    /// </summary>
    internal void SyncAllowDropToBackend()
    {
        Backend?.SetAllowDrop(AllowDrop);
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);

        if (property == AllowDropProperty)
        {
            SyncAllowDropToBackend();
        }
    }
}
