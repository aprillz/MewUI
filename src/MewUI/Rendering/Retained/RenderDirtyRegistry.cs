using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// What changed about a visual. The kinds are tracked apart because they replace different parts
/// of a node: content replaces a slot's data, composition replaces the child order, and state
/// changes only what the scene applies around the node.
/// </summary>
[Flags]
internal enum RenderDirtyKind : byte
{
    None = 0,
    Content = 1,
    Composition = 2,
    State = 4,
    Resource = 8,
    Placement = 16,

    /// <summary>The visual was laid out again, so its drawing is taken again and compared with what it had.</summary>
    Layout = 32,
}

/// <summary>
/// Per-surface dirty queue. Entries added while a pass is open belong to the next pass, so an
/// invalidation raised during recording is never dropped by the pass that caused it.
/// </summary>
internal sealed class RenderDirtyRegistry
{
    private readonly Dictionary<UIElement, RenderDirtyKind> _current = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<UIElement, RenderDirtyKind> _next = new(ReferenceEqualityComparer.Instance);
    private bool _passOpen;

    internal void Add(UIElement element, RenderDirtyKind kind)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (kind == RenderDirtyKind.None)
        {
            return;
        }

        var target = _passOpen ? _next : _current;
        target[element] = target.TryGetValue(element, out var existing) ? existing | kind : kind;
    }

    /// <summary>Whether any of the given kinds is queued for that visual in this pass.</summary>
    internal bool Has(UIElement element, RenderDirtyKind kind)
        => _current.TryGetValue(element, out var queued) && (queued & kind) != 0;

    /// <summary>Drops the kinds a pass has served, keeping the rest queued.</summary>
    internal void Consume(UIElement element, RenderDirtyKind kind)
    {
        if (!_current.TryGetValue(element, out var existing))
        {
            return;
        }

        var remaining = existing & ~kind;
        if (remaining == RenderDirtyKind.None)
        {
            _current.Remove(element);
        }
        else
        {
            _current[element] = remaining;
        }
    }

    /// <summary>What is queued for the pass that reads next, for diagnostics and tests.</summary>
    internal IReadOnlyDictionary<UIElement, RenderDirtyKind> Queued => _current;

    /// <summary>
    /// Drops what is queued for visuals the surface no longer draws. Their change needs no pass, and a
    /// queue that kept them would keep the visuals themselves alive.
    /// </summary>
    internal void RemoveWhere(Func<UIElement, bool> isGone)
    {
        List<UIElement>? gone = null;
        foreach (var element in _current.Keys)
        {
            if (isGone(element))
            {
                gone ??= [];
                gone.Add(element);
            }
        }

        if (gone == null)
        {
            return;
        }

        for (int index = 0; index < gone.Count; index++)
        {
            _current.Remove(gone[index]);
        }
    }

    internal void BeginPass() => _passOpen = true;

    /// <summary>
    /// Closes the pass and moves the invalidations raised during it into the queue the next pass
    /// reads. Entries this pass did not serve stay queued.
    /// </summary>
    internal void EndPass()
    {
        _passOpen = false;

        if (_next.Count == 0)
        {
            return;
        }

        foreach (var entry in _next)
        {
            _current[entry.Key] = _current.TryGetValue(entry.Key, out var existing)
                ? existing | entry.Value
                : entry.Value;
        }

        _next.Clear();
    }

    internal void Clear()
    {
        _current.Clear();
        _next.Clear();
        _passOpen = false;
    }
}
