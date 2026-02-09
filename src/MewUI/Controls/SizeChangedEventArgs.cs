namespace Aprillz.MewUI;

/// <summary>
/// Provides data for a <see cref="Aprillz.MewUI.Controls.FrameworkElement.SizeChanged"/> event.
/// All values are in DIP units.
/// </summary>
public sealed class SizeChangedEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SizeChangedEventArgs"/> class.
    /// </summary>
    /// <param name="oldSize">The previous size in DIPs.</param>
    /// <param name="newSize">The new size in DIPs.</param>
    public SizeChangedEventArgs(Size oldSize, Size newSize)
    {
        OldSize = oldSize;
        NewSize = newSize;
    }

    /// <summary>
    /// Gets the previous size in DIPs.
    /// </summary>
    public Size OldSize { get; }

    /// <summary>
    /// Gets the new size in DIPs.
    /// </summary>
    public Size NewSize { get; }
}
