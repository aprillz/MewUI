using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Provides child traversal for the logical tree.
/// </summary>
public interface ILogicalTreeHost
{
    /// <summary>
    /// Visits the logical children of this element in order, stopping when the visitor returns false; returns false when it stopped early.
    /// </summary>
    bool VisitLogicalChildren(Func<Element, bool> visitor);
}
