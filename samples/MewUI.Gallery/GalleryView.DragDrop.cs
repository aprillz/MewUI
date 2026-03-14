using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private readonly ObservableValue<string> _dropSummary = new(
        "Drop files on this window.\n\nCurrent support:\n- IDataObject API\n- Win32\n- macOS\n- Linux (X11/XDND)");

    private void InitializeDragDropSample()
    {
        window.Drop += OnWindowDrop;
    }

    private void OnWindowDrop(DragEventArgs e)
    {
        if (!e.Data.TryGetData<IReadOnlyList<string>>(StandardDataFormats.StorageItems, out var items) || items is null)
        {
            _dropSummary.Value =
                $"Drop received at {e.Position.X:0.#}, {e.Position.Y:0.#}\nFormats: {string.Join(", ", e.Data.Formats)}";
            return;
        }

        _dropSummary.Value =
            $"Drop received at {e.Position.X:0.#}, {e.Position.Y:0.#}\n" +
            $"Count: {items.Count}\n\n" +
            string.Join("\n", items);
        e.Handled = true;
    }

    private FrameworkElement DragDropCard() =>
        Card(
            "Drag and Drop",
            new DockPanel()
                .Height(220)
                .Spacing(8)
                .Children(
                    new Label()
                        .FontSize(11)
                        .DockTop()
                        .Text("Window-level drag and drop. Drop files anywhere on the gallery window."),
                    new MultiLineTextBox()
                        .BindText(_dropSummary)
                ));
}
