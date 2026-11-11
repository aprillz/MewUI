namespace Aprillz.MewUI.Controls;

/// <summary>The pieces the built-in item templates share.</summary>
internal static class DefaultItemTemplates
{
    /// <summary>
    /// The template a presenter holds until its owner supplies one: a label showing the item index.
    /// </summary>
    public static IDataTemplate IndexPlaceholder { get; } = new DelegateTemplate<object?>(
        build: static _ => new TextBlock(),
        bind: static (view, _, index, _) =>
        {
            if (view is TextBlock label)
            {
                label.Text = index.ToString();
            }
        });

    /// <summary>Gives a default item label its text and the font of the control that owns the items.</summary>
    public static void SyncTextAndFont(TextBlock label, string text, Control owner)
    {
        if (label.Text != text)
        {
            label.Text = text;
        }

        if (label.FontFamily != owner.FontFamily)
        {
            label.FontFamily = owner.FontFamily;
        }

        if (!label.FontSize.Equals(owner.FontSize))
        {
            label.FontSize = owner.FontSize;
        }

        if (label.FontWeight != owner.FontWeight)
        {
            label.FontWeight = owner.FontWeight;
        }
    }

    /// <summary>
    /// Gives a recycled item label a color of its own, or with <see langword="null"/> lets it inherit the
    /// foreground of its container.
    /// </summary>
    public static void SyncForeground(TextBlock label, Color? color)
    {
        if (color.HasValue)
        {
            if (label.Foreground != color.Value)
            {
                label.Foreground = color.Value;
            }
        }
        else if (label.PropertyStore.HasValue(TextElement.ForegroundProperty.Id, ValueSource.Local))
        {
            // Framework bookkeeping rather than a user clear, so nothing goes to the binding diagnostics.
            label.RestoreLocalValue(TextElement.ForegroundProperty, hadLocal: false, value: null);
        }
    }
}
