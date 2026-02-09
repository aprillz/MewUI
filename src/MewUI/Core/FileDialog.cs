namespace Aprillz.MewUI;

/// <summary>
/// Options for opening a single file or multiple files.
/// </summary>
public sealed class OpenFileDialogOptions
{
    /// <summary>Native owner handle (platform-specific).</summary>
    public nint Owner { get; set; }
    /// <summary>Dialog title.</summary>
    public string Title { get; set; } = "Open";
    /// <summary>Initial directory (optional).</summary>
    public string? InitialDirectory { get; set; }
    /// <summary>File filter string (platform-specific).</summary>
    public string? Filter { get; set; }
    /// <summary>Whether multi-selection is enabled.</summary>
    public bool Multiselect { get; set; }
}

/// <summary>
/// Options for saving a file.
/// </summary>
public sealed class SaveFileDialogOptions
{
    /// <summary>Native owner handle (platform-specific).</summary>
    public nint Owner { get; set; }
    /// <summary>Dialog title.</summary>
    public string Title { get; set; } = "Save";
    /// <summary>Initial directory (optional).</summary>
    public string? InitialDirectory { get; set; }
    /// <summary>File filter string (platform-specific).</summary>
    public string? Filter { get; set; }
    /// <summary>Initial file name (optional).</summary>
    public string? FileName { get; set; }
    /// <summary>Default extension (optional).</summary>
    public string? DefaultExtension { get; set; }
    /// <summary>Whether to prompt before overwriting an existing file.</summary>
    public bool OverwritePrompt { get; set; } = true;
}

/// <summary>
/// Options for selecting a folder.
/// </summary>
public sealed class FolderDialogOptions
{
    /// <summary>Native owner handle (platform-specific).</summary>
    public nint Owner { get; set; }
    /// <summary>Dialog title.</summary>
    public string Title { get; set; } = "Select folder";
    /// <summary>Initial directory (optional).</summary>
    public string? InitialDirectory { get; set; }
}

/// <summary>
/// Provides platform-routed file dialogs (open/save/select folder).
/// </summary>
public static class FileDialog
{
    /// <summary>
    /// Opens a dialog for selecting a single file.
    /// </summary>
    public static string? OpenFile(OpenFileDialogOptions? options = null)
    {
        options ??= new OpenFileDialogOptions();
        options.Multiselect = false;

        var host = Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;
        var result = host.FileDialog.OpenFile(options);
        return result is { Length: > 0 } ? result[0] : null;
    }

    /// <summary>
    /// Opens a dialog for selecting multiple files.
    /// </summary>
    public static string[]? OpenFiles(OpenFileDialogOptions? options = null)
    {
        options ??= new OpenFileDialogOptions();
        options.Multiselect = true;

        var host = Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;
        return host.FileDialog.OpenFile(options);
    }

    /// <summary>
    /// Opens a dialog for choosing a save path.
    /// </summary>
    public static string? SaveFile(SaveFileDialogOptions? options = null)
    {
        options ??= new SaveFileDialogOptions();

        var host = Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;
        return host.FileDialog.SaveFile(options);
    }

    /// <summary>
    /// Opens a dialog for selecting a folder.
    /// </summary>
    public static string? SelectFolder(FolderDialogOptions? options = null)
    {
        options ??= new FolderDialogOptions();

        var host = Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;
        return host.FileDialog.SelectFolder(options);
    }
}
