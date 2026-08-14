using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Styling;

/// <summary>
/// A type rule may name its style instead of holding it. The name is resolved where a control's own
/// StyleName is, which is what lets a rule point at a key defined further out - the built-in ones among
/// them - and lets a nearer scope redefine that key.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class NamedTypeRuleTests
{
    private static Style RedButton() => Style.DeriveFromDefault<Button>(
        setters: [Setter.Create(Control.BackgroundProperty, Color.Red)]);

    private static Style BlueButton() => Style.DeriveFromDefault<Button>(
        setters: [Setter.Create(Control.BackgroundProperty, Color.Blue)]);

    [TestMethod]
    public void ARuleTakesTheStyleTheNameResolvesTo()
    {
        var button = new Button { Content = new TextBlock { Text = "Hit" } };
        var panel = new StackPanel()
            .Vertical()
            .StyleSheet(new StyleSheet().WithName<Button>("test-button"))
            .Children(button);

        var window = HeadlessWindow.Create();
        window.StyleSheet = new StyleSheet().With("test-button", RedButton);
        window.Content = panel;
        window.PerformLayout();
        window.ForceStyleSnap();

        // The name is defined on the window, the rule on the panel: the rule reaches out for it.
        Assert.AreEqual(Color.Red, button.Background, "the rule did not resolve its style name");

        window.Close();
    }

    [TestMethod]
    public void ANearerScopeRedefinesTheNameTheRulePointsAt()
    {
        var outer = new Button { Content = new TextBlock { Text = "Outer" } };
        var inner = new Button { Content = new TextBlock { Text = "Inner" } };

        // Both buttons take the same rule; the inner panel redefines what the name means for its own.
        var innerPanel = new StackPanel()
            .Vertical()
            .StyleSheet(new StyleSheet().With("test-button", BlueButton))
            .Children(inner);

        var rulePanel = new StackPanel()
            .Vertical()
            .StyleSheet(new StyleSheet().WithName<Button>("test-button"))
            .Children(outer, innerPanel);

        var window = HeadlessWindow.Create();
        window.StyleSheet = new StyleSheet().With("test-button", RedButton);
        window.Content = rulePanel;
        window.PerformLayout();
        window.ForceStyleSnap();

        Assert.AreEqual(Color.Red, outer.Background, "the outer button did not take the outer definition");
        Assert.AreEqual(Color.Blue, inner.Background, "the nearer definition did not win for the inner button");

        window.Close();
    }

    [TestMethod]
    public void ARuleNamingAStyleNobodyDefinedSaysSo()
    {
        TestPlatformHosts.EnsureRegistered();

        var button = new Button { Content = new TextBlock { Text = "Typo" } };
        var window = new Window
        {
            Content = new StackPanel()
                .Vertical()
                .StyleSheet(new StyleSheet().WithName<Button>("no-such-style"))
                .Children(button),
        };
        window.AttachBackend(new HeadlessWindowBackend());
        window.SetClientSizeDip(200, 100);

        Exception? caught = null;

        // An application has to be running: a detached tree has yet to see the whole scope chain, so a
        // name that is missing there is retried rather than reported, exactly as StyleName is.
        TestPlatformHosts.Queue.Enqueue(new StyleHost((_, mainWindow) =>
        {
            try
            {
                mainWindow.PerformLayout();
            }
            catch (Exception error)
            {
                caught = error;
            }
        }));

        Application.Run(window);

        // Silence would leave the button on its default style and the typo unnoticed.
        Assert.IsInstanceOfType<InvalidOperationException>(caught, "a missing rule name went unreported");
        Assert.Contains("no-such-style", caught!.Message);
    }

    private sealed class StyleHost(Action<Application, Window> onRun) : IPlatformHost
    {
        public IMessageBoxService MessageBox => null!;
        public IFileDialogService FileDialog => null!;
        public IClipboardService Clipboard => null!;
        public string DefaultFontFamily => "Arial";
        public IReadOnlyList<string> DefaultFontFallbacks => [];
        public IWindowBackend CreateWindowBackend(Window window) => throw new NotSupportedException();
        public IDispatcher CreateDispatcher(nint windowHandle) => throw new NotSupportedException();
        public uint GetSystemDpi() => 96;
        public ThemeVariant GetSystemThemeVariant() => ThemeVariant.Light;
        public uint GetDpiForWindow(nint windowHandle) => 96;
        public bool EnablePerMonitorDpiAwareness() => false;
        public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;
        public void Run(Application app, Window? mainWindow) => onRun(app, mainWindow!);
        public void Quit(Application app) { }
        public void DoEvents() { }
        public void Dispose() { }
    }

    [TestMethod]
    public void AControlsOwnStyleNameStillWinsOverTheRule()
    {
        var button = new Button { Content = new TextBlock { Text = "Named" }, StyleName = "test-blue" };
        var panel = new StackPanel()
            .Vertical()
            .StyleSheet(new StyleSheet().WithName<Button>("test-button"))
            .Children(button);

        var window = HeadlessWindow.Create();
        window.StyleSheet = new StyleSheet()
            .With("test-button", RedButton)
            .With("test-blue", BlueButton);
        window.Content = panel;
        window.PerformLayout();
        window.ForceStyleSnap();

        Assert.AreEqual(Color.Blue, button.Background, "the rule overrode the control's own style name");

        window.Close();
    }
}
