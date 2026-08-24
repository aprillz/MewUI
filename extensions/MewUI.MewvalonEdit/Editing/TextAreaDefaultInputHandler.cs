using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.MewvalonEdit.Editing;

/// <summary>
/// The handler a text area starts with, holding the predefined bindings in the groups the original
/// exposes. Caret movement, typing and mouse selection are behaviors of the editing surface here
/// rather than command tables, so the groups carry what the extension layers on top of them and are
/// where a caller adds or replaces bindings of that kind.
/// </summary>
public sealed class TextAreaDefaultInputHandler : TextAreaInputHandler
{
    public TextAreaDefaultInputHandler(TextArea textArea) : base(textArea)
    {
        ArgumentNullException.ThrowIfNull(textArea);
        AddNestedInputHandler(CaretNavigation = new TextAreaInputHandler(textArea));
        AddNestedInputHandler(Editing = new TextAreaInputHandler(textArea));
        AddNestedInputHandler(MouseSelection = new TextAreaInputHandler(textArea));

        // Claimed here rather than left to the editing surface, which has the same shortcuts: the
        // original-file marker counts undo steps, and a step taken behind the stack's back would
        // leave the count pointing at a state the document is no longer in.
        Editing.AddBinding(new TextAreaKeyBinding(
            new KeyGesture(Key.Z, ModifierKeys.Primary),
            () => textArea.Document.UndoStack.Undo(),
            () => textArea.Document.UndoStack.CanUndo));
        Editing.AddBinding(new TextAreaKeyBinding(
            new KeyGesture(Key.Z, ModifierKeys.Primary | ModifierKeys.Shift),
            () => textArea.Document.UndoStack.Redo(),
            () => textArea.Document.UndoStack.CanRedo));
        Editing.AddBinding(new TextAreaKeyBinding(
            new KeyGesture(Key.Y, ModifierKeys.Primary),
            () => textArea.Document.UndoStack.Redo(),
            () => textArea.Document.UndoStack.CanRedo));

        // Insert switches between inserting and overwriting, and only when the options allow it: the
        // key is a common accident, so the original leaves the switch off until a host asks for it.
        Editing.AddBinding(new TextAreaKeyBinding(
            new KeyGesture(Key.Insert),
            () => textArea.OverstrikeMode = !textArea.OverstrikeMode,
            () => textArea.Options.AllowToggleOverstrikeMode));

        // Rectangle editing: the surface holds an empty selection while a rectangle is active, so
        // these claim the keys ahead of it and drive the rectangle instead. With no rectangle the
        // CanExecute declines and the surface behaves as always.
        bool RectangleHasText() => textArea.Selection is RectangleSelection rectangle && rectangle.Length > 0;
        bool HasRectangle() => textArea.Selection is RectangleSelection;
        void DeleteRectangle() => ((RectangleSelection)textArea.Selection).ReplaceSelectionWithText(string.Empty);
        void DeleteRectangleColumn(CaretMovementType direction)
        {
            // A rectangle with no width is a column of carets, and a delete key has to take a
            // character from each of them: the rectangle grows by one step in the key's direction
            // first, exactly as walking it with Alt+Shift would, and then it is cleared. A line too
            // short to give up that character has nothing in the widened rectangle and stays as it is.
            if (textArea.Selection.Length == 0)
            {
                var before = textArea.Selection;
                int line = textArea.Caret.Position.Line;
                CaretNavigationCommandHandler.MoveCaretBoxSelection(textArea, direction);
                if (textArea.Caret.Position.Line != line)
                {
                    // The step left the line, which a rectangle cannot follow: it owns columns, and
                    // widening it across a line boundary would pull the next line up into this one.
                    textArea.Selection = before;
                    return;
                }
            }

            if (textArea.Selection is RectangleSelection widened && widened.Length > 0)
            {
                widened.ReplaceSelectionWithText(string.Empty);
            }
        }
        Editing.AddBinding(new TextAreaKeyBinding(
            new KeyGesture(Key.Backspace), () => DeleteRectangleColumn(CaretMovementType.Backspace), HasRectangle));
        Editing.AddBinding(new TextAreaKeyBinding(
            new KeyGesture(Key.Delete), () => DeleteRectangleColumn(CaretMovementType.CharRight), HasRectangle));
        Editing.AddBinding(new TextAreaKeyBinding(
            new KeyGesture(Key.C, ModifierKeys.Primary),
            () => textArea.CopyRectangleSelection(),
            RectangleHasText));
        Editing.AddBinding(new TextAreaKeyBinding(
            new KeyGesture(Key.X, ModifierKeys.Primary),
            () =>
            {
                if (textArea.CopyRectangleSelection())
                {
                    DeleteRectangle();
                }
            },
            RectangleHasText));
        Editing.AddBinding(new TextAreaKeyBinding(
            new KeyGesture(Key.V, ModifierKeys.Primary),
            () =>
            {
                if (textArea.TryGetClipboardText(out string pasteText))
                {
                    ((RectangleSelection)textArea.Selection).ReplaceSelectionWithText(pasteText);
                }
            },
            () => textArea.Selection is RectangleSelection));

        // The box-selection keys, as the original binds them on the caret-navigation handler.
        void AddBoxBinding(Key key, ModifierKeys modifiers, CaretMovementType direction)
            => CaretNavigation.AddBinding(new TextAreaKeyBinding(
                new KeyGesture(key, modifiers),
                () => CaretNavigationCommandHandler.MoveCaretBoxSelection(textArea, direction)));
        const ModifierKeys ALT_SHIFT = ModifierKeys.Alt | ModifierKeys.Shift;
        AddBoxBinding(Key.Left, ALT_SHIFT, CaretMovementType.CharLeft);
        AddBoxBinding(Key.Right, ALT_SHIFT, CaretMovementType.CharRight);
        AddBoxBinding(Key.Left, ModifierKeys.Control | ALT_SHIFT, CaretMovementType.WordLeft);
        AddBoxBinding(Key.Right, ModifierKeys.Control | ALT_SHIFT, CaretMovementType.WordRight);
        AddBoxBinding(Key.Up, ALT_SHIFT, CaretMovementType.LineUp);
        AddBoxBinding(Key.Down, ALT_SHIFT, CaretMovementType.LineDown);
        AddBoxBinding(Key.Home, ALT_SHIFT, CaretMovementType.LineStart);
        AddBoxBinding(Key.End, ALT_SHIFT, CaretMovementType.LineEnd);
    }

    /// <summary>Bindings that move the caret. The ordinary movement keys live in the surface.</summary>
    public TextAreaInputHandler CaretNavigation { get; }

    /// <summary>Bindings that change the document, undo and redo among them.</summary>
    public TextAreaInputHandler Editing { get; }

    /// <summary>Bindings for selecting with the mouse. The ordinary drag lives in the surface.</summary>
    public TextAreaInputHandler MouseSelection { get; }
}
