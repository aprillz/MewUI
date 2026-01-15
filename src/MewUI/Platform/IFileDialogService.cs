namespace Aprillz.MewUI.Platform;

public interface IFileDialogService
{
    string[]? OpenFile(OpenFileDialogOptions options);

    string? SaveFile(SaveFileDialogOptions options);

    string? SelectFolder(FolderDialogOptions options);
}
