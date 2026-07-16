namespace Aprillz.MewUI.Platform.Linux;

internal sealed class LinuxFileDialogService : IFileDialogService
{
    public string[]? OpenFile(OpenFileDialogOptions options)
        => throw Unavailable();

    public string? SaveFile(SaveFileDialogOptions options)
        => throw Unavailable();

    public string? SelectFolder(FolderDialogOptions options)
        => throw Unavailable();

    public bool IsNativeDialogAvailable()
        => false;

    private static NativeDialogUnavailableException Unavailable()
        => new("No native Linux file-dialog service is available.");
}
