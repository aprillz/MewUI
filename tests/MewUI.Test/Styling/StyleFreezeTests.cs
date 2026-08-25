using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Styling;

[TestClass]
public sealed class StyleFreezeTests
{
    [TestMethod]
    public void Freeze_SnapshotsStyleAndTriggerCollections()
    {
        var setters = new List<SetterBase>
        {
            Setter.Create(Control.BackgroundProperty, Color.FromRgb(1, 2, 3)),
        };
        var triggerSetters = new List<SetterBase>
        {
            Setter.Create(Control.BorderThicknessProperty, 1.0),
        };
        var trigger = new StateTrigger
        {
            Match = VisualStateFlags.Hot,
            Setters = triggerSetters,
        };
        var triggers = new List<StateTrigger> { trigger };
        var style = new Style(typeof(Border))
        {
            Setters = setters,
            Triggers = triggers,
        };
        var sheet = new StyleSheet();
        sheet.Define<Border>(style);

        sheet.Freeze();
        setters.Add(Setter.Create(FrameworkElement.WidthProperty, 100.0));
        triggerSetters.Add(Setter.Create(FrameworkElement.HeightProperty, 100.0));
        triggers.Clear();

        Assert.HasCount(1, style.Setters);
        Assert.HasCount(1, style.Triggers);
        Assert.HasCount(1, trigger.Setters);
    }

    [TestMethod]
    public void Freeze_AllowsPropertiesRegisteredByBaseOwnerTypes()
    {
        var sheet = new StyleSheet();
        sheet.Define<Border>(new Style(typeof(Border))
        {
            Setters =
            [
                Setter.Create(FrameworkElement.MinHeightProperty, 10.0),
                Setter.Create(TextElement.ForegroundProperty, Color.White),
            ],
        });

        sheet.Freeze();

        Assert.IsTrue(sheet.IsFrozen);
    }

    [TestMethod]
    public void Freeze_RejectsSetterFromUnrelatedPropertyOwner()
    {
        var sheet = new StyleSheet();
        sheet.Define<Border>(new Style(typeof(Border))
        {
            Setters = [Setter.Create(Button.ContentProperty, (Element?)null)],
        });

        var exception = Assert.ThrowsExactly<InvalidOperationException>(sheet.Freeze);

        StringAssert.Contains(exception.Message, nameof(Button.Content));
        StringAssert.Contains(exception.Message, typeof(Border).FullName!);
        Assert.IsFalse(sheet.IsFrozen);
    }

    [TestMethod]
    public void Freeze_RejectsIncompatibleBasedOnTarget()
    {
        var sheet = new StyleSheet();
        sheet.Define<Border>(new Style(typeof(Border))
        {
            BasedOn = new Style(typeof(Button)),
        });

        var exception = Assert.ThrowsExactly<InvalidOperationException>(sheet.Freeze);

        StringAssert.Contains(exception.Message, typeof(Button).FullName!);
        StringAssert.Contains(exception.Message, typeof(Border).FullName!);
    }

    [TestMethod]
    public void Freeze_RejectsBasedOnCycle()
    {
        var first = new Style(typeof(Border));
        var second = new Style(typeof(Border)) { BasedOn = first };
        typeof(Style).GetField("_basedOn", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(first, second);
        var sheet = new StyleSheet();
        sheet.Define<Border>(first);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(sheet.Freeze);

        StringAssert.Contains(exception.Message, "BasedOn cycle");
    }

    [TestMethod]
    public void Freeze_RejectsOverlappingAndUndefinedTriggerMasks()
    {
        var overlapping = new StyleSheet();
        overlapping.Define<Border>(new Style(typeof(Border))
        {
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Exclude = VisualStateFlags.Hot,
                    Setters = [],
                },
            ],
        });
        var overlapException = Assert.ThrowsExactly<InvalidOperationException>(overlapping.Freeze);
        StringAssert.Contains(overlapException.Message, "requires and excludes");

        var undefined = new StyleSheet();
        undefined.Define<Border>(new Style(typeof(Border))
        {
            Triggers =
            [
                new StateTrigger
                {
                    Match = (VisualStateFlags)(1u << 31),
                    Setters = [],
                },
            ],
        });
        var undefinedException = Assert.ThrowsExactly<InvalidOperationException>(undefined.Freeze);
        StringAssert.Contains(undefinedException.Message, "undefined VisualStateFlags");
    }

    [TestMethod]
    public void Freeze_RejectsTransitionFromUnrelatedPropertyOwner()
    {
        var sheet = new StyleSheet();
        sheet.Define<Border>(new Style(typeof(Border))
        {
            Transitions = [Transition.Create(Button.ContentProperty)],
        });

        var exception = Assert.ThrowsExactly<InvalidOperationException>(sheet.Freeze);

        StringAssert.Contains(exception.Message, nameof(Button.Content));
    }

    [TestMethod]
    public void Transition_RejectsNonPositiveDurationAtConstruction()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new Transition(Control.BackgroundProperty, TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new Transition(Control.BackgroundProperty, TimeSpan.FromMilliseconds(-1)));
    }
}
