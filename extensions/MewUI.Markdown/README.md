# MewUI Markdown

`Aprillz.MewUI.Markdown` provides a small Markdown viewer extension for MewUI. `MarkdownViewer` inherits from `MarkdownPresenter`, so both controls expose the same Markdown content and options API.

The sample demonstrates tables, definition lists, Unicode text, mixed and extra emphasis, read-only task checkboxes, inline and fenced code, local anchors, relative links, and resolved images. Tight and loose lists retain their distinct block spacing. Link requests are reported in the sample status line; the sample does not launch a browser.

`MarkdownTheme` is available for public theme configuration. There is no selection or built-in syntax highlighting. An optional `CodeBlockFactory` has the signature `Func<string, string?, FrameworkElement?>`: it receives normalized code text and the first language token. Return an unattached element to replace the default block, or `null` for the plain fallback. This can be used to integrate a host `SyntaxViewer`.

Default fenced code blocks display the normalized language and include an overlaid Copy button. While the application is running it uses the platform clipboard service; clipboard access is otherwise best-effort.

Raw HTML is never executed. Inline HTML and HTML blocks are displayed as literal text, including script, iframe, and style elements.

Image loading is disabled by default and requires an application-provided resolver. Resolver work is asynchronous and requires a running UI dispatcher or synchronization context. The host resolver must enforce URI, byte, and pixel limits and may provide caching; the package does not cache images. A resolver returns a lease, for example:

```csharp
var lease = new MarkdownImageLease(decodedSource, () => imageCache.Return(decodedSource));
return ValueTask.FromResult<MarkdownImageLease?>(lease);
```

Use a `null` release callback for a borrowed source owned entirely by the host. The control releases every returned lease, but does not dispose the source implicitly. Images are represented as blocks.

`MarkdownViewer` virtualizes top-level blocks: only blocks within about one viewport of the visible range exist as elements. Blocks that have not been reached use an estimated height (the average of measured blocks of the same kind), so the scroll range is approximate until the document has been read through and the thumb size can change slightly as estimates settle. When a block near the viewport turns out to differ from its estimate, the scroll offset is corrected so the block at the top of the viewport stays where it is; jumping anywhere in the document costs only the blocks around the target. Theme and width changes keep the same block at the top of the viewport. `MarkdownPresenter` has no viewport of its own and always builds the whole tree, so host it in a scrolling container only for small and medium documents.

`ParseDelay` is zero by default, which parses synchronously on the UI thread when the source changes. A positive delay debounces source changes and parses on a worker thread; the previous document stays visible until the newest revision is ready, superseded revisions are dropped, and a failed parse raises `ParseFailed` instead of replacing the document. Background parsing requires a running UI dispatcher or a synchronization context.

Advanced hosts can extend the syntax and the presentation. `MarkdownOptions.ConfigurePipeline` receives the Markdig pipeline builder, so Markdig extensions such as `UseMathematics()` can be enabled; nodes without a renderer are displayed as source text. `MarkdownRenderers` maps Markdig node types to custom presentation: `RegisterBlock<TBlock>` returns an unattached `FrameworkElement` (or `null` to fall back), and `RegisterInline<TInline>` returns an `IInlineTextObject` that occupies the node's text columns. The `MarkdownRenderContext` passed to a renderer exposes the presenter, the theme, the node's source text, and `RenderBlocks`/`RenderInlines` for delegating child content to the default pipeline. Assign a configured `MarkdownRenderers` instance to `Renderers`; registrations made after assignment are not observed.
