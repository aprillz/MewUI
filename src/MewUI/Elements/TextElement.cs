namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for elements that carry font and foreground text-color properties.
/// </summary>
public abstract class TextElement : FrameworkElement
{
    #region MewProperty Declarations

    /// <summary>Foreground (text) color property with inheritance support.</summary>
    public static readonly MewProperty<Color> ForegroundProperty =
        MewProperty<Color>.Register<TextElement>(nameof(Foreground), Color.Black,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.Inherits);

    /// <summary>Font family property with inheritance support.</summary>
    public static readonly MewProperty<string> FontFamilyProperty =
        MewProperty<string>.Register<TextElement>(nameof(FontFamily), "Segoe UI",
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.Inherits);

    /// <summary>Font size property with inheritance support.</summary>
    public static readonly MewProperty<double> FontSizeProperty =
        MewProperty<double>.Register<TextElement>(nameof(FontSize), 12.0,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.Inherits);

    /// <summary>Font weight property with inheritance support.</summary>
    public static readonly MewProperty<FontWeight> FontWeightProperty =
        MewProperty<FontWeight>.Register<TextElement>(nameof(FontWeight), FontWeight.Normal,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.Inherits);

    #endregion

    /// <summary>
    /// Gets or sets the foreground (text) color.
    /// </summary>
    public Color Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    public string FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the font weight.
    /// </summary>
    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }
}
