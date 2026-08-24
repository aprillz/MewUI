using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Text;

/// <summary>
/// The single <see cref="ITextRenderContext"/> implementation: lays paint-span colors,
/// backgrounds, decorations and overlays over a <see cref="ManagedTextLayout"/> and hands each
/// run to the backend through its private realization surface.
/// </summary>
internal sealed class ManagedTextRenderContext : ITextRenderContext, IDisposable
{
    private const int REALIZATION_CAPACITY = 128;
    private const string ELLIPSIS = "...";

    private readonly IGraphicsContext _context;
    private readonly ITextBackendRenderContext _backend;
    private readonly BoundedCache<RunRealizationKey, RealizedRun> _runs = new(
        REALIZATION_CAPACITY,
        static run => run.Dispose());

    public ManagedTextRenderContext(IGraphicsContext context)
    {
        _context = context;
        _backend = context as ITextBackendRenderContext
            ?? throw new InvalidOperationException("The graphics context does not provide text realization services.");
    }

    public IGraphicsContext Graphics => _context;

    internal int CachedLayoutCount => _runs.Count;
    internal IEnumerable<ITextBackendRun> CachedRuns
        => _runs.Values.Select(static run => run.Run);

    public void Draw(ITextLayout layout, Point origin, in TextDrawOptions options)
    {
        var managed = Validate(layout);
        if (CanDrawFastPath(managed, in options))
        {
            DrawFastPath(managed, origin, options.Foreground, options.Owner);
        }
        else
        {
            DrawBackgroundCore(managed, origin, in options);
            DrawForegroundCore(managed, origin, in options);
        }
    }

    public void DrawBackground(ITextLayout layout, Point origin, in TextDrawOptions options)
    {
        var managed = Validate(layout);
        if (!CanDrawFastPath(managed, in options))
        {
            DrawBackgroundCore(managed, origin, in options);
        }
    }

    public void DrawForeground(ITextLayout layout, Point origin, in TextDrawOptions options)
    {
        var managed = Validate(layout);
        if (CanDrawFastPath(managed, in options))
        {
            DrawFastPath(managed, origin, options.Foreground, options.Owner);
        }
        else
        {
            DrawForegroundCore(managed, origin, in options);
        }
    }

    private static ManagedTextLayout Validate(ITextLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout is not ManagedTextLayout managed)
        {
            throw new ArgumentException("The layout was created by a different text engine.", nameof(layout));
        }
        return managed;
    }

    internal static bool CanDrawFastPath(ManagedTextLayout layout, in TextDrawOptions options)
        => layout.IsFastPath &&
           !layout.HasMaterializedColumns &&
           options.PaintSpans.IsEmpty &&
           options.Overlays.IsEmpty;

    private void DrawBackgroundCore(ManagedTextLayout managed, Point origin, in TextDrawOptions options)
    {
        DrawBackgrounds(managed, origin, options.PaintSpans.Span);
        DrawOverlays(managed, origin, options.Overlays.Span);
    }

    private void DrawForegroundCore(ManagedTextLayout managed, Point origin, in TextDrawOptions options)
    {
        var lines = managed.ManagedLines;
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            // Ink renders in the untrimmed box: a cap-trimmed line reports a smaller layout box,
            // but the glyphs keep their font-metric position and may overflow it.
            double inkY = origin.Y + line.Metrics.Bounds.Y - line.TrimTop;
            double inkHeight = line.Metrics.Bounds.Height + line.TrimTop + line.TrimBottom;
            double inkBaseline = line.Metrics.Baseline + line.TrimTop;
            var runs = managed.GetRuns(line);
            for (int index = 0; index < runs.Length; index++)
            {
                ref readonly var run = ref runs[index];
                if (run.Kind == ManagedTextRunKind.Inline)
                {
                    managed.GetInline(in run)?.Draw(this, new Point(origin.X + run.X, inkY));
                    continue;
                }
                if (run.Kind is ManagedTextRunKind.Tab or ManagedTextRunKind.NewLine)
                {
                    continue;
                }

                var bounds = new Rect(origin.X + run.X, inkY, Math.Max(1, run.Width), inkHeight);
                var realized = GetOrCreateRun(
                    managed, run.TextStart, run.TextLength, run.Font, run.Width, inkHeight);
                if (realized is not null)
                {
                    DrawRunColorSegments(
                        managed, line.RunStart + index, origin, bounds, inkBaseline, realized, in options);
                }
            }

            if (line.IsTrimmed)
            {
                DrawEllipsis(managed, line, runs, origin, options.Foreground, options.Owner);
            }
        }

        DrawDecorations(managed, origin, options.PaintSpans.Span);
    }

    private void DrawFastPath(ManagedTextLayout managed, Point origin, Color color, object? owner)
    {
        var line = managed.ManagedLines[0];
        var font = managed.GetDefaultFont();
        Rect? clip = _context.GetClipBoundsLocal();
        if (clip is Rect visibleClip)
        {
            DrawFastPathVisibleRange(managed, line, origin, visibleClip, color, owner, font);
            return;
        }

        foreach (var segment in line.FastSegments ?? [])
        {
            var bounds = new Rect(
                origin.X + segment.X,
                origin.Y + line.Metrics.Bounds.Y - line.TrimTop,
                Math.Max(1, segment.Width),
                line.Metrics.Bounds.Height + line.TrimTop + line.TrimBottom);
            var realized = GetOrCreateRun(managed, segment.Start, segment.Length, font, bounds.Width, bounds.Height);
            if (realized is not null)
            {
                DrawRun(realized, bounds.Position, color, owner);
            }
        }
    }

    private void DrawFastPathVisibleRange(
        ManagedTextLayout managed,
        ManagedTextLine line,
        Point origin,
        Rect clip,
        Color color,
        object? owner,
        IFont font)
    {
        const double OVERSCAN = 32;
        double hitY = line.Metrics.Bounds.Y + line.Metrics.Bounds.Height * 0.5;
        CharacterHit startHit = managed.HitTestPoint(new Point(clip.Left - origin.X - OVERSCAN, hitY));
        CharacterHit endHit = managed.HitTestPoint(new Point(clip.Right - origin.X + OVERSCAN, hitY));
        int textStart = Math.Clamp(startHit.FirstCharacterIndex, 0, managed.Snapshot.Text.Length);
        int textEnd = Math.Clamp(endHit.InsertionIndex, textStart, managed.Snapshot.Text.Length);
        if (textEnd <= textStart)
        {
            return;
        }

        Rect startCaret = managed.GetCaretBounds(new CharacterHit(textStart, 0));
        Rect endCaret = managed.GetCaretBounds(new CharacterHit(textEnd, 0));
        var bounds = new Rect(
            origin.X + startCaret.X,
            origin.Y + line.Metrics.Bounds.Y - line.TrimTop,
            Math.Max(1, endCaret.X - startCaret.X),
            line.Metrics.Bounds.Height + line.TrimTop + line.TrimBottom);
        var realized = GetOrCreateRun(managed, textStart, textEnd - textStart, font, bounds.Width, bounds.Height);
        if (realized is not null)
        {
            DrawRun(realized, bounds.Position, color, owner);
        }
    }

    /// <summary>Draws the trimming ellipsis after the last surviving run of a trimmed line.</summary>
    private void DrawEllipsis(
        ManagedTextLayout managed,
        ManagedTextLine line,
        ReadOnlySpan<ManagedTextRun> runs,
        Point origin,
        Color color,
        object? owner)
    {
        var lineBounds = line.Metrics.Bounds;
        var font = runs.Length > 0 ? runs[^1].Font : managed.GetDefaultFont();
        double x = runs.Length > 0 ? runs[^1].X + runs[^1].Width : lineBounds.X;
        double width = Math.Max(1, lineBounds.Right - x);
        double inkHeight = lineBounds.Height + line.TrimTop + line.TrimBottom;
        using var run = _backend.CreateRun(ELLIPSIS, font, width, inkHeight);
        if (run is null)
        {
            return;
        }

        _backend.DrawRun(run, new Point(origin.X + x, origin.Y + lineBounds.Y - line.TrimTop), color, owner);
    }

    private void DrawBackgrounds(ManagedTextLayout layout, Point origin, ReadOnlySpan<TextPaintSpan> spans)
    {
        foreach (var span in spans)
        {
            if (span.Background is Color color)
            {
                DrawRangeRectangles(layout, origin, span.Range, color);
            }
        }
    }

    private void DrawOverlays(ManagedTextLayout layout, Point origin, ReadOnlySpan<TextOverlay> overlays)
    {
        foreach (var overlay in overlays)
        {
            DrawRangeRectangles(layout, origin, overlay.Range, overlay.Color);
        }
    }

    private void DrawRangeRectangles(ManagedTextLayout layout, Point origin, TextRange range, Color color)
    {
        var bounds = new List<Rect>();
        layout.GetRangeBounds(range.Start, range.Length, bounds);
        foreach (var rect in bounds)
        {
            _context.FillRectangle(new Rect(origin.X + rect.X, origin.Y + rect.Y, rect.Width, rect.Height), color);
        }
    }

    private void DrawDecorations(ManagedTextLayout layout, Point origin, ReadOnlySpan<TextPaintSpan> spans)
    {
        foreach (var span in spans)
        {
            if (span.Decoration == TextDecoration.None)
            {
                continue;
            }
            var bounds = new List<Rect>();
            layout.GetRangeBounds(span.Range.Start, span.Range.Length, bounds);
            Color color = span.Foreground ?? Color.FromArgb(255, 0, 0, 0);
            double dpiScale = _context.DpiScale;
            double thickness = LayoutRounding.SnapThicknessToPixels(1, dpiScale, 1);
            foreach (var rect in bounds)
            {
                if (span.Decoration.HasFlag(TextDecoration.Underline))
                {
                    FillSnappedDecoration(
                        origin.X + rect.X,
                        origin.X + rect.Right,
                        origin.Y + Math.Max(rect.Y, rect.Bottom - thickness),
                        thickness, dpiScale, color);
                }
                if (span.Decoration.HasFlag(TextDecoration.Strikethrough))
                {
                    FillSnappedDecoration(
                        origin.X + rect.X,
                        origin.X + rect.Right,
                        origin.Y + rect.Y + Math.Max(0, (rect.Height - thickness) * 0.55),
                        thickness, dpiScale, color);
                }
            }
        }
    }

    /// <summary>
    /// Draws style-run underline/strikethrough as renderer geometry so every backend matches;
    /// font-level decoration support varies by backend and is not relied on.
    /// </summary>
    private void DrawRunDecoration(TextRunStyle style, double left, double right, in Rect runBounds, double baseline, Color color)
    {
        if (style.Decoration == TextDecoration.None)
        {
            return;
        }

        double dpiScale = _context.DpiScale;
        double thickness = LayoutRounding.SnapThicknessToPixels(1, dpiScale, 1);
        double clampedLeft = Math.Max(left, runBounds.X);
        double clampedRight = Math.Min(right, runBounds.Right);
        if (style.Decoration.HasFlag(TextDecoration.Underline))
        {
            FillSnappedDecoration(
                clampedLeft, clampedRight,
                Math.Min(runBounds.Y + baseline + 1, runBounds.Bottom - thickness),
                thickness, dpiScale, color);
        }
        if (style.Decoration.HasFlag(TextDecoration.Strikethrough))
        {
            // FontSize is in points; 4/3 converts to DIPs, strike sits ~30% of the em above baseline.
            FillSnappedDecoration(
                clampedLeft, clampedRight,
                runBounds.Y + baseline - style.FontSize * (4.0 / 3.0) * 0.3,
                thickness, dpiScale, color);
        }
    }

    /// <summary>Fills one decoration stroke snapped to whole device pixels.</summary>
    private void FillSnappedDecoration(double left, double right, double y, double thickness, double dpiScale, Color color)
    {
        // Both ends round the same way, so a decoration cut into segments by paint spans stays
        // contiguous: neighboring segments share an edge and land on the same pixel.
        double snappedLeft = LayoutRounding.RoundToPixel(left, dpiScale);
        double width = LayoutRounding.RoundToPixel(right, dpiScale) - snappedLeft;
        if (width <= 0)
        {
            return;
        }
        _context.FillRectangle(new Rect(snappedLeft, LayoutRounding.RoundToPixel(y, dpiScale), width, thickness), color);
    }

    /// <summary>
    /// Draws one style run partitioned into effective-foreground segments so every pixel is
    /// painted exactly once. The geometry realization stays whole-run (paint spans never split
    /// or recreate it); only the draw is clipped per color segment, so glyph antialiasing blends
    /// against the clean background instead of an overdrawn base pass.
    /// </summary>
    private void DrawRunColorSegments(
        ManagedTextLayout managed,
        int runIndex,
        Point origin,
        Rect runBounds,
        double baseline,
        RealizedRun realized,
        in TextDrawOptions options)
    {
        var spans = options.PaintSpans.Span;
        ref readonly var run = ref managed.GetRun(runIndex);
        // Colour is resolved at each text element's first code unit, so a span that starts inside one
        // colours the whole element rather than cutting the glyph in half.
        var boundaries = managed.GetRunBoundaries(runIndex);
        int segmentStart = 0;
        var segmentColor = GetSpanForeground(spans, boundaries.Length > 0 ? boundaries[0] : run.TextStart)
            ?? options.Foreground;

        for (int index = 1; index <= boundaries.Length; index++)
        {
            Color nextColor = default;
            if (index < boundaries.Length)
            {
                nextColor = GetSpanForeground(spans, boundaries[index]) ?? options.Foreground;
                if (nextColor == segmentColor)
                {
                    continue;
                }
            }

            int startOffset = boundaries.Length > 0 ? boundaries[segmentStart] : run.TextStart;
            int endOffset = index < boundaries.Length ? boundaries[index] : run.TextEnd;
            double left = origin.X + managed.GetColumnX(in run, startOffset);
            double right = origin.X + managed.GetColumnX(in run, endOffset);
            if (segmentStart == 0 && index == boundaries.Length)
            {
                DrawRun(realized, runBounds.Position, segmentColor, options.Owner);
            }
            else
            {
                // Interior color boundaries floor to whole device pixels so adjacent clips agree
                // on pixel ownership; backend clip rounding otherwise shifts the boundary column
                // into the neighbor color depending on the fractional scroll offset.
                double dpiScale = _context.DpiScale;
                double clipLeft = segmentStart == 0 ? runBounds.X : Math.Floor(left * dpiScale) / dpiScale;
                double clipRight = index == boundaries.Length ? runBounds.Right : Math.Floor(right * dpiScale) / dpiScale;
                var clip = new Rect(clipLeft, runBounds.Y, Math.Max(0, clipRight - clipLeft), runBounds.Height).Intersect(runBounds);
                if (!clip.IsEmpty)
                {
                    _context.Save();
                    try
                    {
                        _context.IntersectClip(clip);
                        DrawRun(realized, runBounds.Position, segmentColor, options.Owner);
                    }
                    finally
                    {
                        _context.Restore();
                    }
                }
            }
            DrawRunDecoration(managed.Snapshot.GetStyle(startOffset), left, right, runBounds, baseline, segmentColor);

            segmentStart = index;
            segmentColor = nextColor;
        }
    }

    /// <summary>Resolves the foreground at a text index; later spans win where ranges overlap.</summary>
    internal static Color? GetSpanForeground(ReadOnlySpan<TextPaintSpan> spans, int index)
    {
        Color? result = null;
        foreach (var span in spans)
        {
            if (span.Foreground is Color color && index >= span.Range.Start && index < span.Range.End)
            {
                result = color;
            }
        }
        return result;
    }

    private RealizedRun? GetOrCreateRun(
        ManagedTextLayout layout,
        int textStart,
        int textLength,
        IFont font,
        double width,
        double height)
    {
        var key = new RunRealizationKey(layout, textStart, textLength, font, Math.Round(width, 6), Math.Round(height, 6));
        if (_runs.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var backendRun = _backend.CreateRun(
            layout.Snapshot.Text.AsSpan(textStart, textLength),
            font,
            Math.Max(1, width),
            Math.Max(1, height));
        if (backendRun is null)
        {
            return null;
        }

        var created = new RealizedRun(
            backendRun);
        _runs.Add(key, created);
        return created;
    }

    private void DrawRun(RealizedRun realized, Point origin, Color color, object? owner)
        => _backend.DrawRun(realized.Run, origin, color, owner);

    public void Dispose() => _runs.Dispose();

    private readonly record struct RunRealizationKey(
        ManagedTextLayout Layout,
        int TextStart,
        int TextLength,
        IFont Font,
        double Width,
        double Height);

    private sealed class RealizedRun(ITextBackendRun run) : IDisposable
    {
        public ITextBackendRun Run { get; } = run;

        public void Dispose() => Run.Dispose();
    }
}
