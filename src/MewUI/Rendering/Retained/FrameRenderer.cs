namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Draws a completed <see cref="RenderScene"/> without asking any visual to render. A slot whose
/// content could not be recorded falls back to that visual's live drawing for that slot only.
/// </summary>
internal static class FrameRenderer
{
    private const int OWN_CONTENT_SLOT = 0;

    internal static void Replay(RenderScene scene, IGraphicsContext context)
        => Replay(scene, context, damage: null);

    /// <summary>
    /// Draws the scene, restricted to <paramref name="damage"/> when one is given: the area is
    /// clipped and visuals that fall outside it are skipped, while the drawing order is unchanged.
    /// </summary>
    internal static void Replay(RenderScene scene, IGraphicsContext context, Rect? damage)
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

        if (damage == null)
        {
            ReplayRoots(scene, root, context, null);
            return;
        }

        context.Save();
        try
        {
            // The damage is in surface coordinates and a clip is set in the context's own, which differ
            // when the scene is replayed under a transform, as a popup window does.
            var clip = damage.Value;
            var transform = context.GetTransform();
            if (!transform.IsIdentity && System.Numerics.Matrix3x2.Invert(transform, out var toLocal))
            {
                // The way there and back is not exact, and the caller has already clipped the surface
                // to the damage itself, so this clip only has to not fall short of it.
                clip = RetainedGeometry.TransformRect(clip, toLocal).Inflate(1, 1);
            }

            context.IntersectClip(clip);
            ReplayRoots(scene, root, context, damage);
        }
        finally
        {
            context.Restore();
        }
    }

    /// <summary>Draws the body and then every layer over it, in the order the scene holds them.</summary>
    private static void ReplayRoots(RenderScene scene, VisualNode root, IGraphicsContext context, Rect? damage)
    {
        ReplayNode(scene, root, context, damage);

        var layerRoots = scene.LayerRoots;
        for (int layerIndex = 0; layerIndex < layerRoots.Count; layerIndex++)
        {
            var layer = scene.FindNode(layerRoots[layerIndex]);
            if (layer != null)
            {
                ReplayNode(scene, layer, context, damage);
            }
        }
    }

    private static void ReplayNode(RenderScene scene, VisualNode node, IGraphicsContext context, Rect? damage)
    {
        if (!node.State.IsVisible)
        {
            return;
        }

        if (damage != null && !Intersects(node.SurfaceSubtreeBounds, damage.Value))
        {
            return;
        }

        if (node.StateBakedIntoContent)
        {
            ReplaySlot(scene, node, OWN_CONTENT_SLOT, context, isCompatibilitySubtree: true);
            return;
        }

        // A faded visual is blended onto the surface once, as a whole, so what it draws goes through a
        // group: two of its children that overlap must not show through each other.
        bool opacityScope = node.State.Opacity < 1;
        var group = default(OpacityGroup);
        if (opacityScope)
        {
            var reach = damage is Rect damaged ? node.SurfaceSubtreeBounds.Intersect(damaged) : node.SurfaceSubtreeBounds;
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
            context.BeginOpaqueBackdrop();
        }

        try
        {
            ReplayEntries(scene, node, context, 0, node.Plan.Entries.Length, damage);
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
        }
    }

    private static void ReplayEntries(
        RenderScene scene,
        VisualNode node,
        IGraphicsContext context,
        int startIndex,
        int count,
        Rect? damage)
    {
        var entries = node.Plan.Entries;
        int entryIndex = startIndex;
        int endIndex = startIndex + count;

        while (entryIndex < endIndex)
        {
            var entry = entries[entryIndex];
            switch (entry.Kind)
            {
                case CompositionEntryKind.Content:
                    ReplaySlot(scene, node, entry.SlotIndex, context, isCompatibilitySubtree: false);
                    entryIndex++;
                    break;

                case CompositionEntryKind.Child:
                    var childNode = scene.FindNode(entry.Child!);
                    if (childNode != null)
                    {
                        ReplayNode(scene, childNode, context, damage);
                    }

                    entryIndex++;
                    break;

                default:
                    context.Save();
                    try
                    {
                        ApplyScope(context, in entry);
                        ReplayEntries(scene, node, context, entryIndex + 1, entry.ScopeLength, damage);
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

    private static bool Intersects(Rect bounds, Rect damage)
        => bounds.Width > 0 && bounds.Height > 0 && bounds.IntersectsWith(damage);

    private static void ReplaySlot(
        RenderScene scene,
        VisualNode node,
        int slotIndex,
        IGraphicsContext context,
        bool isCompatibilitySubtree)
    {
        var data = node.GetSlot(slotIndex);
        if (data != null)
        {
            SceneCapture.ReplayPlaced(data, context, node.PlacedOrigin);
            scene.Statistics.ContentReplayCount++;
            return;
        }

        if (isCompatibilitySubtree)
        {
            node.Element.Render(context);
        }
        else
        {
            node.Element.WriteOwnContent(context, slotIndex);
        }

        scene.Statistics.LiveFallbackCount++;
    }
}
