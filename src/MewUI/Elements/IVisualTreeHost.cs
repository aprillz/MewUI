using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Provides child traversal for the visual tree.
/// </summary>
public interface IVisualTreeHost
{
    /// <summary>
    /// Visits the children that participate in the current frame's visual tree, topmost first.
    /// </summary>
    /// <remarks>
    /// Contract: yield only children that render this frame (a collapsed or hidden part is not
    /// visited), in front-to-back order (a child rendered later, i.e. on top, is visited first).
    /// The base hit test probes children in yield order and takes the first hit, so a host whose
    /// yield order cannot be front-to-back (e.g. a panel storing children in render order) must
    /// keep its own hit-test override. Visitor callbacks run on hot paths; do not allocate.
    /// </remarks>
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
