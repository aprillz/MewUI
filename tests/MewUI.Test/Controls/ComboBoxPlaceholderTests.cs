using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class ComboBoxPlaceholderTests
{
    [TestMethod]
    public void ChangingPlaceholder_RemeasuresAComboBoxWithoutWiderItems()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var comboBox = new ComboBox { Placeholder = "i", HorizontalAlignment = HorizontalAlignment.Left };
        using var window = HeadlessWindow.Create(400, 40);
        window.Content = comboBox;
        window.PerformLayout();
        double narrow = comboBox.DesiredSize.Width;

        comboBox.Placeholder = "WWWWWWWWWWWW";
        window.PerformLayout();

        Assert.IsGreaterThan(narrow, comboBox.DesiredSize.Width,
            "A ComboBox kept the width measured for its previous placeholder.");
    }
}
