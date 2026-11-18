using System.Runtime.InteropServices;

using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Where a glyph's pixels sit in a glyph atlas image; <see cref="Left"/> and <see cref="Top"/> place them from the pen
/// when the glyph was added with that offset, and <see cref="Plot"/> names the block of the image holding them.
/// </summary>
internal readonly record struct MewVGAtlasGlyph(
    int ImageId,
    int X,
    int Y,
    int Width,
    int Height,
    bool IsColor,
    int Left,
    int Top,
    MewVGAtlasPlotUse Plot);

/// <summary>A block of an atlas image as it was when a glyph was taken from it; stale once the block is reused.</summary>
internal readonly record struct MewVGAtlasPlotUse(int Index, int Generation);

/// <summary>
/// Glyph images packed into a few large textures, so text is drawn as quads sampled from them and each glyph is
/// uploaded once instead of with every text it appears in. Coverage glyphs share alpha pages; colour glyphs share
/// premultiplied RGBA pages. Each page is cut into blocks that fill and empty as a whole: a full atlas empties the
/// block used longest ago, and every block written during a frame is uploaded once before the frame is flushed.
/// </summary>
internal sealed class MewVGGlyphAtlas : IDisposable
{
    private const int COVERAGE_PAGE_SIZE = 2048;
    private const int COVERAGE_PLOT_WIDTH = 512;
    private const int COVERAGE_PLOT_HEIGHT = 256;
    private const int COLOR_PAGE_SIZE = 1024;
    private const int COLOR_PLOT_SIZE = 256;
    // Pages of one kind an atlas keeps at the end of a frame.
    private const int MAX_PAGES = 4;
    // Bytes of glyphs too large for a block, each on an image of its own, kept past the frame that drew them.
    private const long LARGE_GLYPH_BUDGET = 4L * 1024 * 1024;
    // Empty texels around every glyph, so a sample at a glyph's edge never reads its neighbour.
    private const int GLYPH_PADDING = 1;
    // Shelf heights are rounded up to this, so glyphs of nearly the same height share a shelf.
    private const int SHELF_HEIGHT_STEP = 4;

    private readonly MewVGContext _vg;
    private readonly Dictionary<GlyphKey, MewVGAtlasGlyph> _glyphs = new();
    // Every block ever made, by index; a block whose page is gone waits in the free list to serve a new page.
    private readonly List<Plot> _plots = new();
    private readonly Stack<Plot> _freePlots = new();
    private readonly List<Plot> _dirtyPlots = new();
    private readonly PageKind _coverage = new(color: false, COVERAGE_PAGE_SIZE, COVERAGE_PAGE_SIZE, COVERAGE_PLOT_WIDTH, COVERAGE_PLOT_HEIGHT);
    private readonly PageKind _color = new(color: true, COLOR_PAGE_SIZE, COLOR_PAGE_SIZE, COLOR_PLOT_SIZE, COLOR_PLOT_SIZE);
    private readonly List<Page> _largePages = new();
    private long _largeBytes;
    // Counts frames; a block used during the current one is never emptied before the frame is flushed.
    private long _frame;
    private bool _disposed;

    public MewVGGlyphAtlas(MewVGContext vg)
    {
        _vg = vg;
    }

    public bool TryGet(object font, uint glyphId, out MewVGAtlasGlyph glyph)
    {
        if (!_glyphs.TryGetValue(new GlyphKey(font, glyphId), out glyph))
        {
            return false;
        }

        _plots[glyph.Plot.Index].LastUse = _frame;
        return true;
    }

    /// <summary>Marks the blocks a kept text samples as used; false when one was emptied since.</summary>
    public bool TryUse(ReadOnlySpan<MewVGAtlasPlotUse> plots)
    {
        foreach (var use in plots)
        {
            var plot = _plots[use.Index];
            if (plot.Generation != use.Generation || plot.Page == null)
            {
                return false;
            }
        }

        foreach (var use in plots)
        {
            _plots[use.Index].LastUse = _frame;
        }

        return true;
    }

    /// <summary>Adds 8-bit coverage with its rows tightly packed; false when no texture can hold it.</summary>
    public bool TryAddCoverage(object font, uint glyphId, ReadOnlySpan<byte> coverage, int width, int height, out MewVGAtlasGlyph glyph,
        int left = 0, int top = 0)
        => TryAdd(_coverage, new GlyphKey(font, glyphId), coverage, width, height, left, top, out glyph);

    /// <summary>Adds premultiplied BGRA pixels with their rows tightly packed; false when no texture can hold them.</summary>
    public bool TryAddColor(object font, uint glyphId, ReadOnlySpan<byte> bgra, int width, int height, out MewVGAtlasGlyph glyph,
        int left = 0, int top = 0)
        => TryAdd(_color, new GlyphKey(font, glyphId), bgra, width, height, left, top, out glyph);

    /// <summary>Uploads the rows of every block written since the last call; called before each flush of draws.</summary>
    public void FlushUploads()
    {
        foreach (var plot in _dirtyPlots)
        {
            var page = plot.Page;
            if (page != null && plot.Data != null)
            {
                int rowBytes = plot.Width * page.BytesPerPixel;
                _vg.UpdateImageRegion(page.ImageId, plot.X, plot.Y + plot.DirtyTop, plot.Width, plot.DirtyBottom - plot.DirtyTop,
                    plot.Data.AsSpan(plot.DirtyTop * rowBytes, (plot.DirtyBottom - plot.DirtyTop) * rowBytes));
            }

            plot.ClearDirty();
        }

        _dirtyPlots.Clear();
    }

    /// <summary>
    /// Called once a frame's draws are flushed: pages past the budget, added while every block was in use, go
    /// again, least recently used first, since no queued draw can still sample them.
    /// </summary>
    public void EndFrame()
    {
        TrimPages(_coverage.Pages, MAX_PAGES);
        TrimPages(_color.Pages, MAX_PAGES);
        while (_largeBytes > LARGE_GLYPH_BUDGET && _largePages.Count > 0)
        {
            int oldest = IndexOfLeastRecentlyUsed(_largePages);
            var page = _largePages[oldest];
            _largePages.RemoveAt(oldest);
            _largeBytes -= page.Bytes;
            DeletePage(page);
        }

        _frame++;
    }

    /// <summary>Removes every glyph and deletes the pages; only safe once no queued draw samples them.</summary>
    public void Clear()
    {
        foreach (var page in _coverage.Pages)
        {
            DeletePage(page);
        }

        foreach (var page in _color.Pages)
        {
            DeletePage(page);
        }

        foreach (var page in _largePages)
        {
            DeletePage(page);
        }

        _coverage.Pages.Clear();
        _color.Pages.Clear();
        _largePages.Clear();
        _largeBytes = 0;
        foreach (var plot in _dirtyPlots)
        {
            plot.ClearDirty();
        }

        _dirtyPlots.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Clear();
        _disposed = true;
    }

    private bool TryAdd(PageKind kind, GlyphKey key, ReadOnlySpan<byte> pixels, int width, int height, int left, int top,
        out MewVGAtlasGlyph glyph)
    {
        glyph = default;
        if (_disposed || width <= 0 || height <= 0)
        {
            return false;
        }

        // The room a glyph takes in a block: its shelf's height and the padding around it.
        int paddedWidth = GLYPH_PADDING + width + GLYPH_PADDING;
        int paddedHeight = GLYPH_PADDING + ShelfHeight(height) + GLYPH_PADDING;
        Plot plot;
        int x;
        int y;
        if (paddedWidth > kind.PlotWidth || paddedHeight > kind.PlotHeight)
        {
            if (!TryAddLargePage(kind, paddedWidth, paddedHeight, out plot) || !plot.TryAllocate(width, height, out x, out y))
            {
                return false;
            }
        }
        else if (!TryAllocate(kind, width, height, out plot, out x, out y))
        {
            return false;
        }

        plot.Write(pixels, x, y, width, height, kind.Color);
        if (!plot.IsDirtyListed)
        {
            plot.IsDirtyListed = true;
            _dirtyPlots.Add(plot);
        }

        plot.LastUse = _frame;
        plot.Glyphs.Add(key);
        glyph = new MewVGAtlasGlyph(plot.Page!.ImageId, plot.X + x, plot.Y + y, width, height, kind.Color, left, top,
            new MewVGAtlasPlotUse(plot.Index, plot.Generation));
        _glyphs[key] = glyph;
        return true;
    }

    private bool TryAllocate(PageKind kind, int width, int height, out Plot plot, out int x, out int y)
    {
        // The block written last is the likeliest to have room.
        var recent = kind.LastPlot;
        if (recent != null && recent.TryAllocate(width, height, out x, out y))
        {
            plot = recent;
            return true;
        }

        foreach (var page in kind.Pages)
        {
            foreach (var candidate in page.Plots)
            {
                if (candidate.TryAllocate(width, height, out x, out y))
                {
                    plot = kind.LastPlot = candidate;
                    return true;
                }
            }
        }

        Plot? reusable = null;
        if (kind.Pages.Count >= MAX_PAGES)
        {
            foreach (var page in kind.Pages)
            {
                foreach (var candidate in page.Plots)
                {
                    if (candidate.LastUse < _frame && (reusable == null || candidate.LastUse < reusable.LastUse))
                    {
                        reusable = candidate;
                    }
                }
            }
        }

        if (reusable != null)
        {
            Empty(reusable);
        }
        else
        {
            // Below the budget, or with every block in use by this frame: a page more, trimmed at the frame's end.
            var page = CreatePage(kind.Color, kind.PageWidth, kind.PageHeight, kind.PlotWidth, kind.PlotHeight);
            if (page == null)
            {
                plot = null!;
                x = 0;
                y = 0;
                return false;
            }

            kind.Pages.Add(page);
            reusable = page.Plots[0];
        }

        kind.LastPlot = reusable;
        plot = reusable;
        return reusable.TryAllocate(width, height, out x, out y);
    }

    private bool TryAddLargePage(PageKind kind, int width, int height, out Plot plot)
    {
        var page = CreatePage(kind.Color, width, height, width, height);
        if (page == null)
        {
            plot = null!;
            return false;
        }

        _largePages.Add(page);
        _largeBytes += page.Bytes;
        plot = page.Plots[0];
        return true;
    }

    private Page? CreatePage(bool color, int width, int height, int plotWidth, int plotHeight)
    {
        // Rows are uploaded before anything samples them, so the image starts without contents.
        int imageId = color
            ? _vg.CreateImageRGBA(width, height, MewVGImageFlags.Nearest | MewVGImageFlags.Premultiplied, [])
            : _vg.CreateImageAlpha(width, height, MewVGImageFlags.Nearest, []);
        if (imageId == 0)
        {
            return null;
        }

        int bytesPerPixel = color ? 4 : 1;
        var page = new Page(imageId, bytesPerPixel, (long)width * height * bytesPerPixel);
        for (int plotY = 0; plotY + plotHeight <= height; plotY += plotHeight)
        {
            for (int plotX = 0; plotX + plotWidth <= width; plotX += plotWidth)
            {
                Plot plot;
                if (_freePlots.Count > 0)
                {
                    plot = _freePlots.Pop();
                }
                else
                {
                    plot = new Plot(_plots.Count);
                    _plots.Add(plot);
                }

                plot.Attach(page, plotX, plotY, plotWidth, plotHeight);
                page.Plots.Add(plot);
            }
        }

        RenderResourceMetrics.TextCacheBytesChanged(page.Bytes);
        return page;
    }

    private void TrimPages(List<Page> pages, int maxPages)
    {
        while (pages.Count > maxPages)
        {
            int oldest = IndexOfLeastRecentlyUsed(pages);
            var page = pages[oldest];
            pages.RemoveAt(oldest);
            DeletePage(page);
        }
    }

    private static int IndexOfLeastRecentlyUsed(List<Page> pages)
    {
        int oldest = 0;
        long oldestUse = long.MaxValue;
        for (int index = 0; index < pages.Count; index++)
        {
            long lastUse = long.MinValue;
            foreach (var plot in pages[index].Plots)
            {
                lastUse = Math.Max(lastUse, plot.LastUse);
            }

            if (lastUse < oldestUse)
            {
                oldestUse = lastUse;
                oldest = index;
            }
        }

        return oldest;
    }

    private void DeletePage(Page page)
    {
        foreach (var plot in page.Plots)
        {
            // A block that goes to the free list may come back on a page of the other kind.
            if (_coverage.LastPlot == plot)
            {
                _coverage.LastPlot = null;
            }

            if (_color.LastPlot == plot)
            {
                _color.LastPlot = null;
            }

            Empty(plot);
            plot.Detach();
            _freePlots.Push(plot);
        }

        _vg.DeleteImage(page.ImageId);
        RenderResourceMetrics.TextCacheBytesChanged(-page.Bytes);
    }

    private void Empty(Plot plot)
    {
        foreach (var key in plot.Glyphs)
        {
            _glyphs.Remove(key);
        }

        plot.Reset();
    }

    private static int ShelfHeight(int glyphHeight)
        => (glyphHeight + SHELF_HEIGHT_STEP - 1) / SHELF_HEIGHT_STEP * SHELF_HEIGHT_STEP;

    private readonly record struct GlyphKey(object Font, uint GlyphId);

    /// <summary>The pages of one pixel format and the size of their blocks.</summary>
    private sealed class PageKind(bool color, int pageWidth, int pageHeight, int plotWidth, int plotHeight)
    {
        public bool Color { get; } = color;

        public int PageWidth { get; } = pageWidth;

        public int PageHeight { get; } = pageHeight;

        public int PlotWidth { get; } = plotWidth;

        public int PlotHeight { get; } = plotHeight;

        public List<Page> Pages { get; } = new();

        public Plot? LastPlot { get; set; }
    }

    /// <summary>One atlas texture and the blocks it is cut into.</summary>
    private sealed class Page(int imageId, int bytesPerPixel, long bytes)
    {
        public int ImageId { get; } = imageId;

        public int BytesPerPixel { get; } = bytesPerPixel;

        public long Bytes { get; } = bytes;

        public List<Plot> Plots { get; } = new();
    }

    /// <summary>
    /// A block of a page, filled shelf by shelf from the top and emptied as a whole. Its pixels are kept on the CPU
    /// too, so the rows written during a frame go up in one upload.
    /// </summary>
    private sealed class Plot(int index)
    {
        private readonly List<Shelf> _shelves = new();
        private int _nextShelfY = GLYPH_PADDING;

        public int Index { get; } = index;

        /// <summary>Changes whenever the block is emptied, so quads taken from it before are known to be stale.</summary>
        public int Generation { get; private set; }

        public Page? Page { get; private set; }

        public int X { get; private set; }

        public int Y { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public long LastUse { get; set; } = -1;

        public List<GlyphKey> Glyphs { get; } = new();

        public byte[]? Data { get; private set; }

        public int DirtyTop { get; private set; } = int.MaxValue;

        public int DirtyBottom { get; private set; }

        public bool IsDirtyListed { get; set; }

        public void Attach(Page page, int x, int y, int width, int height)
        {
            Page = page;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            LastUse = -1;
        }

        public void Detach()
        {
            Page = null;
            Data = null;
            LastUse = -1;
        }

        public void Reset()
        {
            Generation++;
            Glyphs.Clear();
            _shelves.Clear();
            _nextShelfY = GLYPH_PADDING;
            // Rows are uploaded whole, so stale glyphs beside a new one must not go up again.
            if (Data != null)
            {
                Array.Clear(Data);
            }
        }

        public void ClearDirty()
        {
            DirtyTop = int.MaxValue;
            DirtyBottom = 0;
            IsDirtyListed = false;
        }

        public bool TryAllocate(int glyphWidth, int glyphHeight, out int x, out int y)
        {
            int shelfHeight = ShelfHeight(glyphHeight);
            for (int index = 0; index < _shelves.Count; index++)
            {
                var shelf = _shelves[index];
                if (shelf.Height == shelfHeight && shelf.NextX + glyphWidth + GLYPH_PADDING <= Width)
                {
                    x = shelf.NextX;
                    y = shelf.Y;
                    _shelves[index] = shelf with { NextX = shelf.NextX + glyphWidth + GLYPH_PADDING };
                    return true;
                }
            }

            if (Page == null || _nextShelfY + shelfHeight + GLYPH_PADDING > Height || GLYPH_PADDING + glyphWidth + GLYPH_PADDING > Width)
            {
                x = 0;
                y = 0;
                return false;
            }

            x = GLYPH_PADDING;
            y = _nextShelfY;
            _shelves.Add(new Shelf(_nextShelfY, shelfHeight, GLYPH_PADDING + glyphWidth + GLYPH_PADDING));
            _nextShelfY += shelfHeight + GLYPH_PADDING;
            return true;
        }

        /// <summary>Copies a glyph into the block's pixels at its place and widens the rows to upload.</summary>
        public void Write(ReadOnlySpan<byte> pixels, int x, int y, int width, int height, bool color)
        {
            int bytesPerPixel = color ? 4 : 1;
            int rowBytes = Width * bytesPerPixel;
            Data ??= new byte[Height * rowBytes];
            int glyphRowBytes = width * bytesPerPixel;
            for (int row = 0; row < height; row++)
            {
                var source = pixels.Slice(row * glyphRowBytes, glyphRowBytes);
                var target = Data.AsSpan(((y + row) * rowBytes) + (x * bytesPerPixel), glyphRowBytes);
                if (color)
                {
                    // BGRA in, RGBA out: the colour pages are RGBA, which every backend can update in place.
                    var sourcePixels = MemoryMarshal.Cast<byte, uint>(source);
                    var targetPixels = MemoryMarshal.Cast<byte, uint>(target);
                    for (int column = 0; column < sourcePixels.Length; column++)
                    {
                        uint bgra = sourcePixels[column];
                        targetPixels[column] = (bgra & 0xFF00FF00u) | ((bgra & 0x00FF0000u) >> 16) | ((bgra & 0x000000FFu) << 16);
                    }
                }
                else
                {
                    source.CopyTo(target);
                }
            }

            // The padding rows above and below go up too, so a sample past the glyph's edge reads nothing.
            DirtyTop = Math.Min(DirtyTop, Math.Max(0, y - GLYPH_PADDING));
            DirtyBottom = Math.Max(DirtyBottom, Math.Min(Height, y + height + GLYPH_PADDING));
        }
    }

    private readonly record struct Shelf(int Y, int Height, int NextX);
}

/// <summary>
/// Laid-out texts kept as atlas quads, least recently used first out, each valid while the atlas still holds the
/// blocks it samples. The key narrows the lookup; the text itself confirms a hit.
/// </summary>
internal sealed class MewVGGlyphRunCache<TKey> where TKey : notnull
{
    // A quad is a few dozen bytes, so this bounds the count of texts, not their bytes.
    private const int MAX_RUNS = 4096;

    private readonly Dictionary<TKey, LinkedListNode<Entry>> _runs = new();
    private readonly LinkedList<Entry> _lru = new();

    public bool TryGet(TKey key, ReadOnlySpan<char> text, MewVGGlyphAtlas atlas, out MewVGGlyphRun run)
    {
        if (_runs.TryGetValue(key, out var node) && text.SequenceEqual(node.Value.Text))
        {
            if (atlas.TryUse(node.Value.Run.Plots))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                run = node.Value.Run;
                return true;
            }

            _lru.Remove(node);
            _runs.Remove(key);
        }

        run = null!;
        return false;
    }

    public void Store(TKey key, ReadOnlySpan<char> text, MewVGGlyphRun run)
    {
        if (_runs.TryGetValue(key, out var replaced))
        {
            _lru.Remove(replaced);
        }

        var node = new LinkedListNode<Entry>(new Entry(key, text.ToString(), run));
        _lru.AddFirst(node);
        _runs[key] = node;
        while (_runs.Count > MAX_RUNS && _lru.Last != null)
        {
            var last = _lru.Last;
            _lru.RemoveLast();
            _runs.Remove(last.Value.Key);
        }
    }

    public void Clear()
    {
        _runs.Clear();
        _lru.Clear();
    }

    private readonly record struct Entry(TKey Key, string Text, MewVGGlyphRun Run);
}

/// <summary>A text's glyphs as quads in raster pixels sampling atlas images, in drawing order.</summary>
internal sealed class MewVGGlyphRun(MewVGImageQuad[] quads, MewVGGlyphRunSegment[] segments, MewVGAtlasPlotUse[] plots)
{
    public MewVGImageQuad[] Quads { get; } = quads;

    public MewVGGlyphRunSegment[] Segments { get; } = segments;

    /// <summary>The atlas blocks the quads sample, as they were when the quads were taken.</summary>
    public MewVGAtlasPlotUse[] Plots { get; } = plots;
}

/// <summary>Consecutive quads of a glyph run that sample the same atlas image.</summary>
internal readonly record struct MewVGGlyphRunSegment(int ImageId, bool IsColor, int Start, int Count);

/// <summary>Collects a text's atlas quads, merging consecutive quads of one image into a segment.</summary>
internal sealed class MewVGGlyphRunBuilder
{
    private readonly List<MewVGImageQuad> _quads = new();
    private readonly List<MewVGGlyphRunSegment> _segments = new();
    private readonly List<MewVGAtlasPlotUse> _plots = new();

    public int QuadCount => _quads.Count;

    public void Reset()
    {
        _quads.Clear();
        _segments.Clear();
        _plots.Clear();
    }

    public void Add(in MewVGAtlasGlyph glyph, in MewVGImageQuad quad)
    {
        if (_segments.Count > 0 && _segments[^1].ImageId == glyph.ImageId)
        {
            var last = _segments[^1];
            _segments[^1] = last with { Count = last.Count + 1 };
        }
        else
        {
            _segments.Add(new MewVGGlyphRunSegment(glyph.ImageId, glyph.IsColor, _quads.Count, 1));
        }

        if (!_plots.Contains(glyph.Plot))
        {
            _plots.Add(glyph.Plot);
        }

        _quads.Add(quad);
    }

    public ReadOnlySpan<MewVGImageQuad> Quads => CollectionsMarshal.AsSpan(_quads);

    public ReadOnlySpan<MewVGGlyphRunSegment> Segments => CollectionsMarshal.AsSpan(_segments);

    public MewVGGlyphRun ToRun() => new([.. _quads], [.. _segments], [.. _plots]);
}
