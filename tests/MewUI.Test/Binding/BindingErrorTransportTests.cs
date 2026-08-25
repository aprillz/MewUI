using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Binding;

[TestClass]
public sealed class BindingErrorTransportTests
{
    [TestMethod]
    public void ConvertBackFailure_PreservesCandidateAndClearsOnSuccessfulCommit()
    {
        var source = new ObservableValue<int>(1);
        var target = new TextTarget();
        var errors = new List<BindingError?>();
        target.ObserveErrors(errors.Add);
        target.SetBinding(
            TextTarget.TextProperty,
            source,
            static value => value.ToString(),
            static value => int.Parse(value),
            BindingMode.TwoWay);

        target.Commit("invalid");

        Assert.AreEqual("invalid", target.Text);
        Assert.AreEqual(1, source.Value);
        BindingStateSnapshot failed = target.GetState();
        Assert.AreEqual("invalid", failed.CurrentCandidate);
        Assert.AreEqual("1", failed.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingStatus.ValidationError, failed.Error?.Status);
        Assert.AreEqual(BindingErrorStage.ConvertBack, failed.Error?.Stage);

        target.Commit("2");

        Assert.AreEqual(2, source.Value);
        Assert.AreEqual("2", target.Text);
        BindingStateSnapshot recovered = target.GetState();
        Assert.AreEqual("2", recovered.LastSuccessfulTargetValue);
        Assert.IsNull(recovered.Error);
        Assert.HasCount(2, errors);
        Assert.IsNotNull(errors[0]);
        Assert.IsNull(errors[1]);
    }

    [TestMethod]
    public void SourceWriteFailure_DoesNotAssumeTheSourceWasRolledBack()
    {
        var source = new ObservableValue<int>(1);
        var target = new IntTarget();
        target.SetBinding(IntTarget.ValueProperty, source, BindingMode.TwoWay);
        source.Changed += static () => throw new InvalidOperationException("source observer failed");

        target.Commit(2);

        Assert.AreEqual(2, target.Value);
        Assert.AreEqual(2, source.Value, "the setter changed the source before an observer threw");
        BindingStateSnapshot state = target.GetState();
        Assert.AreEqual(2, state.CurrentCandidate);
        Assert.AreEqual(1, state.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingStatus.BindingError, state.Error?.Status);
        Assert.AreEqual(BindingErrorStage.SourceWrite, state.Error?.Stage);
    }

    [TestMethod]
    public void MewPropertySourceValidationFailure_IsRecoverableAndPrecedesTheWrite()
    {
        var source = new ValidatedSource { Value = 1 };
        var target = new IntTarget();
        target.SetBinding(
            IntTarget.ValueProperty,
            source,
            ValidatedSource.ValueProperty,
            static value => value,
            static value => value,
            BindingMode.TwoWay);

        target.Commit(-1);

        Assert.AreEqual(-1, target.Value);
        Assert.AreEqual(1, source.Value);
        BindingStateSnapshot state = target.GetState();
        Assert.AreEqual(-1, state.CurrentCandidate);
        Assert.AreEqual(1, state.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingStatus.ValidationError, state.Error?.Status);
        Assert.AreEqual(BindingErrorStage.SourceValidation, state.Error?.Stage);
    }

    [TestMethod]
    public void ReadBackFailureAfterSourceWrite_IsReportedAsConsistencyError()
    {
        var source = new ObservableValue<int>(1);
        var target = new TextTarget();
        target.SetBinding(
            TextTarget.TextProperty,
            source,
            static value => value == 2
                ? throw new InvalidOperationException("read-back conversion failed")
                : value.ToString(),
            static value => int.Parse(value),
            BindingMode.TwoWay);

        target.Commit("2");

        Assert.AreEqual(2, source.Value);
        Assert.AreEqual("2", target.Text);
        BindingStateSnapshot state = target.GetState();
        Assert.AreEqual("2", state.CurrentCandidate);
        Assert.AreEqual("1", state.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingStatus.BindingError, state.Error?.Status);
        Assert.AreEqual(BindingErrorStage.Consistency, state.Error?.Stage);
    }

    [TestMethod]
    public void SourceConversionFailure_KeepsLastTargetAndRecoversOnNextPush()
    {
        var source = new ObservableValue<int>(1);
        var target = new TextTarget();
        target.SetBinding(
            TextTarget.TextProperty,
            source,
            static value => value == 2
                ? throw new InvalidOperationException("conversion failed")
                : value.ToString(),
            mode: BindingMode.OneWay);

        source.Value = 2;

        Assert.AreEqual("1", target.Text);
        BindingStateSnapshot failed = target.GetState();
        Assert.AreEqual(2, failed.CurrentCandidate);
        Assert.AreEqual("1", failed.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingErrorStage.Convert, failed.Error?.Stage);

        source.Value = 3;

        Assert.AreEqual("3", target.Text);
        BindingStateSnapshot recovered = target.GetState();
        Assert.AreEqual("3", recovered.CurrentCandidate);
        Assert.AreEqual("3", recovered.LastSuccessfulTargetValue);
        Assert.IsNull(recovered.Error);
    }

    [TestMethod]
    public void TargetValidationFailure_LeavesSourceUnchangedAndTracksRejectedCandidate()
    {
        var source = new ObservableValue<int>(1);
        var target = new ValidatedTarget();
        target.SetBinding(ValidatedTarget.ValueProperty, source, BindingMode.TwoWay);

        target.Commit(-1);

        Assert.AreEqual(1, source.Value);
        Assert.AreEqual(1, target.Value);
        BindingStateSnapshot state = target.GetState();
        Assert.AreEqual(-1, state.CurrentCandidate);
        Assert.AreEqual(1, state.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingStatus.ValidationError, state.Error?.Status);
        Assert.AreEqual(BindingErrorStage.TargetValidation, state.Error?.Stage);
    }

    [TestMethod]
    public void ReplacingBinding_ClearsThePreviousErrorState()
    {
        var first = new ObservableValue<int>(1);
        var second = new ObservableValue<int>(3);
        var target = new TextTarget();
        var errors = new List<BindingError?>();
        target.ObserveErrors(errors.Add);
        target.SetBinding(
            TextTarget.TextProperty,
            first,
            static value => value == 2
                ? throw new InvalidOperationException("conversion failed")
                : value.ToString(),
            mode: BindingMode.OneWay);
        first.Value = 2;

        target.SetBinding(
            TextTarget.TextProperty,
            second,
            static value => value.ToString(),
            mode: BindingMode.OneWay);

        BindingStateSnapshot state = target.GetState();
        Assert.AreEqual("3", target.Text);
        Assert.AreEqual("3", state.LastSuccessfulTargetValue);
        Assert.IsNull(state.Error);
        Assert.HasCount(2, errors);
        Assert.IsNotNull(errors[0]);
        Assert.IsNull(errors[1]);
    }

    [TestMethod]
    public void ClearingBinding_RemovesItsErrorState()
    {
        var source = new ObservableValue<int>(1);
        var target = new TextTarget();
        var errors = new List<BindingError?>();
        target.ObserveErrors(errors.Add);
        target.SetBinding(
            TextTarget.TextProperty,
            source,
            static value => value == 2
                ? throw new InvalidOperationException("conversion failed")
                : value.ToString(),
            mode: BindingMode.OneWay);
        source.Value = 2;

        target.ClearBinding(TextTarget.TextProperty);

        Assert.IsNull(target.TryGetState());
        Assert.HasCount(2, errors);
        Assert.IsNotNull(errors[0]);
        Assert.IsNull(errors[1]);
    }

    [TestMethod]
    public void ControlValidationState_ProjectsOnlyValidationErrors()
    {
        var first = new ObservableValue<string>("first");
        var second = new ObservableValue<string>("second");
        var target = new ValidationControl();
        target.SetBinding(ValidationControl.FirstProperty, first, BindingMode.TwoWay);
        target.SetBinding(ValidationControl.SecondProperty, second, BindingMode.TwoWay);

        target.ReportBindingError(
            ValidationControl.FirstProperty,
            "invalid first",
            BindingStatus.ValidationError,
            BindingErrorStage.ConvertBack,
            new FormatException("first validation failed"));
        target.ReportBindingError(
            ValidationControl.SecondProperty,
            "unavailable second",
            BindingStatus.BindingError,
            BindingErrorStage.SourceReadBack,
            new InvalidOperationException("second binding failed"));

        Assert.IsTrue(target.HasValidationError);
        Assert.HasCount(1, target.ValidationErrors);
        Assert.AreSame(ValidationControl.FirstProperty, target.ValidationErrors[0].Property);
        Assert.AreEqual("first validation failed", target.ValidationErrors[0].Message);
        Assert.IsTrue(target.CurrentFlags.HasFlag(VisualStateFlags.Invalid));
        Assert.IsTrue(target.CurrentFlags.HasFlag(VisualStateFlags.Enabled));
        Assert.ThrowsExactly<NotSupportedException>(
            () => ((IList<ValidationError>)target.ValidationErrors).Add(
                new ValidationError(ValidationControl.FirstProperty, "replacement")));

        target.IsEnabled = false;
        Assert.IsTrue(target.CurrentFlags.HasFlag(VisualStateFlags.Invalid));
        Assert.IsFalse(target.CurrentFlags.HasFlag(VisualStateFlags.Enabled));

        target.ReportBindingError(
            ValidationControl.FirstProperty,
            "unavailable first",
            BindingStatus.BindingError,
            BindingErrorStage.Consistency,
            new InvalidOperationException("first consistency failure"));

        Assert.IsFalse(target.HasValidationError);
        Assert.IsEmpty(target.ValidationErrors);
        Assert.IsFalse(target.CurrentFlags.HasFlag(VisualStateFlags.Invalid));
    }

    [TestMethod]
    public void TextBoxDefaultStyle_ShowsAndClearsValidationBorder()
    {
        var source = new ObservableValue<int>(1);
        var target = new TextBox();
        target.ResolveAndApplyStyle();
        target.SetBinding(
            TextBox.TextProperty,
            source,
            static value => value.ToString(),
            static value => int.Parse(value),
            BindingMode.TwoWay);

        ReportValidationError(target, TextBox.TextProperty, "invalid");
        target.ResolveVisualStateInternal(snap: true);

        Assert.IsTrue(target.HasValidationError);
        Assert.AreEqual(target.ThemeInternal.Palette.Error, target.BorderBrush);

        source.Value = 3;
        target.ResolveVisualStateInternal(snap: true);

        Assert.IsFalse(target.HasValidationError);
        Assert.AreEqual(target.ThemeInternal.Palette.ControlBorder, target.BorderBrush);
    }

    [TestMethod]
    public void TextBoxValidationStyle_PreservesLocalBorderBrush()
    {
        var source = new ObservableValue<int>(1);
        var target = new TextBox();
        var localBorder = Color.FromRgb(12, 34, 56);
        target.ResolveAndApplyStyle();
        target.BorderBrush = localBorder;
        target.SetBinding(
            TextBox.TextProperty,
            source,
            static value => value.ToString(),
            static value => int.Parse(value),
            BindingMode.TwoWay);

        ReportValidationError(target, TextBox.TextProperty, "invalid");
        target.ResolveVisualStateInternal(snap: true);

        Assert.IsTrue(target.HasValidationError);
        Assert.AreEqual(localBorder, target.BorderBrush);
        Assert.AreEqual(ValueSource.Local, target.PropertyStore.GetSource(Control.BorderBrushProperty.Id));
    }

    [TestMethod]
    public void TextBoxRenderer_PreservesValidationBorderWhileFocused()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var source = new ObservableValue<int>(1);
        var target = new TextBox();
        target.ResolveAndApplyStyle();
        target.SetBinding(
            TextBox.TextProperty,
            source,
            static value => value.ToString(),
            static value => int.Parse(value),
            BindingMode.TwoWay);

        ReportValidationError(target, TextBox.TextProperty, "invalid");

        AssertFocusedValidationBorderIsRendered(target);
    }

    [TestMethod]
    public void DropDownRenderer_PreservesValidationBorderWhileFocused()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var source = new ObservableValue<int>(-1);
        var target = new ComboBox();
        target.ResolveAndApplyStyle();
        target.SetBinding(ComboBox.SelectedIndexProperty, source, BindingMode.TwoWay);

        ReportValidationError(target, ComboBox.SelectedIndexProperty, 2);

        AssertFocusedValidationBorderIsRendered(target);
    }

    [TestMethod]
    public void ListBoxRenderer_PreservesValidationBorderWhileFocused()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var source = new ObservableValue<int>(-1);
        var target = new ListBox();
        target.ResolveAndApplyStyle();
        target.SetBinding(ListBox.SelectedIndexProperty, source, BindingMode.TwoWay);

        ReportValidationError(target, ListBox.SelectedIndexProperty, 2);

        AssertFocusedValidationBorderIsRendered(target, mayRenderNestedBorders: true);
    }

    [TestMethod]
    public void SegmentedControlRenderer_PreservesValidationBorderWhileFocused()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var source = new ObservableValue<int>(-1);
        var target = new SegmentedControl();
        target.ResolveAndApplyStyle();
        target.SetBinding(SegmentedControl.SelectedIndexProperty, source, BindingMode.TwoWay);

        ReportValidationError(target, SegmentedControl.SelectedIndexProperty, 2);

        AssertFocusedValidationBorderIsRendered(target);
    }

    [TestMethod]
    public void ValidationStateProperties_CannotBeWrittenByStyleOrElementTrigger()
    {
        var style = new Style(typeof(ValidationControl))
        {
            Setters = [Setter.Create(Control.HasValidationErrorProperty, true)],
        };
        var styleException = Assert.ThrowsExactly<InvalidOperationException>(style.Freeze);
        StringAssert.Contains(styleException.Message, "read-only property");

        var target = new ValidationControl();
        var triggerException = Assert.ThrowsExactly<ArgumentException>(() =>
            target.Triggers =
            [
                ElementTrigger.When(
                    UIElement.IsEffectivelyEnabledProperty,
                    false,
                    Setter.Create(Control.HasValidationErrorProperty, true)),
            ]);
        StringAssert.Contains(triggerException.Message, "read-only property");
    }

    private static void AssertFocusedValidationBorderIsRendered(
        Control target,
        bool mayRenderNestedBorders = false)
    {
        target.SetFocused(true);
        target.ResolveVisualStateInternal(snap: true);
        target.Measure(new Size(240, 40));
        target.Arrange(new Rect(0, 0, 240, 40));

        var context = new BorderRecordingContext();
        target.Render(context);

        Assert.IsTrue(target.HasValidationError);
        Assert.AreEqual(target.ThemeInternal.Palette.Error, target.BorderBrush);
        if (mayRenderNestedBorders)
        {
            CollectionAssert.Contains(context.BorderColors, target.ThemeInternal.Palette.Error);
        }
        else
        {
            Assert.AreEqual(target.ThemeInternal.Palette.Error, context.BorderColor);
        }
    }

    private static void ReportValidationError<T>(Control target, MewProperty<T> property, T candidate)
        => target.ReportBindingError(
            property,
            candidate,
            BindingStatus.ValidationError,
            BindingErrorStage.SourceValidation,
            new InvalidOperationException("validation failed"));

    private sealed class BorderRecordingContext : NoOpGraphicsContext, IGraphicsContext
    {
        public Color? BorderColor { get; private set; }

        public List<Color> BorderColors { get; } = [];

        public override double DpiScale => 1;

        void IGraphicsContext.DrawRoundedRectangle(
            Rect rect,
            double radiusX,
            double radiusY,
            Color color,
            double thickness,
            bool strokeInset)
        {
            BorderColor = color;
            BorderColors.Add(color);
        }

        void IGraphicsContext.DrawRectangle(
            Rect rect,
            Color color,
            double thickness,
            bool strokeInset)
        {
            BorderColor = color;
            BorderColors.Add(color);
        }
    }

    private sealed class IntTarget : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<IntTarget>(nameof(Value), 0);

        public int Value => GetValue(ValueProperty);

        public void Commit(int value) => CommitTargetValue(ValueProperty, value);

        public BindingStateSnapshot GetState() => GetBindingState(ValueProperty.Id)!.Value;
    }

    private sealed class TextTarget : MewObject
    {
        public static readonly MewProperty<string> TextProperty =
            MewProperty<string>.Register<TextTarget>(nameof(Text), string.Empty);

        public string Text => GetValue(TextProperty);

        public void Commit(string value) => CommitTargetValue(TextProperty, value);

        public BindingStateSnapshot GetState() => GetBindingState(TextProperty.Id)!.Value;

        public BindingStateSnapshot? TryGetState() => GetBindingState(TextProperty.Id);

        public void ObserveErrors(Action<BindingError?> callback)
            => AddBindingErrorChangedCallback(TextProperty.Id, callback);
    }

    private sealed class ValidatedTarget : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<ValidatedTarget>(
                nameof(Value),
                0,
                validate: static (_, value) =>
                {
                    if (value < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value));
                    }
                });

        public int Value => GetValue(ValueProperty);

        public void Commit(int value) => CommitTargetValue(ValueProperty, value);

        public BindingStateSnapshot GetState() => GetBindingState(ValueProperty.Id)!.Value;
    }

    private sealed class ValidatedSource : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<ValidatedSource>(
                nameof(Value),
                0,
                validate: static (_, value) =>
                {
                    if (value < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(value));
                    }
                });

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    private sealed class ValidationControl : Control
    {
        public static readonly MewProperty<string> FirstProperty =
            MewProperty<string>.Register<ValidationControl>(nameof(First), string.Empty);

        public static readonly MewProperty<string> SecondProperty =
            MewProperty<string>.Register<ValidationControl>(nameof(Second), string.Empty);

        public string First => GetValue(FirstProperty);

        public string Second => GetValue(SecondProperty);

        public VisualStateFlags CurrentFlags => ComputeVisualState().Flags;
    }
}
