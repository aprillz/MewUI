using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public abstract partial class SvgTextBase
{
    private static readonly SvgUnitConverter BaselineShiftConverter = new();
    private PathGeometry? _path;
    private bool _pathBuiltWithRenderer;

    public override Rect Bounds
    {
        get
        {
            var path = Path(null) ?? new PathGeometry();
            foreach (var elem in Children.OfType<SvgVisualElement>())
            {
                if (elem is SvgTextSpan span && string.IsNullOrWhiteSpace(span.Text))
                {
                    continue;
                }

                var childPath = elem.Path(null);
                if (childPath is { IsEmpty: false })
                {
                    path.AddPath(childPath);
                }
            }

            return TransformedBounds(path.GetBounds());
        }
    }

    protected internal override void RenderFillAndStroke(ISvgRenderer renderer)
    {
        base.RenderFillAndStroke(renderer);
        RenderChildren(renderer);
    }

    internal virtual IEnumerable<ISvgNode> GetContentNodes()
    {
        return (Nodes is null || Nodes.Count < 1
                ? Children.OfType<ISvgNode>().Where(o => o is not ISvgDescriptiveElement)
                : Nodes);
    }

    protected virtual PathGeometry? GetBaselinePath(ISvgRenderer renderer)
    {
        return null;
    }

    protected virtual double GetAuthorPathLength()
    {
        return 0;
    }

    public override PathGeometry? Path(ISvgRenderer renderer)
    {
        var nodeCount = GetContentNodes().Count(x => x is SvgContentNode &&
                                                     string.IsNullOrEmpty(x.Content.Trim('\r', '\n', '\t')));
        bool willRebuild = _path is null || IsPathDirty || nodeCount == 1 || (renderer is not null && !_pathBuiltWithRenderer);
        if (willRebuild)
        {
            // Set the flag BEFORE SetPath so that child tspans created in the same
            // pass inherit the same renderer-kind flag (see SetPath child branch).
            if (renderer is not null)
            {
                _pathBuiltWithRenderer = true;
                SetPath(new TextDrawingState(renderer, this));
            }
            else
            {
                _pathBuiltWithRenderer = false;
                var factory = Application.Current?.GraphicsFactory ?? Application.DefaultGraphicsFactory;
                using var context = factory.CreateMeasurementContext(96);
                using var tempRenderer = new MewSvgRenderer(factory, context);
                SetPath(new TextDrawingState(tempRenderer, this));
            }
        }

        return _path;
    }

    protected override void Render(ISvgRenderer renderer)
    {
        base.Render(renderer);
    }

    private void SetPath(TextDrawingState state)
    {
        SetPath(state, true);
    }

    private void SetPath(TextDrawingState state, bool doMeasurements)
    {
        TextDrawingState? originalState = null;
        bool alignOnBaseline = state.BaselinePath is not null &&
                               (TextAnchor == SvgTextAnchor.Middle || TextAnchor == SvgTextAnchor.End);

        if (doMeasurements)
        {
            if (TextLength != SvgUnit.None)
            {
                originalState = state.Clone();
            }
            else if (alignOnBaseline)
            {
                originalState = state.Clone();
                state.BaselinePath = null;
            }
        }

        foreach (var node in GetContentNodes())
        {
            if (node is SvgTextBase textNode)
            {
                var newState = new TextDrawingState(state, textNode);
                textNode.SetPath(newState);
                // Propagate the cache-validity flag — child's path was built with the
                // same renderer-kind (measurement vs real) as ours in this pass. Without
                // this, child.Path(renderer) at child Render time sees its own stale
                // false, re-triggers SetPath with a child-rootless state, and re-emits
                // glyphs at (0,0).
                textNode._pathBuiltWithRenderer = _pathBuiltWithRenderer;
                state.NumChars += newState.NumChars;
                state.Current = newState.Current;
            }
            else if (!string.IsNullOrEmpty(node.Content))
            {
                state.DrawString(PrepareText(node.Content));
            }
        }

        var path = state.GetPath() ?? new PathGeometry();

        if (doMeasurements)
        {
            if (TextLength != SvgUnit.None)
            {
                var specifiedLength = TextLength.ToDeviceValue(state.Renderer, UnitRenderingType.Horizontal, this);
                var actualLength = state.TextBounds.Width;
                var diff = actualLength - specifiedLength;
                if (Math.Abs(diff) > 1.5)
                {
                    if (LengthAdjust == SvgTextLengthAdjust.Spacing)
                    {
                        if (X.Count < 2)
                        {
                            var charDiff = state.NumChars - (originalState?.NumChars ?? 0) - 1;
                            if (charDiff != 0 && originalState is not null)
                            {
                                originalState.LetterSpacingAdjust = -diff / charDiff;
                                SetPath(originalState, false);
                                return;
                            }
                        }
                    }
                    else if (!path.IsEmpty && actualLength > 0)
                    {
                        var matrix =
                            Matrix3x2.CreateTranslation((float)-state.TextBounds.X, 0f) *
                            Matrix3x2.CreateScale((float)(specifiedLength / actualLength), 1f) *
                            Matrix3x2.CreateTranslation((float)state.TextBounds.X, 0f);
                        path = MewSvgPathUtilities.TransformPath(path, matrix);
                    }
                }
            }
            else if (alignOnBaseline && originalState is not null)
            {
                var bounds = path.GetBounds();
                originalState.StartOffsetAdjust = TextAnchor == SvgTextAnchor.Middle
                    ? -bounds.Width / 2
                    : -bounds.Width;
                SetPath(originalState, false);
                return;
            }
        }

        _path = path;
        IsPathDirty = false;
    }

    private sealed class FontBoundable : ISvgBoundable
    {
        private readonly IFontDefn _font;
        private readonly double _width;

        public FontBoundable(IFontDefn font, double width = 1)
        {
            _font = font;
            _width = width;
        }

        public Point Location => default;

        public Size Size => new(_width, _font.Size);

        public Rect Bounds => new(Location, Size);
    }

    private sealed class TextDrawingState
    {
        private double _xAnchor = double.MinValue;
        private IList<PathGeometry> _anchoredPaths = new List<PathGeometry>();
        private PathGeometry? _currPath;
        private PathGeometry? _finalPath;
        private double _authorPathLength;

        public TextDrawingState(ISvgRenderer renderer, SvgTextBase element)
        {
            Element = element;
            Renderer = renderer;
            Current = default;
            TextBounds = Rect.Empty;
            _xAnchor = 0;
            BaselinePath = element.GetBaselinePath(renderer);
            _authorPathLength = element.GetAuthorPathLength();
        }

        public TextDrawingState(TextDrawingState parent, SvgTextBase element)
            : this(parent.Renderer, element)
        {
            Parent = parent;
            Current = parent.Current;
            TextBounds = parent.TextBounds;
            BaselinePath ??= parent.BaselinePath;
            if (_authorPathLength == 0)
            {
                _authorPathLength = parent._authorPathLength;
            }
        }

        private TextDrawingState()
        {
            Element = null!;
            Renderer = null!;
        }

        public PathGeometry? BaselinePath { get; set; }
        public Point Current { get; set; }
        public Rect TextBounds { get; set; }
        public SvgTextBase Element { get; set; }
        public double LetterSpacingAdjust { get; set; }
        public int NumChars { get; set; }
        public TextDrawingState? Parent { get; set; }
        public ISvgRenderer Renderer { get; set; }
        public double StartOffsetAdjust { get; set; }

        public PathGeometry? GetPath()
        {
            FlushPath();
            return _finalPath;
        }

        public TextDrawingState Clone()
        {
            return new TextDrawingState
            {
                _anchoredPaths = _anchoredPaths.ToList(),
                BaselinePath = BaselinePath,
                _xAnchor = _xAnchor,
                Current = Current,
                TextBounds = TextBounds,
                Element = Element,
                NumChars = NumChars,
                Parent = Parent,
                Renderer = Renderer,
                _authorPathLength = _authorPathLength,
            };
        }

        public void DrawString(string value)
        {
            var xAnchors = GetValues(value.Length, e => e._x, UnitRenderingType.HorizontalOffset);
            var yAnchors = GetValues(value.Length, e => e._y, UnitRenderingType.VerticalOffset);
            using var fontManager = Element.OwnerDocument?.FontManager == null ? new SvgFontManager() : null;
            using var font = Element.GetFont(Renderer, fontManager ?? Element.OwnerDocument?.FontManager);

            var fontBaselineHeight = font.Ascent(Renderer);
            PathStatistics? pathStats = null;
            var pathScale = 1.0;
            if (BaselinePath is { IsEmpty: false })
            {
                pathStats = new PathStatistics(BaselinePath);
                if (_authorPathLength > 0)
                {
                    pathScale = _authorPathLength / pathStats.TotalLength;
                }
            }

            IList<double> xOffsets;
            IList<double> yOffsets;
            IList<float> rotations;
            double baselineShift = 0;

            try
            {
                Renderer.SetBoundable(new FontBoundable(font, pathStats is null ? 1 : pathStats.TotalLength));
                xOffsets = GetValues(value.Length, e => e._dx, UnitRenderingType.Horizontal);
                yOffsets = GetValues(value.Length, e => e._dy, UnitRenderingType.Vertical);
                if (StartOffsetAdjust != 0)
                {
                    if (xOffsets.Count < 1)
                    {
                        xOffsets.Add(StartOffsetAdjust);
                    }
                    else
                    {
                        xOffsets[0] += StartOffsetAdjust;
                    }
                }

                if (Element.LetterSpacing.Value != 0.0f ||
                    Element.WordSpacing.Value != 0.0f ||
                    LetterSpacingAdjust != 0.0f)
                {
                    var spacing = Element.LetterSpacing.ToDeviceValue(Renderer, UnitRenderingType.Horizontal, Element) + LetterSpacingAdjust;
                    var wordSpacing = Element.WordSpacing.ToDeviceValue(Renderer, UnitRenderingType.Horizontal, Element);
                    if (Parent is null && NumChars == 0 && xOffsets.Count < 1)
                    {
                        xOffsets.Add(0);
                    }

                    for (int i = Parent is null && NumChars == 0 ? 1 : 0; i < value.Length; i++)
                    {
                        var add = spacing + (char.IsWhiteSpace(value[i]) ? wordSpacing : 0);
                        if (i >= xOffsets.Count)
                        {
                            xOffsets.Add(add);
                        }
                        else
                        {
                            xOffsets[i] += add;
                        }
                    }
                }

                rotations = GetValues(value.Length, e => e._rotations);

                var baselineShiftText = Element.BaselineShift.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(baselineShiftText))
                {
                    baselineShiftText = "baseline";
                }

                baselineShift = baselineShiftText switch
                {
                    "baseline" => 0,
                    "sub" => new SvgUnit(SvgUnitType.Ex, 1).ToDeviceValue(Renderer, UnitRenderingType.Vertical, Element),
                    "super" => -new SvgUnit(SvgUnitType.Ex, 1).ToDeviceValue(Renderer, UnitRenderingType.Vertical, Element),
                    _ => -((SvgUnit)BaselineShiftConverter.ConvertFromInvariantString(baselineShiftText))
                        .ToDeviceValue(Renderer, UnitRenderingType.Vertical, Element),
                };

                if (baselineShift != 0)
                {
                    if (yOffsets.Any())
                    {
                        yOffsets[0] += baselineShift;
                    }
                    else
                    {
                        yOffsets.Add(baselineShift);
                    }
                }
            }
            finally
            {
                Renderer.PopBoundable();
            }

            var xTextStart = Current.X;
            var yPos = Current.Y;
            for (int i = 0; i < xAnchors.Count - 1; i++)
            {
                FlushPath();
                _xAnchor = xAnchors[i] + (xOffsets.Count > i ? xOffsets[i] : 0);
                EnsurePath();
                yPos = (yAnchors.Count > i ? yAnchors[i] : yPos) + (yOffsets.Count > i ? yOffsets[i] : 0);
                xTextStart = xTextStart.Equals(Current.X) ? _xAnchor : xTextStart;
                DrawStringOnCurrPath(
                    value[i].ToString(),
                    font,
                    new Point(_xAnchor, yPos),
                    fontBaselineHeight,
                    rotations.Count > i ? rotations[i] : rotations.LastOrDefault());
            }

            int renderChar = 0;
            var xPos = Current.X;
            if (xAnchors.Any())
            {
                FlushPath();
                renderChar = xAnchors.Count - 1;
                xPos = xAnchors[^1];
                _xAnchor = xPos;
            }

            EnsurePath();

            int lastIndividualChar = renderChar + Math.Max(
                Math.Max(Math.Max(Math.Max(xOffsets.Count, yOffsets.Count), yAnchors.Count), rotations.Count) - renderChar - 1,
                0);
            if (rotations.LastOrDefault() != 0.0f || pathStats is not null)
            {
                lastIndividualChar = value.Length;
            }

            if (lastIndividualChar > renderChar)
            {
                var charBounds = font.MeasureCharacters(Renderer, value.Substring(renderChar, Math.Min(lastIndividualChar + 1, value.Length) - renderChar));
                for (int i = renderChar; i < lastIndividualChar; i++)
                {
                    xPos += pathScale * (xOffsets.Count > i ? xOffsets[i] : 0) +
                            (charBounds[i - renderChar].X - (i == renderChar ? 0 : charBounds[i - renderChar - 1].X));
                    yPos = (yAnchors.Count > i ? yAnchors[i] : yPos) + (yOffsets.Count > i ? yOffsets[i] : 0);
                    if (pathStats is null)
                    {
                        xTextStart = xTextStart.Equals(Current.X) ? xPos : xTextStart;
                        DrawStringOnCurrPath(
                            value[i].ToString(),
                            font,
                            new Point(xPos, yPos),
                            fontBaselineHeight,
                            rotations.Count > i ? rotations[i] : rotations.LastOrDefault());
                    }
                    else
                    {
                        xPos = Math.Max(xPos, 0);
                        var halfWidth = charBounds[i - renderChar].Width / 2;
                        if (pathStats.OffsetOnPath(xPos + halfWidth))
                        {
                            pathStats.LocationAngleAtOffset(xPos + halfWidth, out var pathPoint, out var rotation);
                            pathPoint = new Point(
                                pathPoint.X - halfWidth * Math.Cos(rotation * Math.PI / 180.0) - pathScale * yPos * Math.Sin(rotation * Math.PI / 180.0),
                                pathPoint.Y - halfWidth * Math.Sin(rotation * Math.PI / 180.0) + pathScale * yPos * Math.Cos(rotation * Math.PI / 180.0));
                            xTextStart = xTextStart.Equals(Current.X) ? pathPoint.X : xTextStart;
                            DrawStringOnCurrPath(value[i].ToString(), font, pathPoint, fontBaselineHeight, rotation);
                        }
                    }
                }

                if (lastIndividualChar < value.Length)
                {
                    xPos += charBounds[^1].X - charBounds[^2].X;
                }
                else
                {
                    xPos += charBounds[^1].Width;
                }
            }

            if (lastIndividualChar < value.Length)
            {
                xPos += xOffsets.Count > lastIndividualChar ? xOffsets[lastIndividualChar] : 0;
                yPos = (yAnchors.Count > lastIndividualChar ? yAnchors[lastIndividualChar] : yPos) +
                       (yOffsets.Count > lastIndividualChar ? yOffsets[lastIndividualChar] : 0);
                xTextStart = xTextStart.Equals(Current.X) ? xPos : xTextStart;
                DrawStringOnCurrPath(value.Substring(lastIndividualChar), font, new Point(xPos, yPos), fontBaselineHeight, rotations.LastOrDefault());
                xPos += font.MeasureString(Renderer, value.Substring(lastIndividualChar)).Width;
            }

            NumChars += value.Length;
            Current = new Point(xPos, yPos - baselineShift);
            TextBounds = new Rect(xTextStart, 0, Current.X - xTextStart, 0);
        }

        private void DrawStringOnCurrPath(string value, IFontDefn font, Point location, double fontBaselineHeight, double rotation)
        {
            var drawPath = _currPath!;
            if (rotation != 0.0)
            {
                drawPath = new PathGeometry();
            }

            // location.Y is the SVG baseline y (per SVG spec: <text>'s y is baseline).
            // IGlyphOutlineFont.TryAppendGlyphOutline takes a *baseline* origin per its
            // interface contract. Pass through unchanged — every backend (GdiFont,
            // DirectWriteFont, CoreTextFont, FreeTypeFont) interprets baselineOrigin as
            // the actual baseline. Subtracting fontBaselineHeight here would be a top-
            // of-text origin, which only one backend was patched for and shifted glyphs
            // up by ascent on every other platform.
            font.AddStringToPath(Renderer, drawPath, value, location);
            if (rotation != 0.0 && !drawPath.IsEmpty)
            {
                var matrix =
                    Matrix3x2.CreateTranslation((float)-location.X, (float)-location.Y) *
                    Matrix3x2.CreateRotation((float)(rotation * Math.PI / 180.0)) *
                    Matrix3x2.CreateTranslation((float)location.X, (float)location.Y);
                _currPath!.AddPath(MewSvgPathUtilities.TransformPath(drawPath, matrix));
            }
        }

        private void EnsurePath()
        {
            if (_currPath is null)
            {
                _currPath = new PathGeometry();
                var currentState = this;
                while (currentState is not null && currentState._xAnchor <= double.MinValue)
                {
                    currentState = currentState.Parent;
                }

                currentState?._anchoredPaths.Add(_currPath);
            }
        }

        private void FlushPath()
        {
            if (_currPath is null)
            {
                return;
            }

            if (_currPath.IsEmpty)
            {
                _anchoredPaths.Clear();
                _xAnchor = double.MinValue;
                _currPath = null;
                return;
            }

            if (_xAnchor > double.MinValue)
            {
                double minX = double.MaxValue;
                double maxX = double.MinValue;
                foreach (var path in _anchoredPaths)
                {
                    var bounds = path.GetBounds();
                    if (bounds.Left < minX)
                    {
                        minX = bounds.Left;
                    }

                    if (bounds.Right > maxX)
                    {
                        maxX = bounds.Right;
                    }
                }

                var xOffset = 0.0;
                switch (Element.TextAnchor)
                {
                    case SvgTextAnchor.Middle:
                        xOffset -= _anchoredPaths.Count == 1 ? TextBounds.Width / 2 : (maxX - minX) / 2;
                        break;
                    case SvgTextAnchor.End:
                        xOffset -= _anchoredPaths.Count == 1 ? TextBounds.Width : (maxX - minX);
                        break;
                }

                if (xOffset != 0.0)
                {
                    var matrix = Matrix3x2.CreateTranslation((float)xOffset, 0f);
                    // EnsurePath puts _currPath into the _anchoredPaths it walks up to.
                    // Re-sync after transform — TransformPath returns a new instance
                    // (PathGeometry is immutable here, unlike SVG.NET's mutable GraphicsPath
                    // where in-place .Transform updated both refs). Without this, _finalPath
                    // = _currPath below picks up the un-transformed path and text-anchor
                    // never visually applies.
                    int currIndex = _currPath is null ? -1 : _anchoredPaths.IndexOf(_currPath);
                    for (int i = 0; i < _anchoredPaths.Count; i++)
                    {
                        _anchoredPaths[i] = MewSvgPathUtilities.TransformPath(_anchoredPaths[i], matrix);
                    }
                    if (currIndex >= 0)
                    {
                        _currPath = _anchoredPaths[currIndex];
                    }
                }

                _anchoredPaths.Clear();
                _xAnchor = double.MinValue;
            }

            if (_finalPath is null)
            {
                _finalPath = _currPath;
            }
            else
            {
                _finalPath.AddPath(_currPath);
            }

            _currPath = null;
        }

        private List<float> GetValues(int maxCount, Func<SvgTextBase, IEnumerable<float>> listGetter)
        {
            var currentState = this;
            int charCount = 0;
            var results = new List<float>();
            int resultCount = 0;

            while (currentState is not null)
            {
                charCount += currentState.NumChars;
                results.AddRange(listGetter(currentState.Element).Skip(charCount).Take(maxCount));
                if (results.Count > resultCount)
                {
                    maxCount -= results.Count - resultCount;
                    charCount += results.Count - resultCount;
                    resultCount = results.Count;
                }

                if (maxCount < 1)
                {
                    return results;
                }

                currentState = currentState.Parent;
            }

            return results;
        }

        private List<double> GetValues(int maxCount, Func<SvgTextBase, IEnumerable<SvgUnit>> listGetter, UnitRenderingType renderingType)
        {
            var currentState = this;
            int charCount = 0;
            var results = new List<double>();
            int resultCount = 0;

            while (currentState is not null)
            {
                charCount += currentState.NumChars;
                results.AddRange(listGetter(currentState.Element)
                    .Skip(charCount)
                    .Take(maxCount)
                    .Select(p => (double)p.ToDeviceValue(currentState.Renderer, renderingType, currentState.Element)));
                if (results.Count > resultCount)
                {
                    maxCount -= results.Count - resultCount;
                    charCount += results.Count - resultCount;
                    resultCount = results.Count;
                }

                if (maxCount < 1)
                {
                    return results;
                }

                currentState = currentState.Parent;
            }

            return results;
        }
    }
}
