using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ButtonTemplateTests
{
    private static DelegateControlTemplate<Button> PresenterTemplate()
        => new(static (owner, ctx) => new Border { Child = new ContentPresenter() });

    private static DelegateControlTemplate<Button> FixedSizeTemplate()
        => new(static (owner, ctx) => new Border { Width = 150, Height = 60 });

    private static bool SkipOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("GDI backend is Windows-only.");
        return true;
    }

    [TestMethod]
    public void TemplatedButton_MeasuresThroughTemplateRoot()
    {
        if (SkipOnNonWindows()) return;

        var window = HeadlessWindow.Create();
        var button = new Button { Content = new TextBlock { Text = "save" }, Template = FixedSizeTemplate() };
        window.Content = new StackPanel().Children(button);
        window.PerformLayout();

        Assert.AreEqual(150, button.DesiredSize.Width, "measure follows the template root, not the built-in content");
        Assert.AreEqual(60, button.DesiredSize.Height, "measure follows the template root, not the built-in content");
        Assert.IsNotNull(button.TemplateVisualRoot);
    }

    [TestMethod]
    public void TemplatedButton_ProjectsExplicitContentThroughBarePresenter()
    {
        if (SkipOnNonWindows()) return;

        var content = new TextBlock { Text = "save" };
        var window = HeadlessWindow.Create();
        var button = new Button { Content = content, Template = PresenterTemplate() };
        window.Content = button;
        window.PerformLayout();

        Assert.IsInstanceOfType<ContentPresenter>(content.Parent, "a bare presenter projects the button's display slot");
        Assert.AreSame(button, content.LogicalParent, "logical ownership stays with the button");
    }

    [TestMethod]
    public void TemplatedButton_KeepsCommandPresentationContent()
    {
        if (SkipOnNonWindows()) return;

        var window = HeadlessWindow.Create();
        var command = new Command("test.save", "Save");
        var button = new Button
        {
            Command = command,
            CommandPresentationMode = CommandPresentationMode.TextAndIcon,
            Template = PresenterTemplate(),
        };
        window.Content = button;
        window.PerformLayout();

        var generated = button.EffectiveContent;
        Assert.IsNotNull(generated, "the command supplies the display content when no Content is set");
        Assert.IsInstanceOfType<ContentPresenter>(generated.Parent, "the generated content projects through the presenter");
    }

    [TestMethod]
    public void ClearTemplate_RestoresContentVisualParent()
    {
        if (SkipOnNonWindows()) return;

        var content = new TextBlock { Text = "save" };
        var window = HeadlessWindow.Create();
        var button = new Button { Content = content, Template = PresenterTemplate() };
        window.Content = button;
        window.PerformLayout();

        Assert.IsInstanceOfType<ContentPresenter>(content.Parent);

        button.Template = null;
        window.PerformLayout();

        Assert.AreSame(button, content.Parent, "the button hosts its content again once the template is gone");
        Assert.AreSame(button, content.LogicalParent);
    }

    [TestMethod]
    public void TemplatedButton_ReprojectsReplacedContent()
    {
        if (SkipOnNonWindows()) return;

        var first = new TextBlock { Text = "first" };
        var second = new TextBlock { Text = "second" };
        var window = HeadlessWindow.Create();
        var button = new Button { Content = first, Template = PresenterTemplate() };
        window.Content = button;
        window.PerformLayout();

        var presenter = first.Parent;
        button.Content = second;
        window.PerformLayout();

        Assert.IsNull(first.Parent, "the replaced element leaves the presenter");
        Assert.AreSame(presenter, second.Parent, "the new element takes the same presenter");
        Assert.IsNull(first.LogicalParent, "the replaced element also leaves the logical slot");
    }

    [TestMethod]
    public void ExplicitContent_SuppressesCommandFallback()
    {
        if (SkipOnNonWindows()) return;

        var window = HeadlessWindow.Create();
        var command = new Command("test.save", "Save");
        var button = new Button { Command = command, CommandPresentationMode = CommandPresentationMode.Text };
        window.Content = button;
        window.PerformLayout();

        Assert.IsNotNull(button.EffectiveContent, "the command supplies content while Content has no value source");

        var explicitContent = new TextBlock { Text = "explicit" };
        button.Content = explicitContent;
        window.PerformLayout();
        Assert.AreSame(explicitContent, button.EffectiveContent, "an explicit value wins over the generated content");

        button.Content = null;
        window.PerformLayout();
        Assert.IsNull(button.EffectiveContent, "assigning null retires the generated content");
    }

    [TestMethod]
    public void ExplicitNullOverUnsetContent_SuppressesCommandFallback()
    {
        if (SkipOnNonWindows()) return;

        var window = HeadlessWindow.Create();
        var command = new Command("test.save", "Save");
        var button = new Button { Command = command, CommandPresentationMode = CommandPresentationMode.Text };
        window.Content = button;
        window.PerformLayout();

        Assert.IsNotNull(button.EffectiveContent, "the command supplies content while Content has no value source");

        // The value stays null here; only the value source moves, which is what makes this explicit.
        button.Content = null;
        window.PerformLayout();
        Assert.IsNull(button.EffectiveContent, "an explicitly assigned null retires the generated content");

        button.ClearLocalValue(ContentControl.ContentProperty);
        window.PerformLayout();
        Assert.IsNotNull(button.EffectiveContent, "clearing the value source brings the command content back");
    }

    [TestMethod]
    public void ContentProperty_CompatibilityAliasesShareOneDescriptor()
    {
        Assert.AreSame(ContentControl.ContentProperty, Button.ContentProperty);
        Assert.AreSame(CommandSourceControl.CommandProperty, Button.CommandProperty);
        Assert.AreSame(CommandSourceControl.CommandProperty, SegmentButton.CommandProperty);
    }

    [TestMethod]
    public void TemplatedButton_ClickRunsThroughTemplateSubtree()
    {
        if (SkipOnNonWindows()) return;

        var window = HeadlessWindow.Create();
        int clicks = 0;
        var button = new Button { Content = new TextBlock { Text = "save" }, Template = PresenterTemplate() };
        button.Click += () => clicks++;
        window.Content = button;
        window.PerformLayout();

        button.RaiseClick();

        Assert.AreEqual(1, clicks, "activation still reaches the owner under a template");
    }
}
