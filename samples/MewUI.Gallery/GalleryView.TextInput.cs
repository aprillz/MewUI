using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private ObservableValue<string> name = new ObservableValue<string>("This is my name");
    // Multi-line text box demo that shows the live selection (start / length) bound to the read-only
    // SelectionStart/SelectionLength MewProperties - used to inspect selection geometry.
    private FrameworkElement MultiLineTextBoxDemo()
    {
        var box = new MultiLineTextBox()
            .Height(120)
            .Width(290)
            .Wrap(false)
            .Text("The quick brown fox jumps over the lazy dog, then keeps running far beyond the visible editor width.\n\n- Wrap supported\n- Selection supported\n- Scroll supported");

        return new StackPanel()
            .Vertical()
            .Spacing(6)
            .Children(
                new CheckBox()
                    .Content("Wrap")
                    .IsChecked(box.Wrap)
                    .OnCheckedChanged(isChecked => box.Wrap = isChecked == true),
                box,
                new TextBlock()
                    .FontSize(ThemeFontSize.Small)
                    .Bind(TextBlock.TextProperty, box, TextBase.SelectionStartProperty,
                        (int start) => $"SelectionStart: {start}"),
                new TextBlock()
                    .FontSize(ThemeFontSize.Small)
                    .Bind(TextBlock.TextProperty, box, TextBase.SelectionLengthProperty,
                        (int length) => $"SelectionLength: {length}")
            );
    }


    private const string FIND_DEMO_TEXT =
        "The text engine assembles logical lines into visual lines, wraps them to the viewport, " +
        "and materializes only the lines that are visible.\n\n" +
        "Classifiers attach paint spans to a line without changing its geometry. A search classifier " +
        "is the smallest useful classifier: it scans the line, emits a background span per match, " +
        "and the engine paints the span behind the glyphs.\n\n" +
        "Wrapped lines keep highlight spans consistent: a match that crosses a wrap boundary is " +
        "painted on both visual lines. Scrolling does not recompute matches, because the match " +
        "offsets live in the document, not in the view.\n\n" +
        "Editing the document refreshes the matches. Type into this editor and the highlight " +
        "follows the text. Search for the word line to see many matches, or search for engine " +
        "to see a few.\n\n" +
        "The chevron buttons move the current match, select it, and scroll it into view. The " +
        "current match uses a stronger highlight than the other matches.";

    // Search-match highlighter for the demo: recomputes absolute match offsets on text change and
    // emits line-relative background spans; the current match gets a stronger color.
    private sealed class FindHighlightClassifier : ITextClassifier
    {
        private static readonly Color _matchColor = Color.FromArgb(88, 255, 214, 0);
        private static readonly Color _currentColor = Color.FromArgb(176, 255, 150, 40);

        public List<int> Matches { get; } = new();
        public int QueryLength { get; private set; }
        public int CurrentIndex { get; set; } = -1;

        public void Update(string documentText, string query)
        {
            Matches.Clear();
            CurrentIndex = -1;
            QueryLength = query.Length;
            if (query.Length == 0)
            {
                return;
            }

            int searchStart = 0;
            while (true)
            {
                int hit = documentText.IndexOf(query, searchStart, StringComparison.OrdinalIgnoreCase);
                if (hit < 0)
                {
                    break;
                }

                Matches.Add(hit);
                searchStart = hit + query.Length;
            }
        }

        public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
        {
            if (Matches.Count == 0)
            {
                return;
            }

            int lineStart = context.LogicalLine.Offset;
            int lineEnd = lineStart + context.LogicalLine.Length;

            for (int index = 0; index < Matches.Count; index++)
            {
                int matchStart = Matches[index];
                if (matchStart >= lineEnd)
                {
                    break;
                }

                int clampedStart = Math.Max(lineStart, matchStart);
                int clampedEnd = Math.Min(lineEnd, matchStart + QueryLength);
                if (clampedEnd > clampedStart)
                {
                    output.Add(new TextPaintSpan(
                        new TextRange(clampedStart - lineStart, clampedEnd - clampedStart),
                        Background: index == CurrentIndex ? _currentColor : _matchColor));
                }
            }
        }
    }

    private FrameworkElement FindHighlightDemo()
    {
        var classifier = new FindHighlightClassifier();

        var box = new MultiLineTextBox()
            .Height(240)
            .Width(360)
            .Wrap(true)
            .Text(FIND_DEMO_TEXT);
        box.Extensions.Classifiers.Add(classifier);

        var searchBox = new TextBox().Placeholder("Find...").Width(150);
        var countLabel = new TextBlock().FontSize(ThemeFontSize.Small).CenterVertical();
        var previousMatch = new Command("gallery.find.previous", "Previous match");
        var nextMatch = new Command("gallery.find.next", "Next match");

        void UpdateCountLabel()
            => countLabel.Text = classifier.Matches.Count == 0
                ? "0/0"
                : $"{classifier.CurrentIndex + 1}/{classifier.Matches.Count}";

        void RefreshMatches()
        {
            classifier.Update(box.Text, searchBox.Text);
            box.InvalidateTextView();
            UpdateCountLabel();
        }

        void MoveCurrent(int direction)
        {
            int count = classifier.Matches.Count;
            if (count == 0)
            {
                return;
            }

            if (classifier.CurrentIndex < 0)
            {
                classifier.CurrentIndex = direction > 0 ? 0 : count - 1;
            }
            else
            {
                classifier.CurrentIndex = (classifier.CurrentIndex + direction + count) % count;
            }

            int offset = classifier.Matches[classifier.CurrentIndex];
            box.Select(offset, classifier.QueryLength);
            box.ScrollToCaret();
            box.InvalidateTextView();
            UpdateCountLabel();
        }

        var findNavigation = new ButtonGroup()
            .Items([previousMatch, nextMatch], command => command.Text ?? string.Empty)
            .ItemTemplate<Command>(
                build: _ => new GlyphElement(),
                bind: (view, _, index, _) =>
                    ((GlyphElement)view).Kind = index == 0
                        ? GlyphKind.ChevronUp
                        : GlyphKind.ChevronDown)
            .ItemPadding(Thickness.Zero)
            .PrepareContainer<Command>((segment, command, _) =>
            {
                segment.Command = command;
                segment.ToolTip(command.Text);
                segment.WithTheme((theme, current) =>
                    current.MinWidth(theme.Metrics.BaseControlHeight));
            });
        findNavigation.Commands.Register(
            previousMatch,
            () => MoveCurrent(-1),
            () => classifier.Matches.Count > 0);
        findNavigation.Commands.Register(
            nextMatch,
            () => MoveCurrent(+1),
            () => classifier.Matches.Count > 0);

        searchBox.TextChanged += _ => RefreshMatches();
        box.DocumentChanged += _ => RefreshMatches();
        UpdateCountLabel();

        return new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        searchBox,
                        findNavigation,
                        countLabel),
                box);
    }

    private FrameworkElement TextInputPage() =>
            CardGrid(
                Card(
                    "TextBox",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new TextBox(),
                            new TextBox().Placeholder("Type your name..."),
                            new TextBox().BindText(name),
                            new TextBox().Text("Disabled").Disable()
                        )
                ),

                Card(
                    "PasswordBox",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new PasswordBox().Placeholder("Password"),
                            new PasswordBox { PasswordChar = '★' }.Placeholder("Custom mask"),
                            new PasswordBox().Password("Disabled").Disable()
                        )
                ),

                Card(
                    "MultiLineTextBox",
                    MultiLineTextBoxDemo()
                ),

                Card(
                    "Find Highlight",
                    FindHighlightDemo()
                )
            );

}
