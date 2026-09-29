# Drag and Drop

An element with `AllowDrop` receives drags, and an element with `CanDrag` starts them. The data travels as an `IDataObject`. A drag between MewUI elements and a drag from another application, such as files from the file manager or text from a browser, raise the same events and are read the same way.

```text
Other application ──(OS drag)──┐
                               ▼
MewUI element (CanDrag) ──► Window (AllowDrop) ── elements with AllowDrop, from the one under the pointer up to the window
                                                   DragEnter / DragOver / DragLeave / Drop
```

## Receiving a drop

Set `AllowDrop` on the window to receive drags from other applications, and on each element that should take part. The events go to the elements with `AllowDrop` on the path from the element under the pointer up to the window.

- `DragEnter` and `DragLeave` are raised on each element the pointer enters or leaves, once per element.
- `DragOver` and `Drop` start at the innermost element and go up until a handler accepts the drag or sets `Handled`.
- No `DragLeave` follows a `Drop`.

A handler accepts by setting `Accepted` or `Effect` in `DragOver`. `Effect` must be one of the source's `AllowedEffects`; anything else is treated as `None`.

```csharp
list.AllowDrop = true;
list.DragOver += e =>
{
    e.Accepted = e.Data.Contains(StandardDataFormats.StorageItems);
    e.Effect = DragDropEffects.Copy;
};
list.Drop += e =>
{
    if (e.Data.TryGetData(DataFormats.StorageItems, out var paths))
    {
        AddFiles(paths);
    }
};
```

When no handler set `Accepted` or `Effect`, a drag that carries a standard format (below) is accepted, with `Copy` when the source allows it. A handler that refuses, by setting `Accepted = false`, keeps its refusal. A drag that carries only platform formats has to be accepted by a handler.

On Windows, a drag from another application is delivered in full only when the UI thread is STA: put `[STAThread]` on `Main`. On an MTA thread only a file drop arrives, as a `Drop` without `DragEnter`, `DragOver` or `DragLeave`.

## Reading the data

`DataFormats` names each format together with the type of its value, and `TryGetData` returns that type.

### Standard formats

Every platform reads the standard formats the same way.

| Format | Value | Contents |
|---|---|---|
| `DataFormats.StorageItems` | `IReadOnlyList<string>` | Absolute paths of local files and folders |
| `DataFormats.Uris` | `IReadOnlyList<string>` | Absolute URIs as the source sent them, including items that are not local files (`https://`, `smb://`, an archive member such as `zip:///...`); local files appear as `file://` URIs |
| `DataFormats.Text` | `string` | Plain text |

A file dragged out of an archive has no local path, so it appears in `Uris` but not in `StorageItems`.

### Platform formats

A drag from another application also lists every format the source offered, under the platform's own name. `DataFormats.FromPlatformName(name)` reads one as the bytes the source sent.

```csharp
if (e.Data.TryGetData(DataFormats.FromPlatformName("text/html"), out var html))
{
    ShowHtml(Encoding.UTF8.GetString(html));
}
```

| Platform | Format names | Examples |
|---|---|---|
| Windows | Clipboard format names: predefined formats by their constant name, registered formats by their registered name | `CF_HDROP`, `CF_UNICODETEXT`, `HTML Format`, `UniformResourceLocatorW` |
| Linux (X11) | MIME types and atom names | `text/uri-list`, `text/html`, `UTF8_STRING` |
| macOS | Pasteboard type identifiers | `public.file-url`, `public.html`, `public.png` |

`Formats` lists the standard formats first, then the platform formats in the source's order. On Windows only formats whose whole content is held in global memory are listed. On macOS a drag reaches the window only when it carries files, a URL, text, HTML, rich text or an image.

### When values can be read

The formats are fixed when the drag enters, and each value is read from the source the first time it is requested. Values can be read during `DragEnter` and `DragOver` as well as in `Drop`. When the drag ends the data object lets go of the source: values read before that stay available, others can no longer be read. Copy what you need inside the `Drop` handler.

On Linux, each read asks the source application and waits up to 2 seconds for its answer, so read only the formats you need. Data the source sends in increments (large images, for example) cannot be read.

### String keys

The format names are also available as strings, and `IDataObject` keeps its string members.

```csharp
e.Data.Contains(StandardDataFormats.Uris);
e.Data.TryGetData<IReadOnlyList<string>>(StandardDataFormats.Uris, out var uris);
e.Data.GetData("text/uri-list"); // a platform format: byte[]
```

## Starting a drag

Set `CanDrag` on the element. After the mouse is pressed and moved past the drag threshold, `DragStarting` is raised: set `Data` to start the drag, or leave it `null` or set `Cancel` to skip it.

```csharp
static readonly DataFormat<Card> CardFormat = DataFormats.Create<Card>("application/x-myapp.card");

card.CanDrag = true;
card.DragStarting += e =>
{
    var data = new DataObject();
    data.SetData(CardFormat, card.Model);
    data.SetText(card.Model.Title);
    e.Data = data;
    e.AllowedEffects = DragDropEffects.Move | DragDropEffects.Copy;
};
card.DragCompleted += e =>
{
    if (e.FinalEffect == DragDropEffects.Move)
    {
        RemoveCard(card.Model);
    }
};

board.Drop += e =>
{
    if (e.Data.TryGetData(CardFormat, out var moved))
    {
        Place(moved);
    }
};
```

`DataFormats.Create<T>(name)` defines a format for the application's own data; its value is the .NET object stored under that name. `DragCompleted` reports the effect the target chose (`None` when the drop was refused or released over empty space), whether the drag was canceled, and the screen position where it ended. `BeginDrag(data, allowedEffects, preview)` starts a drag from code without a mouse gesture.

A drag started in MewUI moves between the application's own windows; other applications do not receive it.

## Preview

`DragStartingEventArgs.Preview` (or the `preview` argument of `BeginDrag`) sets a `DragPreviewContent` that follows the pointer.

- `Element` or `Image`: the visual to show. A detached element is laid out and hosted as the preview, capped at `MaxWidth`.
- `Hotspot`: where the pointer sits on the preview; by default the point where the element was grabbed.
- `Opacity`: 0.75 by default.
- `Scope`: `WithinWindow` keeps the preview inside the window under the pointer; `CrossWindow` lets it follow the pointer across windows and the desktop.
