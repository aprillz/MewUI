// Ported from Windows Terminal src/terminal/adapter/PageManager.hpp and PageManager.cpp (MIT).

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Adapter;

/// <summary>One VT page: the buffer it lives in and the rows of that buffer it covers.</summary>
internal struct Page
{
    private readonly TextBuffer _buffer;
    private TilRect _viewport;
    private readonly int _number;

    public Page(TextBuffer buffer, TilRect viewport, int number)
    {
        _buffer = buffer;
        _viewport = viewport;
        _number = number;
    }

    public readonly TextBuffer Buffer => _buffer;

    public readonly TilRect Viewport => _viewport;

    public readonly int Number => _number;

    public readonly Cursor Cursor => _buffer.Cursor;

    public readonly TextAttribute Attributes => _buffer.CurrentAttributes;

    public readonly void SetAttributes(TextAttribute attributes) => _buffer.CurrentAttributes = attributes;

    public readonly TilSize Size => new(Width, Height);

    public readonly int Top => _viewport.Top;

    public readonly int Bottom => _viewport.Bottom;

    public readonly int Width => _buffer.Size.Width;

    public readonly int Height => Bottom - Top;

    public readonly int BufferHeight => _buffer.Size.Height;

    public readonly int XPanOffset => _viewport.Left;

    // Vertical panning is not supported.
    public readonly int YPanOffset => 0;

    public void MoveViewportDown() => _viewport = _viewport with { Top = _viewport.Top + 1, Bottom = _viewport.Bottom + 1 };
}

/// <summary>
/// The VT pages of the main buffer (NP, PP, PPA): page 1 is the visible one; the others are off-screen buffers that
/// swap their rows with the screen when made visible.
/// </summary>
internal sealed class PageManager
{
    private const int MAX_PAGES = 6;

    private readonly ITerminalApi _api;
    private readonly Renderer? _renderer;
    private readonly TextBuffer?[] _buffers = new TextBuffer?[MAX_PAGES];
    private int _activePageNumber = 1;
    private int _visiblePageNumber = 1;

    public PageManager(ITerminalApi api, Renderer? renderer)
    {
        _api = api;
        _renderer = renderer;
    }

    public void Reset()
    {
        _activePageNumber = 1;
        _visiblePageNumber = 1;
        Array.Clear(_buffers);
    }

    public Page Get(int pageNumber)
    {
        int requestedPageNumber = Math.Min(Math.Max(pageNumber, 1), MAX_PAGES);
        var (visibleBuffer, visibleViewport, isMainBuffer) = _api.GetBufferAndViewport();

        // The alternate buffer has a single page.
        if (!isMainBuffer)
        {
            return new Page(visibleBuffer, visibleViewport, 1);
        }

        if (requestedPageNumber == _visiblePageNumber)
        {
            return new Page(visibleBuffer, visibleViewport, _visiblePageNumber);
        }

        var pageSize = visibleViewport.Size;
        var pageBuffer = GetBuffer(requestedPageNumber, pageSize);
        return new Page(pageBuffer, new TilRect(pageSize), requestedPageNumber);
    }

    public Page ActivePage() => Get(_activePageNumber);

    public Page VisiblePage() => Get(_visiblePageNumber);

    public void MoveTo(int pageNumber, bool makeVisible)
    {
        var (visibleBuffer, visibleViewport, isMainBuffer) = _api.GetBufferAndViewport();
        if (!isMainBuffer)
        {
            return;
        }

        var pageSize = visibleViewport.Size;
        int visibleTop = visibleViewport.Top;
        bool wasVisible = _activePageNumber == _visiblePageNumber;
        int newPageNumber = Math.Min(Math.Max(pageNumber, 1), MAX_PAGES);
        bool redrawRequired = false;

        if (makeVisible && _visiblePageNumber != newPageNumber)
        {
            // Save the screen into the old page's buffer and copy the new page onto the screen.
            var newBuffer = GetBuffer(newPageNumber, pageSize);
            var saveBuffer = GetBuffer(_visiblePageNumber, pageSize);
            for (int row = 0; row < pageSize.Height; row++)
            {
                visibleBuffer.CopyRow(visibleTop + row, row, saveBuffer);
            }

            for (int row = 0; row < pageSize.Height; row++)
            {
                newBuffer.CopyRow(row, visibleTop + row, visibleBuffer);
            }

            _visiblePageNumber = newPageNumber;
            redrawRequired = true;
        }

        bool isVisible = newPageNumber == _visiblePageNumber;
        if (!wasVisible || !isVisible)
        {
            // The cursor and attributes follow the active page into its buffer.
            var oldBuffer = wasVisible ? visibleBuffer : GetBuffer(_activePageNumber, pageSize);
            var targetBuffer = isVisible ? visibleBuffer : GetBuffer(newPageNumber, pageSize);
            if (!ReferenceEquals(oldBuffer, targetBuffer))
            {
                int oldTop = wasVisible ? visibleTop : 0;
                int newTop = isVisible ? visibleTop : 0;
                var position = oldBuffer.Cursor.Position;
                position = position with { Y = position.Y - oldTop + newTop };
                targetBuffer.CurrentAttributes = oldBuffer.CurrentAttributes;
                targetBuffer.CopyProperties(oldBuffer);
                targetBuffer.Cursor.SetPosition(position);
            }

            // A cursor on a page that is not shown is not shown either.
            if (wasVisible && !isVisible)
            {
                visibleBuffer.Cursor.SetIsVisible(false);
            }
        }

        _activePageNumber = newPageNumber;
        if (redrawRequired)
        {
            _renderer?.TriggerRedrawAll();
        }
    }

    public void MoveRelative(int pageCount, bool makeVisible) => MoveTo(_activePageNumber + pageCount, makeVisible);

    public void MakeActivePageVisible()
    {
        if (_activePageNumber != _visiblePageNumber)
        {
            MoveTo(_activePageNumber, true);
        }
    }

    private TextBuffer GetBuffer(int pageNumber, TilSize pageSize)
    {
        var buffer = _buffers[pageNumber - 1];
        if (buffer == null)
        {
            buffer = new TextBuffer(pageSize, default, 0, false, _renderer);
            _buffers[pageNumber - 1] = buffer;
        }
        else if (buffer.Size.Dimensions != pageSize)
        {
            buffer.ResizeTraditional(pageSize);
        }

        return buffer;
    }
}
