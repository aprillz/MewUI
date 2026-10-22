namespace Aprillz.MewUI;

/// <summary>
/// Conditionally applies <see cref="SetterBase"/> values when the control's
/// <see cref="VisualStateFlags"/> match the specified criteria.
/// </summary>
public sealed class StateTrigger
{
    private IReadOnlyList<SetterBase>? _setters;

    /// <summary>
    /// Gets the flags that must all be present for this trigger to match; <see cref="VisualStateFlags.None"/>
    /// when only <see cref="Exclude"/> matters.
    /// </summary>
    public VisualStateFlags Match { get; init; }

    /// <summary>
    /// Gets the flags that must all be absent for this trigger to match (for example <c>Exclude = Enabled</c>
    /// matches the disabled state).
    /// </summary>
    public VisualStateFlags Exclude { get; init; }

    /// <summary>
    /// Gets the setters applied while this trigger matches; may contain <see cref="Setter"/> and
    /// <see cref="UnsetSetter"/> declarations.
    /// </summary>
    public required IReadOnlyList<SetterBase> Setters
    {
        get => _setters!;
        init => _setters = value;
    }

    /// <summary>
    /// Returns whether this trigger matches the given flags.
    /// </summary>
    public bool Matches(VisualStateFlags flags)
        => (flags & Match) == Match && (flags & Exclude) == 0;

    internal SetterBase[] SnapshotSetters()
    {
        if (_setters == null)
        {
            throw new InvalidOperationException("StateTrigger.Setters cannot be null.");
        }

        var snapshot = _setters.ToArray();
        _setters = snapshot;
        return snapshot;
    }
}

/// <summary>
/// Visual state flags that a <see cref="StateTrigger"/> matches against.
/// </summary>
[Flags]
public enum VisualStateFlags : uint
{
    /// <summary>No state.</summary>
    None = 0,

    // Tier 1 - common (all Controls)
    /// <summary>The control is effectively enabled.</summary>
    Enabled = 1 << 0,
    /// <summary>The mouse is over the control or the control has captured it.</summary>
    Hot = 1 << 1,
    /// <summary>The control has focus or contains the focused element.</summary>
    Focused = 1 << 2,
    /// <summary>A mouse button or the activation key is held down.</summary>
    Pressed = 1 << 3,

    // Tier 2 - toggle (ToggleBase family)
    /// <summary>The toggle is in the on (checked) state.</summary>
    Checked = 1 << 4,
    /// <summary>The check box is in the indeterminate (null) state.</summary>
    Indeterminate = 1 << 5,

    // Tier 3 - control-specific opt-in
    /// <summary>A sub-element is open or active (drop-down, expander).</summary>
    Active = 1 << 6,
    /// <summary>The item is selected (tab, list item).</summary>
    Selected = 1 << 7,
    /// <summary>The input is read-only.</summary>
    ReadOnly = 1 << 8,

    // Tier 4 - semantic state projected from framework services
    /// <summary>One or more bindings on the control currently have an error.</summary>
    Invalid = 1 << 9,

    // Bits 10-31: reserved for future framework extension
}
