using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

namespace MewUI.Test.Markup;

[TestClass]
public sealed class FluentExtensionCoverageTests
{
    [TestMethod]
    public void CommonPropertyExtensions_SetValues()
    {
        var tag = new object();
        var element = new Border()
            .IsHitTestVisible(false)
            .SkipViewportCull()
            .AllowDrop()
            .CanDrag()
            .Tag(tag);

        Assert.IsFalse(element.IsHitTestVisible);
        Assert.IsTrue(element.SkipViewportCull);
        Assert.IsTrue(element.AllowDrop);
        Assert.IsTrue(element.CanDrag);
        Assert.AreSame(tag, element.Tag);
    }

    [TestMethod]
    public void ControlPropertyExtensions_SetValues()
    {
        var ring = new ProgressRing().IsActive(false);
        var password = new PasswordBox().PasswordChar('*');
        var slider = new Slider()
            .ThumbBrush(Color.Red)
            .ThumbBorderBrush(Color.Black);

        Assert.IsFalse(ring.IsActive);
        Assert.AreEqual('*', password.PasswordChar);
        Assert.AreEqual(Color.Red, slider.ThumbBrush);
        Assert.AreEqual(Color.Black, slider.ThumbBorderBrush);
    }

    [TestMethod]
    public void EventExtensions_SubscribeHandlers()
    {
        var value = 0;
        var slider = new Slider().OnValueChanged(changed => value = (int)changed);

        slider.Value = 12;

        Assert.AreEqual(12, value);
    }

    [TestMethod]
    public void WindowPropertyExtensions_SetValuesBeforeShow()
    {
        var window = new Window()
            .IsToolWindow()
            .AllowsTransparency()
            .CanMinimize(false)
            .CanMaximize(false)
            .CanClose(false)
            .ShowInTaskbar(false)
            .UseLayoutRounding(false);

        Assert.IsTrue(window.IsToolWindow);
        Assert.IsTrue(window.AllowsTransparency);
        Assert.IsFalse(window.CanMinimize);
        Assert.IsFalse(window.CanMaximize);
        Assert.IsFalse(window.CanClose);
        Assert.IsFalse(window.ShowInTaskbar);
        Assert.IsFalse(window.UseLayoutRounding);
    }

    [TestMethod]
    public void TextBasePropertyExtensions_SetValues()
    {
        var textBox = new TextBox()
            .CaretPosition(0)
            .ImeMode(ImeMode.Disabled)
            .MaxLength(20);

        Assert.AreEqual(0, textBox.CaretPosition);
        Assert.AreEqual(ImeMode.Disabled, textBox.ImeMode);
        Assert.AreEqual(20, textBox.MaxLength);
    }

    [TestMethod]
    public void ConvertedBinding_WithConvertBack_UpdatesBothDirections()
    {
        var source = new ObservableValue<int>(12);
        var textBox = new TextBox()
            .BindText(source, value => value.ToString(), int.Parse);

        Assert.AreEqual("12", textBox.Text);

        source.Value = 24;
        Assert.AreEqual("24", textBox.Text);

        textBox.Text = "36";
        Assert.AreEqual(36, source.Value);
    }

    [TestMethod]
    public void ConvertedBinding_WithoutConvertBack_IsOneWay()
    {
        var source = new ObservableValue<int>(1);
        var expander = new Expander()
            .BindIsExpanded(source, value => value > 0);

        Assert.IsTrue(expander.IsExpanded);

        expander.IsExpanded = false;
        Assert.AreEqual(1, source.Value);

        source.Value = 0;
        Assert.IsFalse(expander.IsExpanded);
    }

    [TestMethod]
    public void ConvertedBindingOverloads_AreAvailable()
    {
        var source = new ObservableValue<int>(1);

        _ = new Button().BindContent(source, value => new TextBlock().Text(value.ToString()));
        _ = new MultiLineTextBox().BindText(source, value => value.ToString(), int.Parse);
        _ = new PasswordBox().BindPassword(source, value => value.ToString(), int.Parse);
        _ = new CheckBox().BindIsChecked(source, value => value > 0, value => value == true ? 1 : 0);
        _ = new ToggleButton().BindIsChecked(source, value => value > 0, value => value ? 1 : 0);
        _ = new ToggleSwitch().BindIsChecked(source, value => value > 0, value => value ? 1 : 0);
        _ = new ListBox().BindSelectedIndex(source, value => value, value => value);
        _ = new ComboBox().BindSelectedIndex(source, value => value, value => value);
        _ = new ProgressBar().BindValue(source, value => value);
        _ = new Slider().BindValue(source, value => value, value => (int)value);
        _ = new NumericUpDown().BindValue(source, value => value, value => (int)value);
        _ = new Calendar().BindSelectedDate(source, value => new DateTime(2000, 1, value), value => value?.Day ?? 1);
        _ = new DatePicker().BindSelectedDate(source, value => new DateTime(2000, 1, value), value => value?.Day ?? 1);
        _ = new ProgressRing().BindIsActive(source, value => value > 0);
    }
}
