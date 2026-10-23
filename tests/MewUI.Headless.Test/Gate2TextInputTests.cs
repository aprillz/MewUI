using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;

namespace MewUI.Headless.Test;

/// <summary>Gate 2: text typed into a focused TextBox reaches the value it is bound to.</summary>
[TestClass]
public sealed class Gate2TextInputTests
{
    [TestMethod]
    public void TextInput_IntoFocusedTextBox_UpdatesBoundValue()
    {
        var source = new ObservableValue<string>(string.Empty);
        var textBox = new TextBox().BindText(source);
        var window = HeadlessTestHost.ShowWindow(textBox);
        try
        {
            HeadlessTestHost.Shared.Run(() => Assert.IsTrue(textBox.Focus()));
            window.SendTextInput("Hello");

            Assert.AreEqual("Hello", source.Value);
            Assert.AreEqual("Hello", HeadlessTestHost.Shared.Run(() => textBox.Text));
        }
        finally
        {
            HeadlessTestHost.Close(window);
        }
    }

    [TestMethod]
    public void TextInput_WithoutFocusedTextInput_ChangesNothing()
    {
        var textBox = new TextBox();
        var window = HeadlessTestHost.ShowWindow(textBox);
        try
        {
            window.SendTextInput("Hello");

            Assert.AreEqual(string.Empty, HeadlessTestHost.Shared.Run(() => textBox.Text));
        }
        finally
        {
            HeadlessTestHost.Close(window);
        }
    }
}
