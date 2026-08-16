using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Replaces ranges of the document with elements that draw themselves. The scan protocol matches
/// AvalonEdit: the builder asks each generator where it wants to act, then asks the winner to build.
/// </summary>
/// <summary>
/// A generator the editor attaches on its own, which the editor keeps in step with the options it
/// was attached for. A generator built by hand carries its own settings and never sees this.
/// </summary>
internal interface IBuiltinElementGenerator
{
    void FetchOptions(TextEditorOptions options);
}

public abstract class VisualLineElementGenerator
{
    protected ITextRunConstructionContext? CurrentContext { get; private set; }

    public virtual void StartGeneration(ITextRunConstructionContext context)
        => CurrentContext = context ?? throw new ArgumentNullException(nameof(context));

    public virtual void FinishGeneration() => CurrentContext = null;

    /// <summary>First offset at or after <paramref name="startOffset"/> this generator wants, or -1.</summary>
    public abstract int GetFirstInterestedOffset(int startOffset);

    /// <summary>Builds the element at <paramref name="offset"/>, or null to decline.</summary>
    public abstract VisualLineElement? ConstructElement(int offset);
}

/// <summary>
/// Range of document text a generator only decorates: the text keeps every caret position and the
/// element contributes nothing but its <see cref="VisualLineElement.TextRunProperties"/>.
/// </summary>
public class VisualLineText : VisualLineElement
{
    public VisualLineText(int documentLength) : base(Math.Max(1, documentLength), documentLength)
    {
    }

    protected internal sealed override bool ReplacesText => false;
}

/// <summary>Draws replacement text in place of the document range it covers.</summary>
public class TextReplacementElement : VisualLineElement
{
    private readonly TextRunStyle _style;

    /// <param name="text">Text drawn in place of the document range.</param>
    /// <param name="documentLength">Length of the document range this element stands in for.</param>
    /// <param name="style">Resolved when the element is built; generation context is gone by the time it draws.</param>
    public TextReplacementElement(string text, int documentLength, TextRunStyle style)
        : base(Math.Max(1, text?.Length ?? 0), documentLength)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        _style = style;
    }

    public string Text { get; }

    /// <summary>The replacement text is what occupies the visual surface.</summary>
    protected internal override string GetVisualText() => Text;

    public override InlineMetrics Measure(uint dpi)
    {
        var layout = CreateLayout(dpi);
        return new InlineMetrics(layout.MeasuredSize.Width, layout.MeasuredSize.Height, layout.Lines[0].Baseline);
    }

    public override void Draw(ITextRenderContext context, Point origin, uint dpi)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = new TextDrawOptions(Foreground ?? TextRunProperties.ForegroundBrush ?? Color.FromRgb(0, 0, 0));
        context.Draw(CreateLayout(dpi), origin, in options);
    }

    private ITextLayout CreateLayout(uint dpi)
    {
        var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        return factory.TextEngine.GetOrCreateLayout(
            new TextLayoutRequest
            {
                Text = Text.AsMemory(),
                // Measuring at 96 while the view lays out at the real density clips the tail.
                Dpi = dpi,
                DefaultStyle = _style,
                Paragraph = new TextParagraphStyle { Wrapping = TextWrapping.NoWrap, MaxWidth = double.PositiveInfinity }
            },
            TextLayoutCachePolicy.Content);
    }
}
