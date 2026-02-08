namespace Aprillz.MewUI.Controls;

/// <summary>
/// Represents a node in a tree view.
/// </summary>
public sealed class TreeViewNode
{
    /// <summary>
    /// Initializes a new instance of the TreeViewNode class.
    /// </summary>
    /// <param name="text">The node text.</param>
    /// <param name="children">Optional child nodes.</param>
    /// <param name="tag">Optional user data.</param>
    public TreeViewNode(string text, IReadOnlyList<TreeViewNode>? children = null, object? tag = null)
    {
        Text = text ?? string.Empty;
        Children = children ?? Array.Empty<TreeViewNode>();
        Tag = tag;
    }

    /// <summary>
    /// Gets or sets the node text.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Gets or sets the child nodes collection.
    /// </summary>
    public IReadOnlyList<TreeViewNode> Children { get; set; }

    /// <summary>
    /// Gets or sets custom user data.
    /// </summary>
    public object? Tag { get; set; }

    /// <summary>
    /// Gets whether this node has child nodes.
    /// </summary>
    public bool HasChildren => Children.Count > 0;
}

