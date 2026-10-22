using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

#pragma warning disable CS0618 // Keep fluent compatibility methods for the legacy MultiLineTextBox.

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Fluent API extension methods for controls.
/// </summary>
public static class ControlExtensions
{
    #region Control Base

    /// <summary>
    /// Sets the font size resolved once from the current theme's <see cref="ThemeMetrics"/>;
    /// themes with per-variant metrics re-resolve by calling this inside a <see cref="WithTheme{T}(T, Action{Theme, T}, bool)"/> callback.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="size">Theme font size step.</param>
    /// <returns>The control for chaining.</returns>
    public static T FontSize<T>(this T control, ThemeFontSize size) where T : Control
    {
        control.FontSize = ResolveThemeFontSize(control.ThemeInternal.Metrics, size);
        return control;
    }

    private static double ResolveThemeFontSize(ThemeMetrics metrics, ThemeFontSize size) => size switch
    {
        ThemeFontSize.Small => metrics.FontSizeSmall,
        ThemeFontSize.Medium => metrics.FontSizeMedium,
        ThemeFontSize.Large => metrics.FontSizeLarge,
        ThemeFontSize.ExtraLarge => metrics.FontSizeExtraLarge,
        _ => metrics.FontSize
    };

    /// <summary>
    /// Sets the font weight to semi-bold.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <returns>The control for chaining.</returns>
    [FluentSetsConstant(nameof(Control.FontWeight), MewUI.FontWeight.SemiBold)]
    public static T SemiBold<T>(this T control) where T : Control
    {
        control.FontWeight = MewUI.FontWeight.SemiBold;
        return control;
    }

    /// <summary>
    /// Sets the font weight to bold.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <returns>The control for chaining.</returns>
    [FluentSetsConstant(nameof(Control.FontWeight), MewUI.FontWeight.Bold)]
    public static T Bold<T>(this T control) where T : Control
    {
        control.FontWeight = MewUI.FontWeight.Bold;
        return control;
    }

    /// <summary>
    /// Sets the font style to italic, or back to upright.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="italic">Whether to use the family's italic face.</param>
    /// <returns>The control for chaining.</returns>
    public static T Italic<T>(this T control, bool italic = true) where T : Control
    {
        control.FontStyle = italic ? MewUI.FontStyle.Italic : MewUI.FontStyle.Normal;
        return control;
    }

    /// <summary>
    /// Sets the tooltip text.
    /// </summary>
    /// <typeparam name="T">Element type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="text">Tooltip text.</param>
    /// <returns>The element for chaining.</returns>
    public static T ToolTip<T>(this T element, string? text) where T : FrameworkElement
    {
        element.ToolTip = string.IsNullOrEmpty(text) ? null : new TextBlock { Text = text };
        return element;
    }

    #endregion

    #region UIElement Events (Generic)
    #region UIElement Properties

    /// <summary>
    /// Sets <see cref="UIElement.IsEnabled"/> to true.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    [FluentSetsConstant(nameof(UIElement.IsEnabled), true)]
    public static T Enable<T>(this T element) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        element.IsEnabled = true;
        return element;
    }

    /// <summary>
    /// Sets <see cref="UIElement.IsEnabled"/> to false.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <returns>The element for chaining.</returns>
    [FluentSetsConstant(nameof(UIElement.IsEnabled), false)]
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
    /// <param name="apply">Callback invoked with the current theme and the element whenever the theme changes.</param>
    /// <param name="invokeImmediately">Whether to invoke the callback immediately with the current theme as well.</param>
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
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The element for chaining.</returns>
    public static T BindIsVisible<T>(this T element, ObservableValue<bool> source) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(source);

        element.SetBinding(UIElement.IsVisibleProperty, source);
        return element;
    }

    /// <summary>
    /// Binds the visibility state to a converted observable value.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <typeparam name="TSource">Observable value type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Converts the source value to a visibility state.</param>
    /// <returns>The element for chaining.</returns>
    public static T BindIsVisible<T, TSource>(
        this T element,
        ObservableValue<TSource> source,
        Func<TSource, bool> convert)
        where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        element.SetBinding(UIElement.IsVisibleProperty, source, convert);
        return element;
    }

    /// <summary>
    /// Binds the enabled state to an observable value.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The element for chaining.</returns>
    public static T BindIsEnabled<T>(this T element, ObservableValue<bool> source) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(source);

        element.SetBinding(UIElement.IsEnabledProperty, source);
        return element;
    }

    /// <summary>
    /// Binds the enabled state to a converted observable value.
    /// </summary>
    /// <typeparam name="T">Visual type.</typeparam>
    /// <typeparam name="TSource">Observable value type.</typeparam>
    /// <param name="element">Target element.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Converts the source value to an enabled state.</param>
    /// <returns>The element for chaining.</returns>
    public static T BindIsEnabled<T, TSource>(
        this T element,
        ObservableValue<TSource> source,
        Func<TSource, bool> convert)
        where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        element.SetBinding(UIElement.IsEnabledProperty, source, convert);
        return element;
    }

    #endregion
    #endregion

    #region Border

    /// <summary>
    /// Sets the per-side border thickness.
    /// </summary>
    /// <param name="border">Target border.</param>
    /// <param name="thickness">Per-side border thickness.</param>
    /// <returns>The border for chaining.</returns>
    public static Border BorderThickness(this Border border, [FluentSetsMember(nameof(Border.NonUniformBorderThickness))] Thickness thickness)
    {
        border.NonUniformBorderThickness = thickness;
        return border;
    }

    /// <summary>
    /// Sets the per-corner radius.
    /// </summary>
    /// <param name="border">Target border.</param>
    /// <param name="cornerRadius">Per-corner radius.</param>
    /// <returns>The border for chaining.</returns>
    public static Border CornerRadius(this Border border, [FluentSetsMember(nameof(Border.NonUniformCornerRadius))] CornerRadius cornerRadius)
    {
        border.NonUniformCornerRadius = cornerRadius;
        return border;
    }

    #endregion

    #region HeaderedContentControl

    /// <summary>
    /// Sets the header text.
    /// </summary>
    /// <typeparam name="T">Control type.</typeparam>
    /// <param name="control">Target control.</param>
    /// <param name="text">Header text.</param>
    /// <param name="accessKey">When true (default), "_" prefixes mark access key characters.</param>
    /// <returns>The control for chaining.</returns>
    public static T Header<T>(this T control, string text, bool accessKey = true) where T : HeaderedContentControl
    {
        ArgumentNullException.ThrowIfNull(control);
        if (accessKey)
        {
            var at = new AccessText().SemiBold();
            at.RawText = text ?? string.Empty;
            control.Header = at;
        }
        else
        {
            control.Header = new TextBlock().SemiBold()
                .Text(text ?? string.Empty);
        }
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

    #endregion

    #region Expander

    /// <summary>
    /// Binds the expanded state to an observable value.
    /// </summary>
    /// <param name="expander">Target expander.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The expander for chaining.</returns>
    public static Expander BindIsExpanded(this Expander expander, ObservableValue<bool> source)
    {
        ArgumentNullException.ThrowIfNull(expander);
        expander.SetBinding(Expander.IsExpandedProperty, source);
        return expander;
    }

    /// <summary>
    /// Binds the expanded state to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="expander">Target expander.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-expanded-state converter.</param>
    /// <param name="convertBack">Optional expanded-state-to-source converter.</param>
    /// <returns>The expander for chaining.</returns>
    public static Expander BindIsExpanded<TSource>(
        this Expander expander,
        ObservableValue<TSource> source,
        Func<TSource, bool> convert,
        Func<bool, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(expander);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        expander.SetBinding(Expander.IsExpandedProperty, source, convert, convertBack);
        return expander;
    }

    #endregion

    #region Label

    /// <summary>
    /// Sets <see cref="Label.Target"/>, the element that receives the access key activation.
    /// </summary>
    /// <param name="label">Target label.</param>
    /// <param name="target">Element to focus on access key activation.</param>
    /// <returns>The label for chaining.</returns>
    public static Label AccessKeyTarget(this Label label, UIElement target)
    {
        label.Target = target;
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

        label.SetBinding(Label.TextProperty, source);
        return label;
    }

    /// <summary>
    /// Binds the text to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="label">Target label.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-text converter.</param>
    /// <returns>The label for chaining.</returns>
    public static Label BindText<TSource>(this Label label, ObservableValue<TSource> source, Func<TSource, string> convert)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        label.SetBinding(Label.TextProperty, source, v => convert(v) ?? string.Empty);
        return label;
    }

    #endregion

    #region TextBlock

    /// <summary>
    /// Sets the font size resolved once from the current theme's <see cref="ThemeMetrics"/>;
    /// themes with per-variant metrics re-resolve by calling this inside a <see cref="WithTheme{T}(T, Action{Theme, T}, bool)"/> callback.
    /// </summary>
    /// <param name="textBlock">Target text block.</param>
    /// <param name="size">Theme font size step.</param>
    /// <returns>The text block for chaining.</returns>
    public static TextBlock FontSize(this TextBlock textBlock, ThemeFontSize size)
    {
        textBlock.FontSize = ResolveThemeFontSize(textBlock.ThemeInternal.Metrics, size);
        return textBlock;
    }

    /// <summary>
    /// Sets the font weight to bold.
    /// </summary>
    /// <param name="textBlock">Target text block.</param>
    /// <returns>The text block for chaining.</returns>
    [FluentSetsConstant(nameof(TextBlock.FontWeight), MewUI.FontWeight.Bold)]
    public static TextBlock Bold(this TextBlock textBlock)
    {
        textBlock.FontWeight = MewUI.FontWeight.Bold;
        return textBlock;
    }

    /// <summary>
    /// Sets the font weight to semi-bold.
    /// </summary>
    /// <param name="textBlock">Target text block.</param>
    /// <returns>The text block for chaining.</returns>
    [FluentSetsConstant(nameof(TextBlock.FontWeight), MewUI.FontWeight.SemiBold)]
    public static TextBlock SemiBold(this TextBlock textBlock)
    {
        textBlock.FontWeight = MewUI.FontWeight.SemiBold;
        return textBlock;
    }

    /// <summary>
    /// Sets the font style to italic, or back to upright.
    /// </summary>
    /// <param name="textBlock">Target text block.</param>
    /// <param name="italic">Whether to use the family's italic face.</param>
    /// <returns>The text block for chaining.</returns>
    public static TextBlock Italic(this TextBlock textBlock, bool italic = true)
    {
        textBlock.FontStyle = italic ? MewUI.FontStyle.Italic : MewUI.FontStyle.Normal;
        return textBlock;
    }

    /// <summary>
    /// Binds the text to an observable value.
    /// </summary>
    /// <typeparam name="T">Text block type.</typeparam>
    /// <param name="textBlock">Target text block.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The text block for chaining.</returns>
    public static T BindText<T>(this T textBlock, ObservableValue<string> source) where T : TextBlock
    {
        ArgumentNullException.ThrowIfNull(textBlock);
        ArgumentNullException.ThrowIfNull(source);

        textBlock.SetBinding(TextBlock.TextProperty, source);
        return textBlock;
    }

    /// <summary>
    /// Binds the text to a converted observable value.
    /// </summary>
    /// <typeparam name="T">Text block type.</typeparam>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="textBlock">Target text block.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-text converter.</param>
    /// <returns>The text block for chaining.</returns>
    public static T BindText<T, TSource>(this T textBlock, ObservableValue<TSource> source, Func<TSource, string> convert) where T : TextBlock
    {
        ArgumentNullException.ThrowIfNull(textBlock);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        textBlock.SetBinding(TextBlock.TextProperty, source, v => convert(v) ?? string.Empty);
        return textBlock;
    }

    #endregion

    #region AccessText

    internal static AccessText RawText(this AccessText at, string text)
    {
        at.RawText = text;
        return at;
    }

    internal static AccessText Foreground(this AccessText at, Color color)
    {
        at.Foreground = color;
        return at;
    }

    internal static AccessText FontFamily(this AccessText at, string fontFamily)
    {
        at.FontFamily = fontFamily;
        return at;
    }

    internal static AccessText FontSize(this AccessText at, double fontSize)
    {
        at.FontSize = fontSize;
        return at;
    }

    internal static AccessText FontWeight(this AccessText at, FontWeight fontWeight)
    {
        at.FontWeight = fontWeight;
        return at;
    }

    internal static AccessText Bold(this AccessText at)
    {
        at.FontWeight = MewUI.FontWeight.Bold;
        return at;
    }

    internal static AccessText SemiBold(this AccessText at)
    {
        at.FontWeight = MewUI.FontWeight.SemiBold;
        return at;
    }

    #endregion

    #region Button

    /// <summary>
    /// Sets the button content to a centered text label. When <paramref name="accessKey"/> is true (default),
    /// "_" prefixes mark access key characters (e.g., "_Save" registers Alt+S).
    /// </summary>
    /// <param name="button">Target button.</param>
    /// <param name="text">Content text.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The button for chaining.</returns>
    public static Button Content(this Button button, string text, bool accessKey = true)
    {
        if (accessKey)
        {
            var at = new AccessText
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = MewUI.TextAlignment.Center,
                VerticalTextAlignment = MewUI.TextAlignment.Center,
            };
            at.RawText = text;
            button.Content = at;
        }
        else
        {
            button.Content = new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = MewUI.TextAlignment.Center,
                VerticalTextAlignment = MewUI.TextAlignment.Center,
            };
        }
        return button;
    }

    /// <summary>
    /// Binds the button content to an observable string value (creates a centered text label).
    /// </summary>
    /// <param name="button">Target button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The button for chaining.</returns>
    public static Button BindContent(this Button button, ObservableValue<string> source, bool accessKey = true)
    {
        if (accessKey)
        {
            var at = new AccessText
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = MewUI.TextAlignment.Center,
                VerticalTextAlignment = MewUI.TextAlignment.Center,
            };
            at.SetBinding(AccessText.RawTextProperty, source, BindingMode.OneWay);
            button.Content = at;
        }
        else
        {
            var tb = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = MewUI.TextAlignment.Center,
                VerticalTextAlignment = MewUI.TextAlignment.Center,
            };
            tb.SetBinding(TextBlock.TextProperty, source, BindingMode.OneWay);
            button.Content = tb;
        }
        return button;
    }

    /// <summary>
    /// Binds the button content to a converted observable value (creates a centered text label).
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="button">Target button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-text converter.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The button for chaining.</returns>
    public static Button BindContent<TSource>(this Button button, ObservableValue<TSource> source, Func<TSource, string> convert, bool accessKey = true)
    {
        if (accessKey)
        {
            var at = new AccessText
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = MewUI.TextAlignment.Center,
                VerticalTextAlignment = MewUI.TextAlignment.Center,
            };
            at.SetBinding(AccessText.RawTextProperty, source, v => convert(v) ?? string.Empty);
            button.Content = at;
        }
        else
        {
            var tb = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = MewUI.TextAlignment.Center,
                VerticalTextAlignment = MewUI.TextAlignment.Center,
            };
            tb.SetBinding(TextBlock.TextProperty, source, v => convert(v) ?? string.Empty);
            button.Content = tb;
        }
        return button;
    }

    /// <summary>
    /// Binds the button content element to an observable value.
    /// </summary>
    /// <param name="button">Target button.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The button for chaining.</returns>
    public static T BindContent<T>(this T button, ObservableValue<Element?> source) where T : Button
    {
        button.SetBinding(Button.ContentProperty, source, BindingMode.OneWay);
        return button;
    }

    /// <summary>
    /// Binds the button content element to a converted observable value.
    /// </summary>
    /// <typeparam name="T">Button type.</typeparam>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="button">Target button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-element converter.</param>
    /// <returns>The button for chaining.</returns>
    public static T BindContent<T, TSource>(
        this T button,
        ObservableValue<TSource> source,
        Func<TSource, Element?> convert)
        where T : Button
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        button.SetBinding(Button.ContentProperty, source, convert, mode: BindingMode.OneWay);
        return button;
    }

    /// <summary>
    /// Sets the semantic command invoked by the button.
    /// </summary>
    public static T Command<T>(
        this T button,
        [FluentSetsMember(nameof(Button.Command))] Command? command,
        [FluentSetsMember(nameof(Button.CommandPresentationMode))] CommandPresentationMode presentation = CommandPresentationMode.None)
        where T : Button
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Command = command;
        button.CommandPresentationMode = presentation;
        return button;
    }

    /// <summary>
    /// Binds the button's semantic command to an observable value.
    /// </summary>
    public static T BindCommand<T>(
        this T button,
        ObservableValue<Command?> source,
        CommandPresentationMode presentation = CommandPresentationMode.None)
        where T : Button
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(source);
        button.SetBinding(Button.CommandProperty, source, BindingMode.OneWay);
        button.CommandPresentationMode = presentation;
        return button;
    }

    /// <summary>
    /// Sets the predicate asked whether the button can be clicked.
    /// </summary>
    /// <param name="button">Target button.</param>
    /// <param name="predicate">Predicate, or null to ask nothing.</param>
    /// <returns>The button for chaining.</returns>
    public static T OnCanClick<T>(this T button, Func<bool>? predicate) where T : Button
    {
        ArgumentNullException.ThrowIfNull(button);
        button.CanClick = predicate;
        return button;
    }

    /// <summary>
    /// Adds a left-button double click handler.
    /// </summary>
    /// <param name="button">Target button.</param>
    /// <param name="handler">Double click handler.</param>
    /// <returns>The button for chaining.</returns>
    public static T OnDoubleClick<T>(this T button, Action handler) where T : Button
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

    #endregion

    #region TextBox

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
        textBox.SetBinding(TextBox.TextProperty, source);
        return textBox;
    }

    /// <summary>
    /// Binds the text to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="textBox">Target text box.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-text converter.</param>
    /// <param name="convertBack">Optional text-to-source converter.</param>
    /// <returns>The text box for chaining.</returns>
    public static TextBox BindText<TSource>(
        this TextBox textBox,
        ObservableValue<TSource> source,
        Func<TSource, string> convert,
        Func<string, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        textBox.SetBinding(TextBox.TextProperty, source, convert, convertBack);
        return textBox;
    }

    /// <summary>
    /// Binds the text to an observable value.
    /// </summary>
    /// <param name="textBox">Target multiline text box.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The multiline text box for chaining.</returns>
    public static MultiLineTextBox BindText(this MultiLineTextBox textBox, ObservableValue<string> source)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(source);
        textBox.SetBinding(MultiLineTextBox.TextProperty, source);
        return textBox;
    }

    /// <summary>
    /// Binds the text to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="textBox">Target multiline text box.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-text converter.</param>
    /// <param name="convertBack">Optional text-to-source converter.</param>
    /// <returns>The multiline text box for chaining.</returns>
    public static MultiLineTextBox BindText<TSource>(
        this MultiLineTextBox textBox,
        ObservableValue<TSource> source,
        Func<TSource, string> convert,
        Func<string, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        textBox.SetBinding(MultiLineTextBox.TextProperty, source, convert, convertBack);
        return textBox;
    }

    /// <summary>
    /// Binds the password to an observable value.
    /// </summary>
    /// <param name="passwordBox">Target password box.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The password box for chaining.</returns>
    public static PasswordBox BindPassword(this PasswordBox passwordBox, ObservableValue<string> source)
    {
        ArgumentNullException.ThrowIfNull(passwordBox);
        ArgumentNullException.ThrowIfNull(source);
        passwordBox.SetBinding(PasswordBox.PasswordProperty, source);
        return passwordBox;
    }

    /// <summary>
    /// Binds the password to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="passwordBox">Target password box.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-password converter.</param>
    /// <param name="convertBack">Optional password-to-source converter.</param>
    /// <returns>The password box for chaining.</returns>
    public static PasswordBox BindPassword<TSource>(
        this PasswordBox passwordBox,
        ObservableValue<TSource> source,
        Func<TSource, string> convert,
        Func<string, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(passwordBox);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        passwordBox.SetBinding(PasswordBox.PasswordProperty, source, convert, convertBack);
        return passwordBox;
    }

    #endregion

    #region ToggleBase

    /// <summary>
    /// Sets the content to a text label. When <paramref name="accessKey"/> is true (default),
    /// "_" prefixes mark access key characters (e.g., "_Save" registers Alt+S).
    /// </summary>
    /// <typeparam name="T">Toggle control type.</typeparam>
    /// <param name="control">Target toggle control.</param>
    /// <param name="text">Content text.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The control for chaining.</returns>
    public static T Content<T>(this T control, string text, bool accessKey = true) where T : ToggleBase
    {
        if (accessKey)
        {
            var at = new AccessText();
            at.RawText = text;
            control.Content = at;
        }
        else
        {
            control.Content = new TextBlock { Text = text };
        }
        return control;
    }

    /// <summary>
    /// Binds the content to an observable text label. When <paramref name="accessKey"/> is true (default),
    /// "_" prefixes mark access key characters and stay in sync as the source changes.
    /// </summary>
    /// <typeparam name="T">Toggle control type.</typeparam>
    /// <param name="control">Target toggle control.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The control for chaining.</returns>
    public static T BindContent<T>(this T control, ObservableValue<string> source, bool accessKey = true) where T : ToggleBase
    {
        if (accessKey)
        {
            var at = new AccessText();
            at.SetBinding(AccessText.RawTextProperty, source, BindingMode.OneWay);
            control.Content = at;
        }
        else
        {
            var tb = new TextBlock();
            tb.SetBinding(TextBlock.TextProperty, source, BindingMode.OneWay);
            control.Content = tb;
        }
        return control;
    }

    /// <summary>
    /// Binds the content to a converted observable text label. When <paramref name="accessKey"/> is
    /// true (default), "_" prefixes in the converted text mark access key characters.
    /// </summary>
    /// <typeparam name="T">Toggle control type.</typeparam>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="control">Target toggle control.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-text converter.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The control for chaining.</returns>
    public static T BindContent<T, TSource>(this T control, ObservableValue<TSource> source, Func<TSource, string> convert, bool accessKey = true) where T : ToggleBase
    {
        if (accessKey)
        {
            var at = new AccessText();
            at.SetBinding(AccessText.RawTextProperty, source, v => convert(v) ?? string.Empty);
            control.Content = at;
        }
        else
        {
            var tb = new TextBlock();
            tb.SetBinding(TextBlock.TextProperty, source, v => convert(v) ?? string.Empty);
            control.Content = tb;
        }
        return control;
    }

    #endregion

    #region CheckBox

    /// <summary>
    /// Sets the content to a text label. When <paramref name="accessKey"/> is true (default),
    /// "_" prefixes mark access key characters (e.g., "_Remember me" registers Alt+R).
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="text">Content text.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox Content(this CheckBox checkBox, string text, bool accessKey = true)
    {
        if (accessKey)
        {
            var at = new AccessText();
            at.RawText = text;
            checkBox.Content = at;
        }
        else
        {
            checkBox.Content = new TextBlock { Text = text };
        }
        return checkBox;
    }

    /// <summary>
    /// Binds the content to an observable text label. When <paramref name="accessKey"/> is true (default),
    /// "_" prefixes mark access key characters and stay in sync as the source changes.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox BindContent(this CheckBox checkBox, ObservableValue<string> source, bool accessKey = true)
    {
        if (accessKey)
        {
            var at = new AccessText();
            at.SetBinding(AccessText.RawTextProperty, source, BindingMode.OneWay);
            checkBox.Content = at;
        }
        else
        {
            var tb = new TextBlock();
            tb.SetBinding(TextBlock.TextProperty, source, BindingMode.OneWay);
            checkBox.Content = tb;
        }
        return checkBox;
    }

    /// <summary>
    /// Binds the content to a converted observable text label. When <paramref name="accessKey"/> is
    /// true (default), "_" prefixes in the converted text mark access key characters.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-text converter.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox BindContent<TSource>(this CheckBox checkBox, ObservableValue<TSource> source, Func<TSource, string> convert, bool accessKey = true)
    {
        if (accessKey)
        {
            var at = new AccessText();
            at.SetBinding(AccessText.RawTextProperty, source, v => convert(v) ?? string.Empty);
            checkBox.Content = at;
        }
        else
        {
            var tb = new TextBlock();
            tb.SetBinding(TextBlock.TextProperty, source, v => convert(v) ?? string.Empty);
            checkBox.Content = tb;
        }
        return checkBox;
    }

    /// <summary>
    /// Sets the checked state; null is indeterminate.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="isChecked">Checked state, or null for indeterminate.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox IsChecked(this CheckBox checkBox, bool? isChecked = true)
    {
        checkBox.IsChecked = isChecked;
        return checkBox;
    }

    /// <summary>
    /// Sets <see cref="CheckBox.IsChecked"/> to true.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <returns>The check box for chaining.</returns>
    [FluentSetsConstant(nameof(CheckBox.IsChecked), true)]
    public static CheckBox Check(this CheckBox checkBox)
    {
        checkBox.IsChecked = true;
        return checkBox;
    }

    /// <summary>
    /// Sets <see cref="CheckBox.IsChecked"/> to false.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <returns>The check box for chaining.</returns>
    [FluentSetsConstant(nameof(CheckBox.IsChecked), false)]
    public static CheckBox Uncheck(this CheckBox checkBox)
    {
        checkBox.IsChecked = false;
        return checkBox;
    }

    /// <summary>
    /// Sets <see cref="CheckBox.IsChecked"/> to null, the indeterminate state.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="isIndeterminate">Not read; the state is always set to indeterminate.</param>
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
    /// Sets <see cref="CheckBox.IsThreeState"/> to true.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <returns>The check box for chaining.</returns>
    [FluentSetsConstant(nameof(CheckBox.IsThreeState), true)]
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

        checkBox.SetBinding(CheckBox.IsCheckedProperty, source, v => (bool?)v, v => v ?? false);
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

        checkBox.SetBinding(CheckBox.IsCheckedProperty, source);
        return checkBox;
    }

    /// <summary>
    /// Binds the checked state to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-checked-state converter.</param>
    /// <param name="convertBack">Optional checked-state-to-source converter.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox BindIsChecked<TSource>(
        this CheckBox checkBox,
        ObservableValue<TSource> source,
        Func<TSource, bool?> convert,
        Func<bool?, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(checkBox);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        checkBox.SetBinding(CheckBox.IsCheckedProperty, source, convert, convertBack);
        return checkBox;
    }

    /// <summary>
    /// Adds a <see cref="CheckBox.CheckedChanged"/> handler that receives the three-state value.
    /// </summary>
    /// <param name="checkBox">Target check box.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The check box for chaining.</returns>
    public static CheckBox OnCheckStateChanged(this CheckBox checkBox, [FluentSetsMember(nameof(CheckBox.CheckedChanged))] Action<bool?> handler)
    {
        checkBox.CheckedChanged += handler;
        return checkBox;
    }

    #endregion

    #region RadioButton

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

        radioButton.SetBinding(ToggleBase.IsCheckedProperty, source);
        return radioButton;
    }

    /// <summary>
    /// Binds the checked state to a converted observable value.
    /// </summary>
    /// <typeparam name="T">Source value type.</typeparam>
    /// <param name="radioButton">Target radio button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-checked-state converter.</param>
    /// <param name="convertBack">Optional checked-state-to-source converter.</param>
    /// <returns>The radio button for chaining.</returns>
    public static RadioButton BindIsChecked<T>(this RadioButton radioButton, ObservableValue<T> source, Func<T, bool> convert, Func<bool, T>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(radioButton);
        ArgumentNullException.ThrowIfNull(source);

        radioButton.SetBinding(ToggleBase.IsCheckedProperty, source, convert, convertBack);
        return radioButton;
    }

    /// <summary>
    /// Binds the checked state to a converted observable value, with a convert-back that can report
    /// failure.
    /// </summary>
    /// <typeparam name="T">Source value type.</typeparam>
    /// <param name="radioButton">Target radio button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-checked-state converter.</param>
    /// <param name="convertBack">Optional checked-state-to-source converter that reports success.</param>
    /// <returns>The radio button for chaining.</returns>
    public static RadioButton BindIsChecked<T>(this RadioButton radioButton, ObservableValue<T> source, Func<T, bool> convert, Func<bool, (bool success, T value)>? convertBack)
    {
        ArgumentNullException.ThrowIfNull(radioButton);
        ArgumentNullException.ThrowIfNull(source);

        Func<bool, T>? wrappedConvertBack = convertBack != null
            ? v => { var r = convertBack(v); return r.success ? r.value : source.Value; }
        : null;
        radioButton.SetBinding(ToggleBase.IsCheckedProperty, source, convert, wrappedConvertBack);
        return radioButton;
    }

    #endregion

    #region ToggleButton

    /// <summary>
    /// Sets the content to a centered text label. When <paramref name="accessKey"/> is true (default),
    /// "_" prefixes mark access key characters.
    /// </summary>
    /// <param name="toggleButton">Target toggle button.</param>
    /// <param name="text">Content text.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The toggle button for chaining.</returns>
    public static ToggleButton Content(this ToggleButton toggleButton, string text, bool accessKey = true)
    {
        if (accessKey)
        {
            var at = new AccessText
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = MewUI.TextAlignment.Center,
                VerticalTextAlignment = MewUI.TextAlignment.Center,
            };
            at.RawText = text;
            toggleButton.Content = at;
        }
        else
        {
            toggleButton.Content = new TextBlock
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = MewUI.TextAlignment.Center,
                VerticalTextAlignment = MewUI.TextAlignment.Center,
            };
        }
        return toggleButton;
    }

    /// <summary>
    /// Binds the content to an observable text label (centered). When <paramref name="accessKey"/> is true
    /// (default), "_" prefixes mark access key characters and stay in sync as the source changes.
    /// </summary>
    /// <param name="toggleButton">Target toggle button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The toggle button for chaining.</returns>
    public static ToggleButton BindContent(this ToggleButton toggleButton, ObservableValue<string> source, bool accessKey = true)
    {
        if (accessKey)
        {
            var at = new AccessText
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = MewUI.TextAlignment.Center,
                VerticalTextAlignment = MewUI.TextAlignment.Center,
            };
            at.SetBinding(AccessText.RawTextProperty, source, BindingMode.OneWay);
            toggleButton.Content = at;
        }
        else
        {
            var tb = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = MewUI.TextAlignment.Center,
                VerticalTextAlignment = MewUI.TextAlignment.Center,
            };
            tb.SetBinding(TextBlock.TextProperty, source, BindingMode.OneWay);
            toggleButton.Content = tb;
        }
        return toggleButton;
    }

    /// <summary>
    /// Binds the content to a converted observable text label (centered). When
    /// <paramref name="accessKey"/> is true (default), "_" prefixes in the converted text mark
    /// access key characters.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="toggleButton">Target toggle button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-text converter.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The toggle button for chaining.</returns>
    public static ToggleButton BindContent<TSource>(this ToggleButton toggleButton, ObservableValue<TSource> source, Func<TSource, string> convert, bool accessKey = true)
    {
        if (accessKey)
        {
            var at = new AccessText
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = MewUI.TextAlignment.Center,
                VerticalTextAlignment = MewUI.TextAlignment.Center,
            };
            at.SetBinding(AccessText.RawTextProperty, source, v => convert(v) ?? string.Empty);
            toggleButton.Content = at;
        }
        else
        {
            var tb = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = MewUI.TextAlignment.Center,
                VerticalTextAlignment = MewUI.TextAlignment.Center,
            };
            tb.SetBinding(TextBlock.TextProperty, source, v => convert(v) ?? string.Empty);
            toggleButton.Content = tb;
        }
        return toggleButton;
    }

    /// <summary>
    /// Binds the checked state to an observable value.
    /// </summary>
    /// <param name="toggleButton">Target toggle button.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The toggle button for chaining.</returns>
    public static ToggleButton BindIsChecked(this ToggleButton toggleButton, ObservableValue<bool> source)
    {
        ArgumentNullException.ThrowIfNull(toggleButton);
        ArgumentNullException.ThrowIfNull(source);

        toggleButton.SetBinding(ToggleBase.IsCheckedProperty, source);
        return toggleButton;
    }

    /// <summary>
    /// Binds the checked state to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="toggleButton">Target toggle button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-checked-state converter.</param>
    /// <param name="convertBack">Optional checked-state-to-source converter.</param>
    /// <returns>The toggle button for chaining.</returns>
    public static ToggleButton BindIsChecked<TSource>(
        this ToggleButton toggleButton,
        ObservableValue<TSource> source,
        Func<TSource, bool> convert,
        Func<bool, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(toggleButton);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        toggleButton.SetBinding(ToggleBase.IsCheckedProperty, source, convert, convertBack);
        return toggleButton;
    }

    #endregion

    #region ToggleSwitch

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

        toggleSwitch.SetBinding(ToggleBase.IsCheckedProperty, source);
        return toggleSwitch;
    }

    /// <summary>
    /// Binds the checked state to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="toggleSwitch">Target toggle switch.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-checked-state converter.</param>
    /// <param name="convertBack">Optional checked-state-to-source converter.</param>
    /// <returns>The toggle switch for chaining.</returns>
    public static ToggleSwitch BindIsChecked<TSource>(
        this ToggleSwitch toggleSwitch,
        ObservableValue<TSource> source,
        Func<TSource, bool> convert,
        Func<bool, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(toggleSwitch);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        toggleSwitch.SetBinding(ToggleBase.IsCheckedProperty, source, convert, convertBack);
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
    public static ListBox ItemsSource(this ListBox listBox, ISelectableItemsView itemsSource)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        listBox.ItemsSource = itemsSource ?? ItemsView.EmptySelectable;
        return listBox;
    }

    /// <summary>
    /// Sets the items source from a legacy <see cref="MewUI.ItemsSource"/>; obsolete, use
    /// <see cref="ItemsSource(ListBox, ISelectableItemsView)"/> instead.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <param name="itemsSource">Legacy items source.</param>
    /// <returns>The list box for chaining.</returns>
    [Obsolete("Use the ItemsView.Create overload instead. A legacy ItemsSource reports a snapshot "
        + "only, so items added or removed after this call are not reflected.")]
    public static ListBox ItemsSource(this ListBox listBox, ItemsSource itemsSource)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        listBox.ItemsSource = ItemsView.From(itemsSource);
        return listBox;
    }

    /// <summary>
    /// Sets <see cref="ListBox.ItemsSource"/> to a view over the strings.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <param name="items">Item strings.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox Items(this ListBox listBox, params string[] items)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        listBox.ItemsSource = ItemsView.Create(items ?? Array.Empty<string>());
        return listBox;
    }

    /// <summary>
    /// Sets <see cref="ListBox.ItemsSource"/> to a view over the items, with an optional text
    /// selector.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="listBox">Target list box.</param>
    /// <param name="items">Items collection.</param>
    /// <param name="textSelector">Optional text of an item; without one the item's <c>ToString()</c> is used.</param>
    /// <param name="keySelector">Optional key selector to stabilize selection when items change.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox Items<T>(this ListBox listBox, IReadOnlyList<T> items, Func<T, string>? textSelector = null, Func<T, object?>? keySelector = null)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        listBox.ItemsSource = items == null ? ItemsView.EmptySelectable : ItemsView.Create(items, textSelector, keySelector);
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

    /// <summary>
    /// Sets the item template using delegate-based templating.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="listBox">Target list box.</param>
    /// <param name="build">Template build callback.</param>
    /// <param name="bind">Template bind callback.</param>
    /// <param name="unbind">Optional template cleanup callback.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox ItemTemplate<TItem>(
        this ListBox listBox,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind,
        Action<FrameworkElement, TItem, int, TemplateContext>? unbind = null)
        => ItemTemplate(listBox, new DelegateTemplate<TItem>(build, bind, unbind));

    /// <summary>
    /// Configures the container of each item once its content is bound, so behavior can be attached
    /// to the whole item rather than to the template. The container is the
    /// <see cref="ItemContainer"/> the list box places around every item.
    /// </summary>
    /// <remarks>
    /// The callback runs on every bind, which includes scrolling an item back into view, so keep it
    /// cheap. Register event subscriptions through the supplied context and they are removed before
    /// the next item is bound; properties assigned directly are reset by the framework instead.
    /// </remarks>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="listBox">Target list box.</param>
    /// <param name="prepare">Container configuration callback.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox PrepareContainer<T>(this ListBox listBox, PrepareContainerHandler<ItemContainer, T> prepare)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        ArgumentNullException.ThrowIfNull(prepare);

        listBox.SetPrepareContainer((container, item, index, context) => prepare(container, (T)item!, index, context));
        return listBox;
    }

    /// <summary>
    /// Releases what <see cref="PrepareContainer{T}(ListBox, PrepareContainerHandler{ItemContainer, T})"/>
    /// attached outside the template context, before the container takes another item. Most hooks
    /// need no counterpart: context subscriptions and container properties are undone for you.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="listBox">Target list box.</param>
    /// <param name="clear">Container release callback.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox ClearContainer<T>(this ListBox listBox, PrepareContainerHandler<ItemContainer, T> clear)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        ArgumentNullException.ThrowIfNull(clear);

        listBox.SetClearContainer((container, item, index, context) => clear(container, (T)item!, index, context));
        return listBox;
    }

    /// <summary>
    /// Uses fixed-height row virtualization with theme default item height.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox FixedHeightPresenter(this ListBox listBox)
    {
        listBox.SetPresenter(new FixedHeightItemsPresenter());
        return listBox;
    }

    /// <summary>
    /// Uses fixed-height row virtualization with explicit item height.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <param name="itemHeight">Fixed item height.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox FixedHeightPresenter(this ListBox listBox, double itemHeight)
    {
        listBox.SetPresenter(new FixedHeightItemsPresenter { ItemHeight = itemHeight });
        return listBox;
    }

    /// <summary>
    /// Uses variable-height virtualization (items are measured individually).
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox VariableHeightPresenter(this ListBox listBox)
    {
        listBox.SetPresenter(new VariableHeightItemsPresenter());
        return listBox;
    }

    /// <summary>
    /// Uses non-virtualizing stack layout (all items realized).
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox StackPresenter(this ListBox listBox)
    {
        listBox.SetPresenter(new StackItemsPresenter());
        return listBox;
    }

    /// <summary>
    /// Uses wrap-grid virtualization with fixed item size.
    /// </summary>
    /// <param name="listBox">Target list box.</param>
    /// <param name="itemWidth">Fixed item width.</param>
    /// <param name="itemHeight">Fixed item height.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox WrapPresenter(this ListBox listBox, double itemWidth, double itemHeight)
    {
        listBox.SetPresenter(new WrapItemsPresenter { ItemWidth = itemWidth, ItemHeight = itemHeight });
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

        listBox.SetBinding(ListBox.SelectedIndexProperty, source);
        return listBox;
    }

    /// <summary>
    /// Binds the selected index to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="listBox">Target list box.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-index converter.</param>
    /// <param name="convertBack">Optional index-to-source converter.</param>
    /// <returns>The list box for chaining.</returns>
    public static ListBox BindSelectedIndex<TSource>(
        this ListBox listBox,
        ObservableValue<TSource> source,
        Func<TSource, int> convert,
        Func<int, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        listBox.SetBinding(ListBox.SelectedIndexProperty, source, convert, convertBack);
        return listBox;
    }

    #endregion

    #region SegmentedControl

    /// <summary>
    /// Sets <see cref="SegmentedBase.ItemsSource"/> to a view over the strings, one segment each.
    /// </summary>
    /// <param name="control">Target segmented control.</param>
    /// <param name="items">Segment labels.</param>
    /// <returns>The control for chaining.</returns>
    public static TSelf Items<TSelf>(this TSelf control, params string[] items) where TSelf : SegmentedBase
    {
        ArgumentNullException.ThrowIfNull(control);
        control.ItemsSource = ItemsView.Create(items ?? Array.Empty<string>());
        return control;
    }

    /// <summary>
    /// Sets <see cref="SegmentedBase.ItemsSource"/> to a view over the items, one segment each,
    /// with an optional text selector.
    /// </summary>
    /// <typeparam name="TSelf">Control type.</typeparam>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="control">Target segmented control.</param>
    /// <param name="items">Items collection.</param>
    /// <param name="textSelector">Optional text of an item; without one the item's <c>ToString()</c> is used.</param>
    /// <param name="keySelector">Optional key selector to stabilize selection when items change.</param>
    /// <returns>The control for chaining.</returns>
    public static TSelf Items<TSelf, T>(this TSelf control, IReadOnlyList<T> items, Func<T, string>? textSelector = null, Func<T, object?>? keySelector = null) where TSelf : SegmentedBase
    {
        ArgumentNullException.ThrowIfNull(control);
        control.ItemsSource = items == null ? ItemsView.EmptySelectable : ItemsView.Create(items, textSelector, keySelector);
        return control;
    }

    /// <summary>
    /// Sets the items source.
    /// </summary>
    /// <param name="control">Target segmented control.</param>
    /// <param name="itemsSource">Items source.</param>
    /// <returns>The control for chaining.</returns>
    public static TSelf ItemsSource<TSelf>(this TSelf control, ISelectableItemsView itemsSource) where TSelf : SegmentedBase
    {
        ArgumentNullException.ThrowIfNull(control);
        control.ItemsSource = itemsSource ?? ItemsView.EmptySelectable;
        return control;
    }

    /// <summary>
    /// Sets the segment template.
    /// </summary>
    /// <param name="control">Target segmented control.</param>
    /// <param name="template">Segment template.</param>
    /// <returns>The control for chaining.</returns>
    public static TSelf ItemTemplate<TSelf>(this TSelf control, IDataTemplate template) where TSelf : SegmentedBase
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(template);

        control.ItemTemplate = template;
        return control;
    }

    /// <summary>
    /// Sets the segment template using delegate-based templating.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="control">Target segmented control.</param>
    /// <param name="build">Template build callback.</param>
    /// <param name="bind">Template bind callback.</param>
    /// <param name="unbind">Optional template cleanup callback.</param>
    /// <returns>The control for chaining.</returns>
    public static SegmentedControl ItemTemplate<TItem>(
        this SegmentedControl control,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind,
        Action<FrameworkElement, TItem, int, TemplateContext>? unbind = null)
        => ItemTemplate(control, new DelegateTemplate<TItem>(build, bind, unbind));

    /// <summary>
    /// Configures each segment container after its content is bound. The callback receives the
    /// container, the item, and its index; use it to set or bind any container property (enabled
    /// state, tooltip, etc.). Applied on every rebuild.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="control">Target segmented control.</param>
    /// <param name="prepare">Container configuration callback.</param>
    /// <returns>The control for chaining.</returns>
    public static SegmentedControl PrepareContainer<T>(this SegmentedControl control, Action<SegmentButton, T, int> prepare)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(prepare);

        control.SetPrepareContainer((container, item, index) => prepare(container, (T)item!, index));
        return control;
    }

    /// <summary>
    /// Sets the segment template for a <see cref="ButtonGroup"/> using delegate-based templating.
    /// </summary>
    public static ButtonGroup ItemTemplate<TItem>(
        this ButtonGroup control,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind,
        Action<FrameworkElement, TItem, int, TemplateContext>? unbind = null)
    {
        control.ItemTemplate = new DelegateTemplate<TItem>(build, bind, unbind);
        return control;
    }

    /// <summary>
    /// Configures each <see cref="ButtonGroup"/> segment container after its content is bound. Use it
    /// to assign <see cref="CommandSourceControl.Command"/>, subscribe to <see cref="SegmentButton.Click"/>,
    /// or configure <see cref="SegmentButton.IsCheckable"/> / <see cref="SegmentButton.IsChecked"/>
    /// (independent toggle), enabled state, or tooltip.
    /// </summary>
    public static ButtonGroup PrepareContainer<T>(this ButtonGroup control, Action<SegmentButton, T, int> prepare)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(prepare);

        control.SetPrepareContainer((container, item, index) => prepare(container, (T)item!, index));
        return control;
    }

    /// <summary>Binds the semantic command invoked by a segment button.</summary>
    public static SegmentButton BindCommand(this SegmentButton button, ObservableValue<Command?> source)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(source);
        button.SetBinding(SegmentButton.CommandProperty, source, BindingMode.OneWay);
        return button;
    }

    /// <summary>
    /// Binds the selected index to an observable value.
    /// </summary>
    /// <param name="control">Target segmented control.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The control for chaining.</returns>
    public static SegmentedControl BindSelectedIndex(this SegmentedControl control, ObservableValue<int> source)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(source);

        control.SetBinding(SegmentedControl.SelectedIndexProperty, source);
        return control;
    }

    /// <summary>
    /// Binds the selected index to a converted observable value (e.g. an enum).
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="control">Target segmented control.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-index converter.</param>
    /// <param name="convertBack">Optional index-to-source converter.</param>
    /// <returns>The control for chaining.</returns>
    public static SegmentedControl BindSelectedIndex<TSource>(
        this SegmentedControl control,
        ObservableValue<TSource> source,
        Func<TSource, int> convert,
        Func<int, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        control.SetBinding(SegmentedControl.SelectedIndexProperty, source, convert, convertBack);
        return control;
    }

    #endregion

    #region NavigationList

    /// <summary>
    /// Sets <see cref="NavigationList.ItemsSource"/> and <see cref="NavigationList.KindSelector"/>
    /// from the items and the selectors, keeping the default text template; without a text selector
    /// an item shows its <c>ToString()</c>.
    /// </summary>
    /// <remarks>
    /// Generics are inferred once from <paramref name="items"/>; kind and key are selectors, not
    /// chained generic calls.
    /// </remarks>
    public static NavigationList Items<T>(
        this NavigationList control,
        IReadOnlyList<T> items,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, T, int, TemplateContext> bind,
        Func<T, NavigationItemKind>? kind = null,
        Func<T, object?>? keySelector = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.ItemsSource = ItemsView.Create(items, textSelector: null, keySelector);
        control.ItemTemplate = new DelegateTemplate<T>(build, bind);
        control.KindSelector = kind == null ? null : o => kind((T)o!);
        return control;
    }

    /// <summary>Binds items using the default text template; without a text selector an item shows its <c>ToString()</c>.</summary>
    public static NavigationList Items<T>(
        this NavigationList control,
        IReadOnlyList<T> items,
        Func<T, string>? textSelector = null,
        Func<T, NavigationItemKind>? kind = null,
        Func<T, object?>? keySelector = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.ItemsSource = ItemsView.Create(items, textSelector, keySelector);
        control.KindSelector = kind == null ? null : o => kind((T)o!);
        return control;
    }

    /// <summary>
    /// Binds the selected index to an observable value.
    /// </summary>
    /// <param name="control">Target navigation list.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The navigation list for chaining.</returns>
    public static NavigationList BindSelectedIndex(this NavigationList control, ObservableValue<int> source)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(source);
        control.SetBinding(NavigationList.SelectedIndexProperty, source);
        return control;
    }

    /// <summary>
    /// Configures the container of each row once its content is bound, so behavior can be attached
    /// to the whole row rather than to the template.
    /// </summary>
    /// <remarks>
    /// The callback runs on every bind, which includes scrolling one back into view, so keep it
    /// cheap. Register event subscriptions through the supplied context and they are removed before
    /// the next item is bound; properties assigned directly are reset by the framework instead.
    /// </remarks>
    /// <typeparam name="T">Item type.</typeparam>
    public static NavigationList PrepareContainer<T>(this NavigationList navigationList, PrepareContainerHandler<ItemContainer, T> prepare)
    {
        ArgumentNullException.ThrowIfNull(navigationList);
        ArgumentNullException.ThrowIfNull(prepare);

        navigationList.SetPrepareContainer((container, item, index, context) => prepare(container, (T)item!, index, context));
        return navigationList;
    }

    /// <summary>
    /// Releases what <see cref="PrepareContainer{T}(NavigationList, PrepareContainerHandler{ItemContainer, T})"/>
    /// attached outside the template context, before the container takes another item.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    public static NavigationList ClearContainer<T>(this NavigationList navigationList, PrepareContainerHandler<ItemContainer, T> clear)
    {
        ArgumentNullException.ThrowIfNull(navigationList);
        ArgumentNullException.ThrowIfNull(clear);

        navigationList.SetClearContainer((container, item, index, context) => clear(container, (T)item!, index, context));
        return navigationList;
    }

    #endregion

    #region NavigationView

    /// <summary>
    /// Sets the pane list's items, template and <see cref="NavigationView.ContentSelector"/> from
    /// the items and the selectors, with optional <see cref="PathGeometry"/> icons; without a text
    /// selector an item's label is its <c>ToString()</c>.
    /// </summary>
    public static NavigationView Items<T>(
        this NavigationView view,
        IReadOnlyList<T> items,
        Func<T, string>? textSelector = null,
        Func<T, PathGeometry?>? icon = null,
        Func<T, Element?>? content = null,
        Func<T, NavigationItemKind>? kind = null,
        Func<T, object?>? keySelector = null)
    {
        ArgumentNullException.ThrowIfNull(view);

        Func<T, Element?>? elementIcon = icon == null
            ? null
            : item => CreateNavigationPathIcon(icon(item));
        ApplyNavItems(view, view.Pane, items, textSelector, elementIcon, kind, keySelector);
        if (content != null)
        {
            view.ContentSelector = o => o is T t ? content(t) : null;
        }
        return view;
    }

    /// <summary>
    /// Sets the pane list's items, template and <see cref="NavigationView.ContentSelector"/> from
    /// the items and the selectors, with arbitrary <see cref="Element"/> icons; a returned instance
    /// cannot be shared by rows at the same time, and text is hidden when the pane is compact.
    /// </summary>
    public static NavigationView Items<T>(
        this NavigationView view,
        IReadOnlyList<T> items,
        Func<T, string>? textSelector,
        Func<T, Element?> icon,
        Func<T, Element?>? content = null,
        Func<T, NavigationItemKind>? kind = null,
        Func<T, object?>? keySelector = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(icon);

        ApplyNavItems(view, view.Pane, items, textSelector, icon, kind, keySelector);
        if (content != null)
        {
            view.ContentSelector = o => o is T t ? content(t) : null;
        }
        return view;
    }

    /// <summary>
    /// Sets the footer list's items, template and
    /// <see cref="NavigationView.FooterContentSelector"/> from the items and the selectors, with
    /// arbitrary <see cref="Element"/> icons; a returned instance cannot be shared by rows at the
    /// same time.
    /// </summary>
    public static NavigationView FooterItems<T>(
        this NavigationView view,
        IReadOnlyList<T> items,
        Func<T, string>? textSelector = null,
        Func<T, PathGeometry?>? icon = null,
        Func<T, Element?>? content = null,
        Func<T, NavigationItemKind>? kind = null,
        Func<T, object?>? keySelector = null)
    {
        ArgumentNullException.ThrowIfNull(view);

        Func<T, Element?>? elementIcon = icon == null
            ? null
            : item => CreateNavigationPathIcon(icon(item));
        ApplyNavItems(view, view.FooterPane, items, textSelector, elementIcon, kind, keySelector);
        if (content != null)
        {
            view.FooterContentSelector = o => o is T t ? content(t) : null;
        }
        return view;
    }

    /// <summary>
    /// Configures bottom-pinned footer items with arbitrary <see cref="Element"/> icons. A returned instance
    /// cannot be shared by rows at the same time.
    /// </summary>
    public static NavigationView FooterItems<T>(
        this NavigationView view,
        IReadOnlyList<T> items,
        Func<T, string>? textSelector,
        Func<T, Element?> icon,
        Func<T, Element?>? content = null,
        Func<T, NavigationItemKind>? kind = null,
        Func<T, object?>? keySelector = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(icon);

        ApplyNavItems(view, view.FooterPane, items, textSelector, icon, kind, keySelector);
        if (content != null)
        {
            view.FooterContentSelector = o => o is T t ? content(t) : null;
        }
        return view;
    }

    // Shared row template used by both the main and footer lists; both read the same pane mode from the view.
    private static void ApplyNavItems<T>(
        NavigationView view,
        NavigationList pane,
        IReadOnlyList<T> items,
        Func<T, string>? textSelector,
        Func<T, Element?>? icon,
        Func<T, NavigationItemKind>? kind,
        Func<T, object?>? keySelector)
    {
        textSelector ??= static item => item?.ToString() ?? string.Empty;
        pane.ItemsSource = ItemsView.Create(items, textSelector, keySelector);
        pane.KindSelector = kind == null ? null : o => kind((T)o!);
        pane.ItemTemplate = new DelegateTemplate<T>(
            build: _ =>
            {
                // 20 rather than the 16 a command icon is drawn at: in a rail the icon is the whole of the
                // item, since the text is hidden. It still fits the row, which is BaseControlHeight - 2.
                var iconHost = new ContentControl
                {
                    Width = 20,
                    Height = 20,
                    BorderThickness = 0,
                    IsHitTestVisible = false,
                    HorizontalAlignment = MewUI.HorizontalAlignment.Center,
                    VerticalAlignment = MewUI.VerticalAlignment.Center,
                };
                var label = new TextBlock().CenterVertical();
                return new StackPanel().Horizontal().Spacing(10).CenterVertical().Children(iconHost, label);
            },
            bind: (element, item, index, ctx) =>
            {
                var row = (StackPanel)element;
                var iconHost = (ContentControl)row.Children[0];
                var label = (TextBlock)row.Children[1];
                var itemKind = kind == null ? NavigationItemKind.Item : kind(item);
                bool isHeader = itemKind == NavigationItemKind.Header;
                bool rail = view.PaneIsRail;
                bool showText = view.PaneShowsText;

                iconHost.Content = itemKind == NavigationItemKind.Item ? icon?.Invoke(item) : null;
                iconHost.IsVisible = iconHost.Content != null;

                if (isHeader)
                {
                    // Group headers: small uppercase with space above, no icon. In the compact rail they collapse to a spacer.
                    label.IsVisible = !rail && showText;
                    label.Text = textSelector(item).ToUpperInvariant();
                    label.FontSize = 11;
                    label.FontWeight = MewUI.FontWeight.SemiBold;
                    row.Margin = new Thickness(0, 12, 0, 2);
                    row.HorizontalAlignment = MewUI.HorizontalAlignment.Left;
                }
                else
                {
                    // Items: larger text, indented under their group header; icon-only and centered in the rail.
                    label.IsVisible = showText;
                    label.Text = textSelector(item);
                    label.FontSize = 13;
                    label.FontWeight = MewUI.FontWeight.Normal;
                    row.Margin = showText && !rail ? new Thickness(12, 0, 0, 0) : default;
                    row.HorizontalAlignment = rail ? MewUI.HorizontalAlignment.Center : MewUI.HorizontalAlignment.Left;
                }

                // Compact rail hides the label; surface it as a tooltip on the host so icons stay identifiable.
                if (element.Parent is Border host)
                {
                    host.ToolTip(rail && !isHeader ? textSelector(item) : null);
                }
            },
            unbind: (element, _, _, _) =>
            {
                var row = (StackPanel)element;
                ((ContentControl)row.Children[0]).Content = null;
                if (element.Parent is Border host)
                {
                    host.ToolTip((string?)null);
                }
            });
    }

    private static Element? CreateNavigationPathIcon(PathGeometry? geometry)
    {
        if (geometry == null)
        {
            return null;
        }

        var icon = new PathShape { Data = geometry, Stretch = MewUI.Stretch.Uniform };
        icon.Bind(Shape.FillProperty, icon, TextElement.ForegroundProperty,
            (Color color) => (Brush)new SolidColorBrush(color));
        return icon;
    }

    #endregion

    #region ItemsControl

    /// <summary>
    /// Sets the items source.
    /// </summary>
    /// <param name="itemsControl">Target items control.</param>
    /// <param name="itemsSource">Items source.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl ItemsSource(this ItemsControl itemsControl, IItemsView itemsSource)
    {
        ArgumentNullException.ThrowIfNull(itemsControl);
        itemsControl.ItemsSource = itemsSource ?? ItemsView.Empty;
        return itemsControl;
    }

    /// <summary>
    /// Sets the items source from a legacy <see cref="MewUI.ItemsSource"/>; obsolete, use
    /// <see cref="ItemsSource(ItemsControl, IItemsView)"/> instead.
    /// </summary>
    /// <param name="itemsControl">Target items control.</param>
    /// <param name="itemsSource">Legacy items source.</param>
    /// <returns>The items control for chaining.</returns>
    [Obsolete("Use the ItemsView.Create overload instead. A legacy ItemsSource reports a snapshot "
        + "only, so items added or removed after this call are not reflected.")]
    public static ItemsControl ItemsSource(this ItemsControl itemsControl, ItemsSource itemsSource)
    {
        ArgumentNullException.ThrowIfNull(itemsControl);
        itemsControl.ItemsSource = ItemsView.From(itemsSource);
        return itemsControl;
    }

    /// <summary>
    /// Sets <see cref="ItemsControl.ItemsSource"/> to a view over the strings.
    /// </summary>
    /// <param name="itemsControl">Target items control.</param>
    /// <param name="items">Item strings.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl Items(this ItemsControl itemsControl, params string[] items)
    {
        ArgumentNullException.ThrowIfNull(itemsControl);
        itemsControl.ItemsSource = ItemsView.Create(items ?? Array.Empty<string>());
        return itemsControl;
    }

    /// <summary>
    /// Sets <see cref="ItemsControl.ItemsSource"/> to a view over the items, with an optional text
    /// selector.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="itemsControl">Target items control.</param>
    /// <param name="items">Items collection.</param>
    /// <param name="textSelector">Optional text of an item; without one the item's <c>ToString()</c> is used.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl Items<T>(this ItemsControl itemsControl, IReadOnlyList<T> items, Func<T, string>? textSelector = null)
    {
        ArgumentNullException.ThrowIfNull(itemsControl);
        itemsControl.ItemsSource = items == null ? ItemsView.Empty : ItemsView.Create(items, textSelector);
        return itemsControl;
    }

    /// <summary>
    /// Configures the container of each item once its content is bound. The container is the
    /// <see cref="ItemContainer"/> the items control places around every item.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="itemsControl">Target items control.</param>
    /// <param name="prepare">Container configuration callback.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl PrepareContainer<T>(this ItemsControl itemsControl, PrepareContainerHandler<ItemContainer, T> prepare)
    {
        ArgumentNullException.ThrowIfNull(itemsControl);
        ArgumentNullException.ThrowIfNull(prepare);

        itemsControl.SetPrepareContainer((container, item, index, context) => prepare(container, (T)item!, index, context));
        return itemsControl;
    }

    /// <summary>
    /// Releases what <see cref="PrepareContainer{T}(ItemsControl, PrepareContainerHandler{ItemContainer, T})"/>
    /// attached outside the template context, before the container takes another item.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="itemsControl">Target items control.</param>
    /// <param name="clear">Container release callback.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl ClearContainer<T>(this ItemsControl itemsControl, PrepareContainerHandler<ItemContainer, T> clear)
    {
        ArgumentNullException.ThrowIfNull(itemsControl);
        ArgumentNullException.ThrowIfNull(clear);

        itemsControl.SetClearContainer((container, item, index, context) => clear(container, (T)item!, index, context));
        return itemsControl;
    }

    /// <summary>
    /// Uses fixed-height row virtualization with theme default item height.
    /// </summary>
    /// <param name="itemsControl">Target items control.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl FixedHeightPresenter(this ItemsControl itemsControl)
    {
        itemsControl.EnsurePresenter<FixedHeightItemsPresenter>().Anchor = ItemsAnchor.Top;
        return itemsControl;
    }

    /// <summary>
    /// Uses fixed-height row virtualization anchored to the given viewport edge.
    /// </summary>
    /// <param name="itemsControl">Target items control.</param>
    /// <param name="anchor">Viewport edge the item content rests against and follows.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl FixedHeightPresenter(this ItemsControl itemsControl, ItemsAnchor anchor)
    {
        itemsControl.EnsurePresenter<FixedHeightItemsPresenter>().Anchor = anchor;
        return itemsControl;
    }

    /// <summary>
    /// Uses fixed-height row virtualization with explicit item height.
    /// </summary>
    /// <param name="itemsControl">Target items control.</param>
    /// <param name="itemHeight">Fixed item height.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl FixedHeightPresenter(this ItemsControl itemsControl, double itemHeight)
    {
        itemsControl.EnsurePresenter<FixedHeightItemsPresenter>().ItemHeight = itemHeight;
        return itemsControl;
    }

    /// <summary>
    /// Uses fixed-height row virtualization with explicit item height, anchored to the given viewport edge.
    /// </summary>
    /// <param name="itemsControl">Target items control.</param>
    /// <param name="itemHeight">Fixed item height.</param>
    /// <param name="anchor">Viewport edge the item content rests against and follows.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl FixedHeightPresenter(this ItemsControl itemsControl, double itemHeight, ItemsAnchor anchor)
    {
        var fixedPresenter = itemsControl.EnsurePresenter<FixedHeightItemsPresenter>();
        fixedPresenter.ItemHeight = itemHeight;
        fixedPresenter.Anchor = anchor;
        return itemsControl;
    }

    /// <summary>
    /// Uses variable-height virtualization (items are measured individually).
    /// </summary>
    /// <param name="itemsControl">Target items control.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl VariableHeightPresenter(this ItemsControl itemsControl)
    {
        itemsControl.EnsurePresenter<VariableHeightItemsPresenter>().Anchor = ItemsAnchor.Top;
        return itemsControl;
    }

    /// <summary>
    /// Uses variable-height virtualization anchored to the given viewport edge.
    /// </summary>
    /// <param name="itemsControl">Target items control.</param>
    /// <param name="anchor">Viewport edge the item content rests against and follows.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl VariableHeightPresenter(this ItemsControl itemsControl, ItemsAnchor anchor)
    {
        itemsControl.EnsurePresenter<VariableHeightItemsPresenter>().Anchor = anchor;
        return itemsControl;
    }

    /// <summary>
    /// Uses non-virtualizing stack layout (all items realized).
    /// </summary>
    /// <param name="itemsControl">Target items control.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl StackPresenter(this ItemsControl itemsControl)
    {
        itemsControl.EnsurePresenter<StackItemsPresenter>().Anchor = ItemsAnchor.Top;
        return itemsControl;
    }

    /// <summary>
    /// Uses non-virtualizing stack layout anchored to the given viewport edge.
    /// </summary>
    /// <param name="itemsControl">Target items control.</param>
    /// <param name="anchor">Viewport edge the item content rests against.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl StackPresenter(this ItemsControl itemsControl, ItemsAnchor anchor)
    {
        itemsControl.EnsurePresenter<StackItemsPresenter>().Anchor = anchor;
        return itemsControl;
    }

    /// <summary>
    /// Uses wrap-grid virtualization with fixed item size.
    /// </summary>
    /// <param name="itemsControl">Target items control.</param>
    /// <param name="itemWidth">Fixed item width.</param>
    /// <param name="itemHeight">Fixed item height.</param>
    /// <returns>The items control for chaining.</returns>
    public static ItemsControl WrapPresenter(this ItemsControl itemsControl, double itemWidth, double itemHeight)
    {
        var wrapPresenter = itemsControl.EnsurePresenter<WrapItemsPresenter>();
        wrapPresenter.ItemWidth = itemWidth;
        wrapPresenter.ItemHeight = itemHeight;
        return itemsControl;
    }

    #endregion

    #region GridView presenters

    /// <summary>
    /// Configures each row once its cells are bound, so behavior can be attached to the whole row
    /// instead of being repeated in every cell template. The row is the container the grid already
    /// realizes, so no extra element is created.
    /// </summary>
    /// <remarks>
    /// The callback runs on every bind, which includes scrolling a row back into view, so keep it
    /// cheap. Register event subscriptions through the supplied context and they are removed before
    /// the next item is bound; properties assigned directly are reset by the framework instead.
    /// </remarks>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="grid">Target grid view.</param>
    /// <param name="prepare">Row configuration callback.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView PrepareContainer<T>(this GridView grid, PrepareContainerHandler<GridViewRow, T> prepare)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(prepare);

        grid.SetPrepareRow((row, item, index, context) => prepare(row, (T)item!, index, context));
        return grid;
    }

    /// <summary>
    /// Releases what <see cref="PrepareContainer{T}(GridView, PrepareContainerHandler{GridViewRow, T})"/>
    /// attached outside the template context, before the row takes another item.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="grid">Target grid view.</param>
    /// <param name="clear">Row release callback.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView ClearContainer<T>(this GridView grid, PrepareContainerHandler<GridViewRow, T> clear)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(clear);

        grid.SetClearRow((row, item, index, context) => clear(row, (T)item!, index, context));
        return grid;
    }

    /// <summary>
    /// Configures the container placed around each row once its content is bound, so behavior can be
    /// attached to the whole row, indent and expander included, instead of to the template.
    /// </summary>
    /// <remarks>
    /// The callback runs on every bind, which includes scrolling a row back into view, so keep it
    /// cheap. Register event subscriptions through the supplied context and they are removed before
    /// the next item is bound; properties assigned directly are reset by the framework instead.
    /// </remarks>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="prepare">Container configuration callback.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView PrepareContainer<T>(this TreeView treeView, PrepareContainerHandler<ItemContainer, T> prepare)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        ArgumentNullException.ThrowIfNull(prepare);

        treeView.SetPrepareContainer((container, item, index, context) => prepare(container, (T)item!, index, context));
        return treeView;
    }

    /// <summary>
    /// Releases what <see cref="PrepareContainer{T}(TreeView, PrepareContainerHandler{ItemContainer, T})"/>
    /// attached outside the template context, before the container takes another item. Most hooks
    /// need no counterpart: context subscriptions and container properties are undone for you.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="clear">Container release callback.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView ClearContainer<T>(this TreeView treeView, PrepareContainerHandler<ItemContainer, T> clear)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        ArgumentNullException.ThrowIfNull(clear);

        treeView.SetClearContainer((container, item, index, context) => clear(container, (T)item!, index, context));
        return treeView;
    }

    /// <summary>
    /// Uses fixed-height row virtualization (default). Rows assume <see cref="GridView.RowHeight"/>
    /// or the theme default; cell content taller than that clips.
    /// </summary>
    /// <param name="grid">Target grid view.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView FixedHeightPresenter(this GridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.SetPresenter(new FixedHeightItemsPresenter
        {
            BorderThickness = 0,
            Padding = new Thickness(0),
            UseHorizontalExtentForLayout = true,
        });
        return grid;
    }

    /// <summary>
    /// Uses variable-height row virtualization. Each row's height is the maximum measured
    /// cell height plus <see cref="GridView.CellPadding"/> vertical thickness - suitable for
    /// rows with wrapping text or differently sized cell content.
    /// </summary>
    /// <param name="grid">Target grid view.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView VariableHeightPresenter(this GridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        grid.SetPresenter(new VariableHeightItemsPresenter
        {
            BorderThickness = 0,
            Padding = new Thickness(0),
            UseHorizontalExtentForLayout = true,
        });
        return grid;
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
            ? TreeItemsView.Empty
            : TreeItemsView.Create(items, n => n.Children, textSelector: n => n.Text, keySelector: n => n);
        return treeView;
    }

    /// <summary>
    /// Sets the items source directly from an <see cref="ITreeItemsView"/>.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="itemsView">The tree items view.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView ItemsSource(this TreeView treeView, ITreeItemsView itemsView)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        treeView.ItemsSource = itemsView ?? TreeItemsView.Empty;
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
    /// <param name="isExpandableSelector">
    /// Optional selector that determines whether an item displays an expand indicator independently
    /// of its currently loaded child count.
    /// </param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView Items<T>(
        this TreeView treeView,
        IReadOnlyList<T> roots,
        Func<T, IReadOnlyList<T>> childrenSelector,
        Func<T, string>? textSelector = null,
        Func<T, object?>? keySelector = null,
        Func<T, bool>? isExpandableSelector = null)
    {
        ArgumentNullException.ThrowIfNull(treeView);

        treeView.ItemsSource = roots == null
            ? TreeItemsView.Empty
            : TreeItemsView.Create(
                roots,
                childrenSelector,
                textSelector,
                keySelector,
                isExpandableSelector);

        return treeView;
    }

    /// <summary>
    /// Sets the node template.
    /// </summary>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="template">Node template.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView ItemTemplate(this TreeView treeView, IDataTemplate template)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        ArgumentNullException.ThrowIfNull(template);

        treeView.ItemTemplate = template;
        return treeView;
    }

    /// <summary>
    /// Sets the node template using delegate-based templating.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="treeView">Target tree view.</param>
    /// <param name="build">Template build callback.</param>
    /// <param name="bind">Template bind callback.</param>
    /// <param name="unbind">Optional template cleanup callback.</param>
    /// <returns>The tree view for chaining.</returns>
    public static TreeView ItemTemplate<TItem>(
        this TreeView treeView,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind,
        Action<FrameworkElement, TItem, int, TemplateContext>? unbind = null)
        => ItemTemplate(treeView, new DelegateTemplate<TItem>(build, bind, unbind));

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

    /// <summary>
    /// Adds a handler that runs immediately before a tree item is expanded.
    /// </summary>
    public static TreeView OnExpanding(
        this TreeView treeView,
        Action<TreeViewExpansionEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        ArgumentNullException.ThrowIfNull(handler);
        treeView.Expanding += handler;
        return treeView;
    }

    /// <summary>
    /// Adds a handler that runs immediately before a tree item is collapsed.
    /// </summary>
    public static TreeView OnCollapsing(
        this TreeView treeView,
        Action<TreeViewExpansionEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(treeView);
        ArgumentNullException.ThrowIfNull(handler);
        treeView.Collapsing += handler;
        return treeView;
    }

    #endregion

    #region ContextMenu

    /// <summary>
    /// Replaces the menu items.
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
    /// Adds an entry built beforehand, such as a checkable <see cref="MenuItem"/>, after the existing items.
    /// </summary>
    /// <param name="menu">Target context menu.</param>
    /// <param name="entry">Menu entry to add.</param>
    /// <returns>The context menu for chaining.</returns>
    public static ContextMenu Add(this ContextMenu menu, MenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(entry);
        menu.AddEntry(entry);
        return menu;
    }

    /// <summary>
    /// Adds a semantic command item.
    /// </summary>
    /// <param name="menu">Target context menu.</param>
    /// <param name="text">Item text.</param>
    /// <param name="command">Semantic command.</param>
    /// <returns>The context menu for chaining.</returns>
    public static ContextMenu Item(this ContextMenu menu, string text, Command command)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.AddItem(text, command);
        return menu;
    }

    /// <summary>
    /// Adds a command item that passes <paramref name="data"/> as the invocation argument, so several
    /// items can share one command and differ only in the value they pass.
    /// </summary>
    public static ContextMenu Item(this ContextMenu menu, string text, Command command, object? data)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.AddItem(text, command, data);
        return menu;
    }

    /// <summary>Adds a non-executable presentation item.</summary>
    public static ContextMenu Item(this ContextMenu menu, string text, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.AddItem(text, isEnabled);
        return menu;
    }

    /// <summary>
    /// Adds a semantic command item using command presentation metadata.
    /// </summary>
    /// <param name="menu">Target context menu.</param>
    /// <param name="command">Semantic command.</param>
    /// <returns>The context menu for chaining.</returns>
    public static ContextMenu Item(this ContextMenu menu, Command command)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.AddItem(command);
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

    #endregion

    #region ComboBox

    /// <summary>
    /// Sets the items source.
    /// </summary>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="itemsSource">Items source.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox ItemsSource(this ComboBox comboBox, ISelectableItemsView itemsSource)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.ItemsSource = itemsSource ?? ItemsView.EmptySelectable;
        return comboBox;
    }

    /// <summary>
    /// Sets the items source from a legacy <see cref="MewUI.ItemsSource"/>; obsolete, use
    /// <see cref="ItemsSource(ComboBox, ISelectableItemsView)"/> instead.
    /// </summary>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="itemsSource">Legacy items source.</param>
    /// <returns>The combo box for chaining.</returns>
    [Obsolete("Use the ItemsView.Create overload instead. A legacy ItemsSource reports a snapshot "
        + "only, so items added or removed after this call are not reflected.")]
    public static ComboBox ItemsSource(this ComboBox comboBox, ItemsSource itemsSource)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.ItemsSource = ItemsView.From(itemsSource);
        return comboBox;
    }

    /// <summary>
    /// Sets <see cref="ComboBox.ItemsSource"/> to a view over the strings.
    /// </summary>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="items">Item strings.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox Items(this ComboBox comboBox, params string[] items)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.ItemsSource = ItemsView.Create(items ?? Array.Empty<string>());
        return comboBox;
    }

    /// <summary>
    /// Sets <see cref="ComboBox.ItemsSource"/> to a view over the items, with an optional text
    /// selector.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="items">Items collection.</param>
    /// <param name="textSelector">Optional text of an item; without one the item's <c>ToString()</c> is used.</param>
    /// <param name="keySelector">Optional key selector to stabilize selection when items change.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox Items<T>(this ComboBox comboBox, IReadOnlyList<T> items, Func<T, string>? textSelector = null, Func<T, object?>? keySelector = null)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        comboBox.ItemsSource = items == null ? ItemsView.EmptySelectable : ItemsView.Create(items, textSelector, keySelector);
        return comboBox;
    }

    /// <summary>
    /// Sets the item template for the drop-down list.
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

    /// <summary>
    /// Sets the item template using delegate-based templating.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="build">Template build callback.</param>
    /// <param name="bind">Template bind callback.</param>
    /// <param name="unbind">Optional template cleanup callback.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox ItemTemplate<TItem>(
        this ComboBox comboBox,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind,
        Action<FrameworkElement, TItem, int, TemplateContext>? unbind = null)
        => ItemTemplate(comboBox, new DelegateTemplate<TItem>(build, bind, unbind));

    /// <summary>
    /// Sets the template that presents the selected item in the header. Without it the header
    /// falls back to the item template, then to the item text.
    /// </summary>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="template">Selected item template.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox SelectedItemTemplate(this ComboBox comboBox, IDataTemplate template)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(template);

        comboBox.SelectedItemTemplate = template;
        return comboBox;
    }

    /// <summary>
    /// Sets the selected item template using delegate-based templating.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="build">Template build callback.</param>
    /// <param name="bind">Template bind callback.</param>
    /// <param name="unbind">Optional template cleanup callback.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox SelectedItemTemplate<TItem>(
        this ComboBox comboBox,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind,
        Action<FrameworkElement, TItem, int, TemplateContext>? unbind = null)
        => SelectedItemTemplate(comboBox, new DelegateTemplate<TItem>(build, bind, unbind));

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

        comboBox.SetBinding(ComboBox.SelectedIndexProperty, source);
        return comboBox;
    }

    /// <summary>
    /// Binds the selected index to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="comboBox">Target combo box.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-index converter.</param>
    /// <param name="convertBack">Optional index-to-source converter.</param>
    /// <returns>The combo box for chaining.</returns>
    public static ComboBox BindSelectedIndex<TSource>(
        this ComboBox comboBox,
        ObservableValue<TSource> source,
        Func<TSource, int> convert,
        Func<int, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        comboBox.SetBinding(ComboBox.SelectedIndexProperty, source, convert, convertBack);
        return comboBox;
    }

    /// <summary>
    /// Configures the container of each drop-down item once its content is bound, so behavior can be attached
    /// to the whole drop-down item rather than to the template.
    /// </summary>
    /// <remarks>
    /// The callback runs on every bind, which includes scrolling one back into view, so keep it
    /// cheap. Register event subscriptions through the supplied context and they are removed before
    /// the next item is bound; properties assigned directly are reset by the framework instead.
    /// </remarks>
    /// <typeparam name="T">Item type.</typeparam>
    public static ComboBox PrepareContainer<T>(this ComboBox comboBox, PrepareContainerHandler<ItemContainer, T> prepare)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(prepare);

        comboBox.SetPrepareContainer((container, item, index, context) => prepare(container, (T)item!, index, context));
        return comboBox;
    }

    /// <summary>
    /// Releases what <see cref="PrepareContainer{T}(ComboBox, PrepareContainerHandler{ItemContainer, T})"/>
    /// attached outside the template context, before the container takes another item.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    public static ComboBox ClearContainer<T>(this ComboBox comboBox, PrepareContainerHandler<ItemContainer, T> clear)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(clear);

        comboBox.SetClearContainer((container, item, index, context) => clear(container, (T)item!, index, context));
        return comboBox;
    }

    #endregion

    #region TabItem

    /// <summary>
    /// Sets the header text.
    /// </summary>
    /// <param name="tab">Target tab item.</param>
    /// <param name="text">Header text.</param>
    /// <param name="accessKey">When true (default), "_" prefixes mark access key characters.</param>
    /// <returns>The tab item for chaining.</returns>
    public static TabItem Header(this TabItem tab, string text, bool accessKey = true)
    {
        ArgumentNullException.ThrowIfNull(tab);
        text ??= string.Empty;
        if (accessKey)
        {
            var at = new AccessText();
            at.RawText = text;
            tab.Header = at;
        }
        else
        {
            tab.Header = new TextBlock { Text = text };
        }
        tab.HeaderText = text;
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
    /// Sets the header element and semantic header text.
    /// </summary>
    /// <param name="tab">Target tab item.</param>
    /// <param name="header">Header element.</param>
    /// <param name="text">Semantic header text.</param>
    /// <returns>The tab item for chaining.</returns>
    public static TabItem Header(this TabItem tab, Element header, string text)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(header);
        tab.Header = header;
        tab.HeaderText = text ?? string.Empty;
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

    #endregion

    #region TabControl

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

    #region RangeBase

    /// <summary>
    /// Sets <see cref="RangeBase.Minimum"/> and <see cref="RangeBase.Maximum"/>.
    /// </summary>
    /// <typeparam name="T">RangeBase type.</typeparam>
    /// <param name="rangeBase">Target range-based control.</param>
    /// <param name="minimum">Minimum value.</param>
    /// <param name="maximum">Maximum value.</param>
    /// <returns>The control for chaining.</returns>
    public static T Range<T>(
        this T rangeBase,
        [FluentSetsMember(nameof(RangeBase.Minimum))] double minimum,
        [FluentSetsMember(nameof(RangeBase.Maximum))] double maximum)
        where T : RangeBase
    {
        rangeBase.Minimum = minimum;
        rangeBase.Maximum = maximum;
        return rangeBase;
    }

    #endregion

    #region ProgressBar

    /// <summary>
    /// Binds the indeterminate state to an observable value.
    /// </summary>
    /// <param name="progressBar">Target progress bar.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The progress bar for chaining.</returns>
    public static ProgressBar BindIsIndeterminate(this ProgressBar progressBar, ObservableValue<bool> source)
    {
        progressBar.SetBinding(ProgressBar.IsIndeterminateProperty, source);
        return progressBar;
    }

    /// <summary>
    /// Binds the indeterminate state to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="progressBar">Target progress bar.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-indeterminate-state converter.</param>
    /// <returns>The progress bar for chaining.</returns>
    public static ProgressBar BindIsIndeterminate<TSource>(
        this ProgressBar progressBar,
        ObservableValue<TSource> source,
        Func<TSource, bool> convert)
    {
        ArgumentNullException.ThrowIfNull(progressBar);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        progressBar.SetBinding(ProgressBar.IsIndeterminateProperty, source, convert, mode: BindingMode.OneWay);
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

        progressBar.SetBinding(RangeBase.ValueProperty, source, BindingMode.OneWay);
        return progressBar;
    }

    /// <summary>
    /// Binds the value to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="progressBar">Target progress bar.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-value converter.</param>
    /// <returns>The progress bar for chaining.</returns>
    public static ProgressBar BindValue<TSource>(
        this ProgressBar progressBar,
        ObservableValue<TSource> source,
        Func<TSource, double> convert)
    {
        ArgumentNullException.ThrowIfNull(progressBar);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        progressBar.SetBinding(RangeBase.ValueProperty, source, convert, mode: BindingMode.OneWay);
        return progressBar;
    }

    #endregion

    #region Slider

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

        slider.SetBinding(RangeBase.ValueProperty, source);
        return slider;
    }

    /// <summary>
    /// Binds the value to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="slider">Target slider.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-value converter.</param>
    /// <param name="convertBack">Optional value-to-source converter.</param>
    /// <returns>The slider for chaining.</returns>
    public static Slider BindValue<TSource>(
        this Slider slider,
        ObservableValue<TSource> source,
        Func<TSource, double> convert,
        Func<double, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(slider);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        slider.SetBinding(RangeBase.ValueProperty, source, convert, convertBack);
        return slider;
    }

    #endregion

    #region NumericUpDown

    /// <summary>
    /// Sets the step value; obsolete name of <see cref="RangeBaseMarkupExtensions.SmallChange{T}(T, double)"/>.
    /// </summary>
    /// <param name="numericUpDown">Target numeric up-down.</param>
    /// <param name="step">Step value.</param>
    /// <returns>The numeric up-down for chaining.</returns>
    [Obsolete("Use SmallChange. PageUp and PageDown step by LargeChange.")]
    public static NumericUpDown Step(this NumericUpDown numericUpDown, double step)
    {
        numericUpDown.SmallChange = step;
        return numericUpDown;
    }

    /// <summary>
    /// Binds the value to an observable value.
    /// </summary>
    /// <param name="numericUpDown">Target numeric up-down.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The numeric up-down for chaining.</returns>
    public static NumericUpDown BindValue(this NumericUpDown numericUpDown, ObservableValue<double> source)
    {
        ArgumentNullException.ThrowIfNull(numericUpDown);
        ArgumentNullException.ThrowIfNull(source);

        numericUpDown.SetBinding(RangeBase.ValueProperty, source);
        return numericUpDown;
    }

    /// <summary>
    /// Binds the value to an observable integer value.
    /// </summary>
    /// <param name="numericUpDown">Target numeric up-down.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The numeric up-down for chaining.</returns>
    public static NumericUpDown BindValue(this NumericUpDown numericUpDown, ObservableValue<int> source)
    {
        ArgumentNullException.ThrowIfNull(numericUpDown);
        ArgumentNullException.ThrowIfNull(source);

        numericUpDown.SetBinding(RangeBase.ValueProperty, source, v => (double)v, v => (int)Math.Round(v));
        return numericUpDown;
    }

    /// <summary>
    /// Binds the value to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="numericUpDown">Target numeric up-down.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-value converter.</param>
    /// <param name="convertBack">Optional value-to-source converter.</param>
    /// <returns>The numeric up-down for chaining.</returns>
    public static NumericUpDown BindValue<TSource>(
        this NumericUpDown numericUpDown,
        ObservableValue<TSource> source,
        Func<TSource, double> convert,
        Func<double, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(numericUpDown);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        numericUpDown.SetBinding(RangeBase.ValueProperty, source, convert, convertBack);
        return numericUpDown;
    }

    #endregion

    #region Window

    /// <summary>
    /// Defines and immediately runs the window's build at the composition site. The callback
    /// becomes the build owner (a virtual <see cref="Window.OnBuild"/> override is then not invoked) and is
    /// re-run by Hot Reload when edited.
    /// </summary>
    /// <typeparam name="TWindow">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="build">Build callback.</param>
    /// <returns>The window for chaining.</returns>
    public static TWindow Build<TWindow>(this TWindow window, Action<TWindow> build) where TWindow : Window
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(build);

        window.GuardBuildOwnership();
        window.SetBuildCallback(x => build((TWindow)x), build);

        build(window);

        return window;
    }

    /// <summary>
    /// Adds a client size changed event handler.
    /// </summary>
    /// <typeparam name="TWindow">Window type.</typeparam>
    /// <param name="window">Target window.</param>
    /// <param name="handler">Event handler.</param>
    /// <returns>The window for chaining.</returns>
    public static TWindow OnSizeChanged<TWindow>(this TWindow window, [FluentSetsMember(nameof(Window.ClientSizeChanged))] Action<Size> handler) where TWindow : Window
    {
        window.ClientSizeChanged += handler;
        return window;
    }

    #endregion

    #region ScrollViewer

    /// <summary>
    /// Sets <see cref="ScrollViewer.VerticalScroll"/> to <see cref="ScrollMode.Disabled"/>.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer NoVerticalScroll(this ScrollViewer scrollViewer) => scrollViewer.VerticalScroll(ScrollMode.Disabled);

    /// <summary>
    /// Sets <see cref="ScrollViewer.VerticalScroll"/> to <see cref="ScrollMode.Auto"/>.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer AutoVerticalScroll(this ScrollViewer scrollViewer) => scrollViewer.VerticalScroll(ScrollMode.Auto);

    /// <summary>
    /// Sets <see cref="ScrollViewer.VerticalScroll"/> to <see cref="ScrollMode.Visible"/>.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer ShowVerticalScroll(this ScrollViewer scrollViewer) => scrollViewer.VerticalScroll(ScrollMode.Visible);

    /// <summary>
    /// Sets <see cref="ScrollViewer.HorizontalScroll"/> to <see cref="ScrollMode.Disabled"/>.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer NoHorizontalScroll(this ScrollViewer scrollViewer) => scrollViewer.HorizontalScroll(ScrollMode.Disabled);

    /// <summary>
    /// Sets <see cref="ScrollViewer.HorizontalScroll"/> to <see cref="ScrollMode.Auto"/>.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer AutoHorizontalScroll(this ScrollViewer scrollViewer) => scrollViewer.HorizontalScroll(ScrollMode.Auto);

    /// <summary>
    /// Sets <see cref="ScrollViewer.HorizontalScroll"/> to <see cref="ScrollMode.Visible"/>.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer ShowHorizontalScroll(this ScrollViewer scrollViewer) => scrollViewer.HorizontalScroll(ScrollMode.Visible);

    /// <summary>
    /// Sets <see cref="ScrollViewer.VerticalScroll"/> and
    /// <see cref="ScrollViewer.HorizontalScroll"/>.
    /// </summary>
    /// <param name="scrollViewer">Target scroll viewer.</param>
    /// <param name="vertical">Vertical scroll mode.</param>
    /// <param name="horizontal">Horizontal scroll mode.</param>
    /// <returns>The scroll viewer for chaining.</returns>
    public static ScrollViewer Scroll(
        this ScrollViewer scrollViewer,
        [FluentSetsMember(nameof(ScrollViewer.VerticalScroll))] ScrollMode vertical,
        [FluentSetsMember(nameof(ScrollViewer.HorizontalScroll))] ScrollMode horizontal)
    {
        scrollViewer.VerticalScroll = vertical;
        scrollViewer.HorizontalScroll = horizontal;
        return scrollViewer;
    }

    #endregion

    #region Calendar

    /// <summary>
    /// Binds the selected date to an observable value.
    /// </summary>
    /// <param name="calendar">Target calendar.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The calendar for chaining.</returns>
    public static Calendar BindSelectedDate(this Calendar calendar, ObservableValue<DateTime?> source)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(source);

        calendar.SetBinding(Calendar.SelectedDateProperty, source);
        return calendar;
    }

    /// <summary>
    /// Binds the selected date to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="calendar">Target calendar.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-date converter.</param>
    /// <param name="convertBack">Optional date-to-source converter.</param>
    /// <returns>The calendar for chaining.</returns>
    public static Calendar BindSelectedDate<TSource>(
        this Calendar calendar,
        ObservableValue<TSource> source,
        Func<TSource, DateTime?> convert,
        Func<DateTime?, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        calendar.SetBinding(Calendar.SelectedDateProperty, source, convert, convertBack);
        return calendar;
    }

    #endregion

    #region DatePicker

    /// <summary>
    /// Binds the selected date to an observable value.
    /// </summary>
    /// <param name="datePicker">Target date picker.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The date picker for chaining.</returns>
    public static DatePicker BindSelectedDate(this DatePicker datePicker, ObservableValue<DateTime?> source)
    {
        ArgumentNullException.ThrowIfNull(datePicker);
        ArgumentNullException.ThrowIfNull(source);

        datePicker.SetBinding(DatePicker.SelectedDateProperty, source);
        return datePicker;
    }

    /// <summary>
    /// Binds the selected date to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="datePicker">Target date picker.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-date converter.</param>
    /// <param name="convertBack">Optional date-to-source converter.</param>
    /// <returns>The date picker for chaining.</returns>
    public static DatePicker BindSelectedDate<TSource>(
        this DatePicker datePicker,
        ObservableValue<TSource> source,
        Func<TSource, DateTime?> convert,
        Func<DateTime?, TSource>? convertBack = null)
    {
        ArgumentNullException.ThrowIfNull(datePicker);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        datePicker.SetBinding(DatePicker.SelectedDateProperty, source, convert, convertBack);
        return datePicker;
    }

    #endregion

    #region DropDownButton

    /// <summary>
    /// Sets the drop-down button content to a centered text label. When <paramref name="accessKey"/> is true
    /// (default), "_" prefixes mark access key characters (e.g., "_Export" registers Alt+E).
    /// </summary>
    /// <param name="button">Target drop-down button.</param>
    /// <param name="text">Content text.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The drop-down button for chaining.</returns>
    public static DropDownButton Content(this DropDownButton button, string text, bool accessKey = true)
    {
        if (accessKey)
        {
            var accessText = CreateCenteredAccessText();
            accessText.RawText = text;
            button.Content = accessText;
        }
        else
        {
            var textBlock = CreateCenteredTextBlock();
            textBlock.Text = text;
            button.Content = textBlock;
        }

        return button;
    }

    /// <summary>
    /// Binds the drop-down button content to an observable string value (creates a centered text label).
    /// </summary>
    /// <param name="button">Target drop-down button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The drop-down button for chaining.</returns>
    public static DropDownButton BindContent(this DropDownButton button, ObservableValue<string> source, bool accessKey = true)
    {
        if (accessKey)
        {
            var accessText = CreateCenteredAccessText();
            accessText.SetBinding(AccessText.RawTextProperty, source, BindingMode.OneWay);
            button.Content = accessText;
        }
        else
        {
            var textBlock = CreateCenteredTextBlock();
            textBlock.SetBinding(TextBlock.TextProperty, source, BindingMode.OneWay);
            button.Content = textBlock;
        }

        return button;
    }

    /// <summary>
    /// Binds the drop-down button content to a converted observable value (creates a centered text
    /// label).
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="button">Target drop-down button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-text converter.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The drop-down button for chaining.</returns>
    public static DropDownButton BindContent<TSource>(
        this DropDownButton button,
        ObservableValue<TSource> source,
        Func<TSource, string> convert,
        bool accessKey = true)
    {
        if (accessKey)
        {
            var accessText = CreateCenteredAccessText();
            accessText.SetBinding(AccessText.RawTextProperty, source, value => convert(value) ?? string.Empty);
            button.Content = accessText;
        }
        else
        {
            var textBlock = CreateCenteredTextBlock();
            textBlock.SetBinding(TextBlock.TextProperty, source, value => convert(value) ?? string.Empty);
            button.Content = textBlock;
        }

        return button;
    }

    private static AccessText CreateCenteredAccessText() => new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = MewUI.TextAlignment.Center,
        VerticalTextAlignment = MewUI.TextAlignment.Center,
    };

    private static TextBlock CreateCenteredTextBlock() => new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = MewUI.TextAlignment.Center,
        VerticalTextAlignment = MewUI.TextAlignment.Center,
    };

    #endregion

    #region SplitButton

    // The Button text-content extensions cannot be generic: their signature would match the ToggleBase
    // ones, and a type constraint is not part of a member's signature. These overloads restore the
    // SplitButton type for chaining, the same way CheckBox and ToggleButton do over ToggleBase.

    /// <summary>
    /// Sets the split button content to a centered text label. When <paramref name="accessKey"/> is true
    /// (default), "_" prefixes mark access key characters (e.g., "_Save" registers Alt+S).
    /// </summary>
    /// <param name="button">Target split button.</param>
    /// <param name="text">Content text.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The split button for chaining.</returns>
    public static SplitButton Content(this SplitButton button, string text, bool accessKey = true)
    {
        Content((Button)button, text, accessKey);
        return button;
    }

    /// <summary>
    /// Binds the split button content to an observable string value (creates a centered text
    /// label).
    /// </summary>
    /// <param name="button">Target split button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The split button for chaining.</returns>
    public static SplitButton BindContent(this SplitButton button, ObservableValue<string> source, bool accessKey = true)
    {
        BindContent((Button)button, source, accessKey);
        return button;
    }

    /// <summary>
    /// Binds the split button content to a converted observable value (creates a centered text
    /// label).
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="button">Target split button.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-text converter.</param>
    /// <param name="accessKey">Whether underscore prefixes define access keys.</param>
    /// <returns>The split button for chaining.</returns>
    public static SplitButton BindContent<TSource>(
        this SplitButton button,
        ObservableValue<TSource> source,
        Func<TSource, string> convert,
        bool accessKey = true)
    {
        BindContent((Button)button, source, convert, accessKey);
        return button;
    }

    #endregion

    #region ProgressIndicator

    /// <summary>
    /// Binds the active state to an observable value.
    /// </summary>
    /// <param name="progressIndicator">Target progress indicator.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The progress indicator for chaining.</returns>
    public static ProgressIndicator BindIsActive(this ProgressIndicator progressIndicator, ObservableValue<bool> source)
    {
        progressIndicator.SetBinding(ProgressIndicator.IsActiveProperty, source);
        return progressIndicator;
    }

    /// <summary>
    /// Binds the active state to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="progressIndicator">Target progress indicator.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-active-state converter.</param>
    /// <returns>The progress indicator for chaining.</returns>
    public static ProgressIndicator BindIsActive<TSource>(
        this ProgressIndicator progressIndicator,
        ObservableValue<TSource> source,
        Func<TSource, bool> convert)
    {
        ArgumentNullException.ThrowIfNull(progressIndicator);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        progressIndicator.SetBinding(ProgressIndicator.IsActiveProperty, source, convert, mode: BindingMode.OneWay);
        return progressIndicator;
    }

    #endregion

    #region ProgressRing

    /// <summary>
    /// Binds the indeterminate state to an observable value.
    /// </summary>
    /// <param name="progressRing">Target progress ring.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The progress ring for chaining.</returns>
    public static ProgressRing BindIsIndeterminate(this ProgressRing progressRing, ObservableValue<bool> source)
    {
        progressRing.SetBinding(ProgressRing.IsIndeterminateProperty, source);
        return progressRing;
    }

    /// <summary>
    /// Binds the indeterminate state to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="progressRing">Target progress ring.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-indeterminate-state converter.</param>
    /// <returns>The progress ring for chaining.</returns>
    public static ProgressRing BindIsIndeterminate<TSource>(
        this ProgressRing progressRing,
        ObservableValue<TSource> source,
        Func<TSource, bool> convert)
    {
        ArgumentNullException.ThrowIfNull(progressRing);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        progressRing.SetBinding(ProgressRing.IsIndeterminateProperty, source, convert, mode: BindingMode.OneWay);
        return progressRing;
    }

    /// <summary>
    /// Binds the value to an observable value.
    /// </summary>
    /// <param name="progressRing">Target progress ring.</param>
    /// <param name="source">Observable source.</param>
    /// <returns>The progress ring for chaining.</returns>
    public static ProgressRing BindValue(this ProgressRing progressRing, ObservableValue<double> source)
    {
        ArgumentNullException.ThrowIfNull(progressRing);
        ArgumentNullException.ThrowIfNull(source);

        progressRing.SetBinding(RangeBase.ValueProperty, source, BindingMode.OneWay);
        return progressRing;
    }

    /// <summary>
    /// Binds the value to a converted observable value.
    /// </summary>
    /// <typeparam name="TSource">Source value type.</typeparam>
    /// <param name="progressRing">Target progress ring.</param>
    /// <param name="source">Observable source.</param>
    /// <param name="convert">Source-to-value converter.</param>
    /// <returns>The progress ring for chaining.</returns>
    public static ProgressRing BindValue<TSource>(
        this ProgressRing progressRing,
        ObservableValue<TSource> source,
        Func<TSource, double> convert)
    {
        ArgumentNullException.ThrowIfNull(progressRing);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(convert);

        progressRing.SetBinding(RangeBase.ValueProperty, source, convert, mode: BindingMode.OneWay);
        return progressRing;
    }

    #endregion
}

#pragma warning restore CS0618
