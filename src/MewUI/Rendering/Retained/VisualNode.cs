using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Composition state of one visual: what applies to the whole visual rather than to a single
/// drawing command.
/// </summary>
internal readonly record struct CompositionState(
    bool IsVisible,
    double Opacity,
    bool OpaqueBackdrop);

/// <summary>
/// Scene node of one visual. Owns that visual's content slots and composition, and nothing of its
/// parent or its children.
/// </summary>
internal sealed class VisualNode : IDisposable
{
    private RenderData?[] _slots = [];

    internal VisualNode(UIElement element)
    {
        Element = element;
    }

    internal UIElement Element { get; }

    /// <summary>Bumped when the visual is detached and attached again, so stale data is not reused.</summary>
    internal int AttachmentGeneration { get; set; }

    /// <summary>Visual that composed this node in the last pass, or null for a surface root.</summary>
    internal UIElement? ParentElement { get; set; }

    /// <summary>Pass in which the scene last reached this node.</summary>
    internal int LastVisitedPass { get; set; } = -1;

    /// <summary>
    /// What the last update that looked into this node saw. While the visual still answers the same
    /// to all of it, nothing under the node can have changed, and an update passes over it.
    /// </summary>
    internal CaptureKey Captured { get; set; }

    /// <summary>Stamp of the last reachability sweep that found this node.</summary>
    internal int ReachMark { get; set; }

    /// <summary>Own-content version the recorded slots were taken from.</summary>
    internal int RecordedContentVersion { get; set; } = -1;

    /// <summary>Subtree repaint version the recorded slots were taken from.</summary>
    internal int RecordedSubtreeVersion { get; set; } = -1;

    /// <summary>Surface-space extent of this visual's own content, as the last pass drew it.</summary>
    internal Rect SurfaceBounds { get; set; }

    /// <summary>Surface-space extent of this visual and everything the scene draws under it.</summary>
    internal Rect SurfaceSubtreeBounds { get; set; }

    /// <summary>Where the visual stands in the committed scene; a slot is replayed moved from where it was recorded to here.</summary>
    internal Point PlacedOrigin { get; set; }

    internal CompositionState State { get; set; } = new(true, 1, false);

    internal CompositionPlan Plan { get; set; } = CompositionPlan.Empty;

    /// <summary>
    /// True when the recorded data already contains this visual's own composition state, which is
    /// the case for a compatibility subtree recorded through the element's own render path.
    /// </summary>
    internal bool StateBakedIntoContent { get; set; }

    internal string? NonRecordableReason { get; set; }

    /// <summary>Reason given for a slot that is left unrecorded because its visual changes every pass.</summary>
    internal const string CHANGES_EVERY_PASS = "the visual changes every pass";

    // A recording is worth taking once it gets replayed; a run this long says it will not be.
    private const int CHANGED_PASSES_BEFORE_DRAWN_LIVE = 30;

    // Changes further apart than this are not an animation, however many passes in a row they fill:
    // a blinking caret changes in every pass when nothing else does, half a second apart.
    private const int ANIMATION_GAP_MS = 50;

    private int _changedPassRun;
    private long _lastChangedTimestamp;

    /// <summary>Last pass that found this visual's content changed.</summary>
    internal int LastChangedPass { get; private set; } = -1;

    /// <summary>
    /// Notes that a pass found the content changed, and keeps the run of passes in a row. A visual drawn
    /// live answers for its own box alone, so a recording that inked past that box ends the run.
    /// </summary>
    internal void NoteContentChanged(int pass, long timestamp, bool inkStaysInsideTheVisual)
    {
        if (!inkStaysInsideTheVisual)
        {
            _changedPassRun = 0;
            LastChangedPass = pass;
        }
        else if (LastChangedPass != pass)
        {
            bool continuesTheRun = LastChangedPass == pass - 1 && WithinAnimationGap(timestamp);
            _changedPassRun = continuesTheRun ? _changedPassRun + 1 : 1;
            LastChangedPass = pass;
        }

        _lastChangedTimestamp = timestamp;
    }

    /// <summary>
    /// Whether the content has changed in every one of the passes leading up to <paramref name="pass"/>,
    /// each close enough to the one before to be frames of an animation, the last of them just now.
    /// </summary>
    internal bool ChangesEveryPass(int pass, long timestamp)
        => _changedPassRun >= CHANGED_PASSES_BEFORE_DRAWN_LIVE && LastChangedPass >= pass - 1 && WithinAnimationGap(timestamp);

    private bool WithinAnimationGap(long timestamp)
        => timestamp - _lastChangedTimestamp <= ANIMATION_GAP_MS * System.Diagnostics.Stopwatch.Frequency / 1000;

    internal bool HasContent
    {
        get
        {
            for (int slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                if (_slots[slotIndex] != null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Bytes the recorded slots of this visual hold, commands and their payload together.</summary>
    internal long EstimatedCommandBytes
    {
        get
        {
            long bytes = 0;
            for (int slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                bytes += _slots[slotIndex]?.EstimatedBytes ?? 0;
            }

            return bytes;
        }
    }

    internal RenderData? GetSlot(int slotIndex)
        => slotIndex >= 0 && slotIndex < _slots.Length ? _slots[slotIndex] : null;

    /// <summary>Replaces one slot's data as a whole and releases the data it held.</summary>
    internal void SetSlot(int slotIndex, RenderData? data)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);

        if (slotIndex >= _slots.Length)
        {
            Array.Resize(ref _slots, slotIndex + 1);
        }

        var previous = _slots[slotIndex];
        _slots[slotIndex] = data;
        if (!ReferenceEquals(previous, data))
        {
            previous?.Dispose();
        }
    }

    internal void ClearSlots()
    {
        for (int slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
        {
            _slots[slotIndex]?.Dispose();
            _slots[slotIndex] = null;
        }
    }

    public void Dispose()
    {
        ClearSlots();
        Plan = CompositionPlan.Empty;
    }
}
