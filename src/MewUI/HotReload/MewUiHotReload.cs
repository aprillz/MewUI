using System.Reflection.Metadata;

using Aprillz.MewUI.Controls;

// MewUI declares the Hot Reload handler in its own assembly, so apps that reference
// MewUI get Hot Reload with no per-app declaration (MewUI.dll is always loaded).
// The handler bodies are gated by MetadataUpdater.IsSupported, so this is inert
// outside a Hot Reload session and in release/AOT.
[assembly: MetadataUpdateHandler(typeof(Aprillz.MewUI.HotReload.MewUiMetadataUpdateHandler))]

namespace Aprillz.MewUI.HotReload;

/// <summary>
/// Minimal opt-in Hot Reload bridge for apps using C# markup.
/// Apps register a rebuild callback, and a MetadataUpdateHandler can request a UI reload.
/// </summary>
public static class MewUiHotReload
{
    private static readonly DispatcherMergeKey mergeKey = new(DispatcherPriority.Background);
    private static readonly object _pendingLock = new();
    private static readonly HashSet<Type> _pendingTypes = new();

    // A null updatedTypes from the runtime means "unknown scope" (any type may have changed),
    // which is sticky across accumulated deltas until the next drain.
    private static bool _pendingUnknownScope;
    private static bool reloading;

    public static bool IsEnabled
    {
        get
        {
            return Application.IsRunning && Application.Current.Dispatcher != null;
        }
    }

    /// <summary>
    /// Accumulates a Hot Reload delta and schedules a coalesced UI-thread reload.
    /// </summary>
    /// <param name="updatedTypes">Changed types from the runtime; <see langword="null"/> means unknown scope.</param>
    public static void RequestReload(Type[]? updatedTypes)
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

        // Observers may react to deltas the IL-hash filter above ignores (it only checks
        // registered build methods), so this fires even when zero nodes were rebuilt.
        DeltaApplied?.Invoke();
    }

    /// <summary>
    /// Raised on the UI thread after each Hot Reload delta drain, regardless of whether any
    /// registered node was rebuilt.
    /// </summary>
    internal static event Action? DeltaApplied;

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
/// </summary>
public static class MewUiMetadataUpdateHandler
{
    public static void ClearCache(Type[]? updatedTypes)
    {
        if (!HotReloadGate.IsActive)
        {
            return;
        }

        // Drop instantiated default styles so edited style factories re-run on the next lookup.
        // Runs before UpdateApplication, so the subsequent rebuild resolves fresh styles.
        DefaultStyles.ClearInstantiatedStyles();
    }

    public static void UpdateApplication(Type[]? updatedTypes)
    {
        if (!HotReloadGate.IsActive)
        {
            return;
        }

        MewUiHotReload.RequestReload(updatedTypes);
    }
}
