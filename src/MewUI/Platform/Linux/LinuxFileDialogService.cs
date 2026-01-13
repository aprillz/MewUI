using Aprillz.MewUI.Core;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Platform.Linux;

internal sealed class LinuxFileDialogService : IFileDialogService
{
    public string[]? OpenFile(OpenFileDialogOptions options)
        => LinuxExternalDialogs.OpenFile(options);

    public string? SaveFile(SaveFileDialogOptions options)
        => LinuxExternalDialogs.SaveFile(options);

    public string? SelectFolder(FolderDialogOptions options)
        => LinuxExternalDialogs.SelectFolder(options);
}
