using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>Displays lightweight inline markup as formatted text.</summary>
public sealed partial class MarkupTextBlock : TextBlockBase
{
    /// <summary>Identifies the <see cref="Markup"/> property.</summary>
    public static readonly MewProperty<string> MarkupProperty =
        MewProperty<string>.Register<MarkupTextBlock>(nameof(Markup), string.Empty,
            MewPropertyOptions.AffectsLayout,
            static (self, _, value) => self.ApplyMarkup(value));

    private static readonly MewPropertyKey<string> _textPropertyKey =
        MewProperty<string>.RegisterReadOnly<MarkupTextBlock>(nameof(Text), string.Empty);

    /// <summary>Identifies the read-only <see cref="Text"/> property.</summary>
    public static readonly MewProperty<string> TextProperty = _textPropertyKey.Property;

    /// <summary>Identifies the <see cref="Options"/> property.</summary>
    public static readonly MewProperty<TextMarkupOptions> OptionsProperty =
        MewProperty<TextMarkupOptions>.Register<MarkupTextBlock>(nameof(Options), TextMarkupOptions.Default,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.InvalidateTextLayout(),
            static (_, value) => value ?? TextMarkupOptions.Default);

    private MarkupTextDocument _document = MarkupTextDocument.Empty;

    /// <summary>Gets or sets the source markup.</summary>
    public string Markup
    {
        get => GetValue(MarkupProperty);
        set => SetValue(MarkupProperty, value ?? string.Empty);
    }

    /// <summary>Gets the decoded plain text displayed by the control.</summary>
    public string Text => GetValue(TextProperty);

    /// <summary>Gets or sets markup presentation options.</summary>
    public TextMarkupOptions Options
    {
        get => GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value ?? TextMarkupOptions.Default);
    }

    protected override string DisplayText => Text;

    protected override void OnGetTextGeometryRuns(in TextRunStyle defaultStyle, IList<GeometryStyleRun> output)
        => _document.AppendGeometryRuns(defaultStyle, Options, output);

    protected override void OnGetTextPaintSpans(IList<TextPaintSpan> output)
        => _document.AppendPaintSpans(output);

    private void ApplyMarkup(string markup)
    {
        _document = MarkupTextParser.Parse(markup);
        SetValue(_textPropertyKey, _document.Text);
        InvalidateTextLayout();
    }
}
