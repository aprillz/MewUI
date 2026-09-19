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

    /// <summary>Number of entries this scope encloses; 0 for non-scope entries.</summary>
    internal int ScopeLength { get; init; }
}

/// <summary>
/// Ordered composition of one visual. Holds no drawing commands: content slots and children are
/// referenced, never inlined.
/// </summary>
internal sealed class CompositionPlan
{
    internal static CompositionPlan Empty { get; } = new([], 0, false);

    internal CompositionPlan(CompositionEntry[] entries, int slotCount, bool isUnsupported)
    {
        Entries = entries;
        SlotCount = slotCount;
        IsUnsupported = isUnsupported;
    }

    internal CompositionEntry[] Entries { get; }

    internal int SlotCount { get; }

    /// <summary>True when the declaration named something the scene cannot own as a node.</summary>
    internal bool IsUnsupported { get; }
}

/// <summary>
/// Collects the entries of one visual's composition. Reused across visuals within a pass.
/// </summary>
internal sealed class CompositionPlanBuilder
{
    private readonly List<CompositionEntry> _entries = [];
    private readonly List<int> _openScopes = [];
    private int _slotCount;
    private bool _unsupported;

    internal void Content(int slotIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        _entries.Add(new CompositionEntry { Kind = CompositionEntryKind.Content, SlotIndex = slotIndex });
        _slotCount = Math.Max(_slotCount, slotIndex + 1);
    }

    internal void Child(Element? child)
    {
        if (child == null)
        {
            return;
        }

        if (child is not UIElement visual)
        {
            _unsupported = true;
            return;
        }

        _entries.Add(new CompositionEntry { Kind = CompositionEntryKind.Child, Child = visual });
    }

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

    internal void PushTransform(Matrix3x2 matrix) => PushScope(new CompositionEntry
    {
        Kind = CompositionEntryKind.Transform,
        Matrix = matrix,
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
    /// returned unchanged, so an unchanged composition allocates nothing and reports no damage.
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

        var plan = _entries.Count == 0 && !_unsupported
            ? CompositionPlan.Empty
            : new CompositionPlan([.. _entries], _slotCount, _unsupported);
        Reset();
        return plan;
    }

    private bool Matches(CompositionPlan plan)
    {
        if (plan.SlotCount != _slotCount || plan.IsUnsupported != _unsupported || plan.Entries.Length != _entries.Count)
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
                built.Matrix != existing.Matrix)
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
        _unsupported = false;
    }

    private void PushScope(CompositionEntry entry)
    {
        _openScopes.Add(_entries.Count);
        _entries.Add(entry);
    }
}
