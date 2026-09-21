# Aprillz.MewUI.Markdown

Markdown controls for [MewUI](https://github.com/aprillz/MewUI), rendered as native MewUI elements.

**No web view.** [Markdig](https://github.com/xoofx/markdig) parses the document and the extension turns it into MewUI text layouts and controls, so it draws on every MewUI backend (Direct2D, GDI, MewVG/OpenGL) and is NativeAOT/trim compatible.

## Install

```
dotnet add package Aprillz.MewUI.Markdown
```

Targets `net8.0` and `net10.0`.

## Quick start

```csharp
using Aprillz.MewUI.Markdown;

var viewer = new MarkdownViewer()
    .Markdown(File.ReadAllText("README.md"))
    .OnLinkRequested(args => status.Text = args.Url);
```

`MarkdownViewer` scrolls and virtualizes by itself. `MarkdownPresenter` is the same control without a viewport: it always builds the whole tree, so host it in a scrolling container only for small and medium documents. Both expose the same API, and every property below has a matching fluent extension method.

| Property | Purpose |
|---|---|
| `Markdown` | The source text |
| `Options` | Which syntax is enabled (`MarkdownOptions`) |
| `MarkdownTheme` | Block spacing, list indent, code font and colors |
| `BaseUri` | Base for relative links and image URLs |
| `ImageResolver` | Supplies images; without one, images are not loaded |
| `CodeBlockFactory` | Replaces the default fenced code block |
| `Renderers` | Custom presentation for Markdig node types |
| `ParseDelay` | Debounce and parse off the UI thread |
| `IsSelectionEnabled` | Turns mouse selection off |

Events: `LinkRequested`, `SelectionChanged`, `Copying`, `ParseFailed`.

## Syntax

`MarkdownOptions` is a record, so a modified copy enables or disables one feature:

```csharp
viewer.Options = new MarkdownOptions { UseHtmlFormatting = true };
```

| Option | Default |
|---|---|
| `UsePipeTables` | on |
| `UseTaskLists` | on (read-only checkboxes) |
| `UseAutoLinks` | on |
| `UseStrikethrough` | on |
| `UseInserted` | on |
| `UseMarked` | on |
| `UseDefinitionLists` | on |
| `UseFootnotes` | on |
| `SoftBreakAsNewLine` | off |
| `UseHtmlFormatting` | off |

Tight and loose lists keep their distinct block spacing.

**Footnotes.** `[^label]` references are numbered in first-reference order and jump to the definition list, and each definition carries a return link for every reference. Turning `UseFootnotes` off leaves footnote syntax to the remaining Markdig reference-link rules.

**HTML is never executed.** Inline HTML and HTML blocks are shown as literal text. `UseHtmlFormatting` renders a limited subset (`b`, `strong`, `i`, `em`, `u`, `s`, `del`, `tt`, `code`, `big`, `small`, `sub`, `sup`, `br`, `span`, `font`) as native text formatting and hides complete HTML comments. That mode reads fixed font, size, weight, color, background, underline and strikethrough attributes. Structural HTML, CSS, links, images, script, iframe, event attributes, malformed comments and other executable content stay literal.

## Code blocks

There is no built-in syntax highlighting. The default fenced block shows the normalized language and an overlaid Copy button, which uses the platform clipboard service while the application runs.

`CodeBlockFactory` has the signature `Func<string, string?, FrameworkElement?>`. It receives the normalized code text and the first language token, and returns an unattached element that replaces the default block, or `null` for the plain fallback. This is where a host `SyntaxViewer` plugs in.

## Images

Image loading is off until the application provides a resolver, so a document cannot reach the file system or the network on its own. The resolver enforces URI, byte and pixel limits and may cache; the package does not cache images. Resolver work is asynchronous and needs a running UI dispatcher or synchronization context.

```csharp
public sealed class CachedImages : IMarkdownImageResolver
{
    public ValueTask<MarkdownImageLease?> ResolveAsync(MarkdownImageRequest request, CancellationToken cancellationToken)
    {
        var decodedSource = imageCache.Rent(request.ResolvedUri);
        var lease = new MarkdownImageLease(decodedSource, () => imageCache.Return(decodedSource));
        return ValueTask.FromResult<MarkdownImageLease?>(lease);
    }
}
```

Pass a `null` release callback for a source the host owns entirely. The control releases every lease it received but never disposes the source itself. Images flow inline with the surrounding text, are scaled down to the paragraph width, and re-layout when their source arrives.

## Links and keyboard

Tab and Shift+Tab move between links, across blocks and into blocks the viewer has not created yet, and Enter or Space raises `LinkRequested`. The event carries the raw `Url`, the `ResolvedUri` against `BaseUri`, the link `Title` and the source span. The control opens nothing by itself. A block that holds keyboard focus stays alive while it is scrolled out of the virtualized window.

## Selection

Drag to select across blocks, double-click for a word, triple-click for a block, Shift-click to extend and Escape to clear. Copy and select all are `StandardCommands.Copy` and `StandardCommands.SelectAll` handlers, so platform shortcuts, the right-click menu and command-bound menus and toolbars all act on the selection.

`SelectedText` is built from the parsed document, so blocks scrolled out of view are included. It puts a line break between blocks and between list items, a tab between table cells, and a list item marker followed by a tab when the item starts inside the selection. `Copying` is raised before the clipboard is written and can replace the text or cancel the copy.

Elements produced by custom renderers or `CodeBlockFactory` do not take part in the highlight, although their text still appears in `SelectedText`. Replacing the document clears the selection; theme and width changes keep it.

## Virtualization

`MarkdownViewer` keeps only the top-level blocks within about one viewport of the visible range as elements, and blocks that leave the window stay detached for a while so scrolling back reuses their elements and text layouts.

Blocks that have not been reached use an estimated height, the average of measured blocks of the same kind, so the scroll range is approximate until the document has been read through and the thumb size can change slightly as estimates settle. When a block near the viewport turns out to differ from its estimate, the scroll offset is corrected so the block at the top of the viewport stays where it is. Jumping anywhere in the document costs only the blocks around the target, and theme and width changes keep the same block at the top of the viewport.

## Parsing off the UI thread

`ParseDelay` is zero by default, which parses synchronously on the UI thread whenever the source changes. A positive delay debounces source changes and parses on a worker thread: the previous document stays visible until the newest revision is ready, superseded revisions are dropped, and a failed parse raises `ParseFailed` instead of replacing the document. Background parsing needs a running UI dispatcher or synchronization context.

## Extending the syntax and the presentation

`MarkdownOptions.ConfigurePipeline` receives the Markdig pipeline builder, so Markdig extensions such as `UseMathematics()` can be enabled. Nodes without a renderer are shown as source text.

`MarkdownRenderers` maps Markdig node types to custom presentation. `RegisterBlock<TBlock>` returns an unattached `FrameworkElement` (or `null` to fall back), and `RegisterInline<TInline>` returns an `IInlineTextObject` that occupies the node's text columns. The `MarkdownRenderContext` handed to a renderer exposes the presenter, the theme, the node's source text and `RenderBlocks`/`RenderInlines` for delegating child content to the default pipeline.

```csharp
var renderers = new MarkdownRenderers()
    .RegisterBlock<MathBlock>((block, context) => new MathView().Source(context.GetSource(block)));

viewer.Renderers = renderers;
```

Assign the configured instance to `Renderers`; registrations made after the assignment are not observed.
