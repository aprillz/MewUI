namespace Aprillz.MewUI;

internal readonly record struct PropertyValueCandidateTrace(
    ValueSource Source,
    bool IsSet,
    bool IsWinner,
    object? RawValue);

#if DEBUG
internal enum StyleCascadeLayer
{
    FrameworkDefault,
    Application,
}

internal readonly record struct StyleCascadeEntryTrace(
    Style DeclaringStyle,
    StateTrigger? Trigger,
    StyleCascadeLayer Layer,
    bool IsNewlyInherited,
    bool IsActive,
    bool IsUnset,
    bool HasResolvedValue,
    object? ResolvedValue,
    bool IsFinal,
    bool IsWinner);

internal readonly record struct StyleCascadeTrace(
    MewProperty Property,
    IReadOnlyList<StyleCascadeEntryTrace> Entries,
    bool HasStyleCandidate,
    object? StyleValue,
    ValueSource EffectiveSource,
    bool IsAnimated)
{
    public bool IsStyleEffective
        => HasStyleCandidate && EffectiveSource == ValueSource.Style;

    public StyleCascadeEntryTrace? FinalEntry
    {
        get
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].IsFinal)
                {
                    return Entries[i];
                }
            }

            return null;
        }
    }
}
#endif

internal readonly record struct PropertyValueTrace(
    MewProperty Property,
    object? BaseValue,
    object? VisualValue,
    ValueSource EffectiveSource,
    bool IsAnimated,
    PropertyValueCandidateTrace Local,
    PropertyValueCandidateTrace ElementTrigger,
    PropertyValueCandidateTrace Binding,
    PropertyValueCandidateTrace Style,
    PropertyValueCandidateTrace Inherited,
    PropertyValueCandidateTrace Default,
    BindingStateSnapshot? BindingState)
{
    public bool HasNonDefaultCandidate
        => Local.IsSet || ElementTrigger.IsSet || Binding.IsSet || Style.IsSet || Inherited.IsSet;

    public PropertyValueCandidateTrace GetCandidate(ValueSource source)
        => source switch
        {
            ValueSource.Local => Local,
            ValueSource.ElementTrigger => ElementTrigger,
            ValueSource.Binding => Binding,
            ValueSource.Style => Style,
            ValueSource.Inherited => Inherited,
            ValueSource.Default => Default,
            _ => new PropertyValueCandidateTrace(source, false, false, null),
        };
}
