namespace Aprillz.MewUI;

/// <summary>
/// An element that supplies the operand for commands invoked from within it, such as the item an
/// items control realized a container for. The nearest source above the invocation anchor wins.
/// </summary>
public interface ICommandArgumentSource
{
    /// <summary>
    /// Gets the operand handed to typed handlers, or null when the element currently holds none.
    /// </summary>
    object? CommandArgument { get; }
}
