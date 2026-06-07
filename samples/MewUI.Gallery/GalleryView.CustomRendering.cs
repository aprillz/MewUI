using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement CustomRenderingPage() =>
        CardGrid(
            Card("Offscreen", new SampleOffscreenControl { Height = 240, Width = 280 })
        );
}

public class SampleOffscreenControl : OffscreenControl
{
    private bool _testCase;

    protected override void RenderOffscreen(IGraphicsContext context, Rect bounds)
    {
        Point p0, p1, p2, p3;

        if (_testCase)
        {
            (p0, p1, p2, p3) = (bounds.TopLeft, bounds.BottomRight, bounds.TopRight, bounds.BottomLeft);
        }
        else
        {
            (p0, p1, p2, p3) = (bounds.TopRight, bounds.BottomLeft, bounds.TopLeft, bounds.BottomRight);
        }

        context.DrawLine(p0, p1, Color.Green, 1);
        context.DrawLine(p2, p3, Color.Blue, 3);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        _testCase = !_testCase;
        InvalidateOffscreen();
    }
}
