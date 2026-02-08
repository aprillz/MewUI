namespace Aprillz.MewUI;

/// <summary>
/// Provides data for a <see cref="FrameworkElement.SizeChanged"/> event.
/// All values are in DIP units.
/// </summary>
public sealed class SizeChangedEventArgs
{
    public SizeChangedEventArgs(Size oldSize, Size newSize)
    {
        OldSize = oldSize;
        NewSize = newSize;
    }

    public Size OldSize { get; }

    public Size NewSize { get; }
}

