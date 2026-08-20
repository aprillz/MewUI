using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Fluent extensions for semantic command metadata.
/// </summary>
public static class CommandExtensions
{
    /// <summary>
    /// Sets the sentence saying what running the command does. Material a presenter may use; a tooltip
    /// built from a command carries it when the presenter asks for it.
    /// </summary>
    public static Command Description(this Command command, string? description)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Presentation.Description = description;
        return command;
    }

    /// <summary>
    /// Binds the command's description to an observable source.
    /// </summary>
    public static Command BindDescription(this Command command, ObservableValue<string?> source)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(source);
        command.Presentation.SetBinding(
            CommandPresentation.DescriptionProperty,
            source,
            BindingMode.OneWay);
        return command;
    }

    /// <summary>
    /// Binds the command's default presentation text to an observable source.
    /// </summary>
    /// <remarks>
    /// This creates a one-way binding to <see cref="CommandPresentation.AccessTextProperty"/>; it
    /// does not assign a text snapshot. Source values may contain access-key markers such as
    /// <c>"_Save"</c>. <see cref="Command.Text"/> exposes the current parsed display text.
    /// </remarks>
    public static Command BindText(this Command command, ObservableValue<string> source)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(source);
        command.Presentation.SetBinding(
            CommandPresentation.AccessTextProperty,
            source,
            static value => (string?)value,
            mode: BindingMode.OneWay);
        return command;
    }

    /// <summary>
    /// Binds the command's default icon presentation to an observable source.
    /// </summary>
    /// <remarks>
    /// This creates a one-way binding to <see cref="CommandPresentation.IconProperty"/>. Each
    /// consumer materializes the current template independently at its own icon size.
    /// </remarks>
    public static Command BindIcon(this Command command, ObservableValue<IconTemplate?> source)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(source);
        command.Presentation.SetBinding(
            CommandPresentation.IconProperty,
            source,
            BindingMode.OneWay);
        return command;
    }
}
