using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Per-pass counters used by tests and diagnostics to tell recording apart from replay.
/// </summary>
internal sealed class RenderSceneStatistics
{
    internal int ContentRecordCount { get; set; }

    internal int ContentReplayCount { get; set; }

    internal int RejectedSlotCount { get; set; }

    /// <summary>Slots a replay had to draw through the live element because no data was recorded.</summary>
    internal int LiveFallbackCount { get; set; }

    /// <summary>Visuals an update looked into, as opposed to passing over because nothing under them changed.</summary>
    internal int CapturedNodeCount { get; set; }

    /// <summary>Surfaces a replay made to blend a faded group as a whole.</summary>
    internal int GroupSurfaceCount { get; set; }

    /// <summary>Faded groups a replay blended from the surface kept for them, without replaying what is under them.</summary>
    internal int GroupLayerReuseCount { get; set; }

    /// <summary>Backdrops a replay read from its target and drew filtered.</summary>
    internal int BackdropPassCount { get; set; }

    /// <summary>Backdrops a replay could not read or filter, drawn with their fallback brush or left out.</summary>
    internal int BackdropFallbackCount { get; set; }

    /// <summary>Pixels the backdrops of a replay read from their target.</summary>
    internal long BackdropInputPixelCount { get; set; }

    /// <summary>Frames an update found moved by whole pixels, so their subtrees were shifted instead of visited.</summary>
    internal int FrameMoveCount { get; set; }

    /// <summary>Visuals in view under a moved frame that an update carried along, staging their place without looking into them.</summary>
    internal int CarriedNodeCount { get; set; }

    internal void Reset()
    {
        ContentRecordCount = 0;
        ContentReplayCount = 0;
        RejectedSlotCount = 0;
        LiveFallbackCount = 0;
        CapturedNodeCount = 0;
        GroupSurfaceCount = 0;
        GroupLayerReuseCount = 0;
        BackdropPassCount = 0;
        BackdropFallbackCount = 0;
        BackdropInputPixelCount = 0;
        FrameMoveCount = 0;
        CarriedNodeCount = 0;
    }
}

/// <summary>
/// Retained scene of one surface. Owns the nodes of the visuals recorded for that surface and
/// nothing else; the surface and its device resources are held elsewhere.
/// </summary>
internal sealed class RenderScene : IDisposable
{
    private readonly Dictionary<UIElement, VisualNode> _nodes = new(ReferenceEqualityComparer.Instance);
    private BoundsAccumulator _dirtyBounds;
    private readonly DirtyRegion _dirtyRegion = new();

    internal UIElement? Root { get; set; }

    /// <summary>
    /// The visuals drawn over <see cref="Root"/> on the same surface, in the order they are drawn:
    /// adorners, popups shown inside the surface, overlays. They are roots of the same scene, so they
    /// take part in the same update and the same dirty region as the body.
    /// </summary>
    internal IReadOnlyList<UIElement> LayerRoots { get; set; } = [];

    internal RenderSceneStatistics Statistics { get; } = new();

    /// <summary>The frame every node stands in that no scroll content frames; its origin is zero and never moves.</summary>
    internal SceneFrame RootFrame { get; } = new(null);

    /// <summary>Makes the surfaces that faded groups are drawn into. Without one a group is faded call by call.</summary>
    internal IGraphicsFactory? GroupFactory { get; set; }

    /// <summary>The surfaces kept for faded groups between frames.</summary>
    internal GroupLayerCache GroupLayers { get; } = new();

    /// <summary>Nodes whose content is drawn live because it kept changing; looked over for ones that settled.</summary>
    internal List<VisualNode> DrawnLiveWhileChanging { get; } = [];

    internal int NodeCount => _nodes.Count;

    /// <summary>Identifies the pass being captured, so nodes it never reaches can be dropped.</summary>
    internal int PassId { get; private set; }

    /// <summary>Device the recorded data belongs to. Recorded resources do not survive a change.</summary>
    internal int DeviceGeneration { get; private set; }

    /// <summary>Surface-space area the last update changed, valid while <see cref="IsFullyDirty"/> is false.</summary>
    internal Rect DirtyBounds => _dirtyBounds.Result;

    /// <summary>The same dirty region as separate areas, which is what a frame repaints.</summary>
    internal DirtyRegion DirtyRegion => _dirtyRegion;

    /// <summary>True when the update could not bound what it changed, so the whole surface is stale.</summary>
    internal bool IsFullyDirty { get; private set; }

    internal bool HasDirtyRegion => IsFullyDirty || DirtyBounds.Width > 0;

    /// <summary>Records that an area of the surface no longer matches the scene.</summary>
    internal void AddDirtyRect(Rect bounds)
    {
        _dirtyBounds.Add(bounds);
        _dirtyRegion.Add(bounds);
    }

    internal void MarkFullyDirty() => IsFullyDirty = true;

    // Whole pixels cut a filter's reach off, so it is taken this much wider.
    private const double BACKDROP_REACH_MARGIN = 1;

    private readonly List<VisualNode> _backdropsInPaintOrder = [];
    private readonly List<List<Rect>> _backdropOutputs = [];
    private readonly List<Rect> _changesBeforeBackdrops = [];

    /// <summary>
    /// Closes the dirty region over the backdrops it touches, and decides for each backdrop whether it keeps
    /// what stands behind it. The kept frame holds a backdrop as it was drawn, not what stands behind it, so a
    /// backdrop that reads its box whole is drawn again whole once anything in its box is repainted. One that
    /// keeps its input takes as changed what changed under it, which is what the frame changed and what the
    /// backdrops drawn before it draw differently; what it draws itself then changes no further than its filter
    /// reaches from there, inside its box. Returns how many areas it added.
    /// </summary>
    internal int ExpandDirtyForBackdrops()
    {
        _backdropsInPaintOrder.Clear();
        if (Root != null && FindNode(Root) is VisualNode root)
        {
            CollectBackdrops(root, insideGroup: false);
        }

        for (int index = 0; index < LayerRoots.Count; index++)
        {
            if (FindNode(LayerRoots[index]) is VisualNode layer)
            {
                CollectBackdrops(layer, insideGroup: false);
            }
        }

        if (_backdropsInPaintOrder.Count == 0 || IsFullyDirty || DirtyBounds.Width <= 0 || DirtyBounds.Height <= 0)
        {
            return 0;
        }

        _changesBeforeBackdrops.Clear();
        _changesBeforeBackdrops.AddRange(_dirtyRegion.Areas);
        while (_backdropOutputs.Count < _backdropsInPaintOrder.Count)
        {
            _backdropOutputs.Add([]);
        }

        for (int index = 0; index < _backdropsInPaintOrder.Count; index++)
        {
            _backdropOutputs[index].Clear();
        }

        // A backdrop read whole answers to a change drawn after it too, so this runs until nothing more is added.
        bool grew = true;
        while (grew)
        {
            grew = false;
            for (int index = 0; index < _backdropsInPaintOrder.Count; index++)
            {
                grew |= ExpandForBackdrop(index);
            }
        }

        int added = 0;
        for (int index = 0; index < _backdropsInPaintOrder.Count; index++)
        {
            var outputs = _backdropOutputs[index];
            for (int outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
            {
                AddDirtyRect(outputs[outputIndex]);
                added++;
            }
        }

        return added;
    }

    /// <summary>Adds to what the backdrop at <paramref name="order"/> draws differently; true when it grew.</summary>
    private bool ExpandForBackdrop(int order)
    {
        var node = _backdropsInPaintOrder[order];
        var box = node.ShiftedBackdropBounds;
        var outputs = _backdropOutputs[order];
        var cache = node.BackdropCache;
        if (!node.BackdropRetained || cache == null || cache.NeedsWholeInput || cache.Bounds != box ||
            ReachOf(node.State) is not double reach)
        {
            if (Covers(outputs, box) || !AnyChangeTouches(box, order))
            {
                return false;
            }

            outputs.Add(box);
            return true;
        }

        bool grew = GrowKept(outputs, _changesBeforeBackdrops, box, reach);
        for (int earlier = 0; earlier < order; earlier++)
        {
            grew |= GrowKept(outputs, _backdropOutputs[earlier], box, reach);
        }

        return grew;
    }

    /// <summary>Adds what a kept backdrop draws differently for each of <paramref name="changes"/> under its box.</summary>
    private static bool GrowKept(List<Rect> outputs, List<Rect> changes, Rect box, double reach)
    {
        bool grew = false;
        for (int index = 0; index < changes.Count; index++)
        {
            var changed = changes[index].Intersect(box);
            if (changed.Width <= 0 || changed.Height <= 0)
            {
                continue;
            }

            var reached = changed.Inflate(reach, reach).Intersect(box);
            if (!Covers(outputs, reached))
            {
                outputs.Add(reached);
                grew = true;
            }
        }

        return grew;
    }

    /// <summary>Whether anything the frame repaints, other than what the backdrop at <paramref name="order"/> adds itself, reaches <paramref name="box"/>.</summary>
    private bool AnyChangeTouches(Rect box, int order)
    {
        for (int index = 0; index < _changesBeforeBackdrops.Count; index++)
        {
            if (_changesBeforeBackdrops[index].IntersectsWith(box))
            {
                return true;
            }
        }

        for (int other = 0; other < _backdropsInPaintOrder.Count; other++)
        {
            if (other == order)
            {
                continue;
            }

            var outputs = _backdropOutputs[other];
            for (int index = 0; index < outputs.Count; index++)
            {
                if (outputs[index].IntersectsWith(box))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool Covers(List<Rect> areas, Rect rect)
    {
        for (int index = 0; index < areas.Count; index++)
        {
            var area = areas[index];
            if (area.X <= rect.X && area.Y <= rect.Y && area.Right >= rect.Right && area.Bottom >= rect.Bottom)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// How far a change under a backdrop reaches in what its filter draws, or null when the filter's reach
    /// cannot be bounded and the backdrop reads its box whole.
    /// </summary>
    private static double? ReachOf(in CompositionState state)
    {
        var unit = new Rect(0, 0, 1, 1);
        if (Filters.ImageFilterBounds.TryGetSourceRect(state.BackdropFilter, unit) is not Rect source || source.IsEmpty)
        {
            return null;
        }

        double reach = Math.Max(Math.Max(-source.X, -source.Y), Math.Max(source.Right - 1, source.Bottom - 1));
        return Math.Max(0, reach) + BACKDROP_REACH_MARGIN;
    }

    /// <summary>Lists the backdrops under <paramref name="node"/> in paint order and returns whether it or anything under it has one.</summary>
    private bool CollectBackdrops(VisualNode node, bool insideGroup)
    {
        node.HasBackdropBelow = false;
        if (!node.State.IsVisible)
        {
            return false;
        }

        var box = node.ShiftedBackdropBounds;
        if (node.State.HasBackdrop && box.Width > 0 && box.Height > 0)
        {
            // A visual inside a faded group reads the group's own target, which is made again every frame, and a
            // visual drawn through its own render path reads its box whole there: neither keeps what it read.
            node.BackdropRetained = !insideGroup && !node.StateBakedIntoContent;
            _backdropsInPaintOrder.Add(node);
        }

        bool hasOwn = node.State.HasBackdrop;
        if (node.StateBakedIntoContent)
        {
            // Its own render path draws everything under it, into a target of its own where a cache is.
            return hasOwn;
        }

        bool childrenInsideGroup = insideGroup || node.State.Opacity < 1;
        bool below = false;
        var entries = node.Plan.Entries;
        for (int index = 0; index < entries.Length; index++)
        {
            if (entries[index].Kind == CompositionEntryKind.Child && entries[index].Child is UIElement child &&
                FindNode(child) is VisualNode childNode)
            {
                below |= CollectBackdrops(childNode, childrenInsideGroup);
            }
        }

        node.HasBackdropBelow = below;
        return hasOwn || below;
    }

    internal void ResetDirtyRegion()
    {
        _dirtyBounds = default;
        _dirtyRegion.Clear();
        IsFullyDirty = false;
    }

    /// <summary>The time passes are taken at, in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    internal Func<long> Clock { get; set; } = System.Diagnostics.Stopwatch.GetTimestamp;

    /// <summary>When the current pass was opened, on <see cref="Clock"/>.</summary>
    internal long PassTimestamp { get; private set; }

    /// <summary>Opens the pass an update lands in and returns its id.</summary>
    internal int BeginPass()
    {
        PassTimestamp = Clock();
        _passOpenedThisFrame = true;
        return ++PassId;
    }

    // Changes further apart than this are not an animation, however many passes in a row they fill:
    // a blinking caret changes in every pass when nothing else does, half a second apart.
    private const int ANIMATION_GAP_MS = 50;

    // A frame that takes long to draw spaces the passes of an animation out by its own length.
    private const int ANIMATION_GAP_FRAME_MULTIPLE = 3;

    private bool _passOpenedThisFrame;
    private long _lastFrameWorkTicks;
    // When the frame's update began to capture, which is before its pass opens: recording is part of the frame.
    private bool _frameWorkStarted;
    private long _frameWorkStart;

    /// <summary>
    /// Longest time between two passes that still counts them as frames of one animation: a fixed floor,
    /// or a few times what the last frame took to draw when that is longer.
    /// </summary>
    internal long AnimationGapTicks
        => Math.Max(ANIMATION_GAP_MS * System.Diagnostics.Stopwatch.Frequency / 1000, _lastFrameWorkTicks * ANIMATION_GAP_FRAME_MULTIPLE);

    /// <summary>Notes that the current frame's update has begun, before it records anything.</summary>
    internal void NoteFrameWorkStarted()
    {
        if (!_frameWorkStarted)
        {
            _frameWorkStarted = true;
            _frameWorkStart = Clock();
        }
    }

    /// <summary>Notes that the frame the current pass was drawn in is over.</summary>
    internal void NoteFrameEnded()
    {
        if (_passOpenedThisFrame)
        {
            _passOpenedThisFrame = false;
            _lastFrameWorkTicks = Math.Max(0, Clock() - (_frameWorkStarted ? _frameWorkStart : PassTimestamp));
        }

        _frameWorkStarted = false;
        GroupLayers.EndFrame();
    }

    /// <summary>Takes a node an update created once that update lands.</summary>
    internal void AddNode(VisualNode node) => _nodes.Add(node.Element, node);

    /// <summary>
    /// Drops the recorded content when the device it was recorded against is gone. The nodes and
    /// their composition stay, so the next pass re-records instead of rebuilding the scene.
    /// </summary>
    internal void SetDeviceGeneration(int generation)
    {
        if (generation == DeviceGeneration)
        {
            return;
        }

        DeviceGeneration = generation;
        MarkFullyDirty();
        GroupLayers.Clear();
        foreach (var node in _nodes.Values)
        {
            node.ClearSlots();
            node.RecordedContentVersion = -1;
            node.RecordedSubtreeVersion = -1;
            node.Captured = default;
            node.ReleaseBackdropCache();
        }
    }

    private int _reachMark;

    private void MarkReachable(UIElement element)
    {
        var node = FindNode(element);
        if (node == null || node.ReachMark == _reachMark)
        {
            return;
        }

        node.ReachMark = _reachMark;
        var entries = node.Plan.Entries;
        for (int index = 0; index < entries.Length; index++)
        {
            if (entries[index].Kind == CompositionEntryKind.Child && entries[index].Child is UIElement child)
            {
                MarkReachable(child);
            }
        }
    }

    /// <summary>
    /// Drops the nodes no root reaches any more, releasing what they held. An update passes over
    /// subtrees in which nothing changed, so which nodes it reached says nothing about which are still
    /// in the scene; what the plans compose does.
    /// </summary>
    internal void PruneUnreachable()
    {
        _reachMark++;
        if (Root != null)
        {
            MarkReachable(Root);
        }

        for (int index = 0; index < LayerRoots.Count; index++)
        {
            MarkReachable(LayerRoots[index]);
        }

        List<UIElement>? removed = null;
        foreach (var entry in _nodes)
        {
            if (entry.Value.ReachMark != _reachMark)
            {
                removed ??= [];
                removed.Add(entry.Key);
            }
        }

        if (removed == null)
        {
            return;
        }

        for (int index = 0; index < removed.Count; index++)
        {
            var node = FindNode(removed[index]);
            if (node != null)
            {
                AddDirtyRect(node.ShiftedSurfaceSubtreeBounds);
            }

            RemoveNode(removed[index]);
        }
    }

    /// <summary>
    /// Bytes the recorded commands of this scene occupy, for the cost comparisons the storage
    /// decisions rest on. Counts the command records, their value buffers and their resource
    /// tables, not the resources those tables point at.
    /// </summary>
    internal long EstimatedCommandBytes
    {
        get
        {
            long bytes = 0;
            foreach (var node in _nodes.Values)
            {
                bytes += node.EstimatedCommandBytes;
            }

            return bytes;
        }
    }

    internal VisualNode GetOrCreateNode(UIElement element)
    {
        if (_nodes.TryGetValue(element, out var node))
        {
            return node;
        }

        node = new VisualNode(element);
        _nodes.Add(element, node);
        return node;
    }

    internal VisualNode? FindNode(UIElement element)
        => _nodes.TryGetValue(element, out var node) ? node : null;

    internal void RemoveNode(UIElement element)
    {
        if (_nodes.Remove(element, out var node))
        {
            node.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var node in _nodes.Values)
        {
            node.Dispose();
        }

        _nodes.Clear();
        Root = null;
        GroupLayers.Dispose();
    }
}
