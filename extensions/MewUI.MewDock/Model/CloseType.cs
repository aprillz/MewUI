namespace Aprillz.MewUI.MewDock.Model;

/// <summary>
/// How a tab responds to the close affordance (port of FlexLayout model/ICloseType.ts).
/// </summary>
public enum CloseType
{
    /// <summary>Close if selected or hovered (the x is visible). On touch only the selected tab closes.</summary>
    Visible = 1,

    /// <summary>Close always, whether selected or not (e.g. a custom close image is tapped).</summary>
    Always = 2,

    /// <summary>Close only if selected.</summary>
    Selected = 3,
}
