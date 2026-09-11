using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A styled span of text inside <see cref="TextBlock.Inlines"/>. Unset properties fall back to the
/// owning <see cref="TextBlock"/>, so a run only states what it overrides.
/// </summary>
public sealed class Run : MewObject
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<Run>(nameof(Text), string.Empty,
            changed: static (self, _, _) => self.NotifyChanged(RunChange.Text));

    public static readonly MewProperty<string?> FontFamilyProperty =
        MewProperty<string?>.Register<Run>(nameof(FontFamily), null,
            changed: static (self, _, _) => self.NotifyChanged(RunChange.Layout));

    public static readonly MewProperty<double?> FontSizeProperty =
        MewProperty<double?>.Register<Run>(nameof(FontSize), null,
            changed: static (self, _, _) => self.NotifyChanged(RunChange.Layout));

    public static readonly MewProperty<FontWeight?> FontWeightProperty =
        MewProperty<FontWeight?>.Register<Run>(nameof(FontWeight), null,
            changed: static (self, _, _) => self.NotifyChanged(RunChange.Layout));

    public static readonly MewProperty<bool> ItalicProperty =
        MewProperty<bool>.Register<Run>(nameof(Italic), false,
            changed: static (self, _, _) => self.NotifyChanged(RunChange.Layout));

    public static readonly MewProperty<TextDecoration> DecorationProperty =
        MewProperty<TextDecoration>.Register<Run>(nameof(Decoration), TextDecoration.None,
            changed: static (self, _, _) => self.NotifyChanged(RunChange.Layout));

    public static readonly MewProperty<double> BaselineOffsetProperty =
        MewProperty<double>.Register<Run>(nameof(BaselineOffset), 0,
            changed: static (self, _, _) => self.NotifyChanged(RunChange.Layout));

    public static readonly MewProperty<Color?> ForegroundProperty =
        MewProperty<Color?>.Register<Run>(nameof(Foreground), null,
            changed: static (self, _, _) => self.NotifyChanged(RunChange.Paint));

    public static readonly MewProperty<Color?> BackgroundProperty =
        MewProperty<Color?>.Register<Run>(nameof(Background), null,
            changed: static (self, _, _) => self.NotifyChanged(RunChange.Paint));

    public Run()
    {
    }

    public Run(string text) => Text = text;

    /// <summary>Gets or sets the run text.</summary>
    public string Text
    {
        get => GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>Gets or sets the font family; null inherits from the owning text element.</summary>
    public string? FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>Gets or sets the font size in points; null inherits from the owning text element.</summary>
    public double? FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>Gets or sets the font weight; null inherits from the owning text element.</summary>
    public FontWeight? FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>Gets or sets whether the run renders italic.</summary>
    public bool Italic
    {
        get => GetValue(ItalicProperty);
        set => SetValue(ItalicProperty, value);
    }

    /// <summary>Gets or sets underline and strikethrough for the run.</summary>
    public TextDecoration Decoration
    {
        get => GetValue(DecorationProperty);
        set => SetValue(DecorationProperty, value);
    }

    /// <summary>Gets or sets the baseline shift in DIPs; positive raises the run above the line's baseline.</summary>
    public double BaselineOffset
    {
        get => GetValue(BaselineOffsetProperty);
        set => SetValue(BaselineOffsetProperty, value);
    }

    /// <summary>Gets or sets the text color; null inherits from the owning text element.</summary>
    public Color? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Gets or sets the highlight color painted behind the run; null paints nothing.</summary>
    public Color? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    internal event Action<Run, RunChange>? Changed;

    /// <summary>Resolves the geometry style, filling unset values from the owner's style.</summary>
    internal TextRunStyle ResolveStyle(in TextRunStyle owner)
        => owner with
        {
            FontFamily = FontFamily ?? owner.FontFamily,
            FontSize = FontSize ?? owner.FontSize,
            Weight = FontWeight ?? owner.Weight,
            Italic = Italic,
            Decoration = Decoration,
            BaselineOffset = BaselineOffset
        };

    private void NotifyChanged(RunChange change) => Changed?.Invoke(this, change);
}

internal enum RunChange
{
    /// <summary>The text changed, so the owner's flattened string is stale.</summary>
    Text,

    /// <summary>A font or decoration value changed, so the layout must be rebuilt.</summary>
    Layout,

    /// <summary>Only a paint color changed, so the existing layout can be repainted.</summary>
    Paint
}
