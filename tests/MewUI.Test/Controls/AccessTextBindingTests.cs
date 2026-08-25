using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class AccessTextBindingTests
{
    [TestMethod]
    public void RawText_ParsesMnemonicIntoAccessKey()
    {
        var at = new AccessText { RawText = "_Save" };

        Assert.AreEqual('S', at.AccessKey);
        Assert.AreEqual(0, at.UnderlineIndex);
    }

    [TestMethod]
    public void RawText_WithoutMarker_HasNoAccessKey()
    {
        var at = new AccessText { RawText = "Save" };

        Assert.AreEqual(default(char), at.AccessKey);
        Assert.AreEqual(-1, at.UnderlineIndex);
    }

    [TestMethod]
    public void ButtonBindContent_ParsesMnemonicAndTracksSource()
    {
        var caption = new ObservableValue<string>("_Open");
        var button = new Button().BindContent(caption);

        var at = (AccessText)button.Content!;
        Assert.AreEqual('O', at.AccessKey, "bound content must parse the mnemonic, not treat '_Open' as literal text");

        caption.Value = "_Save";
        Assert.AreEqual('S', at.AccessKey, "the access key must re-parse when the bound source changes");
    }

    [TestMethod]
    public void ToggleBindContent_ParsesMnemonic()
    {
        var checkBox = new CheckBox().BindContent(new ObservableValue<string>("_Enable"));
        var toggleButton = new ToggleButton().BindContent(new ObservableValue<string>("_Run"));

        Assert.AreEqual('E', ((AccessText)checkBox.Content!).AccessKey);
        Assert.AreEqual('R', ((AccessText)toggleButton.Content!).AccessKey);
    }

    [TestMethod]
    public void ButtonBindContent_AltAccessKey_RaisesClick()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        int clicks = 0;
        var button = new Button()
            .BindContent(new ObservableValue<string>("_Open"))
            .OnClick(() => clicks++);
        window.Content = button;
        window.PerformLayout();

        // Alt reveals the access keys, then Alt+O activates the registered target.
        window.ProcessAccessKeyDown(new KeyEventArgs(Key.None, 0, ModifierKeys.Alt));
        window.ProcessAccessKeyDown(new KeyEventArgs(Key.O, 0, ModifierKeys.Alt));

        Assert.AreEqual(1, clicks, "a bound-content mnemonic must register and fire the button via Alt+key");
    }

    [TestMethod]
    public void AltReveal_PropagatesShowAccessKeysToBoundContent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var button = new Button().BindContent(new ObservableValue<string>("_Open"));
        window.Content = button;
        window.PerformLayout();
        var at = (AccessText)button.Content!;

        Assert.IsFalse(ReadShowAccessKeys(at), "access keys are hidden until Alt is pressed");

        // Alt reveals the access keys on the window; the underline only renders when this
        // inherited flag reaches the AccessText.
        window.ProcessAccessKeyDown(new KeyEventArgs(Key.None, 0, ModifierKeys.Alt));

        Assert.IsTrue(ReadShowAccessKeys(at), "ShowAccessKeys must inherit down to the bound AccessText");
    }

    private static bool ReadShowAccessKeys(AccessText at)
    {
        var method = typeof(MewObject)
            .GetMethod("GetValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(typeof(bool));
        return (bool)method.Invoke(at, new object[] { Window.ShowAccessKeysProperty })!;
    }
}
