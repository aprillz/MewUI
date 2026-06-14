namespace Aprillz.MewUI.Controls;

/// <summary>
/// Fluent API extension methods for FrameworkElement.
/// </summary>
public static class ElementExtensions
{
    #region Common Properties

    /// <summary>
    /// Sets whether the element participates in hit testing.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="value">Whether the element participates in hit testing.</param>
    /// <returns>The element for chaining.</returns>
    public static T IsHitTestVisible<T>(this T element, bool value = true) where T : UIElement
    {
        element.IsHitTestVisible = value;
        return element;
    }

    /// <summary>
    /// Sets whether viewport culling is skipped for the element.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="value">Whether viewport culling is skipped.</param>
    /// <returns>The element for chaining.</returns>
    public static T SkipViewportCull<T>(this T element, bool value = true) where T : UIElement
    {
        element.SkipViewportCull = value;
        return element;
    }

    /// <summary>
    /// Sets whether the element accepts drag-and-drop operations.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="value">Whether drop operations are accepted.</param>
    /// <returns>The element for chaining.</returns>
    public static T AllowDrop<T>(this T element, bool value = true) where T : UIElement
    {
        element.AllowDrop = value;
        return element;
    }

    /// <summary>
    /// Sets whether a drag operation can start from the element.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="value">Whether drag operations can start.</param>
    /// <returns>The element for chaining.</returns>
    public static T CanDrag<T>(this T element, bool value = true) where T : UIElement
    {
        element.CanDrag = value;
        return element;
    }

    /// <summary>
    /// Sets the application-defined tag.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="value">Tag value.</param>
    /// <returns>The element for chaining.</returns>
    public static T Tag<T>(this T element, object? value) where T : FrameworkElement
    {
        element.Tag = value;
        return element;
    }

    /// <summary>
    /// Sets the style sheet applied to the element.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="value">Style sheet.</param>
    /// <returns>The element for chaining.</returns>
    public static T StyleSheet<T>(this T element, StyleSheet? value) where T : FrameworkElement
    {
        element.StyleSheet = value;
        return element;
    }

    /// <summary>
    /// Adds a size change handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnLayoutSizeChanged<T>(this T element, Action<SizeChangedEventArgs> handler)
        where T : FrameworkElement
    {
        element.SizeChanged += handler;
        return element;
    }

    /// <summary>
    /// Adds a drag-enter handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnDragEnter<T>(this T element, Action<DragEventArgs> handler) where T : UIElement
    {
        element.DragEnter += handler;
        return element;
    }

    /// <summary>
    /// Adds a drag-over handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnDragOver<T>(this T element, Action<DragEventArgs> handler) where T : UIElement
    {
        element.DragOver += handler;
        return element;
    }

    /// <summary>
    /// Adds a drag-leave handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnDragLeave<T>(this T element, Action<DragEventArgs> handler) where T : UIElement
    {
        element.DragLeave += handler;
        return element;
    }

    /// <summary>
    /// Adds a drop handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnDrop<T>(this T element, Action<DragEventArgs> handler) where T : UIElement
    {
        element.Drop += handler;
        return element;
    }

    /// <summary>
    /// Adds a drag-starting handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnDragStarting<T>(this T element, Action<DragStartingEventArgs> handler) where T : UIElement
    {
        element.DragStarting += handler;
        return element;
    }

    /// <summary>
    /// Adds a drag-completed handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnDragCompleted<T>(this T element, Action<DragCompletedEventArgs> handler) where T : UIElement
    {
        element.DragCompleted += handler;
        return element;
    }

    #endregion

    #region Size

    /// <summary>
    /// Not supported on <see cref="Window"/>. Use <see cref="Resizable"/>, <see cref="Fixed"/>, or the FitContent methods.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="width">Unsupported width value.</param>
    /// <returns>This method always throws.</returns>
    [Obsolete("Use .Resizable(w, h), .Fixed(w, h), or FitContent methods to set Window dimensions.", error: true)]
    public static Window Width(this Window window, double width)
        => throw new NotSupportedException("Use .Resizable(w, h), .Fixed(w, h), or FitContent methods to set Window dimensions.");

    /// <summary>
    /// Not supported on <see cref="Window"/>. Use <see cref="Resizable"/>, <see cref="Fixed"/>, or the FitContent methods.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="height">Unsupported height value.</param>
    /// <returns>This method always throws.</returns>
    [Obsolete("Use .Resizable(w, h), .Fixed(w, h), or FitContent methods to set Window dimensions.", error: true)]
    public static Window Height(this Window window, double height)
        => throw new NotSupportedException("Use .Resizable(w, h), .Fixed(w, h), or FitContent methods to set Window dimensions.");

    /// <summary>
    /// Not supported on <see cref="Window"/>. Use <see cref="Resizable"/>, <see cref="Fixed"/>, or the FitContent methods.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="width">Unsupported width value.</param>
    /// <param name="height">Unsupported height value.</param>
    /// <returns>This method always throws.</returns>
    [Obsolete("Use .Resizable(w, h), .Fixed(w, h), or FitContent methods to set Window dimensions.", error: true)]
    public static Window Size(this Window window, double width, double height)
        => throw new NotSupportedException("Use .Resizable(w, h), .Fixed(w, h), or FitContent methods to set Window dimensions.");

    /// <summary>
    /// Not supported on <see cref="Window"/>. Use <see cref="Resizable"/>, <see cref="Fixed"/>, or the FitContent methods.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="size">Unsupported size value.</param>
    /// <returns>This method always throws.</returns>
    [Obsolete("Use .Resizable(w, h), .Fixed(w, h), or FitContent methods to set Window dimensions.", error: true)]
    public static Window Size(this Window window, double size)
        => throw new NotSupportedException("Use .Resizable(w, h), .Fixed(w, h), or FitContent methods to set Window dimensions.");

    /// <summary>
    /// Sets the window as resizable with optional min/max constraints.
    /// </summary>
    /// <typeparam name="TWindow">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="width">Width value.</param>
    /// <param name="height">Height value.</param>
    /// <param name="minWidth">Minimum width constraint.</param>
    /// <param name="minHeight">Minimum height constraint.</param>
    /// <param name="maxWidth">Maximum width constraint.</param>
    /// <param name="maxHeight">Maximum height constraint.</param>
    /// <returns>The window for chaining.</returns>
    public static TWindow Resizable<TWindow>(this TWindow window, double width, double height,
        double minWidth = 0, double minHeight = 0,
        double maxWidth = double.PositiveInfinity, double maxHeight = double.PositiveInfinity) where TWindow : Window
    {
        window.WindowSize = WindowSize.Resizable(width, height, minWidth, minHeight, maxWidth, maxHeight);
        return window;
    }

    /// <summary>
    /// Sets the window as fixed size.
    /// </summary>
    /// <typeparam name="TWindow">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="width">Width value.</param>
    /// <param name="height">Height value.</param>
    /// <returns>The window for chaining.</returns>
    public static TWindow Fixed<TWindow>(this TWindow window, double width, double height) where TWindow : Window
    {
        window.WindowSize = WindowSize.Fixed(width, height);
        return window;
    }

    /// <summary>
    /// Sets the window to fit content width.
    /// </summary>
    /// <typeparam name="TWindow">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="fixedHeight">Fixed height.</param>
    /// <param name="maxWidth">Maximum width.</param>
    /// <returns>The window for chaining.</returns>
    public static TWindow FitContentWidth<TWindow>(this TWindow window, double fixedHeight, double maxWidth = 1000) where TWindow : Window
    {
        window.WindowSize = WindowSize.FitContentWidth(maxWidth, fixedHeight);
        return window;
    }

    /// <summary>
    /// Sets the window to fit content height.
    /// </summary>
    /// <typeparam name="TWindow">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="fixedWidth">Fixed width.</param>
    /// <param name="maxHeight">Maximum height.</param>
    /// <returns>The window for chaining.</returns>
    public static TWindow FitContentHeight<TWindow>(this TWindow window, double fixedWidth, double maxHeight = 1000) where TWindow : Window
    {
        window.WindowSize = WindowSize.FitContentHeight(fixedWidth, maxHeight);
        return window;
    }

    /// <summary>
    /// Sets the window to fit content size.
    /// </summary>
    /// <typeparam name="TWindow">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="maxWidth">Maximum width.</param>
    /// <param name="maxHeight">Maximum height.</param>
    /// <returns>The window for chaining.</returns>
    public static TWindow FitContentSize<TWindow>(this TWindow window, double maxWidth = 1000, double maxHeight = 1000) where TWindow : Window
    {
        window.WindowSize = WindowSize.FitContentSize(maxWidth, maxHeight);
        return window;
    }

    /// <summary>
    /// Sets the window to open centered on the primary screen. Must be called before <see cref="Window.Show"/>.
    /// </summary>
    /// <typeparam name="TWindow">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <returns>The window for chaining.</returns>
    /// <remarks>
    /// Do not call inside <c>OnBuild</c> — setting startup position after the window is shown throws an exception.
    /// </remarks>
    public static TWindow StartCenterScreen<TWindow>(this TWindow window) where TWindow : Window
    {
        window.StartupLocation = WindowStartupLocation.CenterScreen;
        return window;
    }

    /// <summary>
    /// Sets the window to open centered on the owner window.
    /// The owner is provided when calling <see cref="Window.Show(Window?)"/> or <see cref="Window.ShowDialogAsync(Window?)"/>.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <returns>The window for chaining.</returns>
    /// <remarks>
    /// Do not call inside <c>OnBuild</c> — setting startup position after the window is shown throws an exception.
    /// </remarks>
    public static Window StartCenterOwner(this Window window)
    {
        window.StartupLocation = WindowStartupLocation.CenterOwner;
        return window;
    }

    /// <summary>
    /// Sets the window to open at the specified position in DIPs (primary monitor DPI basis).
    /// Must be called before <see cref="Window.Show"/>.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="leftDip">Left position in DIPs.</param>
    /// <param name="topDip">Top position in DIPs.</param>
    /// <returns>The window for chaining.</returns>
    /// <remarks>
    /// Do not call inside <c>OnBuild</c> — setting startup position after the window is shown throws an exception.
    /// </remarks>
    public static Window StartManualPosition(this Window window, double leftDip, double topDip)
    {
        window.StartupLocation = WindowStartupLocation.Manual;
        window.StartupPosition = new Point(leftDip, topDip);
        return window;
    }

    /// <summary>
    /// Subscribes to <see cref="Window.WindowStateChanged"/>.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static T OnWindowStateChanged<T>(this T window, Action<WindowState> handler) where T : Window
    {
        window.WindowStateChanged += handler;
        return window;
    }

    /// <summary>
    /// Sets the window opacity (0.0 – 1.0).
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="opacity">Window opacity.</param>
    /// <returns>The window for chaining.</returns>
    public static T Opacity<T>(this T window, double opacity) where T : Window
    {
        window.Opacity = opacity;
        return window;
    }

    /// <summary>
    /// Sets the window as topmost.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="topmost">Whether the window is topmost.</param>
    /// <returns>The window for chaining.</returns>
    public static T Topmost<T>(this T window, bool topmost = true) where T : Window
    {
        window.Topmost = topmost;
        return window;
    }

    /// <summary>
    /// Sets whether the window uses tool-window behavior.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="value">Whether tool-window behavior is enabled.</param>
    /// <returns>The window for chaining.</returns>
    public static T IsToolWindow<T>(this T window, bool value = true) where T : Window
    {
        window.IsToolWindow = value;
        return window;
    }

    /// <summary>
    /// Sets whether the window allows transparent content.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="value">Whether transparent content is allowed.</param>
    /// <returns>The window for chaining.</returns>
    public static T AllowsTransparency<T>(this T window, bool value = true) where T : Window
    {
        window.AllowsTransparency = value;
        return window;
    }

    /// <summary>
    /// Sets the height of the title bar extended into the client area.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="value">Extended title bar height.</param>
    /// <returns>The window for chaining.</returns>
    public static T ExtendClientAreaTitleBarHeight<T>(this T window, double value) where T : Window
    {
        window.ExtendClientAreaTitleBarHeight = value;
        return window;
    }

    /// <summary>
    /// Sets platform-specific window options.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="value">Platform-specific options.</param>
    /// <returns>The window for chaining.</returns>
    public static T PlatformOptions<T>(this T window, PlatformWindowOptions? value) where T : Window
    {
        window.PlatformOptions = value;
        return window;
    }

    /// <summary>
    /// Sets the window state.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="value">Window state.</param>
    /// <returns>The window for chaining.</returns>
    public static T WindowState<T>(
        this T window,
        global::Aprillz.MewUI.Controls.WindowState value)
        where T : Window
    {
        window.WindowState = value;
        return window;
    }

    /// <summary>
    /// Sets whether the window can be minimized.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="value">Whether the window can be minimized.</param>
    /// <returns>The window for chaining.</returns>
    public static T CanMinimize<T>(this T window, bool value = true) where T : Window
    {
        window.CanMinimize = value;
        return window;
    }

    /// <summary>
    /// Sets whether the window can be maximized.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="value">Whether the window can be maximized.</param>
    /// <returns>The window for chaining.</returns>
    public static T CanMaximize<T>(this T window, bool value = true) where T : Window
    {
        window.CanMaximize = value;
        return window;
    }

    /// <summary>
    /// Sets whether the window can be closed.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="value">Whether the window can be closed.</param>
    /// <returns>The window for chaining.</returns>
    public static T CanClose<T>(this T window, bool value = true) where T : Window
    {
        window.CanClose = value;
        return window;
    }

    /// <summary>
    /// Sets whether the window appears in the taskbar.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="value">Whether the window appears in the taskbar.</param>
    /// <returns>The window for chaining.</returns>
    public static T ShowInTaskbar<T>(this T window, bool value = true) where T : Window
    {
        window.ShowInTaskbar = value;
        return window;
    }

    /// <summary>
    /// Sets whether layout values are rounded to device pixels.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="value">Whether layout rounding is enabled.</param>
    /// <returns>The window for chaining.</returns>
    public static T UseLayoutRounding<T>(this T window, bool value = true) where T : Window
    {
        window.UseLayoutRounding = value;
        return window;
    }

    /// <summary>
    /// Sets the window state to minimized.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <returns>The window for chaining.</returns>
    public static T Minimized<T>(this T window) where T : Window
    {
        window.WindowState = global::Aprillz.MewUI.Controls.WindowState.Minimized;
        return window;
    }

    /// <summary>
    /// Sets the window state to maximized.
    /// </summary>
    /// <typeparam name="T">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <returns>The window for chaining.</returns>
    public static T Maximized<T>(this T window) where T : Window
    {
        window.WindowState = global::Aprillz.MewUI.Controls.WindowState.Maximized;
        return window;
    }

    /// <summary>
    /// Not supported on <see cref="Window"/>. Use <see cref="Resizable"/> or assign <see cref="Window.WindowSize"/> directly.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="minWidth">Unsupported minimum width.</param>
    /// <returns>This method always throws.</returns>
    [Obsolete("Use .Resizable(w, h, minWidth: ...) or assign WindowSize directly.", error: true)]
    public static Window MinWidth(this Window window, double minWidth)
        => throw new NotSupportedException("Use .Resizable(w, h, minWidth: ...) or assign WindowSize directly.");

    /// <summary>
    /// Not supported on <see cref="Window"/>. Use <see cref="Resizable"/> or assign <see cref="Window.WindowSize"/> directly.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="minHeight">Unsupported minimum height.</param>
    /// <returns>This method always throws.</returns>
    [Obsolete("Use .Resizable(w, h, minHeight: ...) or assign WindowSize directly.", error: true)]
    public static Window MinHeight(this Window window, double minHeight)
        => throw new NotSupportedException("Use .Resizable(w, h, minHeight: ...) or assign WindowSize directly.");

    /// <summary>
    /// Not supported on <see cref="Window"/>. Use <see cref="Resizable"/> or assign <see cref="Window.WindowSize"/> directly.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="maxWidth">Unsupported maximum width.</param>
    /// <returns>This method always throws.</returns>
    [Obsolete("Use .Resizable(w, h, maxWidth: ...) or assign WindowSize directly.", error: true)]
    public static Window MaxWidth(this Window window, double maxWidth)
        => throw new NotSupportedException("Use .Resizable(w, h, maxWidth: ...) or assign WindowSize directly.");

    /// <summary>
    /// Not supported on <see cref="Window"/>. Use <see cref="Resizable"/> or assign <see cref="Window.WindowSize"/> directly.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="maxHeight">Unsupported maximum height.</param>
    /// <returns>This method always throws.</returns>
    [Obsolete("Use .Resizable(w, h, maxHeight: ...) or assign WindowSize directly.", error: true)]
    public static Window MaxHeight(this Window window, double maxHeight)
        => throw new NotSupportedException("Use .Resizable(w, h, maxHeight: ...) or assign WindowSize directly.");

    /// <summary>
    /// Sets the width.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T DockLeft<T>(this T element) where T : Element => element.DockTo(Dock.Left);

    /// <summary>
    /// Docks the element to the top.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T DockTop<T>(this T element) where T : Element => element.DockTo(Dock.Top);

    /// <summary>
    /// Docks the element to the right.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T DockRight<T>(this T element) where T : Element => element.DockTo(Dock.Right);

    /// <summary>
    /// Docks the element to the bottom.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T DockBottom<T>(this T element) where T : Element => element.DockTo(Dock.Bottom);

    #endregion

    #region Margin

    /// <summary>
    /// Sets the margin uniformly.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="value">Margin value.</param>
    /// <returns>The element for chaining.</returns>
    public static T Margin<T>(this T element, Thickness value) where T : FrameworkElement
    {
        element.Margin = value;
        return element;
    }

    /// <summary>
    /// Sets the margin uniformly.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="padding">Padding thickness.</param>
    /// <returns>The element for chaining.</returns>
    public static T Padding<T>(this T element, Thickness padding) where T : Control
    {
        element.Padding = padding;
        return element;
    }

    /// <summary>
    /// Sets the padding uniformly.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="uniform">Uniform padding value.</param>
    /// <returns>The element for chaining.</returns>
    public static T Padding<T>(this T element, double uniform) where T : Control
    {
        element.Padding = new Thickness(uniform);
        return element;
    }

    /// <summary>
    /// Sets the padding with horizontal and vertical values.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="horizontal">Horizontal padding.</param>
    /// <param name="vertical">Vertical padding.</param>
    /// <returns>The element for chaining.</returns>
    public static T Padding<T>(this T element, double horizontal, double vertical) where T : Control
    {
        element.Padding = new Thickness(horizontal, vertical, horizontal, vertical);
        return element;
    }

    /// <summary>
    /// Sets the padding with individual values.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="left">Left padding.</param>
    /// <param name="top">Top padding.</param>
    /// <param name="right">Right padding.</param>
    /// <param name="bottom">Bottom padding.</param>
    /// <returns>The element for chaining.</returns>
    public static T Padding<T>(this T element, double left, double top, double right, double bottom) where T : Control
    {
        element.Padding = new Thickness(left, top, right, bottom);
        return element;
    }

    #endregion

    #region Alignment

    /// <summary>
    /// Sets the horizontal alignment.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T StretchVertical<T>(this T element) where T : FrameworkElement
    {
        element.VerticalAlignment = MewUI.VerticalAlignment.Stretch;
        return element;
    }

    #endregion

    #region Template

    /// <summary>
    /// Registers an element by name in a template context.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="control">Element to register.</param>
    /// <param name="ctx">Template context.</param>
    /// <param name="name">Registration name.</param>
    /// <returns>The element for chaining.</returns>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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

    #region Cursor

    /// <summary>
    /// Sets the cursor type.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="cursor">Cursor type.</param>
    /// <returns>The element for chaining.</returns>
    public static T Cursor<T>(this T element, CursorType cursor) where T : UIElement
    {
        element.Cursor = cursor;
        return element;
    }

    /// <summary>
    /// Sets the render-cache policy. Pass a <see cref="BitmapCache"/> to cache the element's
    /// rendered output to an offscreen bitmap; pass <see langword="null"/> to render live.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="cacheMode">The cache policy, or null for live rendering.</param>
    /// <returns>The element for chaining.</returns>
    public static T CacheMode<T>(this T element, CacheMode? cacheMode) where T : UIElement
    {
        element.CacheMode = cacheMode;
        return element;
    }

    /// <summary>
    /// Enables (or disables) bitmap caching of the element's rendered output. The cached bitmap is
    /// blitted each frame until the element's content, size, or DPI changes; the visual tree stays
    /// live. Equivalent to assigning a <see cref="BitmapCache"/> to <see cref="UIElement.CacheMode"/>.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="cached">Whether to enable bitmap caching.</param>
    /// <returns>The element for chaining.</returns>
    public static T Cached<T>(this T element, bool cached = true) where T : UIElement
    {
        element.CacheMode = cached ? new BitmapCache() : null;
        return element;
    }

    #endregion

    #region Canvas Attached Properties

    /// <summary>
    /// Sets the canvas left position.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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
    /// <typeparam name="T">Visual type.</typeparam>
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

    #region Style

    /// <summary>
    /// Sets the style name for named style resolution from the nearest <see cref="StyleSheet"/>.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="name">Style name.</param>
    /// <returns>The control for chaining.</returns>
    public static T StyleName<T>(this T control, string name) where T : Control
    {
        control.StyleName = name;
        return control;
    }

    #endregion
}
