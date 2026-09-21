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

    internal void Reset()
    {
        ContentRecordCount = 0;
        ContentReplayCount = 0;
        RejectedSlotCount = 0;
        LiveFallbackCount = 0;
        CapturedNodeCount = 0;
        GroupSurfaceCount = 0;
    }
}

/// <summary>
/// Retained scene of one surface. Owns the nodes of the visuals recorded for that surface and
/// nothing else; the surface and its device resources are held elsewhere.
/// </summary>
internal sealed class RenderScene : IDisposable
{
    private readonly Dictionary<UIElement, VisualNode> _nodes = new(ReferenceEqualityComparer.Instance);
    private BoundsAccumulator _damage;
    private readonly DamageRegion _damageRegion = new();

    internal UIElement? Root { get; set; }

    /// <summary>
    /// The visuals drawn over <see cref="Root"/> on the same surface, in the order they are drawn:
    /// adorners, popups shown inside the surface, overlays. They are roots of the same scene, so they
    /// take part in the same update and the same damage as the body.
    /// </summary>
    internal IReadOnlyList<UIElement> LayerRoots { get; set; } = [];

    internal RenderSceneStatistics Statistics { get; } = new();

    /// <summary>Makes the surfaces that faded groups are drawn into. Without one a group is faded call by call.</summary>
    internal IGraphicsFactory? GroupFactory { get; set; }

    /// <summary>Nodes whose content is drawn live because it kept changing; looked over for ones that settled.</summary>
    internal List<VisualNode> DrawnLiveWhileChanging { get; } = [];

    internal int NodeCount => _nodes.Count;

    /// <summary>Identifies the pass being captured, so nodes it never reaches can be dropped.</summary>
    internal int PassId { get; private set; }

    /// <summary>Device the recorded data belongs to. Recorded resources do not survive a change.</summary>
    internal int DeviceGeneration { get; private set; }

    /// <summary>Surface-space area the last update changed, valid while <see cref="IsFullDamage"/> is false.</summary>
    internal Rect DamageBounds => _damage.Result;

    /// <summary>The same damage as separate areas, which is what a frame repaints.</summary>
    internal DamageRegion DamageRegion => _damageRegion;

    /// <summary>True when the update could not bound what it changed, so the whole surface is stale.</summary>
    internal bool IsFullDamage { get; private set; }

    internal bool HasDamage => IsFullDamage || DamageBounds.Width > 0;

    /// <summary>Records that an area of the surface no longer matches the scene.</summary>
    internal void AddDamage(Rect bounds)
    {
        _damage.Add(bounds);
        _damageRegion.Add(bounds);
    }

    internal void RequestFullDamage() => IsFullDamage = true;

    internal void ResetDamage()
    {
        _damage = default;
        _damageRegion.Clear();
        IsFullDamage = false;
    }

    /// <summary>Opens the pass an update lands in and returns its id.</summary>
    internal int BeginPass() => ++PassId;

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
        RequestFullDamage();
        foreach (var node in _nodes.Values)
        {
            node.ClearSlots();
            node.RecordedContentVersion = -1;
            node.RecordedSubtreeVersion = -1;
            node.Captured = default;
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
                AddDamage(node.SurfaceSubtreeBounds);
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
    }
}
