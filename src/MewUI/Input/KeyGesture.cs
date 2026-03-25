namespace Aprillz.MewUI;

/// <summary>
/// Represents a keyboard shortcut as a Key + ModifierKeys combination.
/// <see cref="ModifierKeys.Primary"/> is resolved to the platform's command modifier at matching/display time.
/// </summary>
public readonly record struct KeyGesture(Key Key, ModifierKeys Modifiers = ModifierKeys.None)
{
    /// <summary>
    /// Returns true if this gesture matches the given key event.
    /// Resolves <see cref="ModifierKeys.Primary"/> to the platform modifier before comparing.
    /// </summary>
    public bool Matches(KeyEventArgs e)
        => e.Key == Key && e.Modifiers == ResolveModifiers(Modifiers);

    /// <summary>
    /// Returns a platform-appropriate display string using <see cref="PlatformKeyConfiguration.Current"/>.
    /// </summary>
    public string ToDisplayString()
        => PlatformKeyConfiguration.Current.FormatGesture(Resolve());

    /// <inheritdoc/>
    public override string ToString() => ToDisplayString();

    /// <summary>
    /// Returns a new KeyGesture with <see cref="ModifierKeys.Primary"/> resolved to the actual platform modifier.
    /// </summary>
    internal KeyGesture Resolve()
        => new(Key, ResolveModifiers(Modifiers));

    private static ModifierKeys ResolveModifiers(ModifierKeys m)
    {
        if ((m & ModifierKeys.Primary) != 0)
            return (m & ~ModifierKeys.Primary) | PlatformKeyConfiguration.Current.PrimaryModifier;
        return m;
    }
}
