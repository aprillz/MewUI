using System.Numerics;

namespace Aprillz.MewUI;

/// <summary>
/// Conditionally applies <see cref="SetterBase"/> values when the control's
/// <see cref="VisualStateFlags"/> match the specified criteria.
/// </summary>
public sealed class StateTrigger
{
    /// <summary>
    /// Flags that must ALL be present for this trigger to match.
    /// Use <see cref="VisualStateFlags.None"/> when only <see cref="Exclude"/> matters.
    /// </summary>
    public VisualStateFlags Match { get; init; }

    /// <summary>
    /// Flags that must ALL be absent for this trigger to match.
    /// Common pattern: <c>Exclude = Enabled</c> to match disabled state.
    /// </summary>
    public VisualStateFlags Exclude { get; init; }

    /// <summary>
    /// Setter values to apply when this trigger matches.
    /// May contain both <see cref="Setter"/> and <see cref="TargetSetter"/>.
    /// </summary>
    public required IReadOnlyList<SetterBase> Setters { get; init; }

    /// <summary>
    /// Tests whether this trigger matches the given flags.
    /// </summary>
    public bool Matches(VisualStateFlags flags)
        => (flags & Match) == Match && (flags & Exclude) == 0;

    /// <summary>
    /// Specificity - number of bits set in <see cref="Match"/>.
    /// Higher specificity wins when multiple triggers match for the same property.
    /// Ties are broken by declaration order (later wins).
    /// </summary>
    public int Specificity => BitOperations.PopCount((uint)Match);
}

/// <summary>
/// Framework-defined visual state flags.
/// Public because <see cref="StateTrigger"/> (in Style definitions) references this type.
/// </summary>
[Flags]
public enum VisualStateFlags : uint
{
    None = 0,

    // Tier 1 - common (all Controls)
    /// <summary>Control is effectively enabled.</summary>
    Enabled = 1 << 0,
    /// <summary>Mouse is over or captured.</summary>
    Hot = 1 << 1,
    /// <summary>Control has focus or contains focused element.</summary>
    Focused = 1 << 2,
    /// <summary>Mouse button or activation key is held down.</summary>
    Pressed = 1 << 3,

    // Tier 2 - toggle (ToggleBase family)
    /// <summary>Toggle is in the on/checked state.</summary>
    Checked = 1 << 4,
    /// <summary>CheckBox three-state null value.</summary>
    Indeterminate = 1 << 5,

    // Tier 3 - control-specific opt-in
    /// <summary>Sub-element is open/active (dropdown, expander).</summary>
    Active = 1 << 6,
    /// <summary>Item is selected (tab, list item).</summary>
    Selected = 1 << 7,
    /// <summary>Input is read-only.</summary>
    ReadOnly = 1 << 8,

    // Bits 9–31: reserved for future framework extension
}
