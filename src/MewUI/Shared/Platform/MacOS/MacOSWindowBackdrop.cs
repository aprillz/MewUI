using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>
/// Puts an <c>NSVisualEffectView</c> that blends with what is behind the window under the view the frames are drawn
/// into. Both go into a plain container that takes the content view's place; the drawing view stays on top, so it
/// keeps every event and the first responder it had.
/// </summary>
internal sealed class MacOSWindowBackdrop
{
    // NSVisualEffectMaterial values; both adapt to the light and dark appearance.
    private const ulong MATERIAL_POPOVER = 6;
    private const ulong MATERIAL_HUD_WINDOW = 13;
    private const ulong BLENDING_BEHIND_WINDOW = 0;
    // Active always: the default follows the window's key state and turns the material flat in a background window.
    private const ulong STATE_ACTIVE = 1;
    private const ulong WIDTH_SIZABLE = 2;
    private const ulong HEIGHT_SIZABLE = 16;

    private readonly nint _window;
    private readonly nint _drawingView;
    private nint _container;
    private nint _effectView;

    public MacOSWindowBackdrop(nint window, nint drawingView)
    {
        _window = window;
        _drawingView = drawingView;
    }

    /// <summary>
    /// Shows <paramref name="material"/> behind the drawing view, on <paramref name="area"/> or the whole view, and returns
    /// the one shown. Mica, a wallpaper material of Windows, has no counterpart here and comes down to the acrylic.
    /// </summary>
    public SystemBackdrop Apply(SystemBackdrop material, SystemBackdropArea? area)
    {
        if (material == SystemBackdrop.None)
        {
            Remove();
            return SystemBackdrop.None;
        }

        if (_effectView == 0 && !Insert())
        {
            return SystemBackdrop.None;
        }

        var shown = material == SystemBackdrop.Blur ? SystemBackdrop.Blur : SystemBackdrop.Acrylic;
        ObjC.MsgSend_void_nint_ulong(_effectView, ObjC.Sel("setMaterial:"), shown == SystemBackdrop.Blur ? MATERIAL_POPOVER : MATERIAL_HUD_WINDOW);
        Place(area);
        return shown;
    }

    /// <summary>
    /// Frames the effect view on the area; its autoresizing then keeps the same distance to each edge of the container
    /// when the window is resized.
    /// </summary>
    private void Place(SystemBackdropArea? area)
    {
        var bounds = ObjC.MsgSend_rect(_container, ObjC.Sel("bounds"));
        var frame = bounds;
        double cornerRadius = 0;
        if (area is SystemBackdropArea shape)
        {
            // The container counts up from its bottom edge.
            frame = new NSRect(
                shape.Bounds.X,
                bounds.size.height - shape.Bounds.Bottom,
                shape.Bounds.Width,
                shape.Bounds.Height);
            cornerRadius = shape.CornerRadius;
        }

        ObjC.MsgSend_void_nint_rect(_effectView, ObjC.Sel("setFrame:"), frame);
        ObjC.MsgSend_void_nint_bool(_effectView, ObjC.Sel("setWantsLayer:"), true);
        nint layer = ObjC.MsgSend_nint(_effectView, ObjC.Sel("layer"));
        if (layer != 0)
        {
            ObjC.MsgSend_void_nint_double(layer, ObjC.Sel("setCornerRadius:"), cornerRadius);
            ObjC.MsgSend_void_nint_bool(layer, ObjC.Sel("setMasksToBounds:"), cornerRadius > 0);
        }
    }

    private bool Insert()
    {
        nint viewClass = ObjC.GetClass("NSView");
        nint effectClass = ObjC.GetClass("NSVisualEffectView");
        if (viewClass == 0 || effectClass == 0)
        {
            return false;
        }

        var bounds = ObjC.MsgSend_rect(_drawingView, ObjC.Sel("frame"));
        nint alloc = ObjC.Sel("alloc");
        nint initWithFrame = ObjC.Sel("initWithFrame:");
        nint container = ObjC.MsgSend_nint_rect(ObjC.MsgSend_nint(viewClass, alloc), initWithFrame, bounds);
        nint effect = ObjC.MsgSend_nint_rect(ObjC.MsgSend_nint(effectClass, alloc), initWithFrame, bounds);
        if (container == 0 || effect == 0)
        {
            Release(container);
            Release(effect);
            return false;
        }

        nint setAutoresizingMask = ObjC.Sel("setAutoresizingMask:");
        ObjC.MsgSend_void_nint_ulong(effect, ObjC.Sel("setBlendingMode:"), BLENDING_BEHIND_WINDOW);
        ObjC.MsgSend_void_nint_ulong(effect, ObjC.Sel("setState:"), STATE_ACTIVE);
        ObjC.MsgSend_void_nint_ulong(effect, setAutoresizingMask, WIDTH_SIZABLE | HEIGHT_SIZABLE);
        ObjC.MsgSend_void_nint_ulong(container, setAutoresizingMask, WIDTH_SIZABLE | HEIGHT_SIZABLE);

        nint firstResponder = ObjC.MsgSend_nint(_window, ObjC.Sel("firstResponder"));
        // The window lets go of the drawing view when it takes the container, so it is held across the move.
        ObjC.MsgSend_nint(_drawingView, ObjC.Sel("retain"));
        ObjC.MsgSend_void_nint_nint(_window, ObjC.Sel("setContentView:"), container);
        nint addSubview = ObjC.Sel("addSubview:");
        ObjC.MsgSend_void_nint_nint(container, addSubview, effect);
        ObjC.MsgSend_void_nint_nint(container, addSubview, _drawingView);
        // The window sizes its content view to the content area; before the window is shown the drawing view's own
        // frame can still be empty, and a subview laid out from it would stay empty however the container grows.
        var contentBounds = ObjC.MsgSend_rect(container, ObjC.Sel("bounds"));
        ObjC.MsgSend_void_nint_rect(effect, ObjC.Sel("setFrame:"), contentBounds);
        ObjC.MsgSend_void_nint_rect(_drawingView, ObjC.Sel("setFrame:"), contentBounds);
        Release(_drawingView);
        RestoreFirstResponder(firstResponder);

        // The window and the container own the two views now.
        Release(container);
        Release(effect);
        _container = container;
        _effectView = effect;
        return true;
    }

    private void Remove()
    {
        if (_container == 0)
        {
            return;
        }

        nint firstResponder = ObjC.MsgSend_nint(_window, ObjC.Sel("firstResponder"));
        ObjC.MsgSend_nint(_drawingView, ObjC.Sel("retain"));
        ObjC.MsgSend_void(_drawingView, ObjC.Sel("removeFromSuperview"));
        ObjC.MsgSend_void_nint_nint(_window, ObjC.Sel("setContentView:"), _drawingView);
        Release(_drawingView);
        RestoreFirstResponder(firstResponder);
        _container = 0;
        _effectView = 0;
    }

    private void RestoreFirstResponder(nint firstResponder)
    {
        if (firstResponder == _drawingView)
        {
            ObjC.MsgSend_void_nint_nint(_window, ObjC.Sel("makeFirstResponder:"), _drawingView);
        }
    }

    private static void Release(nint instance)
    {
        if (instance != 0)
        {
            ObjC.MsgSend_void(instance, ObjC.Sel("release"));
        }
    }
}
