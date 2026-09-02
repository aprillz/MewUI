namespace Aprillz.MewUI;

/// <summary>
/// Maps key gestures to commands (or local callbacks) for one context level; keyboard dispatch
/// consults maps from the focused element outward and the nearest map claiming a gesture wins.
/// </summary>
/// <remarks>
/// A command's first mapped gesture is its primary/display gesture; the rest are alternative
/// execution gestures. Mapping a command again with the same data replaces those gestures, and
/// mapping a gesture already claimed by another entry moves that gesture to the new entry (runtime
/// remap semantics). Mutation is a UI-thread operation.
/// </remarks>
public sealed class InputMap
{
    private Dictionary<KeyGesture, InputMapEntry>? _byResolvedGesture;
    private Dictionary<Command, List<KeyGesture>>? _gesturesByCommand;

    /// <summary>
    /// Raised when the map's gesture semantics change, so shortcut presentation can re-resolve.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Maps the given gestures to a command; the first gesture becomes the primary/display gesture.
    /// </summary>
    public InputMap Map(Command command, KeyGesture primaryGesture, params KeyGesture[] alternativeGestures)
        => Map(command, data: null, primaryGesture, alternativeGestures);

    /// <summary>
    /// Maps the given gestures to a command invoked with <paramref name="data"/> as its argument, so
    /// one command can answer to several gestures that differ only in the value they pass.
    /// </summary>
    public InputMap Map(Command command, object? data, KeyGesture primaryGesture, params KeyGesture[] alternativeGestures)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(alternativeGestures);
        ValidateGesture(primaryGesture);
        foreach (var gesture in alternativeGestures)
        {
            ValidateGesture(gesture);
        }

        RemoveCommandCore(command, data);

        var gestures = new List<KeyGesture>(1 + alternativeGestures.Length) { primaryGesture };
        foreach (var gesture in alternativeGestures)
        {
            if (!gestures.Contains(gesture))
            {
                gestures.Add(gesture);
            }
        }

        var byGesture = _byResolvedGesture ??= new Dictionary<KeyGesture, InputMapEntry>(capacity: 4);
        var byCommand = _gesturesByCommand ??= new Dictionary<Command, List<KeyGesture>>(capacity: 4);

        var entry = new InputMapEntry(command, data);
        foreach (var gesture in gestures)
        {
            ClaimGesture(gesture);
            byGesture[gesture.Resolve()] = entry;
        }

        // Gestures mapped with other data stay, so the command's list accumulates across values.
        if (byCommand.TryGetValue(command, out var existing))
        {
            existing.AddRange(gestures);
        }
        else
        {
            byCommand[command] = gestures;
        }

        Changed?.Invoke();
        return this;
    }

    /// <summary>
    /// Maps a gesture to a local callback that is not part of command presentation.
    /// </summary>
    public InputMap Map(KeyGesture gesture, Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ValidateGesture(gesture);

        ClaimGesture(gesture);

        var byGesture = _byResolvedGesture ??= new Dictionary<KeyGesture, InputMapEntry>(capacity: 4);
        byGesture[gesture.Resolve()] = new InputMapEntry(execute, canExecute);
        Changed?.Invoke();
        return this;
    }

    /// <summary>
    /// Removes all gestures mapped to the command; returns false when none were mapped.
    /// </summary>
    public bool Unmap(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!RemoveCommandCore(command))
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Removes the mapping for a single gesture; returns false when the gesture was not mapped.
    /// </summary>
    public bool Unmap(KeyGesture gesture)
    {
        if (!ClaimGesture(gesture))
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Gets the command's primary gesture in this map only (no hierarchy resolution).
    /// </summary>
    public bool TryGetPrimaryGesture(Command command, out KeyGesture gesture)
        => TryGetPrimaryGesture(command, data: null, out gesture);

    /// <summary>
    /// Gets the primary gesture of the command's mapping for <paramref name="data"/> in this map only.
    /// </summary>
    public bool TryGetPrimaryGesture(Command command, object? data, out KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_gesturesByCommand != null && _gesturesByCommand.TryGetValue(command, out var gestures))
        {
            foreach (var candidate in gestures)
            {
                if (TryGetEntry(candidate.Resolve(), out var entry) && Equals(entry.Data, data))
                {
                    gesture = candidate;
                    return true;
                }
            }
        }

        gesture = default;
        return false;
    }

    /// <summary>
    /// Removes every mapping.
    /// </summary>
    public void Clear()
    {
        bool hadEntries = _byResolvedGesture != null && _byResolvedGesture.Count > 0;
        _byResolvedGesture?.Clear();
        _gesturesByCommand?.Clear();
        if (hadEntries)
        {
            Changed?.Invoke();
        }
    }

    internal bool TryGetEntry(KeyGesture resolvedGesture, out InputMapEntry entry)
    {
        if (_byResolvedGesture != null && _byResolvedGesture.TryGetValue(resolvedGesture, out var found))
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    internal IReadOnlyList<KeyGesture>? GetGestures(Command command)
        => _gesturesByCommand != null && _gesturesByCommand.TryGetValue(command, out var gestures) ? gestures : null;

    internal bool IsEmpty => _byResolvedGesture == null || _byResolvedGesture.Count == 0;

    /// <summary>
    /// Removes any existing entry for the gesture (runtime remap steals it from its previous
    /// owner); returns whether an entry was removed.
    /// </summary>
    private bool ClaimGesture(KeyGesture gesture)
    {
        if (_byResolvedGesture == null)
        {
            return false;
        }

        var resolved = gesture.Resolve();
        if (!_byResolvedGesture.Remove(resolved, out var previous))
        {
            return false;
        }

        if (previous.Command is Command previousCommand &&
            _gesturesByCommand != null &&
            _gesturesByCommand.TryGetValue(previousCommand, out var gestures))
        {
            gestures.RemoveAll(candidate => candidate.Resolve() == resolved);
            if (gestures.Count == 0)
            {
                _gesturesByCommand.Remove(previousCommand);
            }
        }

        return true;
    }

    private bool RemoveCommandCore(Command command)
    {
        if (_gesturesByCommand == null || !_gesturesByCommand.Remove(command, out var gestures))
        {
            return false;
        }

        if (_byResolvedGesture != null)
        {
            foreach (var gesture in gestures)
            {
                _byResolvedGesture.Remove(gesture.Resolve());
            }
        }

        return true;
    }

    /// <summary>
    /// Removes only the command's gestures mapped with <paramref name="data"/>; returns whether any were.
    /// </summary>
    private bool RemoveCommandCore(Command command, object? data)
    {
        if (_gesturesByCommand == null || !_gesturesByCommand.TryGetValue(command, out var gestures))
        {
            return false;
        }

        bool removed = false;
        for (int i = gestures.Count - 1; i >= 0; i--)
        {
            var resolved = gestures[i].Resolve();
            if (_byResolvedGesture != null &&
                _byResolvedGesture.TryGetValue(resolved, out var entry) &&
                Equals(entry.Data, data))
            {
                _byResolvedGesture.Remove(resolved);
                gestures.RemoveAt(i);
                removed = true;
            }
        }

        if (gestures.Count == 0)
        {
            _gesturesByCommand.Remove(command);
        }

        return removed;
    }

    private static void ValidateGesture(KeyGesture gesture)
    {
        if (gesture.Key == Key.None)
        {
            throw new ArgumentException("A key gesture must specify a key.", nameof(gesture));
        }
    }
}

/// <summary>
/// One gesture mapping: either a command reference or a local callback pair.
/// </summary>
internal sealed class InputMapEntry
{
    public InputMapEntry(Command command, object? data)
    {
        Command = command;
        Data = data;
    }

    public InputMapEntry(Action callback, Func<bool>? canExecute)
    {
        Callback = callback;
        CallbackCanExecute = canExecute;
    }

    public Command? Command { get; }

    /// <summary>The argument the command is invoked with, or null to resolve one from the focus chain.</summary>
    public object? Data { get; }

    public Action? Callback { get; }

    public Func<bool>? CallbackCanExecute { get; }
}
