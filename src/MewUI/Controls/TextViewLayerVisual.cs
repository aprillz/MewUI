using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>A text view host that draws its layer stack one anchor group at a time.</summary>
internal interface ITextViewLayerDrawing
{
    /// <summary>Draws the layers of <paramref name="anchor"/>'s group, the anchor's own included.</summary>
    void DrawLayerGroup(IGraphicsContext context, TextViewLayerAnchor anchor);
}

/// <summary>The layer visuals of a text view host, one per anchor, and what the host does with them together.</summary>
internal static class TextViewLayerVisuals
{
    internal static TextViewLayerVisual[] Create<THost>(THost host)
        where THost : UIElement, ITextViewLayerDrawing
    {
        var anchors = Enum.GetValues<TextViewLayerAnchor>();
        var visuals = new TextViewLayerVisual[anchors.Length];
        for (int index = 0; index < anchors.Length; index++)
        {
            visuals[index] = new TextViewLayerVisual(host, anchors[index]) { Parent = host };
        }

        return visuals;
    }

    internal static void Invalidate(TextViewLayerVisual[] visuals, TextViewLayerAnchor anchor)
    {
        foreach (var visual in visuals)
        {
            if (visual.Anchor == anchor)
            {
                visual.InvalidateVisual();
            }
        }
    }

    internal static void InvalidateAll(TextViewLayerVisual[] visuals)
    {
        foreach (var visual in visuals)
        {
            visual.InvalidateVisual();
        }
    }

    /// <summary>Lays every visual over the text viewport, where the layers draw.</summary>
    internal static void Arrange(TextViewLayerVisual[] visuals, Rect viewport)
    {
        foreach (var visual in visuals)
        {
            visual.Measure(viewport.Size);
            visual.Arrange(viewport);
        }
    }

    internal static void Render(TextViewLayerVisual[] visuals, IGraphicsContext context, Rect clip)
    {
        context.Save();
        try
        {
            context.SetClip(clip);
            foreach (var visual in visuals)
            {
                visual.Render(context);
            }
        }
        finally
        {
            context.Restore();
        }
    }

    internal static void WriteComposition(TextViewLayerVisual[] visuals, Rendering.Retained.CompositionPlanBuilder builder, Rect clip)
    {
        builder.PushClipRect(clip);
        foreach (var visual in visuals)
        {
            builder.Child(visual);
        }

        builder.Pop();
    }

    internal static bool Visit(TextViewLayerVisual[] visuals, Func<Element, bool> visitor)
    {
        foreach (var visual in visuals)
        {
            if (!visitor(visual))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Draws one anchor's group of a text view's layer stack, so a change of that group records it alone.
/// The host lays it over the text viewport and takes all input; the visual is never hit.
/// </summary>
internal sealed class TextViewLayerVisual : FrameworkElement
{
    private readonly ITextViewLayerDrawing _host;

    internal TextViewLayerVisual(ITextViewLayerDrawing host, TextViewLayerAnchor anchor)
    {
        _host = host;
        Anchor = anchor;
        IsHitTestVisible = false;
    }

    internal TextViewLayerAnchor Anchor { get; }

    protected override Size MeasureContent(Size availableSize) => Size.Empty;

    protected override void OnRender(IGraphicsContext context) => _host.DrawLayerGroup(context, Anchor);
}
