// Replaces Windows Terminal src/renderer/base/renderer.cpp (MIT): the buffer and dispatcher report what changed here,
// and the view reads and clears it when it draws, instead of a render thread painting through an engine.

using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Core;

/// <summary>
/// Collects what a frame has to redraw: the buffer rows that changed, or everything after a scroll, a color change or
/// a resize. <see cref="FrameRequested"/> is raised for every change, on the thread that made it, under the terminal lock.
/// </summary>
internal sealed class Renderer
{
    private int _dirtyTop = int.MaxValue;
    private int _dirtyBottom = int.MinValue;
    private bool _allDirty = true;
    private bool _selectionDirty;
    private bool _titleChanged;
    private bool _backgroundChanged;
    private TilPoint? _scrollDelta;

    /// <summary>Raised when something changed that the next frame should show.</summary>
    public event Action? FrameRequested;

    /// <summary>Raised when synchronized output (mode 2026) starts or ends.</summary>
    public event Action<bool>? SynchronizedOutputChanged;

    /// <summary>Raised with text that was just written, for accessibility announcements.</summary>
    public event Action<string>? NewText;

    public bool IsAllDirty => _allDirty;

    public bool IsSelectionDirty => _selectionDirty;

    public bool IsTitleChanged => _titleChanged;

    public bool IsBackgroundChanged => _backgroundChanged;

    /// <summary>How far the buffer content moved since the last frame, if it moved.</summary>
    public TilPoint? ScrollDelta => _scrollDelta;

    /// <summary>The changed buffer rows [top, bottom), or an empty range.</summary>
    public (int Top, int Bottom) DirtyRows => _dirtyTop <= _dirtyBottom ? (_dirtyTop, _dirtyBottom) : (0, 0);

    public void TriggerRedraw(Viewport region)
    {
        if (!region.IsValid)
        {
            return;
        }

        _dirtyTop = Math.Min(_dirtyTop, region.Top);
        _dirtyBottom = Math.Max(_dirtyBottom, region.BottomExclusive);
        NotifyPaintFrame();
    }

    public void TriggerRedraw(TilPoint cell) => TriggerRedraw(Viewport.FromDimensions(cell, new TilSize(1, 1)));

    public void TriggerRedrawAll(bool backgroundChanged = false, bool frameChanged = false)
    {
        _allDirty = true;
        _backgroundChanged |= backgroundChanged || frameChanged;
        NotifyPaintFrame();
    }

    public void TriggerSelection()
    {
        _selectionDirty = true;
        NotifyPaintFrame();
    }

    public void TriggerSearchHighlight() => TriggerSelection();

    public void TriggerScroll()
    {
        _allDirty = true;
        NotifyPaintFrame();
    }

    /// <summary>Reports that the buffer content moved by <paramref name="delta"/> rows (the circular buffer rotated).</summary>
    public void TriggerScroll(TilPoint delta)
    {
        _scrollDelta = (_scrollDelta ?? default) + delta;
        TriggerScroll();
    }

    public void TriggerTitleChange()
    {
        _titleChanged = true;
        NotifyPaintFrame();
    }

    public void TriggerNewTextNotification(ReadOnlySpan<char> newText)
    {
        var handler = NewText;
        if (handler != null)
        {
            handler(newText.ToString());
        }
    }

    public void NotifySynchronizedOutputChanged(bool isSynchronizing) => SynchronizedOutputChanged?.Invoke(isSynchronizing);

    public void NotifyPaintFrame() => FrameRequested?.Invoke();

    /// <summary>Forgets what was reported; the view calls it once it has invalidated what it read.</summary>
    public void Clear()
    {
        _dirtyTop = int.MaxValue;
        _dirtyBottom = int.MinValue;
        _allDirty = false;
        _selectionDirty = false;
        _titleChanged = false;
        _backgroundChanged = false;
        _scrollDelta = null;
    }
}
