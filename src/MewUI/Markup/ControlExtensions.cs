namespace Aprillz.MewUI.Controls;

/// <summary>
/// Fluent API extension methods for controls.
/// </summary>
public static class ControlExtensions
{
    #region Control Base

    /// <summary>
    /// Sets the background color.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="color">Background color.</param>
    /// <returns>The control for chaining.</returns>
    public static T Background<T>(this T control, Color color) where T : Control
    {
        control.Background = color;
        return control;
    }

    /// <summary>
    /// Sets the foreground color.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="color">Foreground color.</param>
    /// <returns>The control for chaining.</returns>
    public static T Foreground<T>(this T control, Color color) where T : Control
    {
        control.Foreground = color;
        return control;
    }

    /// <summary>
    /// Sets the border brush color.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="color">Border color.</param>
    /// <returns>The control for chaining.</returns>
    public static T BorderBrush<T>(this T control, Color color) where T : Control
    {
        control.BorderBrush = color;
        return control;
    }

    /// <summary>
    /// Sets the border thickness.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="thickness">Border thickness.</param>
    /// <returns>The control for chaining.</returns>
    public static T BorderThickness<T>(this T control, double thickness) where T : Control
    {
        control.BorderThickness = thickness;
        return control;
    }

    /// <summary>
    /// Sets the font family.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="fontFamily">Font family name.</param>
    /// <returns>The control for chaining.</returns>
    public static T FontFamily<T>(this T control, string fontFamily) where T : Control
    {
        control.FontFamily = fontFamily;
        return control;
    }

    /// <summary>
    /// Sets the font size.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="fontSize">Font size.</param>
    /// <returns>The control for chaining.</returns>
    public static T FontSize<T>(this T control, double fontSize) where T : Control
    {
        control.FontSize = fontSize;
        return control;
    }

    /// <summary>
    /// Sets the font weight.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="fontWeight">Font weight.</param>
    /// <returns>The control for chaining.</returns>
    public static T FontWeight<T>(this T control, FontWeight fontWeight) where T : Control
    {
        control.FontWeight = fontWeight;
        return control;
    }

    /// <summary>
    /// Sets the font weight to bold.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <returns>The control for chaining.</returns>
    public static T Bold<T>(this T control) where T : Control
    {
        control.FontWeight = MewUI.FontWeight.Bold;
        return control;
    }

    /// <summary>
    /// Sets the tooltip text.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="text">Tooltip text.</param>
    /// <returns>The control for chaining.</returns>
    public static T ToolTip<T>(this T control, string? text) where T : Control
    {
        control.ToolTipText = text;
        return control;
    }

    /// <summary>
    /// Sets the context menu.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="menu">Context menu.</param>
    /// <returns>The control for chaining.</returns>
    public static T ContextMenu<T>(this T control, ContextMenu? menu) where T : Control
    {
        control.ContextMenu = menu;
        return control;
    }

    #endregion

    #region UIElement Events (Generic)

    #region UIElement Properties

    /// <summary>
    /// Sets the visibility state.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="isVisible">Visibility state.</param>
    /// <returns>The element for chaining.</returns>
    public static T IsVisible<T>(this T element, bool isVisible = true) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        element.IsVisible = isVisible;
        return element;
    }

    /// <summary>
    /// Sets the enabled state.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="isEnabled">Enabled state.</param>
    /// <returns>The element for chaining.</returns>
    public static T IsEnabled<T>(this T element, bool isEnabled = true) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        element.IsEnabled = isEnabled;
        return element;
    }

    /// <summary>
    /// Enables the element.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T Enable<T>(this T element) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        element.IsEnabled = true;
        return element;
    }

    /// <summary>
    /// Disables the element.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    public static T Disable<T>(this T element) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        element.IsEnabled = false;
        return element;
    }

    /// <summary>
    /// Registers a theme callback.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="apply">Theme callback action.</param>
    /// <param name="invokeImmediately">Invoke immediately flag.</param>
    /// <returns>The element for chaining.</returns>
    public static T WithTheme<T>(this T element, Action<Theme, T> apply, bool invokeImmediately = true) where T : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(apply);

        element.RegisterThemeCallback((theme, e) => apply(theme, element), invokeImmediately);
        return element;
    }

    #endregion

    #region UIElement Binding (Explicit)

    /// <summary>
    /// Binds the visibility state to an observable value.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The element for chaining.</returns>
    public static T BindIsVisible<T>(this T element, ObservableValue<bool> source) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(source);

        element.SetIsVisibleBinding(
            () => source.Value,
            h => source.Changed += h,
            h => source.Changed -= h);
        return element;
    }

    /// <summary>
    /// Binds the enabled state to an observable value.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The element for chaining.</returns>
    public static T BindIsEnabled<T>(this T element, ObservableValue<bool> source) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(source);

        element.SetIsEnabledBinding(
            () => source.Value,
            h => source.Changed += h,
            h => source.Changed -= h);
        return element;
    }

    #endregion

    /// <summary>
    /// Adds a got focus event handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnGotFocus<T>(this T element, Action handler) where T : UIElement
    {
        element.GotFocus += handler;
        return element;
    }

    /// <summary>
    /// Adds a lost focus event handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnLostFocus<T>(this T element, Action handler) where T : UIElement
    {
        element.LostFocus += handler;
        return element;
    }

    /// <summary>
    /// Adds a mouse enter event handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnMouseEnter<T>(this T element, Action handler) where T : UIElement
    {
        element.MouseEnter += handler;
        return element;
    }

    /// <summary>
    /// Adds a mouse leave event handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnMouseLeave<T>(this T element, Action handler) where T : UIElement
    {
        element.MouseLeave += handler;
        return element;
    }

    /// <summary>
    /// Adds a mouse down event handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnMouseDown<T>(this T element, Action<MouseEventArgs> handler) where T : UIElement
    {
        element.MouseDown += handler;
        return element;
    }

    /// <summary>
    /// Adds a mouse double click event handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnMouseDoubleClick<T>(this T element, Action<MouseEventArgs> handler) where T : UIElement
    {
        element.MouseDoubleClick += handler;
        return element;
    }

    /// <summary>
    /// Adds a mouse up event handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnMouseUp<T>(this T element, Action<MouseEventArgs> handler) where T : UIElement
    {
        element.MouseUp += handler;
        return element;
    }

    /// <summary>
    /// Adds a mouse move event handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnMouseMove<T>(this T element, Action<MouseEventArgs> handler) where T : UIElement
    {
        element.MouseMove += handler;
        return element;
    }

    /// <summary>
    /// Adds a mouse wheel event handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnMouseWheel<T>(this T element, Action<MouseWheelEventArgs> handler) where T : UIElement
    {
        element.MouseWheel += handler;
        return element;
    }

    /// <summary>
    /// Adds a key down event handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnKeyDown<T>(this T element, Action<KeyEventArgs> handler) where T : UIElement
    {
        element.KeyDown += handler;
        return element;
    }

    /// <summary>
    /// Adds a key up event handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnKeyUp<T>(this T element, Action<KeyEventArgs> handler) where T : UIElement
    {
        element.KeyUp += handler;
        return element;
    }

    /// <summary>
    /// Adds a text input event handler.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The element for chaining.</returns>
    public static T OnTextInput<T>(this T element, Action<TextInputEventArgs> handler) where T : UIElement
    {
        element.TextInput += handler;
        return element;
    }

    #endregion

    #region Border

    /// <summary>
    /// Sets the corner radius.
    /// </summary>
    /// <param name="border">Target border.</param>
    /// <param name="radius">Corner radius.</param>
    /// <returns>The border for chaining.</returns>
    public static Border CornerRadius(this Border border, double radius)
    {
        ArgumentNullException.ThrowIfNull(border);
        border.CornerRadius = radius;
        return border;
    }

    /// <summary>
    /// Sets the child element.
    /// </summary>
    /// <param name="border">Target border.</param>
    /// <param name="child">Child element.</param>
    /// <returns>The border for chaining.</returns>
    public static Border Child(this Border border, UIElement? child)
    {
        ArgumentNullException.ThrowIfNull(border);
        border.Child = child;
        return border;
    }

    #endregion

    #region HeaderedContentControl

    /// <summary>
    /// Sets the header text.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="header">Header text.</param>
    /// <returns>The control for chaining.</returns>
    public static T Header<T>(this T control, string header) where T : HeaderedContentControl
    {
        ArgumentNullException.ThrowIfNull(control);
        control.Header = new Label()
            .Text(header ?? string.Empty)
            .Bold();
        return control;
    }

    /// <summary>
    /// Sets the header element.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="header">Header element.</param>
    /// <returns>The control for chaining.</returns>
    public static T Header<T>(this T control, Element header) where T : HeaderedContentControl
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(header);
        control.Header = header;
        return control;
    }

    /// <summary>
    /// Sets the header spacing.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="spacing">Spacing value.</param>
    /// <returns>The control for chaining.</returns>
    public static T HeaderSpacing<T>(this T control, double spacing) where T : HeaderedContentControl
    {
        ArgumentNullException.ThrowIfNull(control);
        control.HeaderSpacing = spacing;
        return control;
    }

    #endregion

    #region Label

    /// <summary>
    /// Sets the text.
    /// </summary>
    /// <param name="label">Target label.</param>
    /// <param name="text">Text content.</param>
    /// <returns>The label for chaining.</returns>
    public static Label Text(this Label label, string text)
    {
        label.Text = text;
        return label;
    }

    /// <summary>
    /// Sets the text alignment.
    /// </summary>
    /// <param name="label">Target label.</param>
    /// <param name="alignment">Text alignment.</param>
    /// <returns>The label for chaining.</returns>
    public static Label TextAlignment(this Label label, TextAlignment alignment)
    {
        label.TextAlignment = alignment;
        return label;
    }

    /// <summary>
    /// Sets the vertical text alignment.
    /// </summary>
    /// <param name="label">Target label.</param>
    /// <param name="alignment">Vertical text alignment.</param>
    /// <returns>The label for chaining.</returns>
    public static Label VerticalTextAlignment(this Label label, TextAlignment alignment)
    {
        label.VerticalTextAlignment = alignment;
        return label;
    }

    /// <summary>
    /// Sets the text wrapping mode.
    /// </summary>
    /// <param name="label">Target label.</param>
    /// <param name="wrapping">Text wrapping mode.</param>
    /// <returns>The label for chaining.</returns>
    public static Label TextWrapping(this Label label, TextWrapping wrapping)
    {
        label.TextWrapping = wrapping;
        return label;
    }

    /// <summary>
    /// Binds the text to an observable value.
    /// </summary>
    /// <param name="label">Target label.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The label for chaining.</returns>
    public static Label BindText(this Label label, ObservableValue<string> source)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(source);

        label.SetTextBinding(
            () => source.Value,
            h => source.Changed += h,
            h => source.Changed -= h);
        return label;
    }

    /// <summary>
    /// Binds the text to an observable value with converter.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="label">Target label.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Conversion function.</param>
    /// <returns>The label for chaining.</returns>
    public static Label BindText<TSource>(this Label label, ObservableValue<TSource> source, Func<TSource, string> convert)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        label.SetTextBinding(
            () => convert(source.Value) ?? string.Empty,
            h => source.Changed += h,
            h => source.Changed -= h);
        return label;
    }

    #endregion

    #region Button

    /// <summary>
    /// Sets the button content text.
    /// </summary>
    /// <param name="button">Target button.</param>
    /// <param name="content">Content text.</param>
    /// <returns>The button for chaining.</returns>
    public static Button Content(this Button button, string content)
    {
        button.Content = content;
        return button;
    }

    /// <summary>
    /// Adds a click event handler.
    /// </summary>
    /// <param name="button">Target button.</param>
    /// <param name="handler">Click handler.</param>
    /// <returns>The button for chaining.</returns>
    public static Button OnClick(this Button button, Action handler)
    {
        button.Click += handler;
        return button;
    }

    /// <summary>
    /// Adds a left-button double click handler.
    /// </summary>
    /// <param name="button">Target button.</param>
    /// <param name="handler">Double click handler.</param>
    /// <returns>The button for chaining.</returns>
    public static Button OnDoubleClick(this Button button, Action handler)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(handler);

        button.MouseDoubleClick += e =>
        {
            if (e.Button != MouseButton.Left)
            {
                return;
            }

            handler();
            e.Handled = true;
        };
        return button;
    }

    /// <summary>
    /// Sets the can click predicate.
    /// </summary>
    /// <param name="button">Target button.</param>
    /// <param name="canClick">Can click function.</param>
    /// <returns>The button for chaining.</returns>
    public static Button OnCanClick(this Button button, Func<bool> canClick)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(canClick);

        button.CanClick = canClick;
        return button;
    }

    /// <summary>
    /// Binds the content to an observable value.
    /// </summary>
    /// <param name="button">Target button.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The button for chaining.</returns>
    public static Button BindContent(this Button button, ObservableValue<string> source)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(source);

        button.SetContentBinding(
            () => source.Value,
            h => source.Changed += h,
            h => source.Changed -= h);
        return button;
    }

    /// <summary>
    /// Binds the content to an observable value with converter.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="button">Target button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Conversion function.</param>
    /// <returns>The button for chaining.</returns>
    public static Button BindContent<TSource>(this Button button, ObservableValue<TSource> source, Func<TSource, string> convert)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(source);

        button.SetContentBinding(
            () => convert(source.Value) ?? string.Empty,
            h => source.Changed += h,
            h => source.Changed -= h);
        return button;
    }

    #endregion

    #region TextBox

    /// <summary>
    /// Sets the text.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="text">Text content.</param>
    /// <returns>The text box for chaining.</returns>
    public static TextBox Text(this TextBox textBox, string text)
    {
        textBox.Text = text;
        return textBox;
    }

    /// <summary>
    /// Sets the placeholder text.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="placeholder">Placeholder text.</param>
    /// <returns>The text box for chaining.</returns>
    public static TextBox Placeholder(this TextBox textBox, string placeholder)
    {
        textBox.Placeholder = placeholder;
        return textBox;
    }

    /// <summary>
    /// Sets the read-only state.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="isReadOnly">Read-only state.</param>
    /// <returns>The text box for chaining.</returns>
    public static TextBox IsReadOnly(this TextBox textBox, bool isReadOnly = true)
    {
        textBox.IsReadOnly = isReadOnly;
        return textBox;
    }

    /// <summary>
    /// Sets whether the text box accepts tab characters.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="acceptTab">Accept tab flag.</param>
    /// <returns>The text box for chaining.</returns>
    public static TextBox AcceptTab(this TextBox textBox, bool acceptTab = true)
    {
        textBox.AcceptTab = acceptTab;
        return textBox;
    }

    /// <summary>
    /// Adds a text changed event handler.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The text box for chaining.</returns>
    public static TextBox OnTextChanged(this TextBox textBox, Action<string> handler)
    {
        textBox.TextChanged += handler;
        return textBox;
    }

    /// <summary>
    /// Binds the text to an observable value.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The text box for chaining.</returns>
    public static TextBox BindText(this TextBox textBox, ObservableValue<string> source)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(source);

        textBox.SetTextBinding(
            () => source.Value,
            v => source.Value = v,
            h => source.Changed += h,
            h => source.Changed -= h);
        return textBox;
    }

    #endregion

    #region CheckBox

    /// <summary>
    /// Sets the text.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="text">Text content.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox Text(this CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        return checkBox;
    }

    /// <summary>
    /// Sets the checked state.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="isChecked">Checked state.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox IsChecked(this CheckBox checkBox, bool? isChecked = true)
    {
        checkBox.IsChecked = isChecked;
        return checkBox;
    }

    /// <summary>
    /// Checks the check box.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox Check(this CheckBox checkBox)
    {
        checkBox.IsChecked = true;
        return checkBox;
    }

    /// <summary>
    /// Unchecks the check box.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox Uncheck(this CheckBox checkBox)
    {
        checkBox.IsChecked = false;
        return checkBox;
    }

    /// <summary>
    /// Sets the check box to indeterminate state.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="isIndeterminate">Indeterminate flag.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox Indeterminate(this CheckBox checkBox, bool isIndeterminate = true)
    {
        checkBox.IsChecked = null;
        return checkBox;
    }

    /// <summary>
    /// Adds a checked changed event handler.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox OnCheckedChanged(this CheckBox checkBox, Action<bool> handler)
    {
        checkBox.CheckedChanged += v => handler(v ?? false);
        return checkBox;
    }

    /// <summary>
    /// Enables three-state mode.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox ThreeState(this CheckBox checkBox)
    {
        checkBox.IsThreeState = true;
        return checkBox;
    }

    /// <summary>
    /// Binds the checked state to an observable value.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox BindIsChecked(this CheckBox checkBox, ObservableValue<bool> source)
    {
        ArgumentNullException.ThrowIfNull(checkBox);
        ArgumentNullException.ThrowIfNull(source);

        checkBox.SetIsCheckedBinding(
            () => source.Value,
            v => source.Value = v ?? false,
            h => source.Changed += h,
            h => source.Changed -= h);
        return checkBox;
    }

    /// <summary>
    /// Binds the checked state to an observable nullable value.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox BindIsChecked(this CheckBox checkBox, ObservableValue<bool?> source)
    {
        ArgumentNullException.ThrowIfNull(checkBox);
        ArgumentNullException.ThrowIfNull(source);

        checkBox.SetIsCheckedBinding(
            () => source.Value,
            v => source.Value = v,
            h => source.Changed += h,
            h => source.Changed -= h);
        return checkBox;
    }

    /// <summary>
    /// Adds a check state changed event handler.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox OnCheckStateChanged(this CheckBox checkBox, Action<bool?> handler)
    {
        checkBox.CheckedChanged += handler;
        return checkBox;
    }

    /// <summary>
    /// Sets the three-state mode.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="isThreeState">Three-state flag.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox IsThreeState(this CheckBox checkBox, bool isThreeState = true)
    {
        checkBox.IsThreeState = isThreeState;
        return checkBox;
    }

    #endregion

    #region RadioButton

    /// <summary>
    /// Sets the text.
    /// </summary>
    /// <param name="radioButton">Target radio button.</param>
    /// <param name="text">Text content.</param>
    /// <returns>The radio button for chaining.</returns>
    public static RadioButton Text(this RadioButton radioButton, string text)
    {
        radioButton.Text = text;
        return radioButton;
    }

    /// <summary>
    /// Sets the group name.
    /// </summary>
    /// <param name="radioButton">Target radio button.</param>
    /// <param name="groupName">Group name.</param>
    /// <returns>The radio button for chaining.</returns>
    public static RadioButton GroupName(this RadioButton radioButton, string? groupName)
    {
        radioButton.GroupName = groupName;
        return radioButton;
    }

    /// <summary>
    /// Sets the checked state.
    /// </summary>
    /// <param name="radioButton">Target radio button.</param>
    /// <param name="isChecked">Checked state.</param>
    /// <returns>The radio button for chaining.</returns>
    public static RadioButton IsChecked(this RadioButton radioButton, bool isChecked = true)
    {
        radioButton.IsChecked = isChecked;
        return radioButton;
    }

    /// <summary>
    /// Adds a checked changed event handler.
    /// </summary>
    /// <param name="radioButton">Target radio button.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The radio button for chaining.</returns>
    public static RadioButton OnCheckedChanged(this RadioButton radioButton, Action<bool> handler)
    {
        radioButton.CheckedChanged += handler;
        return radioButton;
    }

    /// <summary>
    /// Adds a checked event handler.
    /// </summary>
    /// <param name="radioButton">Target radio button.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The radio button for chaining.</returns>
    public static RadioButton OnChecked(this RadioButton radioButton, Action handler)
    {
        radioButton.CheckedChanged += isChecked =>
        {
            if (isChecked) handler.Invoke();
        };
        return radioButton;
    }

    /// <summary>
    /// Adds an unchecked event handler.
    /// </summary>
    /// <param name="radioButton">Target radio button.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The radio button for chaining.</returns>
    public static RadioButton OnUnchecked(this RadioButton radioButton, Action handler)
    {
        radioButton.CheckedChanged += isChecked =>
        {
            if (!isChecked) handler.Invoke();
        };
        return radioButton;
    }
    /// <summary>
    /// Binds the checked state to an observable value.
    /// </summary>
    /// <param name="radioButton">Target radio button.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The radio button for chaining.</returns>
    public static RadioButton BindIsChecked(this RadioButton radioButton, ObservableValue<bool> source)
    {
        ArgumentNullException.ThrowIfNull(radioButton);
        ArgumentNullException.ThrowIfNull(source);

        radioButton.SetIsCheckedBinding(
            () => source.Value,
            v => source.Value = v,
            h => source.Changed += h,
            h => source.Changed -= h);
        return radioButton;
    }

    /// <summary>
    /// Binds the checked state to an observable value with converter.
    /// </summary>
    /// <typeparam name="T">Source value type.</typeparam>
    /// <param name="radioButton">Target radio button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Convert function.</param>
    /// <param name="convertBack">Convert back function.</param>
    /// <returns>The radio button for chaining.</returns>
    public static RadioButton BindIsChecked<T>(this RadioButton radioButton, ObservableValue<T> source, Func<T, bool> convert, Func<bool, T>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(radioButton);
        ArgumentNullException.ThrowIfNull(source);

        radioButton.SetIsCheckedBinding(
            () => convert(source.Value),
            v =>
            {
                if (convertBack is not null)
                {
                    source.Value = convertBack.Invoke(v);
                }
            },
            h => source.Changed += h,
            h => source.Changed -= h);
        return radioButton;
    }

    /// <summary>
    /// Binds the checked state to an observable value with converter.
    /// </summary>
    /// <typeparam name="T">Source value type.</typeparam>
    /// <param name="radioButton">Target radio button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Convert function.</param>
    /// <param name="convertBack">Convert back function with success flag.</param>
    /// <returns>The radio button for chaining.</returns>
    public static RadioButton BindIsChecked<T>(this RadioButton radioButton, ObservableValue<T> source, Func<T, bool> convert, Func<bool, (bool success, T value)>? convertBack)
    {
        ArgumentNullException.ThrowIfNull(radioButton);
        ArgumentNullException.ThrowIfNull(source);

        radioButton.SetIsCheckedBinding(
            () => convert(source.Value),
            v =>
            {
                if (convertBack is not null)
                {
                    var result = convertBack.Invoke(v);

                    if (result.success)
                    {
                        source.Value = result.value;
                    }
                }
            },
            h => source.Changed += h,
            h => source.Changed -= h);
        return radioButton;
    }

    #endregion

    #region ToggleSwitch

    /// <summary>
    /// Sets the text.
    /// </summary>
    /// <param name="toggleSwitch">Target toggle switch.</param>
    /// <param name="text">Text content.</param>
    /// <returns>The toggle switch for chaining.</returns>
    public static ToggleSwitch Text(this ToggleSwitch toggleSwitch, string text)
    {
        toggleSwitch.Text = text;
        return toggleSwitch;
    }

    /// <summary>
    /// Sets the checked state.
    /// </summary>
    /// <param name="toggleSwitch">Target toggle switch.</param>
    /// <param name="isChecked">Checked state.</param>
    /// <returns>The toggle switch for chaining.</returns>
    public static ToggleSwitch IsChecked(this ToggleSwitch toggleSwitch, bool isChecked = true)
    {
        toggleSwitch.IsChecked = isChecked;
        return toggleSwitch;
    }

    /// <summary>
    /// Adds a checked changed event handler.
    /// </summary>
    /// <param name="toggleSwitch">Target toggle switch.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The toggle switch for chaining.</returns>
    public static ToggleSwitch OnCheckedChanged(this ToggleSwitch toggleSwitch, Action<bool> handler)
    {
        toggleSwitch.CheckedChanged += handler;
        return toggleSwitch;
    }

    /// <summary>
    /// Binds the checked state to an observable value.
    /// </summary>
    /// <param name="toggleSwitch">Target toggle switch.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The toggle switch for chaining.</returns>
    public static ToggleSwitch BindIsChecked(this ToggleSwitch toggleSwitch, ObservableValue<bool> source)
    {
        ArgumentNullException.ThrowIfNull(toggleSwitch);
        ArgumentNullException.ThrowIfNull(source);

        toggleSwitch.SetIsCheckedBinding(
            () => source.Value,
            v => source.Value = v,
            h => source.Changed += h,
            h => source.Changed -= h);
        return toggleSwitch;
    }

    #endregion

    #region ListBox

    /// <summary>
    /// Sets the items source.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <param name="itemsSource">Items source.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox ItemsSource(this ListBox listBox, IItemsView itemsSource)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        listBox.ItemsSource = itemsSource ?? ItemsView.Empty;
        return listBox;
    }

    public static ListBox ItemsSource(this ListBox listBox, ItemsSource itemsSource)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        listBox.ItemsSource = ItemsView.From(itemsSource);
        return listBox;
    }

    /// <summary>
    /// Sets the items from string array.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <param name="items">Items array.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox Items(this ListBox listBox, params string[] items)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        listBox.ItemsSource = ItemsView.Create(items ?? Array.Empty<string>());
        return listBox;
    }

    /// <summary>
    /// Sets the items with text selector.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="listBox">Target list box.</param>
    /// <param name="items">Items collection.</param>
    /// <param name="textSelector">Text selector function.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox Items<T>(this ListBox listBox, IReadOnlyList<T> items, Func<T, string> textSelector, Func<T, object?>? keySelector = null)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        listBox.ItemsSource = items == null ? ItemsView.Empty : ItemsView.Create(items, textSelector, keySelector);
        return listBox;
    }

    /// <summary>
    /// Sets the item height.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <param name="itemHeight">Item height.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox ItemHeight(this ListBox listBox, double itemHeight)
    {
        listBox.ItemHeight = itemHeight;
        return listBox;
    }

    /// <summary>
    /// Sets the item padding.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <param name="itemPadding">Item padding.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox ItemPadding(this ListBox listBox, Thickness itemPadding)
    {
        listBox.ItemPadding = itemPadding;
        return listBox;
    }

    /// <summary>
    /// Sets the item template.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <param name="template">Item template.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox ItemTemplate(this ListBox listBox, IDataTemplate template)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        ArgumentNullException.ThrowIfNull(template);

        listBox.ItemTemplate = template;
        return listBox;
    }

    public static ListBox ItemTemplate<TItem>(
        this ListBox listBox,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind)
        => ItemTemplate(listBox, new DelegateTemplate<TItem>(build, bind));

    /// <summary>
    /// Sets the selected index.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <param name="selectedIndex">Selected index.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox SelectedIndex(this ListBox listBox, int selectedIndex)
    {
        listBox.SelectedIndex = selectedIndex;
        return listBox;
    }

    /// <summary>
    /// Adds a selection changed event handler.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox OnSelectionChanged(this ListBox listBox, Action<object?> handler)
    {
        listBox.SelectionChanged += handler;
        return listBox;
    }

    /// <summary>
    /// Binds the selected index to an observable value.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox BindSelectedIndex(this ListBox listBox, ObservableValue<int> source)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        ArgumentNullException.ThrowIfNull(source);

        listBox.SetSelectedIndexBinding(
            () => source.Value,
            v => source.Value = v,
            h => source.Changed += h,
            h => source.Changed -= h);
        return listBox;
    }

    #endregion

    #region TreeView

    /// <summary>
    /// Sets the items source.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="items">Items collection.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView ItemsSource(this TreeView treeView, IReadOnlyList<TreeViewNode> items)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        treeView.ItemsSource = items == null
            ? ItemsView.Empty
            : TreeItemsView.Create(items, n => n.Children, textSelector: n => n.Text, keySelector: n => n);
        return treeView;
    }

    /// <summary>
    /// Sets a hierarchical items source using a children selector.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="roots">Root items collection.</param>
    /// <param name="childrenSelector">Selector for child collection.</param>
    /// <param name="textSelector">Optional text selector for the default template.</param>
    /// <param name="keySelector">Optional key selector for selection/state stability.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView Items<T>(
        this TreeView treeView,
        IReadOnlyList<T> roots,
        Func<T, IReadOnlyList<T>> childrenSelector,
        Func<T, string>? textSelector = null,
        Func<T, object?>? keySelector = null)
    {
        ArgumentNullException.ThrowIfNull(treeView);

        treeView.ItemsSource = roots == null
            ? ItemsView.Empty
            : TreeItemsView.Create(roots, childrenSelector, textSelector, keySelector);

        return treeView;
    }

    /// <summary>
    /// Sets the selected node.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="selectedNode">Selected node.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView SelectedNode(this TreeView treeView, TreeViewNode? selectedNode)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        treeView.SelectedNode = selectedNode;
        return treeView;
    }

    /// <summary>
    /// Sets the item height.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="itemHeight">Item height.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView ItemHeight(this TreeView treeView, double itemHeight)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        treeView.ItemHeight = itemHeight;
        return treeView;
    }

    /// <summary>
    /// Sets the item padding.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="itemPadding">Item padding.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView ItemPadding(this TreeView treeView, Thickness itemPadding)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        treeView.ItemPadding = itemPadding;
        return treeView;
    }

    /// <summary>
    /// Sets the item template.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="template">Item template.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView ItemTemplate(this TreeView treeView, IDataTemplate template)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        ArgumentNullException.ThrowIfNull(template);

        treeView.ItemTemplate = template;
        return treeView;
    }

    public static TreeView ItemTemplate<TItem>(
        this TreeView treeView,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind)
        => ItemTemplate(treeView, new DelegateTemplate<TItem>(build, bind));

    /// <summary>
    /// Sets the indent size.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="indent">Indent size.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView Indent(this TreeView treeView, double indent)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        treeView.Indent = indent;
        return treeView;
    }

    /// <summary>
    /// Expands a node.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="node">Node to expand.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView Expand(this TreeView treeView, TreeViewNode node)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        ArgumentNullException.ThrowIfNull(node);
        treeView.Expand(node);
        return treeView;
    }

    /// <summary>
    /// Collapses a node.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="node">Node to collapse.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView Collapse(this TreeView treeView, TreeViewNode node)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        ArgumentNullException.ThrowIfNull(node);
        treeView.Collapse(node);
        return treeView;
    }

    /// <summary>
    /// Toggles a node expansion state.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="node">Node to toggle.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView Toggle(this TreeView treeView, TreeViewNode node)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        ArgumentNullException.ThrowIfNull(node);
        treeView.Toggle(node);
        return treeView;
    }

    /// <summary>
    /// Adds a selection changed event handler.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView OnSelectionChanged(this TreeView treeView, Action<object?> handler)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        ArgumentNullException.ThrowIfNull(handler);
        treeView.SelectionChanged += handler;
        return treeView;
    }

    /// <summary>
    /// Adds a selected node changed event handler.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView OnSelectedNodeChanged(this TreeView treeView, Action<TreeViewNode?> handler)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        ArgumentNullException.ThrowIfNull(handler);
        treeView.SelectedNodeChanged += handler;
        return treeView;
    }

    #endregion

    #region ContextMenu

    /// <summary>
    /// Sets the menu items.
    /// </summary>
    /// <param name="menu">Target context menu.</param>
    /// <param name="items">Menu items.</param>
    /// <returns>The context menu for chaining.</returns>
    public static ContextMenu Items(this ContextMenu menu, params MenuEntry[] items)
    {
        ArgumentNullException.ThrowIfNull(menu);

        menu.SetItems(items);

        return menu;
    }

    /// <summary>
    /// Adds a menu item.
    /// </summary>
    /// <param name="menu">Target context menu.</param>
    /// <param name="text">Item text.</param>
    /// <param name="onClick">Click handler.</param>
    /// <param name="isEnabled">Enabled state.</param>
    /// <returns>The context menu for chaining.</returns>
    public static ContextMenu Item(this ContextMenu menu, string text, Action? onClick = null, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.AddItem(text, onClick, isEnabled);
        return menu;
    }

    /// <summary>
    /// Adds a menu item with shortcut text.
    /// </summary>
    /// <param name="menu">Target context menu.</param>
    /// <param name="text">Item text.</param>
    /// <param name="shortcutText">Shortcut text.</param>
    /// <param name="onClick">Click handler.</param>
    /// <param name="isEnabled">Enabled state.</param>
    /// <returns>The context menu for chaining.</returns>
    public static ContextMenu Item(this ContextMenu menu, string text, string shortcutText, Action? onClick = null, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.AddItem(text, onClick, isEnabled, shortcutText);
        return menu;
    }

    /// <summary>
    /// Adds a submenu.
    /// </summary>
    /// <param name="menu">Target context menu.</param>
    /// <param name="text">Submenu text.</param>
    /// <param name="subMenu">Submenu.</param>
    /// <param name="isEnabled">Enabled state.</param>
    /// <returns>The context menu for chaining.</returns>
    public static ContextMenu SubMenu(this ContextMenu menu, string text, ContextMenu subMenu, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(subMenu);

        menu.AddSubMenu(text, subMenu.Menu, isEnabled);
        return menu;
    }

    /// <summary>
    /// Adds a submenu with shortcut text.
    /// </summary>
    /// <param name="menu">Target context menu.</param>
    /// <param name="text">Submenu text.</param>
    /// <param name="shortcutText">Shortcut text.</param>
    /// <param name="subMenu">Submenu.</param>
    /// <param name="isEnabled">Enabled state.</param>
    /// <returns>The context menu for chaining.</returns>
    public static ContextMenu SubMenu(this ContextMenu menu, string text, string shortcutText, ContextMenu subMenu, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(subMenu);

        menu.AddSubMenu(text, subMenu.Menu, isEnabled, shortcutText);
        return menu;
    }

    /// <summary>
    /// Adds a separator.
    /// </summary>
    /// <param name="menu">Target context menu.</param>
    /// <returns>The context menu for chaining.</returns>
    public static ContextMenu Separator(this ContextMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.AddSeparator();
        return menu;
    }

    /// <summary>
    /// Sets the item height.
    /// </summary>
    /// <param name="menu">Target context menu.</param>
    /// <param name="itemHeight">Item height.</param>
    /// <returns>The context menu for chaining.</returns>
    public static ContextMenu ItemHeight(this ContextMenu menu, double itemHeight)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.ItemHeight = itemHeight;
        return menu;
    }

    /// <summary>
    /// Sets the item padding.
    /// </summary>
    /// <param name="menu">Target context menu.</param>
    /// <param name="itemPadding">Item padding.</param>
    /// <returns>The context menu for chaining.</returns>
    public static ContextMenu ItemPadding(this ContextMenu menu, Thickness itemPadding)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.ItemPadding = itemPadding;
        return menu;
    }

    /// <summary>
    /// Sets the maximum menu height.
    /// </summary>
    /// <param name="menu">Target context menu.</param>
    /// <param name="height">Maximum height.</param>
    /// <returns>The context menu for chaining.</returns>
    public static ContextMenu MaxMenuHeight(this ContextMenu menu, double height)
    {
        menu.MaxMenuHeight = height;
        return menu;
    }

    #endregion

    #region MultiLineTextBox

    /// <summary>
    /// Sets the text.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="text">Text content.</param>
    /// <returns>The text box for chaining.</returns>
    public static MultiLineTextBox Text(this MultiLineTextBox textBox, string text)
    {
        textBox.Text = text;
        return textBox;
    }

    /// <summary>
    /// Sets the placeholder text.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="placeholder">Placeholder text.</param>
    /// <returns>The text box for chaining.</returns>
    public static MultiLineTextBox Placeholder(this MultiLineTextBox textBox, string placeholder)
    {
        textBox.Placeholder = placeholder;
        return textBox;
    }

    /// <summary>
    /// Sets the read-only state.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="isReadOnly">Read-only state.</param>
    /// <returns>The text box for chaining.</returns>
    public static MultiLineTextBox IsReadOnly(this MultiLineTextBox textBox, bool isReadOnly = true)
    {
        textBox.IsReadOnly = isReadOnly;
        return textBox;
    }

    /// <summary>
    /// Sets whether the text box accepts tab characters.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="acceptTab">Accept tab flag.</param>
    /// <returns>The text box for chaining.</returns>
    public static MultiLineTextBox AcceptTab(this MultiLineTextBox textBox, bool acceptTab = true)
    {
        textBox.AcceptTab = acceptTab;
        return textBox;
    }

    /// <summary>
    /// Sets the text wrapping mode.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="wrap">Wrap flag.</param>
    /// <returns>The text box for chaining.</returns>
    public static MultiLineTextBox Wrap(this MultiLineTextBox textBox, bool wrap = true)
    {
        textBox.Wrap = wrap;
        return textBox;
    }

    /// <summary>
    /// Adds a wrap changed event handler.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The text box for chaining.</returns>
    public static MultiLineTextBox OnWrapChanged(this MultiLineTextBox textBox, Action<bool> handler)
    {
        textBox.WrapChanged += handler;
        return textBox;
    }

    /// <summary>
    /// Adds a text changed event handler.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The text box for chaining.</returns>
    public static MultiLineTextBox OnTextChanged(this MultiLineTextBox textBox, Action<string> handler)
    {
        textBox.TextChanged += handler;
        return textBox;
    }

    /// <summary>
    /// Binds the text to an observable value.
    /// </summary>
    /// <param name="textBox">Target text box.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The text box for chaining.</returns>
    public static MultiLineTextBox BindText(this MultiLineTextBox textBox, ObservableValue<string> source)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(source);

        textBox.SetTextBinding(
            () => source.Value,
            v => source.Value = v,
            h => source.Changed += h,
            h => source.Changed -= h);
        return textBox;
    }

    #endregion

    #region ComboBox

    /// <summary>
    /// Sets the items source.
    /// </summary>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="itemsSource">Items source.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox ItemsSource(this ComboBox comboBox, IItemsView itemsSource)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.ItemsSource = itemsSource ?? ItemsView.Empty;
        return comboBox;
    }

    public static ComboBox ItemsSource(this ComboBox comboBox, ItemsSource itemsSource)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.ItemsSource = ItemsView.From(itemsSource);
        return comboBox;
    }

    /// <summary>
    /// Sets the items from string array.
    /// </summary>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="items">Items array.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox Items(this ComboBox comboBox, params string[] items)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.ItemsSource = ItemsView.Create(items ?? Array.Empty<string>());
        return comboBox;
    }

    /// <summary>
    /// Sets the items with text selector.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="items">Items collection.</param>
    /// <param name="textSelector">Text selector function.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox Items<T>(this ComboBox comboBox, IReadOnlyList<T> items, Func<T, string> textSelector, Func<T, object?>? keySelector = null)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.ItemsSource = items == null ? ItemsView.Empty : ItemsView.Create(items, textSelector, keySelector);
        return comboBox;
    }

    /// <summary>
    /// Sets the item template for the dropdown list.
    /// </summary>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="template">Item template.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox ItemTemplate(this ComboBox comboBox, IDataTemplate template)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(template);

        comboBox.ItemTemplate = template;
        return comboBox;
    }

    public static ComboBox ItemTemplate<TItem>(
        this ComboBox comboBox,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind)
        => ItemTemplate(comboBox, new DelegateTemplate<TItem>(build, bind));

    /// <summary>
    /// Sets the selected index.
    /// </summary>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="selectedIndex">Selected index.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox SelectedIndex(this ComboBox comboBox, int selectedIndex)
    {
        comboBox.SelectedIndex = selectedIndex;
        return comboBox;
    }

    /// <summary>
    /// Sets the placeholder text.
    /// </summary>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="placeholder">Placeholder text.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox Placeholder(this ComboBox comboBox, string placeholder)
    {
        comboBox.Placeholder = placeholder;
        return comboBox;
    }

    /// <summary>
    /// Adds a selection changed event handler.
    /// </summary>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox OnSelectionChanged(this ComboBox comboBox, Action<object?> handler)
    {
        comboBox.SelectionChanged += handler;
        return comboBox;
    }

    /// <summary>
    /// Binds the selected index to an observable value.
    /// </summary>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox BindSelectedIndex(this ComboBox comboBox, ObservableValue<int> source)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(source);

        comboBox.SetSelectedIndexBinding(
            () => source.Value,
            v => source.Value = v,
            h => source.Changed += h,
            h => source.Changed -= h);
        return comboBox;
    }

    #endregion

    #region TabItem

    /// <summary>
    /// Sets the header text.
    /// </summary>
    /// <param name="tab">Target tab item.</param>
    /// <param name="header">Header text.</param>
    /// <returns>The tab item for chaining.</returns>
    public static TabItem Header(this TabItem tab, string header)
    {
        ArgumentNullException.ThrowIfNull(tab);
        tab.Header = new Label().Text(header ?? string.Empty);
        return tab;
    }

    /// <summary>
    /// Sets the header element.
    /// </summary>
    /// <param name="tab">Target tab item.</param>
    /// <param name="header">Header element.</param>
    /// <returns>The tab item for chaining.</returns>
    public static TabItem Header(this TabItem tab, Element header)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(header);
        tab.Header = header;
        return tab;
    }

    /// <summary>
    /// Sets the content element.
    /// </summary>
    /// <param name="tab">Target tab item.</param>
    /// <param name="content">Content element.</param>
    /// <returns>The tab item for chaining.</returns>
    public static TabItem Content(this TabItem tab, Element content)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(content);
        tab.Content = content;
        return tab;
    }

    /// <summary>
    /// Sets the enabled state.
    /// </summary>
    /// <param name="tab">Target tab item.</param>
    /// <param name="isEnabled">Enabled state.</param>
    /// <returns>The tab item for chaining.</returns>
    public static TabItem IsEnabled(this TabItem tab, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(tab);
        tab.IsEnabled = isEnabled;
        return tab;
    }

    #endregion

    #region TabControl

    /// <summary>
    /// Sets the padding.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <param name="padding">Padding value.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl Padding(this TabControl tabControl, Thickness padding)
    {
        tabControl.Padding = padding;
        return tabControl;
    }

    /// <summary>
    /// Sets the padding with uniform value.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <param name="uniform">Uniform padding.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl Padding(this TabControl tabControl, double uniform)
    {
        tabControl.Padding = new Thickness(uniform);
        return tabControl;
    }

    /// <summary>
    /// Sets the padding with horizontal and vertical values.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <param name="horizontal">Horizontal padding.</param>
    /// <param name="vertical">Vertical padding.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl Padding(this TabControl tabControl, double horizontal, double vertical)
    {
        tabControl.Padding = new Thickness(horizontal, vertical, horizontal, vertical);
        return tabControl;
    }

    /// <summary>
    /// Sets the padding with individual values.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <param name="left">Left padding.</param>
    /// <param name="top">Top padding.</param>
    /// <param name="right">Right padding.</param>
    /// <param name="bottom">Bottom padding.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl Padding(this TabControl tabControl, double left, double top, double right, double bottom)
    {
        tabControl.Padding = new Thickness(left, top, right, bottom);
        return tabControl;
    }

    /// <summary>
    /// Sets the tab items.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <param name="tabs">Tab items.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl TabItems(this TabControl tabControl, params TabItem[] tabs)
    {
        ArgumentNullException.ThrowIfNull(tabControl);
        ArgumentNullException.ThrowIfNull(tabs);

        tabControl.ClearTabs();
        tabControl.AddTabs(tabs);
        return tabControl;
    }

    /// <summary>
    /// Sets the selected index.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <param name="selectedIndex">Selected index.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl SelectedIndex(this TabControl tabControl, int selectedIndex)
    {
        tabControl.SelectedIndex = selectedIndex;
        return tabControl;
    }

    /// <summary>
    /// Adds a selection changed event handler.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl OnSelectionChanged(this TabControl tabControl, Action<object?> handler)
    {
        tabControl.SelectionChanged += handler;
        return tabControl;
    }

    /// <summary>
    /// Adds a tab with text header.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <param name="header">Header text.</param>
    /// <param name="content">Content element.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl Tab(this TabControl tabControl, string header, Element content)
    {
        ArgumentNullException.ThrowIfNull(tabControl);
        tabControl.AddTab(new TabItem().Header(header).Content(content));
        return tabControl;
    }

    /// <summary>
    /// Adds a tab with element header.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <param name="header">Header element.</param>
    /// <param name="content">Content element.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl Tab(this TabControl tabControl, Element header, Element content)
    {
        ArgumentNullException.ThrowIfNull(tabControl);
        tabControl.AddTab(new TabItem().Header(header).Content(content));
        return tabControl;
    }

    #endregion

    #region ProgressBar

    /// <summary>
    /// Sets the minimum value.
    /// </summary>
    /// <param name="progressBar">Target progress bar.</param>
    /// <param name="minimum">Minimum value.</param>
    /// <returns>The progress bar for chaining.</returns>
    public static ProgressBar Minimum(this ProgressBar progressBar, double minimum)
    {
        progressBar.Minimum = minimum;
        return progressBar;
    }

    /// <summary>
    /// Sets the maximum value.
    /// </summary>
    /// <param name="progressBar">Target progress bar.</param>
    /// <param name="maximum">Maximum value.</param>
    /// <returns>The progress bar for chaining.</returns>
    public static ProgressBar Maximum(this ProgressBar progressBar, double maximum)
    {
        progressBar.Maximum = maximum;
        return progressBar;
    }

    /// <summary>
    /// Sets the value.
    /// </summary>
    /// <param name="progressBar">Target progress bar.</param>
    /// <param name="value">Value.</param>
    /// <returns>The progress bar for chaining.</returns>
    public static ProgressBar Value(this ProgressBar progressBar, double value)
    {
        progressBar.Value = value;
        return progressBar;
    }

    /// <summary>
    /// Binds the value to an observable value.
    /// </summary>
    /// <param name="progressBar">Target progress bar.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The progress bar for chaining.</returns>
    public static ProgressBar BindValue(this ProgressBar progressBar, ObservableValue<double> source)
    {
        ArgumentNullException.ThrowIfNull(progressBar);
        ArgumentNullException.ThrowIfNull(source);

        progressBar.SetValueBinding(
            () => source.Value,
            h => source.Changed += h,
            h => source.Changed -= h);
        return progressBar;
    }

    #endregion

    #region Slider

    /// <summary>
    /// Sets the minimum value.
    /// </summary>
    /// <param name="slider">Target slider.</param>
    /// <param name="minimum">Minimum value.</param>
    /// <returns>The slider for chaining.</returns>
    public static Slider Minimum(this Slider slider, double minimum)
    {
        slider.Minimum = minimum;
        return slider;
    }

    /// <summary>
    /// Sets the maximum value.
    /// </summary>
    /// <param name="slider">Target slider.</param>
    /// <param name="maximum">Maximum value.</param>
    /// <returns>The slider for chaining.</returns>
    public static Slider Maximum(this Slider slider, double maximum)
    {
        slider.Maximum = maximum;
        return slider;
    }

    /// <summary>
    /// Sets the value.
    /// </summary>
    /// <param name="slider">Target slider.</param>
    /// <param name="value">Value.</param>
    /// <returns>The slider for chaining.</returns>
    public static Slider Value(this Slider slider, double value)
    {
        slider.Value = value;
        return slider;
    }

    /// <summary>
    /// Sets the small change value.
    /// </summary>
    /// <param name="slider">Target slider.</param>
    /// <param name="smallChange">Small change value.</param>
    /// <returns>The slider for chaining.</returns>
    public static Slider SmallChange(this Slider slider, double smallChange)
    {
        slider.SmallChange = smallChange;
        return slider;
    }

    /// <summary>
    /// Adds a value changed event handler.
    /// </summary>
    /// <param name="slider">Target slider.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The slider for chaining.</returns>
    public static Slider OnValueChanged(this Slider slider, Action<double> handler)
    {
        slider.ValueChanged += handler;
        return slider;
    }

    /// <summary>
    /// Binds the value to an observable value.
    /// </summary>
    /// <param name="slider">Target slider.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The slider for chaining.</returns>
    public static Slider BindValue(this Slider slider, ObservableValue<double> source)
    {
        ArgumentNullException.ThrowIfNull(slider);
        ArgumentNullException.ThrowIfNull(source);

        slider.SetValueBinding(
            () => source.Value,
            v => source.Value = v,
            h => source.Changed += h,
            h => source.Changed -= h);
        return slider;
    }

    #endregion

    #region Window

    /// <summary>
    /// Sets the window title.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="title">Window title.</param>
    /// <returns>The window for chaining.</returns>
    public static Window Title(this Window window, string title)
    {
        window.Title = title;
        return window;
    }

    /// <summary>
    /// Sets the build callback.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="build">Build callback.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnBuild(this Window window, Action<Window> build)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(build);

        window.SetBuildCallback(build);

        build(window);

        return window;
    }

    /// <summary>
    /// Adds a loaded event handler.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnLoaded(this Window window, Action handler)
    {
        window.Loaded += handler;
        return window;
    }

    /// <summary>
    /// Adds a closed event handler.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnClosed(this Window window, Action handler)
    {
        window.Closed += handler;
        return window;
    }

    /// <summary>
    /// Adds an activated event handler.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnActivated(this Window window, Action handler)
    {
        window.Activated += handler;
        return window;
    }

    /// <summary>
    /// Adds a deactivated event handler.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnDeactivated(this Window window, Action handler)
    {
        window.Deactivated += handler;
        return window;
    }

    /// <summary>
    /// Adds a size changed event handler.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnSizeChanged(this Window window, Action<Size> handler)
    {
        window.ClientSizeChanged += handler;
        return window;
    }

    /// <summary>
    /// Adds a DPI changed event handler.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnDpiChanged(this Window window, Action<uint, uint> handler)
    {
        window.DpiChanged += handler;
        return window;
    }

    /// <summary>
    /// Adds a theme changed event handler.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnThemeChanged(this Window window, Action<Theme, Theme> handler)
    {
        window.ThemeChanged += handler;
        return window;
    }

    /// <summary>
    /// Adds a first frame rendered event handler.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnFirstFrameRendered(this Window window, Action handler)
    {
        window.FirstFrameRendered += handler;
        return window;
    }

    /// <summary>
    /// Adds a frame rendered event handler.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnFrameRendered(this Window window, Action handler)
    {
        window.FrameRendered += handler;
        return window;
    }

    /// <summary>
    /// Adds a preview key down event handler.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnPreviewKeyDown(this Window window, Action<KeyEventArgs> handler)
    {
        window.PreviewKeyDown += handler;
        return window;
    }

    /// <summary>
    /// Adds a preview key up event handler.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnPreviewKeyUp(this Window window, Action<KeyEventArgs> handler)
    {
        window.PreviewKeyUp += handler;
        return window;
    }

    /// <summary>
    /// Adds a preview text input event handler.
    /// </summary>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static Window OnPreviewTextInput(this Window window, Action<TextInputEventArgs> handler)
    {
        window.PreviewTextInput += handler;
        return window;
    }

    #endregion

    #region ScrollViewer

    /// <summary>
    /// Sets the vertical scroll mode.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <param name="mode">Scroll mode.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer VerticalScroll(this ScrollViewer scrollViewer, ScrollMode mode)
    {
        scrollViewer.VerticalScroll = mode;
        return scrollViewer;
    }

    /// <summary>
    /// Sets the horizontal scroll mode.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <param name="mode">Scroll mode.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer HorizontalScroll(this ScrollViewer scrollViewer, ScrollMode mode)
    {
        scrollViewer.HorizontalScroll = mode;
        return scrollViewer;
    }

    /// <summary>
    /// Disables vertical scrolling.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer NoVerticalScroll(this ScrollViewer scrollViewer) => scrollViewer.VerticalScroll(ScrollMode.Disabled);

    /// <summary>
    /// Enables auto vertical scrolling.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer AutoVerticalScroll(this ScrollViewer scrollViewer) => scrollViewer.VerticalScroll(ScrollMode.Auto);

    /// <summary>
    /// Shows vertical scrollbar.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer ShowVerticalScroll(this ScrollViewer scrollViewer) => scrollViewer.VerticalScroll(ScrollMode.Visible);

    /// <summary>
    /// Disables horizontal scrolling.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer NoHorizontalScroll(this ScrollViewer scrollViewer) => scrollViewer.HorizontalScroll(ScrollMode.Disabled);

    /// <summary>
    /// Enables auto horizontal scrolling.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer AutoHorizontalScroll(this ScrollViewer scrollViewer) => scrollViewer.HorizontalScroll(ScrollMode.Auto);

    /// <summary>
    /// Shows horizontal scrollbar.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer ShowHorizontalScroll(this ScrollViewer scrollViewer) => scrollViewer.HorizontalScroll(ScrollMode.Visible);

    /// <summary>
    /// Sets both vertical and horizontal scroll modes.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <param name="vertical">Vertical scroll mode.</param>
    /// <param name="horizontal">Horizontal scroll mode.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer Scroll(this ScrollViewer scrollViewer, ScrollMode vertical, ScrollMode horizontal)
    {
        scrollViewer.VerticalScroll = vertical;
        scrollViewer.HorizontalScroll = horizontal;
        return scrollViewer;
    }

    #endregion

    #region ContentControl

    /// <summary>
    /// Sets the content element.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="content">Content element.</param>
    /// <returns>The control for chaining.</returns>
    public static T Content<T>(this T control, Element content) where T : ContentControl
    {
        control.Content = content;
        return control;
    }

    #endregion

    #region TabControl

    /// <summary>
    /// Sets the vertical scroll mode.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <param name="mode">Scroll mode.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl VerticalScroll(this TabControl tabControl, ScrollMode mode)
    {
        tabControl.VerticalScroll = mode;
        return tabControl;
    }

    /// <summary>
    /// Sets the horizontal scroll mode.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <param name="mode">Scroll mode.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl HorizontalScroll(this TabControl tabControl, ScrollMode mode)
    {
        tabControl.HorizontalScroll = mode;
        return tabControl;
    }

    /// <summary>
    /// Disables vertical scrolling.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl NoVerticalScroll(this TabControl tabControl) => tabControl.VerticalScroll(ScrollMode.Disabled);

    /// <summary>
    /// Enables auto vertical scrolling.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl AutoVerticalScroll(this TabControl tabControl) => tabControl.VerticalScroll(ScrollMode.Auto);

    /// <summary>
    /// Shows vertical scrollbar.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl ShowVerticalScroll(this TabControl tabControl) => tabControl.VerticalScroll(ScrollMode.Visible);

    /// <summary>
    /// Disables horizontal scrolling.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl NoHorizontalScroll(this TabControl tabControl) => tabControl.HorizontalScroll(ScrollMode.Disabled);

    /// <summary>
    /// Enables auto horizontal scrolling.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl AutoHorizontalScroll(this TabControl tabControl) => tabControl.HorizontalScroll(ScrollMode.Auto);

    /// <summary>
    /// Shows horizontal scrollbar.
    /// </summary>
    /// <param name="tabControl">Target tab control.</param>
    /// <returns>The tab control for chaining.</returns>
    public static TabControl ShowHorizontalScroll(this TabControl tabControl) => tabControl.HorizontalScroll(ScrollMode.Visible);

    #endregion
}
