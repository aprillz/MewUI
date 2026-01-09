using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Elements;
using Aprillz.MewUI.Panels;
using Aprillz.MewUI.Primitives;

namespace Aprillz.MewUI.Markup;

/// <summary>
/// Fluent API extension methods for FrameworkElement.
/// </summary>
public static class ElementExtensions
{
    #region Size

    public static Window Width(this Window window, double width)
    {
        window.WindowSize = Controls.WindowSize.Resizable(width, window.Height);
        return window;
    }

    public static Window Height(this Window window, double height)
    {
        window.WindowSize = Controls.WindowSize.Resizable(window.Width, height);
        return window;
    }

    public static Window Size(this Window window, double width, double height)
    {
        window.WindowSize = Controls.WindowSize.Resizable(width, height);
        return window;
    }

    public static Window Size(this Window window, double size)
    {
        window.WindowSize = Controls.WindowSize.Resizable(size, size);
        return window;
    }

    public static Window Resizable(this Window window, double width, double height)
    {
        window.WindowSize = Controls.WindowSize.Resizable(width, height);
        return window;
    }

    public static Window Fixed(this Window window, double width, double height)
    {
        window.WindowSize = Controls.WindowSize.Fixed(width, height);
        return window;
    }

    public static Window FitContentWidth(this Window window, double maxWidth, double fixedHeight)
    {
        window.WindowSize = Controls.WindowSize.FitContentWidth(maxWidth, fixedHeight);
        return window;
    }

    public static Window FitContentHeight(this Window window, double fixedWidth, double maxHeight)
    {
        window.WindowSize = Controls.WindowSize.FitContentHeight(fixedWidth, maxHeight);
        return window;
    }

    public static Window FitContentSize(this Window window, double maxWidth, double maxHeight)
    {
        window.WindowSize = Controls.WindowSize.FitContentSize(maxWidth, maxHeight);
        return window;
    }

    public static T Width<T>(this T element, double width) where T : FrameworkElement
    {
        element.Width = width;
        return element;
    }

    public static T Height<T>(this T element, double height) where T : FrameworkElement
    {
        element.Height = height;
        return element;
    }

    public static T Size<T>(this T element, double width, double height) where T : FrameworkElement
    {
        element.Width = width;
        element.Height = height;
        return element;
    }

    public static T Size<T>(this T element, double size) where T : FrameworkElement
    {
        element.Width = size;
        element.Height = size;
        return element;
    }

    public static T MinWidth<T>(this T element, double minWidth) where T : FrameworkElement
    {
        element.MinWidth = minWidth;
        return element;
    }

    public static T MinHeight<T>(this T element, double minHeight) where T : FrameworkElement
    {
        element.MinHeight = minHeight;
        return element;
    }

    public static T MaxWidth<T>(this T element, double maxWidth) where T : FrameworkElement
    {
        element.MaxWidth = maxWidth;
        return element;
    }

    public static T MaxHeight<T>(this T element, double maxHeight) where T : FrameworkElement
    {
        element.MaxHeight = maxHeight;
        return element;
    }

    #endregion

    #region DockPanel

    public static T DockTo<T>(this T element, Dock dock) where T : Element
    {
        DockPanel.SetDock(element, dock);
        return element;
    }

    public static T DockLeft<T>(this T element) where T : Element => element.DockTo(Dock.Left);
    public static T DockTop<T>(this T element) where T : Element => element.DockTo(Dock.Top);
    public static T DockRight<T>(this T element) where T : Element => element.DockTo(Dock.Right);
    public static T DockBottom<T>(this T element) where T : Element => element.DockTo(Dock.Bottom);

    #endregion

    #region Margin

    public static T Margin<T>(this T element, double uniform) where T : FrameworkElement
    {
        element.Margin = new Thickness(uniform);
        return element;
    }

    public static T Margin<T>(this T element, double horizontal, double vertical) where T : FrameworkElement
    {
        element.Margin = new Thickness(horizontal, vertical, horizontal, vertical);
        return element;
    }

    public static T Margin<T>(this T element, double left, double top, double right, double bottom) where T : FrameworkElement
    {
        element.Margin = new Thickness(left, top, right, bottom);
        return element;
    }

    #endregion

    #region Padding

    public static T Padding<T>(this T element, double uniform) where T : FrameworkElement
    {
        element.Padding = new Thickness(uniform);
        return element;
    }

    public static T Padding<T>(this T element, double horizontal, double vertical) where T : FrameworkElement
    {
        element.Padding = new Thickness(horizontal, vertical, horizontal, vertical);
        return element;
    }

    public static T Padding<T>(this T element, double left, double top, double right, double bottom) where T : FrameworkElement
    {
        element.Padding = new Thickness(left, top, right, bottom);
        return element;
    }

    #endregion

    #region Alignment

    public static T HorizontalAlignment<T>(this T element, HorizontalAlignment alignment) where T : FrameworkElement
    {
        element.HorizontalAlignment = alignment;
        return element;
    }

    public static T VerticalAlignment<T>(this T element, VerticalAlignment alignment) where T : FrameworkElement
    {
        element.VerticalAlignment = alignment;
        return element;
    }

    public static T Center<T>(this T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = Elements.HorizontalAlignment.Center;
        element.VerticalAlignment = Elements.VerticalAlignment.Center;
        return element;
    }

    public static T CenterHorizontal<T>(this T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = Elements.HorizontalAlignment.Center;
        return element;
    }

    public static T CenterVertical<T>(this T element) where T : FrameworkElement
    {
        element.VerticalAlignment = Elements.VerticalAlignment.Center;
        return element;
    }

    public static T Left<T>(this T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = Elements.HorizontalAlignment.Left;
        return element;
    }

    public static T Right<T>(this T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = Elements.HorizontalAlignment.Right;
        return element;
    }

    public static T Top<T>(this T element) where T : FrameworkElement
    {
        element.VerticalAlignment = Elements.VerticalAlignment.Top;
        return element;
    }

    public static T Bottom<T>(this T element) where T : FrameworkElement
    {
        element.VerticalAlignment = Elements.VerticalAlignment.Bottom;
        return element;
    }

    public static T Stretch<T>(this T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = Elements.HorizontalAlignment.Stretch;
        element.VerticalAlignment = Elements.VerticalAlignment.Stretch;
        return element;
    }

    #endregion

    #region Grid Attached Properties

    public static T Row<T>(this T element, int row) where T : Element
    {
        Grid.SetRow(element, row);
        return element;
    }

    public static T Column<T>(this T element, int column) where T : Element
    {
        Grid.SetColumn(element, column);
        return element;
    }

    public static T RowSpan<T>(this T element, int rowSpan) where T : Element
    {
        Grid.SetRowSpan(element, rowSpan);
        return element;
    }

    public static T ColumnSpan<T>(this T element, int columnSpan) where T : Element
    {
        Grid.SetColumnSpan(element, columnSpan);
        return element;
    }

    public static T GridPosition<T>(this T element, int row, int column) where T : Element
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        return element;
    }

    public static T GridPosition<T>(this T element, int row, int column, int rowSpan, int columnSpan) where T : Element
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetRowSpan(element, rowSpan);
        Grid.SetColumnSpan(element, columnSpan);
        return element;
    }

    #endregion

    #region Canvas Attached Properties

    public static T CanvasLeft<T>(this T element, double left) where T : Element
    {
        Canvas.SetLeft(element, left);
        return element;
    }

    public static T CanvasTop<T>(this T element, double top) where T : Element
    {
        Canvas.SetTop(element, top);
        return element;
    }

    public static T CanvasRight<T>(this T element, double right) where T : Element
    {
        Canvas.SetRight(element, right);
        return element;
    }

    public static T CanvasBottom<T>(this T element, double bottom) where T : Element
    {
        Canvas.SetBottom(element, bottom);
        return element;
    }

    public static T CanvasPosition<T>(this T element, double left, double top) where T : Element
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        return element;
    }

    #endregion
}
