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

/// <summary>What one delta asks of the live UI.</summary>
internal sealed class HotReloadPlan
{
    internal List<HotReloadReaction> Reactions { get; } = new();

    /// <summary>Types whose live instances only need layout and rendering again (framework callbacks changed).</summary>
    internal List<Type> RepaintTypes { get; } = new();

    /// <summary>Changed methods that run only from event handlers and take effect on the next call.</summary>
    internal int HandlerOnlyChanges { get; set; }

    /// <summary>Why every registered build is re-run, or null when the changes were all attributed.</summary>
    internal string? RebuildAllReason { get; set; }
}

/// <summary>
/// Classifies a delta. A change a registered build runs rebuilds that build's node; an override the
/// framework calls (rendering, layout, input) repaints the instances of its type; code that only event
/// handlers run needs nothing, since the next call runs the new body. Anything else cannot be traced
/// to the UI it affects, so every registered build is re-run, as Blazor and Flutter do for every delta.
/// </summary>
internal static class HotReloadPlanner
{
    internal static HotReloadPlan Plan(List<HotReloadEntry> entries, HotReloadReachability.Changes changes, HotReloadReachability reachability)
    {
        var plan = new HotReloadPlan();
        if (changes.IsEmpty)
        {
            return plan;
        }

        var reached = new HashSet<HotReloadReachability.MethodKey>();
        foreach (var entry in entries)
        {
            if (entry.Owner.TryGetTarget(out var owner) && reachability.CollectReached(entry.Method, changes.Methods, reached))
            {
                plan.Reactions.Add(new HotReloadReaction(owner, KindOf(entry)));
            }
        }

        string? unattributed = null;
        foreach (var key in changes.Methods)
        {
            if (reached.Contains(key))
            {
                continue;
            }

            var callbackOwner = reachability.FindFrameworkCallbackOwner(key);
            if (callbackOwner != null)
            {
                AddDistinct(plan.RepaintTypes, callbackOwner);
            }
            else if (reachability.IsHandlerCode(key))
            {
                plan.HandlerOnlyChanges++;
            }
            else
            {
                unattributed ??= $"{reachability.Describe(key)} is not run by any registered build or event handler";
            }
        }

        foreach (var type in changes.UnrecordedTypes)
        {
            if (typeof(Element).IsAssignableFrom(type))
            {
                AddDistinct(plan.RepaintTypes, type);
            }
            unattributed ??= $"{type.FullName} changed outside the code registered builds and handlers run";
        }

        if (changes.UnknownScope)
        {
            unattributed ??= "the runtime did not report which types changed";
        }

        if (unattributed != null)
        {
            plan.RebuildAllReason = unattributed;
            plan.Reactions.Clear();
            foreach (var entry in entries)
            {
                if (entry.Owner.TryGetTarget(out var owner))
                {
                    plan.Reactions.Add(new HotReloadReaction(owner, KindOf(entry)));
                }
            }
        }

        return plan;
    }

    private static HotReloadReactionKind KindOf(HotReloadEntry entry) => entry.Role == HotReloadRole.Template
        ? HotReloadReactionKind.RefreshTemplate
        : HotReloadReactionKind.RebuildNode;

    private static void AddDistinct(List<Type> types, Type type)
    {
        if (!types.Contains(type))
        {
            types.Add(type);
        }
    }
}
