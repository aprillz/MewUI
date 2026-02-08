using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A panel that positions children using absolute coordinates.
/// </summary>
public class Canvas : Panel
{
    private static readonly ConditionalWeakTable<Element, CanvasAttachedProperties> _attachedProperties = new();

    private sealed class CanvasAttachedProperties
    {
        public double Left = double.NaN;
        public double Top = double.NaN;
        public double Right = double.NaN;
        public double Bottom = double.NaN;
    }

    #region Attached Properties

    /// <summary>
    /// Sets the left position of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="value">Left position.</param>
    public static void SetLeft(Element element, double value) =>
        SetAttachedValue(element, ref GetOrCreate(element).Left, value);
    /// <summary>
    /// Gets the left position of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <returns>The left position.</returns>
    public static double GetLeft(Element element) => TryGet(element, out var props) ? props.Left : double.NaN;

    /// <summary>
    /// Sets the top position of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="value">Top position.</param>
    public static void SetTop(Element element, double value) =>
        SetAttachedValue(element, ref GetOrCreate(element).Top, value);
    /// <summary>
    /// Gets the top position of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <returns>The top position.</returns>
    public static double GetTop(Element element) => TryGet(element, out var props) ? props.Top : double.NaN;

    /// <summary>
    /// Sets the right position of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="value">Right position.</param>
    public static void SetRight(Element element, double value) =>
        SetAttachedValue(element, ref GetOrCreate(element).Right, value);
    /// <summary>
    /// Gets the right position of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <returns>The right position.</returns>
    public static double GetRight(Element element) => TryGet(element, out var props) ? props.Right : double.NaN;

    /// <summary>
    /// Sets the bottom position of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="value">Bottom position.</param>
    public static void SetBottom(Element element, double value) =>
        SetAttachedValue(element, ref GetOrCreate(element).Bottom, value);
    /// <summary>
    /// Gets the bottom position of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <returns>The bottom position.</returns>
    public static double GetBottom(Element element) => TryGet(element, out var props) ? props.Bottom : double.NaN;

    private static CanvasAttachedProperties GetOrCreate(Element element) =>
        _attachedProperties.GetOrCreateValue(element);

    private static bool TryGet(Element element, [NotNullWhen(true)] out CanvasAttachedProperties? properties)
        => _attachedProperties.TryGetValue(element, out properties!);

    private static void SetAttachedValue(Element element, ref double field, double value)
    {
        if (double.IsNaN(field) && double.IsNaN(value))
        {
            return;
        }

        if (field.Equals(value))
        {
            return;
        }

        field = value;
        element.InvalidateArrange();
    }

    #endregion

    protected override void OnChildRemoved(Element child)
    {
        // Clean up attached properties
        _attachedProperties.Remove(child);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        // Canvas measures children with infinite space
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        // Canvas doesn't have a natural size - it takes available space
        return Size.Empty;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        foreach (var child in Children)
        {
            double x = bounds.X;
            double y = bounds.Y;
            double width = child.DesiredSize.Width;
            double height = child.DesiredSize.Height;

            double left = GetLeft(child);
            double top = GetTop(child);
            double right = GetRight(child);
            double bottom = GetBottom(child);

            // Position from left or right
            if (!double.IsNaN(left))
            {
                x = bounds.X + left;
                if (!double.IsNaN(right))
                {
                    width = bounds.Width - left - right;
                }
            }
            else if (!double.IsNaN(right))
            {
                x = bounds.Right - right - width;
            }

            // Position from top or bottom
            if (!double.IsNaN(top))
            {
                y = bounds.Y + top;
                if (!double.IsNaN(bottom))
                {
                    height = bounds.Height - top - bottom;
                }
            }
            else if (!double.IsNaN(bottom))
            {
                y = bounds.Bottom - bottom - height;
            }

            child.Arrange(new Rect(x, y, width, height));
        }
    }
}
