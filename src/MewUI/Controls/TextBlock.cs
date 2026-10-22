using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Lightweight text element (WPF-like) that does not carry full <see cref="Control"/> features.
/// Inherits <see cref="TextElement.ForegroundProperty"/>, <see cref="TextElement.FontFamilyProperty"/>,
/// <see cref="TextElement.FontSizeProperty"/>, and <see cref="TextElement.FontWeightProperty"/> so that
/// inherited values propagate naturally from parent controls without style-target interference.
/// </summary>
public partial class TextBlock : TextBlockBase
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<TextBlock>(nameof(Text), string.Empty,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextChanged());

    private InlineCollection? _inlines;

    /// <summary>
    /// Gets or sets the text content; setting it replaces <see cref="Inlines"/>, and while runs
    /// are present the value is their concatenated text.
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set
        {
            _inlines?.Clear();
            SetValue(TextProperty, value ?? string.Empty);
        }
    }

    /// <summary>
    /// Gets the styled runs that make up the content. Runs take precedence over a directly assigned
    /// <see cref="Text"/>, which then reports their concatenated text.
    /// </summary>
    public InlineCollection Inlines => _inlines ??= new InlineCollection(OnInlinesChanged);

    protected override string DisplayText => Text;

    protected virtual void OnTextChanged() => InvalidateTextLayout();

    protected override void OnGetTextGeometryRuns(in TextRunStyle defaultStyle, IList<GeometryStyleRun> output)
    {
        if (_inlines is null || _inlines.Count == 0)
        {
            return;
        }

        int offset = 0;
        foreach (var run in _inlines)
        {
            int length = run.Text.Length;
            if (length > 0 && !run.ResolveStyle(defaultStyle).Equals(defaultStyle))
            {
                output.Add(new GeometryStyleRun(offset, length, run.ResolveStyle(defaultStyle)));
            }
            offset += length;
        }
    }

    protected override void OnGetTextPaintSpans(IList<TextPaintSpan> output)
    {
        if (_inlines is null || _inlines.Count == 0)
        {
            return;
        }

        int offset = 0;
        foreach (var run in _inlines)
        {
            int length = run.Text.Length;
            if (length > 0 && (run.Foreground is not null || run.Background is not null))
            {
                output.Add(new TextPaintSpan(
                    new TextRange(offset, length),
                    run.Foreground,
                    run.Background));
            }
            offset += length;
        }
    }

    private void OnInlinesChanged(RunChange change)
    {
        if (change == RunChange.Paint)
        {
            InvalidateVisual();
            return;
        }

        if (change == RunChange.Text)
        {
            // Text stays the single source the layout measures; the runs only style ranges of it.
            SetValue(TextProperty, FlattenInlines());
            return;
        }

        InvalidateTextLayout();
        InvalidateMeasure();
    }

    private string FlattenInlines()
    {
        if (_inlines is null || _inlines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder();
        foreach (var run in _inlines)
        {
            builder.Append(run.Text);
        }
        return builder.ToString();
    }
}
