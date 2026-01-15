namespace Aprillz.MewUI;

public sealed class OpenFileDialogOptions
{
    public nint Owner { get; set; }
    public string Title { get; set; } = "Open";
    public string? InitialDirectory { get; set; }
    public string? Filter { get; set; }
    public bool Multiselect { get; set; }
}

public sealed class SaveFileDialogOptions
{
    public nint Owner { get; set; }
    public string Title { get; set; } = "Save";
    public string? InitialDirectory { get; set; }
    public string? Filter { get; set; }
    public string? FileName { get; set; }
    public string? DefaultExtension { get; set; }
    public bool OverwritePrompt { get; set; } = true;
}

public sealed class FolderDialogOptions
{
    public nint Owner { get; set; }
    public string Title { get; set; } = "Select folder";
    public string? InitialDirectory { get; set; }
}

public static class FileDialog
{
    public static string? OpenFile(OpenFileDialogOptions? options = null)
    {
        options ??= new OpenFileDialogOptions();
        options.Multiselect = false;

        var host = Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;
        var result = host.FileDialog.OpenFile(options);
        return result is { Length: > 0 } ? result[0] : null;
    }

    public static string[]? OpenFiles(OpenFileDialogOptions? options = null)
    {
        options ??= new OpenFileDialogOptions();
        options.Multiselect = true;

        var host = Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;
        return host.FileDialog.OpenFile(options);
    }

    public static string? SaveFile(SaveFileDialogOptions? options = null)
    {
        options ??= new SaveFileDialogOptions();

        var host = Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;
        return host.FileDialog.SaveFile(options);
    }

    public static string? SelectFolder(FolderDialogOptions? options = null)
    {
        options ??= new FolderDialogOptions();

        var host = Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;
        return host.FileDialog.SelectFolder(options);
    }
}
