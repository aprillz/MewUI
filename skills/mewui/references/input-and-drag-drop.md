# Handle focus, routed input, and drag-and-drop

Use commands and `InputMap` for reusable actions and shortcuts. Use direct input events only for control-local behavior such as accepting Enter in one editor or inspecting pointer position.

## Focus and keyboard input

```csharp
var status = new ObservableValue<string>("Ready");
var editor = new TextBox()
    .Placeholder("Press Enter")
    .TabIndex(0)
    .OnGotFocus(() => status.Value = "Editor focused")
    .OnLostFocus(() => status.Value = "Editor lost focus")
    .OnKeyDown(args =>
    {
        if (args.Key == Key.Enter)
        {
            status.Value = "Accepted";
            args.Handled = true;
        }
    });
```

Call `editor.Focus()` after the element belongs to a shown window, commonly from the window's loaded handler. `KeyDown` bubbles from the focused element; setting `Handled` stops later handling. Use `Window.PreviewKeyDown` only when the window must observe a key before the focused control.

Mouse events expose window-relative DIP positions and modifier state. Handle an event only when the current element owns the interaction; otherwise allow it to bubble.

## Element drag-and-drop

```csharp
using Aprillz.MewUI.Platform;

const string CARD_FORMAT = "application/x-example-card";
var status = new ObservableValue<string>("Drag the source into the target");

var source = new Border()
    .Padding(12)
    .CanDrag()
    .Child(new TextBlock().Text("Drag me"))
    .OnDragStarting(args =>
    {
        var data = new DataObject();
        data.SetData(CARD_FORMAT, "Card A");
        args.Data = data;
        args.AllowedEffects = DragDropEffects.Copy;
    });

var target = new Border()
    .Padding(24)
    .AllowDrop()
    .Child(new TextBlock().Text("Drop here"))
    .OnDragOver(args =>
    {
        if (args.Data.TryGetData<string>(CARD_FORMAT, out _))
        {
            args.Effect = DragDropEffects.Copy;
            args.Accepted = true;
        }
    })
    .OnDrop(args =>
    {
        if (args.Data.TryGetData<string>(CARD_FORMAT, out var card))
        {
            status.Value = $"Dropped: {card}";
            args.Effect = DragDropEffects.Copy;
            args.Accepted = true;
        }
    });
```

The source must set `Data`; leaving it null cancels the drag. The target must opt in with `AllowDrop`, choose an effect allowed by the source, and set `Accepted` only for data it understands.

For operating-system file drops, enable `AllowDrop` on the owning window and read standard formats from `args.Data`. Treat dropped paths as untrusted input and validate them before opening files. Use `DragCompleted` when the source must react to the final effect or cancellation.
