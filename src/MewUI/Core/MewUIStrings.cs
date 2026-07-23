namespace Aprillz.MewUI;

/// <summary>
/// Centralized UI strings for localization.
/// Default values are English. Assign <see cref="ObservableValue{T}.Value"/> at runtime to update all bound UI.
/// </summary>
public static class MewUIStrings
{
    // Common - shared button labels reused across surfaces (access-key mnemonic in value)
    public static ObservableValue<string> CommonOK { get; } = new("_OK");

    public static ObservableValue<string> CommonCancel { get; } = new("_Cancel");

    public static ObservableValue<string> CommonYes { get; } = new("_Yes");

    public static ObservableValue<string> CommonNo { get; } = new("_No");

    public static ObservableValue<string> CommonRetry { get; } = new("_Retry");

    public static ObservableValue<string> CommonIgnore { get; } = new("_Ignore");

    public static ObservableValue<string> CommonAbort { get; } = new("_Abort");

    // Prompt - MessageBox icon titles and detail toggle
    public static ObservableValue<string> PromptInformation { get; } = new("Information");

    public static ObservableValue<string> PromptWarning { get; } = new("Warning");

    public static ObservableValue<string> PromptError { get; } = new("Error");

    public static ObservableValue<string> PromptQuestion { get; } = new("Confirm");

    public static ObservableValue<string> PromptSuccess { get; } = new("Success");

    public static ObservableValue<string> PromptShield { get; } = new("Security");

    public static ObservableValue<string> PromptCrash { get; } = new("Crash");

    public static ObservableValue<string> PromptShowDetail { get; } = new("Show _Details");

    // BusyIndicator
    public static ObservableValue<string> BusyIndicatorAbortConfirmation { get; } = new("Are you sure you want to abort this operation?");

    public static ObservableValue<string> BusyIndicatorAborting { get; } = new("Aborting...");

    // TextBoxContextMenu
    public static ObservableValue<string> TextBoxContextMenuUndo { get; } = new("Undo");

    public static ObservableValue<string> TextBoxContextMenuRedo { get; } = new("Redo");

    public static ObservableValue<string> TextBoxContextMenuCut { get; } = new("Cut");

    public static ObservableValue<string> TextBoxContextMenuCopy { get; } = new("Copy");

    public static ObservableValue<string> TextBoxContextMenuPaste { get; } = new("Paste");

    public static ObservableValue<string> TextBoxContextMenuSelectAll { get; } = new("Select All");

    // FileDialog
    public static ObservableValue<string> FileDialogTitleOpenSingle { get; } = new("Open File");

    public static ObservableValue<string> FileDialogTitleOpenMultiple { get; } = new("Open Files");

    public static ObservableValue<string> FileDialogTitleSave { get; } = new("Save File");

    public static ObservableValue<string> FileDialogTitleSelectFolder { get; } = new("Select Folder");

    public static ObservableValue<string> FileDialogTitleFallback { get; } = new("File Dialog");

    public static ObservableValue<string> FileDialogAcceptOpen { get; } = new("_Open");

    public static ObservableValue<string> FileDialogAcceptSave { get; } = new("_Save");

    public static ObservableValue<string> FileDialogAcceptSelect { get; } = new("_Select");

    public static ObservableValue<string> FileDialogFileNameLabel { get; } = new("File name:");

    public static ObservableValue<string> FileDialogFileTypeLabel { get; } = new("File type:");

    public static ObservableValue<string> FileDialogNavBack { get; } = new("Back");

    public static ObservableValue<string> FileDialogNavForward { get; } = new("Forward");

    public static ObservableValue<string> FileDialogNavUp { get; } = new("Up");

    public static ObservableValue<string> FileDialogViewGrid { get; } = new("Grid");

    public static ObservableValue<string> FileDialogViewList { get; } = new("List");

    public static ObservableValue<string> FileDialogAllFiles { get; } = new("All files");

    public static ObservableValue<string> FileDialogColumnName { get; } = new("Name");

    public static ObservableValue<string> FileDialogColumnSize { get; } = new("Size");

    public static ObservableValue<string> FileDialogColumnModified { get; } = new("Modified");

    // Sidebar - file dialog places section headers
    public static ObservableValue<string> SidebarQuickAccess { get; } = new("Quick access");

    public static ObservableValue<string> SidebarThisPC { get; } = new("This PC");

    public static ObservableValue<string> SidebarFavorites { get; } = new("Favorites");

    public static ObservableValue<string> SidebarLocations { get; } = new("Locations");

    public static ObservableValue<string> SidebarPlaces { get; } = new("Places");

    public static ObservableValue<string> SidebarDevices { get; } = new("Devices");

    // Folder - known folder labels shared across platforms
    public static ObservableValue<string> FolderHome { get; } = new("Home");

    public static ObservableValue<string> FolderDesktop { get; } = new("Desktop");

    public static ObservableValue<string> FolderDownloads { get; } = new("Downloads");

    public static ObservableValue<string> FolderDocuments { get; } = new("Documents");

    public static ObservableValue<string> FolderPictures { get; } = new("Pictures");

    public static ObservableValue<string> FolderMusic { get; } = new("Music");

    public static ObservableValue<string> FolderVideos { get; } = new("Videos");

    public static ObservableValue<string> FolderApplications { get; } = new("Applications");

    // ColorPicker
    public static ObservableValue<string> ColorPickerHex { get; } = new("Hex");
}
