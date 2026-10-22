using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.MewvalonEdit;

/// <summary>
/// Editing and display options of an editor. Every member is virtual so a host can force a value,
/// and every value is a <see cref="MewProperty"/> so it can be bound to.
/// </summary>
public class TextEditorOptions : MewObject
{
    /// <summary>The number of columns per indentation level; the default is 4.</summary>
    public static readonly MewProperty<int> IndentationSizeProperty =
        MewProperty<int>.Register<TextEditorOptions>(nameof(IndentationSize), 4);

    /// <summary>Whether indentation is written as spaces instead of tabs; the default is false.</summary>
    public static readonly MewProperty<bool> ConvertTabsToSpacesProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(ConvertTabsToSpaces), false);

    /// <summary>Whether the caret can go past the end of a line; the default is false.</summary>
    public static readonly MewProperty<bool> EnableVirtualSpaceProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(EnableVirtualSpace), false);

    /// <summary>Whether a rectangular selection can be started; the default is true.</summary>
    public static readonly MewProperty<bool> EnableRectangularSelectionProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(EnableRectangularSelection), true);

    /// <summary>Whether Insert switches between inserting and overwriting; the default is false.</summary>
    public static readonly MewProperty<bool> AllowToggleOverstrikeModeProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(AllowToggleOverstrikeMode), false);

    /// <summary>Whether text can be composed through an input method; the default is true.</summary>
    public static readonly MewProperty<bool> EnableImeSupportProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(EnableImeSupport), true);

    /// <summary>Whether the pointer is hidden while typing; the default is true.</summary>
    public static readonly MewProperty<bool> HideCursorWhileTypingProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(HideCursorWhileTyping), true);

    /// <summary>Whether spaces are drawn as visible markers; the default is false.</summary>
    public static readonly MewProperty<bool> ShowSpacesProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(ShowSpaces), false);

    /// <summary>Whether tabs are drawn as visible markers; the default is false.</summary>
    public static readonly MewProperty<bool> ShowTabsProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(ShowTabs), false);

    /// <summary>Whether line ends are drawn as visible markers; the default is false.</summary>
    public static readonly MewProperty<bool> ShowEndOfLineProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(ShowEndOfLine), false);

    /// <summary>Whether a control character is drawn as a box naming it; the default is true.</summary>
    public static readonly MewProperty<bool> ShowBoxForControlCharactersProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(ShowBoxForControlCharacters), true);

    /// <summary>Whether a vertical rule is drawn at the column ruler position; the default is false.</summary>
    public static readonly MewProperty<bool> ShowColumnRulerProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(ShowColumnRuler), false);

    /// <summary>The column the rule sits at, counted in wide spaces; the default is 80.</summary>
    public static readonly MewProperty<int> ColumnRulerPositionProperty =
        MewProperty<int>.Register<TextEditorOptions>(nameof(ColumnRulerPosition), 80);

    /// <summary>Whether the line holding the caret is highlighted; the default is false.</summary>
    public static readonly MewProperty<bool> HighlightCurrentLineProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(HighlightCurrentLine), false);

    /// <summary>Whether web addresses in the text become links; the default is true.</summary>
    public static readonly MewProperty<bool> EnableHyperlinksProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(EnableHyperlinks), true);

    /// <summary>Whether mail addresses in the text become links; the default is true.</summary>
    public static readonly MewProperty<bool> EnableEmailHyperlinksProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(EnableEmailHyperlinks), true);

    /// <summary>Whether following a link needs Ctrl held; the default is true.</summary>
    public static readonly MewProperty<bool> RequireControlModifierForHyperlinkClickProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(RequireControlModifierForHyperlinkClick), true);

    /// <summary>Whether copying or cutting with nothing selected takes the whole line; the default is true.</summary>
    public static readonly MewProperty<bool> CutCopyWholeLineProperty =
        MewProperty<bool>.Register<TextEditorOptions>(nameof(CutCopyWholeLine), true);

    /// <summary>Occurs when an option changed, carrying the option that did.</summary>
    public event EventHandler<MewProperty>? OptionChanged;

    /// <summary>Gets or sets the number of columns per indentation level; the default is 4.</summary>
    /// <remarks>A value of zero or less throws <see cref="ArgumentOutOfRangeException"/>.</remarks>
    public virtual int IndentationSize
    {
        get => GetValue(IndentationSizeProperty);
        set
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
            SetValue(IndentationSizeProperty, value);
        }
    }

    /// <summary>Gets or sets whether indentation is written as spaces instead of tabs; the default is false.</summary>
    public virtual bool ConvertTabsToSpaces
    {
        get => GetValue(ConvertTabsToSpacesProperty);
        set => SetValue(ConvertTabsToSpacesProperty, value);
    }

    /// <summary>Gets the text that indents from column 1 to the next indentation level.</summary>
    public string IndentationString => GetIndentationString(1);

    /// <summary>
    /// Gets the text that indents from <paramref name="column"/> to the next indentation level; a
    /// column below 1 throws <see cref="ArgumentOutOfRangeException"/>.
    /// </summary>
    /// <remarks>
    /// Converted tabs fill only up to the next stop, so a tab pressed mid-column does not overshoot
    /// it.
    /// </remarks>
    public virtual string GetIndentationString(int column)
    {
        if (column < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(column), column, "Value must be at least 1.");
        }
        return ConvertTabsToSpaces
            ? new string(' ', IndentationSize - ((column - 1) % IndentationSize))
            : "\t";
    }

    /// <summary>
    /// Gets or sets whether the caret can go past the end of a line, into virtual space; the default
    /// is false.
    /// </summary>
    /// <remarks>
    /// A rectangular selection uses virtual space whatever this says, since it spans columns rather
    /// than offsets.
    /// </remarks>
    public virtual bool EnableVirtualSpace
    {
        get => GetValue(EnableVirtualSpaceProperty);
        set => SetValue(EnableVirtualSpaceProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the box-selection keys and Alt+drag may start a rectangular selection;
    /// the default is true.
    /// </summary>
    public virtual bool EnableRectangularSelection
    {
        get => GetValue(EnableRectangularSelectionProperty);
        set => SetValue(EnableRectangularSelectionProperty, value);
    }

    /// <summary>
    /// Gets or sets whether Insert switches the editor between inserting and overwriting; the
    /// default is false.
    /// </summary>
    public virtual bool AllowToggleOverstrikeMode
    {
        get => GetValue(AllowToggleOverstrikeModeProperty);
        set => SetValue(AllowToggleOverstrikeModeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether text can be composed in the editor through an input method; the default
    /// is true.
    /// </summary>
    public virtual bool EnableImeSupport
    {
        get => GetValue(EnableImeSupportProperty);
        set => SetValue(EnableImeSupportProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the pointer is taken out of the way while text is being typed; the
    /// default is true.
    /// </summary>
    public virtual bool HideCursorWhileTyping
    {
        get => GetValue(HideCursorWhileTypingProperty);
        set => SetValue(HideCursorWhileTypingProperty, value);
    }

    /// <summary>Gets or sets whether spaces are drawn as visible markers; the default is false.</summary>
    public virtual bool ShowSpaces
    {
        get => GetValue(ShowSpacesProperty);
        set => SetValue(ShowSpacesProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a vertical rule is drawn at <see cref="ColumnRulerPosition"/>; the
    /// default is false.
    /// </summary>
    public virtual bool ShowColumnRuler
    {
        get => GetValue(ShowColumnRulerProperty);
        set => SetValue(ShowColumnRulerProperty, value);
    }

    /// <summary>Gets or sets the column the rule sits at, counted in wide spaces; the default is 80.</summary>
    public virtual int ColumnRulerPosition
    {
        get => GetValue(ColumnRulerPositionProperty);
        set => SetValue(ColumnRulerPositionProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the line holding the caret is painted in the view's current-line
    /// colors; the default is false.
    /// </summary>
    public virtual bool HighlightCurrentLine
    {
        get => GetValue(HighlightCurrentLineProperty);
        set => SetValue(HighlightCurrentLineProperty, value);
    }

    /// <summary>Gets or sets whether tabs are drawn as visible markers; the default is false.</summary>
    public virtual bool ShowTabs
    {
        get => GetValue(ShowTabsProperty);
        set => SetValue(ShowTabsProperty, value);
    }

    /// <summary>Gets or sets whether line ends are drawn as visible markers; the default is false.</summary>
    public virtual bool ShowEndOfLine
    {
        get => GetValue(ShowEndOfLineProperty);
        set => SetValue(ShowEndOfLineProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a control character is drawn as a box naming it; the default is true.
    /// </summary>
    /// <remarks>An otherwise invisible character then shows without reading as ordinary text.</remarks>
    public virtual bool ShowBoxForControlCharacters
    {
        get => GetValue(ShowBoxForControlCharactersProperty);
        set => SetValue(ShowBoxForControlCharactersProperty, value);
    }

    /// <summary>Gets or sets whether web addresses in the text become links; the default is true.</summary>
    public virtual bool EnableHyperlinks
    {
        get => GetValue(EnableHyperlinksProperty);
        set => SetValue(EnableHyperlinksProperty, value);
    }

    /// <summary>Gets or sets whether mail addresses in the text become links; the default is true.</summary>
    public virtual bool EnableEmailHyperlinks
    {
        get => GetValue(EnableEmailHyperlinksProperty);
        set => SetValue(EnableEmailHyperlinksProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a link needs Ctrl held to follow it, rather than a plain click; the
    /// default is true.
    /// </summary>
    public virtual bool RequireControlModifierForHyperlinkClick
    {
        get => GetValue(RequireControlModifierForHyperlinkClickProperty);
        set => SetValue(RequireControlModifierForHyperlinkClickProperty, value);
    }

    /// <summary>
    /// Gets or sets whether copying or cutting with nothing selected takes the whole line; the
    /// default is true.
    /// </summary>
    public virtual bool CutCopyWholeLine
    {
        get => GetValue(CutCopyWholeLineProperty);
        set => SetValue(CutCopyWholeLineProperty, value);
    }

    /// <summary>Raises <see cref="OptionChanged"/>. Override to see every option change.</summary>
    protected override void OnMewPropertyChanged(MewProperty property)
        => OptionChanged?.Invoke(this, property);
}
