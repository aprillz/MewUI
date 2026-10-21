using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Types;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Terminal;

using CursorType = Aprillz.MewUI.Terminal.Buffer.CursorType;

/// <summary>
/// Measures the cell grid of the font and draws rows and the cursor. Runs of narrow characters in the main font are
/// drawn as one text layout per row; other glyphs are placed cell by cell, and box-drawing characters as shapes.
/// </summary>
internal sealed class TerminalRenderer
{
    private const int CLUSTER_CACHE_LIMIT = 4096;

    // The printable ASCII characters, measured together for the average advance that becomes the cell width.
    private const string METRICS_SAMPLE =
        " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    private readonly Dictionary<(string Text, bool Bold, bool Italic), ITextLayout> _clusterLayouts = new();
    private readonly Dictionary<char, bool> _narrow = new();
    private readonly List<TextPaintSpan> _paintSpans = new();
    private readonly List<GeometryStyleRun> _styleRuns = new();
    private char[] _rowChars = Array.Empty<char>();
    private uint[] _foregrounds = Array.Empty<uint>();
    private uint[] _backgrounds = Array.Empty<uint>();
    private TextAttribute[] _attributes = Array.Empty<TextAttribute>();
    private string?[] _glyphs = Array.Empty<string?>();
    private byte[] _spans = Array.Empty<byte>();
    private bool[] _simple = Array.Empty<bool>();

    private ITextEngine? _textEngine;
    private TextRunStyle _style;
    private uint _dpi;
    private bool _fontIsMonospace;
    private double _baseline;

    public double CellWidth { get; private set; } = 8;

    public double CellHeight { get; private set; } = 16;

    public double Baseline => _baseline;

    public bool HasMetrics => _textEngine != null;

    /// <summary>Measures the font; returns whether the cell size changed.</summary>
    public bool UpdateMetrics(ITextEngine textEngine, TextRunStyle style, uint dpi)
    {
        if (ReferenceEquals(_textEngine, textEngine) && _style == style && _dpi == dpi)
        {
            return false;
        }

        _textEngine = textEngine;
        _style = style;
        _dpi = dpi;
        _clusterLayouts.Clear();
        _narrow.Clear();

        var sample = CreateLayout(METRICS_SAMPLE, bold: false, italic: false);
        var thin = CreateLayout(new string('i', 20), bold: false, italic: false);
        var wide = CreateLayout(new string('M', 20), bold: false, italic: false);
        double oldWidth = CellWidth;
        double oldHeight = CellHeight;
        double scale = dpi / 96.0;

        // The cell is a whole number of device pixels, so cell edges never fall between pixels.
        CellWidth = Math.Max(1, Math.Round(sample.MeasuredSize.Width / METRICS_SAMPLE.Length * scale) / scale);
        CellHeight = Math.Max(1, Math.Ceiling((sample.MeasuredSize.Height * scale) - 0.01) / scale);
        _fontIsMonospace = Math.Abs(wide.MeasuredSize.Width - thin.MeasuredSize.Width) < 0.05 * 20;
        _baseline = sample.Lines.Count > 0 ? sample.Lines[0].Baseline : CellHeight * 0.8;
        return oldWidth != CellWidth || oldHeight != CellHeight;
    }

    public void ReleaseOwner(object owner) => _textEngine?.ManagedCache.ReleaseOwner(owner);

    /// <summary>
    /// Draws one buffer row: backgrounds, text, decorations, then the selection and search highlights given as column
    /// ranges.
    /// </summary>
    public void RenderRow(IGraphicsContext context, Row row, Rect bounds, RenderSettings settings,
        IReadOnlyList<(int Start, int End)> selection, IReadOnlyList<(int Start, int End, bool Focused)> highlights, object owner)
    {
        if (_textEngine == null)
        {
            return;
        }

        int columns = Math.Min(row.Size, (int)Math.Ceiling(bounds.Width / CellWidth) + 1);
        EnsureScratch(columns);
        double scale = context.DpiScale;
        bool intenseIsBold = settings.GetRenderMode(RenderSettings.Mode.IntenseIsBold);

        // Gather each column's glyph and colors.
        int column = 0;
        foreach (var run in row.Attributes.Runs)
        {
            int runEnd = Math.Min(columns, column + run.Length);
            var (foreground, background) = settings.GetAttributeColors(run.Value);
            for (; column < runEnd; column++)
            {
                _attributes[column] = run.Value;
                _foregrounds[column] = foreground;
                _backgrounds[column] = background;
            }

            if (column >= columns)
            {
                break;
            }
        }

        int lastText = -1;
        for (column = 0; column < columns; column++)
        {
            var dbcs = row.DbcsAttrAt(column);
            if (dbcs == DbcsAttribute.Trailing)
            {
                _spans[column] = 0;
                _glyphs[column] = null;
                _simple[column] = true;
                _rowChars[column] = ' ';
                continue;
            }

            var glyph = row.GlyphAt(column);
            byte span = dbcs == DbcsAttribute.Leading ? (byte)2 : (byte)1;
            _spans[column] = span;
            bool simple = span == 1 && glyph.Length == 1 && IsSimple(glyph[0]);
            _simple[column] = simple;
            if (simple)
            {
                _glyphs[column] = null;
                _rowChars[column] = glyph[0];
                if (glyph[0] != ' ')
                {
                    lastText = column;
                }
            }
            else
            {
                _glyphs[column] = glyph.ToString();
                _rowChars[column] = ' ';
            }
        }

        uint defaultBackground = settings.GetAttributeColors(default).Background;

        // Backgrounds: one rectangle per run of the same color.
        int runStart = 0;
        while (runStart < columns)
        {
            uint background = _backgrounds[runStart];
            int runEnd = runStart + 1;
            while (runEnd < columns && _backgrounds[runEnd] == background)
            {
                runEnd++;
            }

            if (background != defaultBackground)
            {
                var rect = new Rect(bounds.X + (runStart * CellWidth), bounds.Y, (runEnd - runStart) * CellWidth, CellHeight);
                context.FillRectangle(LayoutRounding.SnapRectEdgesToPixels(rect, scale), ToColor(background));
            }

            runStart = runEnd;
        }

        // Search highlights sit under the text; the focused one is brighter.
        foreach (var (start, end, focused) in highlights)
        {
            var rect = ColumnRect(bounds, start, end);
            context.FillRectangle(LayoutRounding.SnapRectEdgesToPixels(rect, scale), focused ? Color.FromArgb(0xC0, 0xFF, 0xA5, 0x00) : Color.FromArgb(0x80, 0xFF, 0xFF, 0x00));
        }

        if (lastText >= 0)
        {
            DrawSimpleText(context, lastText + 1, new Point(bounds.X, bounds.Y), intenseIsBold, owner);
        }

        for (column = 0; column < columns; column++)
        {
            int span = _spans[column];
            if (span == 0)
            {
                continue;
            }

            var attr = _attributes[column];
            double cellX = bounds.X + (column * CellWidth);
            var foreground = ToColor(_foregrounds[column]);
            if (!_simple[column])
            {
                bool bold = intenseIsBold && attr.IsIntense();
                DrawComplexCell(context, _glyphs[column]!, span, cellX, bounds.Y, foreground, bold, attr.IsItalic(), scale);
            }

            if (attr.IsUnderlined() || attr.IsCrossedOut() || attr.IsOverlined() || attr.IsHyperlink())
            {
                DrawDecorations(context, attr, settings, cellX, bounds.Y, span * CellWidth, foreground, scale);
            }
        }

        // The selection is drawn over the text, translucent, as the terminal's own selection color.
        if (selection.Count > 0)
        {
            uint selectionColor = settings.GetColorTableEntry(TextColor.SELECTION_BACKGROUND);
            var color = selectionColor == TextAttribute.INVALID_COLOR ? Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF) : ToColor(selectionColor, 0x60);
            foreach (var (start, end) in selection)
            {
                context.FillRectangle(LayoutRounding.SnapRectEdgesToPixels(ColumnRect(bounds, start, end), scale), color);
            }
        }
    }

    /// <summary>Draws the cursor in <paramref name="cell"/>, with the glyph under a filled cursor in the background color.</summary>
    public void RenderCursor(IGraphicsContext context, Rect cell, CursorType type, bool focused, Color cursorColor,
        string? glyph, Color glyphColor, bool bold, bool italic, int span)
    {
        if (_textEngine == null)
        {
            return;
        }

        double scale = context.DpiScale;
        var rect = LayoutRounding.SnapRectEdgesToPixels(cell, scale);
        double pixel = 1.0 / scale;
        double thickness = Math.Max(1, Math.Round(scale)) / scale;

        if (!focused)
        {
            // An unfocused terminal shows where the cursor is without drawing attention to it.
            context.DrawRectangle(rect, cursorColor, pixel, strokeInset: true);
            return;
        }

        switch (type)
        {
            case CursorType.VerticalBar:
            {
                double width = Math.Max(thickness, Math.Round(CellWidth * 0.12 * scale) / scale);
                context.FillRectangle(new Rect(rect.X, rect.Y, width, rect.Height), cursorColor);
                break;
            }

            case CursorType.Underscore:
            {
                double height = Math.Max(thickness, Math.Round(CellHeight * 0.1 * scale) / scale);
                context.FillRectangle(new Rect(rect.X, rect.Bottom - height, rect.Width, height), cursorColor);
                break;
            }

            case CursorType.DoubleUnderscore:
            {
                context.FillRectangle(new Rect(rect.X, rect.Bottom - thickness, rect.Width, thickness), cursorColor);
                context.FillRectangle(new Rect(rect.X, rect.Bottom - (3 * thickness), rect.Width, thickness), cursorColor);
                break;
            }

            case CursorType.EmptyBox:
                context.DrawRectangle(rect, cursorColor, thickness, strokeInset: true);
                break;
            default:
                context.FillRectangle(rect, cursorColor);
                if (!string.IsNullOrEmpty(glyph) && glyph != " ")
                {
                    DrawComplexCell(context, glyph, span, cell.X, cell.Y, glyphColor, bold, italic, scale);
                }

                break;
        }
    }

    /// <summary>Draws text being composed by an input method at the cursor, underlined, over the default background.</summary>
    public double RenderComposition(IGraphicsContext context, Point origin, string text, Color foreground, Color background)
    {
        if (_textEngine == null || text.Length == 0)
        {
            return 0;
        }

        var layout = CreateLayout(text, bold: false, italic: false);
        double width = Math.Ceiling(layout.MeasuredSize.Width / CellWidth) * CellWidth;
        double scale = context.DpiScale;
        context.FillRectangle(LayoutRounding.SnapRectEdgesToPixels(new Rect(origin.X, origin.Y, width, CellHeight), scale), background);
        double top = origin.Y + (_baseline - (layout.Lines.Count > 0 ? layout.Lines[0].Baseline : _baseline));
        context.Text.Draw(layout, new Point(origin.X, top), new TextDrawOptions(foreground, Transient: true));
        double thickness = Math.Max(1, Math.Round(scale)) / scale;
        context.FillRectangle(new Rect(origin.X, origin.Y + CellHeight - thickness, width, thickness), foreground);
        return width;
    }

    /// <summary>The width in cells that composing <paramref name="text"/> takes.</summary>
    public int MeasureCompositionCells(string text)
    {
        if (_textEngine == null || text.Length == 0)
        {
            return 0;
        }

        var layout = CreateLayout(text, bold: false, italic: false);
        return Math.Max(1, (int)Math.Ceiling(layout.MeasuredSize.Width / CellWidth));
    }

    public static Color ToColor(uint colorRef, byte alpha = 0xFF) =>
        Color.FromArgb(alpha, (byte)(colorRef & 0xFF), (byte)((colorRef >> 8) & 0xFF), (byte)((colorRef >> 16) & 0xFF));

    private Rect ColumnRect(Rect bounds, int start, int end) =>
        new(bounds.X + (start * CellWidth), bounds.Y, Math.Max(0, end - start) * CellWidth, CellHeight);

    private void EnsureScratch(int columns)
    {
        if (_rowChars.Length < columns)
        {
            _rowChars = new char[columns];
            _foregrounds = new uint[columns];
            _backgrounds = new uint[columns];
            _attributes = new TextAttribute[columns];
            _glyphs = new string?[columns];
            _spans = new byte[columns];
            _simple = new bool[columns];
        }
    }

    /// <summary>Whether a character can go in the row's shared layout: one cell wide in the main font.</summary>
    private bool IsSimple(char character)
    {
        if (character == ' ')
        {
            return true;
        }

        if (!_fontIsMonospace || char.IsSurrogate(character) || TerminalBoxDrawing.Handles(character))
        {
            return false;
        }

        if (character >= '!' && character <= '~')
        {
            return true;
        }

        if (!_narrow.TryGetValue(character, out bool narrow))
        {
            var layout = CreateLayout(character.ToString(), bold: false, italic: false);
            narrow = Math.Abs(layout.MeasuredSize.Width - CellWidth) < 0.5;
            _narrow[character] = narrow;
        }

        return narrow;
    }

    private void DrawSimpleText(IGraphicsContext context, int length, Point origin, bool intenseIsBold, object owner)
    {
        _paintSpans.Clear();
        _styleRuns.Clear();

        uint defaultForeground = _foregrounds[0];
        int column = 0;
        while (column < length)
        {
            if (_rowChars[column] == ' ')
            {
                column++;
                continue;
            }

            uint foreground = _foregrounds[column];
            var attr = _attributes[column];
            bool bold = intenseIsBold && attr.IsIntense();
            bool italic = attr.IsItalic();
            int end = column + 1;
            while (end < length && _rowChars[end] != ' ' && _foregrounds[end] == foreground
                && (intenseIsBold && _attributes[end].IsIntense()) == bold && _attributes[end].IsItalic() == italic)
            {
                end++;
            }

            if (foreground != defaultForeground)
            {
                _paintSpans.Add(new TextPaintSpan(new TextRange(column, end - column), ToColor(foreground)));
            }

            if (bold || italic)
            {
                _styleRuns.Add(new GeometryStyleRun(column, end - column, _style with { Weight = bold ? FontWeight.Bold : _style.Weight, Italic = italic }));
            }

            column = end;
        }

        var request = new TextLayoutRequest
        {
            Text = new string(_rowChars, 0, length).AsMemory(),
            Dpi = _dpi,
            DefaultStyle = _style,
            Runs = _styleRuns.Count == 0 ? Array.Empty<GeometryStyleRun>() : _styleRuns.ToArray(),
        };
        var layout = _textEngine!.GetOrCreateLayout(request, TextLayoutCachePolicy.Owner, owner);
        ReadOnlyMemory<TextPaintSpan> spans = _paintSpans.Count == 0 ? default : _paintSpans.ToArray();
        context.Text.Draw(layout, origin, new TextDrawOptions(ToColor(defaultForeground), spans, Owner: owner));
    }

    private void DrawComplexCell(IGraphicsContext context, string glyph, int span, double cellX, double cellY, Color foreground,
        bool bold, bool italic, double scale)
    {
        if (glyph.Length == 0 || glyph == " ")
        {
            return;
        }

        double width = span * CellWidth;
        if (glyph.Length == 1 && TerminalBoxDrawing.Handles(glyph[0]))
        {
            TerminalBoxDrawing.Draw(context, glyph[0], new Rect(cellX, cellY, width, CellHeight), foreground, scale);
            return;
        }

        var layout = GetClusterLayout(glyph, bold, italic);
        double measured = layout.MeasuredSize.Width;
        double baseline = layout.Lines.Count > 0 ? layout.Lines[0].Baseline : _baseline;
        double top = cellY + (_baseline - baseline);
        var options = new TextDrawOptions(foreground);
        if (measured > width + 0.5 && measured > 0)
        {
            // A glyph wider than its cells is squeezed into them, so it cannot cover its neighbors.
            double ratio = width / measured;
            context.Save();
            context.Translate(cellX, top);
            context.Scale(ratio, 1);
            context.Text.Draw(layout, new Point(0, 0), in options);
            context.Restore();
        }
        else
        {
            double inset = Math.Max(0, (width - measured) / 2);
            context.Text.Draw(layout, new Point(cellX + inset, top), in options);
        }
    }

    private void DrawDecorations(IGraphicsContext context, in TextAttribute attr, RenderSettings settings, double cellX, double cellY,
        double width, Color foreground, double scale)
    {
        double thickness = Math.Max(1, Math.Round(scale)) / scale;
        double underlineY = Math.Min(cellY + CellHeight - thickness, cellY + _baseline + thickness);
        var underlineColor = ToColor(settings.GetAttributeUnderlineColor(attr));

        switch (attr.UnderlineStyle)
        {
            case UnderlineStyle.SinglyUnderlined:
                FillSnapped(context, new Rect(cellX, underlineY, width, thickness), underlineColor, scale);
                break;
            case UnderlineStyle.DoublyUnderlined:
            {
                double gap = 2 * thickness;
                double upperY = Math.Max(cellY + _baseline, underlineY - gap);
                FillSnapped(context, new Rect(cellX, upperY, width, thickness), underlineColor, scale);
                FillSnapped(context, new Rect(cellX, Math.Min(cellY + CellHeight - thickness, upperY + gap), width, thickness), underlineColor, scale);
                break;
            }

            case UnderlineStyle.CurlyUnderlined:
            {
                // A wave of one period per cell, drawn as line segments.
                double amplitude = Math.Max(thickness, Math.Min(2 * thickness, (CellHeight - _baseline) / 3));
                double centerY = Math.Min(underlineY, cellY + CellHeight - amplitude - thickness);
                const int SEGMENTS = 8;
                double step = CellWidth / SEGMENTS;
                var previous = new Point(cellX, centerY);
                for (double offset = step; offset <= width + 0.01; offset += step)
                {
                    double phase = offset / CellWidth * 2 * Math.PI;
                    var next = new Point(cellX + offset, centerY - (Math.Sin(phase) * amplitude));
                    context.DrawLine(previous, next, underlineColor, thickness);
                    previous = next;
                }

                break;
            }

            case UnderlineStyle.DottedUnderlined:
                for (double offset = 0; offset < width; offset += 2 * thickness)
                {
                    FillSnapped(context, new Rect(cellX + offset, underlineY, thickness, thickness), underlineColor, scale);
                }

                break;
            case UnderlineStyle.DashedUnderlined:
            {
                double dash = Math.Max(2 * thickness, CellWidth / 3);
                for (double offset = 0; offset < width; offset += 2 * dash)
                {
                    FillSnapped(context, new Rect(cellX + offset, underlineY, Math.Min(dash, width - offset), thickness), underlineColor, scale);
                }

                break;
            }

            default:
                // A hyperlink without its own underline is marked with a dashed one in the text color.
                if (attr.IsHyperlink())
                {
                    for (double offset = 0; offset < width; offset += 2 * thickness)
                    {
                        FillSnapped(context, new Rect(cellX + offset, underlineY, thickness, thickness), foreground, scale);
                    }
                }

                break;
        }

        if (attr.IsCrossedOut())
        {
            FillSnapped(context, new Rect(cellX, cellY + (_baseline * 0.65), width, thickness), foreground, scale);
        }

        if (attr.IsOverlined())
        {
            FillSnapped(context, new Rect(cellX, cellY, width, thickness), foreground, scale);
        }
    }

    private static void FillSnapped(IGraphicsContext context, Rect rect, Color color, double scale) =>
        context.FillRectangle(LayoutRounding.SnapRectEdgesToPixels(rect, scale), color);

    private ITextLayout GetClusterLayout(string glyph, bool bold, bool italic)
    {
        if (_clusterLayouts.Count > CLUSTER_CACHE_LIMIT)
        {
            _clusterLayouts.Clear();
        }

        var key = (glyph, bold, italic);
        if (!_clusterLayouts.TryGetValue(key, out var layout))
        {
            layout = CreateLayout(glyph, bold, italic);
            _clusterLayouts[key] = layout;
        }

        return layout;
    }

    private ITextLayout CreateLayout(string text, bool bold, bool italic)
    {
        var style = _style;
        if (bold)
        {
            style = style with { Weight = FontWeight.Bold };
        }

        if (italic)
        {
            style = style with { Italic = true };
        }

        var request = new TextLayoutRequest
        {
            Text = text.AsMemory(),
            Dpi = _dpi,
            DefaultStyle = style,
        };
        return _textEngine!.CreateLayout(request);
    }
}
