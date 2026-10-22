namespace Aprillz.MewUI.MewDock;

/// <summary>
/// The single store of user-visible strings for the docking UI. Each entry is an <see cref="ObservableValue{T}"/>
/// so a host can swap languages at runtime: bound consumers (tooltips) update immediately, transient ones
/// (menus, drag chips) pick up the new value the next time they are shown. Defaults are English.
/// </summary>
public static class MewUIDockString
{
    /// <summary>The title shown for a tab without a name.</summary>
    public static readonly ObservableValue<string> TitleUnnamedTab = new("[Unnamed Tab]");

    /// <summary>The drag chip suffix when one more tab is dragged along.</summary>
    public static readonly ObservableValue<string> DragChipOneMore = new("+1 tab");
    /// <summary>The drag chip suffix when several more tabs are dragged along; {0} is their count.</summary>
    public static readonly ObservableValue<string> DragChipManyMore = new("+{0} tabs");

    /// <summary>The menu item that floats a pane or group.</summary>
    public static readonly ObservableValue<string> MenuFloat = new("Float");
    /// <summary>The menu item that unpins a tool to its auto-hide edge.</summary>
    public static readonly ObservableValue<string> MenuAutoHide = new("Auto Hide");
    /// <summary>The menu item that closes a pane.</summary>
    public static readonly ObservableValue<string> MenuClose = new("Close");
    /// <summary>The menu item that pins an auto-hidden tool into a docked group.</summary>
    public static readonly ObservableValue<string> MemuDock = new("Dock");
    /// <summary>The menu item that closes every other pane in the group.</summary>
    public static readonly ObservableValue<string> MenuCloseOthers = new("Close Others");
    /// <summary>The menu item that closes every pane in the group.</summary>
    public static readonly ObservableValue<string> MenuCloseAll = new("Close All");
    /// <summary>The menu item that splits a pane into a new group below.</summary>
    public static readonly ObservableValue<string> MenuNewVerticalTabGroup = new("New Vertical Tab Group");
    /// <summary>The menu item that splits a pane into a new group to the right.</summary>
    public static readonly ObservableValue<string> MenuNewHorizontalTabGroup = new("New Horizontal Tab Group");
    /// <summary>The menu item that moves a pane to the next group.</summary>
    public static readonly ObservableValue<string> MenuMoveToNextTabGroup = new("Move to Next Tab Group");
    /// <summary>The menu item that moves a pane to the previous group.</summary>
    public static readonly ObservableValue<string> MenuMoveToPreviousTabGroup = new("Move to Previous Tab Group");
    /// <summary>The menu item that maximizes a document group.</summary>
    public static readonly ObservableValue<string> MenuMaximize = new("Maximize");
    /// <summary>The menu item that restores a maximized document group.</summary>
    public static readonly ObservableValue<string> MenuRestore = new("Restore");
    
    /// <summary>The tooltip of the group options button.</summary>
    public static readonly ObservableValue<string> ToolTipOptions = new("Options");
    /// <summary>The tooltip of the auto-hide button.</summary>
    public static readonly ObservableValue<string> ToolTipAutoHide = new("Auto Hide");
    /// <summary>The tooltip of the dock (pin) button.</summary>
    public static readonly ObservableValue<string> ToolTipDock = new("Dock");
    /// <summary>The tooltip of the close button.</summary>
    public static readonly ObservableValue<string> ToolTipClose = new("Close");
    /// <summary>The tooltip of the maximize button.</summary>
    public static readonly ObservableValue<string> ToolTipMaximize = new("Maximize");
    /// <summary>The tooltip of the restore button.</summary>
    public static readonly ObservableValue<string> ToolTipRestore = new("Restore");
    /// <summary>The tooltip of the button that lists the tabs that do not fit in the strip.</summary>
    public static readonly ObservableValue<string> ToolTipHiddenTabs = new("Hidden tabs");
}
