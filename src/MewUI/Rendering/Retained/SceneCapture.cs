using System.Numerics;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Builds or refreshes a <see cref="RenderScene"/> while the frame paints: a visual whose content
/// is dirty draws through the recorder, and a visual whose content is still valid replays the data
/// it already owns, so an unchanged visual is never asked to render again. The result is staged and
/// applied as one update, leaving the previous scene intact if the pass fails.
/// </summary>
internal sealed class SceneCapture
{
    private const int OWN_CONTENT_SLOT = 0;

    // Layout rounding leaves device-pixel positions this close to whole numbers.
    private const double PLACEMENT_EPSILON = 0.001;

    private readonly CompositionPlanBuilder _planBuilder = new();
    private readonly RenderDataBuilder _slotBuilder = new();

    // The clip of the composition scopes the visit currently stands inside, in surface space.
    private Rect? _ambientClip;
    private SceneUpdate? _update;

    /// <summary>
    /// Updates <paramref name="scene"/> from <paramref name="root"/> and paints the result into
    /// <paramref name="context"/>. A null dirty set records every visual.
    /// </summary>
    internal void Capture(
        RenderScene scene,
        UIElement root,
        IGraphicsContext context,
        IReadOnlySet<UIElement>? dirtyContent = null)
        => Capture(scene, root, [], context, new DirtyView(dirtyContent, null));

    /// <summary>Updates <paramref name="scene"/> from the invalidations queued in the registry.</summary>
    internal void Capture(
        RenderScene scene,
        UIElement root,
        IGraphicsContext context,
        RenderDirtyRegistry registry)
        => Capture(scene, root, [], context, registry);

    /// <summary>
    /// Updates <paramref name="scene"/> for a whole surface: the body root and the layer roots drawn over
    /// it, in order, as one update.
    /// </summary>
    internal void Capture(
        RenderScene scene,
        UIElement root,
        IReadOnlyList<UIElement> layerRoots,
        IGraphicsContext context,
        RenderDirtyRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(layerRoots);

        registry.BeginPass();
        try
        {
            Capture(scene, root, layerRoots, context, new DirtyView(null, registry));
        }
        finally
        {
            registry.EndPass();
        }
    }

    private void Capture(
        RenderScene scene,
        UIElement root,
        IReadOnlyList<UIElement> layerRoots,
        IGraphicsContext context,
        DirtyView dirty)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(context);

        var recorder = context as RenderDataRecorder ?? new RenderDataRecorder(context);
        var update = _update ??= new SceneUpdate(scene);
        update.Begin(scene);
        update.StageRoot(root);
        _ambientClip = null;
        update.StageLayerRoots(layerRoots);

        try
        {
            CaptureVisual(scene, update, root, recorder, dirty, parent: null, context.GetTransform());
            for (int layerIndex = 0; layerIndex < layerRoots.Count; layerIndex++)
            {
                CaptureVisual(scene, update, layerRoots[layerIndex], recorder, dirty, parent: null, context.GetTransform());
            }
        }
        catch
        {
            update.Discard();
            throw;
        }

        if (!update.TryValidate(out string? failure))
        {
            update.Discard();
            throw new InvalidOperationException($"The scene update was rejected: {failure}.");
        }

        update.Commit(dirty.Registry);
        scene.PruneUnvisited();
        dirty.Registry?.RemoveWhere(element => scene.FindNode(element) == null);
    }

    /// <summary>Records or replays one visual and returns its surface-space subtree extent.</summary>
    private Rect CaptureVisual(
        RenderScene scene,
        SceneUpdate update,
        UIElement element,
        RenderDataRecorder recorder,
        DirtyView dirty,
        UIElement? parent,
        Matrix3x2 transform)
    {
        // Nothing here touches the scene: the node may exist only in the update, and what the visit
        // changes about it is applied when the update lands.
        var node = update.GetOrCreateNode(element);
        bool attachmentChanged = node.ParentElement != null && !ReferenceEquals(node.ParentElement, parent);
        update.StageVisit(node, parent, attachmentChanged);

        double opacity = element.Opacity;
        bool isVisible = element.IsVisible && opacity > 0;
        var state = new CompositionState(isVisible, opacity, element is Control { Background.A: 255 });

        if (!isVisible)
        {
            update.StageComposition(node, CompositionPlan.Empty, state, stateBakedIntoContent: false);
            update.StageBounds(node, default, default, Origin(element));
            return default;
        }

        // A cached element serves its pixels from its own bitmap inside its render path, so the scene
        // records that path as a whole instead of splitting it into slots.
        if (!element.SupportsDeclaredComposition || element.HasBitmapCache)
        {
            return CaptureCompatibilitySubtree(
                scene, update, element, node, recorder, dirty, attachmentChanged, transform);
        }

        var plan = BuildPlan(update, element, node, attachmentChanged);
        if (plan.IsUnsupported)
        {
            return CaptureCompatibilitySubtree(
                scene, update, element, node, recorder, dirty, attachmentChanged, transform);
        }

        update.StageComposition(node, plan, state, stateBakedIntoContent: false);

        // A pass that only brings the scene up to date draws nothing, so it opens no scope on the
        // surface either: a backend may realize a scope as a layer that it blends back when the scope
        // closes, and blending back what the surface already holds changes every translucent pixel.
        var context = recorder.Inner;
        bool opacityScope = opacity < 1 && recorder.Draws;
        if (opacityScope)
        {
            context.BeginOpacity(opacity);
        }

        bool backdropScope = state.OpaqueBackdrop && recorder.Draws;
        if (backdropScope)
        {
            context.BeginOpaqueBackdrop();
        }

        var ownBounds = default(BoundsAccumulator);
        var subtreeBounds = default(BoundsAccumulator);

        try
        {
            CaptureEntries(
                scene,
                update,
                node,
                plan,
                recorder,
                dirty,
                attachmentChanged,
                0,
                plan.Entries.Length,
                transform,
                ref ownBounds,
                ref subtreeBounds);
        }
        finally
        {
            if (backdropScope)
            {
                context.EndOpaqueBackdrop();
            }

            if (opacityScope)
            {
                context.EndOpacity();
            }
        }

        subtreeBounds.Add(ownBounds.Result);
        update.StageBounds(
            node, ownBounds.Result, subtreeBounds.Result, Origin(element), IsClippedAway(element, transform, subtreeBounds.Result));
        return subtreeBounds.Result;
    }

    /// <summary>
    /// Asks the visual for its composition every pass. A declaration that has not changed returns the
    /// plan the node already holds, so nothing is allocated and no damage is reported; that is what
    /// lets a visual whose children or geometry did change be noticed without a separate signal.
    /// </summary>
    private CompositionPlan BuildPlan(SceneUpdate update, UIElement element, VisualNode node, bool attachmentChanged)
    {
        element.WriteComposition(_planBuilder);
        var plan = _planBuilder.Build(attachmentChanged ? null : node.Plan);
        update.StageConsumption(element, RenderDirtyKind.Composition | RenderDirtyKind.State | RenderDirtyKind.Placement);
        return plan;
    }

    private Rect CaptureCompatibilitySubtree(
        RenderScene scene,
        SceneUpdate update,
        UIElement element,
        VisualNode node,
        RenderDataRecorder recorder,
        DirtyView dirty,
        bool attachmentChanged,
        Matrix3x2 transform)
    {
        // The element's own render path applies its opacity and backdrop, so the data carries them.
        double opacity = element.Opacity;
        var state = new CompositionState(true, opacity, element is Control { Background.A: 255 });
        update.StageComposition(node, CompositionPlan.Empty, state, stateBakedIntoContent: true);

        // One recording carries the whole subtree, so it answers for every kind of change in it.
        update.StageConsumption(
            element,
            RenderDirtyKind.Composition | RenderDirtyKind.State | RenderDirtyKind.Placement);

        if (NeedsCompatibilityRecording(node, element, dirty, attachmentChanged))
        {
            RecordSlot(scene, update, node, OWN_CONTENT_SLOT, recorder, compatibilitySubtree: true);
            update.StageConsumption(element, RenderDirtyKind.Content | RenderDirtyKind.Resource | RenderDirtyKind.Layout);
        }
        else
        {
            ReplaySlot(scene, node, OWN_CONTENT_SLOT, recorder);
        }

        var bounds = default(BoundsAccumulator);
        bounds.Add(Visible(RetainedGeometry.TransformRect(SlotBounds(update, node, OWN_CONTENT_SLOT), transform)));
        update.StageBounds(node, bounds.Result, bounds.Result, Origin(element), IsClippedAway(element, transform, bounds.Result));
        return bounds.Result;
    }

    private void CaptureEntries(
        RenderScene scene,
        SceneUpdate update,
        VisualNode node,
        CompositionPlan plan,
        RenderDataRecorder recorder,
        DirtyView dirty,
        bool attachmentChanged,
        int startIndex,
        int count,
        Matrix3x2 transform,
        ref BoundsAccumulator ownBounds,
        ref BoundsAccumulator subtreeBounds)
    {
        var entries = plan.Entries;
        var context = recorder.Inner;
        int entryIndex = startIndex;
        int endIndex = startIndex + count;

        while (entryIndex < endIndex)
        {
            var entry = entries[entryIndex];
            switch (entry.Kind)
            {
                case CompositionEntryKind.Content:
                    int slotIndex = entry.SlotIndex;
                    if (NeedsRecording(node, node.Element, slotIndex, dirty, attachmentChanged, recorder.DpiScale))
                    {
                        RecordSlot(scene, update, node, slotIndex, recorder, compatibilitySubtree: false);
                    }
                    else
                    {
                        ReplaySlot(scene, node, slotIndex, recorder);
                    }

                    ownBounds.Add(Visible(RetainedGeometry.TransformRect(SlotBounds(update, node, slotIndex), transform)));
                    entryIndex++;
                    break;

                case CompositionEntryKind.Child:
                    var childBounds = CaptureVisual(
                        scene, update, entry.Child!, recorder, dirty, node.Element, transform);
                    subtreeBounds.Add(childBounds);
                    entryIndex++;
                    break;

                default:
                    context.Save();
                    var outerClip = _ambientClip;
                    try
                    {
                        ApplyScope(context, in entry);
                        if (entry.Kind is CompositionEntryKind.ClipRect or CompositionEntryKind.ClipRoundedRect)
                        {
                            // What lies past the clip never reaches the surface, so it is neither ink nor damage.
                            var scopeClip = RetainedGeometry.TransformRect(entry.Rect, transform);
                            _ambientClip = outerClip is Rect outer ? outer.Intersect(scopeClip) : scopeClip;
                        }

                        var scopeTransform = entry.Kind == CompositionEntryKind.Transform
                            ? entry.Matrix * transform
                            : transform;
                        CaptureEntries(
                            scene,
                            update,
                            node,
                            plan,
                            recorder,
                            dirty,
                            attachmentChanged,
                            entryIndex + 1,
                            entry.ScopeLength,
                            scopeTransform,
                            ref ownBounds,
                            ref subtreeBounds);
                    }
                    finally
                    {
                        _ambientClip = outerClip;
                        context.Restore();
                    }

                    entryIndex += entry.ScopeLength + 1;
                    break;
            }
        }

        if (startIndex == 0)
        {
            update.StageConsumption(node.Element, RenderDirtyKind.Content | RenderDirtyKind.Resource | RenderDirtyKind.Layout);
        }
    }

    /// <summary>Local bounds of a slot, taken from this pass's staged data when it was re-recorded.</summary>
    private static Rect SlotBounds(SceneUpdate update, VisualNode node, int slotIndex)
    {
        bool stagedThisPass = update.TryGetStagedSlot(node, slotIndex, out var staged);
        var data = stagedThisPass ? staged : node.GetSlot(slotIndex);
        if (data == null)
        {
            // A slot that could not be recorded is drawn live. What it inks is not known, so it answers
            // for the bounds of its visual: enough to be found by a damaged area and to damage its own.
            bool drawnLive = stagedThisPass || node.NonRecordableReason != null;
            return drawnLive ? node.Element.Bounds : default;
        }

        if (data.LocalBounds.IsEmpty)
        {
            return default;
        }

        // The recorded extent stands where the recording was taken; the visual may have moved since.
        var bounds = node.Element.Bounds;
        return data.LocalBounds.Offset(bounds.X - data.RecordedBounds.X, bounds.Y - data.RecordedBounds.Y);
    }

    private static void ApplyScope(IGraphicsContext context, in CompositionEntry entry)
    {
        switch (entry.Kind)
        {
            case CompositionEntryKind.ClipRect:
                context.SetClip(entry.Rect);
                break;
            case CompositionEntryKind.ClipRoundedRect:
                context.SetClipRoundedRect(entry.Rect, entry.RadiusX, entry.RadiusY);
                break;
            case CompositionEntryKind.Transform:
                context.SetTransform(entry.Matrix * context.GetTransform());
                break;
            default:
                throw new InvalidOperationException($"The composition entry {entry.Kind} is not a scope.");
        }
    }

    private static bool NeedsRecording(
        VisualNode node,
        UIElement element,
        int slotIndex,
        DirtyView dirty,
        bool attachmentChanged,
        double dpiScale)
    {
        var data = node.GetSlot(slotIndex);
        bool changed = attachmentChanged ||
            node.RecordedContentVersion != element.RenderContentVersion ||
            dirty.IsDirty(element, RenderDirtyKind.Content | RenderDirtyKind.Resource | RenderDirtyKind.Layout);
        if (data == null)
        {
            // A slot already found unrecordable is tried again only when its visual changed; trying it
            // every pass would damage its area every frame for nothing.
            return changed || node.NonRecordableReason == null;
        }

        return changed || !CanPlaceRecording(data, element.Bounds, dpiScale);
    }

    /// <summary>
    /// A compatibility subtree owns its descendants' drawing, so any repaint under it invalidates it.
    /// Its drawing is not known to be independent of where it stands, so a move records it again too.
    /// </summary>
    private static bool NeedsCompatibilityRecording(
        VisualNode node,
        UIElement element,
        DirtyView dirty,
        bool attachmentChanged)
    {
        var data = node.GetSlot(OWN_CONTENT_SLOT);
        return attachmentChanged ||
            data == null ||
            node.RecordedContentVersion != element.RenderContentVersion ||
            dirty.IsDirty(element, RenderDirtyKind.Content | RenderDirtyKind.Resource | RenderDirtyKind.Layout) ||
            node.RecordedSubtreeVersion != element.SubtreeContentVersion ||
            data.RecordedBounds != element.Bounds;
    }

    /// <summary>
    /// Whether a recording still draws the visual correctly when
    /// moved to <paramref name="current"/>. The size has to be the same, and the move has to be whole
    /// device pixels: drawing snaps to the pixel grid, so a fractional move changes what was drawn.
    /// </summary>
    private static bool CanPlaceRecording(RenderData data, Rect current, double dpiScale)
    {
        var recorded = data.RecordedBounds;
        if (recorded.Width != current.Width || recorded.Height != current.Height)
        {
            return false;
        }

        if (!data.CanBePlaced && (recorded.X != current.X || recorded.Y != current.Y))
        {
            return false;
        }

        return IsWholeDevicePixels((current.X - recorded.X) * dpiScale) &&
            IsWholeDevicePixels((current.Y - recorded.Y) * dpiScale);
    }

    private static bool IsWholeDevicePixels(double devicePixels)
        => Math.Abs(devicePixels - Math.Round(devicePixels)) < PLACEMENT_EPSILON;

    /// <summary>
    /// True when the visual has a box but the clips around it let none of it, nor of anything under it,
    /// reach the surface. A visual that merely draws nothing is not clipped away: once it has something
    /// to draw, that has to be able to show.
    /// </summary>
    private bool IsClippedAway(UIElement element, Matrix3x2 transform, Rect visibleSubtree)
    {
        if (_ambientClip == null || (visibleSubtree.Width > 0 && visibleSubtree.Height > 0))
        {
            return false;
        }

        var box = RetainedGeometry.TransformRect(element.Bounds, transform);
        return box.Width > 0 && box.Height > 0 && Visible(box).IsEmpty;
    }

    /// <summary>The part of an extent that the clips around it let reach the surface.</summary>
    private Rect Visible(Rect extent)
    {
        if (_ambientClip is not Rect clip || extent.IsEmpty)
        {
            return extent;
        }

        var visible = extent.Intersect(clip);
        return visible.Width > 0 && visible.Height > 0 ? visible : default;
    }

    private static Point Origin(UIElement element) => new(element.Bounds.X, element.Bounds.Y);

    private void RecordSlot(
        RenderScene scene,
        SceneUpdate update,
        VisualNode node,
        int slotIndex,
        RenderDataRecorder recorder,
        bool compatibilitySubtree)
    {
        _slotBuilder.Discard();
        recorder.Slot = _slotBuilder;
        try
        {
            if (compatibilitySubtree)
            {
                node.Element.Render(recorder);
            }
            else
            {
                node.Element.WriteOwnContent(recorder, slotIndex);
            }
        }
        finally
        {
            recorder.Slot = null;
        }

        scene.Statistics.ContentRecordCount++;

        int contentVersion = node.Element.RenderContentVersion;
        int subtreeVersion = node.Element.SubtreeContentVersion;

        if (_slotBuilder.TryBuild(out var data))
        {
            data.RecordedBounds = node.Element.Bounds;
            update.StageSlot(node, slotIndex, data, null, contentVersion, subtreeVersion);
        }
        else
        {
            update.StageSlot(node, slotIndex, null, _slotBuilder.RejectionReason, contentVersion, subtreeVersion);
            scene.Statistics.RejectedSlotCount++;
            _slotBuilder.Discard();
        }
    }

    private static void ReplaySlot(RenderScene scene, VisualNode node, int slotIndex, RenderDataRecorder recorder)
    {
        // A pass that only updates the scene has nothing to put on the surface for a valid slot.
        if (!recorder.Draws)
        {
            return;
        }

        var data = node.GetSlot(slotIndex);
        if (data == null)
        {
            return;
        }

        ReplayPlaced(data, recorder.Inner, Origin(node.Element));
        scene.Statistics.ContentReplayCount++;
    }

    /// <summary>Replays a recording moved from where it was taken to where the visual stands now.</summary>
    internal static void ReplayPlaced(RenderData data, IGraphicsContext context, Point placedOrigin)
    {
        double offsetX = placedOrigin.X - data.RecordedBounds.X;
        double offsetY = placedOrigin.Y - data.RecordedBounds.Y;
        if (offsetX == 0 && offsetY == 0)
        {
            data.Replay(context);
            return;
        }

        context.Save();
        try
        {
            context.Translate(offsetX, offsetY);
            data.ReplayMoved(context, offsetX, offsetY);
        }
        finally
        {
            context.Restore();
        }
    }

    /// <summary>
    /// What this pass treats as dirty. A pass driven by a plain element set records those elements,
    /// a pass driven by the registry records what the registry queued, and a pass with neither
    /// records everything.
    /// </summary>
    private readonly struct DirtyView(IReadOnlySet<UIElement>? elements, RenderDirtyRegistry? registry)
    {
        internal bool IsDirty(UIElement element, RenderDirtyKind kind)
        {
            if (registry != null)
            {
                return registry.Has(element, kind);
            }

            if (elements != null)
            {
                return elements.Contains(element);
            }

            return true;
        }

        internal RenderDirtyRegistry? Registry => registry;
    }
}
