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
    public void ClearLocalTemplate_HidesTemplateEntirely()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        // NumericUpDown has no built-in (non-templated) rendering path anymore: its default
        // style supplies a template. An explicit local Template = null sits at the Local tier
        // and hides that style template too (see StyleTemplateTests.LocalNullTemplate_HidesStyleTemplate),
        // leaving the control with no visual content at all.
        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown { Template = FixedSizeTemplate() };
        window.Content = new StackPanel().Children(nud);
        window.PerformLayout();

        Assert.AreEqual(130, nud.DesiredSize.Width);

        nud.Template = null;
        window.PerformLayout();

        Assert.IsFalse(nud.HasTemplateInstance, "an explicit local null hides the style-supplied default template too");
        Assert.AreNotEqual(130, nud.DesiredSize.Width, "with no template at all, the control has nothing to measure");
    }

    [TestMethod]
    public void NoLocalTemplate_BuildsDefaultTemplateFromStyle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown();
        window.Content = nud;
        window.PerformLayout();

        Assert.IsTrue(nud.HasTemplateInstance, "the default style supplies a Template setter, so every NumericUpDown builds a template instance");
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

        // Locate the increment RepeatButton by its glyph rather than hardcoding pixel offsets
        // from the control's bounds: the default template's Padding wraps the whole chrome
        // (including the spinner column), so a fixed inset from the edge is not stable.
        var incrementButton = (RepeatButton?)VisualTree.Find(nud,
            element => element is RepeatButton repeatButton && repeatButton.Content is GlyphElement { Kind: GlyphKind.ChevronUp });
        Assert.IsNotNull(incrementButton, "the default template's increment spinner is a RepeatButton with a ChevronUp glyph");

        var incrementPoint = incrementButton!.CenterOf();
        var hit = nud.HitTest(incrementPoint);
        var button = FindAncestorRepeatButton(hit);
        Assert.IsNotNull(button, "the spinner area resolves into a RepeatButton part");

        button.RaiseMouseDown(new MouseEventArgs(incrementPoint, incrementPoint, MouseButton.Left, leftButton: true));

        Assert.AreEqual(6, nud.Value, "pressing the increment spinner steps immediately");
    }

    [TestMethod]
    public void TextAreaHit_BubblesMouseDownToBeginEdit()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        // With the default template, the text area is the display TextBlock's Grid cell, not
        // the control itself: TextBlock does not participate in hit testing (it is not a
        // UIElement) and the Grid/Border chrome around it claim the hit instead. A left-button
        // mouse-down still bubbles up from that descendant to the control, which begins editing.
        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown { Minimum = 0, Maximum = 10, Value = 5, Width = 120, Height = 28 };
        window.Content = nud;
        window.PerformLayout();

        var bounds = nud.Bounds;
        var textPoint = new Point(bounds.X + 5, bounds.Y + bounds.Height / 2);
        var hit = nud.HitTest(textPoint);

        Assert.IsNotNull(hit, "the text area resolves to a descendant inside the template tree");
        Assert.IsFalse(nud.IsEditing);

        window.SendMouseDown(textPoint);

        Assert.IsTrue(nud.IsEditing, "the mouse-down bubbles from the hit descendant up to the control, which begins editing");
    }

    [TestMethod]
    public void DisplayText_TracksValueAndFormat()
    {
        var nud = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 5 };

        Assert.AreEqual("5", nud.DisplayText);

        nud.Value = 7.5;
        Assert.AreEqual("7.5", nud.DisplayText);

        nud.Format = "0.00";
        Assert.AreEqual("7.50", nud.DisplayText);
    }

    [TestMethod]
    public void DefaultTemplate_BeginEdit_SwapsDisplayForEditPart()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 5 };
        window.Content = nud;
        window.PerformLayout();

        nud.BeginEdit();

        Assert.IsTrue(nud.IsEditing);
        Assert.AreEqual("5", nud.DisplayText, "the display text still reflects the committed value while editing");
    }

    [TestMethod]
    public void DefaultTemplate_CommitEdit_UpdatesValueAndExitsEditMode()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 5 };
        window.Content = nud;
        window.PerformLayout();

        nud.BeginEdit();
        var textBox = (TextBox?)VisualTree.Find(nud, element => element is TextBox);
        Assert.IsNotNull(textBox, "the default template's PART_TextBox is reachable through the visual tree");

        textBox!.Text = "42";
        nud.CommitEdit();

        Assert.AreEqual(42, nud.Value);
        Assert.IsFalse(nud.IsEditing);
        Assert.AreEqual("42", nud.DisplayText);
    }

    [TestMethod]
    public void DefaultTemplate_CancelEdit_RestoresPreviousValue()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var nud = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 5 };
        window.Content = nud;
        window.PerformLayout();

        nud.BeginEdit();
        var textBox = (TextBox?)VisualTree.Find(nud, element => element is TextBox);
        Assert.IsNotNull(textBox);

        textBox!.Text = "not-a-number";
        nud.CancelEdit();

        Assert.AreEqual(5, nud.Value, "an unparsable edit is discarded, not committed");
        Assert.IsFalse(nud.IsEditing);
        Assert.AreEqual("5", nud.DisplayText);
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
