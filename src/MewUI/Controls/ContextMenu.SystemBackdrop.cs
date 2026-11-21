using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;

namespace Aprillz.MewUI.Controls;

public sealed partial class ContextMenu
{
    // The theme's fill as a tint over a material, moved away from the text color: the color behind stays visible and
    // the rows stay readable over a page of the opposite brightness.
    private const byte MATERIAL_TINT_ALPHA = 156;
    private const double MATERIAL_TINT_DEPTH = 0.6;
    // A blur radius is three deviations, so these match the deviations of the platform materials.
    private const double OVERLAY_BLUR_RADIUS = 36;
    private const double OVERLAY_ACRYLIC_RADIUS = 90;
    private const float OVERLAY_ACRYLIC_SATURATION = 1.6f;

    private static readonly ImageFilter _overlayBlur = new BlurFilter(OVERLAY_BLUR_RADIUS);
    private static readonly ImageFilter _overlayAcrylic =
        new ColorMatrixFilter(SaturationMatrix(OVERLAY_ACRYLIC_SATURATION), new BlurFilter(OVERLAY_ACRYLIC_RADIUS));

    public static readonly MewProperty<SystemBackdrop> SystemBackdropProperty =
        MewProperty<SystemBackdrop>.Register<ContextMenu>(nameof(SystemBackdrop), SystemBackdrop.None, MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.OnSystemBackdropChanged());

    /// <summary>
    /// Gets or sets the material behind the menu; submenus opened from it follow. A menu in its own window gets the
    /// platform's material, and a menu drawn inside its owner's surface the same look from what that surface holds. Where
    /// neither can be shown, <see cref="UIElement.BackdropFallbackBrush"/> is drawn under the background.
    /// </summary>
    public SystemBackdrop SystemBackdrop
    {
        get => GetValue(SystemBackdropProperty);
        set => SetValue(SystemBackdropProperty, value);
    }

    internal override bool FillsBoundsOpaquely => SystemBackdrop == SystemBackdrop.None && base.FillsBoundsOpaquely;

    internal override bool MayDrawBackdrop => SystemBackdrop != SystemBackdrop.None || base.MayDrawBackdrop;

    internal override ImageFilter? EffectiveBackdropFilter
        => BackdropFilter ?? (Parent is PopupChrome { HostSurface: null } ? OverlayFilter : null);

    // The platform's Mica shows the wallpaper, which a surface cannot read, so the overlay comes down to the acrylic.
    private ImageFilter? OverlayFilter => SystemBackdrop switch
    {
        SystemBackdrop.None => null,
        SystemBackdrop.Blur => _overlayBlur,
        _ => _overlayAcrylic,
    };

    /// <summary>The fill drawn over the material: a background set on the menu as given, the theme's as a tint.</summary>
    private Color BackgroundOverBackdrop
    {
        get
        {
            var background = Background;
            if (SystemBackdrop == SystemBackdrop.None || background.A < 255 ||
                PropertyStore.GetSource(BackgroundProperty.Id) != ValueSource.Style)
            {
                return background;
            }

            return background.Lerp(Theme.IsDark ? Color.Black : Color.White, MATERIAL_TINT_DEPTH).WithAlpha(MATERIAL_TINT_ALPHA);
        }
    }

    private void OnSystemBackdropChanged()
    {
        NoteBackdropChanged();
        if (Parent is PopupChrome chrome)
        {
            if (chrome.HostSurface != null)
            {
                chrome.HostSurface.SystemBackdrop = SystemBackdrop;
            }

            // The shadow stays out of the box while a material shows through it.
            chrome.InvalidateVisual();
        }
    }

    private void DrawNativeBackdropFallback(IGraphicsContext context, Rect bounds)
    {
        if (SystemBackdrop != SystemBackdrop.None &&
            BackdropFallbackBrush is Brush fallback &&
            Parent is PopupChrome { HostSurface: Window surface } &&
            surface.ActualSystemBackdrop == SystemBackdrop.None)
        {
            context.FillRoundedRectangle(bounds, CornerRadius, CornerRadius, fallback);
        }
    }

    /// <summary>A submenu takes the material and whatever of its look was set on this menu rather than by the theme.</summary>
    private void CopySystemBackdropTo(ContextMenu subMenu)
    {
        if (SystemBackdrop == SystemBackdrop.None)
        {
            return;
        }

        subMenu.SystemBackdrop = SystemBackdrop;
        if (PropertyStore.GetSource(BackdropFallbackBrushProperty.Id) == ValueSource.Local)
        {
            subMenu.BackdropFallbackBrush = BackdropFallbackBrush;
        }

        if (PropertyStore.GetSource(BackgroundProperty.Id) == ValueSource.Local)
        {
            subMenu.Background = Background;
        }
    }

    private static float[] SaturationMatrix(float saturation)
    {
        const float LUMA_RED = 0.2126f;
        const float LUMA_GREEN = 0.7152f;
        const float LUMA_BLUE = 0.0722f;
        float rest = 1 - saturation;
        return
        [
            LUMA_RED * rest + saturation, LUMA_GREEN * rest, LUMA_BLUE * rest, 0, 0,
            LUMA_RED * rest, LUMA_GREEN * rest + saturation, LUMA_BLUE * rest, 0, 0,
            LUMA_RED * rest, LUMA_GREEN * rest, LUMA_BLUE * rest + saturation, 0, 0,
            0, 0, 0, 1, 0,
        ];
    }
}
