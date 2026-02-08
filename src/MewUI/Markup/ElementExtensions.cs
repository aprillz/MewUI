namespace Aprillz.MewUI.Controls;

/// <summary>
/// Fluent API extension methods for FrameworkElement.
/// </summary>
public static class ElementExtensions
{
    #region Size

    /// <summary>
    /// Sets the window width.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="width">Width value.</param>
    /// <returns>The window for chaining.</returns>
    public static Window Width(this Window window, double width)
    {
        window.WindowSize = WindowSize.Resizable(width, window.Height);
        return window;
    }

    /// <summary>
    /// Sets the window height.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="height">Height value.</param>
    /// <returns>The window for chaining.</returns>
    public static Window Height(this Window window, double height)
    {
        window.WindowSize = WindowSize.Resizable(window.Width, height);
        return window;
    }

    /// <summary>
    /// Sets the window size.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="width">Width value.</param>
    /// <param name="height">Height value.</param>
    /// <returns>The window for chaining.</returns>
    public static Window Size(this Window window, double width, double height)
    {
        window.WindowSize = WindowSize.Resizable(width, height);
        return window;
    }

    /// <summary>
    /// Sets the window size uniformly.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="size">Size value.</param>
    /// <returns>The window for chaining.</returns>
    public static Window Size(this Window window, double size)
    {
        window.WindowSize = WindowSize.Resizable(size, size);
        return window;
    }

    /// <summary>
    /// Sets the window as resizable.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="width">Width value.</param>
    /// <param name="height">Height value.</param>
    /// <returns>The window for chaining.</returns>
    public static Window Resizable(this Window window, double width, double height)
    {
        window.WindowSize = WindowSize.Resizable(width, height);
        return window;
    }

    /// <summary>
    /// Sets the window as fixed size.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="width">Width value.</param>
    /// <param name="height">Height value.</param>
    /// <returns>The window for chaining.</returns>
    public static Window Fixed(this Window window, double width, double height)
    {
        window.WindowSize = WindowSize.Fixed(width, height);
        return window;
    }

    /// <summary>
    /// Sets the window to fit content width.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="maxWidth">Maximum width.</param>
    /// <param name="fixedHeight">Fixed height.</param>
    /// <returns>The window for chaining.</returns>
    public static Window FitContentWidth(this Window window, double maxWidth, double fixedHeight)
    {
        window.WindowSize = WindowSize.FitContentWidth(maxWidth, fixedHeight);
        return window;
    }

    /// <summary>
    /// Sets the window to fit content height.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="fixedWidth">Fixed width.</param>
    /// <param name="maxHeight">Maximum height.</param>
    /// <returns>The window for chaining.</returns>
    public static Window FitContentHeight(this Window window, double fixedWidth, double maxHeight)
    {
        window.WindowSize = WindowSize.FitContentHeight(fixedWidth, maxHeight);
        return window;
    }

    /// <summary>
    /// Sets the window to fit content size.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="maxWidth">Maximum width.</param>
    /// <param name="maxHeight">Maximum height.</param>
    /// <returns>The window for chaining.</returns>
    public static Window FitContentSize(this Window window, double maxWidth, double maxHeight)
    {
        window.WindowSize = WindowSize.FitContentSize(maxWidth, maxHeight);
        return window;
    }

    /// <summary>
    /// Sets the width.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="width">Width value.</param>
    /// <returns>The element for chaining.</returns>
    public static T Width<T>(this T element, double width) where T : FrameworkElement
    {
        element.Width = width;
        return element;
    }

    /// <summary>
    /// Sets the height.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="height">Height value.</param>
    /// <returns>The element for chaining.</returns>
    public static T Height<T>(this T element, double height) where T : FrameworkElement
    {
        element.Height = height;
        return element;
    }

    /// <summary>
    /// Sets the size.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="width">Width value.</param>
    /// <param name="height">Height value.</param>
    /// <returns>The element for chaining.</returns>
    public static T Size<T>(this T element, double width, double height) where T : FrameworkElement
    {
        element.Width = width;
        element.Height = height;
        return element;
    }

    /// <summary>
    /// Sets the size uniformly.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="size">Size value.</param>
    /// <returns>The element for chaining.</returns>
    public static T Size<T>(this T element, double size) where T : FrameworkElement
    {
        element.Width = size;
        element.Height = size;
        return element;
    }

    /// <summary>
    /// Sets the minimum width.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="minWidth">Minimum width.</param>
    /// <returns>The element for chaining.</returns>
    public static T MinWidth<T>(this T element, double minWidth) where T : FrameworkElement
    {
        element.MinWidth = minWidth;
        return element;
    }

    /// <summary>
    /// Sets the minimum height.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="minHeight">Minimum height.</param>
    /// <returns>The element for chaining.</returns>
    public static T MinHeight<T>(this T element, double minHeight) where T : FrameworkElement
    {
        element.MinHeight = minHeight;
        return element;
    }

    /// <summary>
    /// Sets the maximum width.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="maxWidth">Maximum width.</param>
    /// <returns>The element for chaining.</returns>
    public static T MaxWidth<T>(this T element, double maxWidth) where T : FrameworkElement
    {
        element.MaxWidth = maxWidth;
        return element;
    }

    /// <summary>
    /// Sets the maximum height.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="maxHeight">Maximum height.</param>
    /// <returns>The element for chaining.</returns>
    public static T MaxHeight<T>(this T element, double maxHeight) where T : FrameworkElement
    {
        element.MaxHeight = maxHeight;
        return element;
    }

    #endregion

    #region DockPanel

    /// <summary>
    /// Sets the dock position.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="dock">Dock position.</param>
    /// <returns>The element for chaining.</returns>
    public static T DockTo<T>(this T element, Dock dock) where T : Element
    {
        DockPanel.SetDock(element, dock);
        return element;
    }

    /// <summary>
    /// Docks the element to the left.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T DockLeft<T>(this T element) where T : Element => element.DockTo(Dock.Left);

    /// <summary>
    /// Docks the element to the top.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T DockTop<T>(this T element) where T : Element => element.DockTo(Dock.Top);

    /// <summary>
    /// Docks the element to the right.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T DockRight<T>(this T element) where T : Element => element.DockTo(Dock.Right);

    /// <summary>
    /// Docks the element to the bottom.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T DockBottom<T>(this T element) where T : Element => element.DockTo(Dock.Bottom);

    #endregion

    #region Margin

    /// <summary>
    /// Sets the margin uniformly.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="uniform">Uniform margin value.</param>
    /// <returns>The element for chaining.</returns>
    public static T Margin<T>(this T element, double uniform) where T : FrameworkElement
    {
        element.Margin = new Thickness(uniform);
        return element;
    }

    /// <summary>
    /// Sets the margin with horizontal and vertical values.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="horizontal">Horizontal margin.</param>
    /// <param name="vertical">Vertical margin.</param>
    /// <returns>The element for chaining.</returns>
    public static T Margin<T>(this T element, double horizontal, double vertical) where T : FrameworkElement
    {
        element.Margin = new Thickness(horizontal, vertical, horizontal, vertical);
        return element;
    }

    /// <summary>
    /// Sets the margin with individual values.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="left">Left margin.</param>
    /// <param name="top">Top margin.</param>
    /// <param name="right">Right margin.</param>
    /// <param name="bottom">Bottom margin.</param>
    /// <returns>The element for chaining.</returns>
    public static T Margin<T>(this T element, double left, double top, double right, double bottom) where T : FrameworkElement
    {
        element.Margin = new Thickness(left, top, right, bottom);
        return element;
    }

    #endregion

    #region Padding

    /// <summary>
    /// Sets the padding.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="padding">Padding thickness.</param>
    /// <returns>The element for chaining.</returns>
    public static T Padding<T>(this T element, Thickness padding) where T : FrameworkElement
    {
        element.Padding = padding;
        return element;
    }

    /// <summary>
    /// Sets the padding uniformly.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="uniform">Uniform padding value.</param>
    /// <returns>The element for chaining.</returns>
    public static T Padding<T>(this T element, double uniform) where T : FrameworkElement
    {
        element.Padding = new Thickness(uniform);
        return element;
    }

    /// <summary>
    /// Sets the padding with horizontal and vertical values.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="horizontal">Horizontal padding.</param>
    /// <param name="vertical">Vertical padding.</param>
    /// <returns>The element for chaining.</returns>
    public static T Padding<T>(this T element, double horizontal, double vertical) where T : FrameworkElement
    {
        element.Padding = new Thickness(horizontal, vertical, horizontal, vertical);
        return element;
    }

    /// <summary>
    /// Sets the padding with individual values.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="left">Left padding.</param>
    /// <param name="top">Top padding.</param>
    /// <param name="right">Right padding.</param>
    /// <param name="bottom">Bottom padding.</param>
    /// <returns>The element for chaining.</returns>
    public static T Padding<T>(this T element, double left, double top, double right, double bottom) where T : FrameworkElement
    {
        element.Padding = new Thickness(left, top, right, bottom);
        return element;
    }

    #endregion

    #region Alignment

    /// <summary>
    /// Sets the horizontal alignment.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="alignment">Horizontal alignment.</param>
    /// <returns>The element for chaining.</returns>
    public static T HorizontalAlignment<T>(this T element, HorizontalAlignment alignment) where T : FrameworkElement
    {
        element.HorizontalAlignment = alignment;
        return element;
    }

    /// <summary>
    /// Sets the vertical alignment.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="alignment">Vertical alignment.</param>
    /// <returns>The element for chaining.</returns>
    public static T VerticalAlignment<T>(this T element, VerticalAlignment alignment) where T : FrameworkElement
    {
        element.VerticalAlignment = alignment;
        return element;
    }

    /// <summary>
    /// Centers the element horizontally and vertically.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T Center<T>(this T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = MewUI.HorizontalAlignment.Center;
        element.VerticalAlignment = MewUI.VerticalAlignment.Center;
        return element;
    }

    /// <summary>
    /// Centers the element horizontally.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T CenterHorizontal<T>(this T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = MewUI.HorizontalAlignment.Center;
        return element;
    }

    /// <summary>
    /// Centers the element vertically.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T CenterVertical<T>(this T element) where T : FrameworkElement
    {
        element.VerticalAlignment = MewUI.VerticalAlignment.Center;
        return element;
    }

    /// <summary>
    /// Aligns the element to the left.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T Left<T>(this T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = MewUI.HorizontalAlignment.Left;
        return element;
    }

    /// <summary>
    /// Aligns the element to the right.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T Right<T>(this T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = MewUI.HorizontalAlignment.Right;
        return element;
    }

    /// <summary>
    /// Aligns the element to the top.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T Top<T>(this T element) where T : FrameworkElement
    {
        element.VerticalAlignment = MewUI.VerticalAlignment.Top;
        return element;
    }

    /// <summary>
    /// Aligns the element to the bottom.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T Bottom<T>(this T element) where T : FrameworkElement
    {
        element.VerticalAlignment = MewUI.VerticalAlignment.Bottom;
        return element;
    }

    /// <summary>
    /// Stretches the element horizontally.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T StretchHorizontal<T>(this T element) where T : FrameworkElement
    {
        element.HorizontalAlignment = MewUI.HorizontalAlignment.Stretch;
        return element;
    }

    /// <summary>
    /// Stretches the element vertically.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T StretchVertical<T>(this T element) where T : FrameworkElement
    {
        element.VerticalAlignment = MewUI.VerticalAlignment.Stretch;
        return element;
    }

    #endregion

    #region Template
    
    public static T Register<T>(this T control, TemplateContext ctx, string name) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(name);
        ctx.Register(name, control);
        return control;
    }

    #endregion

    #region Grid Attached Properties

    /// <summary>
    /// Sets the grid row.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="row">Row index.</param>
    /// <returns>The element for chaining.</returns>
    public static T Row<T>(this T element, int row) where T : Element
    {
        Grid.SetRow(element, row);
        return element;
    }

    /// <summary>
    /// Sets the grid column.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="column">Column index.</param>
    /// <returns>The element for chaining.</returns>
    public static T Column<T>(this T element, int column) where T : Element
    {
        Grid.SetColumn(element, column);
        return element;
    }

    /// <summary>
    /// Sets the grid row span.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="rowSpan">Row span count.</param>
    /// <returns>The element for chaining.</returns>
    public static T RowSpan<T>(this T element, int rowSpan) where T : Element
    {
        Grid.SetRowSpan(element, rowSpan);
        return element;
    }

    /// <summary>
    /// Sets the grid column span.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="columnSpan">Column span count.</param>
    /// <returns>The element for chaining.</returns>
    public static T ColumnSpan<T>(this T element, int columnSpan) where T : Element
    {
        Grid.SetColumnSpan(element, columnSpan);
        return element;
    }

    /// <summary>
    /// Sets the grid position.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="row">Row index.</param>
    /// <param name="column">Column index.</param>
    /// <returns>The element for chaining.</returns>
    public static T GridPosition<T>(this T element, int row, int column) where T : Element
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        return element;
    }

    /// <summary>
    /// Sets the grid position with spans.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="row">Row index.</param>
    /// <param name="column">Column index.</param>
    /// <param name="rowSpan">Row span count.</param>
    /// <param name="columnSpan">Column span count.</param>
    /// <returns>The element for chaining.</returns>
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

    /// <summary>
    /// Sets the canvas left position.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="left">Left position.</param>
    /// <returns>The element for chaining.</returns>
    public static T CanvasLeft<T>(this T element, double left) where T : Element
    {
        Canvas.SetLeft(element, left);
        return element;
    }

    /// <summary>
    /// Sets the canvas top position.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="top">Top position.</param>
    /// <returns>The element for chaining.</returns>
    public static T CanvasTop<T>(this T element, double top) where T : Element
    {
        Canvas.SetTop(element, top);
        return element;
    }

    /// <summary>
    /// Sets the canvas right position.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="right">Right position.</param>
    /// <returns>The element for chaining.</returns>
    public static T CanvasRight<T>(this T element, double right) where T : Element
    {
        Canvas.SetRight(element, right);
        return element;
    }

    /// <summary>
    /// Sets the canvas bottom position.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="bottom">Bottom position.</param>
    /// <returns>The element for chaining.</returns>
    public static T CanvasBottom<T>(this T element, double bottom) where T : Element
    {
        Canvas.SetBottom(element, bottom);
        return element;
    }

    /// <summary>
    /// Sets the canvas position.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="left">Left position.</param>
    /// <param name="top">Top position.</param>
    /// <returns>The element for chaining.</returns>
    public static T CanvasPosition<T>(this T element, double left, double top) where T : Element
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        return element;
    }

    #endregion
}
