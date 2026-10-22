namespace Aprillz.MewUI;

/// <summary>
/// Centralized UI strings for localization, with English defaults.
/// </summary>
/// <remarks>
/// Assign <see cref="ObservableValue{T}.Value"/> at runtime to update all bound UI, and call
/// <see cref="ResetToDefaults"/> to restore every string to its built-in English default.
/// </remarks>
public static class MewUIStrings
{
    private static readonly List<Action> _resetters = [];

    private static ObservableValue<string> Define(string defaultValue)
    {
        var value = new ObservableValue<string>(defaultValue);
        _resetters.Add(() => value.Value = defaultValue);
        return value;
    }

    /// <summary>Restores every string to its built-in English default.</summary>
    public static void ResetToDefaults()
    {
        foreach (var reset in _resetters)
        {
            reset();
        }
    }

    // Common - shared button labels reused across surfaces (access-key mnemonic in value)
    /// <summary>Gets the OK button label.</summary>
    public static ObservableValue<string> CommonOK { get; } = Define("_OK");

    /// <summary>Gets the Cancel button label.</summary>
    public static ObservableValue<string> CommonCancel { get; } = Define("_Cancel");

    /// <summary>Gets the Yes button label.</summary>
    public static ObservableValue<string> CommonYes { get; } = Define("_Yes");

    /// <summary>Gets the No button label.</summary>
    public static ObservableValue<string> CommonNo { get; } = Define("_No");

    /// <summary>Gets the Retry button label.</summary>
    public static ObservableValue<string> CommonRetry { get; } = Define("_Retry");

    /// <summary>Gets the Ignore button label.</summary>
    public static ObservableValue<string> CommonIgnore { get; } = Define("_Ignore");

    /// <summary>Gets the Abort button label.</summary>
    public static ObservableValue<string> CommonAbort { get; } = Define("_Abort");

    // Prompt - MessageBox icon titles and detail toggle
    /// <summary>Gets the title of an information prompt.</summary>
    public static ObservableValue<string> PromptInformation { get; } = Define("Information");

    /// <summary>Gets the title of a warning prompt.</summary>
    public static ObservableValue<string> PromptWarning { get; } = Define("Warning");

    /// <summary>Gets the title of an error prompt.</summary>
    public static ObservableValue<string> PromptError { get; } = Define("Error");

    /// <summary>Gets the title of a question prompt.</summary>
    public static ObservableValue<string> PromptQuestion { get; } = Define("Confirm");

    /// <summary>Gets the title of a success prompt.</summary>
    public static ObservableValue<string> PromptSuccess { get; } = Define("Success");

    /// <summary>Gets the title of a security prompt.</summary>
    public static ObservableValue<string> PromptShield { get; } = Define("Security");

    /// <summary>Gets the title of a crash prompt.</summary>
    public static ObservableValue<string> PromptCrash { get; } = Define("Crash");

    /// <summary>Gets the label of the button that reveals a prompt's detail.</summary>
    public static ObservableValue<string> PromptShowDetail { get; } = Define("Show _Details");

    // BusyIndicator
    /// <summary>Gets the question asked before aborting a busy operation.</summary>
    public static ObservableValue<string> BusyIndicatorAbortConfirmation { get; } = Define("Are you sure you want to abort this operation?");

    /// <summary>Gets the status text shown while a busy operation aborts.</summary>
    public static ObservableValue<string> BusyIndicatorAborting { get; } = Define("Aborting...");

    // Standard command presentation
    /// <summary>Gets the presentation text of <see cref="StandardCommands.Undo"/>.</summary>
    public static ObservableValue<string> CommandUndo { get; } = Define("_Undo");

    /// <summary>Gets the presentation text of <see cref="StandardCommands.Redo"/>.</summary>
    public static ObservableValue<string> CommandRedo { get; } = Define("_Redo");

    /// <summary>Gets the presentation text of <see cref="StandardCommands.Cut"/>.</summary>
    public static ObservableValue<string> CommandCut { get; } = Define("Cu_t");

    /// <summary>Gets the presentation text of <see cref="StandardCommands.Copy"/>.</summary>
    public static ObservableValue<string> CommandCopy { get; } = Define("_Copy");

    /// <summary>Gets the presentation text of <see cref="StandardCommands.Paste"/>.</summary>
    public static ObservableValue<string> CommandPaste { get; } = Define("_Paste");

    /// <summary>Gets the presentation text of <see cref="StandardCommands.Delete"/>.</summary>
    public static ObservableValue<string> CommandDelete { get; } = Define("_Delete");

    /// <summary>Gets the presentation text of <see cref="StandardCommands.SelectAll"/>.</summary>
    public static ObservableValue<string> CommandSelectAll { get; } = Define("Select _All");

    // Compatibility names share the same live command-presentation sources.
    /// <summary>Gets <see cref="CommandUndo"/> under its compatibility name.</summary>
    public static ObservableValue<string> TextBoxContextMenuUndo => CommandUndo;

    /// <summary>Gets <see cref="CommandRedo"/> under its compatibility name.</summary>
    public static ObservableValue<string> TextBoxContextMenuRedo => CommandRedo;

    /// <summary>Gets <see cref="CommandCut"/> under its compatibility name.</summary>
    public static ObservableValue<string> TextBoxContextMenuCut => CommandCut;

    /// <summary>Gets <see cref="CommandCopy"/> under its compatibility name.</summary>
    public static ObservableValue<string> TextBoxContextMenuCopy => CommandCopy;

    /// <summary>Gets <see cref="CommandPaste"/> under its compatibility name.</summary>
    public static ObservableValue<string> TextBoxContextMenuPaste => CommandPaste;

    /// <summary>Gets <see cref="CommandSelectAll"/> under its compatibility name.</summary>
    public static ObservableValue<string> TextBoxContextMenuSelectAll => CommandSelectAll;

    // FileDialog
    /// <summary>Gets the title of the open-file dialog.</summary>
    public static ObservableValue<string> FileDialogTitleOpenSingle { get; } = Define("Open File");

    /// <summary>Gets the title of the open-files dialog.</summary>
    public static ObservableValue<string> FileDialogTitleOpenMultiple { get; } = Define("Open Files");

    /// <summary>Gets the title of the save-file dialog.</summary>
    public static ObservableValue<string> FileDialogTitleSave { get; } = Define("Save File");

    /// <summary>Gets the title of the select-folder dialog.</summary>
    public static ObservableValue<string> FileDialogTitleSelectFolder { get; } = Define("Select Folder");

    /// <summary>Gets the file dialog title used when no other applies.</summary>
    public static ObservableValue<string> FileDialogTitleFallback { get; } = Define("File Dialog");

    /// <summary>Gets the accept button label of the open dialog.</summary>
    public static ObservableValue<string> FileDialogAcceptOpen { get; } = Define("_Open");

    /// <summary>Gets the accept button label of the save dialog.</summary>
    public static ObservableValue<string> FileDialogAcceptSave { get; } = Define("_Save");

    /// <summary>Gets the accept button label of the select-folder dialog.</summary>
    public static ObservableValue<string> FileDialogAcceptSelect { get; } = Define("_Select");

    /// <summary>Gets the label of the file name field.</summary>
    public static ObservableValue<string> FileDialogFileNameLabel { get; } = Define("File name:");

    /// <summary>Gets the label of the file type field.</summary>
    public static ObservableValue<string> FileDialogFileTypeLabel { get; } = Define("File type:");

    /// <summary>Gets the label of the back navigation button.</summary>
    public static ObservableValue<string> FileDialogNavBack { get; } = Define("Back");

    /// <summary>Gets the label of the forward navigation button.</summary>
    public static ObservableValue<string> FileDialogNavForward { get; } = Define("Forward");

    /// <summary>Gets the label of the up navigation button.</summary>
    public static ObservableValue<string> FileDialogNavUp { get; } = Define("Up");

    /// <summary>Gets the label of the grid view option.</summary>
    public static ObservableValue<string> FileDialogViewGrid { get; } = Define("Grid");

    /// <summary>Gets the label of the list view option.</summary>
    public static ObservableValue<string> FileDialogViewList { get; } = Define("List");

    /// <summary>Gets the name of the filter that matches every file.</summary>
    public static ObservableValue<string> FileDialogAllFiles { get; } = Define("All files");

    /// <summary>Gets the header of the name column.</summary>
    public static ObservableValue<string> FileDialogColumnName { get; } = Define("Name");

    /// <summary>Gets the header of the size column.</summary>
    public static ObservableValue<string> FileDialogColumnSize { get; } = Define("Size");

    /// <summary>Gets the header of the modified date column.</summary>
    public static ObservableValue<string> FileDialogColumnModified { get; } = Define("Modified");

    // Sidebar - file dialog places section headers
    /// <summary>Gets the sidebar header of the quick access section.</summary>
    public static ObservableValue<string> SidebarQuickAccess { get; } = Define("Quick access");

    /// <summary>Gets the sidebar header of the this PC section.</summary>
    public static ObservableValue<string> SidebarThisPC { get; } = Define("This PC");

    /// <summary>Gets the sidebar header of the favorites section.</summary>
    public static ObservableValue<string> SidebarFavorites { get; } = Define("Favorites");

    /// <summary>Gets the sidebar header of the locations section.</summary>
    public static ObservableValue<string> SidebarLocations { get; } = Define("Locations");

    /// <summary>Gets the sidebar header of the places section.</summary>
    public static ObservableValue<string> SidebarPlaces { get; } = Define("Places");

    /// <summary>Gets the sidebar header of the devices section.</summary>
    public static ObservableValue<string> SidebarDevices { get; } = Define("Devices");

    // Folder - known folder labels shared across platforms
    /// <summary>Gets the label of the home folder.</summary>
    public static ObservableValue<string> FolderHome { get; } = Define("Home");

    /// <summary>Gets the label of the desktop folder.</summary>
    public static ObservableValue<string> FolderDesktop { get; } = Define("Desktop");

    /// <summary>Gets the label of the downloads folder.</summary>
    public static ObservableValue<string> FolderDownloads { get; } = Define("Downloads");

    /// <summary>Gets the label of the documents folder.</summary>
    public static ObservableValue<string> FolderDocuments { get; } = Define("Documents");

    /// <summary>Gets the label of the pictures folder.</summary>
    public static ObservableValue<string> FolderPictures { get; } = Define("Pictures");

    /// <summary>Gets the label of the music folder.</summary>
    public static ObservableValue<string> FolderMusic { get; } = Define("Music");

    /// <summary>Gets the label of the videos folder.</summary>
    public static ObservableValue<string> FolderVideos { get; } = Define("Videos");

    /// <summary>Gets the label of the applications folder.</summary>
    public static ObservableValue<string> FolderApplications { get; } = Define("Applications");

    // ColorPicker
    /// <summary>Gets the label of the color picker's hex field.</summary>
    public static ObservableValue<string> ColorPickerHex { get; } = Define("Hex");
}
