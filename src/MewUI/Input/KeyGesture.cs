using System.Diagnostics.CodeAnalysis;

using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a keyboard shortcut as a <see cref="MewUI.Key"/> and <see cref="ModifierKeys"/> combination.
/// </summary>
/// <remarks>
/// <see cref="ModifierKeys.Primary"/> is resolved to the platform's command modifier at matching and display
/// time.
/// </remarks>
/// <param name="Key">The key.</param>
/// <param name="Modifiers">The modifier keys held with it.</param>
public readonly record struct KeyGesture(Key Key, ModifierKeys Modifiers = ModifierKeys.None)
{
    /// <summary>
    /// Returns whether this gesture matches the given key event, with <see cref="ModifierKeys.Primary"/>
    /// resolved to the platform modifier first.
    /// </summary>
    public bool Matches(KeyEventArgs e)
        => e.Key == Key && e.Modifiers == ResolveModifiers(Modifiers);

    /// <summary>
    /// Returns a platform-appropriate display string using <see cref="PlatformConventions.Current"/>.
    /// </summary>
    public string ToDisplayString()
        => PlatformConventions.Current.FormatGesture(Resolve());

    /// <inheritdoc/>
    public override string ToString() => ToDisplayString();

    /// <summary>
    /// Parses the portable form: modifiers (Ctrl, Shift, Alt, Meta, Primary) and a key joined by '+', where the key is
    /// a <see cref="Key"/> name or the character it types on a US keyboard. Example: "Ctrl+Shift+-".
    /// </summary>
    /// <exception cref="FormatException">The text is not a key gesture.</exception>
    public static KeyGesture Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!KeyGestureText.TryParseGesture(text, out var gesture))
        {
            throw new FormatException($"'{text}' is not a key gesture.");
        }

        return gesture;
    }

    /// <summary>Parses the portable form accepted by <see cref="Parse"/>; false when the text is not a key gesture.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out KeyGesture gesture)
    {
        if (text == null)
        {
            gesture = default;
            return false;
        }

        return KeyGestureText.TryParseGesture(text, out gesture);
    }

    /// <summary>
    /// Returns a new KeyGesture with <see cref="ModifierKeys.Primary"/> resolved to the actual platform modifier.
    /// </summary>
    internal KeyGesture Resolve()
        => new(Key, ResolveModifiers(Modifiers));

    private static ModifierKeys ResolveModifiers(ModifierKeys m)
    {
        if ((m & ModifierKeys.Primary) != 0)
            return (m & ~ModifierKeys.Primary) | PlatformConventions.Current.PrimaryModifier;
        return m;
    }
}
