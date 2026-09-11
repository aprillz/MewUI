# MewUI Markdown

`Aprillz.MewUI.Markdown` provides a small Markdown viewer extension for MewUI. `MarkdownViewer` inherits from `MarkdownPresenter`, so both controls expose the same Markdown content and options API.

The sample demonstrates tables, definition lists, numbered footnotes with return links, Unicode text, mixed and extra emphasis, read-only task checkboxes, inline and fenced code, local anchors, relative links, and resolved images. Tight and loose lists retain their distinct block spacing. Link requests are reported in the sample status line; the sample does not launch a browser.

`MarkdownTheme` is available for public theme configuration. There is no selection or built-in syntax highlighting. An optional `CodeBlockFactory` has the signature `Func<string, string?, FrameworkElement?>`: it receives normalized code text and the first language token. Return an unattached element to replace the default block, or `null` for the plain fallback. This can be used to integrate a host `SyntaxViewer`.

Default fenced code blocks display the normalized language and include an overlaid Copy button. While the application is running it uses the platform clipboard service; clipboard access is otherwise best-effort.

Footnotes are enabled by default. `[^label]` references display first-reference-order numbers and jump to the definition list; each definition includes a return link for every reference. Set `MarkdownOptions.UseFootnotes` to `false` to leave footnote syntax to the remaining Markdig reference-link rules.

Raw HTML is never executed. Inline HTML and HTML blocks are displayed as literal text by default. Set `MarkdownOptions.UseHtmlFormatting` to render the limited `b`, `strong`, `i`, `em`, `u`, `s`, `del`, `tt`, `code`, `big`, `small`, `sub`, `sup`, `br`, `span`, and `font` subset as native text formatting and hide complete HTML comments. This mode supports fixed font, size, weight, color, background, underline, and strikethrough attributes; structural HTML, CSS, links, images, script, iframe, event attributes, malformed comments, and other executable content stay literal.

Image loading is disabled by default and requires an application-provided resolver. Resolver work is asynchronous and requires a running UI dispatcher or synchronization context. The host resolver must enforce URI, byte, and pixel limits and may provide caching; the package does not cache images. A resolver returns a lease, for example:

```csharp
var lease = new MarkdownImageLease(decodedSource, () => imageCache.Return(decodedSource));
return ValueTask.FromResult<MarkdownImageLease?>(lease);
```

Use a `null` release callback for a borrowed source owned entirely by the host. The control releases every returned lease, but does not dispose the source implicitly. Images flow inline with the surrounding text, are scaled down to the paragraph width, and re-layout when their source arrives.

Links are keyboard reachable: Tab and Shift+Tab move between links, across blocks and into blocks the viewer has not created yet, and Enter or Space raises `LinkRequested`. A block holding keyboard focus stays alive while it is scrolled out of the virtualized window. Blocks that leave the window are kept detached for a while so scrolling back reuses their elements and text layouts.

`MarkdownViewer` virtualizes top-level blocks: only blocks within about one viewport of the visible range exist as elements. Blocks that have not been reached use an estimated height (the average of measured blocks of the same kind), so the scroll range is approximate until the document has been read through and the thumb size can change slightly as estimates settle. When a block near the viewport turns out to differ from its estimate, the scroll offset is corrected so the block at the top of the viewport stays where it is; jumping anywhere in the document costs only the blocks around the target. Theme and width changes keep the same block at the top of the viewport. `MarkdownPresenter` has no viewport of its own and always builds the whole tree, so host it in a scrolling container only for small and medium documents.

Text can be selected with the mouse across blocks: drag to select, double-click for a word, triple-click for a block, Shift-click to extend, Ctrl+A to select all, Ctrl+C to copy and Escape to clear. `SelectedText` returns the selection as plain text with blank lines between blocks and tabs between table cells; it is built from the parsed document, so blocks that are scrolled out of view are included. `IsSelectionEnabled` turns the feature off. Elements produced by custom renderers or `CodeBlockFactory` do not take part in the highlight, although their text still appears in `SelectedText`. Replacing the document clears the selection; theme and width changes keep it.

`ParseDelay` is zero by default, which parses synchronously on the UI thread when the source changes. A positive delay debounces source changes and parses on a worker thread; the previous document stays visible until the newest revision is ready, superseded revisions are dropped, and a failed parse raises `ParseFailed` instead of replacing the document. Background parsing requires a running UI dispatcher or a synchronization context.

Advanced hosts can extend the syntax and the presentation. `MarkdownOptions.ConfigurePipeline` receives the Markdig pipeline builder, so Markdig extensions such as `UseMathematics()` can be enabled; nodes without a renderer are displayed as source text. `MarkdownRenderers` maps Markdig node types to custom presentation: `RegisterBlock<TBlock>` returns an unattached `FrameworkElement` (or `null` to fall back), and `RegisterInline<TInline>` returns an `IInlineTextObject` that occupies the node's text columns. The `MarkdownRenderContext` passed to a renderer exposes the presenter, the theme, the node's source text, and `RenderBlocks`/`RenderInlines` for delegating child content to the default pipeline. Assign a configured `MarkdownRenderers` instance to `Renderers`; registrations made after assignment are not observed.
