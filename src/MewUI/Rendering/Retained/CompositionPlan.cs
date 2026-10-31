using System.Numerics;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Rendering.Retained;

internal enum CompositionEntryKind : byte
{
    Content,
    Child,
    ClipRect,
    ClipRoundedRect,
    Transform,
    Alpha,

    /// <summary>
    /// A scope around one child that roots a <see cref="SceneFrame"/>: the scene reads what lies under the
    /// child shifted by how far the child has moved since each node was staged, so a whole-pixel move of the
    /// subtree visits no node that stays out of view. Drawing applies nothing for it.
    /// </summary>
    Frame,
}

/// <summary>
/// One step of a visual's composition: its own content, a child, or a scope that applies to the
/// entries it encloses.
/// </summary>
internal readonly struct CompositionEntry
{
    internal CompositionEntryKind Kind { get; init; }
    internal int SlotIndex { get; init; }
    internal UIElement? Child { get; init; }
    internal Rect Rect { get; init; }
    internal double RadiusX { get; init; }
    internal double RadiusY { get; init; }
    internal Matrix3x2 Matrix { get; init; }

    // A transform scope under which glyphs would land between pixels turns text snapping off for what it encloses.
    internal bool DisablesTextPixelSnap { get; init; }

    // An alpha scope multiplies the global alpha of every drawing call it encloses.
    internal double Alpha { get; init; }

    /// <summary>Number of entries this scope encloses; 0 for non-scope entries.</summary>
    internal int ScopeLength { get; init; }
}

/// <summary>
/// Ordered composition of one visual. Holds no drawing commands: content slots and children are
/// referenced, never inlined.
/// </summary>
internal sealed class CompositionPlan
{
    internal static CompositionPlan Empty { get; } = new([], 0);

    internal CompositionPlan(CompositionEntry[] entries, int slotCount)
    {
        Entries = entries;
        SlotCount = slotCount;
    }

    internal CompositionEntry[] Entries { get; }

    internal int SlotCount { get; }
}

/// <summary>
/// Collects the entries of one visual's composition. Reused across visuals within a pass.
/// </summary>
internal sealed class CompositionPlanBuilder
{
    private readonly List<CompositionEntry> _entries = [];
    private readonly List<int> _openScopes = [];
    private int _slotCount;

    internal void Content(int slotIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        _entries.Add(new CompositionEntry { Kind = CompositionEntryKind.Content, SlotIndex = slotIndex });
        _slotCount = Math.Max(_slotCount, slotIndex + 1);
    }

    internal void Child(UIElement child)
        => _entries.Add(new CompositionEntry { Kind = CompositionEntryKind.Child, Child = child });

    internal void PushClipRect(Rect rect) => PushScope(new CompositionEntry
    {
        Kind = CompositionEntryKind.ClipRect,
        Rect = rect,
    });

    internal void PushClipRoundedRect(Rect rect, double radiusX, double radiusY) => PushScope(new CompositionEntry
    {
        Kind = CompositionEntryKind.ClipRoundedRect,
        Rect = rect,
        RadiusX = radiusX,
        RadiusY = radiusY,
    });

    internal void PushTransform(Matrix3x2 matrix, bool disablesTextPixelSnap = false) => PushScope(new CompositionEntry
    {
        Kind = CompositionEntryKind.Transform,
        Matrix = matrix,
        DisablesTextPixelSnap = disablesTextPixelSnap,
    });

    /// <summary>Opens a scope that draws what it encloses with its global alpha multiplied by <paramref name="alpha"/>.</summary>
    internal void PushAlpha(double alpha) => PushScope(new CompositionEntry
    {
        Kind = CompositionEntryKind.Alpha,
        Alpha = alpha,
    });

    /// <summary>Opens a scope whose one child roots a frame; the next entry has to be that child.</summary>
    internal void PushFrame() => PushScope(new CompositionEntry
    {
        Kind = CompositionEntryKind.Frame,
    });

    internal void Pop()
    {
        if (_openScopes.Count == 0)
        {
            throw new InvalidOperationException("The composition closes a scope that was never opened.");
        }

        int scopeIndex = _openScopes[^1];
        _openScopes.RemoveAt(_openScopes.Count - 1);
        _entries[scopeIndex] = _entries[scopeIndex] with { ScopeLength = _entries.Count - scopeIndex - 1 };
    }

    /// <summary>
    /// Completes the declaration. When it matches <paramref name="previous"/> that instance is
    /// returned unchanged, so an unchanged composition allocates nothing and reports no dirty region.
    /// </summary>
    internal CompositionPlan Build(CompositionPlan? previous = null)
    {
        if (_openScopes.Count != 0)
        {
            throw new InvalidOperationException("The composition leaves a scope open.");
        }

        if (previous != null && Matches(previous))
        {
            Reset();
            return previous;
        }

        var plan = _entries.Count == 0
            ? CompositionPlan.Empty
            : new CompositionPlan([.. _entries], _slotCount);
        Reset();
        return plan;
    }

    private bool Matches(CompositionPlan plan)
    {
        if (plan.SlotCount != _slotCount || plan.Entries.Length != _entries.Count)
        {
            return false;
        }

        for (int index = 0; index < _entries.Count; index++)
        {
            var built = _entries[index];
            var existing = plan.Entries[index];
            if (built.Kind != existing.Kind ||
                built.SlotIndex != existing.SlotIndex ||
                !ReferenceEquals(built.Child, existing.Child) ||
                built.ScopeLength != existing.ScopeLength ||
                built.Rect != existing.Rect ||
                built.RadiusX != existing.RadiusX ||
                built.RadiusY != existing.RadiusY ||
                built.Matrix != existing.Matrix ||
                built.DisablesTextPixelSnap != existing.DisablesTextPixelSnap ||
                built.Alpha != existing.Alpha)
            {
                return false;
            }
        }

        return true;
    }

    internal void Reset()
    {
        _entries.Clear();
        _openScopes.Clear();
        _slotCount = 0;
    }

    private void PushScope(CompositionEntry entry)
    {
        _openScopes.Add(_entries.Count);
        _entries.Add(entry);
    }
}
