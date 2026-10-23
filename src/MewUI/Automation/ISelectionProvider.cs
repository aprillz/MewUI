namespace Aprillz.MewUI.Automation;

/// <summary>A container whose children can be selected, such as a list.</summary>
public interface ISelectionProvider
{
    bool CanSelectMultiple { get; }

    bool IsSelectionRequired { get; }

    /// <summary>The peers of the selected children that are realized; a virtualized list leaves out the rest.</summary>
    IReadOnlyList<AutomationPeer> GetSelection();
}

/// <summary>A child that can be selected inside a selection container, such as a list item.</summary>
public interface ISelectionItemProvider
{
    bool IsSelected { get; }

    /// <summary>The peer of the container this item is selected in.</summary>
    AutomationPeer? SelectionContainer { get; }

    /// <summary>Selects this item alone.</summary>
    void Select();

    /// <summary>Adds this item to a selection that allows several items.</summary>
    void AddToSelection();

    /// <summary>Removes this item from the selection.</summary>
    void RemoveFromSelection();
}
