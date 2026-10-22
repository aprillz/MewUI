using Aprillz.MewUI.MewvalonEdit.Document;

namespace Aprillz.MewUI.MewvalonEdit;

/// <summary>
/// What the editor, its text area and its view have in common, so ported code can take any of the
/// three and still reach the document, the options and the registered services.
/// </summary>
public interface ITextEditorComponent
{
    /// <summary>Gets the registered service, or null when neither this component nor the document has it.</summary>
    TService? GetService<TService>() where TService : class;

    /// <summary>Gets the document being edited.</summary>
    TextDocument Document { get; }

    /// <summary>Occurs when the document was replaced, not when its content changed.</summary>
    event EventHandler? DocumentChanged;

    /// <summary>Gets the editing and display options.</summary>
    TextEditorOptions Options { get; }

    /// <summary>Occurs when an option changed, carrying the option that did.</summary>
    event EventHandler<MewProperty>? OptionChanged;
}
