using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ControlTemplateTests
{
    private sealed class TemplatedControl : Control
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

    private static DelegateControlTemplate<TemplatedControl> LabelTemplate()
        => new((owner, ctx) =>
        {
            var label = new TextBlock();
            ctx.Register("Label", label);
            return new Border { Child = label };
        });

    private static (Window Window, TemplatedControl Control) CreateShown(ControlTemplate? template)
    {
        var window = HeadlessWindow.Create();
        var control = new TemplatedControl();
        if (template != null)
        {
            control.Template = template;
        }
        window.Content = control;
        window.PerformLayout();
        return (window, control);
    }

    [TestMethod]
    public void Build_IsLazy_UntilMeasure()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var control = new TemplatedControl { Template = LabelTemplate() };
        Assert.AreEqual(0, control.ApplyTemplateCount, "setting Template must not build eagerly");

        var window = HeadlessWindow.Create();
        window.Content = control;
        window.PerformLayout();

        Assert.AreEqual(1, control.ApplyTemplateCount, "template builds on the first measure");
        Assert.IsNotNull(control.Label, "named part resolved in OnApplyTemplate");
        Assert.AreSame(control, control.Label.FindVisualRoot() is Window ? control.Label.Parent?.Parent : null,
            "part sits under the template root which sits under the control");
    }

    [TestMethod]
    public void SameTemplate_OnTwoControls_BuildsDistinctTrees()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var template = LabelTemplate();
        var first = new TemplatedControl { Template = template };
        var second = new TemplatedControl { Template = template };

        var window = HeadlessWindow.Create();
        window.Content = new StackPanel().Children(first, second);
        window.PerformLayout();

        Assert.IsNotNull(first.Label);
        Assert.IsNotNull(second.Label);
        Assert.AreNotSame(first.Label, second.Label, "a definition must not share elements between applications");
    }

    [TestMethod]
    public void Traversal_ExposesTemplateRootOnly()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (_, control) = CreateShown(LabelTemplate());

        var children = new List<Element>();
        ((IVisualTreeHost)control).VisitChildren(child =>
        {
            children.Add(child);
            return true;
        });

        Assert.HasCount(1, children);
        Assert.IsInstanceOfType<Border>(children[0], "the single visual child is the template root");
    }

    [TestMethod]
    public void GetTemplateChild_MissingName_ReturnsNull()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (_, control) = CreateShown(new DelegateControlTemplate<TemplatedControl>(
            static (owner, ctx) => new Border()));

        Assert.AreEqual(1, control.ApplyTemplateCount);
        Assert.IsNull(control.Label, "missing part lookups return null instead of throwing");
    }

    [TestMethod]
    public void ReplaceTemplate_DetachesOldTreeAndRebuilds()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, control) = CreateShown(LabelTemplate());
        var oldLabel = control.Label;
        Assert.IsNotNull(oldLabel);
        var oldRoot = (Border)oldLabel.Parent!;

        control.Template = LabelTemplate();
        Assert.IsNull(oldRoot.Parent, "the old root detaches eagerly on template change");

        window.PerformLayout();
        Assert.AreEqual(2, control.ApplyTemplateCount, "the replacement builds on the next measure");
        Assert.AreNotSame(oldLabel, control.Label);
    }

    [TestMethod]
    public void ReplaceTemplate_ClearsFocusInsideOldTree()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var template = new DelegateControlTemplate<TemplatedControl>((owner, ctx) =>
        {
            var button = new Button().Content("part");
            ctx.Register("Part", button);
            return new Border { Child = button };
        });

        var (window, control) = CreateShown(template);
        var button = (Button?)VisualTree.Find(control, element => element is Button);
        Assert.IsNotNull(button);
        button.Focus();
        Assert.AreSame(button, window.FocusManager.FocusedElement);

        control.Template = LabelTemplate();

        Assert.IsNull(window.FocusManager.FocusedElement, "focus inside the discarded template unwinds via detach");
    }

    [TestMethod]
    public void ClearTemplate_RestoresOwnVisualPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, control) = CreateShown(LabelTemplate());
        Assert.IsNotNull(control.Label);

        control.Template = null;
        window.PerformLayout();

        var children = new List<Element>();
        ((IVisualTreeHost)control).VisitChildren(child =>
        {
            children.Add(child);
            return true;
        });
        Assert.IsEmpty(children, "without a template the control exposes no template child");
    }

    [TestMethod]
    public void HitTest_RoutesIntoTemplateTree()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (window, control) = CreateShown(LabelTemplate());

        var hit = window.HitTest(control.CenterOf());

        Assert.IsNotNull(hit);
        Assert.IsTrue(VisualTree.IsInSubtreeOf(hit, control), "hits resolve inside the template subtree");
        Assert.AreNotSame(control, hit, "the template root sits in front of the control itself");
    }

    [TestMethod]
    public void MeasuresThroughTemplateRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var template = new DelegateControlTemplate<TemplatedControl>(
            static (owner, ctx) => new Border { Width = 120, Height = 40 });

        var window = HeadlessWindow.Create();
        var control = new TemplatedControl { Template = template };
        window.Content = new StackPanel().Children(control);
        window.PerformLayout();

        Assert.AreEqual(120, control.DesiredSize.Width);
        Assert.AreEqual(40, control.DesiredSize.Height);
    }

    [TestMethod]
    public void DuplicatePartName_ThrowsDuringBuild()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var template = new DelegateControlTemplate<TemplatedControl>((owner, ctx) =>
        {
            ctx.Register("Part", new TextBlock());
            ctx.Register("Part", new TextBlock());
            return new Border();
        });

        var window = HeadlessWindow.Create();
        window.Content = new TemplatedControl { Template = template };

        Assert.ThrowsExactly<InvalidOperationException>(() => window.PerformLayout());
    }

    [TestMethod]
    public void TypedTemplate_OnWrongOwner_Throws()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var template = new DelegateControlTemplate<Badge>(static (owner, ctx) => new Border());

        var window = HeadlessWindow.Create();
        window.Content = new TemplatedControl { Template = template };

        Assert.ThrowsExactly<InvalidOperationException>(() => window.PerformLayout());
    }

    [TestMethod]
    public void BindChrome_ForwardsChromePropertiesAndFollowsChanges()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        Border? chrome = null;
        var template = new DelegateControlTemplate<TemplatedControl>((owner, ctx) =>
        {
            chrome = new Border();
            ctx.BindChrome(chrome);
            return chrome;
        });

        var window = HeadlessWindow.Create();
        var control = new TemplatedControl
        {
            Template = template,
            Background = Color.Red,
            BorderBrush = Color.Blue,
            BorderThickness = 3,
            CornerRadius = 5,
        };
        window.Content = control;
        window.PerformLayout();

        Assert.IsNotNull(chrome);
        Assert.AreEqual(Color.Red, chrome.Background, "BindChrome applies the current background");
        Assert.AreEqual(Color.Blue, chrome.BorderBrush);
        Assert.AreEqual(3, chrome.BorderThickness);
        Assert.AreEqual(5, chrome.CornerRadius);

        control.Background = Color.Green;
        Assert.AreEqual(Color.Green, chrome.Background, "BindChrome keeps forwarding changes");
    }

    private sealed class Badge : Control
    {
    }
}
