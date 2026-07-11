using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Verifies the "default template supplied through a Style" path: a Style's Template setter
/// builds the control's visual tree with no new core API, purely through the existing
/// PropertyValueStore precedence (Local &gt; Trigger &gt; Style &gt; Inherited &gt; Default).
/// </summary>
[TestClass]
public sealed class StyleTemplateTests
{
    private sealed class StyledControl : Control
    {
        public int ApplyTemplateCount { get; private set; }
        public TextBlock? Label { get; private set; }

        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            ApplyTemplateCount++;
            Label = GetTemplateChild<TextBlock>("Label");
        }
    }

    private static DelegateControlTemplate<StyledControl> LabelTemplate(double width, double height)
        => new((owner, ctx) =>
        {
            var label = new TextBlock();
            ctx.Register("Label", label);
            return new Border { Width = width, Height = height, Child = label };
        });

    private static Style TemplateStyle(double width, double height)
        => new(typeof(StyledControl))
        {
            Setters = [Setter.Create(Control.TemplateProperty, (ControlTemplate?)LabelTemplate(width, height))],
        };

    [TestMethod]
    public void StyleTemplateSetter_BuildsInstance_WithoutLocalTemplate()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var sheet = new StyleSheet();
        sheet.Define<StyledControl>(TemplateStyle(100, 40));
        window.StyleSheet = sheet;

        var control = new StyledControl();
        window.Content = control;
        window.PerformLayout();

        Assert.IsTrue(control.HasTemplateInstance, "the style-supplied Template setter must build a template instance");
        Assert.AreEqual(1, control.ApplyTemplateCount);
        Assert.IsNotNull(control.Label, "named part resolved from the style-applied template");
        Assert.AreEqual(100, control.DesiredSize.Width, "measure follows the style template root");
        Assert.AreEqual(40, control.DesiredSize.Height);
    }

    [TestMethod]
    public void LocalTemplate_OverridesStyleTemplate()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var sheet = new StyleSheet();
        sheet.Define<StyledControl>(TemplateStyle(100, 40));
        window.StyleSheet = sheet;

        var control = new StyledControl();
        window.Content = control;
        window.PerformLayout();
        Assert.AreEqual(100, control.DesiredSize.Width, "sanity: style template applied first");

        control.Template = LabelTemplate(200, 60);
        window.PerformLayout();

        Assert.AreEqual(200, control.DesiredSize.Width, "a local Template must win over the style-supplied one");
        Assert.AreEqual(60, control.DesiredSize.Height);
    }

    [TestMethod]
    public void LocalNullTemplate_HidesStyleTemplate()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var sheet = new StyleSheet();
        sheet.Define<StyledControl>(TemplateStyle(100, 40));
        window.StyleSheet = sheet;

        var control = new StyledControl { Template = null };
        window.Content = control;
        window.PerformLayout();

        Assert.IsFalse(control.HasTemplateInstance,
            "an explicit local null must be stored at the Local tier and hide the style template");
        Assert.AreEqual(0, control.ApplyTemplateCount);
    }

    [TestMethod]
    public void StyleNameSwap_DetachesOldTemplateAndRebuildsNew()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var sheet = new StyleSheet();
        sheet.Define("A", () => TemplateStyle(100, 40));
        sheet.Define("B", () => TemplateStyle(200, 60));
        window.StyleSheet = sheet;

        var control = new StyledControl { StyleName = "A" };
        window.Content = control;
        window.PerformLayout();

        Assert.AreEqual(1, control.ApplyTemplateCount);
        var oldRoot = control.TemplateVisualRoot;
        Assert.IsNotNull(oldRoot);
        Assert.AreEqual(100, control.DesiredSize.Width);

        control.StyleName = "B";

        Assert.IsFalse(control.HasTemplateInstance, "the old template instance detaches eagerly on style swap");
        Assert.IsNull(oldRoot!.Parent, "the old template root detaches eagerly on style swap");

        window.PerformLayout();

        Assert.AreEqual(2, control.ApplyTemplateCount, "the new style's template builds on the next measure");
        Assert.AreEqual(200, control.DesiredSize.Width, "measure now follows style B's template");
        Assert.AreEqual(60, control.DesiredSize.Height);
        Assert.AreNotSame(oldRoot, control.TemplateVisualRoot);
    }
}
