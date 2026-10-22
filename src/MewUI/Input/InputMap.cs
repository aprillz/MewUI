namespace Aprillz.MewUI;

/// <summary>
/// Maps key gestures and key sequences to commands (or local callbacks) for one context level; keyboard dispatch
/// consults maps from the focused element outward and the nearest map claiming a gesture wins.
/// </summary>
/// <remarks>
/// A command's first mapped sequence is its primary/display sequence; the rest are alternative
/// execution sequences. Mapping a command again with the same data replaces those sequences, and
/// mapping a sequence already claimed by another entry moves it to the new entry (runtime
/// remap semantics). Within one map a single gesture runs before any longer sequence that starts with it,
/// so that longer sequence is unreachable there. Mutation is a UI-thread operation.
/// </remarks>
public sealed class InputMap
{
    private Dictionary<KeySequence, InputMapEntry>? _byResolvedSequence;
    private Dictionary<Command, List<KeySequence>>? _sequencesByCommand;

    // Every proper prefix of a mapped sequence of two or more strokes; rebuilt on the first lookup after a change.
    private HashSet<KeySequence>? _resolvedPrefixes;
    private bool _prefixesStale;

    /// <summary>
    /// Occurs when the map's gesture semantics change, so shortcut presentation can re-resolve.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Maps the given sequences to a command; the first becomes the primary/display sequence.
    /// </summary>
    public InputMap Map(Command command, KeySequence primarySequence, params KeySequence[] alternativeSequences)
        => Map(command, data: null, primarySequence, alternativeSequences);

    /// <summary>
    /// Maps the given sequences to a command invoked with <paramref name="data"/> as its argument, so
    /// one command can answer to several sequences that differ only in the value they pass.
    /// </summary>
    public InputMap Map(Command command, object? data, KeySequence primarySequence, params KeySequence[] alternativeSequences)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(alternativeSequences);
        ValidateSequence(primarySequence, nameof(primarySequence));
        foreach (var sequence in alternativeSequences)
        {
            ValidateSequence(sequence, nameof(alternativeSequences));
        }

        RemoveCommandCore(command, data);

        var sequences = new List<KeySequence>(1 + alternativeSequences.Length) { primarySequence };
        foreach (var sequence in alternativeSequences)
        {
            if (!sequences.Contains(sequence))
            {
                sequences.Add(sequence);
            }
        }

        var bySequence = _byResolvedSequence ??= new Dictionary<KeySequence, InputMapEntry>(capacity: 4);
        var byCommand = _sequencesByCommand ??= new Dictionary<Command, List<KeySequence>>(capacity: 4);

        var entry = new InputMapEntry(command, data);
        foreach (var sequence in sequences)
        {
            ClaimSequence(sequence);
            bySequence[sequence.Resolve()] = entry;
        }

        // Sequences mapped with other data stay, so the command's list accumulates across values.
        if (byCommand.TryGetValue(command, out var existing))
        {
            existing.AddRange(sequences);
        }
        else
        {
            byCommand[command] = sequences;
        }

        OnChanged();
        return this;
    }

    /// <summary>
    /// Maps a sequence to a local callback that is not part of command presentation.
    /// </summary>
    public InputMap Map(KeySequence sequence, Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ValidateSequence(sequence, nameof(sequence));

        ClaimSequence(sequence);

        var bySequence = _byResolvedSequence ??= new Dictionary<KeySequence, InputMapEntry>(capacity: 4);
        bySequence[sequence.Resolve()] = new InputMapEntry(execute, canExecute);
        OnChanged();
        return this;
    }

    /// <summary>
    /// Removes all sequences mapped to the command; returns false when none were mapped.
    /// </summary>
    public bool Unmap(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!RemoveCommandCore(command))
        {
            return false;
        }

        OnChanged();
        return true;
    }

    /// <summary>
    /// Removes the mapping for a single sequence; returns false when it was not mapped.
    /// </summary>
    public bool Unmap(KeySequence sequence)
    {
        if (sequence.Length == 0 || !ClaimSequence(sequence))
        {
            return false;
        }

        OnChanged();
        return true;
    }

    /// <summary>
    /// Gets the command's primary gesture in this map only (no hierarchy resolution); false when the command is not
    /// mapped or its primary mapping is a sequence of more than one stroke.
    /// </summary>
    public bool TryGetPrimaryGesture(Command command, out KeyGesture gesture)
        => TryGetPrimaryGesture(command, data: null, out gesture);

    /// <summary>
    /// Gets the primary gesture of the command's mapping for <paramref name="data"/> in this map only; false when
    /// there is none or it is a sequence of more than one stroke.
    /// </summary>
    public bool TryGetPrimaryGesture(Command command, object? data, out KeyGesture gesture)
    {
        if (TryGetPrimarySequence(command, data, out var sequence) && sequence.IsSingle)
        {
            gesture = sequence[0];
            return true;
        }

        gesture = default;
        return false;
    }

    /// <summary>
    /// Gets the command's primary sequence in this map only (no hierarchy resolution).
    /// </summary>
    public bool TryGetPrimarySequence(Command command, out KeySequence sequence)
        => TryGetPrimarySequence(command, data: null, out sequence);

    /// <summary>
    /// Gets the primary sequence of the command's mapping for <paramref name="data"/> in this map only.
    /// </summary>
    public bool TryGetPrimarySequence(Command command, object? data, out KeySequence sequence)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_sequencesByCommand != null && _sequencesByCommand.TryGetValue(command, out var sequences))
        {
            foreach (var candidate in sequences)
            {
                if (TryGetEntry(candidate.Resolve(), out var entry) && Equals(entry.Data, data))
                {
                    sequence = candidate;
                    return true;
                }
            }
        }

        sequence = default;
        return false;
    }

    /// <summary>
    /// Removes every mapping.
    /// </summary>
    public void Clear()
    {
        bool hadEntries = _byResolvedSequence != null && _byResolvedSequence.Count > 0;
        _byResolvedSequence?.Clear();
        _sequencesByCommand?.Clear();
        if (hadEntries)
        {
            OnChanged();
        }
    }

    internal bool TryGetEntry(KeySequence resolvedSequence, out InputMapEntry entry)
    {
        if (_byResolvedSequence != null && _byResolvedSequence.TryGetValue(resolvedSequence, out var found))
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>
    /// What the strokes pressed so far mean to this map: a mapping, the start of a longer one, or nothing.
    /// </summary>
    internal InputMapMatch Match(KeySequence resolvedSequence, out InputMapEntry entry)
    {
        if (TryGetEntry(resolvedSequence, out entry))
        {
            return InputMapMatch.Exact;
        }

        return IsPrefix(resolvedSequence) ? InputMapMatch.Prefix : InputMapMatch.None;
    }

    internal IReadOnlyList<KeySequence>? GetSequences(Command command)
        => _sequencesByCommand != null && _sequencesByCommand.TryGetValue(command, out var sequences) ? sequences : null;

    internal bool IsEmpty => _byResolvedSequence == null || _byResolvedSequence.Count == 0;

    private bool IsPrefix(KeySequence resolvedSequence)
    {
        if (_prefixesStale)
        {
            _prefixesStale = false;
            _resolvedPrefixes?.Clear();
            if (_byResolvedSequence != null)
            {
                foreach (var mapped in _byResolvedSequence.Keys)
                {
                    for (int count = 1; count < mapped.Length; count++)
                    {
                        (_resolvedPrefixes ??= new HashSet<KeySequence>()).Add(mapped.Prefix(count));
                    }
                }
            }
        }

        return _resolvedPrefixes != null && _resolvedPrefixes.Contains(resolvedSequence);
    }

    private void OnChanged()
    {
        _prefixesStale = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// Removes any existing entry for the sequence (runtime remap steals it from its previous
    /// owner); returns whether an entry was removed.
    /// </summary>
    private bool ClaimSequence(KeySequence sequence)
    {
        if (_byResolvedSequence == null)
        {
            return false;
        }

        var resolved = sequence.Resolve();
        if (!_byResolvedSequence.Remove(resolved, out var previous))
        {
            return false;
        }

        if (previous.Command is Command previousCommand &&
            _sequencesByCommand != null &&
            _sequencesByCommand.TryGetValue(previousCommand, out var sequences))
        {
            sequences.RemoveAll(candidate => candidate.Resolve() == resolved);
            if (sequences.Count == 0)
            {
                _sequencesByCommand.Remove(previousCommand);
            }
        }

        return true;
    }

    private bool RemoveCommandCore(Command command)
    {
        if (_sequencesByCommand == null || !_sequencesByCommand.Remove(command, out var sequences))
        {
            return false;
        }

        if (_byResolvedSequence != null)
        {
            foreach (var sequence in sequences)
            {
                _byResolvedSequence.Remove(sequence.Resolve());
            }
        }

        return true;
    }

    /// <summary>
    /// Removes only the command's sequences mapped with <paramref name="data"/>; returns whether any were.
    /// </summary>
    private bool RemoveCommandCore(Command command, object? data)
    {
        if (_sequencesByCommand == null || !_sequencesByCommand.TryGetValue(command, out var sequences))
        {
            return false;
        }

        bool removed = false;
        for (int index = sequences.Count - 1; index >= 0; index--)
        {
            var resolved = sequences[index].Resolve();
            if (_byResolvedSequence != null &&
                _byResolvedSequence.TryGetValue(resolved, out var entry) &&
                Equals(entry.Data, data))
            {
                _byResolvedSequence.Remove(resolved);
                sequences.RemoveAt(index);
                removed = true;
            }
        }

        if (sequences.Count == 0)
        {
            _sequencesByCommand.Remove(command);
        }

        return removed;
    }

    private static void ValidateSequence(KeySequence sequence, string parameterName)
    {
        if (sequence.Length == 0)
        {
            throw new ArgumentException("A key sequence must have at least one stroke.", parameterName);
        }
    }
}

/// <summary>What a map makes of the strokes pressed so far.</summary>
internal enum InputMapMatch
{
    None,
    Exact,
    Prefix,
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
