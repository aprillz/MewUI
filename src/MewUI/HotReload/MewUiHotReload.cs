using System.Reflection.Metadata;

using Aprillz.MewUI.Controls;

// MewUI declares the Hot Reload handler in its own assembly, so apps that reference
// MewUI get Hot Reload with no per-app declaration (MewUI.dll is always loaded).
// The handler bodies are gated by MetadataUpdater.IsSupported, so this is inert
// outside a Hot Reload session and in release/AOT.
[assembly: MetadataUpdateHandler(typeof(Aprillz.MewUI.HotReload.MewUiMetadataUpdateHandler))]

namespace Aprillz.MewUI.HotReload;

/// <summary>
/// Coalesces Hot Reload deltas onto the UI thread and rebuilds the affected nodes.
/// </summary>
internal static class MewUiHotReload
{
    private static readonly DispatcherMergeKey mergeKey = new(DispatcherPriority.Background);
    private static readonly object _pendingLock = new();
    private static readonly HashSet<Type> _pendingTypes = new();

    // A null updatedTypes from the runtime means "unknown scope" (any type may have changed),
    // which is sticky across accumulated deltas until the next drain.
    private static bool _pendingUnknownScope;
    private static bool reloading;

    /// <summary>
    /// Accumulates a Hot Reload delta and schedules a coalesced UI-thread reload.
    /// </summary>
    /// <param name="updatedTypes">Changed types from the runtime; <see langword="null"/> means unknown scope.</param>
    internal static void RequestReload(Type[]? updatedTypes)
    {
        lock (_pendingLock)
        {
            if (updatedTypes == null)
            {
                _pendingUnknownScope = true;
            }
            else
            {
                for (int i = 0; i < updatedTypes.Length; i++)
                {
                    _pendingTypes.Add(updatedTypes[i]);
                }
            }
        }

        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher == null)
        {
            return;
        }

        // PostMerged coalesces only the execution count; the pending set above merges the payload,
        // so no delta's types are lost even when merged into a single run.
        (dispatcher as IDispatcherCore)?.PostMerged(mergeKey, DrainOnUiThread, DispatcherPriority.Input);
    }

    private static void DrainOnUiThread()
    {
        if (reloading)
        {
            return;
        }

        if (!Application.IsRunning)
        {
            return;
        }

        Type[]? scope;
        lock (_pendingLock)
        {
            scope = _pendingUnknownScope ? null : [.. _pendingTypes];
            _pendingTypes.Clear();
            _pendingUnknownScope = false;
        }

        reloading = true;
        try
        {
            var entries = HotReloadRegistry.SnapshotAndSweep();
            var reactions = HotReloadPlanner.Plan(entries);
            ExecuteReactions(reactions, scope);
        }
        finally
        {
            reloading = false;
        }
    }

    private static void ExecuteReactions(List<HotReloadReaction> reactions, Type[]? scope)
    {
        Log($"scope={(scope == null ? "unknown" : scope.Length + " types")}, reactions={reactions.Count}");

        // Resolve each reaction to a rebuildable node. Template refresh currently falls back to
        // rebuilding the nearest rebuildable ancestor (granular item refresh is a follow-up).
        var targets = new List<Element>();
        for (int i = 0; i < reactions.Count; i++)
        {
            var reaction = reactions[i];
            Element? target = reaction.Kind == HotReloadReactionKind.RebuildNode
                ? reaction.Owner
                : NearestRebuildable(reaction.Owner);

            if (target != null && !targets.Contains(target))
            {
                targets.Add(target);
            }
        }

        // Shallowest-first: an ancestor rebuild detaches descendants, which we then skip.
        targets.Sort(static (left, right) => Depth(left).CompareTo(Depth(right)));

        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (!IsAttached(target))
            {
                continue;
            }

            try
            {
                Rebuild(target);
            }
            catch
            {
                // Isolate: one node's rebuild failure must not abort the others.
            }
        }
    }

    private static void Rebuild(Element target)
    {
        if (target is Window window && window.BuildCallback is Action<Window> build)
        {
            build(window);
            return;
        }

        if (target is UserControl control && control.GetBuiltContent() is Element content)
        {
            control.Content = content;
        }
    }

    private static Element? NearestRebuildable(Element element)
    {
        for (Element? current = element; current != null; current = current.Parent)
        {
            if (current is Window window && window.BuildCallback != null)
            {
                return window;
            }

            if (current is UserControl)
            {
                return current;
            }
        }

        return null;
    }

    private static bool IsAttached(Element element)
    {
        Element root = element;
        while (root.Parent != null)
        {
            root = root.Parent;
        }

        if (root is not Window window)
        {
            return false;
        }

        var windows = Application.Current.AllWindows;
        for (int i = 0; i < windows.Count; i++)
        {
            if (ReferenceEquals(windows[i], window))
            {
                return true;
            }
        }

        return false;
    }

    private static int Depth(Element element)
    {
        int depth = 0;
        for (Element? current = element.Parent; current != null; current = current.Parent)
        {
            depth++;
        }

        return depth;
    }

    private static void Log(string message)
    {
        try
        {
            Console.Error.WriteLine($"[MewUI HotReload] {message}");
        }
        catch
        {
            // No console attached; diagnostics are best-effort.
        }
    }
}

/// <summary>
/// Runtime Hot Reload callback entrypoint. Registered automatically by MewUI; apps need no
/// declaration and can opt out with <c>&lt;MewUIHotReload&gt;false&lt;/MewUIHotReload&gt;</c>.
/// The runtime invokes these methods by reflection (including non-public), so the type and
/// members stay internal.
/// </summary>
internal static class MewUiMetadataUpdateHandler
{
    internal static void ClearCache(Type[]? updatedTypes)
    {
        if (!HotReloadGate.IsActive)
        {
            return;
        }

        // Drop instantiated default styles so edited style factories re-run on the next lookup.
        // Runs before UpdateApplication, so the subsequent rebuild resolves fresh styles.
        DefaultStyles.ClearInstantiatedStyles();
    }

    internal static void UpdateApplication(Type[]? updatedTypes)
    {
        if (!HotReloadGate.IsActive)
        {
            return;
        }

        MewUiHotReload.RequestReload(updatedTypes);
    }
}
