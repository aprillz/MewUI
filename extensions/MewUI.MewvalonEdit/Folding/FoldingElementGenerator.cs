using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Folding;

/// <summary>
/// A <see cref="VisualLineElementGenerator"/> that produces line elements for folded
/// <see cref="FoldingSection"/>s.
/// </summary>
public sealed class FoldingElementGenerator : VisualLineElementGenerator
{
    /// <summary>The folding manager whose foldings are shown.</summary>
    public FoldingManager? FoldingManager { get; set; }

    /// <inheritdoc/>
    public override int GetFirstInterestedOffset(int startOffset)
        => FoldingManager?.GetNextFoldedFoldingStart(startOffset) ?? -1;

    /// <inheritdoc/>
    public override VisualLineElement? ConstructElement(int offset)
    {
        var foldingManager = FoldingManager;
        if (foldingManager is null)
        {
            return null;
        }
        int foldedUntil = -1;
        FoldingSection? foldingSection = null;
        foreach (var section in foldingManager.GetFoldingsContaining(offset))
        {
            if (section.IsFolded)
            {
                if (section.EndOffset > foldedUntil)
                {
                    foldedUntil = section.EndOffset;
                    foldingSection = section;
                }
            }
        }
        if (foldedUntil > offset && foldingSection is not null)
        {
            // Handle overlapping foldings: if there's another folded folding
            // (starting within the foldingSection) that continues after the end of the folded section,
            // then we'll extend our fold element to cover that overlapping folding.
            bool foundOverlappingFolding;
            do
            {
                foundOverlappingFolding = false;
                foreach (var section in foldingManager.GetFoldingsContaining(foldedUntil))
                {
                    if (section.IsFolded && section.EndOffset > foldedUntil)
                    {
                        foldedUntil = section.EndOffset;
                        foundOverlappingFolding = true;
                    }
                }
            } while (foundOverlappingFolding);

            string title = foldingSection.Title ?? string.Empty;
            if (string.IsNullOrEmpty(title))
            {
                title = "...";
            }
            return new FoldingLineElement(
                foldingSection, title, foldedUntil - offset, CurrentContext!.DefaultStyle);
        }
        else
        {
            return null;
        }
    }

    private sealed class FoldingLineElement(
        FoldingSection section,
        string title,
        int documentLength,
        TextRunStyle style)
        : TextReplacementElement(title, documentLength, style)
    {
        private const double CORNER_RADIUS = 2;

        protected internal override void PrepareForPaint(TextView textView)
        {
            ArgumentNullException.ThrowIfNull(textView);
            // Assigned every paint: the scan cache outlives a theme change, so a colour kept from
            // the last paint would never be replaced.
            Foreground = textView.ResolvedFoldingMarker;
        }

        public override void Draw(ITextRenderContext context, Point origin, uint dpi)
        {
            ArgumentNullException.ThrowIfNull(context);
            var metrics = Measure(dpi);
            double dpiScale = dpi / 96.0;
            var pen = new ColorPen(Foreground ?? Color.FromRgb(0x80, 0x80, 0x80)).SnapThickness(dpiScale);
            // The stroke sits on the edge it is given, so the box is inset by half of it; without
            // that it straddles the snapped edge and covers a pixel on either side.
            var box = LayoutRounding.SnapBoundsRectToPixels(
                new Rect(origin.X, origin.Y, metrics.Width, metrics.Height), dpiScale);
            context.Graphics.DrawRoundedRectangle(
                box.Deflate(new Thickness(pen.Thickness / 2)),
                CORNER_RADIUS,
                CORNER_RADIUS,
                pen.Color,
                pen.Thickness);
            base.Draw(context, origin, dpi);
        }

        protected internal override void OnMouseDown(MouseEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (e.ClickCount == 2 && e.Button == MouseButton.Left)
            {
                section.IsFolded = false;
                e.Handled = true;
            }
            else
            {
                base.OnMouseDown(e);
            }
        }
    }
}
