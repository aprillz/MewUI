using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class NumericUpDownTemplateTests
{
    [TestMethod]
    public void StepUp_IncreasesValueByEffectiveStep()
    {
        var nud = new NumericUpDown { Minimum = 0, Maximum = 100, Step = 2, Value = 5 };

        nud.StepUp();

        Assert.AreEqual(7, nud.Value);
    }

    [TestMethod]
    public void StepDown_DecreasesValueByEffectiveStep()
    {
        var nud = new NumericUpDown { Minimum = 0, Maximum = 100, Step = 2, Value = 5 };

        nud.StepDown();

        Assert.AreEqual(3, nud.Value);
    }

    [TestMethod]
    public void StepUp_ClampsAtMaximum()
    {
        var nud = new NumericUpDown { Minimum = 0, Maximum = 10, Step = 5, Value = 9 };

        nud.StepUp();

        Assert.AreEqual(10, nud.Value);
    }

    [TestMethod]
    public void StepDown_ClampsAtMinimum()
    {
        var nud = new NumericUpDown { Minimum = 0, Maximum = 10, Step = 5, Value = 1 };

        nud.StepDown();

        Assert.AreEqual(0, nud.Value);
    }

    [TestMethod]
    public void StepUp_WhenIsInteger_UsesStepOfAtLeastOne()
    {
        var nud = new NumericUpDown { Minimum = 0, Maximum = 100, IsInteger = true, Step = 0.2, Value = 1 };

        nud.StepUp();

        Assert.AreEqual(2, nud.Value, "the effective step rounds up to at least 1 when IsInteger is set");
    }

    private static DelegateControlTemplate<NumericUpDown> FixedSizeTemplate()
        => new(static (owner, ctx) => new Border { Width = 130, Height = 44 });

    [TestMethod]
    public void TemplatedNumericUpDown_MeasuresThroughTemplateRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown { Template = FixedSizeTemplate() };
        window.Content = new StackPanel().Children(nud);
        window.PerformLayout();

        Assert.AreEqual(130, nud.DesiredSize.Width, "measure follows the template root, not the built-in layout");
        Assert.AreEqual(44, nud.DesiredSize.Height, "measure follows the template root, not the built-in layout");
    }

    [TestMethod]
    public void ClearTemplate_RestoresBuiltInMeasure()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown { Template = FixedSizeTemplate() };
        window.Content = new StackPanel().Children(nud);
        window.PerformLayout();

        Assert.AreEqual(130, nud.DesiredSize.Width);

        nud.Template = null;
        window.PerformLayout();

        Assert.AreNotEqual(130, nud.DesiredSize.Width, "without a template, measure comes from the built-in layout again");
    }

    [TestMethod]
    public void PartTextBox_CommitEdit_UpdatesValue()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        TextBox? capturedPart = null;
        var template = new DelegateControlTemplate<NumericUpDown>((owner, ctx) =>
        {
            var textBox = new TextBox();
            ctx.Register(NumericUpDown.PART_TEXT_BOX, textBox);
            capturedPart = textBox;
            return textBox;
        });

        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown { Minimum = 0, Maximum = 100, Template = template };
        window.Content = nud;
        window.PerformLayout();

        Assert.IsNotNull(capturedPart, "the template's PART_TextBox is picked up by OnTemplateInstanceAttached");

        nud.BeginEdit();
        Assert.IsTrue(nud.IsEditing);

        capturedPart!.Text = "42";
        nud.CommitEdit();

        Assert.AreEqual(42, nud.Value);
        Assert.IsFalse(nud.IsEditing);
    }

    [TestMethod]
    public void PartTextBox_CancelEdit_RestoresPreviousValue()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        TextBox? capturedPart = null;
        var template = new DelegateControlTemplate<NumericUpDown>((owner, ctx) =>
        {
            var textBox = new TextBox();
            ctx.Register(NumericUpDown.PART_TEXT_BOX, textBox);
            capturedPart = textBox;
            return textBox;
        });

        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 5, Template = template };
        window.Content = nud;
        window.PerformLayout();

        nud.BeginEdit();
        capturedPart!.Text = "not-a-number";
        nud.CancelEdit();

        Assert.AreEqual(5, nud.Value, "an unparsable edit is discarded, not committed");
        Assert.IsFalse(nud.IsEditing);
    }

    [TestMethod]
    public void PartTextBox_HiddenEditPattern_VisibilityFollowsIsEditing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        TextBox? capturedPart = null;
        var template = new DelegateControlTemplate<NumericUpDown>((owner, ctx) =>
        {
            var textBox = new TextBox { IsVisible = false };
            ctx.Register(NumericUpDown.PART_TEXT_BOX, textBox);
            ctx.Bind(textBox, TextBox.IsVisibleProperty, NumericUpDown.IsEditingProperty);
            capturedPart = textBox;
            return textBox;
        });

        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown { Template = template };
        window.Content = nud;
        window.PerformLayout();

        Assert.IsFalse(capturedPart!.IsVisible, "hidden until editing starts, as authored by the template");

        nud.BeginEdit();

        Assert.IsTrue(capturedPart.IsVisible, "ctx.Bind mirrors IsEditing onto the part's IsVisible");
    }

    [TestMethod]
    public void SpinnerHit_ResolvesToRepeatButtonAndStepsOnPress()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown { Minimum = 0, Maximum = 10, Step = 1, Value = 5, Width = 120, Height = 28 };
        window.Content = nud;
        window.PerformLayout();

        var bounds = nud.Bounds;
        var incrementPoint = new Point(bounds.Right - 5, bounds.Y + 5);
        var hit = nud.HitTest(incrementPoint);
        var button = FindAncestorRepeatButton(hit);
        Assert.IsNotNull(button, "the spinner area resolves into a RepeatButton part");

        button.RaiseMouseDown(new MouseEventArgs(incrementPoint, incrementPoint, MouseButton.Left, leftButton: true));

        Assert.AreEqual(6, nud.Value, "pressing the increment spinner steps immediately");
    }

    [TestMethod]
    public void TextAreaHit_ResolvesToControlItself()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown { Minimum = 0, Maximum = 10, Value = 5, Width = 120, Height = 28 };
        window.Content = nud;
        window.PerformLayout();

        var bounds = nud.Bounds;
        var textPoint = new Point(bounds.X + 5, bounds.Y + bounds.Height / 2);
        var hit = nud.HitTest(textPoint);

        Assert.AreSame(nud, hit, "the text area hits the control so a click begins editing");
    }

    private static RepeatButton? FindAncestorRepeatButton(UIElement? hit)
    {
        for (Element? current = hit; current != null; current = current.Parent)
        {
            if (current is RepeatButton repeatButton)
            {
                return repeatButton;
            }
        }

        return null;
    }
}
