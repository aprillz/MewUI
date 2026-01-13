using Aprillz.MewUI.Core;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Linux;

namespace Aprillz.MewUI.Platform.Linux.X11;

internal sealed class X11FileDialogService : IFileDialogService
{
    public string[]? OpenFile(OpenFileDialogOptions options)
        => LinuxExternalDialogs.OpenFile(options);

    public string? SaveFile(SaveFileDialogOptions options)
        => LinuxExternalDialogs.SaveFile(options);

    public string? SelectFolder(FolderDialogOptions options)
        => LinuxExternalDialogs.SelectFolder(options);
}
