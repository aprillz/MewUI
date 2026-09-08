internal sealed record ReviewCase(string Name, string Notes, string Markdown);

internal static class ReviewCases
{
    private const string MARKDOWN_HARD_BREAK = "  ";

    internal static readonly IReadOnlyList<ReviewCase> All = Build();

    private static IReadOnlyList<ReviewCase> Build()
    {
        ReviewCase[] cases =
        [
            new("01 Headings", "ATX H1-H6, Setext, inline styles, Unicode and duplicate anchors.", """
                # Heading 1
                ## Heading 2
                ### Heading 3
                #### Heading 4
                ##### Heading 5
                ###### Heading 6

                Setext heading 1
                ================

                Setext heading 2
                ----------------

                ## **Bold** and *italic* and `code` heading
                ## 한국어 제목 / 日本語 / 中文
                ## Repeated heading
                ## Repeated heading

                [First repeated heading](#repeated-heading) / [Second repeated heading](#repeated-heading-2)
                """),
            new("02 Inline styles", "Bold must not imply italic. Strike must not imply bold. Inspect nested styles and styled links.", """
                Normal **bold only** normal.

                Normal *italic only* normal.

                __Underscore bold__ and _underscore italic_.

                ***Bold italic*** and ___also bold italic___.

                **Bold with *nested italic* and normal bold again.**

                *Italic with **nested bold** and normal italic again.*

                ~~Strike only~~ and **~~bold strike~~** and *~~italic strike~~*.

                ++Inserted underline++ and ==marked background==.

                `inline code` and ``code containing `backticks` `` and `  normalized spaces  `.

                **굵은 한글** *기울인 한글* ~~취소 한글~~ `한글 코드`.

                [**bold link** and *italic link* and `code link`](https://example.test/styled "Styled link")
                """),
            new("03 Paragraphs / breaks", "Soft break becomes a space. Two spaces/backslash create hard breaks. Resize for wrapping.", $"""
                This is the first line of a paragraph.
                This is its soft continuation in the same flowing paragraph.

                A blank line starts another paragraph.

                Hard break using two spaces:{MARKDOWN_HARD_BREAK}
                This must start on a new line.

                Hard break using a backslash:\
                This must also start on a new line.

                Long paragraph: The quick brown fox jumps over the lazy dog. 한글 문장의 자동 줄바꿈을 확인합니다. The quick brown fox jumps over the lazy dog. 한글 문장의 자동 줄바꿈을 확인합니다. The quick brown fox jumps over the lazy dog. 한글 문장의 자동 줄바꿈을 확인합니다.

                AReallyLongUnbrokenToken_ABCDEFGHIJKLMNOPQRSTUVWXYZ_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ_0123456789
                """),
            new("04 Lists", "Unordered markers, ordered start, tight/loose lists, nesting and continuation paragraphs.", """
                - Dash item
                - Second dash item

                * Star item
                * Second star item

                + Plus item
                + Second plus item

                1. First ordered item
                2. Second ordered item
                3. Third ordered item

                7) Starts at seven
                8) Continues at eight

                - Parent one
                  - Child one
                    - Grandchild
                  - Child two
                - Parent two
                  1. Ordered child
                  2. Ordered child two

                1. First loose item.

                   Second paragraph in the same item, not a new list.

                   > A block quote inside this list item.

                       indented_code_inside_list();

                2. Second loose item.

                - **Bold content**, *italic*, `code`, and [link](https://example.test/list).
                """),
            new("05 Task lists", "Read-only GFM checkboxes: checked/unchecked, nested, styled and multiline contents.", """
                - [ ] Unchecked task
                - [x] Checked lowercase x
                - [X] Checked uppercase X
                - [ ] **Important** task with a [link](https://example.test/task)
                  - [x] Nested completed task
                  - [ ] Nested open task
                - [ ] Long task: resize and check continuation alignment with the task content, without overlap. 한글 줄바꿈도 확인합니다.

                1. [x] Ordered completed task
                2. [ ] Ordered open task
                """),
            new("06 Quotes / rules", "Nested quotes, lazy continuation, lists/code inside quotes, three thematic-break syntaxes.", """"
                > A simple quote.
                > Continuation in the same paragraph.

                > Outer quote.
                >
                > > Inner quote with **bold**.
                > >
                > > > Third level quote.
                >
                > - List inside a quote
                > - Another item
                >
                > ```text
                > code inside a quote
                > ```

                > Lazy continuation starts here
                and continues without another marker.

                Before dash rule.

                ---

                Before star rule.

                ***

                Before underscore rule.

                ___

                After all rules.
                """"),
            new("07 Code blocks", "Fenced, indented, tilde fences, unknown language, blank lines and long lines. Copy must preserve code only.", """"
                ```csharp
                public static string Hello(string name)
                {
                    return $"Hello, {name}! 안녕하세요";
                }
                ```

                ```json
                {
                  "text": "한글 / emoji 🌍",
                  "number": 42,
                  "enabled": true
                }
                ```

                ~~~python
                def example():
                    return "tilde fence"
                ~~~

                    first indented line
                    second indented line

                    third line after a blank

                ```unknown-language
                unknown language must remain readable
                **this is code, not bold**
                <script>this is code, never executed</script>
                ```

                ````text
                Triple backticks inside a four-backtick fence:
                ```
                ````

                ```text
                very_long_line_ABCDEFGHIJKLMNOPQRSTUVWXYZ_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ_0123456789_ABCDEFGHIJKLMNOPQRSTUVWXYZ_0123456789
                short line
                ```
                """"),
            new("08 Tables", "Left/center/right alignment, inline formatting, escaped pipes, empty cells and wide/narrow tables.", """
                | Left | Center | Right |
                | :--- | :---: | ---: |
                | left value | centered value | 123.45 |
                | 짧음 | 가운데 | 7 |
                | A much longer value that should wrap in a narrow viewport | middle | 987654321 |

                | Style | Example |
                | --- | --- |
                | Bold | **bold** |
                | Italic | *italic* |
                | Strike | ~~gone~~ |
                | Code | `value` |
                | Link | [table link](https://example.test/table) |
                | Pipe | escaped \| pipe |
                | Empty | |
                | | Empty first cell |

                One | Two | Three
                --- | --- | ---
                no outer pipes | supported | yes

                | A | B | C | D | E | F | G | H |
                |---|---|---|---|---|---|---|---|
                | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
                | long long long | 한글 한글 한글 | text | text | text | text | text | text |
                """),
            new("09 Links / anchors", "Click and Tab/Shift+Tab each link. Requests appear below; no browser/file opens. Scroll and retry.", """
                # Links

                [First link](https://example.test/first) [Second link](https://example.test/second) [Third link](https://example.test/third)

                [Relative file](guide/page.md "Relative title") and [root path](/root/page).

                [Email](mailto:person@example.test) and <person@example.test>.

                <https://example.test/angle> and https://example.test/bare and www.example.com.

                [Full reference][destination], [collapsed reference][], and [shortcut].

                [destination]: https://example.test/reference "Reference title"
                [collapsed reference]: https://example.test/collapsed
                [shortcut]: https://example.test/shortcut

                [A very long **styled link** with *italic text* and Korean 한글 that should wrap over multiple visual lines when the preview is narrow](https://example.test/wrapped)

                [Jump to bottom](#link-target)

                [File request only](file:///C:/Windows/win.ini) and [custom scheme request only](custom:example).

                Spacer paragraph one.

                Spacer paragraph two.

                Spacer paragraph three.

                Spacer paragraph four.

                Spacer paragraph five.

                ## Link target

                [Back to links heading](#links)
                """),
            new("10 Images", "In-memory fixtures: valid, wide, delayed, missing, empty-alt and inline flow. Switch cases during delayed loading.", """
                Normal image:

                ![160 x 80 color fixture](demo:checker "Local fixture")

                Wide image should fit the available width:

                ![Wide 960 x 120 fixture](demo:wide)

                Delayed image resolves after 1.2 seconds:

                ![Loading delayed fixture](demo:slow)

                Missing image keeps alternative text:

                ![Missing image alternative text](demo:missing)

                Empty-alt valid image:

                ![](demo:checker)

                Text before ![inline-position image](demo:checker) text after. The image stays in the same flowing paragraph.

                [![Linked image](demo:checker)](https://example.test/image-link)

                ![Network image must not load](https://example.test/no-network.png)
                """),
            new("11 Escapes / Unicode", "Escaped punctuation, entities, combining marks, emoji sequences, RTL and code preservation.", """
                \*not italic\* and \**not bold\** and \_not italic\_.

                \# not a heading

                \[not a link\](destination) and escaped backslash \\.

                Entities: &amp; &lt; &gt; &quot; &copy; &#169; &#x1F30D;.

                Code keeps entity source: `&amp; &lt; **literal**`.

                한글: **가나다라마바사**. 日本語: *こんにちは*. 中文: **你好世界**.

                Combining: café / naïve / Å / **é** / *ä*.

                Emoji: 🌍 😀 👍🏽 👩‍💻 👨‍👩‍👧‍👦 🇰🇷.

                العربية **مرحبا** English עברית **שלום** 123.

                [한글 🌍 é العربية mixed link](https://example.test/unicode)
                """),
            new("12 Definition lists", "Multiple terms and definitions, rich inline content, paragraphs, lists and code inside definitions.", """"
                Apple
                :   A fruit with **bold**, *italic*, and `code` content.

                First term
                Second term
                :   One definition shared by two terms.

                Term with multiple definitions
                :   First definition.

                :   Second definition with a [link](https://example.test/definition).

                Structured definition
                :   First paragraph in the definition.

                  Second paragraph in the same definition.

                  - Nested list item
                  - Another nested item

                      indented_code_inside_definition();
                """"),
            new("13 Unsupported", "Deliberately unsupported: HTML stays literal; math, footnotes, Mermaid and other extensions are not enabled.", """"
                Inline <b>HTML bold</b> and <em>HTML italic</em> must not execute as HTML.

                <div class="sample">
                <strong>Raw HTML block</strong>
                <script>alert('must never execute')</script>
                </div>

                <!-- HTML comment -->

                Footnote reference[^note].

                [^note]: Footnote definition: not enabled.

                Math: $x^2 + y^2 = z^2$.

                $$
                E = mc^2
                $$

                ```mermaid
                graph LR
                    A[Input] --> B[Output]
                ```

                :smile: emoji shortcode is not enabled.

                ~subscript~ / ^superscript^ are not enabled.
                """"),
            new("14 Mixed document", "README-style combination: inspect vertical rhythm, nesting, clipping and scrolling as a whole.", """"
                # Example project

                A **small native UI** with *styled text*, [documentation](https://example.test/docs) and `code`.

                ![Project banner](demo:wide)

                ## Features

                - [x] Native controls
                - [x] Unicode: 한글 / 日本語 / 🌍
                - [ ] Full rich-text selection
                  - This remains unimplemented.

                ## Quick start

                ```csharp
                var viewer = new MarkdownViewer
                {
                    Markdown = "# Hello\n\n**MewUI**"
                };
                ```

                > **Note:** Links are requests. The host decides whether navigation is allowed.
                >
                > 1. Review the URI.
                > 2. Apply your policy.

                | Option | Default | Description |
                | :--- | :---: | --- |
                | Tables | on | Pipe table parsing |
                | Network | off | No automatic remote loading |
                | HTML | literal | Never execute scripts |

                ---

                ## License

                Parser: Markdig, BSD-2-Clause. [More information](https://github.com/xoofx/markdig).
                """"),
            new("15 Malformed / edge", "Unmatched delimiters, unresolved references, empty constructs and an unclosed fence. Content must not vanish or crash.", """"
                #

                **unclosed bold

                *unclosed italic

                [unclosed link](

                [unresolved reference][missing]

                []()

                -
                - valid item after empty item

                >
                > quote after empty quote

                ```text
                This fence is intentionally never closed.
                The last line must remain visible: END-OF-FIXTURE
                """"),
            new("16 Long document", "200 repeated sections for scroll/resize review. Not a virtualization or performance pass.",
                "# Long document\n\n" + string.Join("\n\n", Enumerable.Range(1, 200).Select(index =>
                    $"## Section {index}\n\nParagraph {index}: **bold**, *italic*, `code`, 한글 🌍 and [link {index}](https://example.test/{index}). Resize and scroll to check layout stability.")))
        ];
        string overview = "# Markdown case catalogue\n\nCommonMark core + enabled GFM + unsupported syntax. Select a category for focused inspection.\n\n" +
            string.Join("\n\n", cases.Take(14).Select(entry => "# " + entry.Name + "\n\n" + entry.Markdown));
        return new[] { new ReviewCase("00 All cases", "Visual catalogue, not a claim of full Markdown conformance. Edge and long-document fixtures have separate categories.", overview) }.Concat(cases).ToArray();
    }
}
