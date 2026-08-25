using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ControlBindingCommitTests
{
    [TestMethod]
    public void CheckBoxToggle_CommitsWithoutRemovingBinding()
    {
        var source = new ObservableValue<bool?>(false);
        var checkBox = new CheckBox();
        checkBox.SetBinding(CheckBox.IsCheckedProperty, source);

        checkBox.Toggle();

        Assert.IsTrue(source.Value.GetValueOrDefault());
        Assert.IsTrue(checkBox.HasPropertyBinding(CheckBox.IsCheckedProperty.Id));
    }

    [TestMethod]
    public void CheckBoxToggle_UpdatesSourceBeforeCheckedChanged()
    {
        var source = new ObservableValue<bool?>(false);
        var checkBox = new CheckBox();
        checkBox.SetBinding(CheckBox.IsCheckedProperty, source);

        bool? sourceDuringEvent = null;
        checkBox.CheckedChanged += _ => sourceDuringEvent = source.Value;

        checkBox.Toggle();

        Assert.IsTrue(sourceDuringEvent.GetValueOrDefault());
        Assert.IsTrue(source.Value.GetValueOrDefault());
        Assert.IsTrue(checkBox.IsChecked.GetValueOrDefault());
    }

    [TestMethod]
    public void ToggleButtonAccessKey_CommitsWithoutRemovingBinding()
    {
        var source = new ObservableValue<bool>(false);
        var toggle = new ToggleButton();
        toggle.SetBinding(ToggleBase.IsCheckedProperty, source);

        toggle.OnAccessKey();

        Assert.IsTrue(source.Value);
        Assert.IsTrue(toggle.HasPropertyBinding(ToggleBase.IsCheckedProperty.Id));
    }

    [TestMethod]
    public void ExpanderAccessKey_CommitsWithoutRemovingBinding()
    {
        var source = new ObservableValue<bool>(true);
        var expander = new Expander();
        expander.SetBinding(Expander.IsExpandedProperty, source);

        expander.OnAccessKey();

        Assert.IsFalse(source.Value);
        Assert.IsTrue(expander.HasPropertyBinding(Expander.IsExpandedProperty.Id));
    }

    [TestMethod]
    public void NumericUpDownStep_CommitsNormalizedValue()
    {
        var source = new ObservableValue<double>(1);
        var numeric = new NumericUpDown { Minimum = 0, Maximum = 10, Step = 20 };
        numeric.SetBinding(RangeBase.ValueProperty, source);

        numeric.StepUp();

        Assert.AreEqual(10, source.Value);
        Assert.AreEqual(10, numeric.Value);
        Assert.IsTrue(numeric.HasPropertyBinding(RangeBase.ValueProperty.Id));
    }

    [TestMethod]
    public void TextBoxDocumentEdit_CommitsWithoutRemovingBinding()
    {
        var source = new ObservableValue<string>("a");
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, source);

        textBox.AppendText("b");

        Assert.AreEqual("ab", source.Value);
        Assert.AreEqual("ab", textBox.Text);
        Assert.IsTrue(textBox.HasPropertyBinding(TextBox.TextProperty.Id));
    }
}
