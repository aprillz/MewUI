# MewUI.WinFormsHost.Win32.Sample

Shows how to host Windows Forms controls inside a MewUI layout on Win32, for
controls that cannot be rewritten as MewUI controls: 3D viewers, map controls, and
third-party components shipped without source.

`WinFormsHost` lives in this sample rather than in a package. Copy the three files
into your own project and adapt them:

| File | Contents |
|---|---|
| `WinFormsHost.cs` | The `FrameworkElement` that owns the child window |
| `Interop.cs` | The user32/gdi32 entry points it needs |
| `WinFormsHostExtensions.cs` | Fluent helpers for the markup style |

## Usage

```csharp
new WinFormsHost()
    .Child(new System.Windows.Forms.MonthCalendar())
    .Height(200)
```

The application must run on an STA thread and must perform this startup sequence
before creating any Windows Forms control:

```csharp
System.Windows.Forms.Application.EnableVisualStyles();
System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
```

## Running

`dotnet run` picks the Direct2D backend; pass `--gdi` or `--vg` for the others. All
three behave the same.

## How it works

The guest control lives in a native child window layered over the MewUI window.
Windows draws that child window after MewUI presents its frame, so the hosted
control appears on top of MewUI content regardless of element order.

Four details make it work, and each is a bug when missing:

- `WS_CLIPCHILDREN` on the MewUI window. MewUI presents to the whole client area, so
  without it the frame erases the guest, which only repaints on `WM_PAINT`.
- The guest goes inside a `ContainerControl` whose handle is reparented. Reparenting
  the guest's own handle makes Windows Forms recreate it.
- The host window is hidden while the element is detached from the visual tree, which
  is how a `TabControl` holds the content of tabs that are not selected.
- Win32 focus is handed back to the window when MewUI focus moves away, or keystrokes
  keep reaching the guest.

## DPI

`SetHighDpiMode(PerMonitorV2)` is required, not optional. Windows Forms otherwise
creates its control handles DPI-unaware while MewUI runs per-monitor aware, and
Windows bitmap-stretches the guest: it looks blurry and keeps 96 DPI metrics. The
call only succeeds before the first Windows Forms handle exists.

With it in place a guest on a 144 DPI monitor reports `DeviceDpi = 144` and scales
its fonts and layout by 1.5 like any Windows Forms application.

## Keyboard

The MewUI message loop dispatches messages without calling `IsDialogMessage`, so
Windows Forms would never see Tab, arrow-key group navigation, or mnemonics. A
thread-local `WH_GETMESSAGE` hook runs `IsDialogMessage` for keyboard messages headed
into a host.

Tab crosses the boundary in both directions. Focusing the host lands on the guest's
first tab stop rather than on the host window, and tabbing past the guest's last tab
stop moves on to the next MewUI element. Shift+Tab does the same in reverse.

## Clipping

`ClipToAncestors` (default `true`) intersects the host window with the bounds of every
MewUI ancestor and applies the result as a window region, so a host inside a
`ScrollViewer` is cut at the viewport edge and hidden once scrolled out of view. Set it
to `false` for the unclipped behavior of WPF's `HwndHost`, which repositions the child
window but never clips it.

Scroll bars are drawn over the content rather than reserving width, so a host that
fills the viewport covers them. Reserve `Theme.Metrics.ScrollBarHitThickness` as scroll
viewer padding when that matters.

## Limitations

- Not trim-safe and not NativeAOT-compatible, because Windows Forms is neither.
- MewUI content drawn into the window surface cannot appear over a hosted control.
  Popups, menus and tooltips are unaffected, because they get their own OS windows.
