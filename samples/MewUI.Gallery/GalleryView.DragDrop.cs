using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private const string DragSampleFormat = "application/x-mewui-gallery.drag-card";

    private readonly ObservableValue<string> _dropSummary = new(
        "Drop files on this window, or use the element-level drag below.");
    private readonly ObservableValue<string> _dragLog = new("(no drag yet)");
    private StackPanel? _slotA;
    private StackPanel? _slotB;

    private void InitializeDragDropSample()
    {
        // Opt the window into platform drop-target registration (WinForms/WPF-style AllowDrop).
        // Without this the OS does not register IDropTarget / WM_DROPFILES / Xdnd / NSDraggingDestination.
        window.AllowDrop = true;
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

    private FrameworkElement WindowDragDropCard() =>
        Card(
            "Window Drag and Drop",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .FontSize(11)
                        .Text("Window-level file drop:"),
                    new MultiLineTextBox()
                        .Height(80)
                        .BindText(_dropSummary)
                ),
            minWidth: 360);

    private FrameworkElement ElementDragDropCard() =>
        Card(
            "Element Drag and Drop",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .FontSize(11)
                        .Text("Internal element drag (drag a chip between the two slots):"),
                    BuildInternalDragSample(),
                    new TextBlock()
                        .FontSize(11)
                        .BindText(_dragLog)
                ),
            minWidth: 360);

    private FrameworkElement BuildInternalDragSample()
    {
        _slotA = new StackPanel().Vertical().Spacing(4);
        _slotB = new StackPanel().Vertical().Spacing(4);

        foreach (var label in new[] { "Alpha", "Bravo", "Charlie" })
        {
            _slotA.Add(BuildDragChip(label));
        }

        return new StackPanel()
            .Horizontal()
            .Spacing(8)
            .Children(
                BuildDragSlot("Slot A", _slotA),
                BuildDragSlot("Slot B", _slotB));
    }

    private FrameworkElement BuildDragSlot(string title, StackPanel content)
    {
        // The insertion line is a child of the Canvas; it lives above the chip stack and is positioned
        // absolutely via Canvas.SetTop. Hidden by default; shown only while a drag is over this slot.
        var insertionLine = new Border()
            .Height(2)
            .CornerRadius(1)
            .WithTheme((t, b) => b.Background(t.Palette.Accent));
        insertionLine.IsVisible = false;
        Canvas.SetLeft(insertionLine, 0);
        Canvas.SetRight(insertionLine, 0);

        // Canvas-positioned chip stack pinned to fill the canvas horizontally.
        Canvas.SetLeft(content, 0);
        Canvas.SetTop(content, 0);
        Canvas.SetRight(content, 0);

        var dropCanvas = new Canvas().MinHeight(100);
        dropCanvas.Add(content);
        dropCanvas.Add(insertionLine);

        int pendingInsertIndex = 0;

        var slot = new Border()
            .MinWidth(140)
            .Padding(8)
            .CornerRadius(6)
            .WithTheme((t, c) => c.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .BorderThickness(1)
            .Child(new StackPanel()
                .Vertical()
                .Spacing(6)
                .Children(
                    new TextBlock().Text(title).Bold().FontSize(11),
                    dropCanvas));

        slot.AllowDrop = true;

        void UpdateInsertionLine(Point cursorInWindow)
        {
            pendingInsertIndex = ComputeInsertIndex(content, cursorInWindow.Y);
            var lineY = ResolveInsertionLineY(content, dropCanvas, pendingInsertIndex);
            Canvas.SetTop(insertionLine, lineY - 1);
            if (!insertionLine.IsVisible) insertionLine.IsVisible = true;
        }

        slot.DragEnter += e =>
        {
            e.Accepted = true;
            e.Effect = DragDropEffects.Move;
            UpdateInsertionLine(e.Position);
        };
        slot.DragOver += e =>
        {
            e.Accepted = true;
            e.Effect = DragDropEffects.Move;
            UpdateInsertionLine(e.Position);
        };
        slot.DragLeave += _ =>
        {
            insertionLine.IsVisible = false;
        };
        slot.Drop += e =>
        {
            insertionLine.IsVisible = false;
            if (e.Data.TryGetData<string>(DragSampleFormat, out var label) && label is not null)
            {
                var chip = BuildDragChip(label);
                var index = Math.Clamp(pendingInsertIndex, 0, content.Children.Count);
                content.Insert(index, chip);
                e.Effect = DragDropEffects.Move;
                e.Accepted = true;
                e.Handled = true;
                _dragLog.Value = $"Dropped '{label}' on {title} at index {index}";
            }
        };
        return slot;
    }

    // Walks the chip list (top-down) and returns the insert index based on cursor Y vs each chip's midpoint.
    private static int ComputeInsertIndex(StackPanel content, double cursorY)
    {
        var children = content.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is not UIElement child) continue;
            var b = child.Bounds;
            if (cursorY < b.Y + b.Height / 2) return i;
        }
        return children.Count;
    }

    // Resolves the Y where the insertion line should appear, in canvas-local coords.
    // For the boundary positions (before first / after last) the line is offset by half the StackPanel's
    // spacing so it sits in the virtual gap a newly-inserted chip would occupy, matching the middle case.
    private static double ResolveInsertionLineY(StackPanel content, Canvas canvas, int insertIndex)
    {
        var children = content.Children;
        var halfSpacing = content.Spacing / 2;
        double windowY;
        if (children.Count == 0)
        {
            windowY = content.Bounds.Y;
        }
        else if (insertIndex <= 0)
        {
            windowY = ((UIElement)children[0]).Bounds.Y - halfSpacing;
        }
        else if (insertIndex >= children.Count)
        {
            var last = (UIElement)children[^1];
            windowY = last.Bounds.Y + last.Bounds.Height + halfSpacing;
        }
        else
        {
            var above = (UIElement)children[insertIndex - 1];
            var below = (UIElement)children[insertIndex];
            windowY = (above.Bounds.Y + above.Bounds.Height + below.Bounds.Y) / 2;
        }
        return windowY - canvas.Bounds.Y;
    }

    private FrameworkElement BuildDragChip(string label)
    {
        var chip = new Border()
            .Padding(new Thickness(8, 4))
            .CornerRadius(4)
            .WithTheme((t, c) => c.Background(t.Palette.ButtonFace.Lerp(t.Palette.Accent, 0.5)))
            .Child(new TextBlock().Text(label).FontSize(11));

        chip.CanDrag = true;
        chip.DragStarting += e =>
        {
            var data = new DataObject();
            data.SetData(DragSampleFormat, label);
            data.SetText(label);
            e.Data = data;
            e.AllowedEffects = DragDropEffects.Move | DragDropEffects.Copy;
            // Hotspot left null → router uses StartPositionInElement so the preview lines up with the grabbed chip.
            e.Preview = new DragPreviewContent { Element = chip, Opacity = 0.7 };
            _dragLog.Value = $"Starting drag for '{label}'";
        };
        chip.DragCompleted += e =>
        {
            _dragLog.Value = $"Drag of '{label}' completed: {e.FinalEffect}" + (e.WasCanceled ? " (canceled)" : "");
            if (e.FinalEffect == DragDropEffects.Move && !e.WasCanceled)
            {
                // Remove the chip from its original parent panel. Works across windows too: when the chip is
                // dropped on a slot in another window, that slot reports Move and the router returns it here.
                if (chip.Parent is StackPanel parent)
                {
                    parent.Remove(chip);
                }
            }
        };
        return chip;
    }

    // --- Cross-window drag and drop -------------------------------------------------------------------
    // Demonstrates same-process cross-window routing: open a second window and drag chips between it and the
    // main window. The router (Application.AllWindows + global cursor) handles the window hand-off; the slots
    // and chips are the same components used in the single-window sample.

    private FrameworkElement CrossWindowDragDropCard()
    {
        var source = new StackPanel().Vertical().Spacing(4);
        foreach (var label in new[] { "Red", "Green", "Blue" })
        {
            source.Add(BuildDragChip(label));
        }

        return Card(
            "Cross-Window Drag and Drop",
            new StackPanel()
                .Width(320)
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .FontSize(11)
                        .TextWrapping(TextWrapping.Wrap)
                        .Text("Open a second window, then drag a chip from here into its slot (and back). "
                            + "Same-process routing — no OS clipboard, the .NET payload is passed by reference."),
                    new Button()
                        .Content("Open second window")
                        .OnClick(OpenCrossWindowTarget),
                    BuildDragSlot("Drag from here", source)),
            minWidth: 320);
    }

    private void OpenCrossWindowTarget()
    {
        var target = new StackPanel().Vertical().Spacing(4);
        target.Add(BuildDragChip("Yellow"));

        Window second = null!;
        new Window()
            .Ref(out second)
            .Resizable(340, 280)
            .StartCenterOwner()
            .OnBuild(x =>
            {
                x.Title = "Cross-Window D&D";
                x.Padding = new Thickness(16);
                x.Content = new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .FontSize(11)
                            .TextWrapping(TextWrapping.Wrap)
                            .Text("Drag chips here from the main window — or drag 'Yellow' back."),
                        BuildDragSlot("Drop here", target));
            });

        second.Show(window);
    }
}
