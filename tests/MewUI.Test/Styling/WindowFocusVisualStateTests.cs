using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Styling;

[TestClass]
public sealed class WindowFocusVisualStateTests
{
    [TestMethod]
    public void FocusedState_FollowsWindowActivationWithoutClearingFocus()
    {
        var button = new TestButton();
        var window = HeadlessWindow.Create();
        window.Content = button;
        window.PerformLayout();
        // Keep transition-backed style changes deterministic by making the target offscreen.
        button.Arrange(new Rect(1000, 1000, 100, 30));
        window.SetIsActive(true);

        Assert.IsTrue(button.Focus());
        window.UpdateVisualStates();

        Assert.AreSame(button, window.FocusManager.FocusedElement);
        Assert.IsTrue(button.IsFocused);
        Assert.IsTrue(button.ResolvedFlags.HasFlag(VisualStateFlags.Focused));
        Assert.AreEqual(button.ThemeInternal.Palette.Accent, button.BorderBrush);

        window.SetIsActive(false);

        Assert.IsTrue(button.IsVisualStateDirty);
        window.UpdateVisualStates();
        Assert.AreSame(button, window.FocusManager.FocusedElement);
        Assert.IsTrue(button.IsFocused);
        Assert.IsFalse(button.ResolvedFlags.HasFlag(VisualStateFlags.Focused));
        Assert.AreEqual(button.ThemeInternal.Palette.ControlBorder, button.BorderBrush);

        window.SetIsActive(true);

        window.UpdateVisualStates();
        Assert.IsTrue(button.ResolvedFlags.HasFlag(VisualStateFlags.Focused));
        Assert.AreEqual(button.ThemeInternal.Palette.Accent, button.BorderBrush);
    }

    [TestMethod]
    public void FocusWithinState_FollowsWindowActivationOnAncestorChain()
    {
        var textBox = new TextBox();
        var button = new TestButton { Content = textBox };
        var window = HeadlessWindow.Create();
        window.Content = button;
        window.PerformLayout();
        button.Arrange(new Rect(1000, 1000, 100, 30));
        window.SetIsActive(true);

        Assert.IsTrue(textBox.Focus());
        window.UpdateVisualStates();

        Assert.IsTrue(button.IsFocusWithin);
        Assert.IsTrue(button.ResolvedFlags.HasFlag(VisualStateFlags.Focused));
        Assert.AreEqual(button.ThemeInternal.Palette.Accent, button.BorderBrush);

        window.SetIsActive(false);

        Assert.IsTrue(button.IsVisualStateDirty);
        window.UpdateVisualStates();
        Assert.IsTrue(button.IsFocusWithin);
        Assert.IsFalse(button.ResolvedFlags.HasFlag(VisualStateFlags.Focused));
        Assert.AreEqual(button.ThemeInternal.Palette.ControlBorder, button.BorderBrush);

        window.SetIsActive(true);

        window.UpdateVisualStates();
        Assert.IsTrue(button.ResolvedFlags.HasFlag(VisualStateFlags.Focused));
        Assert.AreEqual(button.ThemeInternal.Palette.Accent, button.BorderBrush);
    }

    [TestMethod]
    public void InvalidState_RemainsVisibleWhileFocusedWindowIsInactive()
    {
        var source = new ObservableValue<int>(1);
        var textBox = new TextBox();
        textBox.SetBinding(
            TextBox.TextProperty,
            source,
            static value => value.ToString(),
            static value => int.Parse(value),
            BindingMode.TwoWay);
        var window = HeadlessWindow.Create();
        window.Content = textBox;
        window.PerformLayout();
        // Keep transition-backed style changes deterministic by making the target offscreen.
        textBox.Arrange(new Rect(1000, 1000, 100, 30));
        window.SetIsActive(true);
        ReportValidationError(textBox);

        Assert.IsTrue(textBox.Focus());
        window.UpdateVisualStates();
        Assert.IsTrue(textBox.HasValidationError);
        Assert.AreEqual(textBox.ThemeInternal.Palette.Error, textBox.BorderBrush);

        window.SetIsActive(false);
        window.UpdateVisualStates();

        Assert.IsTrue(textBox.IsFocused);
        Assert.IsTrue(textBox.HasValidationError);
        Assert.AreEqual(textBox.ThemeInternal.Palette.Error, textBox.BorderBrush);
    }

    [TestMethod]
    public void DetachedFocusedControl_KeepsFocusedState()
    {
        var button = new TestButton();
        button.SetFocused(true);
        button.ResolveAndApplyStyle();

        Assert.IsTrue(button.ResolvedFlags.HasFlag(VisualStateFlags.Focused));
        Assert.AreEqual(button.ThemeInternal.Palette.Accent, button.BorderBrush);
    }

    private sealed class TestButton : Button
    {
        public VisualStateFlags ResolvedFlags => CurrentVisualState.Flags;
    }

    private static void ReportValidationError(TextBox textBox)
        => textBox.ReportBindingError(
            TextBox.TextProperty,
            "invalid",
            BindingStatus.ValidationError,
            BindingErrorStage.SourceValidation,
            new InvalidOperationException("validation failed"));
}
