using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Overlay element that renders a drag preview at a cursor position.
/// Added to <see cref="OverlayLayer"/> of the window the cursor is currently over;
/// migrates between windows as the cursor moves across them.
/// </summary>
internal sealed class DragPreviewOverlay : UIElement
{
    private readonly DragPreviewContent _content;
    private readonly Point _hotspot;
    private Point _cursorInWindow;

    public DragPreviewOverlay(DragPreviewContent content, Point hotspot)
    {
        _content = content;
        _hotspot = hotspot;
        IsHitTestVisible = false;
    }

    public void UpdateCursorPosition(Point cursorInWindow)
    {
        if (_cursorInWindow == cursorInWindow) return;
        _cursorInWindow = cursorInWindow;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private Size GetPreviewSize()
    {
        if (_content.Image is { } image)
        {
            return new Size(image.PixelWidth, image.PixelHeight);
        }
        if (_content.Element is { } element)
        {
            var bounds = element.Bounds;
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                return new Size(bounds.Width, bounds.Height);
            }
        }
        return _content.Size;
    }

    protected override Size MeasureOverride(Size availableSize) => GetPreviewSize();

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    protected override UIElement? OnHitTest(Point point) => null;

    protected override void OnRender(IGraphicsContext context)
    {
        var size = GetPreviewSize();
        var topLeft = new Point(
            _cursorInWindow.X - _hotspot.X,
            _cursorInWindow.Y - _hotspot.Y);
        var rect = new Rect(topLeft.X, topLeft.Y, size.Width, size.Height);

        var opacity = (float)Math.Clamp(_content.Opacity, 0.0, 1.0);

        context.Save();
        try
        {
            context.GlobalAlpha *= opacity;

            if (_content.Image is { } image)
            {
                context.DrawImage(image, rect);
                return;
            }

            if (_content.Element is { } element && element.IsVisible &&
                element.Bounds.Width > 0 && element.Bounds.Height > 0)
            {
                // Children render at window-absolute Bounds (no per-parent transform stack in MewUI),
                // so a single Translate aligns the source element's render onto the preview rect.
                var dx = topLeft.X - element.Bounds.X;
                var dy = topLeft.Y - element.Bounds.Y;
                context.Translate(dx, dy);
                element.Render(context);
                return;
            }

            // Placeholder when no Element/Image is provided.
            var fill = Color.FromArgb(0x80, 0x60, 0xA0, 0xFF);
            var stroke = Color.FromArgb(0xFF, 0x40, 0x70, 0xC0);
            context.FillRectangle(rect, fill);
            context.DrawRectangle(rect, stroke, 1);
        }
        finally
        {
            context.Restore();
        }
    }
}
