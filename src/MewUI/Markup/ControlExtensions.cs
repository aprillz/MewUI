using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Fluent API extension methods for controls.
/// </summary>
public static class ControlExtensions
{
    #region Control Base

    public static T Background<T>(this T control, Color color) where T : Control
    {
        control.Background = color;
        return control;
    }

    public static T Foreground<T>(this T control, Color color) where T : Control
    {
        control.Foreground = color;
        return control;
    }

    public static T BorderBrush<T>(this T control, Color color) where T : Control
    {
        control.BorderBrush = color;
        return control;
    }

    public static T BorderThickness<T>(this T control, double thickness) where T : Control
    {
        control.BorderThickness = thickness;
        return control;
    }

    public static T FontFamily<T>(this T control, string fontFamily) where T : Control
    {
        control.FontFamily = fontFamily;
        return control;
    }

    public static T FontSize<T>(this T control, double fontSize) where T : Control
    {
        control.FontSize = fontSize;
        return control;
    }

    public static T FontWeight<T>(this T control, FontWeight fontWeight) where T : Control
    {
        control.FontWeight = fontWeight;
        return control;
    }

    public static T Bold<T>(this T control) where T : Control
    {
        control.FontWeight = MewUI.FontWeight.Bold;
        return control;
    }

    public static T ToolTip<T>(this T control, string? text) where T : Control
    {
        control.ToolTipText = text;
        return control;
    }

    public static T ContextMenu<T>(this T control, ContextMenu? menu) where T : Control
    {
        control.ContextMenu = menu;
        return control;
    }

    #endregion

    #region UIElement Events (Generic)

    #region UIElement Properties

    public static T IsVisible<T>(this T element, bool isVisible = true) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        element.IsVisible = isVisible;
        return element;
    }

    public static T IsEnabled<T>(this T element, bool isEnabled = true) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        element.IsEnabled = isEnabled;
        return element;
    }

    public static T Enable<T>(this T element) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        element.IsEnabled = true;
        return element;
    }

    public static T Disable<T>(this T element) where T : UIElement
    {
        ArgumentNullException.ThrowIfNull(element);
        element.IsEnabled = false;
        return element;
    }

    public static T WithTheme<T>(this T element, Action<Theme, T> apply, bool invokeImmediately = true) where T : FrameworkElement
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(apply);

        element.RegisterThemeCallback((theme, e) => apply(theme, element), invokeImmediately);
        return element;
    }

    #endregion

    #region UIElement Binding (Explicit)

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

    public static T OnGotFocus<T>(this T element, Action handler) where T : UIElement
    {
        element.GotFocus += handler;
        return element;
    }

    public static T OnLostFocus<T>(this T element, Action handler) where T : UIElement
    {
        element.LostFocus += handler;
        return element;
    }

    public static T OnMouseEnter<T>(this T element, Action handler) where T : UIElement
    {
        element.MouseEnter += handler;
        return element;
    }

    public static T OnMouseLeave<T>(this T element, Action handler) where T : UIElement
    {
        element.MouseLeave += handler;
        return element;
    }

    public static T OnMouseDown<T>(this T element, Action<MouseEventArgs> handler) where T : UIElement
    {
        element.MouseDown += handler;
        return element;
    }

    public static T OnMouseUp<T>(this T element, Action<MouseEventArgs> handler) where T : UIElement
    {
        element.MouseUp += handler;
        return element;
    }

    public static T OnMouseMove<T>(this T element, Action<MouseEventArgs> handler) where T : UIElement
    {
        element.MouseMove += handler;
        return element;
    }

    public static T OnMouseWheel<T>(this T element, Action<MouseWheelEventArgs> handler) where T : UIElement
    {
        element.MouseWheel += handler;
        return element;
    }

    public static T OnKeyDown<T>(this T element, Action<KeyEventArgs> handler) where T : UIElement
    {
        element.KeyDown += handler;
        return element;
    }

    public static T OnKeyUp<T>(this T element, Action<KeyEventArgs> handler) where T : UIElement
    {
        element.KeyUp += handler;
        return element;
    }

    public static T OnTextInput<T>(this T element, Action<TextInputEventArgs> handler) where T : UIElement
    {
        element.TextInput += handler;
        return element;
    }

    #endregion

    #region Border

    public static Border CornerRadius(this Border border, double radius)
    {
        ArgumentNullException.ThrowIfNull(border);
        border.CornerRadius = radius;
        return border;
    }

    public static Border Child(this Border border, UIElement? child)
    {
        ArgumentNullException.ThrowIfNull(border);
        border.Child = child;
        return border;
    }

    #endregion

    #region HeaderedContentControl

    public static T Header<T>(this T control, string header) where T : HeaderedContentControl
    {
        ArgumentNullException.ThrowIfNull(control);
        control.Header = new Label()
            .Text(header ?? string.Empty)
            .Bold();
        return control;
    }

    public static T Header<T>(this T control, Element header) where T : HeaderedContentControl
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(header);
        control.Header = header;
        return control;
    }

    public static T HeaderSpacing<T>(this T control, double spacing) where T : HeaderedContentControl
    {
        ArgumentNullException.ThrowIfNull(control);
        control.HeaderSpacing = spacing;
        return control;
    }

    #endregion

    #region Label

    public static Label Text(this Label label, string text)
    {
        label.Text = text;
        return label;
    }

    public static Label TextAlignment(this Label label, TextAlignment alignment)
    {
        label.TextAlignment = alignment;
        return label;
    }

    public static Label VerticalTextAlignment(this Label label, TextAlignment alignment)
    {
        label.VerticalTextAlignment = alignment;
        return label;
    }

    public static Label TextWrapping(this Label label, TextWrapping wrapping)
    {
        label.TextWrapping = wrapping;
        return label;
    }

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

    public static Button Content(this Button button, string content)
    {
        button.Content = content;
        return button;
    }

    public static Button OnClick(this Button button, Action handler)
    {
        button.Click += handler;
        return button;
    }

    public static Button OnCanClick(this Button button, Func<bool> canClick)
    {
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(canClick);

        button.CanClick = canClick;
        return button;
    }

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

    public static TextBox Text(this TextBox textBox, string text)
    {
        textBox.Text = text;
        return textBox;
    }

    public static TextBox Placeholder(this TextBox textBox, string placeholder)
    {
        textBox.Placeholder = placeholder;
        return textBox;
    }

    public static TextBox IsReadOnly(this TextBox textBox, bool isReadOnly = true)
    {
        textBox.IsReadOnly = isReadOnly;
        return textBox;
    }

    public static TextBox AcceptTab(this TextBox textBox, bool acceptTab = true)
    {
        textBox.AcceptTab = acceptTab;
        return textBox;
    }

    public static TextBox OnTextChanged(this TextBox textBox, Action<string> handler)
    {
        textBox.TextChanged += handler;
        return textBox;
    }

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

    public static CheckBox Text(this CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        return checkBox;
    }

    public static CheckBox IsChecked(this CheckBox checkBox, bool? isChecked = true)
    {
        checkBox.IsChecked = isChecked;
        return checkBox;
    }

    public static CheckBox Check(this CheckBox checkBox)
    {
        checkBox.IsChecked = true;
        return checkBox;
    }

    public static CheckBox Uncheck(this CheckBox checkBox)
    {
        checkBox.IsChecked = false;
        return checkBox;
    }

    public static CheckBox Indeterminate(this CheckBox checkBox, bool isIndeterminate = true)
    {
        checkBox.IsChecked = null;
        return checkBox;
    }

    public static CheckBox OnCheckedChanged(this CheckBox checkBox, Action<bool> handler)
    {
        checkBox.CheckedChanged += v => handler(v ?? false);
        return checkBox;
    }

    public static CheckBox ThreeState(this CheckBox checkBox)
    {
        checkBox.IsThreeState = true;
        return checkBox;
    }

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

    public static CheckBox OnCheckStateChanged(this CheckBox checkBox, Action<bool?> handler)
    {
        checkBox.CheckedChanged += handler;
        return checkBox;
    }

    public static CheckBox IsThreeState(this CheckBox checkBox, bool isThreeState = true)
    {
        checkBox.IsThreeState = isThreeState;
        return checkBox;
    }

    #endregion

    #region RadioButton

    public static RadioButton Text(this RadioButton radioButton, string text)
    {
        radioButton.Text = text;
        return radioButton;
    }

    public static RadioButton GroupName(this RadioButton radioButton, string? groupName)
    {
        radioButton.GroupName = groupName;
        return radioButton;
    }

    public static RadioButton IsChecked(this RadioButton radioButton, bool isChecked = true)
    {
        radioButton.IsChecked = isChecked;
        return radioButton;
    }

    public static RadioButton OnCheckedChanged(this RadioButton radioButton, Action<bool> handler)
    {
        radioButton.CheckedChanged += handler;
        return radioButton;
    }

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

    public static ToggleSwitch Text(this ToggleSwitch toggleSwitch, string text)
    {
        toggleSwitch.Text = text;
        return toggleSwitch;
    }

    public static ToggleSwitch IsChecked(this ToggleSwitch toggleSwitch, bool isChecked = true)
    {
        toggleSwitch.IsChecked = isChecked;
        return toggleSwitch;
    }

    public static ToggleSwitch OnCheckedChanged(this ToggleSwitch toggleSwitch, Action<bool> handler)
    {
        toggleSwitch.CheckedChanged += handler;
        return toggleSwitch;
    }

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

    public static ListBox Items(this ListBox listBox, params string[] items)
    {
        listBox.ClearItems();
        foreach (var item in items)
        {
            listBox.AddItem(item);
        }

        return listBox;
    }

    public static ListBox ItemHeight(this ListBox listBox, double itemHeight)
    {
        listBox.ItemHeight = itemHeight;
        return listBox;
    }

    public static ListBox ItemPadding(this ListBox listBox, Thickness itemPadding)
    {
        listBox.ItemPadding = itemPadding;
        return listBox;
    }

    public static ListBox SelectedIndex(this ListBox listBox, int selectedIndex)
    {
        listBox.SelectedIndex = selectedIndex;
        return listBox;
    }

    public static ListBox OnSelectionChanged(this ListBox listBox, Action<int> handler)
    {
        listBox.SelectionChanged += handler;
        return listBox;
    }

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

    #region ContextMenu

    public static ContextMenu Items(this ContextMenu menu, params MenuEntry[] items)
    {
        ArgumentNullException.ThrowIfNull(menu);

        menu.SetItems(items);

        return menu;
    }

    public static ContextMenu Item(this ContextMenu menu, string text, Action? onClick = null, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.AddItem(text, onClick, isEnabled);
        return menu;
    }

    public static ContextMenu Item(this ContextMenu menu, string text, string shortcutText, Action? onClick = null, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.AddItem(text, onClick, isEnabled, shortcutText);
        return menu;
    }

    public static ContextMenu SubMenu(this ContextMenu menu, string text, ContextMenu subMenu, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(subMenu);

        menu.AddSubMenu(text, subMenu.Menu, isEnabled);
        return menu;
    }

    public static ContextMenu SubMenu(this ContextMenu menu, string text, string shortcutText, ContextMenu subMenu, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(subMenu);

        menu.AddSubMenu(text, subMenu.Menu, isEnabled, shortcutText);
        return menu;
    }

    public static ContextMenu Separator(this ContextMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.AddSeparator();
        return menu;
    }

    public static ContextMenu ItemHeight(this ContextMenu menu, double itemHeight)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.ItemHeight = itemHeight;
        return menu;
    }

    public static ContextMenu ItemPadding(this ContextMenu menu, Thickness itemPadding)
    {
        ArgumentNullException.ThrowIfNull(menu);
        menu.ItemPadding = itemPadding;
        return menu;
    }

    public static ContextMenu MaxMenuHeight(this ContextMenu menu, double height)
    {
        menu.MaxMenuHeight = height;
        return menu;
    }

    #endregion

    #region MultiLineTextBox

    public static MultiLineTextBox Text(this MultiLineTextBox textBox, string text)
    {
        textBox.Text = text;
        return textBox;
    }

    public static MultiLineTextBox Placeholder(this MultiLineTextBox textBox, string placeholder)
    {
        textBox.Placeholder = placeholder;
        return textBox;
    }

    public static MultiLineTextBox IsReadOnly(this MultiLineTextBox textBox, bool isReadOnly = true)
    {
        textBox.IsReadOnly = isReadOnly;
        return textBox;
    }

    public static MultiLineTextBox AcceptTab(this MultiLineTextBox textBox, bool acceptTab = true)
    {
        textBox.AcceptTab = acceptTab;
        return textBox;
    }

    public static MultiLineTextBox Wrap(this MultiLineTextBox textBox, bool wrap = true)
    {
        textBox.Wrap = wrap;
        return textBox;
    }

    public static MultiLineTextBox OnWrapChanged(this MultiLineTextBox textBox, Action<bool> handler)
    {
        textBox.WrapChanged += handler;
        return textBox;
    }

    public static MultiLineTextBox OnTextChanged(this MultiLineTextBox textBox, Action<string> handler)
    {
        textBox.TextChanged += handler;
        return textBox;
    }

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

    public static ComboBox Items(this ComboBox comboBox, params string[] items)
    {
        comboBox.ClearItems();
        foreach (var item in items)
        {
            comboBox.AddItem(item);
        }

        return comboBox;
    }

    public static ComboBox SelectedIndex(this ComboBox comboBox, int selectedIndex)
    {
        comboBox.SelectedIndex = selectedIndex;
        return comboBox;
    }

    public static ComboBox Placeholder(this ComboBox comboBox, string placeholder)
    {
        comboBox.Placeholder = placeholder;
        return comboBox;
    }

    public static ComboBox OnSelectionChanged(this ComboBox comboBox, Action<int> handler)
    {
        comboBox.SelectionChanged += handler;
        return comboBox;
    }

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

    public static TabItem Header(this TabItem tab, string header)
    {
        ArgumentNullException.ThrowIfNull(tab);
        tab.Header = new Label().Text(header ?? string.Empty);
        return tab;
    }

    public static TabItem Header(this TabItem tab, Element header)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(header);
        tab.Header = header;
        return tab;
    }

    public static TabItem Content(this TabItem tab, Element content)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(content);
        tab.Content = content;
        return tab;
    }

    public static TabItem IsEnabled(this TabItem tab, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(tab);
        tab.IsEnabled = isEnabled;
        return tab;
    }

    #endregion

    #region TabControl

    public static TabControl Padding(this TabControl tabControl, Thickness padding)
    {
        tabControl.Padding = padding;
        return tabControl;
    }

    public static TabControl Padding(this TabControl tabControl, double uniform)
    {
        tabControl.Padding = new Thickness(uniform);
        return tabControl;
    }

    public static TabControl Padding(this TabControl tabControl, double horizontal, double vertical)
    {
        tabControl.Padding = new Thickness(horizontal, vertical, horizontal, vertical);
        return tabControl;
    }

    public static TabControl Padding(this TabControl tabControl, double left, double top, double right, double bottom)
    {
        tabControl.Padding = new Thickness(left, top, right, bottom);
        return tabControl;
    }

    public static TabControl TabItems(this TabControl tabControl, params TabItem[] tabs)
    {
        ArgumentNullException.ThrowIfNull(tabControl);
        ArgumentNullException.ThrowIfNull(tabs);

        tabControl.ClearTabs();
        tabControl.AddTabs(tabs);
        return tabControl;
    }

    public static TabControl SelectedIndex(this TabControl tabControl, int selectedIndex)
    {
        tabControl.SelectedIndex = selectedIndex;
        return tabControl;
    }

    public static TabControl OnSelectionChanged(this TabControl tabControl, Action<int> handler)
    {
        tabControl.SelectionChanged += handler;
        return tabControl;
    }

    public static TabControl Tab(this TabControl tabControl, string header, Element content)
    {
        ArgumentNullException.ThrowIfNull(tabControl);
        tabControl.AddTab(new TabItem().Header(header).Content(content));
        return tabControl;
    }

    public static TabControl Tab(this TabControl tabControl, Element header, Element content)
    {
        ArgumentNullException.ThrowIfNull(tabControl);
        tabControl.AddTab(new TabItem().Header(header).Content(content));
        return tabControl;
    }

    #endregion

    #region ProgressBar

    public static ProgressBar Minimum(this ProgressBar progressBar, double minimum)
    {
        progressBar.Minimum = minimum;
        return progressBar;
    }

    public static ProgressBar Maximum(this ProgressBar progressBar, double maximum)
    {
        progressBar.Maximum = maximum;
        return progressBar;
    }

    public static ProgressBar Value(this ProgressBar progressBar, double value)
    {
        progressBar.Value = value;
        return progressBar;
    }

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

    public static Slider Minimum(this Slider slider, double minimum)
    {
        slider.Minimum = minimum;
        return slider;
    }

    public static Slider Maximum(this Slider slider, double maximum)
    {
        slider.Maximum = maximum;
        return slider;
    }

    public static Slider Value(this Slider slider, double value)
    {
        slider.Value = value;
        return slider;
    }

    public static Slider SmallChange(this Slider slider, double smallChange)
    {
        slider.SmallChange = smallChange;
        return slider;
    }

    public static Slider OnValueChanged(this Slider slider, Action<double> handler)
    {
        slider.ValueChanged += handler;
        return slider;
    }

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

    public static Window Title(this Window window, string title)
    {
        window.Title = title;
        return window;
    }

    public static Window OnLoaded(this Window window, Action handler)
    {
        window.Loaded += handler;
        return window;
    }

    public static Window OnClosed(this Window window, Action handler)
    {
        window.Closed += handler;
        return window;
    }

    public static Window OnActivated(this Window window, Action handler)
    {
        window.Activated += handler;
        return window;
    }

    public static Window OnDeactivated(this Window window, Action handler)
    {
        window.Deactivated += handler;
        return window;
    }

    public static Window OnSizeChanged(this Window window, Action<Size> handler)
    {
        window.SizeChanged += handler;
        return window;
    }

    public static Window OnDpiChanged(this Window window, Action<uint, uint> handler)
    {
        window.DpiChanged += handler;
        return window;
    }

    public static Window OnThemeChanged(this Window window, Action<Theme, Theme> handler)
    {
        window.ThemeChanged += handler;
        return window;
    }

    public static Window OnFirstFrameRendered(this Window window, Action handler)
    {
        window.FirstFrameRendered += handler;
        return window;
    }

    public static Window OnPreviewKeyDown(this Window window, Action<KeyEventArgs> handler)
    {
        window.PreviewKeyDown += handler;
        return window;
    }

    public static Window OnPreviewKeyUp(this Window window, Action<KeyEventArgs> handler)
    {
        window.PreviewKeyUp += handler;
        return window;
    }

    public static Window OnPreviewTextInput(this Window window, Action<TextInputEventArgs> handler)
    {
        window.PreviewTextInput += handler;
        return window;
    }

    #endregion

    #region ScrollViewer

    public static ScrollViewer VerticalScroll(this ScrollViewer scrollViewer, ScrollMode mode)
    {
        scrollViewer.VerticalScroll = mode;
        return scrollViewer;
    }

    public static ScrollViewer HorizontalScroll(this ScrollViewer scrollViewer, ScrollMode mode)
    {
        scrollViewer.HorizontalScroll = mode;
        return scrollViewer;
    }

    public static ScrollViewer NoVerticalScroll(this ScrollViewer scrollViewer) => scrollViewer.VerticalScroll(ScrollMode.Disabled);

    public static ScrollViewer AutoVerticalScroll(this ScrollViewer scrollViewer) => scrollViewer.VerticalScroll(ScrollMode.Auto);

    public static ScrollViewer ShowVerticalScroll(this ScrollViewer scrollViewer) => scrollViewer.VerticalScroll(ScrollMode.Visible);

    public static ScrollViewer NoHorizontalScroll(this ScrollViewer scrollViewer) => scrollViewer.HorizontalScroll(ScrollMode.Disabled);

    public static ScrollViewer AutoHorizontalScroll(this ScrollViewer scrollViewer) => scrollViewer.HorizontalScroll(ScrollMode.Auto);

    public static ScrollViewer ShowHorizontalScroll(this ScrollViewer scrollViewer) => scrollViewer.HorizontalScroll(ScrollMode.Visible);

    public static ScrollViewer Scroll(this ScrollViewer scrollViewer, ScrollMode vertical, ScrollMode horizontal)
    {
        scrollViewer.VerticalScroll = vertical;
        scrollViewer.HorizontalScroll = horizontal;
        return scrollViewer;
    }

    #endregion

    #region ContentControl

    public static T Content<T>(this T control, Element content) where T : ContentControl
    {
        control.Content = content;
        return control;
    }

    #endregion

    #region TabControl

    public static TabControl VerticalScroll(this TabControl tabControl, ScrollMode mode)
    {
        tabControl.VerticalScroll = mode;
        return tabControl;
    }

    public static TabControl HorizontalScroll(this TabControl tabControl, ScrollMode mode)
    {
        tabControl.HorizontalScroll = mode;
        return tabControl;
    }

    public static TabControl NoVerticalScroll(this TabControl tabControl) => tabControl.VerticalScroll(ScrollMode.Disabled);

    public static TabControl AutoVerticalScroll(this TabControl tabControl) => tabControl.VerticalScroll(ScrollMode.Auto);

    public static TabControl ShowVerticalScroll(this TabControl tabControl) => tabControl.VerticalScroll(ScrollMode.Visible);

    public static TabControl NoHorizontalScroll(this TabControl tabControl) => tabControl.HorizontalScroll(ScrollMode.Disabled);

    public static TabControl AutoHorizontalScroll(this TabControl tabControl) => tabControl.HorizontalScroll(ScrollMode.Auto);

    public static TabControl ShowHorizontalScroll(this TabControl tabControl) => tabControl.HorizontalScroll(ScrollMode.Visible);

    #endregion
}
