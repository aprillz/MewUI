using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Core;

[TestClass]
public sealed class PropertyValueTraceTests
{
    [TestMethod]
    public void Trace_ReportsVisualBaseWinnerAndAllRawCandidates()
    {
        var source = new ObservableValue<int>(1);
        var target = new TraceTarget();
        target.SetBinding(TraceTarget.ValueProperty, source, BindingMode.OneWay);
        target.SetStyle(2);
        target.SetElementTrigger(3);
        target.SetLocalWithoutReplacingBinding(4);
        target.SetAnimated(5);

        PropertyValueTrace trace = target.TraceValue();

        Assert.AreEqual(4, trace.BaseValue);
        Assert.AreEqual(5, trace.VisualValue);
        Assert.AreEqual(ValueSource.Local, trace.EffectiveSource);
        Assert.IsTrue(trace.IsAnimated);
        AssertCandidate(trace, ValueSource.Local, 4, isWinner: true);
        AssertCandidate(trace, ValueSource.ElementTrigger, 3, isWinner: false);
        AssertCandidate(trace, ValueSource.Binding, 1, isWinner: false);
        AssertCandidate(trace, ValueSource.Style, 2, isWinner: false);
        Assert.IsFalse(trace.GetCandidate(ValueSource.Inherited).IsSet);
        AssertCandidate(trace, ValueSource.Default, 0, isWinner: false);
        Assert.AreEqual(1, trace.BindingState?.LastSuccessfulTargetValue);
    }

    [TestMethod]
    public void Trace_DistinguishesAnExplicitNullCandidateFromAnUnsetSlot()
    {
        var target = new TraceTarget();
        target.SetTextBindingCandidate("binding");
        target.SetTextLocalCandidate(null);

        PropertyValueTrace trace = target.TraceText();

        var local = trace.GetCandidate(ValueSource.Local);
        Assert.IsTrue(local.IsSet);
        Assert.IsTrue(local.IsWinner);
        Assert.IsNull(local.RawValue);
        AssertCandidate(trace, ValueSource.Binding, "binding", isWinner: false);
        Assert.IsNull(trace.VisualValue);
    }

    [TestMethod]
    public void Trace_IncludesBindingErrorCandidateAndLastSuccessfulValue()
    {
        var source = new ObservableValue<int>(1);
        var target = new TraceTarget();
        target.SetBinding(
            TraceTarget.TextProperty,
            source,
            static value => value.ToString(),
            static value => int.Parse(value!),
            BindingMode.TwoWay);

        target.CommitText("invalid");

        PropertyValueTrace trace = target.TraceText();
        Assert.AreEqual(ValueSource.Binding, trace.EffectiveSource);
        Assert.AreEqual("invalid", trace.VisualValue);
        Assert.AreEqual("invalid", trace.BindingState?.CurrentCandidate);
        Assert.AreEqual("1", trace.BindingState?.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingStatus.ValidationError, trace.BindingState?.Error?.Status);
        Assert.AreEqual(BindingErrorStage.ConvertBack, trace.BindingState?.Error?.Stage);
    }

    private static void AssertCandidate(
        PropertyValueTrace trace,
        ValueSource source,
        object? expected,
        bool isWinner)
    {
        var candidate = trace.GetCandidate(source);
        Assert.IsTrue(candidate.IsSet, $"{source} candidate should be set");
        Assert.AreEqual(expected, candidate.RawValue);
        Assert.AreEqual(isWinner, candidate.IsWinner);
    }

    private sealed class TraceTarget : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<TraceTarget>(nameof(Value), 0);

        public static readonly MewProperty<string?> TextProperty =
            MewProperty<string?>.Register<TraceTarget>(nameof(Text), null);

        public int Value => GetValue(ValueProperty);

        public string? Text => GetValue(TextProperty);

        public void SetStyle(int value) => PropertyStore.SetStyle(ValueProperty, value);

        public void SetElementTrigger(int value) => PropertyStore.SetElementTrigger(ValueProperty, value);

        public void SetLocalWithoutReplacingBinding(int value) => PropertyStore.SetLocal(ValueProperty, value);

        public void SetAnimated(int value) => PropertyStore.SetAnimatedValue(ValueProperty.Id, value);

        public void SetTextBindingCandidate(string? value) => PropertyStore.SetBinding(TextProperty, value);

        public void SetTextLocalCandidate(string? value) => PropertyStore.SetLocal(TextProperty, value);

        public void CommitText(string? value) => CommitTargetValue(TextProperty, value);

        public PropertyValueTrace TraceValue() => GetPropertyValueTrace(ValueProperty);

        public PropertyValueTrace TraceText() => GetPropertyValueTrace(TextProperty);
    }
}
