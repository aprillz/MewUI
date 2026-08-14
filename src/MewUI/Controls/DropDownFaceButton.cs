using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>Which end of a drop-down button's chrome a face occupies.</summary>
internal enum DropDownFaceSide
{
    /// <summary>The face fills the chrome, so every corner follows it.</summary>
    Whole,

    /// <summary>The face sits at the left end; its right edge meets a neighbouring face.</summary>
    Left,

    /// <summary>The face sits at the right end; its left edge meets a neighbouring face.</summary>
    Right,
}

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

    /// <summary>Which end of the owner's chrome this face sits at, so it rounds only the outer corners.</summary>
    internal static readonly MewProperty<DropDownFaceSide> FaceSideProperty =
        MewProperty<DropDownFaceSide>.Register<DropDownFaceButton>(nameof(FaceSide),
            DropDownFaceSide.Whole, MewPropertyOptions.AffectsRender);

    internal DropDownFaceSide FaceSide
    {
        get => GetValue(FaceSideProperty);
        set => SetValue(FaceSideProperty, value);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (HasTemplateInstance)
        {
            return;
        }

        // The magnitude comes from CornerRadius, which the template binds to the owner, so a theme that
        // changes its corner radius reaches the faces without the template being rebuilt. Only the side
        // is structural.
        double radius = CornerRadius;
        var corners = FaceSide switch
        {
            DropDownFaceSide.Left => new CornerRadius(radius, 0, 0, radius),
            DropDownFaceSide.Right => new CornerRadius(0, radius, radius, 0),
            _ => new CornerRadius(radius),
        };

        DrawBackgroundAndBorder(
            context,
            GetSnappedBorderBounds(Bounds),
            GetValue(BackgroundProperty),
            Color.Transparent,
            Thickness.Zero,
            corners);
    }
}
