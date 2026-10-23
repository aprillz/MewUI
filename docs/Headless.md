# Headless Testing

`Aprillz.MewUI.Headless` runs MewUI windows in unit and integration tests without a display server: windows, the dispatcher, timers, layout, bindings and popups work as in a running application, input is sent from the test, and an optional render mode draws every frame into an offscreen surface whose pixels the test can read.

The package is modelled on Avalonia.Headless. Its API is explicit calls only, with no test-framework attributes and no reflection, so test projects publish with trimming and NativeAOT.

---

## 1. Getting started

Reference the package and one graphics backend. The backend measures text, so layout in a headless test uses the same font metrics as the real application:

| Operating system | Backend package | Register call |
|---|---|---|
| Windows | `Aprillz.MewUI.Backend.Gdi` or `Aprillz.MewUI.Backend.Direct2D` | `GdiBackend.Register()` / `Direct2DBackend.Register()` |
| Linux | `Aprillz.MewUI.Backend.MewVG.X11` | `MewVGX11Backend.Register()` |
| macOS | `Aprillz.MewUI.Backend.MewVG.MacOS` | `MewVGMacOSBackend.Register()` |

No platform package (`Aprillz.MewUI.Platform.*`) is referenced: the headless package is the platform host. One backend is registered per process, before the session starts.

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;

GdiBackend.Register();

using var session = HeadlessSession.Start(new HeadlessOptions
{
    Rendering = HeadlessRendering.MeasureOnly,   // or Offscreen
    Dpi = 96,
    Clock = HeadlessClock.Virtual,               // or RealTime
});

var button = new Button().Content("OK");
int clicks = 0;
button.Click += () => clicks++;

var window = session.Run(() =>
{
    var created = new Window { WindowSize = WindowSize.Resizable(400, 300), Content = button };
    created.Show();
    return created;
});

window.SendClick(button.CenterInWindow());
Debug.Assert(clicks == 1);
```

`HeadlessSession.Start` runs a real `Application` on a dedicated UI thread and returns once the application loop is running. One session exists per process at a time; disposing it closes every window, ends the run and releases the thread, after which a new session can start.

On Linux the session runs without `DISPLAY`: the MewVG backend measures text with FreeType and opens no X connection in measure-only mode, and render mode draws on a surfaceless EGL context (section 7). On macOS a plain SSH session is enough, including render mode.

---

## 2. Running code on the UI thread

Everything that touches elements runs on the session's UI thread. `Run` executes a delegate there and returns its result; an exception thrown inside is rethrown to the caller:

```csharp
session.Run(() => textBox.Focus());
string text = session.Run(() => textBox.Text);
```

`RunAsync` takes an async delegate; every `await` inside it resumes on the UI thread, which is what view-model code that awaits expects:

```csharp
await session.RunAsync(async () =>
{
    await viewModel.LoadAsync();
    Assert.Equal(3, listBox.ItemsSource.Count);
});
```

Calling `Run` from the UI thread itself executes the delegate inline, so helpers can be used from either side.

---

## 3. Windows

A window is shown with `Show()` as in an application. The headless host attaches a window backend that has no native window: its size comes from `WindowSize` (or the window default), its DPI from `HeadlessOptions.Dpi`, and `ClientToScreen` is the identity, so screen coordinates equal window coordinates.

Popups (`ComboBox` drop-downs, context menus, tooltips) open inside the owner window's surface, because native popup windows cannot exist without a display. They are hit-testable and closable through input like any other content.

Modal dialogs (`ShowDialog`) run a nested loop on the same pump, so a test can open a dialog from `Run` and drive it from another `Run` call.

---

## 4. Input

Input is sent with extension methods on `Window`. They call the same `WindowInputRouter` entry points the platform backends use, so hit testing, mouse-over, focus, popup close policy and event bubbling behave exactly as in the running application; this is where MewUI differs from Avalonia.Headless, which synthesizes raw input events. Each method runs the queued dispatcher work afterwards (`RunJobs`), so the next line of the test sees the result.

| Method | What it does |
|---|---|
| `SendMouseMove(point)` | Moves the pointer; updates mouse-over and hover state. |
| `SendMouseDown(point, button)` / `SendMouseUp(point, button)` | Presses or releases a button at the point. |
| `SendClick(point, button)` | Move, press and release with click count 1. |
| `SendDoubleClick(point, button)` | A click followed by a press and release with click count 2. |
| `SendMouseWheel(point, delta)` | Wheel in notches, positive Y scrolls up. |
| `SendKeyDown(key, modifiers)` / `SendKeyUp(key, modifiers)` | Preview, bubbling from the focused element, access keys, and for an unhandled Tab the focus navigation, in the order the platforms raise them. |
| `SendKeyPress(key, modifiers)` | Key down followed by key up. |
| `SendTextInput(text)` | Delivers text to the focused text input, as typing does after the key events. |
| `SendDragDrop(point, data, effects)` | An external drag entering, moving over and dropping at the point; returns the effect the target chose. |

Points are window client coordinates in DIPs. `element.CenterInWindow()` returns the center of an element's arranged bounds, which is what a click on a control needs. The methods may be called from the test thread; they forward to the UI thread and return once the input has been handled.

Key events carry no platform key code and no text; a test that needs the typed character sends `SendTextInput` after the key, as every platform delivers it. Touch and pen input are not simulated.

---

## 5. Jobs and time

`session.RunJobs()` drains the dispatcher queue and the timers that are due, then lays out and draws every window that asked for a frame, repeating until nothing is pending. The input methods call it for you; call it yourself after `BeginInvoke`, a binding change or an animation start.

With the default `HeadlessClock.Virtual`, time does not pass by itself. `session.AdvanceTime(TimeSpan)` moves the clock and runs every timer and animation frame on the way, stopping at each timer's due time so re-arming timers fire at their interval:

```csharp
var timer = session.Run(() =>
{
    var created = new DispatcherTimer(TimeSpan.FromMilliseconds(300));
    created.Tick += () => ticks++;
    created.Start();
    return created;
});

session.AdvanceTime(TimeSpan.FromMilliseconds(299));   // ticks == 0
session.AdvanceTime(TimeSpan.FromMilliseconds(1));     // ticks == 1
```

Animations are pulsed with the same clock: an `AnimationClock` started in the session advances only through `AdvanceTime`, in frames of at most 16 ms. Pausing and resuming an animation clock measures the paused span on the wall clock.

`HeadlessClock.RealTime` makes timers expire on the wall clock, as in an application; `AdvanceTime` is rejected on such a session.

---

## 6. Services

The session owns the platform services the application sees, so a test scripts them instead of a dialog appearing:

| Member | Behaviour |
|---|---|
| `session.Clipboard` | An in-memory clipboard; `Text` reads and writes it, `Application.Current.PlatformServices.Clipboard` sees the same value. |
| `session.MessageBoxes` | `Enqueue(bool?)` scripts the answer of the next `NativeMessageBox.Show` (true, false or null for cancel); an empty queue answers cancel. `Calls` records every request with its text, caption, buttons and result. |
| `session.FileDialogs` | `EnqueueOpen(paths)`, `EnqueueSave(path)` and `EnqueueFolder(path)` script the next native file dialogs; an empty queue answers cancel. `Calls` records every request. |

Shell icons and mounted volumes are empty, and the system theme variant is `HeadlessOptions.SystemTheme` (light by default).

---

## 7. Render mode

`HeadlessRendering.MeasureOnly` (the default) runs layout and the render pass but discards every drawing call, which keeps tests fast and backend-independent.

`HeadlessRendering.Offscreen` draws every frame into an offscreen surface of the registered backend. `window.CaptureFrame()` returns the pixels of the last frame as a `HeadlessFrame`: `Width`, `Height` and `Stride` in device pixels and bytes, `DpiScale`, the BGRA32 `Pixels` span, and `GetPixel(x, y)` for a single color:

```csharp
using var session = HeadlessSession.Start(new HeadlessOptions { Rendering = HeadlessRendering.Offscreen });
...
HeadlessFrame frame = window.CaptureFrame();
Assert.Equal(Color.FromArgb(255, 40, 120, 200), frame.GetPixel(30, 30));
```

Render mode is available on Windows (GDI, Direct2D), macOS (MewVG Metal) and Linux (MewVG). The frame is drawn through the same retained-mode path a window on screen uses, so a frame captured after a change shows exactly the repaint the user would see.

On Linux the MewVG backend draws the frame on an OpenGL context of Mesa's surfaceless EGL platform (`EGL_MESA_platform_surfaceless`), so no X server, Wayland compositor or DRM device is needed. The machine needs Mesa's `libEGL.so.1` and `libGL.so.1` with a driver that offers desktop OpenGL 3.1 or later; on a machine without a GPU, Mesa's llvmpipe rasterizer serves (Debian and Ubuntu: `libegl1`, `libgl1`, `libgl1-mesa-dri`). When the surfaceless platform is missing, `HeadlessSession.Start` throws `PlatformNotSupportedException` naming what is absent. With a GPU present, Mesa renders on it through its render node (`/dev/dri/renderD*`); `LIBGL_ALWAYS_SOFTWARE=1` forces llvmpipe when a test run must not depend on the GPU.

---

## 8. Test framework fixtures

The package has no framework adapters; a fixture starts the session once per test assembly and tests use `session.Run`. The session is process-wide, so test classes that share it must not run in parallel.

### xUnit

```csharp
public sealed class HeadlessFixture : IDisposable
{
    public HeadlessFixture()
    {
        GdiBackend.Register();
        Session = HeadlessSession.Start();
    }

    public HeadlessSession Session { get; }

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition("headless", DisableParallelization = true)]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessFixture>
{
}

[Collection("headless")]
public sealed class ButtonTests(HeadlessFixture fixture)
{
    [Fact]
    public void Click_RaisesClick()
    {
        var button = new Button().Content("OK");
        int clicks = 0;
        button.Click += () => clicks++;
        var window = fixture.Session.Run(() =>
        {
            var created = new Window { Content = button };
            created.Show();
            return created;
        });

        window.SendClick(button.CenterInWindow());

        Assert.Equal(1, clicks);
        fixture.Session.Run(window.Close);
    }
}
```

### NUnit

```csharp
[SetUpFixture]
public sealed class HeadlessSetup
{
    public static HeadlessSession Session { get; private set; } = null!;

    [OneTimeSetUp]
    public void Start()
    {
        GdiBackend.Register();
        Session = HeadlessSession.Start();
    }

    [OneTimeTearDown]
    public void Stop() => Session.Dispose();
}

[TestFixture, NonParallelizable]
public sealed class ButtonTests
{
    [Test]
    public void Click_RaisesClick()
    {
        var session = HeadlessSetup.Session;
        ...
    }
}
```

### MSTest

```csharp
[TestClass]
public static class AssemblyFixture
{
    public static HeadlessSession Session { get; private set; } = null!;

    [AssemblyInitialize]
    public static void Start(TestContext context)
    {
        GdiBackend.Register();
        Session = HeadlessSession.Start();
    }

    [AssemblyCleanup]
    public static void Stop() => Session.Dispose();
}
```

MSTest runs classes sequentially unless `[assembly: Parallelize]` is declared; `[assembly: DoNotParallelize]` states it explicitly.

### Choosing the backend per operating system

A test assembly that runs on every operating system registers the backend of the one it runs on:

```csharp
if (OperatingSystem.IsWindows()) GdiBackend.Register();
else if (OperatingSystem.IsMacOS()) MewVGMacOSBackend.Register();
else MewVGX11Backend.Register();
```

---

## 9. Comparison with Avalonia.Headless

| Avalonia.Headless | MewUI.Headless | Note |
|---|---|---|
| `HeadlessUnitTestSession.StartNew(typeof(App))` | `HeadlessSession.Start(options)` | MewUI runs a real `Application` on its own thread; no app class is needed. |
| `session.Dispatch(...)` | `session.Run(...)`, `session.RunAsync(...)` | `RunAsync` keeps awaits on the UI thread. |
| `Dispatcher.UIThread.RunJobs()` | `session.RunJobs()` | Also lays out and draws pending windows. |
| `AvaloniaHeadlessPlatform.ForceRenderTimerTick()` | `session.AdvanceTime(...)` / `RunJobs()` | Timers and animations run on a virtual clock by default. |
| `UseHeadlessDrawing = true` / `false` | `HeadlessRendering.MeasureOnly` / `Offscreen` | Measure-only still measures text with the real backend. |
| `window.CaptureRenderedFrame()` | `window.CaptureFrame()` | BGRA32 pixels with stride and DPI scale. |
| `MouseDown`, `MouseMove`, `MouseUp`, `MouseWheel` | `SendMouseDown`, `SendMouseMove`, `SendMouseUp`, `SendMouseWheel`, plus `SendClick` and `SendDoubleClick` | MewUI routes through the window input router, so focus, hover and popup policy are the real ones. |
| `KeyPress`, `KeyRelease`, `KeyTextInput` | `SendKeyDown`, `SendKeyUp`, `SendKeyPress`, `SendTextInput` | Key down includes access keys and Tab navigation, as every platform backend does. |
| `DragDrop(point, type, data, effects)` | `SendDragDrop(point, data, effects)` | One call performs enter, over and drop. |
| `[AvaloniaFact]`, `[AvaloniaTest]` | Fixtures in section 8 | No framework adapters, no reflection. |
| Headless clipboard, no dialogs | `session.Clipboard`, `session.MessageBoxes`, `session.FileDialogs` | Dialogs are scripted and recorded. |
