using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Binding;
using Aprillz.MewUI.Core;
using Aprillz.MewUI.Elements;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Primitives;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Markup;

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
        control.FontWeight = Rendering.FontWeight.Bold;
        return control;
    }

    #endregion

    // Binding intentionally stays explicit (BindText/BindContent/...) instead of a generic Bind API.

    #region UIElement Events (Generic)

    #region UIElement Binding (Explicit)

    public static T BindIsVisible<T>(this T element, ObservableValue<bool> source) where T : UIElement
    {
        if (element == null) throw new ArgumentNullException(nameof(element));
        if (source == null) throw new ArgumentNullException(nameof(source));

        element.SetIsVisibleBinding(
            get: () => source.Value,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
        return element;
    }

    public static T BindIsEnabled<T>(this T element, ObservableValue<bool> source) where T : UIElement
    {
        if (element == null) throw new ArgumentNullException(nameof(element));
        if (source == null) throw new ArgumentNullException(nameof(source));

        element.SetIsEnabledBinding(
            get: () => source.Value,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
        return element;
    }

    #endregion

    public static T OnGotFocus<T>(this T element, Action handler) where T : UIElement
    {
        element.GotFocus = handler;
        return element;
    }

    public static T OnLostFocus<T>(this T element, Action handler) where T : UIElement
    {
        element.LostFocus = handler;
        return element;
    }

    public static T OnMouseEnter<T>(this T element, Action handler) where T : UIElement
    {
        element.MouseEnter = handler;
        return element;
    }

    public static T OnMouseLeave<T>(this T element, Action handler) where T : UIElement
    {
        element.MouseLeave = handler;
        return element;
    }

    public static T OnMouseDown<T>(this T element, Action<MouseEventArgs> handler) where T : UIElement
    {
        element.MouseDown = handler;
        return element;
    }

    public static T OnMouseUp<T>(this T element, Action<MouseEventArgs> handler) where T : UIElement
    {
        element.MouseUp = handler;
        return element;
    }

    public static T OnMouseMove<T>(this T element, Action<MouseEventArgs> handler) where T : UIElement
    {
        element.MouseMove = handler;
        return element;
    }

    public static T OnMouseWheel<T>(this T element, Action<MouseWheelEventArgs> handler) where T : UIElement
    {
        element.MouseWheel = handler;
        return element;
    }

    public static T OnKeyDown<T>(this T element, Action<KeyEventArgs> handler) where T : UIElement
    {
        element.KeyDown = handler;
        return element;
    }

    public static T OnKeyUp<T>(this T element, Action<KeyEventArgs> handler) where T : UIElement
    {
        element.KeyUp = handler;
        return element;
    }

    public static T OnTextInput<T>(this T element, Action<TextInputEventArgs> handler) where T : UIElement
    {
        element.TextInput = handler;
        return element;
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
        if (label == null) throw new ArgumentNullException(nameof(label));
        if (source == null) throw new ArgumentNullException(nameof(source));

        label.SetTextBinding(
            get: () => source.Value,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
        return label;
    }

    public static Label BindText<TSource>(this Label label, ObservableValue<TSource> source, Func<TSource, string> convert)
    {
        if (label == null) throw new ArgumentNullException(nameof(label));
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (convert == null) throw new ArgumentNullException(nameof(convert));

        label.SetTextBinding(
            get: () => convert(source.Value) ?? string.Empty,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
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
        button.Click = handler;
        return button;
    }

    public static Button BindContent(this Button button, ObservableValue<string> source)
    {
        if (button == null) throw new ArgumentNullException(nameof(button));
        if (source == null) throw new ArgumentNullException(nameof(source));

        button.SetContentBinding(
            get: () => source.Value,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
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
        textBox.TextChanged = handler;
        return textBox;
    }

    public static TextBox BindText(this TextBox textBox, ObservableValue<string> source)
    {
        if (textBox == null) throw new ArgumentNullException(nameof(textBox));
        if (source == null) throw new ArgumentNullException(nameof(source));

        textBox.SetTextBinding(
            get: () => source.Value,
            set: v => source.Value = v,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
        return textBox;
    }

    #endregion

    #region CheckBox

    public static CheckBox Text(this CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        return checkBox;
    }

    public static CheckBox IsChecked(this CheckBox checkBox, bool isChecked = true)
    {
        checkBox.IsChecked = isChecked;
        return checkBox;
    }

    public static CheckBox OnCheckedChanged(this CheckBox checkBox, Action<bool> handler)
    {
        checkBox.CheckedChanged = handler;
        return checkBox;
    }

    public static CheckBox BindIsChecked(this CheckBox checkBox, ObservableValue<bool> source)
    {
        if (checkBox == null) throw new ArgumentNullException(nameof(checkBox));
        if (source == null) throw new ArgumentNullException(nameof(source));

        checkBox.SetIsCheckedBinding(
            get: () => source.Value,
            set: v => source.Value = v,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
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
        radioButton.CheckedChanged = handler;
        return radioButton;
    }

    public static RadioButton BindIsChecked(this RadioButton radioButton, ObservableValue<bool> source)
    {
        if (radioButton == null) throw new ArgumentNullException(nameof(radioButton));
        if (source == null) throw new ArgumentNullException(nameof(source));

        radioButton.SetIsCheckedBinding(
            get: () => source.Value,
            set: v => source.Value = v,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
        return radioButton;
    }

    #endregion

    #region ListBox

    public static ListBox Items(this ListBox listBox, params string[] items)
    {
        listBox.ClearItems();
        foreach (var item in items)
            listBox.AddItem(item);
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
        listBox.SelectionChanged = handler;
        return listBox;
    }

    public static ListBox BindSelectedIndex(this ListBox listBox, ObservableValue<int> source)
    {
        if (listBox == null) throw new ArgumentNullException(nameof(listBox));
        if (source == null) throw new ArgumentNullException(nameof(source));

        listBox.SetSelectedIndexBinding(
            get: () => source.Value,
            set: v => source.Value = v,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
        return listBox;
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

    public static MultiLineTextBox OnTextChanged(this MultiLineTextBox textBox, Action<string> handler)
    {
        textBox.TextChanged = handler;
        return textBox;
    }

    public static MultiLineTextBox BindText(this MultiLineTextBox textBox, ObservableValue<string> source)
    {
        if (textBox == null) throw new ArgumentNullException(nameof(textBox));
        if (source == null) throw new ArgumentNullException(nameof(source));

        textBox.SetTextBinding(
            get: () => source.Value,
            set: v => source.Value = v,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
        return textBox;
    }

    #endregion

    #region ComboBox

    public static ComboBox Items(this ComboBox comboBox, params string[] items)
    {
        comboBox.ClearItems();
        foreach (var item in items)
            comboBox.AddItem(item);
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
        comboBox.SelectionChanged = handler;
        return comboBox;
    }

    public static ComboBox BindSelectedIndex(this ComboBox comboBox, ObservableValue<int> source)
    {
        if (comboBox == null) throw new ArgumentNullException(nameof(comboBox));
        if (source == null) throw new ArgumentNullException(nameof(source));

        comboBox.SetSelectedIndexBinding(
            get: () => source.Value,
            set: v => source.Value = v,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
        return comboBox;
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
        if (progressBar == null) throw new ArgumentNullException(nameof(progressBar));
        if (source == null) throw new ArgumentNullException(nameof(source));

        progressBar.SetValueBinding(
            get: () => source.Value,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
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
        slider.ValueChanged = handler;
        return slider;
    }

    public static Slider BindValue(this Slider slider, ObservableValue<double> source)
    {
        if (slider == null) throw new ArgumentNullException(nameof(slider));
        if (source == null) throw new ArgumentNullException(nameof(source));

        slider.SetValueBinding(
            get: () => source.Value,
            set: v => source.Value = v,
            subscribe: h => source.Changed += h,
            unsubscribe: h => source.Changed -= h);
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
        window.Loaded = handler;
        return window;
    }

    public static Window OnClosed(this Window window, Action handler)
    {
        window.Closed = handler;
        return window;
    }

    public static Window OnActivated(this Window window, Action handler)
    {
        window.Activated = handler;
        return window;
    }

    public static Window OnDeactivated(this Window window, Action handler)
    {
        window.Deactivated = handler;
        return window;
    }

    public static Window OnSizeChanged(this Window window, Action<Size> handler)
    {
        window.SizeChanged = handler;
        return window;
    }

    public static Window OnDpiChanged(this Window window, Action<uint, uint> handler)
    {
        window.DpiChanged = handler;
        return window;
    }

    public static Window OnThemeChanged(this Window window, Action<Theme, Theme> handler)
    {
        window.ThemeChanged = handler;
        return window;
    }

    public static Window OnFirstFrameRendered(this Window window, Action handler)
    {
        window.FirstFrameRendered = handler;
        return window;
    }

    public static Window OnPreviewKeyDown(this Window window, Action<KeyEventArgs> handler)
    {
        window.PreviewKeyDown = handler;
        return window;
    }

    public static Window OnPreviewKeyUp(this Window window, Action<KeyEventArgs> handler)
    {
        window.PreviewKeyUp = handler;
        return window;
    }

    public static Window OnPreviewTextInput(this Window window, Action<TextInputEventArgs> handler)
    {
        window.PreviewTextInput = handler;
        return window;
    }

    #endregion

    #region ContentControl

    public static T Content<T>(this T control, Elements.Element content) where T : ContentControl
    {
        control.Content = content;
        return control;
    }

    #endregion
}
