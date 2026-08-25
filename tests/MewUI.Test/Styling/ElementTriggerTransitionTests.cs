using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Styling;

/// <summary>
/// Element triggers and transitions live on the element instance, not on a Style, so they must work
/// on elements that are not controls and must compose: a trigger switches the value, a transition
/// animates the switch, and leaving the condition restores whatever is underneath.
/// </summary>
[TestClass]
public sealed class ElementTriggerTransitionTests
{
    private static StackPanel Parent(Element child)
    {
        var parent = new StackPanel();
        parent.Add(child);
        return parent;
    }

    private static ElementTrigger DimTrigger(double opacity = 0.5)
        => ElementTrigger.When(UIElement.IsEffectivelyEnabledProperty, false,
            Setter.Create(UIElement.OpacityProperty, opacity));

    [TestMethod]
    public void Trigger_Applies_WhenConditionAlreadyHolds()
    {
        var image = new Image();
        var root = new StackPanel { IsEnabled = false };
        root.Add(image);

        image.Triggers = [DimTrigger()];

        Assert.AreEqual(0.5, image.Opacity);
    }

    [TestMethod]
    public void Trigger_Applies_WhenConditionBecomesTrue()
    {
        var image = new Image { Triggers = [DimTrigger()] };
        var root = Parent(image);

        root.IsEnabled = false;

        Assert.AreEqual(0.5, image.Opacity);
    }

    [TestMethod]
    public void Trigger_Restores_WhenConditionLeaves()
    {
        var image = new Image { Triggers = [DimTrigger()] };
        var root = Parent(image);

        root.IsEnabled = false;
        root.IsEnabled = true;

        Assert.AreEqual(1.0, image.Opacity, "leaving the condition reveals the value underneath");
    }

    [TestMethod]
    public void LaterTrigger_Wins_ForTheSameProperty()
    {
        var image = new Image
        {
            Triggers =
            [
                DimTrigger(0.5),
                DimTrigger(0.25),
            ],
        };
        var root = Parent(image);

        root.IsEnabled = false;

        Assert.AreEqual(0.25, image.Opacity, "declaration order decides, no specificity");
    }

    [TestMethod]
    public void ReplacingTheTriggerList_RemovesItsEffects()
    {
        var image = new Image { Triggers = [DimTrigger()] };
        var root = Parent(image);
        root.IsEnabled = false;
        Assert.AreEqual(0.5, image.Opacity);

        image.Triggers = null;

        Assert.AreEqual(1.0, image.Opacity, "the old list's applied values must not survive it");
    }

    [TestMethod]
    public void ReplacingTheTriggerList_ChangesAWinningPropertyOnce()
    {
        var element = new TrackingElement { Triggers = [DimTrigger()] };
        var root = Parent(element);
        root.IsEnabled = false;
        Assert.AreEqual(0.5, element.Opacity);
        element.ResetOpacityChanges();

        element.Triggers = [DimTrigger(0.25)];

        Assert.AreEqual(0.25, element.Opacity);
        Assert.AreEqual(1, element.OpacityChanges, "the old winner is replaced without exposing the base value");
    }

    [TestMethod]
    public void ReplacingTheTriggerList_WithTheSameWinnerDoesNotNotify()
    {
        var element = new TrackingElement { Triggers = [DimTrigger()] };
        var root = Parent(element);
        root.IsEnabled = false;
        element.ResetOpacityChanges();

        element.Triggers = [DimTrigger()];

        Assert.AreEqual(0.5, element.Opacity);
        Assert.AreEqual(0, element.OpacityChanges);
    }

    [TestMethod]
    public void TriggerValue_SitsBelowLocal()
    {
        var image = new Image { Triggers = [DimTrigger()] };
        var root = Parent(image);
        root.IsEnabled = false;

        image.Opacity = 0.8;

        Assert.AreEqual(0.8, image.Opacity, "a local value outranks a trigger value");
    }

    [TestMethod]
    public void UnsetSetter_IsAConfigurationError()
    {
        var image = new Image();

        Assert.ThrowsExactly<ArgumentException>(() =>
            image.Triggers =
            [
                new ElementTrigger
                {
                    Property = UIElement.IsEffectivelyEnabledProperty,
                    Value = false,
                    Setters = [Setter.Unset(UIElement.OpacityProperty)],
                },
            ]);
    }

    [TestMethod]
    public void MismatchedConditionValueType_IsAConfigurationError()
    {
        var image = new Image();

        Assert.ThrowsExactly<ArgumentException>(() =>
            image.Triggers =
            [
                new ElementTrigger
                {
                    Property = UIElement.IsEffectivelyEnabledProperty,   // bool
                    Value = "false",                                     // string
                    Setters = [Setter.Create(UIElement.OpacityProperty, 0.5)],
                },
            ]);
    }

    [TestMethod]
    public void NullConditionValue_IsOnlyValidForNullableProperties()
    {
        var image = new Image();

        Assert.ThrowsExactly<ArgumentException>(() =>
            image.Triggers =
            [
                new ElementTrigger
                {
                    Property = UIElement.IsEffectivelyEnabledProperty,   // non-nullable bool
                    Value = null,
                    Setters = [Setter.Create(UIElement.OpacityProperty, 0.5)],
                },
            ]);
    }

    [TestMethod]
    public void SetterTargetingAConditionProperty_IsAConfigurationError()
    {
        // Self/mutual/chain references would re-enter evaluation; all are rejected up front.
        var image = new Image();

        Assert.ThrowsExactly<ArgumentException>(() =>
            image.Triggers =
            [
                DimTrigger(),
                ElementTrigger.When(UIElement.OpacityProperty, 0.5,      // condition on Opacity...
                    Setter.Create(UIElement.IsVisibleProperty, false)),
                ElementTrigger.When(UIElement.IsEffectivelyEnabledProperty, false,
                    Setter.Create(UIElement.OpacityProperty, 0.25)),      // ...which this sets
            ]);
    }

    [TestMethod]
    public void TriggerShadowsBinding_AndRevealsItsLatestValueWhenConditionLeaves()
    {
        // The combination is allowed and fully defined by tier priority: ElementTrigger sits above
        // Binding, whose candidate continues to update underneath.
        var image = new Image { Triggers = [DimTrigger()] };
        image.Bind(UIElement.OpacityProperty, image, UIElement.IsEffectivelyEnabledProperty,
            (bool enabled) => enabled ? 1.0 : 0.7);
        var root = Parent(image);

        root.IsEnabled = false;
        Assert.AreEqual(0.5, image.Opacity, "the active trigger shadows Binding");

        root.IsEnabled = true;

        Assert.AreEqual(1.0, image.Opacity, "leaving the trigger reveals the latest Binding value");
    }

    [TestMethod]
    public void MutatingTheOriginalList_AfterAssignment_IsNotObserved()
    {
        var image = new Image();
        var original = new List<ElementTrigger> { DimTrigger() };
        image.Triggers = original;
        var root = Parent(image);

        original.Clear();   // snapshot semantics: the declaration set is what was assigned
        root.IsEnabled = false;

        Assert.AreEqual(0.5, image.Opacity);
        Assert.HasCount(1, image.Triggers!);
    }

    [TestMethod]
    public void Transition_SnapsWhileDetached()
    {
        // Object-initializer assignments and other detached writes are not visible changes.
        var image = new Image { Transitions = [Transition.Create(UIElement.OpacityProperty, 150)] };

        image.Opacity = 0.9;

        Assert.AreEqual(0.9, image.Opacity);
    }

    [TestMethod]
    public void Transition_AnimatesALocalSet_WhenAttached()
    {
        var image = new Image { Transitions = [Transition.Create(UIElement.OpacityProperty, 150)] };
        var root = Parent(image);

        image.Opacity = 0.1;

        Assert.AreEqual(1.0, image.Opacity, "the visible default is the from-value; no snap on first set");
    }

    [TestMethod]
    public void Transition_AnimatesABindingPush()
    {
        var image = new Image { Transitions = [Transition.Create(UIElement.OpacityProperty, 150)] };
        image.Bind(UIElement.OpacityProperty, image, UIElement.IsEffectivelyEnabledProperty,
            (bool enabled) => enabled ? 1.0 : 0.5);
        var root = Parent(image);
        Assert.AreEqual(1.0, image.Opacity);

        root.IsEnabled = false;   // binding pushes 0.5; the transition animates it

        Assert.AreEqual(1.0, image.Opacity, "the push starts an animation from the previous value");
    }

    [TestMethod]
    public void Transition_AnimatesATriggerApply()
    {
        var image = new Image
        {
            Transitions = [Transition.Create(UIElement.OpacityProperty, 150)],
            Triggers = [DimTrigger()],
        };
        var root = Parent(image);

        root.IsEnabled = false;

        Assert.AreEqual(1.0, image.Opacity, "the trigger's apply animates from the previous value");
    }

    [TestMethod]
    public void Detach_ReevaluatesTheCondition_AndSnaps()
    {
        var image = new Image
        {
            Transitions = [Transition.Create(UIElement.OpacityProperty, 150)],
            Triggers = [DimTrigger()],
        };
        var root = Parent(image);
        root.IsEnabled = false;

        root.Remove(image);   // no longer under a disabled ancestor: the condition stops holding

        Assert.AreEqual(1.0, image.Opacity, "detach is an ordinary re-evaluation, snapped");
    }

    [TestMethod]
    public void Transition_AnimatesTheReveal_WhenATriggerLeaves()
    {
        var image = new Image { Triggers = [DimTrigger()] };
        var root = Parent(image);
        root.IsEnabled = false;                        // no transition yet: settles at 0.5
        Assert.AreEqual(0.5, image.Opacity);

        image.Transitions = [Transition.Create(UIElement.OpacityProperty, 150)];
        root.IsEnabled = true;                         // trigger leaves: reveal 1.0, animated from 0.5

        Assert.AreEqual(0.5, image.Opacity, "the reveal animates from what was showing");
    }

    [TestMethod]
    public void UnrelatedProperties_AreNotAnimated()
    {
        var image = new Image { Transitions = [Transition.Create(UIElement.OpacityProperty, 150)] };

        image.Width = 10;
        image.Width = 20;

        Assert.AreEqual(20, image.Width, "only registered properties animate");
    }

    [TestMethod]
    public void NonControlElements_SupportTriggersAndTransitions()
    {
        // The reason these live on UIElement: TextBlock and shapes have no Style resolver at all.
        var text = new TextBlock { Triggers = [DimTrigger()] };
        var root = Parent(text);

        root.IsEnabled = false;

        Assert.AreEqual(0.5, text.Opacity);
    }

    private sealed class TrackingElement : FrameworkElement
    {
        public int OpacityChanges { get; private set; }

        public void ResetOpacityChanges() => OpacityChanges = 0;

        protected override void OnMewPropertyChanged(MewProperty property)
        {
            base.OnMewPropertyChanged(property);
            if (property == OpacityProperty)
            {
                OpacityChanges++;
            }
        }
    }
}
