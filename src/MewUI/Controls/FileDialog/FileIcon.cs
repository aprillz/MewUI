using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Platform;

public enum FileIconKind
{
    File,
    Folder,
    Drive,
}

/// <summary>
/// Lightweight vector icon (folder / file / drive) drawn directly, theme-aware.
/// Prototype stand-in for the core FileIconKind asset bundle.
/// </summary>
internal sealed class FileIconElement : FrameworkElement
{
    private FileIconKind _kind;
    private readonly double _box;

    // Optional shell-icon target (file rows). When set and the OS provides a type icon, it is drawn
    // instead of the vector. Places/sidebar entries leave this null and stay vector.
    private string? _shellPath;
    private bool _shellIsDirectory;
    private ShellPlaceKind? _shellPlace;
    private string? _placePath;
    private IImage? _shellImage;
    private string? _shellImageKey;

    // Finder-style async upgrade: the placeholder above is shown immediately; the real per-file icon is
    // resolved on a background thread and swapped in here when ready. Cache is shared across all icons so a
    // revisited folder reuses results. _realImage (if set) is drawn in preference to the placeholder.
    private IImage? _realImage;
    private string? _realKey;
    private static readonly Dictionary<string, ImageSource?> _realCache = new(StringComparer.Ordinal);
    private static readonly object _realGate = new();

    public FileIconElement(double box = 16)
    {
        _box = box;
    }

    /// <summary>Points this icon at a filesystem entry so it can show the OS shell type icon (by extension,
    /// non-blocking). Pass null to use the bundled vector icon only.</summary>
    public void SetShellTarget(string? path, bool isDirectory)
    {
        if (_shellPath == path && _shellIsDirectory == isDirectory)
        {
            return;
        }
        _shellPath = path;
        _shellIsDirectory = isDirectory;
        ResetRealIcon();
        InvalidateVisual();
    }

    /// <summary>Points this icon at a sidebar place so it can show the distinctive special-folder/drive icon
    /// (resolved from a fixed system resource, non-blocking). Pass null to use the vector icon only.</summary>
    public void SetShellPlace(ShellPlaceKind? kind, string? path = null)
    {
        if (_shellPlace == kind && _placePath == path)
        {
            return;
        }
        _shellPlace = kind;
        _placePath = path;
        ResetRealIcon();
        InvalidateVisual();
    }

    // Drop any upgraded real icon when this element is recycled to a different item (virtualization).
    private void ResetRealIcon()
    {
        _realImage?.Dispose();
        _realImage = null;
        _realKey = null;
    }

    public FileIconKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value)
            {
                return;
            }
            _kind = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureContent(Size availableSize) => new(_box, _box);

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // Use the full allocated square as the box (no resize). Each shape snaps its own edges to the pixel
        // grid for crispness; because every shape edge is a fraction (< 1.0) of the box it always stays
        // inside Bounds, so nothing is clipped.
        double scale = GetDpi() / 96.0;
        double side = Math.Min(bounds.Width, bounds.Height);
        double x = bounds.X + (bounds.Width - side) / 2;
        double y = bounds.Y + (bounds.Height - side) / 2;
        var box = new Rect(x, y, side, side);

        if (_shellPlace != null || _shellPath != null)
        {
            // Snap the destination to the device pixel grid and request the bitmap at exactly that device
            // pixel size, so the backend draws it 1:1 with no scaling. (MewVG's texture minification is
            // fuzzy at non-100% scaling; drawing 1:1 sidesteps it. GDI/D2D are unaffected.)
            var snapped = SnapRect(box, scale);
            if (TryGetShellImage(snapped, scale, out var placeholderImage))
            {
                // Draw the upgraded real icon if it has arrived, otherwise the generic placeholder.
                var drawn = _realImage ?? placeholderImage;
                var previousQuality = context.ImageScaleQuality;
                context.ImageScaleQuality = ImageScaleQuality.HighQuality;
                context.DrawImage(drawn, snapped);
                context.ImageScaleQuality = previousQuality;

                // Kick off (once per key) the background resolution of the real per-file icon.
                RequestRealIcon(snapped, scale);
                return;
            }
        }

        bool dark = Application.Current.Theme.IsDark;

        switch (_kind)
        {
            case FileIconKind.Folder:
                DrawFolder(context, box, scale);
                break;
            case FileIconKind.Drive:
                DrawDrive(context, box, dark, scale);
                break;
            default:
                DrawFile(context, box, scale);
                break;
        }
    }

    private bool TryGetShellImage(Rect snappedBox, double scale, out IImage shellImage)
    {
        // Exact device pixel size of the (already pixel-snapped) box. The provider returns the icon at
        // exactly this size, so the backend draws it 1:1 (no scaling -> crisp at any DPI).
        int targetPx = Math.Max(1, (int)Math.Round(snappedBox.Width * scale));
        var provider = ShellIconProviders.Current;

        string key = _shellPlace != null
            ? $"place:{_shellPlace}|{targetPx}"
            : $"{(_shellIsDirectory ? "<dir>" : Path.GetExtension(_shellPath!).ToLowerInvariant())}|{targetPx}";

        if (_shellImageKey != key)
        {
            _shellImage?.Dispose();
            var source = _shellPlace != null
                ? provider.GetPlaceIcon(_shellPlace.Value, targetPx)
                : provider.GetIcon(_shellPath!, _shellIsDirectory, targetPx);
            _shellImage = source?.CreateImage(GetGraphicsFactory());
            _shellImageKey = key;
        }

        shellImage = _shellImage!;
        return _shellImage != null;
    }

    // Finder-style: resolve the real per-file icon off the UI thread, swap it in when ready. Places (no real
    // path) and platforms whose GetRealIcon returns null simply keep the placeholder.
    private void RequestRealIcon(Rect snappedBox, double scale)
    {
        // File rows carry the path directly; sidebar places carry it via SetShellPlace.
        string? realPath = _shellPath ?? _placePath;
        if (realPath == null)
        {
            return;
        }

        int targetPx = Math.Max(1, (int)Math.Round(snappedBox.Width * scale));
        string key = $"{realPath}|{targetPx}";
        if (_realKey == key)
        {
            return; // already requested/applied for this element and size
        }
        _realKey = key;

        lock (_realGate)
        {
            if (_realCache.TryGetValue(key, out var cached))
            {
                ApplyReal(cached, key);
                return;
            }
        }

        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher == null)
        {
            return;
        }

        string path = realPath;
        var provider = ShellIconProviders.Current;
        Task.Run(() =>
        {
            ImageSource? real = null;
            try
            {
                // Real-path query (may touch a slow/network volume) - background only; UI keeps the placeholder.
                real = provider.GetRealIcon(path, targetPx);
            }
            catch
            {
                // Leave placeholder.
            }
            lock (_realGate)
            {
                _realCache[key] = real;
            }
            dispatcher.BeginInvoke(() => ApplyReal(real, key));
        });
    }

    private void ApplyReal(ImageSource? real, string key)
    {
        if (_realKey != key || real == null)
        {
            return; // element was recycled to another item, or no real icon available
        }
        _realImage?.Dispose();
        _realImage = real.CreateImage(GetGraphicsFactory());
        InvalidateVisual();
    }

    private static double Snap(double value, double scale) => Math.Round(value * scale) / scale;

    private static Rect SnapRect(Rect rect, double scale)
    {
        double left = Snap(rect.X, scale);
        double top = Snap(rect.Y, scale);
        double right = Snap(rect.X + rect.Width, scale);
        double bottom = Snap(rect.Y + rect.Height, scale);
        return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    // Folder tint follows the host OS convention: Windows manila-yellow, macOS blue, Linux blue-gray.
    private static (Color Back, Color Front) FolderColors()
    {
        if (OperatingSystem.IsMacOS())
        {
            return (new Color(0x4A, 0x90, 0xD9), new Color(0x5E, 0xA8, 0xEE));
        }
        if (OperatingSystem.IsLinux())
        {
            return (new Color(0x6B, 0x93, 0xB5), new Color(0x86, 0xB0, 0xD2));
        }
        return (new Color(0xE2, 0xAC, 0x45), new Color(0xF8, 0xC8, 0x5C));
    }

    private static void DrawFolder(IGraphicsContext context, Rect box, double scale)
    {
        double s = box.Width;
        var (back, front) = FolderColors();
        double radius = s * 0.10;

        // Back panel + tab (darker), then a brighter front pocket leaving a thin back rim on top.
        // Taller body so the folder doesn't look flat. Edges snapped to the pixel grid.
        var folderInset = 0.08;// 0.10;
        var tab = SnapRect(new Rect(box.X + s * folderInset, box.Y + s * 0.16, s * 0.40, s * 0.16), scale);
        context.FillRoundedRectangle(tab, radius, radius, back);

        var bodyBack = SnapRect(new Rect(box.X + s * 0.08, box.Y + s * 0.24, s * 0.84, s * 0.66), scale);
        context.FillRoundedRectangle(bodyBack, radius, radius, back);

        var pocket = SnapRect(new Rect(box.X + s * 0.08, box.Y + s * 0.38, s * 0.84, s * 0.52), scale);
        context.FillRoundedRectangle(pocket, radius, radius, front);
    }

    // Page geometry cached in LOCAL coordinates (origin 0,0) per box size; positioned at draw via Translate.
    // Local + snapped, so it is reused across rows without baking in an absolute position.
    private static readonly Dictionary<double, (PathGeometry Page, PathGeometry Corner)> _fileGeometry = new();

    private static (PathGeometry Page, PathGeometry Corner) GetFileGeometry(double s, double scale)
    {
        if (_fileGeometry.TryGetValue(s, out var cached))
        {
            return cached;
        }

        double left = Snap(s * 0.16, scale);
        double top = Snap(s * 0.06, scale);
        double right = Snap(s * 0.84, scale);
        double bottom = Snap(s * 0.94, scale);
        double foldX = Snap(right - s * 0.28, scale);
        double foldY = Snap(top + s * 0.28, scale);

        var page = new PathGeometry();
        page.MoveTo(left, top);
        page.LineTo(foldX, top);
        page.LineTo(right, foldY);
        page.LineTo(right, bottom);
        page.LineTo(left, bottom);
        page.Close();

        var corner = new PathGeometry();
        corner.MoveTo(foldX, top);
        corner.LineTo(foldX, foldY);
        corner.LineTo(right, foldY);
        corner.Close();

        cached = (page, corner);
        _fileGeometry[s] = cached;
        return cached;
    }

    private static void DrawFile(IGraphicsContext context, Rect box, double scale)
    {
        // Flat light page + darker folded corner (no outline), matching the reference.
        var pageColor = new Color(0xE6, 0xE9, 0xEC);
        var foldColor = new Color(0xC3, 0xC8, 0xCE);

        var (page, corner) = GetFileGeometry(box.Width, scale);

        // Translate the cached local geometry to this icon's (pixel-snapped) position.
        var previous = context.GetTransform();
        context.Translate(Snap(box.X, scale), Snap(box.Y, scale));
        context.FillPath(page, pageColor);
        context.FillPath(corner, foldColor);
        context.SetTransform(previous);
    }

    private static void DrawDrive(IGraphicsContext context, Rect box, bool dark, double scale)
    {
        double s = box.Width;
        var body = dark ? new Color(0x7E, 0x8B, 0x99) : new Color(0x8A, 0x97, 0xA6);
        var led = new Color(0x6F, 0xCF, 0x6F);

        var bodyRect = SnapRect(new Rect(box.X + s * 0.10, box.Y + s * 0.32, s * 0.80, s * 0.36), scale);
        context.FillRoundedRectangle(bodyRect, s * 0.16, s * 0.16, body);

        var ledRect = new Rect(box.X + s * 0.68, box.Y + s * 0.46, s * 0.09, s * 0.09);
        context.FillEllipse(ledRect, led);
    }
}
