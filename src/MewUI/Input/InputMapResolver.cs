using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Resolves key sequences through the input-map hierarchy (focused chain, fallback-target chain,
/// window, application) and provides the matching reverse lookup for shortcut display.
/// </summary>
/// <remarks>
/// The nearest map claiming a sequence shadows farther maps even when the mapped command turns out
/// to be unavailable; gesture meaning and command availability are deliberately separate. A map
/// claims the strokes pressed so far either as a mapping or as the start of a longer one.
/// </remarks>
internal static class InputMapResolver
{
    // Defensive bound for context-parent walks; mirrors CommandRouter's guard.
    private const int MAX_CHAIN_LENGTH = 256;

    // UI-thread scratch for reverse lookup enumeration; never retained past a call.
    private static readonly List<InputMap> _effectiveLookupScratch = new();

    /// <summary>
    /// Dispatches an unhandled KeyDown through the input-map hierarchy; marks the event handled
    /// only when a callback or command handler actually ran, or when the key started a longer sequence.
    /// </summary>
    public static bool TryDispatchKeyDown(Window window, KeyEventArgs args)
    {
        if (args.Handled || args.Key == Key.None)
        {
            return false;
        }

        var origin = ResolveFocusedOrigin(window);
        var stroke = new KeySequence(new KeyGesture(args.Key, args.Modifiers));
        var match = Match(window, origin, stroke, out var entry);
        if (match == InputMapMatch.Exact)
        {
            return TryExecute(window, origin, entry, args);
        }

        if (match == InputMapMatch.Prefix)
        {
            window.SetPendingKeySequence(stroke);
            args.Handled = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gives a KeyDown to the key sequence in progress, if there is one; the key then completes, extends or cancels
    /// it and never reaches the focused element.
    /// </summary>
    public static bool TryContinueKeySequence(Window window, KeyEventArgs args)
    {
        // A modifier pressed on the way to the next stroke is not a stroke.
        if (args.Handled || args.Key == Key.None || !window.TryGetPendingKeySequence(out var pending))
        {
            return false;
        }

        args.Handled = true;
        if (args.Key == Key.Escape && args.Modifiers == ModifierKeys.None)
        {
            window.ClearPendingKeySequence();
            return true;
        }

        var origin = ResolveFocusedOrigin(window);
        var candidate = pending.Append(new KeyGesture(args.Key, args.Modifiers));
        var match = Match(window, origin, candidate, out var entry);
        if (match == InputMapMatch.Prefix && candidate.Length < KeySequence.MAX_LENGTH)
        {
            window.SetPendingKeySequence(candidate);
        }
        else
        {
            window.ClearPendingKeySequence();
            if (match == InputMapMatch.Exact)
            {
                TryExecute(window, origin, entry, args);
                args.Handled = true;
            }
        }

        return true;
    }

    /// <summary>
    /// The display string for the gesture or sequence the given command would actually respond to, or
    /// <see langword="null"/> when it answers to none. Every surface that labels a shortcut reads it from
    /// here, so a menu row and a tooltip never disagree about what the key is. A surface that passes
    /// data finds the mapping made with that same data.
    /// </summary>
    public static string? GetEffectiveGestureText(Window window, Command command, Element? origin, object? data = null)
        => TryGetEffectiveSequence(window, command, origin, data, out var sequence) ? sequence.ToDisplayString() : null;

    /// <summary>
    /// Finds the single gesture the given command would actually respond to for the origin context,
    /// consistent with forward key resolution; false when it answers only to a longer sequence.
    /// </summary>
    public static bool TryGetEffectiveGesture(Window window, Command command, Element? origin, out KeyGesture gesture)
        => TryGetEffectiveGesture(window, command, origin, data: null, out gesture);

    /// <summary>
    /// Finds the single gesture the command mapped with <paramref name="data"/> would actually respond to.
    /// </summary>
    public static bool TryGetEffectiveGesture(Window window, Command command, Element? origin, object? data, out KeyGesture gesture)
    {
        if (TryGetEffectiveSequence(window, command, origin, data, out var sequence) && sequence.IsSingle)
        {
            gesture = sequence[0];
            return true;
        }

        gesture = default;
        return false;
    }

    /// <summary>
    /// Finds the sequence the command mapped with <paramref name="data"/> would actually respond to for the origin
    /// context, consistent with forward key resolution; used for menu shortcut labels.
    /// </summary>
    public static bool TryGetEffectiveSequence(Window window, Command command, Element? origin, object? data, out KeySequence sequence)
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
            var sequences = map.GetSequences(command);
            if (sequences == null)
            {
                continue;
            }

            foreach (var candidate in sequences)
            {
                if (TryResolveEntry(window, origin, candidate.Resolve(), out var entry) &&
                    ReferenceEquals(entry.Command, command) &&
                    Equals(entry.Data, data))
                {
                    maps.Clear();
                    sequence = candidate;
                    return true;
                }
            }
        }

        maps.Clear();
        sequence = default;
        return false;
    }

    /// <summary>
    /// Finds the single gesture the command mapped with <paramref name="data"/> answers to in the application input
    /// map alone, for a menu outside every window (a tray or dock menu).
    /// </summary>
    internal static bool TryGetApplicationGesture(Command command, object? data, out KeyGesture gesture)
    {
        var applicationMap = Application.CurrentInputMapOrNull;
        var sequences = applicationMap?.GetSequences(command);
        if (applicationMap != null && sequences != null)
        {
            foreach (var candidate in sequences)
            {
                var resolved = candidate.Resolve();
                if (resolved.IsSingle &&
                    applicationMap.Match(resolved, out var entry) == InputMapMatch.Exact &&
                    ReferenceEquals(entry.Command, command) &&
                    Equals(entry.Data, data))
                {
                    gesture = resolved[0];
                    return true;
                }
            }
        }

        gesture = default;
        return false;
    }

    /// <summary>
    /// The mapping that pressing the whole sequence reaches: every stroke before the last has to be taken as the start
    /// of a longer sequence, which a nearer map mapping those strokes to something else prevents.
    /// </summary>
    internal static bool TryResolveEntry(Window window, Element? origin, KeySequence resolvedSequence, out InputMapEntry entry)
    {
        for (int count = 1; count < resolvedSequence.Length; count++)
        {
            if (Match(window, origin, resolvedSequence.Prefix(count), out entry) != InputMapMatch.Prefix)
            {
                entry = null!;
                return false;
            }
        }

        if (Match(window, origin, resolvedSequence, out entry) == InputMapMatch.Exact)
        {
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>What the nearest map that claims the strokes makes of them.</summary>
    private static InputMapMatch Match(Window window, Element? origin, KeySequence resolvedSequence, out InputMapEntry entry)
    {
        var match = MatchInChain(window, origin, resolvedSequence, out entry);
        if (match != InputMapMatch.None)
        {
            return match;
        }

        var fallbackOrigin = window.TryGetCommandRouter()?.FallbackTarget?.OriginElement;
        if (fallbackOrigin != null && !ReferenceEquals(fallbackOrigin, origin))
        {
            match = MatchInChain(window, fallbackOrigin, resolvedSequence, out entry);
            if (match != InputMapMatch.None)
            {
                return match;
            }
        }

        if (window.TryGetInputMap() is InputMap windowMap)
        {
            match = windowMap.Match(resolvedSequence, out entry);
            if (match != InputMapMatch.None)
            {
                return match;
            }
        }

        if (Application.CurrentInputMapOrNull is InputMap applicationMap)
        {
            return applicationMap.Match(resolvedSequence, out entry);
        }

        entry = null!;
        return InputMapMatch.None;
    }

    private static InputMapMatch MatchInChain(Window window, Element? origin, KeySequence resolvedSequence, out InputMapEntry entry)
    {
        int steps = 0;
        for (Element? current = origin; current != null && steps < MAX_CHAIN_LENGTH; current = current.ContextParent, steps++)
        {
            // The window's own map is a later, separate resolution stage (after the fallback chain).
            if (ReferenceEquals(current, window))
            {
                break;
            }

            if (current.TryGetInputMap() is InputMap map)
            {
                var match = map.Match(resolvedSequence, out entry);
                if (match != InputMapMatch.None)
                {
                    return match;
                }
            }
        }

        entry = null!;
        return InputMapMatch.None;
    }

    private static bool TryExecute(Window window, UIElement? origin, InputMapEntry entry, KeyEventArgs args)
    {
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
