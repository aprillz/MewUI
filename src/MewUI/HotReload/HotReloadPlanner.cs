using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.HotReload;

internal enum HotReloadReactionKind
{
    RebuildNode,
    RefreshTemplate,
}

internal readonly struct HotReloadReaction
{
    internal HotReloadReaction(Element owner, HotReloadReactionKind kind)
    {
        Owner = owner;
        Kind = kind;
    }

    internal Element Owner { get; }

    internal HotReloadReactionKind Kind { get; }
}

/// <summary>
/// Pure change classifier: recomputes the normalized hash of each registered method, emits a
/// reaction for those whose body actually changed, and advances the per-entry baseline (epoch)
/// so an unrelated later delta does not re-trigger the same node.
/// </summary>
internal static class HotReloadPlanner
{
    internal static List<HotReloadReaction> Plan(List<HotReloadEntry> entries)
    {
        var reactions = new List<HotReloadReaction>();

        foreach (var entry in entries)
        {
            if (!entry.Owner.TryGetTarget(out var owner))
            {
                continue;
            }

            var current = IlNormalizer.Hash(entry.Method);
            bool changed = !BytesEqual(entry.Baseline, current);
            entry.Baseline = current;

            if (!changed)
            {
                continue;
            }

            var kind = entry.Role == HotReloadRole.Template
                ? HotReloadReactionKind.RefreshTemplate
                : HotReloadReactionKind.RebuildNode;
            reactions.Add(new HotReloadReaction(owner, kind));
        }

        return reactions;
    }

    private static bool BytesEqual(byte[]? left, byte[]? right)
    {
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        return left.AsSpan().SequenceEqual(right);
    }
}
