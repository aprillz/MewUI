namespace Aprillz.MewUI.Platform;

/// <summary>
/// Platform implementation for file/folder dialogs.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Opens a file dialog and returns selected paths (or null when canceled).
    /// </summary>
    string[]? OpenFile(OpenFileDialogOptions options);

    /// <summary>
    /// Opens a save file dialog and returns the chosen path (or null when canceled).
    /// </summary>
    string? SaveFile(SaveFileDialogOptions options);

    /// <summary>
    /// Opens a folder selection dialog and returns the chosen path (or null when canceled).
    /// </summary>
    string? SelectFolder(FolderDialogOptions options);
}
