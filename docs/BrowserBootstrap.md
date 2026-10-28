# Browser Bootstrap (WebAssembly)

This document covers how a MewUI application runs in a browser: creating one from the template, and what each file of it does, so an app can change those files or set a project up by hand. For how a run starts and ends, see [Application Lifecycle](ApplicationLifecycle.md), section 2.5.

The Gallery browser sample is a larger example of the same structure: [samples/MewUI.Gallery.Browser](../samples/MewUI.Gallery.Browser).

---

## 1. Creating an app

Install the WebAssembly build tools and the MewUI templates once per machine, then create the app and run it:

```
dotnet workload install wasm-tools
dotnet new install Aprillz.MewUI.Templates
dotnet new mewui-browser -o MyBrowserApp
cd MyBrowserApp
dotnet run
```

The template creates these files, each covered in the sections below:

| File | Contents | Section |
|---|---|---|
| `MyBrowserApp.csproj` | The WebAssembly project and the two browser packages | 3 |
| `Program.cs` | Registration and the main window | 4 |
| `BrowserExports.cs` | The calls the page makes into the application | 5 |
| `wwwroot/index.html` | The canvas, the hidden text field and the loading text | 6 |
| `wwwroot/main.js` | Runtime start, DOM events and the frame loop | 6, 7, 8 |
| `Properties/launchSettings.json` | The address `dotnet run` serves the app on | |

The template references the MewUI packages of the same version as the template package itself.

---

## 2. The division of work

The desktop hosts own a window, a message loop and an input queue. In the browser the page owns all three, so the application and the page split the work:

| Owner | Responsibility |
|---|---|
| Page | The canvas, the animation-frame loop, DOM events, fetching resources, hiding the loading state |
| MewUI | Layout, rendering into the canvas through WebGL, input routing, timers, theme |

The page therefore calls into the application, and the application calls back into the page for two things only: writing the clipboard and moving the text field onto the caret.

---

## 3. Project setup

The template's project file is the following, and a project set up by hand needs the same:

```xml
<Project Sdk="Microsoft.NET.Sdk.WebAssembly">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aprillz.MewUI.Platform.Browser" Version="MEWUI_VERSION" />
    <PackageReference Include="Aprillz.MewUI.Backend.MewVG.Browser" Version="MEWUI_VERSION" />
  </ItemGroup>

</Project>
```

- `MEWUI_VERSION` stands for the MewUI version the app uses. Every MewUI package carries the same version, and the two packages bring the core (`Aprillz.MewUI.Core`) with them.
- `AllowUnsafeBlocks` is required because the `[JSImport]` and `[JSExport]` source generators emit unsafe code.
- The backend package compiles a small native WebGL shim into the app's runtime, because the backend calls GL entry points the runtime does not export on its own. It also adds the WebGL 2 link flags. Both apply only to a `browser-wasm` build, and the native step is why the `wasm-tools` workload is needed: without it the build stops with an error that names it.
- The platform package adds `mewui-text-input.js` to the app's static files at the root, next to the page, where `main.js` imports it (section 8). Both `dotnet run` and `dotnet publish` serve it from there.
- The page (`index.html`, `main.js`) and the exports class are application code, so the template creates them and the packages do not. A project set up by hand can start from the template's copies or from the Gallery sample's [main.js](../samples/MewUI.Gallery.Browser/wwwroot/main.js) and [BrowserExports.cs](../samples/MewUI.Gallery.Browser/BrowserExports.cs).

---

## 4. Startup

Registration and the entry point are ordinary MewUI code:

```csharp
BrowserPlatform.Register();
MewVGBrowserBackend.Register();

await Application.Create()
    .BuildMainWindow(() => new MainWindow())
    .RunAsync();
```

`Application.Run` throws in the browser. `RunAsync` returns as soon as the window is shown, and the task completes at shutdown.

The browser backend is marked `[SupportedOSPlatform("browser")]`. The template's `Program.cs` marks the app assembly the same way, so the platform analyzer accepts the calls without a guard.

---

## 5. The exports the page calls

The page reaches the application through `[JSExport]` methods. The app declares thin wrappers over `BrowserPlatform`, and connects the two page functions the platform calls back:

```csharp
[SupportedOSPlatform("browser")]
internal static partial class BrowserExports
{
    [JSImport("writeClipboard", "main.js")]
    internal static partial void WriteClipboard(string text);

    [JSImport("moveTextInput", "main.js")]
    internal static partial void MoveTextInput(double x, double y, double height);

    static BrowserExports()
    {
        BrowserPlatform.ClipboardWriter = WriteClipboard;
        BrowserPlatform.CaretReporter = MoveTextInput;
    }

    [JSExport]
    internal static bool RenderFrame(double cssWidth, double cssHeight, double devicePixelRatio,
        int pixelWidth, int pixelHeight, double frameAgeMs)
        => BrowserPlatform.RenderFrame(cssWidth, cssHeight, devicePixelRatio, pixelWidth, pixelHeight, frameAgeMs);

    // One wrapper per BrowserPlatform member the page needs.
}
```

The members a complete host uses:

| Group | Members |
|---|---|
| Frames | `RenderFrame`, `NextWakeDelayMs` |
| Mouse | `PointerMove`, `PointerButton`, `PointerCancel()`, `PointerLeave` |
| Touch and pen | `PointerDown`, `PointerMove`, `PointerUp`, `PointerCancel(pointerId)` |
| Wheel | `PointerWheel` |
| Keyboard | `KeyDown`, `KeyUp`, `TextInput` |
| IME | `CompositionStart`, `CompositionUpdate`, `CompositionEnd`, `GetTextInputState`, `ReplaceText`, `WantsTextInput`, `SyncTextCaret` |
| Window state | `FocusChanged`, `SetSystemDarkMode` |
| Clipboard | `SetClipboardText` (paste), `ClipboardWriter` (copy) |
| Caret | `CaretReporter` |

The template's `main.js` looks the class up with `getAssemblyExports(config.mainAssemblyName)` and then by its namespace, so moving the class to another assembly or namespace means changing that line too.

`SetSystemDarkMode` is the only way the application learns the color scheme, so the page reports it once at startup and on every `prefers-color-scheme` change.

---

## 6. The page

`index.html` carries a full-size canvas, a hidden text field and the loading text:

```html
<canvas id="canvas"></canvas>
<input id="textinput" type="text" autocomplete="off" autocorrect="off" autocapitalize="off" spellcheck="false">
<div id="loader">Loading...</div>
<script type="module" src="./main.js"></script>
```

- `canvas { touch-action: none }` keeps the browser from claiming a touch drag; without it a drag ends in `pointercancel` halfway through.
- The text field stays on screen at one pixel and fully transparent. A field the browser considers hidden is not a live IME target, and the IME draws its candidate list where the field sits, which is why `moveTextInput` parks it on the caret.
- The loading text is the page's own, since nothing managed runs until the runtime has downloaded. The template's `main.js` hides it after the first frame that draws, and shows a startup failure in it.

`main.js` does five things:

1. **Start the runtime.** Import `./_framework/dotnet.js`, carrying its own query string over, then `dotnet.create()` and `getAssemblyExports`.
2. **Register the page functions** with `setModuleImports('main.js', { writeClipboard, moveTextInput })`.
3. **Size the canvas** from its measured box times `devicePixelRatio`, inside the frame that repaints it. Resizing the backing store clears it, so a resize outside the redraw flickers.
4. **Forward DOM events** to the exports.
5. **Run the frame loop** (see 7).

---

## 7. The frame loop

```js
function frame(frameTimeMs) {
    const drew = app.RenderFrame(cssWidth, cssHeight, dpr, canvas.width, canvas.height,
        Math.max(0, performance.now() - frameTimeMs));
    idleFrames = drew ? 0 : idleFrames + 1;
    if (idleFrames < IDLE_FRAMES_BEFORE_SLEEP) {
        requestAnimationFrame(frame);
    } else {
        sleepUntilNextTimer();   // setTimeout(wake, app.NextWakeDelayMs())
    }
}
```

- `RenderFrame` returns whether anything was drawn. A few quiet frames in a row end the loop, so an idle page costs nothing.
- `NextWakeDelayMs` returns the delay until the next timer, or -1 when nothing is due. With -1 the page waits for the next event.
- Every event handler calls `wake()` before forwarding, so input restarts a sleeping loop.
- The last argument is how long ago the browser timestamped the frame, from the timestamp `requestAnimationFrame` supplies. Reading the clock inside the frame instead makes animation curves step.

---

## 8. Text input and IME

The platform ships the page side of text input as `mewui-text-input.js`. The page imports it and hands it the pieces it owns:

```js
const { createTextInputBridge } = await import('./mewui-text-input.js');
const textInputBridge = createTextInputBridge({ app, canvas, field: textInput, wake });
```

The bridge drives composition events, the hidden field and the caret position through the exports in section 5. A host that does not use it has to implement composition, the field and the caret placement itself.

---

## 9. Resources

The browser has no file system to preload from, so files the application reads at runtime are fetched by the page and handed over. The template fetches nothing; the Gallery sample does it this way:

```js
for (const name of app.ResourceFileNames()) {
    const response = await fetch(`./Resources/${name}`);
    app.ApplyResource(name, new Uint8Array(await response.arrayBuffer()));
}
```

`ResourceFileNames` and `ApplyResource` are the application's own exports, not framework members: the app decides what its pages need and where a fetched file goes. Font files are not worth fetching, because this backend draws text with the browser's fonts.

---

## 10. Publishing

`dotnet publish` produces `wwwroot` with `_framework` beside the page files. Serve it as static files, with `application/wasm` for `.wasm`.

Cache pinning matters. `dotnet.js` is the one asset the SDK leaves unfingerprinted, and it carries the hashed names of every other asset, so a host that caches it hands an old manifest to a new deploy and the runtime requests files that no longer exist. Give the loader script a version query, which the template's `main.js` carries over to the runtime import:

```html
<script type="module" src="./main.js?v=DEPLOY_ID"></script>
```

```js
const { dotnet } = await import(`./_framework/dotnet.js${new URL(import.meta.url).search}`);
```

AOT compilation (`-p:RunAOTCompilation=true`) trades a larger download for a faster first frame.

---

## 11. What a browser host cannot do

- One top-level `Window`; dialogs, popups and menus are drawn inside the canvas.
- `Window.ShowDialog` throws; `ShowDialogAsync` is the modal form.
- Native message boxes and file dialogs are not available.
- Clipboard reads return what the page handed over in the last copy or paste gesture.
- `Application.Shutdown()` ends the run but leaves the page loaded.

See [Application Lifecycle](ApplicationLifecycle.md), section 2.5, for the lifetime details.
