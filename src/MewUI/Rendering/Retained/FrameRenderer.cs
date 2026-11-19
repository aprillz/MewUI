using System.Numerics;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Draws a completed <see cref="RenderScene"/> without asking any visual to render. A slot whose
/// content could not be recorded falls back to that visual's live drawing for that slot only.
/// </summary>
internal static class FrameRenderer
{
    private const int OWN_CONTENT_SLOT = 0;

    internal static void Replay(RenderScene scene, IGraphicsContext context)
        => Replay(scene, context, dirtyRect: null);

    /// <summary>
    /// Draws the scene, restricted to <paramref name="dirtyRect"/> when one is given: the area is
    /// clipped and visuals that fall outside it are skipped, while the drawing order is unchanged.
    /// </summary>
    internal static void Replay(RenderScene scene, IGraphicsContext context, Rect? dirtyRect)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(context);

        if (scene.Root == null)
        {
            return;
        }

        var root = scene.FindNode(scene.Root);
        if (root == null)
        {
            return;
        }

        RenderDataReplayer.ReplayPass = scene.PassId;

        if (dirtyRect == null)
        {
            ReplayRoots(scene, root, context, null);
            return;
        }

        context.Save();
        try
        {
            // The dirty region is in surface coordinates and a clip is set in the context's own, which differ
            // when the scene is replayed under a transform, as a popup window does.
            var clip = dirtyRect.Value;
            var transform = context.GetTransform();
            if (!transform.IsIdentity && System.Numerics.Matrix3x2.Invert(transform, out var toLocal))
            {
                // The way there and back is not exact, and the caller has already clipped the surface
                // to the dirty region itself, so this clip only has to not fall short of it.
                clip = RetainedGeometry.TransformRect(clip, toLocal).Inflate(1, 1);
            }

            context.IntersectClip(clip);
            ReplayRoots(scene, root, context, dirtyRect);
        }
        finally
        {
            context.Restore();
        }
    }

    /// <summary>Draws the body and then every layer over it, in the order the scene holds them.</summary>
    private static void ReplayRoots(RenderScene scene, VisualNode root, IGraphicsContext context, Rect? dirtyRect)
    {
        ReplayNode(scene, root, context, dirtyRect);

        var layerRoots = scene.LayerRoots;
        for (int layerIndex = 0; layerIndex < layerRoots.Count; layerIndex++)
        {
            var layer = scene.FindNode(layerRoots[layerIndex]);
            if (layer != null)
            {
                ReplayNode(scene, layer, context, dirtyRect);
            }
        }
    }

    private static void ReplayNode(RenderScene scene, VisualNode node, IGraphicsContext context, Rect? dirtyRect)
    {
        if (!node.State.IsVisible)
        {
            return;
        }

        // A node under a frame that moved since it was staged stands shifted by that move; the frame moved
        // whole, so only this one shift is read and nothing under it was written again. The plan may be
        // older than the place, when the frame carried the node along, and reads under its own shift.
        var subtreeBounds = node.ShiftedSurfaceSubtreeBounds;
        var localShift = node.LocalShift;
        var planShift = node.PlanLocalShift;

        // A subtree the clips around it hide has no extent on the surface, so a whole frame leaves it out as a
        // partial one does: a scroll replays the containers in the viewport, not the whole list behind it.
        if (!HasExtent(subtreeBounds) || (dirtyRect != null && !subtreeBounds.IntersectsWith(dirtyRect.Value)))
        {
            return;
        }

        if (node.StateBakedIntoContent)
        {
            ReplaySlot(scene, node, OWN_CONTENT_SLOT, context, isCompatibilitySubtree: true, localShift);
            return;
        }

        // A faded visual is blended onto the surface once, as a whole, so what it draws goes through a
        // group: two of its children that overlap must not show through each other.
        // One shape has nothing of its own to show through, so it fades without a group.
        bool faded = node.State.Opacity < 1;
        bool alphaScope = faded && node.DrawsOneShapeAtMost;
        bool opacityScope = faded && !alphaScope;
        var group = default(OpacityGroup);
        if (alphaScope)
        {
            context.Save();
            context.GlobalAlpha *= (float)node.State.Opacity;
        }

        if (opacityScope && TryReplayKeptGroup(scene, node, context, subtreeBounds, localShift, planShift))
        {
            return;
        }

        if (opacityScope)
        {
            var reach = dirtyRect is Rect dirty ? subtreeBounds.Intersect(dirty) : subtreeBounds;
            group = OpacityGroup.Begin(context, scene.GroupFactory, node.State.Opacity, reach);
            context = group.Target;
            if (!ReferenceEquals(context, group.Outer))
            {
                scene.Statistics.GroupSurfaceCount++;
            }
        }

        bool backdropScope = node.State.OpaqueBackdrop;
        if (backdropScope)
        {
            OpaqueBackdropScope.Begin(context, node.Element.Bounds);
        }

        try
        {
            ReplayEntries(scene, node, context, 0, node.Plan.Entries.Length, dirtyRect, localShift, planShift);
        }
        finally
        {
            if (backdropScope)
            {
                context.EndOpaqueBackdrop();
            }

            if (opacityScope)
            {
                group.End();
            }

            if (alphaScope)
            {
                context.Restore();
            }
        }
    }

    /// <summary>
    /// Blends a faded visual through the surface kept for it: as kept when the group would come out the same,
    /// drawn again into it otherwise. False when the group cannot be kept, which leaves it to a passing surface.
    /// </summary>
    private static bool TryReplayKeptGroup(
        RenderScene scene,
        VisualNode node,
        IGraphicsContext context,
        Rect subtreeBounds,
        Vector localShift,
        Vector planShift)
    {
        var factory = scene.GroupFactory;
        var layers = scene.GroupLayers;

        if (factory == null || context is IGroupOpacityContext || context is not GraphicsContextBase || layers.IsVolatile(node))
        {
            return false;
        }

        var anchor = node.PlacedOrigin + localShift;
        var signature = layers.Scratch;
        signature.Clear();
        if (!TryCollectGroup(scene, node, anchor, localShift, signature, isRoot: true, out var scopeMove))
        {
            return false;
        }

        double scale = context.DpiScale;
        var transform = context.GetTransform();
        var linear = transform with { M31 = 0, M32 = 0 };
        // A move the group's own transform scope makes moves everything in the group by that much on the surface.
        var mappedAnchor = Vector2.Transform(new Vector2((float)anchor.X, (float)anchor.Y), transform) +
            Vector2.TransformNormal(new Vector2((float)scopeMove.X, (float)scopeMove.Y), transform);
        var surfaceAnchor = new Point(mappedAnchor.X, mappedAnchor.Y);
        var rootNode = scene.Root == null ? null : scene.FindNode(scene.Root);
        if (rootNode != null)
        {
            layers.NoteSurfacePixels((long)(rootNode.SurfaceBounds.Width * scale * rootNode.SurfaceBounds.Height * scale));
        }

        if (layers.TryGet(node, out var kept) && kept.Surface != null && ReferenceEquals(kept.Factory, factory) &&
            kept.Scale == scale && kept.Linear == linear && kept.Signature.SameAs(signature))
        {
            double moveX = surfaceAnchor.X - kept.SurfaceAnchor.X;
            double moveY = surfaceAnchor.Y - kept.SurfaceAnchor.Y;
            if (IsWholeDevicePixels(moveX, scale) && IsWholeDevicePixels(moveY, scale) &&
                IsNear(subtreeBounds, VisualNode.Shifted(kept.Reach, new Vector(moveX, moveY))))
            {
                BlendLayer(context, kept, new Rect(kept.Placement.X + moveX, kept.Placement.Y + moveY, kept.Placement.Width, kept.Placement.Height), node.State.Opacity);
                layers.NoteHit(kept);
                scene.Statistics.GroupLayerReuseCount++;
                return true;
            }
        }

        double left = Math.Floor(subtreeBounds.X * scale) / scale;
        double top = Math.Floor(subtreeBounds.Y * scale) / scale;
        int pixelWidth = (int)Math.Ceiling((subtreeBounds.Right - left) * scale);
        int pixelHeight = (int)Math.Ceiling((subtreeBounds.Bottom - top) * scale);
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return false;
        }

        var layer = layers.GetOrAdd(node);
        if (!layers.NoteMiss(node, layer))
        {
            return false;
        }

        var surface = layer.EnsureSurface(factory, pixelWidth, pixelHeight, scale);
        layer.ForgetImage();
        var inner = factory.CreateContext(surface);
        try
        {
            inner.BeginFrame(surface);
            inner.Clear(Color.Transparent);
            if (inner is GraphicsContextBase innerBase)
            {
                innerBase.SetRootTransform(Matrix3x2.CreateTranslation((float)-left, (float)-top));
            }
            else
            {
                inner.Translate(-left, -top);
            }

            inner.SetTransform(transform);
            inner.ImageScaleQuality = context.ImageScaleQuality;
            bool backdropScope = node.State.OpaqueBackdrop;
            if (backdropScope)
            {
                OpaqueBackdropScope.Begin(inner, node.Element.Bounds);
            }

            ReplayEntries(scene, node, inner, 0, node.Plan.Entries.Length, null, localShift, planShift);
            if (backdropScope)
            {
                inner.EndOpaqueBackdrop();
            }

            inner.EndFrame();
        }
        finally
        {
            inner.Dispose();
        }

        scene.Statistics.GroupSurfaceCount++;
        layers.Keep(layer);
        layer.Placement = new Rect(left, top, pixelWidth / scale, pixelHeight / scale);
        layer.Reach = subtreeBounds;
        layer.SurfaceAnchor = surfaceAnchor;
        layer.Linear = linear;
        BlendLayer(context, layer, layer.Placement, node.State.Opacity);
        return true;
    }

    /// <summary>
    /// Collects what replaying a group reads from one visual under it, relative to the group's anchor and shift.
    /// False when the group cannot be kept because something under it is drawn live.
    /// </summary>
    private static bool TryCollectGroup(RenderScene scene, VisualNode node, Point groupAnchor, Vector groupShift, GroupSignature signature, bool isRoot)
        => TryCollectGroup(scene, node, groupAnchor, groupShift, signature, isRoot, out _);

    /// <summary>
    /// As above, and for the group's own visual gives the translation of the transform scope that moves everything
    /// it draws, as a decorator's does (see <see cref="TryAddMovablePlan"/>), which the caller counts as a move.
    /// </summary>
    private static bool TryCollectGroup(RenderScene scene, VisualNode node, Point groupAnchor, Vector groupShift, GroupSignature signature,
        bool isRoot, out Vector scopeMove)
    {
        scopeMove = default;
        if (isRoot)
        {
            // The group's own opacity applies when the surface is blended, not when it is drawn.
            signature.Add(node.State with { Opacity = 1 });
        }
        else
        {
            signature.Add(node.State);
            if (!node.State.IsVisible)
            {
                return true;
            }

            if (!HasExtent(node.ShiftedSurfaceSubtreeBounds))
            {
                signature.Add(1);
                return true;
            }

            signature.Add(2);
        }

        var anchor = node.PlacedOrigin + node.LocalShift;
        var planShift = node.PlanLocalShift;
        signature.Add(node);
        signature.Add(anchor.X - groupAnchor.X);
        signature.Add(anchor.Y - groupAnchor.Y);
        signature.Add(planShift.X - groupShift.X);
        signature.Add(planShift.Y - groupShift.Y);

        // A visual that fills its box opaquely tells the context so, over the box it stands in.
        if (node.State.OpaqueBackdrop)
        {
            var box = node.Element.Bounds;
            signature.Add(box.X - groupAnchor.X);
            signature.Add(box.Y - groupAnchor.Y);
            signature.Add(box.Width);
            signature.Add(box.Height);
        }
        else
        {
            signature.Add(-1);
        }
        if (node.StateBakedIntoContent)
        {
            var baked = node.GetSlot(OWN_CONTENT_SLOT);
            signature.Add(baked);
            return baked != null;
        }

        var entries = node.Plan.Entries;
        if (!isRoot || !TryAddMovablePlan(node, entries, planShift, signature, out scopeMove))
        {
            signature.Add(node.Plan);
        }

        for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
        {
            ref readonly var entry = ref entries[entryIndex];
            if (entry.Kind == CompositionEntryKind.Content)
            {
                var data = node.GetSlot(entry.SlotIndex);
                if (data == null)
                {
                    return false;
                }

                signature.Add(data);
            }
            else if (entry.Kind == CompositionEntryKind.Child)
            {
                var childNode = node.FindChildNode(scene, entryIndex, entry.Child!);
                signature.Add(childNode);
                if (childNode != null && !TryCollectGroup(scene, childNode, groupAnchor, groupShift, signature, isRoot: false))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Adds the plan of a group's own visual when everything it draws moves as one: under a single transform scope
    /// at the top, as a decorator's child is, or under none, with nothing drawn outside that scope. The scope's
    /// translation is left out and given back as the move; its absence reads as an identity scope, so a decorator
    /// whose transform comes and goes reads the same.
    /// </summary>
    private static bool TryAddMovablePlan(VisualNode node, CompositionEntry[] entries, Vector planShift, GroupSignature signature, out Vector scopeMove)
    {
        scopeMove = default;
        int scopeIndex = -1;
        int entryIndex = 0;
        while (entryIndex < entries.Length)
        {
            ref readonly var entry = ref entries[entryIndex];
            switch (entry.Kind)
            {
                case CompositionEntryKind.Transform:
                    if (scopeIndex >= 0)
                    {
                        return false;
                    }

                    scopeIndex = entryIndex;
                    entryIndex += entry.ScopeLength + 1;
                    break;
                case CompositionEntryKind.Content:
                    var data = node.GetSlot(entry.SlotIndex);
                    if (data == null || !data.IsEmpty)
                    {
                        return false;
                    }

                    entryIndex++;
                    break;
                case CompositionEntryKind.Child:
                    entryIndex++;
                    break;
                default:
                    return false;
            }
        }

        // A child outside the scope would stay where it is while the scope moves.
        if (scopeIndex >= 0)
        {
            for (int index = 0; index < entries.Length; index++)
            {
                bool insideScope = index > scopeIndex && index <= scopeIndex + entries[scopeIndex].ScopeLength;
                if (!insideScope && entries[index].Kind == CompositionEntryKind.Child)
                {
                    return false;
                }
            }
        }

        // The scope reads without its translation and its length, and an absent scope reads as an identity one.
        var scopeEntry = new CompositionEntry { Kind = CompositionEntryKind.Transform, Matrix = Matrix3x2.Identity };
        if (scopeIndex >= 0)
        {
            var scope = RetainedGeometry.Shifted(entries[scopeIndex].Matrix, planShift);
            scopeMove = new Vector(scope.M31, scope.M32);
            scopeEntry = entries[scopeIndex] with { Matrix = scope with { M31 = 0, M32 = 0 }, ScopeLength = 0 };
        }

        signature.Add(in scopeEntry);
        for (int index = 0; index < entries.Length; index++)
        {
            if (index != scopeIndex)
            {
                signature.Add(in entries[index]);
            }
        }

        return true;
    }

    private static void BlendLayer(IGraphicsContext context, GroupLayerCache.Layer layer, Rect placement, double opacity)
    {
        var image = layer.GetImage();
        context.Save();
        try
        {
            context.SetTransform(Matrix3x2.Identity);
            context.GlobalAlpha *= (float)Math.Clamp(opacity, 0.0, 1.0);
            context.DrawImage(image, placement, new Rect(0, 0, layer.PixelWidth, layer.PixelHeight));
        }
        finally
        {
            context.Restore();
        }
    }

    // How close to a whole device pixel a move has to be for the kept surface to land where a fresh one would.
    private const double WHOLE_PIXEL_TOLERANCE = 1.0 / 1024;

    private static bool IsWholeDevicePixels(double distance, double scale)
    {
        double pixels = distance * scale;
        return Math.Abs(pixels - Math.Round(pixels)) <= WHOLE_PIXEL_TOLERANCE;
    }

    private static bool IsNear(Rect first, Rect second)
        => Math.Abs(first.X - second.X) <= WHOLE_PIXEL_TOLERANCE && Math.Abs(first.Y - second.Y) <= WHOLE_PIXEL_TOLERANCE &&
           Math.Abs(first.Width - second.Width) <= WHOLE_PIXEL_TOLERANCE && Math.Abs(first.Height - second.Height) <= WHOLE_PIXEL_TOLERANCE;

    private static void ReplayEntries(
        RenderScene scene,
        VisualNode node,
        IGraphicsContext context,
        int startIndex,
        int count,
        Rect? dirtyRect,
        Vector localShift,
        Vector planShift)
    {
        var entries = node.Plan.Entries;
        int entryIndex = startIndex;
        int endIndex = startIndex + count;

        while (entryIndex < endIndex)
        {
            // By reference: an entry carries a rectangle and a matrix, and most visits read only its kind and child.
            ref readonly var entry = ref entries[entryIndex];
            switch (entry.Kind)
            {
                case CompositionEntryKind.Content:
                    ReplaySlot(scene, node, entry.SlotIndex, context, isCompatibilitySubtree: false, localShift);
                    entryIndex++;
                    break;

                case CompositionEntryKind.Child:
                    var childNode = node.FindChildNode(scene, entryIndex, entry.Child!);
                    if (childNode != null)
                    {
                        ReplayNode(scene, childNode, context, dirtyRect);
                    }

                    entryIndex++;
                    break;

                case CompositionEntryKind.Frame:
                    // The child under it reads its own frame; drawing applies nothing for the scope.
                    ReplayEntries(scene, node, context, entryIndex + 1, entry.ScopeLength, dirtyRect, localShift, planShift);
                    entryIndex += entry.ScopeLength + 1;
                    break;

                default:
                    context.Save();
                    try
                    {
                        ApplyScope(context, in entry, planShift);
                        ReplayEntries(scene, node, context, entryIndex + 1, entry.ScopeLength, dirtyRect, localShift, planShift);
                    }
                    finally
                    {
                        context.Restore();
                    }

                    entryIndex += entry.ScopeLength + 1;
                    break;
            }
        }
    }

    /// <summary>Applies a scope as the plan holds it, moved by <paramref name="planShift"/> when its node's frame moved since the plan was built.</summary>
    private static void ApplyScope(IGraphicsContext context, in CompositionEntry entry, Vector planShift)
    {
        switch (entry.Kind)
        {
            case CompositionEntryKind.ClipRect:
                context.SetClip(VisualNode.Shifted(entry.Rect, planShift));
                break;
            case CompositionEntryKind.ClipRoundedRect:
                context.SetClipRoundedRect(VisualNode.Shifted(entry.Rect, planShift), entry.RadiusX, entry.RadiusY);
                break;
            case CompositionEntryKind.Transform:
                context.SetTransform(RetainedGeometry.Shifted(entry.Matrix, planShift) * context.GetTransform());
                if (entry.DisablesTextPixelSnap)
                {
                    context.TextPixelSnap = false;
                }

                break;
            case CompositionEntryKind.Alpha:
                context.GlobalAlpha *= (float)entry.Alpha;
                break;
            default:
                throw new InvalidOperationException($"The composition entry {entry.Kind} is not a scope.");
        }
    }

    private static bool HasExtent(Rect bounds) => bounds.Width > 0 && bounds.Height > 0;

    private static void ReplaySlot(
        RenderScene scene,
        VisualNode node,
        int slotIndex,
        IGraphicsContext context,
        bool isCompatibilitySubtree,
        Vector localShift)
    {
        var data = node.GetSlot(slotIndex);
        if (data != null)
        {
            SceneCapture.ReplayPlaced(data, context, node.PlacedOrigin + localShift);
            scene.Statistics.ContentReplayCount++;
            return;
        }

        if (isCompatibilitySubtree)
        {
            node.Element.Render(context);
        }
        else
        {
            node.Element.RenderOwnSlot(context, slotIndex);
        }

        scene.Statistics.LiveFallbackCount++;
    }
}
