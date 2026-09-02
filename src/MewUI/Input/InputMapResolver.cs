using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Resolves key gestures through the input-map hierarchy (focused chain, fallback-target chain,
/// window, application) and provides the matching reverse lookup for shortcut display.
/// </summary>
/// <remarks>
/// The nearest map claiming a gesture shadows farther maps even when the mapped command turns out
/// to be unavailable; gesture meaning and command availability are deliberately separate.
/// </remarks>
internal static class InputMapResolver
{
    // Defensive bound for context-parent walks; mirrors CommandRouter's guard.
    private const int MAX_CHAIN_LENGTH = 256;

    // UI-thread scratch for reverse lookup enumeration; never retained past a call.
    private static readonly List<InputMap> _effectiveLookupScratch = new();

    /// <summary>
    /// Dispatches an unhandled KeyDown through the input-map hierarchy; marks the event handled
    /// only when a callback or command handler actually ran.
    /// </summary>
    public static bool TryDispatchKeyDown(Window window, KeyEventArgs args)
    {
        if (args.Handled)
        {
            return false;
        }

        var origin = ResolveFocusedOrigin(window);
        var lookupGesture = new KeyGesture(args.Key, args.Modifiers);
        if (!TryResolveEntry(window, origin, lookupGesture, out var entry))
        {
            return false;
        }

        if (entry.Callback != null)
        {
            if (entry.CallbackCanExecute != null && !entry.CallbackCanExecute())
            {
                // The gesture stays claimed (no farther-map fallback); the key itself keeps bubbling.
                return false;
            }

            entry.Callback();
            args.Handled = true;
            return true;
        }

        var router = window.CommandRouter;
        var target = router.CaptureTarget();
        bool executed = entry.Data is object data
            ? router.TryExecuteFromInput(entry.Command!, target, origin, data)
            : router.TryExecuteFromInput(entry.Command!, target, origin);
        if (executed)
        {
            args.Handled = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The display string for the gesture the given command would actually respond to, or
    /// <see langword="null"/> when it answers to none. Every surface that labels a shortcut reads it from
    /// here, so a menu row and a tooltip never disagree about what the key is. A surface that passes
    /// data finds the gesture mapped with that same data.
    /// </summary>
    public static string? GetEffectiveGestureText(Window window, Command command, Element? origin, object? data = null)
        => TryGetEffectiveGesture(window, command, origin, data, out var gesture) ? gesture.ToDisplayString() : null;

    /// <summary>
    /// Finds the gesture the given command would actually respond to for the origin context,
    /// consistent with forward key resolution; used for menu shortcut labels.
    /// </summary>
    public static bool TryGetEffectiveGesture(Window window, Command command, Element? origin, out KeyGesture gesture)
        => TryGetEffectiveGesture(window, command, origin, data: null, out gesture);

    /// <summary>
    /// Finds the gesture the command mapped with <paramref name="data"/> would actually respond to.
    /// </summary>
    public static bool TryGetEffectiveGesture(Window window, Command command, Element? origin, object? data, out KeyGesture gesture)
    {
        var maps = _effectiveLookupScratch;
        maps.Clear();
        CollectChainMaps(window, origin, maps);

        var fallbackOrigin = window.TryGetCommandRouter()?.FallbackTarget?.OriginElement;
        if (fallbackOrigin != null && !ReferenceEquals(fallbackOrigin, origin))
        {
            CollectChainMaps(window, fallbackOrigin, maps);
        }

        if (window.TryGetInputMap() is InputMap windowMap)
        {
            maps.Add(windowMap);
        }

        if (Application.CurrentInputMapOrNull is InputMap applicationMap)
        {
            maps.Add(applicationMap);
        }

        foreach (var map in maps)
        {
            var gestures = map.GetGestures(command);
            if (gestures == null)
            {
                continue;
            }

            foreach (var candidate in gestures)
            {
                if (TryResolveEntry(window, origin, candidate.Resolve(), out var entry) &&
                    ReferenceEquals(entry.Command, command) &&
                    Equals(entry.Data, data))
                {
                    maps.Clear();
                    gesture = candidate;
                    return true;
                }
            }
        }

        maps.Clear();
        gesture = default;
        return false;
    }

    internal static bool TryResolveEntry(Window window, Element? origin, KeyGesture resolvedGesture, out InputMapEntry entry)
    {
        if (TryResolveFromChain(window, origin, resolvedGesture, out entry))
        {
            return true;
        }

        var fallbackOrigin = window.TryGetCommandRouter()?.FallbackTarget?.OriginElement;
        if (fallbackOrigin != null &&
            !ReferenceEquals(fallbackOrigin, origin) &&
            TryResolveFromChain(window, fallbackOrigin, resolvedGesture, out entry))
        {
            return true;
        }

        if (window.TryGetInputMap() is InputMap windowMap && windowMap.TryGetEntry(resolvedGesture, out entry))
        {
            return true;
        }

        if (Application.CurrentInputMapOrNull is InputMap applicationMap && applicationMap.TryGetEntry(resolvedGesture, out entry))
        {
            return true;
        }

        entry = null!;
        return false;
    }

    private static bool TryResolveFromChain(Window window, Element? origin, KeyGesture resolvedGesture, out InputMapEntry entry)
    {
        int steps = 0;
        for (Element? current = origin; current != null && steps < MAX_CHAIN_LENGTH; current = current.ContextParent, steps++)
        {
            // The window's own map is a later, separate resolution stage (after the fallback chain).
            if (ReferenceEquals(current, window))
            {
                break;
            }

            if (current.TryGetInputMap() is InputMap map && map.TryGetEntry(resolvedGesture, out entry))
            {
                return true;
            }
        }

        entry = null!;
        return false;
    }

    private static void CollectChainMaps(Window window, Element? origin, List<InputMap> maps)
    {
        int steps = 0;
        for (Element? current = origin; current != null && steps < MAX_CHAIN_LENGTH; current = current.ContextParent, steps++)
        {
            if (ReferenceEquals(current, window))
            {
                break;
            }

            if (current.TryGetInputMap() is InputMap map && !maps.Contains(map))
            {
                maps.Add(map);
            }
        }
    }

    private static UIElement? ResolveFocusedOrigin(Window window)
    {
        var focused = window.FocusManager.FocusedElement;
        if (focused != null && !ReferenceEquals(focused.FindVisualRoot(), window))
        {
            focused = null;
        }

        return focused;
    }
}
