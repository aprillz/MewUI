namespace Aprillz.MewUI;

/// <summary>
/// Identifies a semantic operation (e.g. "file.save"). A command owns neither its handler nor its
/// shortcut; handlers live in <see cref="CommandScope"/> and gestures in <see cref="InputMap"/>.
/// </summary>
/// <remarks>
/// Runtime lookup uses reference identity: two <see cref="Command"/> instances with the same
/// <see cref="Id"/> are distinct commands. <see cref="Id"/> is a stable textual identity for
/// diagnostics, logging and persistence.
/// </remarks>
public sealed class Command
{
    /// <summary>
    /// Creates a semantic command with optional default presentation metadata.
    /// </summary>
    /// <param name="id">Stable textual identity used for diagnostics, logging and persistence.</param>
    /// <param name="text">
    /// Optional default presentation text. A single underscore marks the following character as
    /// the default access key, and a double underscore represents a literal underscore. The
    /// public <see cref="Text"/> value is normalized and never includes access-key markers.
    /// </param>
    /// <param name="icon">Optional reusable icon presentation.</param>
    public Command(string id, string? text = null, IconTemplate? icon = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Presentation = new CommandPresentation(text, icon);
    }

    /// <summary>
    /// Gets the stable textual identity of this command.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the normalized default presentation label, or null when presentation supplies its own
    /// text. Access-key markers accepted by the constructor are removed from this value, so
    /// presenters that do not support access keys can display it directly.
    /// </summary>
    public string? Text => Presentation.DisplayText;

    /// <summary>
    /// Gets the stable bindable default presentation for this command.
    /// </summary>
    public CommandPresentation Presentation { get; }

    /// <summary>
    /// Gets the reusable icon presentation, or null when presenters should show no command icon.
    /// </summary>
    public IconTemplate? Icon => Presentation.Icon;

    /// <summary>
    /// Gets the sentence saying what running this command does, or null when it has none.
    /// </summary>
    public string? Description => Presentation.Description;

    public override string ToString() => Id;
}
