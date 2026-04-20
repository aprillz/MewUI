using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Provides child traversal for the visual tree.
/// </summary>
public interface IVisualTreeHost
{
    /// <summary>
    /// Visits visual children of the current element.
    /// </summary>
    bool VisitChildren(Func<Element, bool> visitor);
}

/// <summary>
/// Marker for controls whose visual subtree is private composition backed by external state
/// (e.g. <c>ItemsSource</c>). When such a host's <see cref="Element.InvalidateMeasure"/> is called,
/// the dirty flag propagates into its subtree so the inner <see cref="Controls.ScrollViewer"/>
/// and presenter re-measure instead of short-circuiting on an unchanged constraint.
/// </summary>
public interface ISubtreeInvalidationHost : IVisualTreeHost
{
}
