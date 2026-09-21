using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// One scene update as a transaction: new content and composition are staged, validated, and then
/// applied together. A failed update releases only what it staged and leaves the previous scene
/// exactly as it was.
/// </summary>
internal sealed class SceneUpdate
{
    private readonly List<PendingSlot> _slots = [];
    private readonly List<PendingComposition> _compositions = [];
    private readonly List<PendingBounds> _bounds = [];
    private readonly List<PendingExtent> _extents = [];
    private readonly List<CompositionEntry> _removedEntries = [];
    private readonly Dictionary<UIElement, VisualNode> _createdNodes = new(ReferenceEqualityComparer.Instance);
    private readonly List<PendingVisit> _visits = [];
    private readonly List<PendingConsumption> _consumptions = [];
    private UIElement? _root;
    private UIElement[]? _layerRoots;
    private RenderScene _scene;

    internal SceneUpdate(RenderScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
    }

    /// <summary>Readies this instance for another pass, keeping the buffers it already grew.</summary>
    internal void Begin(RenderScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Discard();
        _scene = scene;
    }

    /// <summary>The root the scene takes when this update lands.</summary>
    internal void StageRoot(UIElement root) => _root = root;

    /// <summary>The layer roots the scene takes, in drawing order, when this update lands.</summary>
    internal void StageLayerRoots(IReadOnlyList<UIElement> layerRoots)
    {
        _layerRoots = new UIElement[layerRoots.Count];
        for (int index = 0; index < layerRoots.Count; index++)
        {
            _layerRoots[index] = layerRoots[index];
        }
    }

    /// <summary>
    /// The node of a visual, created for this update when the scene has none. A created node is not in
    /// the scene until the update lands, so a failed update leaves no trace of it.
    /// </summary>
    internal VisualNode GetOrCreateNode(UIElement element)
    {
        var existing = _scene.FindNode(element);
        if (existing != null)
        {
            return existing;
        }

        if (!_createdNodes.TryGetValue(element, out var created))
        {
            created = new VisualNode(element);
            _createdNodes.Add(element, created);
        }

        return created;
    }

    internal VisualNode? FindNode(UIElement element)
        => _scene.FindNode(element) ?? (_createdNodes.TryGetValue(element, out var created) ? created : null);

    /// <summary>Notes that the pass reached the node under this parent; applied when the update lands.</summary>
    internal void StageVisit(VisualNode node, UIElement? parent, bool attachmentChanged)
        => _visits.Add(new PendingVisit(node, parent, attachmentChanged));

    /// <summary>Notes what the pass served from the dirty queue; taken off the queue when the update lands.</summary>
    internal void StageConsumption(UIElement element, RenderDirtyKind kind)
        => _consumptions.Add(new PendingConsumption(element, kind));

    internal int StagedSlotCount => _slots.Count;

    internal int StagedCompositionCount => _compositions.Count;

    internal void StageSlot(
        VisualNode node,
        int slotIndex,
        RenderData? data,
        string? rejectionReason,
        int contentVersion,
        int subtreeVersion)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        _slots.Add(new PendingSlot(node, slotIndex, data, rejectionReason, contentVersion, subtreeVersion));
    }

    internal void StageComposition(
        VisualNode node,
        CompositionPlan plan,
        CompositionState state,
        bool stateBakedIntoContent)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(plan);
        _compositions.Add(new PendingComposition(node, plan, state, stateBakedIntoContent));
    }

    internal void StageBounds(
        VisualNode node,
        Rect surfaceBounds,
        Rect surfaceSubtreeBounds,
        Point placedOrigin,
        bool clippedAway = false,
        CaptureKey captured = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        _bounds.Add(new PendingBounds(node, surfaceBounds, surfaceSubtreeBounds, placedOrigin, clippedAway, captured));
    }

    /// <summary>
    /// Where a slot reaches the surface in this pass, whether it was recorded again or only stands in a
    /// new place. A recording that changed is repainted there and where it reached before, which can be
    /// far less than the box of a visual with more than one drawing of its own.
    /// </summary>
    /// <summary>
    /// Stages where a slot reaches on the surface. <paramref name="staysInsideTheVisual"/> says whether
    /// all of that lies within the visual's own box, which is what a slot drawn live answers for.
    /// </summary>
    internal void StageSlotExtent(VisualNode node, int slotIndex, Rect extent, bool staysInsideTheVisual)
    {
        _extents.Add(new PendingExtent(node, slotIndex, extent));
        int last = _slots.Count - 1;
        if (last >= 0 && ReferenceEquals(_slots[last].Node, node) && _slots[last].SlotIndex == slotIndex)
        {
            _slots[last] = _slots[last] with { Extent = extent, StaysInsideTheVisual = staysInsideTheVisual };
        }
    }

    /// <summary>
    /// Narrows the damage of the slot staged last to where its new recording draws differently from the
    /// one it replaces. Without it the slot answers for everything either recording reaches.
    /// </summary>
    internal void StageSlotChange(VisualNode node, int slotIndex, Rect change)
    {
        int last = _slots.Count - 1;
        if (last >= 0 && ReferenceEquals(_slots[last].Node, node) && _slots[last].SlotIndex == slotIndex)
        {
            _slots[last] = _slots[last] with { Change = change };
        }
    }

    /// <summary>Finds the data this pass staged for a slot, which is not in the node yet.</summary>
    internal bool TryGetStagedSlot(VisualNode node, int slotIndex, out RenderData? data)
    {
        for (int index = _slots.Count - 1; index >= 0; index--)
        {
            var slot = _slots[index];
            if (ReferenceEquals(slot.Node, node) && slot.SlotIndex == slotIndex)
            {
                data = slot.Data;
                return true;
            }
        }

        data = null;
        return false;
    }

    /// <summary>
    /// Checks that the staged result is drawable on its own: every child it names has a node, and no
    /// slot was staged twice.
    /// </summary>
    internal bool TryValidate(out string? failure)
    {
        for (int compositionIndex = 0; compositionIndex < _compositions.Count; compositionIndex++)
        {
            var composition = _compositions[compositionIndex];
            var entries = composition.Plan.Entries;
            for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
            {
                var entry = entries[entryIndex];
                if (entry.Kind == CompositionEntryKind.Child && FindNode(entry.Child!) == null)
                {
                    failure = $"{composition.Node.Element.GetType().Name} names a child that has no scene node";
                    return false;
                }
            }
        }

        for (int slotIndex = 0; slotIndex < _slots.Count; slotIndex++)
        {
            var slot = _slots[slotIndex];
            for (int otherIndex = slotIndex + 1; otherIndex < _slots.Count; otherIndex++)
            {
                var other = _slots[otherIndex];
                if (ReferenceEquals(slot.Node, other.Node) && slot.SlotIndex == other.SlotIndex)
                {
                    failure = $"{slot.Node.Element.GetType().Name} staged slot {slot.SlotIndex} twice";
                    return false;
                }
            }
        }

        failure = null;
        return true;
    }

    /// <summary>True when the last commit changed which visuals the scene holds or who composes them.</summary>
    internal bool StructureChanged { get; private set; }

    /// <summary>Applies everything staged. Nothing before this call has touched the scene.</summary>
    internal void Commit(RenderDirtyRegistry? registry)
    {
        int passId = _scene.BeginPass();

        // What the scene holds can only change where a root, a parent or a plan did; an update that
        // changed none of them has nothing to prune.
        StructureChanged = _createdNodes.Count > 0;
        if (_root != null)
        {
            StructureChanged |= !ReferenceEquals(_scene.Root, _root);
            _scene.Root = _root;
        }

        int firstReorderedLayer = CommitLayerRoots();
        StructureChanged |= firstReorderedLayer >= 0;

        foreach (var created in _createdNodes.Values)
        {
            _scene.AddNode(created);
        }

        for (int visitIndex = 0; visitIndex < _visits.Count; visitIndex++)
        {
            var visit = _visits[visitIndex];
            visit.Node.LastVisitedPass = passId;
            if (visit.AttachmentChanged)
            {
                // The recorded coordinates belong to the previous parent, so nothing of them survives.
                _scene.AddDamage(visit.Node.SurfaceSubtreeBounds);
                visit.Node.AttachmentGeneration++;
                StructureChanged = true;
                visit.Node.ClearSlots();
            }

            visit.Node.ParentElement = visit.Parent;
        }

        if (registry != null)
        {
            for (int consumptionIndex = 0; consumptionIndex < _consumptions.Count; consumptionIndex++)
            {
                registry.Consume(_consumptions[consumptionIndex].Element, _consumptions[consumptionIndex].Kind);
            }
        }

        for (int compositionIndex = 0; compositionIndex < _compositions.Count; compositionIndex++)
        {
            var composition = _compositions[compositionIndex];
            bool planChanged = !ReferenceEquals(composition.Node.Plan, composition.Plan);
            if (composition.Node.State != composition.State ||
                (planChanged && !PlanChange.TryFindRemoved(composition.Node.Plan, composition.Plan, _removedEntries)))
            {
                // What the visual looked like still occupies the surface, so both extents are stale.
                _scene.AddDamage(composition.Node.SurfaceSubtreeBounds);
            }
            else if (planChanged)
            {
                // Children and drawings were only put in or taken out. What was put in repaints itself
                // as it is recorded; what was taken out is repainted from here.
                for (int removedIndex = 0; removedIndex < _removedEntries.Count; removedIndex++)
                {
                    var removedEntry = _removedEntries[removedIndex];
                    if (removedEntry.Kind == CompositionEntryKind.Child)
                    {
                        var removedNode = _scene.FindNode(removedEntry.Child!);
                        _scene.AddDamage(removedNode?.SurfaceSubtreeBounds ?? composition.Node.SurfaceSubtreeBounds);
                    }
                    else
                    {
                        var removedData = composition.Node.GetSlot(removedEntry.SlotIndex);
                        _scene.AddDamage(removedData?.SurfaceExtent ?? composition.Node.SurfaceBounds);
                    }
                }
            }

            StructureChanged |= !ReferenceEquals(composition.Node.Plan, composition.Plan);
            composition.Node.Plan = composition.Plan;
            composition.Node.State = composition.State;
            composition.Node.StateBakedIntoContent = composition.StateBakedIntoContent;
        }

        for (int slotIndex = 0; slotIndex < _slots.Count; slotIndex++)
        {
            var slot = _slots[slotIndex];
            var previous = slot.Node.GetSlot(slot.SlotIndex);
            if (previous != null && slot.Data != null && slot.RejectionReason == null && previous.DrawsTheSameAs(slot.Data))
            {
                // Taken again and found identical: the surface already shows it, so nothing is damaged
                // and the recording the node holds stays.
                slot.Data.Dispose();
                slot.Node.RecordedContentVersion = slot.ContentVersion;
                slot.Node.RecordedSubtreeVersion = slot.SubtreeVersion;
                continue;
            }

            slot.Node.NoteContentChanged(passId, slot.Data == null || slot.StaysInsideTheVisual);

            bool wasDrawnLive = previous == null && slot.Node.NonRecordableReason != null;
            bool isDrawnLive = slot.Data == null;
            if (slot.RejectionReason == VisualNode.CHANGES_EVERY_PASS && slot.Node.NonRecordableReason != VisualNode.CHANGES_EVERY_PASS)
            {
                _scene.DrawnLiveWhileChanging.Add(slot.Node);
            }

            if (wasDrawnLive || isDrawnLive)
            {
                // A slot without a recording is drawn live across the visual, before or after this pass.
                _scene.AddDamage(slot.Node.SurfaceBounds);
                _scene.AddDamage(slot.Extent);
                _scene.AddDamage(previous?.SurfaceExtent ?? default);
            }
            else if (previous != null && slot.Data != null && slot.Change is Rect change)
            {
                // Only some of the drawing calls differ, and the rest already stands on the surface.
                // The margin around them never reaches past what the two recordings answer for as a whole.
                _scene.AddDamage(change.Intersect(previous.SurfaceExtent.Union(slot.Extent)));
                slot.Data.SurfaceExtent = previous.SurfaceExtent;
            }
            else
            {
                // A slot the plan did not have before reached nowhere, which an empty extent says.
                _scene.AddDamage(previous?.SurfaceExtent ?? default);
                _scene.AddDamage(slot.Extent);
            }

            slot.Node.SetSlot(slot.SlotIndex, slot.Data);
            slot.Node.NonRecordableReason = slot.RejectionReason;
            slot.Node.RecordedContentVersion = slot.ContentVersion;
            slot.Node.RecordedSubtreeVersion = slot.SubtreeVersion;
        }

        for (int boundsIndex = 0; boundsIndex < _bounds.Count; boundsIndex++)
        {
            var bounds = _bounds[boundsIndex];
            // Each recording answers for where it was and is (below). Only a visual drawn live has no
            // recording to do that, so its box does.
            if (bounds.Node.NonRecordableReason != null && bounds.Node.SurfaceBounds != bounds.SurfaceBounds)
            {
                _scene.AddDamage(bounds.Node.SurfaceBounds);
                _scene.AddDamage(bounds.SurfaceBounds);
            }

            bounds.Node.SurfaceBounds = bounds.SurfaceBounds;
            bounds.Node.SurfaceSubtreeBounds = bounds.SurfaceSubtreeBounds;
            bounds.Node.PlacedOrigin = bounds.PlacedOrigin;
            bounds.Node.Element.NoteReachesSurface(!bounds.ClippedAway);
            bounds.Node.Captured = bounds.Captured;
        }

        for (int extentIndex = 0; extentIndex < _extents.Count; extentIndex++)
        {
            var extent = _extents[extentIndex];
            var data = extent.Node.GetSlot(extent.SlotIndex);
            if (data != null)
            {
                // A recording that stands somewhere else now, recorded again or not, leaves where it was
                // and covers where it is.
                if (data.SurfaceExtent != extent.Extent)
                {
                    _scene.AddDamage(data.SurfaceExtent);
                    _scene.AddDamage(extent.Extent);
                }

                data.SurfaceExtent = extent.Extent;
            }
        }

        DamageLayersFrom(firstReorderedLayer);

        _slots.Clear();
        _compositions.Clear();
        _bounds.Clear();
        _extents.Clear();
        _createdNodes.Clear();
        _visits.Clear();
        _consumptions.Clear();
        _root = null;
        _layerRoots = null;
    }

    /// <summary>
    /// Takes the staged layer order and returns the first position whose root changed, or -1. A layer
    /// that kept its visual and its bounds but changed its place among the others still changes what the
    /// surface shows wherever layers overlap, so everything from that position up is damaged: what stood
    /// there before here, and what stands there now once the new bounds are in.
    /// </summary>
    private int CommitLayerRoots()
    {
        if (_layerRoots == null)
        {
            return -1;
        }

        var previous = _scene.LayerRoots;
        int first = 0;
        while (first < previous.Count && first < _layerRoots.Length && ReferenceEquals(previous[first], _layerRoots[first]))
        {
            first++;
        }

        if (first == previous.Count && first == _layerRoots.Length)
        {
            return -1;
        }

        for (int index = first; index < previous.Count; index++)
        {
            var node = _scene.FindNode(previous[index]);
            if (node != null)
            {
                _scene.AddDamage(node.SurfaceSubtreeBounds);
            }
        }

        _scene.LayerRoots = _layerRoots;
        return first;
    }

    private void DamageLayersFrom(int firstReorderedLayer)
    {
        if (firstReorderedLayer < 0)
        {
            return;
        }

        var roots = _scene.LayerRoots;
        for (int index = firstReorderedLayer; index < roots.Count; index++)
        {
            var node = _scene.FindNode(roots[index]);
            if (node != null)
            {
                _scene.AddDamage(node.SurfaceSubtreeBounds);
            }
        }
    }

    internal void Discard()
    {
        for (int slotIndex = 0; slotIndex < _slots.Count; slotIndex++)
        {
            _slots[slotIndex].Data?.Dispose();
        }

        _slots.Clear();
        _compositions.Clear();
        _bounds.Clear();
        _extents.Clear();

        foreach (var created in _createdNodes.Values)
        {
            created.Dispose();
        }

        _createdNodes.Clear();
        _visits.Clear();
        _consumptions.Clear();
        _root = null;
        _layerRoots = null;
    }

    private readonly record struct PendingVisit(VisualNode Node, UIElement? Parent, bool AttachmentChanged);

    private readonly record struct PendingConsumption(UIElement Element, RenderDirtyKind Kind);

    private readonly record struct PendingBounds(
        VisualNode Node,
        Rect SurfaceBounds,
        Rect SurfaceSubtreeBounds,
        Point PlacedOrigin,
        bool ClippedAway,
        CaptureKey Captured);

    private readonly record struct PendingSlot(
        VisualNode Node,
        int SlotIndex,
        RenderData? Data,
        string? RejectionReason,
        int ContentVersion,
        int SubtreeVersion,
        Rect Extent = default,
        Rect? Change = null,
        bool StaysInsideTheVisual = false);

    private readonly record struct PendingExtent(VisualNode Node, int SlotIndex, Rect Extent);

    private readonly record struct PendingComposition(
        VisualNode Node,
        CompositionPlan Plan,
        CompositionState State,
        bool StateBakedIntoContent);
}
