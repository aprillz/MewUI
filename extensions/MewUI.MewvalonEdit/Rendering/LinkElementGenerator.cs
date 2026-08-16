using System.Diagnostics;
using System.Text.RegularExpressions;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.MewvalonEdit.Rendering;

/// <summary>
/// Link text that opens its target in the system browser. It decorates the document text rather
/// than replacing it, so a link stays editable and the caret still moves inside it.
/// </summary>
public class VisualLineLinkText : VisualLineText
{
    public VisualLineLinkText(int documentLength) : base(documentLength)
    {
    }

    /// <summary>Target opened on click. Mail addresses carry the mailto prefix already.</summary>
    public string NavigateUri { get; set; } = string.Empty;

    /// <summary>Requires Ctrl+Click to follow the link, leaving a plain click for the caret.</summary>
    public bool RequireControlModifierForClick { get; set; } = true;

    protected internal override void PrepareForPaint(TextView textView)
    {
        ArgumentNullException.ThrowIfNull(textView);
        // Assigned every time, not filled in when empty: the scan cache outlives a colour change,
        // so a value kept from the last paint would never be replaced.
        Foreground = textView.ResolvedLinkTextForeground;
        BackgroundBrush = textView.LinkTextBackgroundBrush;
        TextRunProperties.SetTextDecorations(
            textView.LinkTextUnderline ? TextDecoration.Underline : TextDecoration.None);
    }

    /// <summary>
    /// Whether the link can be followed right now: true while Control is held, or whenever
    /// <see cref="RequireControlModifierForClick"/> is off. Override to add a condition.
    /// </summary>
    protected virtual bool LinkIsClickable(ModifierKeys modifiers)
    {
        if (NavigateUri.Length == 0)
        {
            return false;
        }
        return !RequireControlModifierForClick || (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
    }

    protected internal override void OnQueryCursor(QueryCursorEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (LinkIsClickable(e.Modifiers))
        {
            e.Cursor = CursorType.Hand;
        }
    }

    protected internal override void OnMouseDown(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Button != MouseButton.Left || e.Handled || !LinkIsClickable(e.Modifiers))
        {
            return;
        }
        NavigateTo(NavigateUri);
        e.Handled = true;
    }

    /// <summary>Opens the target. Override to intercept navigation, e.g. for in-app handling.</summary>
    protected virtual void NavigateTo(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (SystemException)
        {
            // No handler for the scheme is the user's configuration, not the editor's failure.
        }
    }
}

/// <summary>Underlines URLs found in the document text.</summary>
public class LinkElementGenerator : VisualLineElementGenerator, IBuiltinElementGenerator
{
    public static readonly Regex DefaultLinkRegex =
        new(@"\b(https?://|ftp://|www\.)[\w\d\._/\-~%@()+:?&=#!]*[\w\d/]", RegexOptions.CultureInvariant);

    public static readonly Regex DefaultMailRegex =
        new(@"\b[\w\d\.\-]+@[\w\d\.\-]+\.[a-z]{2,6}\b", RegexOptions.CultureInvariant);

    private readonly Regex _linkRegex;

    public LinkElementGenerator() : this(DefaultLinkRegex)
    {
    }

    public LinkElementGenerator(Regex regex)
        => _linkRegex = regex ?? throw new ArgumentNullException(nameof(regex));

    /// <summary>Requires Ctrl+Click to follow generated links. Default true, as in AvalonEdit.</summary>
    public bool RequireControlModifierForClick { get; set; } = true;

    void IBuiltinElementGenerator.FetchOptions(TextEditorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequireControlModifierForClick = options.RequireControlModifierForHyperlinkClick;
    }

    /// <summary>Builds the link element. Override to substitute a subclass, e.g. one intercepting navigation.</summary>
    protected virtual VisualLineLinkText CreateLinkElement(string text, int documentLength)
        => new(documentLength);

    /// <summary>
    /// Target of a matched text, or null when the match is not a well-formed URI and no element
    /// should stand in for it.
    /// </summary>
    protected virtual string? GetUriFromMatch(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        string target = match.Value.StartsWith("www.", StringComparison.Ordinal)
            ? "http://" + match.Value
            : match.Value;
        return Uri.IsWellFormedUriString(target, UriKind.Absolute) ? target : null;
    }

    /// <summary>
    /// The element standing in for the match, or null to leave the text alone. The default builds a
    /// <see cref="VisualLineLinkText"/> around the target <see cref="GetUriFromMatch"/> resolved.
    /// </summary>
    protected virtual VisualLineElement? ConstructElementFromMatch(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (GetUriFromMatch(match) is not string uri)
        {
            return null;
        }
        var element = CreateLinkElement(match.Value, match.Length);
        element.NavigateUri = uri;
        element.RequireControlModifierForClick = RequireControlModifierForClick;
        return element;
    }

    public override int GetFirstInterestedOffset(int startOffset)
        => Match(startOffset, out int matchOffset).Success ? matchOffset : -1;

    public override VisualLineElement? ConstructElement(int offset)
    {
        var match = Match(offset, out int matchOffset);
        return match.Success && matchOffset == offset ? ConstructElementFromMatch(match) : null;
    }

    private Match Match(int startOffset, out int matchOffset)
    {
        if (CurrentContext is not ITextRunConstructionContext context)
        {
            matchOffset = -1;
            return System.Text.RegularExpressions.Match.Empty;
        }
        var line = context.CurrentDocumentLine;
        int lineEnd = line.Offset + line.Length;
        if (startOffset >= lineEnd)
        {
            matchOffset = -1;
            return System.Text.RegularExpressions.Match.Empty;
        }
        string text = context.Document.GetText(startOffset, lineEnd - startOffset);
        var match = _linkRegex.Match(text);
        matchOffset = match.Success ? startOffset + match.Index : -1;
        return match;
    }
}

/// <summary>Underlines mail addresses and opens them through the mailto scheme.</summary>
public class MailLinkElementGenerator : LinkElementGenerator
{
    public MailLinkElementGenerator() : base(DefaultMailRegex)
    {
    }

    public MailLinkElementGenerator(Regex regex) : base(regex)
    {
    }

    protected override string? GetUriFromMatch(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);
        string target = "mailto:" + match.Value;
        return Uri.IsWellFormedUriString(target, UriKind.Absolute) ? target : null;
    }
}
