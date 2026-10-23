namespace Aprillz.MewUI.Automation;

/// <summary>Which views of the automation tree an element appears in.</summary>
public enum AccessibilityView
{
    /// <summary>The element appears as a control, the default for elements that have a peer.</summary>
    Control,

    /// <summary>The element carries content a reader presents, such as a text block inside a control.</summary>
    Content,

    /// <summary>The element is left out of the tree; its children are presented in its place.</summary>
    Raw,
}
