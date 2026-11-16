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
    private static readonly DispatcherMergeKey _mergeKey = new(DispatcherPriority.Input);
    private static readonly object _pendingLock = new();
    private static readonly HashSet<Type> _pendingTypes = new();

    // A null updatedTypes from the runtime means "unknown scope" (any type may have changed),
    // which is sticky across accumulated deltas until the next drain.
    private static bool _pendingUnknownScope;
    private static readonly List<WeakReference<Element>> _failedOwners = new();
    private static bool _hasPending;
    private static bool _reloading;

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
                for (int index = 0; index < updatedTypes.Length; index++)
                {
                    _pendingTypes.Add(updatedTypes[index]);
                }
            }
            _hasPending = true;
        }

        ScheduleDrain();
    }

    private static void ScheduleDrain()
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher == null)
        {
            return;
        }

        // PostMerged coalesces only the execution count; the pending set above merges the payload,
        // so no delta's types are lost even when merged into a single run.
        (dispatcher as IDispatcherCore)?.PostMerged(_mergeKey, DrainOnUiThread, DispatcherPriority.Input);
    }

    private static void DrainOnUiThread()
    {
        if (_reloading || !Application.IsRunning)
        {
            // A build that pumps the dispatcher (a modal dialog) re-enters here; the pending delta
            // stays queued and is drained once the outer pass finishes.
            return;
        }

        Type[]? scope;
        lock (_pendingLock)
        {
            if (!_hasPending)
            {
                return;
            }
            scope = _pendingUnknownScope ? null : [.. _pendingTypes];
            _pendingTypes.Clear();
            _pendingUnknownScope = false;
            _hasPending = false;
        }

        List<Element> rebuilt;
        _reloading = true;
        try
        {
            Application.Current.InvalidateStyleCachesForHotReload();
            var reachability = HotReloadReachability.Shared;
            var changes = reachability.DetectChanges(scope);
            var entries = HotReloadRegistry.SnapshotAndSweep();
            var plan = HotReloadPlanner.Plan(entries, changes, reachability);
            rebuilt = ExecuteReactions(plan.Reactions);
            int repainted = Repaint(plan.RepaintTypes);
            Application.Current.RefreshStylesAfterHotReload();
            LogOutcome(scope, changes, plan, entries.Count, rebuilt.Count, repainted);
        }
        finally
        {
            _reloading = false;
        }

        lock (_pendingLock)
        {
            if (_hasPending)
            {
                ScheduleDrain();
            }
        }

        DeltaApplied?.Invoke(rebuilt);
    }

    /// <summary>
    /// Raised on the UI thread after each Hot Reload delta drain with the nodes that were rebuilt,
    /// which is empty when the delta touched no tracked build code.
    /// </summary>
    internal static event Action<IReadOnlyList<Element>>? DeltaApplied;

    private static void LogOutcome(Type[]? scope, HotReloadReachability.Changes changes, HotReloadPlan plan,
        int registeredBuilds, int rebuiltCount, int repaintedCount)
    {
        var message = $"scope={(scope == null ? "unknown" : scope.Length + " types")}, changed methods={changes.Methods.Count}"
            + $", rebuilt={rebuiltCount}, repainted={repaintedCount}, handler-only={plan.HandlerOnlyChanges}";
        if (plan.RebuildAllReason != null)
        {
            message += $"; re-ran every registered build because {plan.RebuildAllReason}";
            if (registeredBuilds == 0)
            {
                message += "; nothing is registered to re-run, so build windows with Window.Build(...) or an OnBuild() override";
            }
        }
        Log(message);
    }

    /// <summary>Re-measures and re-renders the live elements of <paramref name="types"/>; returns how many.</summary>
    private static int Repaint(List<Type> types)
    {
        if (types.Count == 0)
        {
            return 0;
        }

        int count = 0;
        var windows = Application.Current.AllWindows;
        for (int index = 0; index < windows.Count; index++)
        {
            VisualTree.Visit(windows[index], element =>
            {
                if (IsInstanceOfAny(element, types))
                {
                    element.InvalidateMeasure();
                    element.InvalidateVisual();
                    count++;
                }
            });
        }
        return count;
    }

    private static bool IsInstanceOfAny(Element element, List<Type> types)
    {
        for (Type? current = element.GetType(); current != null; current = current.BaseType)
        {
            var definition = current.IsGenericType ? current.GetGenericTypeDefinition() : current;
            if (types.Contains(definition))
            {
                return true;
            }
        }
        return false;
    }

    private static List<Element> ExecuteReactions(List<HotReloadReaction> reactions)
    {
        // Resolve each reaction to a rebuildable node. Template refresh currently falls back to
        // rebuilding the nearest rebuildable ancestor (granular item refresh is a follow-up).
        var targets = new List<Element>();
        for (int index = 0; index < reactions.Count; index++)
        {
            var reaction = reactions[index];
            Element? target = reaction.Kind == HotReloadReactionKind.RebuildNode
                ? reaction.Owner
                : NearestRebuildable(reaction.Owner);

            if (target != null && !targets.Contains(target))
            {
                targets.Add(target);
            }
        }

        // A node whose last rebuild threw keeps the old content; it is retried with the next delta,
        // which is usually the fix for the error.
        foreach (var failed in _failedOwners)
        {
            if (failed.TryGetTarget(out var owner) && !targets.Contains(owner))
            {
                targets.Add(owner);
            }
        }
        _failedOwners.Clear();

        // Shallowest-first: an ancestor rebuild detaches descendants, which we then skip.
        targets.Sort(static (left, right) => Depth(left).CompareTo(Depth(right)));

        var rebuilt = new List<Element>();
        for (int index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            if (!IsAttached(target))
            {
                continue;
            }

            try
            {
                Rebuild(target);
                rebuilt.Add(target);
            }
            catch (Exception ex)
            {
                // Isolate: one node's rebuild failure must not abort the others.
                _failedOwners.Add(new WeakReference<Element>(target));
                Log($"rebuild of {target.GetType().FullName} failed; retried with the next change: {ex}");
            }
        }

        return rebuilt;
    }

    private static void Rebuild(Element target)
    {
        if (target is Window window)
        {
            // Build ownership: the composition-site callback owns the build when set; otherwise
            // the window's virtual OnBuild hook does.
            if (window.BuildCallback is Action<Window> build)
            {
                build(window);
            }
            else if (window.HasBuildHookRegistered)
            {
                window.InvokeOnBuildHook();
            }
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
            if (current is Window window && (window.BuildCallback != null || window.HasBuildHookRegistered))
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
