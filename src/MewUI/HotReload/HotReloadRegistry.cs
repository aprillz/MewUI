using System.Reflection;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.HotReload;

internal enum HotReloadRole
{
    Build,
    Template,
}

/// <summary>
/// One tracked UI-producing method (a build method or a template build function) with the
/// element it belongs to and the last known normalized-IL hash.
/// </summary>
internal sealed class HotReloadEntry
{
    public required MethodBase Method { get; init; }

    public required HotReloadRole Role { get; init; }

    public required WeakReference<Element> Owner { get; init; }

    public byte[]? Baseline { get; set; }
}

/// <summary>
/// Debug-only registry of UI-producing methods. Event handlers are intentionally NOT registered,
/// so a handler edit changes no registered method and triggers no reaction.
/// </summary>
internal static class HotReloadRegistry
{
    private static readonly List<HotReloadEntry> _entries = new();
    private static readonly object _lock = new();

    public static void RegisterBuild(Element owner, MethodBase? method) => Register(owner, method, HotReloadRole.Build);

    public static void RegisterBuild(Element owner, Delegate? source) => Register(owner, source?.Method, HotReloadRole.Build);

    public static void RegisterTemplate(Element owner, Delegate? build) => Register(owner, build?.Method, HotReloadRole.Template);

    /// <summary>Registers a UserControl's overriding <c>OnBuild</c> method as its build source.</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Reached only under Hot Reload (JIT/debug); trimmed out of AOT/trimmed builds.")]
    public static void RegisterUserControl(UserControl owner)
    {
        if (!HotReloadGate.IsActive)
        {
            return;
        }

        var method = owner.GetType().GetMethod("OnBuild", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (method != null && method.DeclaringType != typeof(UserControl))
        {
            Register(owner, method, HotReloadRole.Build);
        }
    }

    /// <summary>Registers a Window's overriding <c>OnBuild</c> hook as its build source.</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "The rebuild paths that consume this (Hot Reload, preview) only run in JIT sessions; never reached in AOT/trimmed builds.")]
    internal static void RegisterWindow(Window owner)
    {
        // Both consumers of the hook flag are JIT-session features (Hot Reload rebuilds and
        // preview RefreshTarget); plain production runs skip the reflection entirely, which
        // also keeps this unreachable in AOT/trimmed builds.
        bool hotReloadActive = HotReloadGate.IsActive;
        if (!hotReloadActive && !Design.IsPreviewMode)
        {
            return;
        }

        var method = owner.GetType().GetMethod("OnBuild", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null || method.DeclaringType == typeof(Window))
        {
            return;
        }

        // The flag must not depend on the Hot Reload gate alone: preview rebuilds need it even
        // in projects that opted out of Hot Reload. Only the IL tracking is gate-scoped.
        owner.HasBuildHookRegistered = true;

        if (hotReloadActive)
        {
            Register(owner, method, HotReloadRole.Build);
        }
    }

    private static void Register(Element owner, MethodBase? method, HotReloadRole role)
    {
        if (method == null || !HotReloadGate.IsActive)
        {
            return;
        }

        lock (_lock)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var existing = _entries[i];
                if (existing.Role == role && existing.Method == method
                    && existing.Owner.TryGetTarget(out var target) && ReferenceEquals(target, owner))
                {
                    return;
                }
            }

            _entries.Add(new HotReloadEntry
            {
                Method = method,
                Role = role,
                Owner = new WeakReference<Element>(owner),
                Baseline = IlNormalizer.Hash(method),
            });
        }
    }

    /// <summary>Returns the live entries, dropping any whose owner has been collected.</summary>
    public static List<HotReloadEntry> SnapshotAndSweep()
    {
        lock (_lock)
        {
            _entries.RemoveAll(static entry => !entry.Owner.TryGetTarget(out _));
            return new List<HotReloadEntry>(_entries);
        }
    }
}
