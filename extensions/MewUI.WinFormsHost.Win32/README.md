# Aprillz.MewUI.WinFormsHost.Win32

Hosts Windows Forms controls inside Aprillz.MewUI layouts on Win32.

Use it for controls that cannot be rewritten as MewUI controls: 3D viewers, map
controls, and third-party components shipped without source.

## Supported Platforms

| Target | Status |
|---|---|
| Windows x64 / x86 / ARM64 | Supported |

## Supported .NET Versions

- .NET 8.0 (`net8.0-windows`)
- .NET 10.0 (`net10.0-windows`)

## Usage

```csharp
var calendar = new System.Windows.Forms.MonthCalendar();

new WinFormsHost()
    .Child(calendar)
    .Height(200)
```

The application must run on an STA thread and must perform this startup sequence
before creating any Windows Forms control:

```csharp
System.Windows.Forms.Application.EnableVisualStyles();
System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
```

## DPI

`SetHighDpiMode(PerMonitorV2)` is required, not optional. Windows Forms otherwise
creates its control handles DPI-unaware while MewUI runs per-monitor aware, and
Windows bitmap-stretches the guest: it looks blurry and keeps 96 DPI metrics. The
call only succeeds before the first Windows Forms handle exists, so it cannot be
made from this package.

With it in place a guest on a 144 DPI monitor reports `DeviceDpi = 144` and scales
its fonts and layout by 1.5 like any Windows Forms application.

## How it works

The guest control lives in a native child window layered over the MewUI window.
Windows draws that child window after MewUI presents its frame, so the hosted
control appears on top of MewUI content regardless of element order. This works
on the Direct2D, MewVG, and GDI backends alike.

## Keyboard

The MewUI message loop dispatches messages without calling `IsDialogMessage`, so
Windows Forms would never see Tab, arrow-key group navigation, or mnemonics. This
package installs a thread-local `WH_GETMESSAGE` hook that runs `IsDialogMessage`
for keyboard messages headed into a host, so several controls in one host navigate
the way they do in a Windows Forms application.

Tab crosses the boundary in both directions. Focusing the host lands on the guest's
first tab stop rather than on the host window, and tabbing past the guest's last tab
stop moves on to the next MewUI element. Shift+Tab does the same in reverse.

## Clipping

`ClipToAncestors` (default `true`) intersects the host window with the bounds of
every MewUI ancestor and applies the result as a window region, so a host inside
a `ScrollViewer` is cut at the viewport edge and hidden once scrolled out of
view. Set it to `false` for the unclipped behavior of WPF's `HwndHost`, which
repositions the child window but never clips it.

Scroll bars are drawn over the content rather than reserving width, so a host
that fills the viewport covers them. Reserve `Theme.Metrics.ScrollBarHitThickness`
as scroll viewer padding when that matters.

## Limitations

- Not trim-safe and not NativeAOT-compatible, because Windows Forms is neither.
- MewUI content cannot be drawn over a hosted control, including popups and
  menus that overlap it.

## License

MIT
