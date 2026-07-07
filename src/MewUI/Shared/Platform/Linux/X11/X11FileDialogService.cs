namespace Aprillz.MewUI.Platform.Linux.X11;

internal sealed class X11FileDialogService : IFileDialogService
{
    // Prefer the XDG Desktop Portal (native, sandbox-friendly); fall back to external tools (zenity/kdialog).
    private readonly XdgPortalFileDialogService _portal = new();

    public string[]? OpenFile(OpenFileDialogOptions options)
        => XdgPortalFileDialogService.IsAvailable() ? _portal.OpenFile(options) : LinuxExternalDialogs.OpenFile(options);

    public string? SaveFile(SaveFileDialogOptions options)
        => XdgPortalFileDialogService.IsAvailable() ? _portal.SaveFile(options) : LinuxExternalDialogs.SaveFile(options);

    public string? SelectFolder(FolderDialogOptions options)
        => XdgPortalFileDialogService.IsAvailable() ? _portal.SelectFolder(options) : LinuxExternalDialogs.SelectFolder(options);

    public bool IsNativeDialogAvailable()
        => XdgPortalFileDialogService.IsAvailable() || LinuxExternalDialogs.IsToolAvailable();
}
