using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A face part of the drop-down button family. It paints only its own fill, with per-corner radii so
/// the outer corners follow the owner's chrome while the edge shared with the neighbouring face
/// stays square. The owner's chrome draws the border.
/// </summary>
internal sealed class DropDownFaceButton : Button
{
    // Removes beforefieldinit so the registration below runs on first instantiation rather than on
    // first static field access, which instantiating alone does not guarantee.
    static DropDownFaceButton() { }

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<DropDownFaceButton>(DefaultStyles.CreateDropDownFaceStyle);

    /// <summary>Per-corner radius of this face's fill.</summary>
    internal CornerRadius FaceCornerRadius { get; set; }

    protected override void OnRender(IGraphicsContext context)
    {
        if (HasTemplateInstance)
        {
            return;
        }

        DrawBackgroundAndBorder(
            context,
            GetSnappedBorderBounds(Bounds),
            GetValue(BackgroundProperty),
            Color.Transparent,
            Thickness.Zero,
            FaceCornerRadius);
    }
}
