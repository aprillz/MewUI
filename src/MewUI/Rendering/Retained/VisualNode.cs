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

    /// <summary>
    /// Surface-space extent of the visual and everything under it before the clips opened outside its frame
    /// cut it down. A frame move tests this, shifted, against the clip to tell what came into view.
    /// </summary>
    internal Rect SurfaceSubtreeReach { get; set; }

    /// <summary>
    /// True when the last visit found the clips around the visual hide all of it. It holds no place on the
    /// surface then, and has to be visited again when a frame move brings it back.
    /// </summary>
    internal bool ClippedAwayAtVisit { get; set; }

    /// <summary>The frame this node was staged in, or null before its first staging.</summary>
    internal SceneFrame? Frame { get; set; }

    /// <summary>The frame this node's visual roots, when the visual that composes it opened one for it.</summary>
    internal SceneFrame? RootedFrame { get; set; }

    /// <summary>Where <see cref="Frame"/> stood in window coordinates when this node was last staged.</summary>
    internal Point CapturedFrameLocalOrigin { get; set; }

    /// <summary>Where <see cref="Frame"/> stood in surface coordinates when this node was last staged.</summary>
    internal Point CapturedFrameSurfaceOrigin { get; set; }

    /// <summary>
    /// Where <see cref="Frame"/> stood in window coordinates when <see cref="Plan"/> was built. A node the
    /// frame carried along keeps its plan, whose clips and transforms stand where the visual stood then.
    /// </summary>
    internal Point PlanFrameLocalOrigin { get; set; }

    /// <summary>How far the frame has moved since the plan was built, which offsets every clip and transform in it.</summary>
    internal Vector PlanLocalShift => Frame == null ? default : Frame.LocalOrigin - PlanFrameLocalOrigin;

    /// <summary>True when a slot's recording cannot be replayed moved, so a move of the visual records it again.</summary>
    internal bool HasUnplaceableSlot
    {
        get
        {
            for (int slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
            {
                if (_slots[slotIndex] is RenderData data && !data.CanBePlaced)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// How far the frame has moved in window coordinates since this node was staged. The node's own geometry
    /// was not written again for the move, so every window-space value it holds is read offset by this.
    /// </summary>
    internal Vector LocalShift => Frame == null ? default : Frame.LocalOrigin - CapturedFrameLocalOrigin;

    /// <summary>The same move in surface coordinates, which offsets every surface-space value the node holds.</summary>
    internal Vector SurfaceShift => Frame == null ? default : Frame.SurfaceOrigin - CapturedFrameSurfaceOrigin;

    /// <summary>Where the visual and everything under it stand on the surface now, the frame move included.</summary>
    internal Rect ShiftedSurfaceSubtreeBounds => Shifted(SurfaceSubtreeBounds, SurfaceShift);

    /// <summary>Where the visual's own content stands on the surface now, the frame move included.</summary>
    internal Rect ShiftedSurfaceBounds => Shifted(SurfaceBounds, SurfaceShift);

    /// <summary>Where a slot's recording stands now, for a replay that moves it from where it was recorded.</summary>
    internal Point ShiftedPlacedOrigin => PlacedOrigin + LocalShift;

    internal static Rect Shifted(Rect rect, Vector shift)
        => shift.X == 0 && shift.Y == 0 ? rect : rect.Offset(shift);

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

    /// <summary>
    /// Forgets where the visual and its slots reached the surface, as a visit that found them clipped away
    /// would: a node without an extent is left out of every replay.
    /// </summary>
    internal void ClearSurfaceExtents()
    {
        SurfaceBounds = default;
        SurfaceSubtreeBounds = default;
        for (int slotIndex = 0; slotIndex < _slots.Length; slotIndex++)
        {
            if (_slots[slotIndex] is RenderData data)
            {
                data.SurfaceExtent = default;
            }
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
