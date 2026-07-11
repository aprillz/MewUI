using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class DropDownTemplateTests
{
    private static DelegateControlTemplate<ComboBox> FixedSizeTemplate()
        => new(static (owner, ctx) => new Border { Width = 150, Height = 60 });

    [TestMethod]
    public void TemplatedComboBox_MeasuresThroughTemplateRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var comboBox = new ComboBox { Template = FixedSizeTemplate() };
        window.Content = new StackPanel().Children(comboBox);
        window.PerformLayout();

        Assert.AreEqual(150, comboBox.DesiredSize.Width, "measure follows the template root, not the built-in header");
        Assert.AreEqual(60, comboBox.DesiredSize.Height, "measure follows the template root, not the built-in header");
        Assert.IsNotNull(comboBox.TemplateVisualRoot);
        Assert.AreSame(comboBox, comboBox.TemplateVisualRoot!.Parent, "the template root's visual parent is the control");
    }

    [TestMethod]
    public void TemplatedComboBox_TogglesDropDownWithoutException()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var comboBox = new ComboBox { Template = FixedSizeTemplate() };
        window.Content = new StackPanel().Children(comboBox);
        window.PerformLayout();

        comboBox.IsDropDownOpen = true;
        window.PerformLayout();
        Assert.IsTrue(comboBox.IsDropDownOpen, "popup logic still reflects the open state under a template");

        comboBox.IsDropDownOpen = false;
        window.PerformLayout();
        Assert.IsFalse(comboBox.IsDropDownOpen, "popup logic still reflects the closed state under a template");
    }

    [TestMethod]
    public void ClearTemplate_RestoresComboBoxOwnMeasure()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var comboBox = new ComboBox { Template = FixedSizeTemplate() };
        window.Content = new StackPanel().Children(comboBox);
        window.PerformLayout();

        Assert.AreEqual(150, comboBox.DesiredSize.Width);
        Assert.AreEqual(60, comboBox.DesiredSize.Height);

        comboBox.Template = null;
        window.PerformLayout();

        Assert.AreNotEqual(150, comboBox.DesiredSize.Width, "without a template, measure comes from the built-in header again");
        Assert.AreNotEqual(60, comboBox.DesiredSize.Height, "without a template, measure comes from the built-in header again");
    }
}
