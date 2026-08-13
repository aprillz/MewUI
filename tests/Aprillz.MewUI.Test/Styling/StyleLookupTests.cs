using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;

namespace MewUI.Test.Styling;

[TestClass]
[DoNotParallelize]
public sealed class StyleLookupTests
{
    private static readonly Color SelfColor = Color.FromRgb(10, 20, 30);
    private static readonly Color ParentColor = Color.FromRgb(40, 50, 60);
    private static readonly Color ApplicationColor = Color.FromRgb(70, 80, 90);

    [TestMethod]
    public void NamedAndTypeLookup_UseTheSameSelfParentApplicationScopeOrder()
    {
        using var application = new ApplicationScope();
        var selfNamed = StyleWithBackground(typeof(LookupControl), SelfColor);
        var parentNamed = StyleWithBackground(typeof(LookupControl), ParentColor);
        var applicationNamed = StyleWithBackground(typeof(LookupControl), ApplicationColor);
        var selfTyped = StyleWithWidth(typeof(LookupControl), 10);
        var parentTyped = StyleWithWidth(typeof(LookupControl), 20);
        var applicationTyped = StyleWithWidth(typeof(LookupControl), 30);

        application.StyleSheet.Define("named", () => applicationNamed);
        application.StyleSheet.Define<LookupControl>(applicationTyped);

        var parentSheet = new StyleSheet();
        parentSheet.Define("named", () => parentNamed);
        parentSheet.Define<LookupControl>(parentTyped);
        var parent = new Border { StyleSheet = parentSheet };

        var selfSheet = new StyleSheet();
        selfSheet.Define("named", () => selfNamed);
        selfSheet.Define<LookupControl>(selfTyped);
        var child = new LookupControl { StyleSheet = selfSheet };
        parent.Child = child;

        Assert.AreSame(
            selfNamed,
            StyleScopeResolver.Resolve(child, "named", application.StyleSheet));
        Assert.AreSame(
            selfTyped,
            StyleScopeResolver.Resolve(child, styleName: null, application.StyleSheet));

        child.StyleSheet = null;
        Assert.AreSame(
            parentNamed,
            StyleScopeResolver.Resolve(child, "named", application.StyleSheet));
        Assert.AreSame(
            parentTyped,
            StyleScopeResolver.Resolve(child, styleName: null, application.StyleSheet));

        parent.StyleSheet = null;
        Assert.AreSame(
            applicationNamed,
            StyleScopeResolver.Resolve(child, "named", application.StyleSheet));
        Assert.AreSame(
            applicationTyped,
            StyleScopeResolver.Resolve(child, styleName: null, application.StyleSheet));
    }

    [TestMethod]
    public void ApplicationNamedAndTypeRules_AreAppliedBeforeDefaults()
    {
        using var application = new ApplicationScope();
        application.StyleSheet.Define(
            "application",
            () => StyleWithBackground(typeof(LookupControl), ApplicationColor));
        application.StyleSheet.Define<LookupControl>(
            StyleWithWidth(typeof(LookupControl), 42));

        var namedWindow = new Window();
        var named = new LookupControl { StyleName = "application" };
        namedWindow.Content = named;
        Assert.AreEqual(ApplicationColor, named.Background);
        Assert.IsTrue(double.IsNaN(named.Width),
            "an explicit named style does not also pick up the matching type rule");

        var typedWindow = new Window();
        var typed = new LookupControl();
        typedWindow.Content = typed;
        Assert.AreEqual(42, typed.Width);
    }

    [TestMethod]
    public void MissingStyleName_DefersDetachedAndThrowsWhenFullAttachedScopeIsAvailable()
    {
        var sheet = new StyleSheet();
        var detached = new LookupControl { StyleSheet = sheet, StyleName = "late" };

        detached.Measure(new Size(100, 100));
        sheet.Define(
            "late",
            () => StyleWithBackground(typeof(LookupControl), SelfColor));
        var resolvedWindow = new Window { Content = detached };
        Assert.AreEqual(SelfColor, detached.Background);

        using var application = new ApplicationScope();
        var attached = new LookupControl { StyleName = "missing" };
        var window = new Window();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => window.Content = attached);
        StringAssert.Contains(exception.Message, "missing");
        StringAssert.Contains(exception.Message, typeof(LookupControl).FullName!);
        StringAssert.Contains(exception.Message, nameof(Application));
    }

    [TestMethod]
    public void ResolvedStyleTargetType_MustAcceptTheControlType()
    {
        var sheet = new StyleSheet();
        sheet.Define("wrong", () => new Style(typeof(Button)));
        var child = new LookupControl { StyleName = "wrong" };
        var parent = new Border { StyleSheet = sheet, Child = child };
        var window = new Window();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => window.Content = parent);
        StringAssert.Contains(exception.Message, typeof(Button).FullName!);
        StringAssert.Contains(exception.Message, typeof(LookupControl).FullName!);
    }

    [TestMethod]
    public void TypeRuleStyleSheet_CanBeRemovedAndReappliedWhileAttached()
    {
        var sheet = new StyleSheet();
        sheet.Define<LookupControl>(StyleWithWidth(typeof(LookupControl), 42));

        var child = new LookupControl();
        var scope = new Border { StyleSheet = sheet, Child = child };
        var window = new Window { Content = scope };

        Assert.AreEqual(42, child.Width);

        scope.StyleSheet = null;

        Assert.IsTrue(double.IsNaN(child.Width), "removing the type rule restores the default Width");

        scope.StyleSheet = sheet;

        Assert.AreEqual(42, child.Width, "the same frozen sheet can be reapplied");
    }

    private static Style StyleWithBackground(Type targetType, Color value)
        => new(targetType)
        {
            Setters = [Setter.Create(Control.BackgroundProperty, value)],
        };

    private static Style StyleWithWidth(Type targetType, double value)
        => new(targetType)
        {
            Setters = [Setter.Create(FrameworkElement.WidthProperty, value)],
        };

    private sealed class LookupControl : ContentControl
    {
    }

    private sealed class ApplicationScope : IDisposable
    {
        private static readonly FieldInfo CurrentField = typeof(Application).GetField(
            "_current",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        private readonly Application? _previous;

        public ApplicationScope()
        {
            _previous = (Application?)CurrentField.GetValue(null);
            Assert.IsNull(_previous, "ApplicationScope requires no active Application.");
            var application = (Application)Activator.CreateInstance(
                typeof(Application),
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                args: [new TestPlatformHost()],
                culture: null)!;
            CurrentField.SetValue(null, application);
            StyleSheet = application.StyleSheet;
        }

        public StyleSheet StyleSheet { get; }

        public void Dispose() => CurrentField.SetValue(null, _previous);
    }

    private sealed class TestPlatformHost : IPlatformHost
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
        public void Run(Application app, Window? mainWindow) { }
        public void Quit(Application app) { }
        public void DoEvents() { }
        public void Dispose() { }
    }
}
