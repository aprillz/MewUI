namespace Aprillz.MewUI.MewDock;

/// <summary>
/// The single store of user-visible strings for the docking UI. Each entry is an <see cref="ObservableValue{T}"/>
/// so a host can swap languages at runtime: bound consumers (tooltips) update immediately, transient ones
/// (menus, drag chips) pick up the new value the next time they are shown. Defaults are English.
/// </summary>
public static class MewUIDockString
{
    public static readonly ObservableValue<string> TitleUnnamedTab = new("[Unnamed Tab]");

    public static readonly ObservableValue<string> DragChipOneMore = new("+1 tab");
    public static readonly ObservableValue<string> DragChipManyMore = new("+{0} tabs");

    public static readonly ObservableValue<string> MenuFloat = new("Float");
    public static readonly ObservableValue<string> MenuAutoHide = new("Auto Hide");
    public static readonly ObservableValue<string> MenuClose = new("Close");
    public static readonly ObservableValue<string> MemuDock = new("Dock");
    public static readonly ObservableValue<string> MenuCloseOthers = new("Close Others");
    public static readonly ObservableValue<string> MenuCloseAll = new("Close All");
    public static readonly ObservableValue<string> MenuNewVerticalTabGroup = new("New Vertical Tab Group");
    public static readonly ObservableValue<string> MenuNewHorizontalTabGroup = new("New Horizontal Tab Group");
    public static readonly ObservableValue<string> MenuMoveToNextTabGroup = new("Move to Next Tab Group");
    public static readonly ObservableValue<string> MenuMoveToPreviousTabGroup = new("Move to Previous Tab Group");
    public static readonly ObservableValue<string> MenuMaximize = new("Maximize");
    public static readonly ObservableValue<string> MenuRestore = new("Restore");
    
    public static readonly ObservableValue<string> ToolTipOptions = new("Options");
    public static readonly ObservableValue<string> ToolTipAutoHide = new("Auto Hide");
    public static readonly ObservableValue<string> ToolTipDock = new("Dock");
    public static readonly ObservableValue<string> ToolTipClose = new("Close");
    public static readonly ObservableValue<string> ToolTipMaximize = new("Maximize");
    public static readonly ObservableValue<string> ToolTipRestore = new("Restore");
    public static readonly ObservableValue<string> ToolTipHiddenTabs = new("Hidden tabs");
}
