namespace Aprillz.MewUI.Platform.Linux.X11;

internal sealed class X11FileDialogService : IFileDialogService
{
    // XDG Desktop Portal is the only native Linux file-dialog path. Core falls back to managed.
    private readonly XdgPortalFileDialogService _portal = new();

    public string[]? OpenFile(OpenFileDialogOptions options)
        => _portal.OpenFile(options);

    public string? SaveFile(SaveFileDialogOptions options)
        => _portal.SaveFile(options);

    public string? SelectFolder(FolderDialogOptions options)
        => _portal.SelectFolder(options);

    public bool IsNativeDialogAvailable()
        => XdgPortalFileDialogService.IsAvailable();
}
