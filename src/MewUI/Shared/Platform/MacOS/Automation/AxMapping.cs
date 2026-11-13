using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Platform.MacOS.Automation;

/// <summary>Translates the automation tree's roles and states to NSAccessibility roles, subroles, and values.</summary>
internal static class AxMapping
{
    internal static string Role(AutomationPeer peer)
    {
        var type = peer.GetControlType();
        switch (type)
        {
            case AutomationControlType.Button:
                return peer is IToggleProvider ? "AXCheckBox" : "AXButton";
            case AutomationControlType.CheckBox:
                return "AXCheckBox";
            case AutomationControlType.RadioButton:
            case AutomationControlType.TabItem:
                return "AXRadioButton";
            case AutomationControlType.ComboBox:
                return "AXPopUpButton";
            case AutomationControlType.Edit:
                return OwnerOf(peer) is MultiLineTextBox ? "AXTextArea" : "AXTextField";
            case AutomationControlType.Text:
                return "AXStaticText";
            case AutomationControlType.Hyperlink:
                return "AXLink";
            case AutomationControlType.Image:
                return "AXImage";
            case AutomationControlType.List:
                return "AXList";
            case AutomationControlType.ListItem:
                // A list item that only names itself reads as its text; one with content is a group of it.
                return peer.GetChildren().Count == 0 ? "AXStaticText" : "AXGroup";
            case AutomationControlType.Tree:
                return "AXOutline";
            case AutomationControlType.DataGrid:
                return "AXTable";
            case AutomationControlType.TreeItem:
            case AutomationControlType.DataItem:
                return "AXRow";
            case AutomationControlType.Tab:
                return "AXTabGroup";
            case AutomationControlType.Menu:
                return "AXMenu";
            case AutomationControlType.MenuBar:
                return "AXMenuBar";
            case AutomationControlType.MenuItem:
                return peer.GetParent()?.GetControlType() == AutomationControlType.MenuBar ? "AXMenuBarItem" : "AXMenuItem";
            case AutomationControlType.Slider:
                return "AXSlider";
            case AutomationControlType.Spinner:
                return "AXIncrementor";
            case AutomationControlType.ProgressBar:
                return "AXProgressIndicator";
            case AutomationControlType.ScrollBar:
                return "AXScrollBar";
            case AutomationControlType.Pane:
                return OwnerOf(peer) is ScrollViewer ? "AXScrollArea" : "AXGroup";
            case AutomationControlType.ToolBar:
                return "AXToolbar";
            case AutomationControlType.ToolTip:
                return "AXHelpTag";
            case AutomationControlType.HeaderItem:
                return "AXButton";
            default:
                return "AXGroup";
        }
    }

    /// <summary>The subrole that refines <see cref="Role"/>, or null.</summary>
    internal static string? Subrole(AutomationPeer peer)
    {
        var type = peer.GetControlType();
        if (type == AutomationControlType.TabItem)
        {
            return "AXTabButton";
        }
        else if (type == AutomationControlType.Button && peer is IToggleProvider)
        {
            return "AXToggle";
        }
        else if (type == AutomationControlType.Edit && OwnerOf(peer) is PasswordBox)
        {
            return "AXSecureTextField";
        }
        else if (OwnerOf(peer) is ToggleSwitch)
        {
            return "AXSwitch";
        }
        else
        {
            return null;
        }
    }

    /// <summary>True when the element reports its selection as its value, as AppKit radio buttons do.</summary>
    internal static bool SelectionIsValue(AutomationPeer peer)
        => peer.GetControlType() is AutomationControlType.RadioButton or AutomationControlType.TabItem;

    internal static int ToggleValue(ToggleState state) => state switch
    {
        ToggleState.On => 1,
        ToggleState.Indeterminate => 2,
        _ => 0,
    };

    private static UIElement? OwnerOf(AutomationPeer peer) => peer is UIElementAutomationPeer elementPeer ? elementPeer.Owner : null;
}
