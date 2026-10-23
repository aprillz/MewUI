using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A draggable handle. It draws only its chrome and reports how far the pointer has moved from the
/// press, in its parent's coordinates, so the control that owns it maps the drag to a value.
/// </summary>
public sealed class Thumb : Control
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<Thumb>(DefaultStyles.CreateThumbStyle);

    private static readonly MewProperty<bool> IsDraggingProperty =
        MewProperty<bool>.Register<Thumb>(nameof(IsDragging), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState);

    private Point _pressOrigin;
    private Action? _onCaptureLost;

    /// <summary>Gets whether a drag is in progress.</summary>
    public bool IsDragging => GetValue(IsDraggingProperty);

    /// <summary>Occurs when a press takes the mouse capture and a drag begins.</summary>
    public event Action? DragStarted;

    /// <summary>Occurs on each pointer move during a drag, with the total distance from the press point.</summary>
    public event Action<Vector>? DragDelta;

    /// <summary>Occurs when the drag ends; true when the capture was taken away instead of released.</summary>
    /// <remarks>Named apart from <see cref="UIElement.DragCompleted"/>, which belongs to drag and drop.</remarks>
    public event Action<bool>? DragEnded;

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        if (IsDragging)
        {
            state = state with { Flags = state.Flags | VisualStateFlags.Pressed };
        }

        return state;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Handled || !IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        if (FindVisualRoot() is not Window window || !window.CaptureMouse(this, _onCaptureLost ??= OnCaptureLost))
        {
            return;
        }

        _pressOrigin = e.Position;
        SetValue(IsDraggingProperty, true);
        DragStarted?.Invoke();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsDragging || !IsMouseCaptured)
        {
            return;
        }

        var position = e.Position;
        DragDelta?.Invoke(new Vector(position.X - _pressOrigin.X, position.Y - _pressOrigin.Y));
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left || !IsDragging)
        {
            return;
        }

        SetValue(IsDraggingProperty, false);
        if (IsMouseCaptured && FindVisualRoot() is Window window)
        {
            window.ReleaseMouseCapture();
        }

        DragEnded?.Invoke(false);
        e.Handled = true;
    }

    private void OnCaptureLost()
    {
        // A capture taken away never delivers the release that would end the drag.
        if (!IsDragging)
        {
            return;
        }

        SetValue(IsDraggingProperty, false);
        DragEnded?.Invoke(true);
    }
}
