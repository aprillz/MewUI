using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

public partial class Window
{
    private int _bitmapCacheInvalidationColorIndex;

    /// <summary>
    /// Gets whether <see cref="BitmapCache"/> regions are covered with a translucent diagnostic
    /// color that changes whenever the cache is rebuilt.
    /// </summary>
    public bool DevToolsBitmapCacheInvalidationOverlayEnabled { get; private set; }

    public event Action<bool>? DevToolsBitmapCacheInvalidationOverlayChanged;

    private void InitializeBitmapCacheDiagnostics()
    {
        KeyBindings.Add(new KeyBinding(
            new KeyGesture(Key.B, ModifierKeys.Primary | ModifierKeys.Shift),
            ToggleBitmapCacheInvalidationOverlay));
    }

    /// <summary>
    /// Toggles visualization of <see cref="BitmapCache"/> regions. The overlay remains visible while
    /// enabled and changes color whenever the cache is rebuilt. It is composited after the cached
    /// image, so the diagnostic color is never stored in the cache.
    /// </summary>
    public void ToggleBitmapCacheInvalidationOverlay()
    {
        DevToolsBitmapCacheInvalidationOverlayEnabled = !DevToolsBitmapCacheInvalidationOverlayEnabled;
        RequestRender();
        DevToolsBitmapCacheInvalidationOverlayChanged?.Invoke(DevToolsBitmapCacheInvalidationOverlayEnabled);
    }

    internal Color NextBitmapCacheInvalidationOverlayColor()
    {
        int index = (_bitmapCacheInvalidationColorIndex++ & int.MaxValue) % 5;
        return index switch
        {
            0 => Color.FromArgb(96, 255, 64, 64),
            1 => Color.FromArgb(96, 64, 192, 255),
            2 => Color.FromArgb(96, 255, 192, 64),
            3 => Color.FromArgb(96, 192, 64, 255),
            _ => Color.FromArgb(96, 64, 255, 128),
        };
    }
}
