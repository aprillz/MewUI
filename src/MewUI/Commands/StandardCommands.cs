namespace Aprillz.MewUI;

/// <summary>
/// Universal editing commands shared by keyboard defaults, menus and toolbars; controls provide
/// handlers via their <see cref="Controls.Element.Commands"/> scope.
/// </summary>
public static class StandardCommands
{
    /// <summary>
    /// Gets the cut command (<c>edit.cut</c>).
    /// </summary>
    public static Command Cut { get; } = new Command("edit.cut").BindText(MewUIStrings.CommandCut);

    /// <summary>
    /// Gets the copy command (<c>edit.copy</c>).
    /// </summary>
    public static Command Copy { get; } = new Command("edit.copy").BindText(MewUIStrings.CommandCopy);

    /// <summary>
    /// Gets the paste command (<c>edit.paste</c>).
    /// </summary>
    public static Command Paste { get; } = new Command("edit.paste").BindText(MewUIStrings.CommandPaste);

    /// <summary>
    /// Gets the delete command (<c>edit.delete</c>).
    /// </summary>
    public static Command Delete { get; } = new Command("edit.delete").BindText(MewUIStrings.CommandDelete);

    /// <summary>
    /// Gets the undo command (<c>edit.undo</c>).
    /// </summary>
    public static Command Undo { get; } = new Command("edit.undo").BindText(MewUIStrings.CommandUndo);

    /// <summary>
    /// Gets the redo command (<c>edit.redo</c>).
    /// </summary>
    public static Command Redo { get; } = new Command("edit.redo").BindText(MewUIStrings.CommandRedo);

    /// <summary>
    /// Gets the select all command (<c>edit.selectAll</c>).
    /// </summary>
    public static Command SelectAll { get; } = new Command("edit.selectAll").BindText(MewUIStrings.CommandSelectAll);
}
