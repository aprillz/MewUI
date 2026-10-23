namespace Aprillz.MewUI.Automation;

/// <summary>Whether a control's hidden content is shown.</summary>
public enum ExpandCollapseState
{
    Collapsed,
    Expanded,
    PartiallyExpanded,
    LeafNode,
}

/// <summary>A control that shows and hides content, such as an expander or a drop-down.</summary>
public interface IExpandCollapseProvider
{
    ExpandCollapseState ExpandCollapseState { get; }

    void Expand();

    void Collapse();
}
