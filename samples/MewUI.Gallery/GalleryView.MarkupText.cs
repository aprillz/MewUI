using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement MarkupTextPage()
    {
        const string INLINE_STYLES =
            "Plain, <b>bold</b>, <i>italic</i>, <u>underline</u>, " +
            "<s>strikethrough</s>, and <code>inline_code()</code>.";
        const string NESTED_STYLES =
            "<strong>Bold <em>and italic <u>with underline</u></em> back to bold</strong> back to normal.";
        const string FONT_STYLES =
            "Default | <font font='Times New Roman'>Times New Roman</font> | " +
            "<tt>monospace</tt><br>" +
            "<small>small</small> | normal | <big>big</big> | " +
            "<span size='20px'>20 DIP</span> | <span size='1.5x'>1.5x</span><br>" +
            "<span weight='300'>Light 300</span> | <span weight='600'>SemiBold 600</span> | " +
            "<span weight='900'>Black 900</span>";
        const string COLOR_FORMATS =
            "<span color='#D13438'>#RRGGBB</span>  " +
            "<span color='rgb(16, 124, 16)'>rgb(16,124,16)</span>  " +
            "<span color='#CC0078D4'>#AARRGGBB</span><br>" +
            "<span background='#403B82F6'>alpha background</span>  " +
            "<span color='red'><span color='not-a-color'>invalid inherits red</span></span>";
        const string NAMED_COLORS =
            "<span background='black' color='white'> black </span> " +
            "<span background='silver' color='black'> silver </span> " +
            "<span background='gray' color='white'> gray </span> " +
            "<span background='white' color='black'> white </span><br>" +
            "<span background='maroon' color='white'> maroon </span> " +
            "<span background='red' color='white'> red </span> " +
            "<span background='purple' color='white'> purple </span> " +
            "<span background='fuchsia' color='black'> fuchsia </span><br>" +
            "<span background='green' color='white'> green </span> " +
            "<span background='lime' color='black'> lime </span> " +
            "<span background='olive' color='white'> olive </span> " +
            "<span background='yellow' color='black'> yellow </span><br>" +
            "<span background='navy' color='white'> navy </span> " +
            "<span background='blue' color='white'> blue </span> " +
            "<span background='teal' color='white'> teal </span> " +
            "<span background='aqua' color='black'> aqua </span>";
        const string ATTRIBUTE_STYLES =
            "<span font='Georgia' size='18' weight='700' color='#7A3E9D' " +
            "background='#207A3E9D' underline strikethrough>Combined attributes</span><br>" +
            "<u>Outer underline <span underline='false' color='blue'>removed inside</span> restored outside</u>";
        const string ENTITIES =
            "&lt;b&gt; stays literal, &amp; &quot;quotes&quot; &apos;apostrophes&apos;<br>" +
            "Decimal &#9731;, hexadecimal &#x1F642;, unknown &mew;";
        const string MALFORMED =
            "Unknown tags: <badge level='2'>kept as text</badge><br>" +
            "Unmatched closing: before </b> after<br>" +
            "Crossed closing: <b>bold <i>both</b> plain, then </i> is literal<br>" +
            "Unclosed opening: <u>underline continues to the end";
        const string WRAPPING =
            "A single logical surface can wrap while <b>bold text remains bold across line boundaries</b>, " +
            "<span background='#403B82F6'>background paint follows the wrapped range</span>, and " +
            "<code>code_with_a_long_identifier()</code> participates in the same text layout.";

        var liveMarkup = new MarkupTextBlock
            {
                Width = 620,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap
            }
            .Markup("Runtime value: <b>bold</b>");
        var decodedText = new TextBlock()
            .FontFamily("Consolas")
            .FontSize(ThemeFontSize.Small);
        decodedText.SetBinding(TextBlock.TextProperty, liveMarkup, MarkupTextBlock.TextProperty);

        return CardGrid(
            Card("Basic Inline Styles", MarkupExample(INLINE_STYLES), minWidth: 650),
            Card("Nested Styles and Restoration", MarkupExample(NESTED_STYLES), minWidth: 650),
            Card(
                "Font, Size, and Weight",
                MarkupExample(FONT_STYLES, options: new TextMarkupOptions("Courier New")),
                minWidth: 650),
            Card("Fixed Color Formats", MarkupExample(COLOR_FORMATS), minWidth: 650),
            Card("Named Colors", MarkupExample(NAMED_COLORS), minWidth: 650),
            Card("Combined Attributes", MarkupExample(ATTRIBUTE_STYLES), minWidth: 650),
            Card("Entities and Line Breaks", MarkupExample(ENTITIES), minWidth: 650),
            Card("Malformed and Unknown Markup", MarkupExample(MALFORMED), minWidth: 650),
            Card(
                "Wrapping",
                MarkupExample(WRAPPING, resultWidth: 430),
                minWidth: 650),
            Card(
                "Runtime Markup and Read-only Text",
                new StackPanel()
                    .Vertical()
                    .Spacing(10)
                    .Children(
                        liveMarkup,
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new Button()
                                    .Content("Nested")
                                    .OnClick(() => liveMarkup.Markup = "Runtime: <b>bold <i>italic</i></b>"),
                                new Button()
                                    .Content("Color")
                                    .OnClick(() => liveMarkup.Markup = "Runtime: <span color='aqua' background='navy'>fixed colors</span>"),
                                new Button()
                                    .Content("Malformed")
                                    .OnClick(() => liveMarkup.Markup = "Runtime: <b>unclosed")),
                        new TextBlock()
                            .Text("Decoded Text")
                            .FontSize(ThemeFontSize.Small)
                            .SemiBold(),
                        decodedText),
                minWidth: 650));
    }

    private FrameworkElement MarkupExample(
        string markup,
        double resultWidth = 620,
        TextMarkupOptions? options = null)
        => new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new TextBlock()
                    .Width(620)
                    .FontFamily("Consolas")
                    .FontSize(ThemeFontSize.Small)
                    .TextWrapping(TextWrapping.Wrap)
                    .Text(markup),
                new Border()
                    .Width(resultWidth)
                    .Padding(12)
                    .BorderThickness(1)
                    .CornerRadius(6)
                    .WithTheme((theme, border) => border
                        .Background(theme.Palette.ContainerBackground)
                        .BorderBrush(theme.Palette.ControlBorder))
                    .Child(
                        new MarkupTextBlock
                            {
                                FontSize = 16,
                                TextWrapping = TextWrapping.Wrap,
                                Options = options ?? TextMarkupOptions.Default
                            }
                            .Markup(markup)));
}
