using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement TypographyPage()
    {
        var runDemo = new TextBlock()
            .Width(620)
            .FontSize(16)
            .TextWrapping(TextWrapping.Wrap)
            .Inlines(
                new Run("Normal text, "),
                new Run("bold text").Bold(),
                new Run(", "),
                new Run("italic text").Italic(),
                new Run(", "),
                new Run("accent text").Foreground(Color.FromHex("#D83B01")),
                new Run(", "),
                new Run("underlined text").Underline(),
                new Run(", and "),
                new Run("struck text").Strikethrough(),
                new Run(".\nMixed fonts: Segoe UI + "),
                new Run("Consolas").FontFamily("Consolas"),
                new Run(" + "),
                new Run("22 pt").FontSize(22),
                new Run("."));

        // Font Inheritance: Border sets FontSize=16, children inherit
        var inheritanceDemo = new Border()
            .FontSize(16)
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("Inherited 16pt (from parent Border)"),
                        new TextBlock().Text("Also inherited 16pt"),
                        new TextBlock().Text("Override: 10pt").FontSize(10),
                        new Button().Content("Button (inherited 16pt)"),
                        new TextBox().Placeholder("TextBox (inherited 16pt)")
                    ));

        // FontFamily Inheritance
        var fontFamilyDemo = new Border()
            .FontFamily("Consolas")
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("Inherited Consolas"),
                        new TextBlock().Text("Also Consolas"),
                        new TextBlock().Text("Override: Segoe UI").FontFamily("Segoe UI"),
                        new Button().Content("Consolas Button")
                    ));

        // FontWeight Inheritance
        var fontWeightDemo = new Border()
            .Bold()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("Inherited Bold"),
                        new TextBlock().Text("Also Bold"),
                        new TextBlock().Text("Override: Normal").FontWeight(FontWeight.Normal),
                        new Button().Content("Bold Button")
                    ));

        // Nested inheritance: outer=20pt, inner=12pt
        var nestedDemo = new Border()
            .FontSize(20)
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("20pt (from outer)"),
                        new Border()
                            .FontSize(12)
                            .Padding(8)
                            .BorderThickness(1)
                            .CornerRadius(6)
                            .WithTheme((t, b) => b.BorderBrush(t.Palette.ControlBorder))
                            .Child(
                                new StackPanel()
                                    .Vertical()
                                    .Spacing(4)
                                    .Children(
                                        new TextBlock().Text("12pt (from inner Border)"),
                                        new TextBlock().Text("Also 12pt")
                                    )),
                        new TextBlock().Text("Back to 20pt")
                    ));

        return CardGrid(
            Card(
                "Run-like Inline Text",
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        runDemo,
                        new TextBlock()
                            .FontSize(ThemeFontSize.Small)
                            .Text("One logical text surface with per-range color, weight, italic, decoration, font, and size.")),
                minWidth: 650),
            Card("Line Box (LineSpacing / LineBoxTrim)", LineBoxDemo(), minWidth: 500),
            Card("Search Highlight (ListBox / TreeView)", SearchHighlightDemo(), minWidth: 500),
            Card("SyntaxViewer", SyntaxViewerDemo(), minWidth: 650),
            Card("Font Size Inheritance", inheritanceDemo),
            Card("Font Family Inheritance", fontFamilyDemo),
            Card("Font Weight Inheritance", fontWeightDemo),
            Card("Nested Inheritance", nestedDemo)
        );
    }

    private FrameworkElement LineBoxDemo()
    {
        var lineSpacing = new ObservableValue<double>();

        var sample = new TextBlock()
            .FontSize(ThemeFontSize.Large)
            .Bind(TextBlock.LineSpacingProperty, lineSpacing)
            .Text("Àccents float above the cap line,\nglyphs like g, y and p hang\ntheir descenders below the baseline,\nand spacing opens the leading.");

        // The border's height tracks the measured box, so trimming visibly pulls the top and
        // bottom edges onto the glyphs; stretching keeps the render width equal to the measure
        // width, which pins the wrap points.
        var border = new Border()
            .Left()
            .BorderThickness(1)
            .WithTheme((t, b) => b.BorderBrush(t.Palette.Accent.WithAlpha(128)))
            .Child(sample);

        var spacingLabel = new TextBlock()
            .FontSize(ThemeFontSize.Small)
            .CenterVertical()
            .Bind(TextBlock.TextProperty, lineSpacing, x => $"LineSpacing: {x:0.#}");

        return new StackPanel()
            .Vertical()
            .Spacing(10)
            .Children(
                border,
                new StackPanel()
                    .Horizontal()
                    .Spacing(10)
                    .Children(
                        new RadioButton()
                            .Content("None")
                            .IsChecked(true)
                            .OnChecked(() => sample.LineBoxTrim = LineBoxTrim.None),
                        new RadioButton()
                            .Content("Cap")
                            .OnChecked(() => sample.LineBoxTrim = LineBoxTrim.Cap),
                        new RadioButton()
                            .Content("Cap + Baseline")
                            .OnChecked(() => sample.LineBoxTrim = LineBoxTrim.CapAndBaseline)),
                new StackPanel()
                    .Horizontal()
                    .Spacing(10)
                    .Children(
                        new Slider()
                            .Width(120)
                            .Minimum(-16)
                            .Maximum(16)
                            .Value(0)
                            .BindValue(lineSpacing),
                        new Button()
                            .Content("Reset")
                            .OnClick(() => lineSpacing.Value = 0),
                        spacingLabel),
                new TextBlock()
                    .FontSize(ThemeFontSize.Small)
                    .Text("The border tracks the measured box: trimming cuts it to cap and baseline while the ink overflows; the slider tightens or opens the leading."));
    }

    private FrameworkElement SearchHighlightDemo()
    {
        string[] controlNames =
        [
            "Button", "TextBox", "TextBlock", "TreeView", "ListBox", "ComboBox", "CheckBox",
            "RadioButton", "Slider", "ProgressBar", "TabControl", "ToolTip", "ContextMenu",
            "ScrollViewer", "MenuBar", "ToggleSwitch", "NumericUpDown", "ColorPicker"
        ];
        var treeItems = new[]
        {
            new TreeViewNode("Controls",
            [
                new TreeViewNode("Button.cs"),
                new TreeViewNode("TextBox.cs"),
                new TreeViewNode("TreeView.cs"),
                new TreeViewNode("ListBox.cs")
            ]),
            new TreeViewNode("Text",
            [
                new TreeViewNode("TextServices.cs"),
                new TreeViewNode("ManagedTextEngine.cs"),
                new TreeViewNode("ManagedTextRenderContext.cs"),
                new TreeViewNode("TextViewLayout.cs")
            ])
        };

        string query = string.Empty;
        var highlightColor = Color.FromArgb(110, 255, 184, 0);

        // Paint spans repaint only, so the layout and measured width never change while typing.
        void ApplyHighlight(TextBlock target, string text)
        {
            if (query.Length == 0 || !text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                target.Text = text;
                return;
            }
            target.Inlines.Clear();
            int position = 0;
            while (position < text.Length)
            {
                int match = text.IndexOf(query, position, StringComparison.OrdinalIgnoreCase);
                if (match < 0)
                {
                    break;
                }
                if (match > position)
                {
                    target.Inlines.Add(new Run(text[position..match]));
                }
                target.Inlines.Add(new Run(text.Substring(match, query.Length)).Background(highlightColor));
                position = match + query.Length;
            }
            if (position < text.Length)
            {
                target.Inlines.Add(new Run(text[position..]));
            }
        }

        var listBox = new ListBox()
            .Height(230)
            .Items(controlNames);
        var treeView = new TreeView()
            .Height(230)
            .Width(250)
            .ItemsSource(treeItems);

        var description = new TextBlock()
                   .DockBottom()
                   .FontSize(ThemeFontSize.Small)
                   .Text("Run.Background becomes a paint span behind the matched glyphs; items stay plain TextBlocks.");

        // A fresh template instance is the public rebind trigger: the setter rebuilds realized
        // containers while selection and expansion state stay on the control.
        void ApplyTemplates()
        {
            listBox.ItemTemplate(new DelegateTemplate<string>(
                build: ctx => new TextBlock().Register(ctx, "Text").CenterVertical(),
                bind: (_, item, _, ctx) => ApplyHighlight(ctx.Get<TextBlock>("Text"), item ?? "")));
            treeView.ItemTemplate<TreeViewNode>(
                build: ctx => new TextBlock().Register(ctx, "Text").CenterVertical(),
                bind: (_, item, _, ctx) => ApplyHighlight(ctx.Get<TextBlock>("Text"), item.Text));

            ApplyHighlight(description, description.Text);
        }

        ApplyTemplates();

        foreach (var node in treeItems)
        {
            treeView.Expand(node);
        }

        var search = new TextBox()
            .Placeholder("Type to highlight matches, e.g. box")
            .OnTextChanged(text =>
            {
                query = text;
                ApplyTemplates();
            });

        return new DockPanel()
            .Spacing(8)
            .Children(
                search.DockTop(),
                description,
                treeView.DockRight(),
                listBox);
    }

    private FrameworkElement SyntaxViewerDemo()
    {
        var viewer = new SyntaxViewer
        {
            Width = 680,
            Height = 360,
            Wrap = false,
            FontFamily = "Consolas",
            Text = """
                using System.Collections.Generic;
                using System.Linq;

                namespace Gallery.Syntax;

                [Obsolete("Use CreateAsync instead")]
                public sealed record Result(int Id, string Name);

                public static class ResultService
                {
                    // Keywords, types, numbers, members, strings, and interpolation.
                    public static async Task<IReadOnlyList<Result>> CreateAsync(
                        IEnumerable<string?> names,
                        CancellationToken cancellationToken = default)
                    {
                        const int minimumLength = 3;
                        await Task.Delay(42, cancellationToken);

                        return names
                            .Where(name => !string.IsNullOrWhiteSpace(name) && name.Length >= minimumLength)
                            .Select((name, index) => new Result(index + 1, $"Item {index}: {name!.Trim()}"))
                            .ToArray();
                    }
                }
                """
        };
        var classifier = new GalleryCSharpClassifier();
        viewer.Extensions.Classifiers.Add(classifier);
        viewer.WithTheme((theme, target) =>
        {
            classifier.IsDark = theme.IsDark;
            target.InvalidateTextView();
        });
        return viewer;
    }

    private sealed class GalleryCSharpClassifier : ITextClassifier
    {
        public bool IsDark { get; set; } = true;

        private string CommentColor => IsDark ? "#6A9955" : "#008000";
        private string StringColor => IsDark ? "#CE9178" : "#A31515";
        private string NumberColor => IsDark ? "#B5CEA8" : "#098658";
        private string KeywordColor => IsDark ? "#569CD6" : "#0000FF";
        private string TypeColor => IsDark ? "#4EC9B0" : "#267F99";
        private string MemberColor => IsDark ? "#DCDCAA" : "#795E26";

        private static readonly HashSet<string> Keywords =
        [
            "async", "await", "class", "const", "default", "false", "namespace", "new", "null",
            "public", "record", "return", "sealed", "static", "true", "using"
        ];

        private static readonly HashSet<string> BuiltInTypes =
            ["bool", "double", "int", "object", "string", "var", "void"];

        public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output)
        {
            ReadOnlySpan<char> text = context.Text.Span;
            int index = 0;
            while (index < text.Length)
            {
                if (char.IsWhiteSpace(text[index]))
                {
                    index++;
                    continue;
                }

                if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '/')
                {
                    Add(output, index, text.Length - index, CommentColor);
                    break;
                }

                int stringPrefix = text[index] == '$' && index + 1 < text.Length && text[index + 1] == '"' ? 1 : 0;
                if (text[index + stringPrefix] is '"' or '\'')
                {
                    char delimiter = text[index + stringPrefix];
                    int end = index + stringPrefix + 1;
                    while (end < text.Length)
                    {
                        if (text[end] == '\\')
                        {
                            end = Math.Min(text.Length, end + 2);
                            continue;
                        }
                        if (text[end++] == delimiter) break;
                    }
                    Add(output, index, end - index, StringColor);
                    index = end;
                    continue;
                }

                if (char.IsDigit(text[index]))
                {
                    int end = index + 1;
                    while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '.' or '_')) end++;
                    Add(output, index, end - index, NumberColor);
                    index = end;
                    continue;
                }

                if (char.IsLetter(text[index]) || text[index] == '_')
                {
                    int end = index + 1;
                    while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
                    string identifier = text[index..end].ToString();
                    if (Keywords.Contains(identifier) || BuiltInTypes.Contains(identifier))
                        Add(output, index, end - index, KeywordColor);
                    else if (char.IsUpper(identifier[0]))
                        Add(output, index, end - index, TypeColor);
                    else if (PreviousNonWhitespace(text, index) == '.')
                        Add(output, index, end - index, MemberColor);
                    index = end;
                    continue;
                }

                index++;
            }
        }

        private static char PreviousNonWhitespace(ReadOnlySpan<char> text, int index)
        {
            for (int current = index - 1; current >= 0; current--)
            {
                if (!char.IsWhiteSpace(text[current])) return text[current];
            }
            return '\0';
        }

        private static void Add(IList<TextPaintSpan> output, int start, int length, string color)
            => output.Add(new TextPaintSpan(
                new TextRange(start, length),
                Foreground: Color.FromHex(color)));
    }
}
