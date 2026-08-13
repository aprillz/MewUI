using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Styling;

[TestClass]
public sealed class StyleResolverTests
{
    private static readonly MewProperty<double> ValueProperty =
        MewProperty<double>.Register<TestControl>(nameof(TestControl.Value), 0.0);

    private static readonly Color AMBIENT = Color.FromRgb(245, 245, 245);
    private static readonly Color DISABLED = Color.FromRgb(110, 110, 110);

    [TestMethod]
    public void StyleCandidate_UsesStyleSlotBelowElementTrigger()
    {
        var control = new TestControl
        {
            Triggers =
            [
                ElementTrigger.When(
                    UIElement.IsEffectivelyEnabledProperty,
                    false,
                    Setter.Create(ValueProperty, 30.0)),
            ],
        };
        var parent = new StackPanel();
        parent.Add(control);

        control.SetStyle(new Style(typeof(TestControl))
        {
            Setters = [Setter.Create(ValueProperty, 20.0)],
        });
        Assert.AreEqual(20.0, control.Value);
        Assert.AreEqual(ValueSource.Style, control.PropertyStore.GetSource(ValueProperty.Id));

        parent.IsEnabled = false;
        Assert.AreEqual(30.0, control.Value, "the element trigger owns the higher slot");

        control.ReconcileStyle(snap: true);

        Assert.AreEqual(30.0, control.Value, "a style reconciliation must not overwrite the element trigger");
        Assert.AreEqual(ValueSource.ElementTrigger, control.PropertyStore.GetSource(ValueProperty.Id));

        parent.IsEnabled = true;

        Assert.AreEqual(20.0, control.Value, "leaving the element trigger reveals the latest Style candidate");
        Assert.AreEqual(ValueSource.Style, control.PropertyStore.GetSource(ValueProperty.Id));
    }

    [TestMethod]
    public void StateTrigger_WritesTheStyleSource()
    {
        var control = new TestControl();
        control.SetStyle(PressedStyle(20.0));

        control.SetPressedState(true);
        control.ReconcileStyle(snap: true);

        Assert.AreEqual(20.0, control.Value);
        Assert.AreEqual(ValueSource.Style, control.PropertyStore.GetSource(ValueProperty.Id));
    }

    [TestMethod]
    public void ShadowedStyleUpdate_IsPreservedForLocalReveal()
    {
        var control = new TestControl();
        control.SetStyle(new Style(typeof(TestControl))
        {
            Setters = [Setter.Create(ValueProperty, 10.0)],
        });
        control.Value = 50.0;

        control.SetStyle(new Style(typeof(TestControl))
        {
            Setters = [Setter.Create(ValueProperty, 20.0)],
        });
        Assert.AreEqual(50.0, control.Value);

        control.PropertyStore.ClearLocalValue(ValueProperty);

        Assert.AreEqual(20.0, control.Value);
        Assert.AreEqual(ValueSource.Style, control.PropertyStore.GetSource(ValueProperty.Id));
    }

    [TestMethod]
    public void TriggerUnset_RemovesTheCurrentStyleCandidate()
    {
        var style = new Style(typeof(TestControl))
        {
            Setters = [Setter.Create(ValueProperty, 10.0)],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters = [Setter.Unset(ValueProperty)],
                },
            ],
        };
        var control = new TestControl();
        control.SetStyle(style);
        Assert.AreEqual(10.0, control.Value);

        control.SetPressedState(true);
        control.ReconcileStyle(snap: true);

        Assert.AreEqual(0.0, control.Value);
        Assert.AreEqual(ValueSource.Default, control.PropertyStore.GetSource(ValueProperty.Id));
    }

    [TestMethod]
    public void RemovingStyle_ClearsATriggerOnlyProperty()
    {
        var control = new TestControl();
        control.SetPressedState(true);
        control.SetStyle(PressedStyle(20.0));
        Assert.AreEqual(20.0, control.Value);

        control.SetStyle(null);

        Assert.AreEqual(0.0, control.Value);
        Assert.AreEqual(ValueSource.Default, control.PropertyStore.GetSource(ValueProperty.Id));
    }

    [TestMethod]
    public void FindTransition_LaterDeclarationWinsWithinTheSameStyle()
    {
        var first = Transition.Create(ValueProperty, 100);
        var last = Transition.Create(ValueProperty, 300);
        var style = new Style(typeof(TestControl))
        {
            Transitions = [first, last],
        };

        Assert.AreSame(last, style.FindTransition(ValueProperty.Id));
    }

    [TestMethod]
    public void StyleTransition_UsesApplicationThenFrameworkDefault()
    {
        var defaultTransition = Style.ForType<Button>()!
            .FindTransition(Control.BackgroundProperty.Id);
        Assert.IsNotNull(defaultTransition, "the Button default style supplies a background transition");

        var applicationTransition = Transition.Create(Control.BackgroundProperty, 777);
        var button = new Button();
        button.SetStyle(new Style(typeof(Button))
        {
            Transitions = [applicationTransition],
        });

        Assert.AreSame(applicationTransition, button.FindStyleTransition(Control.BackgroundProperty.Id),
            "the application layer wins when it defines the transition");

        button.SetStyle(new Style(typeof(Button)));
        Assert.AreSame(defaultTransition, button.FindStyleTransition(Control.BackgroundProperty.Id),
            "an omitted application transition falls through to the framework default");

        button.SetStyle(new Style(typeof(Button)) { OverridesDefaultStyle = true });
        Assert.IsNull(button.FindStyleTransition(Control.BackgroundProperty.Id),
            "a full replacement does not consult framework default transitions");
    }

    [TestMethod]
    public void ApplicationUnset_RemovesFrameworkDefaultCandidate()
    {
        var button = new Button();
        button.SetStyle(new Style(typeof(Button))
        {
            Setters = [Setter.Unset(Control.PaddingProperty)],
        });

        Assert.AreEqual(default, button.Padding,
            "Unset removes the lower framework-default Style candidate");
        Assert.AreEqual(ValueSource.Default,
            button.PropertyStore.GetSource(Control.PaddingProperty.Id));
    }

    [TestMethod]
    public void OverridesDefaultStyle_KeepsItsOwnBasedOnChain()
    {
        var baseStyle = new Style(typeof(Button))
        {
            Setters = [Setter.Create(Control.PaddingProperty, new Thickness(3))],
        };
        var button = new Button();
        button.SetStyle(new Style(typeof(Button))
        {
            OverridesDefaultStyle = true,
            BasedOn = baseStyle,
        });

        Assert.AreEqual(new Thickness(3), button.Padding,
            "full replacement skips only the runtime framework default, not explicit BasedOn");
        Assert.AreEqual(0.0, button.CornerRadius,
            "unmentioned framework defaults do not leak into a full replacement");
    }

    [TestMethod]
    public void DisabledTriggerExit_RevealsInheritedValueWithoutDefaultSentinel()
    {
        var style = new Style(typeof(TestControl))
        {
            Transitions = [Transition.Create(TextElement.ForegroundProperty, 300)],
            Triggers =
            [
                new StateTrigger
                {
                    Exclude = VisualStateFlags.Enabled,
                    Setters = [Setter.Create(TextElement.ForegroundProperty, DISABLED)],
                },
            ],
        };
        var parent = new Border { Foreground = AMBIENT };
        var control = new TestControl { IsEnabled = false };
        parent.Child = control;
        control.SetStyle(style);
        Assert.AreEqual(DISABLED, control.Foreground);

        control.IsEnabled = true;
        control.ReconcileStyle(snap: false);
        control.PropertyStore.ClearAnimatedValue(TextElement.ForegroundProperty.Id);

        Assert.AreEqual(AMBIENT, control.Foreground);
        Assert.AreEqual(ValueSource.Inherited, control.PropertyStore.GetSource(TextElement.ForegroundProperty.Id));
    }

#if DEBUG
    [TestMethod]
    public void StyleCascadeTrace_ReportsOrderActivityUnsetAndWinner()
    {
        var pressedTrigger = new StateTrigger
        {
            Match = VisualStateFlags.Pressed,
            Setters = [Setter.Create(ValueProperty, 20.0)],
        };
        var inactiveTrigger = new StateTrigger
        {
            Match = VisualStateFlags.Hot,
            Setters =
            [
                Setter.Create<double>(ValueProperty, static _ =>
                    throw new InvalidOperationException("Inactive resolvers must not run while tracing.")),
            ],
        };
        var winningTrigger = new StateTrigger
        {
            Match = VisualStateFlags.Pressed,
            Setters = [Setter.Create(ValueProperty, 30.0)],
        };
        var baseStyle = new Style(typeof(TestControl))
        {
            Setters = [Setter.Create(ValueProperty, 10.0)],
            Triggers = [pressedTrigger, inactiveTrigger],
        };
        var style = new Style(typeof(TestControl))
        {
            BasedOn = baseStyle,
            Setters = [Setter.Unset(ValueProperty)],
            Triggers = [winningTrigger],
        };
        var control = new TestControl();
        control.SetPressedState(true);
        control.SetStyle(style);

        var trace = control.TraceStyle(ValueProperty);

        Assert.HasCount(5, trace.Entries);
        Assert.AreSame(baseStyle, trace.Entries[0].DeclaringStyle);
        Assert.AreEqual(10.0, trace.Entries[0].ResolvedValue);
        Assert.AreSame(pressedTrigger, trace.Entries[1].Trigger);
        Assert.IsTrue(trace.Entries[1].IsActive);
        Assert.AreSame(inactiveTrigger, trace.Entries[2].Trigger);
        Assert.IsFalse(trace.Entries[2].IsActive);
        Assert.IsFalse(trace.Entries[2].HasResolvedValue);
        Assert.IsNull(trace.Entries[2].ResolvedValue);
        Assert.IsTrue(trace.Entries[3].IsUnset);
        Assert.IsTrue(trace.Entries[4].IsFinal);
        Assert.IsTrue(trace.Entries[4].IsWinner);
        Assert.AreEqual(30.0, trace.StyleValue);
        Assert.IsTrue(trace.HasStyleCandidate);
        Assert.IsTrue(trace.IsStyleEffective);
    }

    [TestMethod]
    public void StyleCascadeTrace_DistinguishesNewDefaultFromExplicitBasedOn()
    {
        var control = new TestControl();
        control.SetStyle(new Style(typeof(TestControl)));

        var layered = control.TraceStyle(Control.CornerRadiusProperty);

        Assert.IsTrue(layered.HasStyleCandidate);
        Assert.AreEqual(StyleCascadeLayer.FrameworkDefault, layered.FinalEntry!.Value.Layer);
        Assert.IsTrue(layered.FinalEntry!.Value.IsNewlyInherited);

        control.SetStyle(new Style(typeof(TestControl))
        {
            BasedOn = Style.ForType<Control>(),
        });

        var explicitBasedOn = control.TraceStyle(Control.CornerRadiusProperty);

        Assert.IsTrue(explicitBasedOn.HasStyleCandidate);
        Assert.AreEqual(StyleCascadeLayer.Application, explicitBasedOn.FinalEntry!.Value.Layer);
        Assert.IsFalse(explicitBasedOn.FinalEntry!.Value.IsNewlyInherited,
            "a default style already reachable through BasedOn is not reported as newly inherited");
        Assert.HasCount(1, explicitBasedOn.Entries,
            "the shared default Style instance is visited only once");

        control.SetStyle(null);
        var ordinaryDefault = control.TraceStyle(Control.CornerRadiusProperty);
        Assert.IsFalse(ordinaryDefault.FinalEntry!.Value.IsNewlyInherited,
            "ordinary default styling is not reported as migration-induced inheritance");
    }

    [TestMethod]
    public void StyleCascadeTrace_ReportsShadowingAnimationAndFinalUnset()
    {
        var control = new TestControl();
        control.SetStyle(new Style(typeof(TestControl))
        {
            Setters = [Setter.Create(ValueProperty, 10.0)],
        });
        control.Value = 50.0;
        control.SetAnimatedValue(60.0);

        var shadowedTrace = control.TraceStyle(ValueProperty);

        Assert.IsTrue(shadowedTrace.HasStyleCandidate);
        Assert.IsFalse(shadowedTrace.IsStyleEffective);
        Assert.AreEqual(ValueSource.Local, shadowedTrace.EffectiveSource);
        Assert.IsTrue(shadowedTrace.IsAnimated);

        control.ClearAnimatedValue();
        var baseStyle = new Style(typeof(TestControl))
        {
            Setters = [Setter.Create(ValueProperty, 20.0)],
        };
        control.SetStyle(new Style(typeof(TestControl))
        {
            BasedOn = baseStyle,
            Setters = [Setter.Unset(ValueProperty)],
        });

        var unsetTrace = control.TraceStyle(ValueProperty);

        Assert.IsFalse(unsetTrace.HasStyleCandidate);
        Assert.AreEqual(ValueSource.Local, unsetTrace.EffectiveSource);
        Assert.IsTrue(unsetTrace.FinalEntry!.Value.IsUnset);
        Assert.IsFalse(unsetTrace.FinalEntry!.Value.IsWinner);
    }
#endif

    private static Style PressedStyle(double value)
        => new(typeof(TestControl))
        {
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters = [Setter.Create(ValueProperty, value)],
                },
            ],
        };

    private sealed class TestControl : Control
    {
        public double Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public void SetPressedState(bool value) => SetPressed(value);

        public void ReconcileStyle(bool snap) => ResolveVisualStateInternal(snap);

#if DEBUG
        public StyleCascadeTrace TraceStyle(MewProperty property) => GetStyleCascadeTrace(property);

        public void SetAnimatedValue(double value) => PropertyStore.SetAnimatedValue(ValueProperty.Id, value);

        public void ClearAnimatedValue() => PropertyStore.ClearAnimatedValue(ValueProperty.Id);
#endif
    }
}
