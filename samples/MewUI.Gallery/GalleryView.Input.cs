using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private ObservableValue<string> name = new ObservableValue<string>("This is my name");
    private ObservableValue<int> intBinding = new ObservableValue<int>(1);
    private ObservableValue<double> doubleBinding = new ObservableValue<double>(42.5);

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

        static Button ChevronButton(GlyphKind kind, Action onClick)
            => new Button()
                .Content(new GlyphElement().Kind(kind))
                .Padding(0)
                .WithTheme((t, c) => c.MinWidth(t.Metrics.BaseControlHeight))
                .OnClick(onClick);

        searchBox.TextChanged += _ => RefreshMatches();
        box.DocumentChanged += _ => RefreshMatches();
        UpdateCountLabel();

        return new StackPanel()
            .Vertical()
            .Spacing(6)
            .Children(
                new StackPanel()
                    .Horizontal()
                    .Spacing(4)
                    .Children(
                        searchBox,
                        ChevronButton(GlyphKind.ChevronUp, () => MoveCurrent(-1)),
                        ChevronButton(GlyphKind.ChevronDown, () => MoveCurrent(+1)),
                        countLabel),
                box);
    }

    private FrameworkElement InputsPage() =>
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
                    "NumericUpDown (int/double)",
                    new Grid()
                        .Columns("Auto,Auto,Auto")
                        .Rows("Auto,Auto,Auto")
                        .Spacing(8)
                        .AutoIndexing()
                        .Children(
                            new TextBlock()
                                .Text("Int")
                                .CenterVertical(),

                            new NumericUpDown()
                                .Width(140)
                                .Minimum(0)
                                .Maximum(100)
                                .Step(1)
                                .Format("0")
                                .BindValue(intBinding)
                                .CenterVertical(),

                            new TextBlock()
                                .BindText(intBinding, value => $"Value: {value}")
                                .CenterVertical(),

                            new TextBlock()
                                .Text("Double")
                                .CenterVertical(),

                            new NumericUpDown()
                                .Width(140)
                                .Minimum(0)
                                .Maximum(100)
                                .Step(0.1)
                                .Format("0.##")
                                .BindValue(doubleBinding)
                                .CenterVertical(),

                            new TextBlock()
                                .BindText(doubleBinding, value => $"Value: {value:0.##}")
                                .CenterVertical(),

                            new TextBlock()
                                .Text("Disabled")
                                .CenterVertical(),

                            new NumericUpDown()
                                .Disable()
                                .Width(140)
                                .Minimum(0)
                                .Maximum(100)
                                .Step(0.1)
                                .Format("0.##")
                                .BindValue(doubleBinding)
                                .CenterVertical()
                        )
                ),

                Card(
                    "Emoji",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new TextBlock()
                                .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                                .FontSize(24),
                            new TextBlock()
                                .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                                .FontSize(20),
                            new TextBlock()
                                .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                                .FontSize(16),
                            new TextBlock()
                                .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                                .FontSize(12),
                            new TextBox()
                                .Placeholder("Type or paste emoji here...")
                                .Text("\U0001F36B\U0001F600\U0001F389"),
                            new TextBlock()
                                .Text("Mixed: Hello \U0001F30D World \U0001F680!")
                                .FontSize(14)
                        )
                ),

                Card(
                    "MultiLineTextBox",
                    MultiLineTextBoxDemo()
                ),

                Card(
                    "Find Highlight",
                    FindHighlightDemo()
                ),

                Card(
                    "ToolTip / ContextMenu",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new TextBlock()
                                .Text("Hover to show a tooltip. Right-click to open a context menu.")
                                .TextWrapping(TextWrapping.Wrap)
                                .Width(290)
                                .FontSize(ThemeFontSize.Small),

                            new Button()
                                .Content("Hover / Right-click me")
                                .ToolTip("ToolTip text")
                                .ContextMenu(
                                    new ContextMenu()
                                        .Item("Copy")
                                        .Item("Paste")
                                        .Separator()
                                        .SubMenu("Transform", new ContextMenu()
                                            .Item("Uppercase")
                                            .Item("Lowercase")
                                            .Separator()
                                            .SubMenu("More", new ContextMenu()
                                                .Item("Trim")
                                                .Item("Normalize")
                                                .Item("Sort"))
                                        )
                                        .SubMenu("View", new ContextMenu()
                                            .Item("Zoom In")
                                            .Item("Zoom Out")
                                            .Item("Reset Zoom")
                                        )
                                        .Separator()
                                        .Item("Disabled", isEnabled: false)
                                )
                         )
                 )
             );

}
