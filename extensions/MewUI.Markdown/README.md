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

Parsing and initial layout are synchronous. Large documents are not virtualized; approximately 1 MB documents can take several seconds to measure in a debug GDI run, so this extension is currently intended for small and medium documents.
