using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Styling;

[TestClass]
public sealed class StyleSheetTests
{
    [TestMethod]
    public void DefineFactory_DoesNotCreateStyleUntilLookup()
    {
        var sheet = new StyleSheet();
        var style = new Style(typeof(Button));
        var calls = 0;

        sheet.Define("lazy", () =>
        {
            calls++;
            return style;
        });

        Assert.AreEqual(0, calls);

        Assert.AreSame(style, sheet.Get("lazy"));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void DefineFactory_CachesCreatedStyle()
    {
        var sheet = new StyleSheet();
        var calls = 0;

        sheet.Define("lazy", () =>
        {
            calls++;
            return new Style(typeof(Button));
        });

        var first = sheet.Get("lazy");
        var second = sheet.Get("lazy");

        Assert.IsNotNull(first);
        Assert.AreSame(first, second);
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void DefineFactory_ReplacesPendingFactory()
    {
        var sheet = new StyleSheet();
        var calls = 0;
        var replacement = new Style(typeof(Button));

        sheet.Define("style", () =>
        {
            calls++;
            return new Style(typeof(Button));
        });
        sheet.Define("style", () => replacement);

        Assert.AreSame(replacement, sheet.Get("style"));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void Freeze_DoesNotMaterializeNamedFactories_AndRejectsFurtherDefinitions()
    {
        var sheet = new StyleSheet();
        var calls = 0;
        sheet.Define("lazy", () =>
        {
            calls++;
            return new Style(typeof(Button));
        });

        sheet.Freeze();

        Assert.IsTrue(sheet.IsFrozen);
        Assert.AreEqual(0, calls);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => sheet.Define("other", () => new Style(typeof(Button))));
        Assert.ThrowsExactly<InvalidOperationException>(
            () => sheet.Define<Button>(new Style(typeof(Button))));
        Assert.IsNotNull(sheet.Get("lazy"));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void OrdinaryLookup_DoesNotFreezeAStillConfigurableSheet()
    {
        var sheet = new StyleSheet();
        sheet.Define("first", () => new Style(typeof(Button)));

        Assert.IsNotNull(sheet.Get("first"));

        Assert.IsFalse(sheet.IsFrozen);
        sheet.Define("second", () => new Style(typeof(Button)));
        Assert.IsNotNull(sheet.Get("second"));
    }

    [TestMethod]
    public async Task DefineFactory_ConcurrentFirstLookup_MaterializesExactlyOnce()
    {
        var sheet = new StyleSheet();
        var expected = new Style(typeof(Button));
        var calls = 0;
        sheet.Define("shared", () =>
        {
            Interlocked.Increment(ref calls);
            Thread.Sleep(20);
            return expected;
        });
        sheet.Freeze();

        var tasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => sheet.Get("shared")))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.AreEqual(1, calls);
        Assert.IsTrue(results.All(style => ReferenceEquals(expected, style)));
    }

    [TestMethod]
    public void DefineFactory_FailureIsCachedPerName()
    {
        var sheet = new StyleSheet();
        var calls = 0;
        sheet.Define("broken", () =>
        {
            calls++;
            throw new InvalidOperationException("factory failed");
        });
        sheet.Freeze();

        var first = Assert.ThrowsExactly<InvalidOperationException>(() => sheet.Get("broken"));
        var second = Assert.ThrowsExactly<InvalidOperationException>(() => sheet.Get("broken"));

        Assert.AreEqual(1, calls);
        Assert.AreEqual("factory failed", first.Message);
        Assert.AreEqual(first.Message, second.Message);
    }

    [TestMethod]
    public void DefineFactory_ReentrantLookupFailsAndCachesTheFailure()
    {
        var sheet = new StyleSheet();
        var calls = 0;
        sheet.Define("cycle", () =>
        {
            calls++;
            return sheet.Get("cycle")!;
        });
        sheet.Freeze();

        var first = Assert.ThrowsExactly<InvalidOperationException>(() => sheet.Get("cycle"));
        var second = Assert.ThrowsExactly<InvalidOperationException>(() => sheet.Get("cycle"));

        Assert.AreEqual(1, calls);
        StringAssert.Contains(first.Message, "recursively requested itself");
        Assert.AreEqual(first.Message, second.Message);
    }

    [TestMethod]
    public void InvalidateLazyCache_RecreatesNamedStyleButKeepsSheetFrozen()
    {
        var sheet = new StyleSheet();
        var calls = 0;
        sheet.Define("reloadable", () =>
        {
            calls++;
            return new Style(typeof(Button));
        });
        sheet.Freeze();
        var first = sheet.Get("reloadable");

        sheet.InvalidateLazyCache();
        var second = sheet.Get("reloadable");

        Assert.IsTrue(sheet.IsFrozen);
        Assert.AreEqual(2, calls);
        Assert.AreNotSame(first, second);
        Assert.ThrowsExactly<InvalidOperationException>(
            () => sheet.Define("late", () => new Style(typeof(Button))));
    }

    [TestMethod]
    public void InvalidateLazyCache_AllowsFailedFactoryToRetryAfterHotReload()
    {
        var sheet = new StyleSheet();
        var shouldFail = true;
        var calls = 0;
        sheet.Define("reloadable", () =>
        {
            calls++;
            if (shouldFail)
            {
                throw new InvalidOperationException("old failure");
            }

            return new Style(typeof(Button));
        });
        sheet.Freeze();
        Assert.ThrowsExactly<InvalidOperationException>(() => sheet.Get("reloadable"));

        shouldFail = false;
        sheet.InvalidateLazyCache();

        Assert.IsNotNull(sheet.Get("reloadable"));
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public void GetByType_SelectsNearestBaseRegardlessOfRegistrationOrder()
    {
        var sheet = new StyleSheet();
        var nearest = new Style(typeof(LookupBase));
        var farther = new Style(typeof(Control));
        sheet.Define<LookupBase>(nearest);
        sheet.Define<Control>(farther);

        Assert.AreSame(nearest, sheet.GetByType(typeof(LookupDerived)));
    }

    [TestMethod]
    public void GetByType_UsesTheLastRuleForTheSameType()
    {
        var sheet = new StyleSheet();
        var first = new Style(typeof(LookupBase));
        var second = new Style(typeof(LookupBase));
        sheet.Define<LookupBase>(first);
        sheet.Define<LookupBase>(second);

        Assert.AreSame(second, sheet.GetByType(typeof(LookupDerived)));
    }

    [TestMethod]
    public void StyleAndTypeLookup_RejectNonControlTypes()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new Style(typeof(string)));
        Assert.ThrowsExactly<ArgumentException>(() => Style.ForType(typeof(string)));

        var sheet = new StyleSheet();
        Assert.ThrowsExactly<ArgumentException>(() => sheet.GetByType(typeof(string)));
    }

    [TestMethod]
    public void DefineTypeRule_RejectsAnIncompatibleStyleTarget()
    {
        var sheet = new StyleSheet();

        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => sheet.Define<LookupBase>(new Style(typeof(Button))));

        StringAssert.Contains(exception.Message, typeof(Button).FullName!);
        StringAssert.Contains(exception.Message, typeof(LookupBase).FullName!);
    }

    [TestMethod]
    public void DeriveFromDefault_UsesExactDefaultWhenRegistered()
    {
        var style = Style.DeriveFromDefault<Button>(
            setters: [Setter.Create(Control.BorderThicknessProperty, 0.0)]);

        Assert.AreEqual(typeof(Button), style.TargetType);
        Assert.AreSame(Style.ForType<Button>(), style.BasedOn);
        Assert.HasCount(1, style.Setters);
    }

    [TestMethod]
    public void DeriveFromDefault_UsesNearestControlBaseForCustomControl()
    {
        var style = Style.DeriveFromDefault<LookupDerived>();

        Assert.AreEqual(typeof(LookupDerived), style.TargetType);
        Assert.AreSame(Style.ForType<Control>(), style.BasedOn);
        Assert.IsEmpty(style.Setters);
        Assert.IsEmpty(style.Triggers);
        Assert.IsEmpty(style.Transitions);
    }

    [TestMethod]
    public void FrameworkNamedStyles_UseExplicitNearestDefaultBases()
    {
        var sheet = new StyleSheet();
        BuiltInStyles.Register(sheet);
        FileDialogStyles.Register(sheet);

        Assert.AreEqual(typeof(Button), sheet.Get(BuiltInStyles.FlatButton)!.BasedOn!.TargetType);
        Assert.AreEqual(typeof(ListBox), sheet.Get(BuiltInStyles.ComboBoxPopup)!.BasedOn!.TargetType);
        Assert.AreEqual(typeof(Calendar), sheet.Get(BuiltInStyles.DatePickerPopup)!.BasedOn!.TargetType);
        Assert.AreEqual(typeof(TextBase), sheet.Get(FileDialogStyles.NullTextBox)!.BasedOn!.TargetType);
    }

    [TestMethod]
    public void FrameworkNamedStyleKeys_RegisterFactoriesForCustomControls()
    {
        string flatButton = BuiltInStyles.FlatButton;
        string comboBoxPopup = BuiltInStyles.ComboBoxPopup;
        string datePickerPopup = BuiltInStyles.DatePickerPopup;
        var sheet = new StyleSheet { UsesFrameworkNamedStyles = true };

        Assert.AreEqual(typeof(Button), sheet.Get(flatButton)!.BasedOn!.TargetType);
        Assert.AreEqual(typeof(ListBox), sheet.Get(comboBoxPopup)!.BasedOn!.TargetType);
        Assert.AreEqual(typeof(Calendar), sheet.Get(datePickerPopup)!.BasedOn!.TargetType);
    }

    [TestMethod]
    public void ControlBasedDefaults_ShareTheTriggerlessControlBase()
    {
        var controlBase = Style.ForType<Control>();
        Type[] derivedTypes =
        [
            typeof(CheckBox),
            typeof(RadioButton),
            typeof(NumericUpDown),
            typeof(ItemsControl),
            typeof(ScrollableItemsBase),
            typeof(TreeView),
            typeof(GridView),
        ];

        foreach (var type in derivedTypes)
        {
            var style = Style.ForType(type);
            Assert.IsNotNull(style);
            Assert.AreSame(controlBase, style.BasedOn, type.Name);
            Assert.IsFalse(
                style.Setters.Any(setter =>
                    setter.Property == Control.CornerRadiusProperty ||
                    setter.Property == Control.BorderThicknessProperty),
                $"{type.Name} should inherit shared chrome metrics instead of redeclaring them.");
        }
    }

    [TestMethod]
    public void InputDefaults_EndWithValidationBorderTrigger()
    {
        Type[] inputTypes =
        [
            typeof(CheckBox),
            typeof(RadioButton),
            typeof(ToggleButton),
            typeof(ToggleSwitch),
            typeof(NumericUpDown),
            typeof(Slider),
            typeof(Calendar),
            typeof(TextBase),
            typeof(DropDownBase),
            typeof(ListBox),
            typeof(SegmentedControl),
        ];

        foreach (var type in inputTypes)
        {
            var style = Style.ForType(type);
            Assert.IsNotNull(style);
            var trigger = style.Triggers[^1];
            Assert.AreEqual(VisualStateFlags.Invalid, trigger.Match, type.Name);
            Assert.AreEqual(VisualStateFlags.None, trigger.Exclude, type.Name);
            Assert.HasCount(1, trigger.Setters, type.Name);
            Assert.AreSame(Control.BorderBrushProperty, trigger.Setters[0].Property, type.Name);
        }
    }

    [TestMethod]
    public void Palette_UsesSeedErrorColorOrCrimsonDefault()
    {
        var error = Color.FromRgb(12, 34, 56);
        var palette = new Palette(
            ThemeSeed.DefaultLight with { Error = error },
            Color.FromRgb(1, 2, 3));
        var defaultLight = new Palette(
            ThemeSeed.DefaultLight with { Error = null },
            Color.FromRgb(1, 2, 3));
        var defaultDark = new Palette(
            ThemeSeed.DefaultDark with { Error = null },
            Color.FromRgb(1, 2, 3));

        Assert.AreEqual(error, palette.Error);
        Assert.AreEqual(Color.FromRgb(220, 20, 60), defaultLight.Error);
        Assert.AreEqual(Color.FromRgb(220, 20, 60), defaultDark.Error);
    }

    private class LookupBase : Control
    {
    }

    private sealed class LookupDerived : LookupBase
    {
    }
}
