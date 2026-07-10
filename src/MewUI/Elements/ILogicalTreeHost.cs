using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Provides child traversal for the logical tree.
/// </summary>
public interface ILogicalTreeHost
{
    /// <summary>
    /// Visits logical children of the current element.
    /// </summary>
    bool VisitLogicalChildren(Func<Element, bool> visitor);
}
