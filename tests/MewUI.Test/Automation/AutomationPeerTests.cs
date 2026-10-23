using Aprillz.MewUI;
using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Automation;

[TestClass]
public sealed class AutomationPeerTests
{
    private static bool SkipWithoutGdi()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("GDI backend is Windows-only.");
        return true;
    }

    private static AutomationPeer PeerOf(UIElement element)
    {
        var peer = element.GetAutomationPeer();
        Assert.IsNotNull(peer, $"{element.GetType().Name} has no peer");
        return peer;
    }

    private static Window Host(Element content)
    {
        var window = HeadlessWindow.Create();
        window.Content = content;
        window.PerformLayout();
        return window;
    }

    [TestMethod]
    public void StandardControls_ReportRoleNameAndPatterns()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var image = new Image();
        AutomationProperties.SetName(image, "Logo");
        var expander = new Expander().Header("Details");
        expander.Content = new TextBlock { Text = "inside" };
        var groupBox = new GroupBox().Header("Options");
        groupBox.Content = new TextBlock { Text = "inside" };

        var cases = new (UIElement Element, AutomationControlType Type, string Name, Type[] Patterns)[]
        {
            (new Button().Content("_Save"), AutomationControlType.Button, "Save", [typeof(IInvokeProvider)]),
            (new RepeatButton().Content("Step"), AutomationControlType.Button, "Step", [typeof(IInvokeProvider)]),
            (new CheckBox().Content("Agree"), AutomationControlType.CheckBox, "Agree", [typeof(IToggleProvider)]),
            (new ToggleButton().Content("Bold"), AutomationControlType.Button, "Bold", [typeof(IToggleProvider)]),
            (new ToggleSwitch(), AutomationControlType.CheckBox, "", [typeof(IToggleProvider)]),
            (new RadioButton { Content = new TextBlock { Text = "Small" } }, AutomationControlType.RadioButton, "Small", [typeof(ISelectionItemProvider)]),
            (new TextBox { Placeholder = "Search" }, AutomationControlType.Edit, "Search", [typeof(IValueProvider)]),
            (new PasswordBox(), AutomationControlType.Edit, "", [typeof(IValueProvider)]),
            (new MultiLineTextBox(), AutomationControlType.Edit, "", [typeof(IValueProvider)]),
            (new TextBlock { Text = "Hello" }, AutomationControlType.Text, "Hello", []),
            (new Label { Text = "_Name" }, AutomationControlType.Text, "Name", []),
            (new Slider(), AutomationControlType.Slider, "", [typeof(IRangeValueProvider)]),
            (new NumericUpDown(), AutomationControlType.Spinner, "", [typeof(IRangeValueProvider)]),
            (new ProgressBar(), AutomationControlType.ProgressBar, "", [typeof(IRangeValueProvider)]),
            (new ProgressRing(), AutomationControlType.ProgressBar, "", [typeof(IRangeValueProvider)]),
            (new ComboBox().Items("One", "Two"), AutomationControlType.ComboBox, "", [typeof(IExpandCollapseProvider), typeof(IValueProvider)]),
            (new ListBox().Items("One", "Two"), AutomationControlType.List, "", [typeof(ISelectionProvider), typeof(IScrollProvider)]),
            (new ScrollViewer { Content = new TextBlock { Text = "long" } }, AutomationControlType.Pane, "", [typeof(IScrollProvider)]),
            (expander, AutomationControlType.Group, "Details", [typeof(IExpandCollapseProvider)]),
            (groupBox, AutomationControlType.Group, "Options", []),
            (image, AutomationControlType.Image, "Logo", []),
            (new GlyphElement(), AutomationControlType.Image, "", []),
            (new Calendar(), AutomationControlType.Calendar, "", []),
            (new ToolBar(), AutomationControlType.ToolBar, "", []),
        };

        foreach (var (element, type, name, patterns) in cases)
        {
            Host(element);
            var peer = PeerOf(element);
            string label = element.GetType().Name;
            Assert.AreEqual(type, peer.GetControlType(), label);
            Assert.AreEqual(name, peer.GetName(), label);
            foreach (var pattern in patterns)
            {
                Assert.IsTrue(pattern.IsInstanceOfType(peer), $"{label} lacks {pattern.Name}");
            }
        }
    }

    [TestMethod]
    public void Window_IsTheRootAndIsNamedByItsTitle()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var window = Host(new Button().Content("OK"));
        window.Title = "Settings";

        var peer = PeerOf(window);
        Assert.AreEqual(AutomationControlType.Window, peer.GetControlType());
        Assert.AreEqual("Settings", peer.GetName());
        Assert.IsNull(peer.GetParent());
    }

    [TestMethod]
    public void ExplicitName_WinsOverLabeledByAndContent()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var label = new TextBlock { Text = "User name" };
        var labeled = new TextBox { Placeholder = "type here" };
        AutomationProperties.SetLabeledBy(labeled, label);
        var named = new Button().Content("Go");
        AutomationProperties.SetName(named, "Start the search");
        Host(new StackPanel().Children(label, labeled, named));

        Assert.AreEqual("User name", PeerOf(labeled).GetName());
        Assert.AreEqual("Start the search", PeerOf(named).GetName());
    }

    [TestMethod]
    public void PanelsAndBorders_AreSkipped_AndTheirChildrenPresented()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var button = new Button().Content("A");
        var text = new TextBlock { Text = "B" };
        var window = Host(new StackPanel().Children(new Border { Child = button }, text));

        var root = PeerOf(window);
        var children = root.GetChildren();
        CollectionAssert.AreEqual(new[] { PeerOf(button), PeerOf(text) }, children.ToArray());
        Assert.AreSame(root, PeerOf(button).GetParent());
    }

    [TestMethod]
    public void TextContent_IsTheName_AndIsNotRepeatedAsAChild()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var button = new Button().Content("Save");
        Host(button);

        var peer = PeerOf(button);
        Assert.AreEqual(0, peer.GetChildren().Count);
        Assert.IsNull(((UIElement)button.Content!).GetAutomationPeer());
    }

    [TestMethod]
    public void RawView_HidesTheElement()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var hidden = new TextBlock { Text = "decoration" };
        AutomationProperties.SetAccessibilityView(hidden, AccessibilityView.Raw);
        var shown = new TextBlock { Text = "shown" };
        var window = Host(new StackPanel().Children(hidden, shown));

        CollectionAssert.AreEqual(new[] { PeerOf(shown) }, PeerOf(window).GetChildren().ToArray());
    }

    [TestMethod]
    public void Invoke_RaisesClick_AndRefusesWhenDisabled()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        int clicks = 0;
        var button = new Button().Content("Go");
        button.Click += () => clicks++;
        Host(button);

        PeerOf(button).GetProvider<IInvokeProvider>()!.Invoke();
        Assert.AreEqual(1, clicks);

        button.IsEnabled = false;
        Assert.ThrowsExactly<InvalidOperationException>(() => PeerOf(button).GetProvider<IInvokeProvider>()!.Invoke());
        Assert.AreEqual(1, clicks);
    }

    [TestMethod]
    public void Toggle_ChangesIsChecked_AndCommitsToTheBinding()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var source = new ObservableValue<bool?>(false);
        var checkBox = new CheckBox().Content("Agree");
        checkBox.SetBinding(CheckBox.IsCheckedProperty, source);
        var toggle = new ToggleButton().Content("Bold");
        Host(new StackPanel().Children(checkBox, toggle));

        var checkPeer = PeerOf(checkBox).GetProvider<IToggleProvider>()!;
        checkPeer.Toggle();
        Assert.AreEqual(ToggleState.On, checkPeer.ToggleState);
        Assert.IsTrue(source.Value.GetValueOrDefault());
        Assert.IsTrue(checkBox.HasPropertyBinding(CheckBox.IsCheckedProperty.Id));

        var togglePeer = PeerOf(toggle).GetProvider<IToggleProvider>()!;
        togglePeer.Toggle();
        Assert.IsTrue(toggle.IsChecked);
        Assert.AreEqual(ToggleState.On, togglePeer.ToggleState);
    }

    [TestMethod]
    public void RadioButtonSelect_ChecksIt_AndClearsTheGroup()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var first = new RadioButton { Content = new TextBlock { Text = "Small" }, IsChecked = true };
        var second = new RadioButton { Content = new TextBlock { Text = "Large" } };
        Host(new StackPanel().Children(first, second));

        PeerOf(second).GetProvider<ISelectionItemProvider>()!.Select();

        Assert.IsTrue(second.IsChecked);
        Assert.IsFalse(first.IsChecked);
    }

    [TestMethod]
    public void ValueSetValue_ChangesTheText_AndTheBindingFollows()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var source = new ObservableValue<string>("old");
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, source);
        Host(textBox);

        var value = PeerOf(textBox).GetProvider<IValueProvider>()!;
        value.SetValue("new text");

        Assert.AreEqual("new text", textBox.Text);
        Assert.AreEqual("new text", value.Value);
        Assert.AreEqual("new text", source.Value);
    }

    [TestMethod]
    public void PasswordBox_NeverReportsItsValue()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var password = new PasswordBox();
        Host(password);

        var value = PeerOf(password).GetProvider<IValueProvider>()!;
        value.SetValue("secret");

        Assert.AreEqual(string.Empty, value.Value);
    }

    [TestMethod]
    public void ReadOnlyTextBox_RefusesSetValue()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var textBox = new TextBox { IsReadOnly = true };
        Host(textBox);

        Assert.ThrowsExactly<InvalidOperationException>(() => PeerOf(textBox).GetProvider<IValueProvider>()!.SetValue("x"));
    }

    [TestMethod]
    public void RangeSetValue_ChangesTheSliderValue_AndChecksTheRange()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var source = new ObservableValue<double>(0);
        var slider = new Slider { Minimum = 0, Maximum = 10 };
        slider.SetBinding(RangeBase.ValueProperty, source);
        var progress = new ProgressBar();
        Host(new StackPanel().Children(slider, progress));

        var range = PeerOf(slider).GetProvider<IRangeValueProvider>()!;
        range.SetValue(7);
        Assert.AreEqual(7, slider.Value);
        Assert.AreEqual(7, source.Value);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => range.SetValue(11));

        var progressRange = PeerOf(progress).GetProvider<IRangeValueProvider>()!;
        Assert.IsTrue(progressRange.IsReadOnly);
        Assert.ThrowsExactly<InvalidOperationException>(() => progressRange.SetValue(1));
    }

    [TestMethod]
    public void ListItems_AreNamedByTheirText_AndSelectChangesTheSelection()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var list = new ListBox().Items("Red", "Green", "Blue");
        Host(list);

        var listPeer = PeerOf(list);
        var items = listPeer.GetChildren();
        CollectionAssert.AreEqual(new[] { "Red", "Green", "Blue" }, items.Select(item => item.GetName()).ToArray());
        Assert.IsTrue(items.All(item => item.GetControlType() == AutomationControlType.ListItem));
        Assert.IsTrue(items.All(item => item.GetParent() == listPeer));

        items[1].GetProvider<ISelectionItemProvider>()!.Select();

        Assert.AreEqual(1, list.SelectedIndex);
        CollectionAssert.AreEqual(new[] { items[1] }, listPeer.GetProvider<ISelectionProvider>()!.GetSelection().ToArray());
        Assert.AreSame(listPeer, items[1].GetProvider<ISelectionItemProvider>()!.SelectionContainer);
    }

    [TestMethod]
    public void MultiSelectList_AddsAndRemovesItems()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var list = new ListBox().Items("Red", "Green", "Blue");
        list.SelectionMode = ItemsSelectionMode.Extended;
        Host(list);

        var listPeer = PeerOf(list);
        var items = listPeer.GetChildren();
        items[0].GetProvider<ISelectionItemProvider>()!.Select();
        items[2].GetProvider<ISelectionItemProvider>()!.AddToSelection();
        list.PerformLayoutOfOwnerWindow();

        Assert.IsTrue(listPeer.GetProvider<ISelectionProvider>()!.CanSelectMultiple);
        CollectionAssert.AreEqual(new[] { 0, 2 }, list.SelectedIndices.ToArray());

        items[0].GetProvider<ISelectionItemProvider>()!.RemoveFromSelection();
        CollectionAssert.AreEqual(new[] { 2 }, list.SelectedIndices.ToArray());
    }

    [TestMethod]
    public void Expand_OpensTheExpander_AndTheComboBox()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var source = new ObservableValue<bool>(false);
        var expander = new Expander().Header("More");
        expander.Content = new TextBlock { Text = "hidden" };
        expander.SetBinding(Expander.IsExpandedProperty, source);
        var combo = new ComboBox().Items("One", "Two");
        Host(new StackPanel().Children(expander, combo));

        var expand = PeerOf(expander).GetProvider<IExpandCollapseProvider>()!;
        expand.Expand();
        Assert.IsTrue(expander.IsExpanded);
        Assert.IsTrue(source.Value);
        Assert.AreEqual(ExpandCollapseState.Expanded, expand.ExpandCollapseState);

        var comboExpand = PeerOf(combo).GetProvider<IExpandCollapseProvider>()!;
        comboExpand.Expand();
        Assert.IsTrue(combo.IsDropDownOpen);
        comboExpand.Collapse();
        Assert.IsFalse(combo.IsDropDownOpen);
    }

    [TestMethod]
    public void ScrollViewer_ScrollsByPercent()
    {
        if (SkipWithoutGdi())
        {
            return;
        }

        var viewer = new ScrollViewer
        {
            Height = 100,
            Content = new Border { Height = 1100 },
        };
        var window = Host(viewer);

        var scroll = PeerOf(viewer).GetProvider<IScrollProvider>()!;
        Assert.IsTrue(scroll.VerticallyScrollable);
        Assert.AreEqual(0, scroll.VerticalScrollPercent);

        scroll.SetScrollPercent(IScrollProvider.NoScroll, 50);
        window.PerformLayout();

        Assert.AreEqual(50, scroll.VerticalScrollPercent, 0.5);
    }
}

internal static class AutomationPeerTestExtensions
{
    public static void PerformLayoutOfOwnerWindow(this UIElement element)
        => (element.FindVisualRoot() as Window)?.PerformLayout();
}
