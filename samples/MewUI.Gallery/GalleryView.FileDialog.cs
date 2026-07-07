using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement FileDialogPage()
    {
        // Structured filters shared by the open/save actions.
        var filters = new[]
        {
            new FileFilter("Text/Markdown", "*.txt", "*.md"),
            new FileFilter("Images", "*.png", "*.jpg", "*.jpeg", "*.gif"),
            new FileFilter("All files", "*.*"),
        };

        // One card per backend: every action inside forces that backend via options.Backend.
        FrameworkElement BackendCard(string title, FileDialogBackend backend)
        {
            var status = new ObservableValue<string>("Result: -");

            void OpenFile()
            {
                var path = FileDialog.OpenFile(new OpenFileDialogOptions
                {
                    Owner = window,
                    Title = "Open File",
                    Filters = filters,
                    Backend = backend,
                });
                status.Value = path is null ? "Result: canceled" : $"Result: {path}";
            }

            void OpenFiles()
            {
                var files = FileDialog.OpenFiles(new OpenFileDialogOptions
                {
                    Owner = window,
                    Title = "Open Files",
                    Filters = filters,
                    Backend = backend,
                });
                status.Value = files is null || files.Length == 0
                    ? "Result: canceled"
                    : $"Result: {files.Length} selected\n{string.Join("\n", files)}";
            }

            void SaveFile()
            {
                var path = FileDialog.SaveFile(new SaveFileDialogOptions
                {
                    Owner = window,
                    Title = "Save File",
                    Filters = filters,
                    FileName = "untitled.txt",
                    DefaultExtension = "txt",
                    Backend = backend,
                });
                status.Value = path is null ? "Result: canceled" : $"Result: {path}";
            }

            void SelectFolder()
            {
                var folder = FileDialog.SelectFolder(new FolderDialogOptions
                {
                    Owner = window,
                    Title = "Select Folder",
                    Backend = backend,
                });
                status.Value = folder is null ? "Result: canceled" : $"Result: {folder}";
            }

            return Card(
                title,
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Button().Content("Open File...").OnClick(OpenFile),
                        new Button().Content("Open Files...").OnClick(OpenFiles),
                        new Button().Content("Save File...").OnClick(SaveFile),
                        new Button().Content("Select Folder...").OnClick(SelectFolder),
                        new TextBlock().BindText(status).FontSize(11).TextWrapping(TextWrapping.Wrap)));
        }

        return CardGrid(
            BackendCard("Managed", FileDialogBackend.Managed),
            BackendCard("Native", FileDialogBackend.Native));
    }
}
