using System.ComponentModel;
using System.Runtime.CompilerServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Binding;

[TestClass]
public sealed class MultiBindingTests
{
    [TestMethod]
    public void SameSource_RecomputesWhenEitherInputChanges()
    {
        var person = new Person { First = "Ada", Last = "Lovelace" };
        var target = new TestObject();

        target.SetBinding(
            TestObject.TextProperty,
            person,
            static value => value.First,
            static value => value.Last,
            static (first, last) => $"{first} {last}");
        Assert.AreEqual("Ada Lovelace", target.Text);

        person.First = "Grace";
        Assert.AreEqual("Grace Lovelace", target.Text);

        person.Last = "Hopper";
        Assert.AreEqual("Grace Hopper", target.Text);
    }

    [TestMethod]
    public void SameSource_ThreeAndFourGettersRecomputeOnEveryInput()
    {
        var person = new Person { Title = "Dr", First = "Ada", Last = "Lovelace", Age = 36 };
        var threeTarget = new TestObject().Bind(
            TestObject.TextProperty,
            person,
            static value => value.Title,
            static value => value.First,
            static value => value.Last,
            static (title, first, last) => $"{title} {first} {last}");
        var fourTarget = new TestObject().Bind(
            TestObject.TextProperty,
            person,
            static value => value.Title,
            static value => value.First,
            static value => value.Last,
            static value => value.Age,
            static (title, first, last, age) => $"{title} {first} {last} {age}");

        person.Title = "Countess";
        person.Age = 37;

        Assert.AreEqual("Countess Ada Lovelace", threeTarget.Text);
        Assert.AreEqual("Countess Ada Lovelace 37", fourTarget.Text);
    }

    [TestMethod]
    public void DifferentSources_CombineObservableAndMewPropertyInputs()
    {
        var price = new ObservableValue<int>(3);
        var quantity = new TestObject { Value = 2 };
        var target = new TestObject();

        target.SetBinding(
            TestObject.ValueProperty,
            BindingInput.From(price),
            BindingInput.From(quantity, TestObject.ValueProperty),
            static (unitPrice, count) => unitPrice * count);
        Assert.AreEqual(6, target.Value);

        price.Value = 5;
        Assert.AreEqual(10, target.Value);

        quantity.Value = 4;
        Assert.AreEqual(20, target.Value);
    }

    [TestMethod]
    public void PathInput_RewiresWhenAnIntermediateIsReplaced()
    {
        var oldLeaf = new Leaf(1);
        var root = new Root();
        root.OptionalLeaf.Value = oldLeaf;
        var offset = new ObservableValue<int>(10);
        var target = new TestObject();

        target.SetBinding(
            TestObject.ValueProperty,
            BindingInput.From(root, CreateAmountPath()),
            BindingInput.From(offset),
            static (amount, add) => amount + add);
        Assert.AreEqual(11, target.Value);

        var newLeaf = new Leaf(2);
        root.OptionalLeaf.Value = newLeaf;
        Assert.AreEqual(12, target.Value);

        oldLeaf.Amount.Value = 99;
        Assert.AreEqual(12, target.Value);

        newLeaf.Amount.Value = 5;
        Assert.AreEqual(15, target.Value);
    }

    [TestMethod]
    public void UnavailableInput_ShowsFallbackWithoutCombiningAndRecovers()
    {
        var root = new Root();
        var offset = new ObservableValue<int>(10);
        var target = new TestObject();
        int combineCount = 0;

        target.SetBinding(
            TestObject.ValueProperty,
            BindingInput.From(root, CreateAmountPath()),
            BindingInput.From(offset),
            -1,
            (amount, add) =>
            {
                combineCount++;
                return amount + add;
            });
        Assert.AreEqual(-1, target.Value);

        offset.Value = 20;
        Assert.AreEqual(-1, target.Value);
        Assert.AreEqual(0, combineCount);

        root.OptionalLeaf.Value = new Leaf(3);
        Assert.AreEqual(23, target.Value);

        root.OptionalLeaf.Value = null;
        Assert.AreEqual(-1, target.Value);
    }

    [TestMethod]
    public void UnavailableInput_WithoutFallbackShowsTheDefaultValue()
    {
        var root = new Root();
        root.OptionalLeaf.Value = new Leaf(3);
        var target = new TestObject { Value = 7 };

        target.SetBinding(
            TestObject.ValueProperty,
            BindingInput.From(root, CreateAmountPath()),
            BindingInput.From(new ObservableValue<int>(10)),
            static (amount, add) => amount + add);
        Assert.AreEqual(13, target.Value);

        root.OptionalLeaf.Value = null;
        Assert.AreEqual(0, target.Value);
    }

    [TestMethod]
    public void NullValue_IsCombinedRatherThanReplacedByFallback()
    {
        var first = new ObservableValue<string?>(null);
        var second = new ObservableValue<string?>("b");
        var target = new TestObject();

        target.SetBinding(
            TestObject.TextProperty,
            BindingInput.From(first),
            BindingInput.From(second),
            "fallback",
            static (left, right) => $"{left ?? "null"}|{right ?? "null"}");

        Assert.AreEqual("null|b", target.Text);
    }

    [TestMethod]
    public void NonNullTarget_AcceptsEveryForm()
    {
        var person = new Person { First = "Ada", Last = "Lovelace" };
        var target = new TestObject()
            .Bind(
                TestObject.NameProperty,
                person,
                static value => value.First,
                static value => value.Last,
                "-",
                static (first, last) => $"{first} {last}");

        person.First = "Grace";

        Assert.AreEqual("Grace Lovelace", target.Name);
    }

    [TestMethod]
    public void CombineFailure_KeepsTheLastValueAndRecordsAnErrorUntilRecovery()
    {
        var first = new ObservableValue<int>(1);
        var second = new ObservableValue<int>(1);
        var target = new TestObject();

        target.SetBinding(
            TestObject.TextProperty,
            BindingInput.From(first),
            BindingInput.From(second),
            static (left, right) => left == 2
                ? throw new InvalidOperationException("combine failed")
                : $"{left}+{right}");

        first.Value = 2;
        Assert.AreEqual("1+1", target.Text);
        BindingStateSnapshot failed = target.GetState(TestObject.TextProperty);
        Assert.AreEqual("1+1", failed.LastSuccessfulTargetValue);
        Assert.AreEqual(BindingErrorStage.Convert, failed.Error?.Stage);

        first.Value = 3;
        Assert.AreEqual("3+1", target.Text);
        Assert.IsNull(target.GetState(TestObject.TextProperty).Error);
    }

    [TestMethod]
    public void InputReadFailure_RecordsASourceErrorUntilThatInputRecovers()
    {
        bool fail = false;
        Func<Leaf, int> getter = value => fail
            ? throw new InvalidOperationException("read failed")
            : value.Amount.Value;
        var root = new Root();
        root.OptionalLeaf.Value = new Leaf(1);
        var offset = new ObservableValue<int>(10);
        var target = new TestObject();
        var path = BindingPath
            .From<Root>()
            .Then(static value => value.OptionalLeaf)
            .Then(value => getter(value!));

        target.SetBinding(
            TestObject.ValueProperty,
            BindingInput.From(root, path),
            BindingInput.From(offset),
            static (amount, add) => amount + add);

        fail = true;
        root.OptionalLeaf.Value = new Leaf(2);
        offset.Value = 20;

        Assert.AreEqual(11, target.Value);
        Assert.AreEqual(
            BindingErrorStage.SourceReadBack,
            target.GetState(TestObject.ValueProperty).Error?.Stage);

        fail = false;
        root.OptionalLeaf.Value = new Leaf(3);

        Assert.AreEqual(23, target.Value);
        Assert.IsNull(target.GetState(TestObject.ValueProperty).Error);
    }

    [TestMethod]
    public void ThreeAndFourInputs_RecomputeOnEveryInput()
    {
        var first = new ObservableValue<int>(1);
        var second = new ObservableValue<int>(2);
        var third = new ObservableValue<int>(3);
        var fourth = new ObservableValue<int>(4);
        var threeTarget = new TestObject();
        var fourTarget = new TestObject();

        threeTarget.SetBinding(
            TestObject.ValueProperty,
            BindingInput.From(first),
            BindingInput.From(second),
            BindingInput.From(third),
            static (one, two, three) => one + two + three);
        fourTarget.SetBinding(
            TestObject.ValueProperty,
            BindingInput.From(first),
            BindingInput.From(second),
            BindingInput.From(third),
            BindingInput.From(fourth),
            static (one, two, three, four) => one * 1000 + two * 100 + three * 10 + four);
        Assert.AreEqual(6, threeTarget.Value);
        Assert.AreEqual(1234, fourTarget.Value);

        third.Value = 5;
        fourth.Value = 6;

        Assert.AreEqual(8, threeTarget.Value);
        Assert.AreEqual(1256, fourTarget.Value);
    }

    [TestMethod]
    public void TwoWayDefaultTarget_StaysOneWay()
    {
        var person = new Person { First = "Ada", Last = "Lovelace" };
        var target = new TestObject();
        BindFullName(target, person);

        target.CommitText("typed");

        Assert.AreEqual("typed", target.Text);
        Assert.AreEqual("Ada", person.First);
        Assert.AreEqual("Lovelace", person.Last);

        person.Last = "Byron";
        Assert.AreEqual("Ada Byron", target.Text);
    }

    [TestMethod]
    public void ShadowedTarget_KeepsTheBindingCandidateCurrent()
    {
        var first = new ObservableValue<int>(1);
        var second = new ObservableValue<int>(2);
        var target = new TestObject();
        target.SetBinding(
            TestObject.ValueProperty,
            BindingInput.From(first),
            BindingInput.From(second),
            static (left, right) => left + right);
        target.PropertyStore.SetElementTrigger(TestObject.ValueProperty, 9);

        first.Value = 5;
        Assert.AreEqual(9, target.Value);

        target.PropertyStore.ClearSource(TestObject.ValueProperty.Id, ValueSource.ElementTrigger);
        Assert.AreEqual(7, target.Value);
    }

    [TestMethod]
    public void ClearBinding_StopsUpdatesFromEveryInput()
    {
        var person = new Person { First = "Ada", Last = "Lovelace" };
        var target = new TestObject();
        BindFullName(target, person);

        target.ClearBinding(TestObject.TextProperty);
        person.First = "Grace";
        person.Last = "Hopper";

        Assert.IsNull(target.Text);
    }

    [TestMethod]
    public void Rebinding_DetachesThePreviousInputs()
    {
        var oldPerson = new Person { First = "Ada", Last = "Lovelace" };
        var newPerson = new Person { First = "Grace", Last = "Hopper" };
        var target = new TestObject();
        BindFullName(target, oldPerson);

        BindFullName(target, newPerson);
        oldPerson.First = "Old";

        Assert.AreEqual("Grace Hopper", target.Text);
    }

    [TestMethod]
    public void FailingInputAttach_KeepsTheExistingBinding()
    {
        var person = new Person { First = "Ada", Last = "Lovelace" };
        var target = new TestObject();
        BindFullName(target, person);
        Func<Root, ObservableValue<string>> missing = static _ => null!;

        Assert.ThrowsExactly<InvalidOperationException>(() => target.SetBinding(
            TestObject.TextProperty,
            BindingInput.From(person, static value => value.First),
            BindingInput.From(new Root(), BindingPath.From<Root>().Then(missing)),
            static (first, last) => $"{first} {last}"));

        person.First = "Grace";
        Assert.AreEqual("Grace Lovelace", target.Text);
    }

    [TestMethod]
    public void Inputs_CanBeReusedAcrossTargets()
    {
        var person = new Person { First = "Ada", Last = "Lovelace" };
        var first = BindingInput.From(person, static value => value.First);
        var last = BindingInput.From(person, static value => value.Last);
        var firstTarget = new TestObject()
            .Bind(
                TestObject.TextProperty,
                first,
                last,
                static (given, family) => $"{given} {family}");
        var secondTarget = new TestObject()
            .Bind(
                TestObject.TextProperty,
                first,
                last,
                static (given, family) => $"{family}, {given}");

        firstTarget.ClearBinding(TestObject.TextProperty);
        person.First = "Grace";

        Assert.IsNull(firstTarget.Text);
        Assert.AreEqual("Lovelace, Grace", secondTarget.Text);
    }

    [TestMethod]
    public void TemplateContextReset_ClearsTheBinding()
    {
        var person = new Person { First = "Ada", Last = "Lovelace" };
        var target = new TestObject();
        var context = new TemplateContext();

        context.Bind(
            target,
            TestObject.TextProperty,
            BindingInput.From(person, static value => value.First),
            BindingInput.From(person, static value => value.Last),
            static (first, last) => $"{first} {last}");
        Assert.AreEqual("Ada Lovelace", target.Text);

        context.Reset();
        person.First = "Grace";

        Assert.IsNull(target.Text);
    }

    [TestMethod]
    public void Binding_DoesNotKeepTargetAlive()
    {
        var person = new Person { First = "Ada", Last = "Lovelace" };
        var offset = new ObservableValue<int>(1);
        var targetReference = CreateBinding(person, offset);

        Collect(targetReference);

        Assert.IsFalse(targetReference.IsAlive);
        person.First = "Grace";
        offset.Value = 2;
    }

    [TestMethod]
    public void SingleGetterFallback_BindsTheGetter()
    {
        var person = new Person { First = "Ada", Age = 36 };
        var name = new TestObject()
            .Bind(TestObject.NameProperty, person, static value => value.First, "none");
        var age = new TestObject().Bind(
            TestObject.NameProperty,
            person,
            static value => value.Age,
            static value => value.ToString(),
            "none");

        person.First = "Grace";
        person.Age = 37;

        Assert.AreEqual("Grace", name.Name);
        Assert.AreEqual("37", age.Name);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateBinding(Person person, ObservableValue<int> offset)
    {
        var target = new TestObject();
        target.SetBinding(
            TestObject.TextProperty,
            BindingInput.From(person, static value => value.First),
            BindingInput.From(offset),
            static (first, count) => $"{first} {count}");
        return new WeakReference(target);
    }

    private static void Collect(WeakReference reference)
    {
        for (int attempt = 0; attempt < 5 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static void BindFullName(TestObject target, Person person)
        => target.SetBinding(
            TestObject.TextProperty,
            person,
            static value => value.First,
            static value => value.Last,
            static (first, last) => $"{first} {last}");

    private static BindingPath<Root, int> CreateAmountPath()
        => BindingPath
            .From<Root>()
            .Then(static value => value.OptionalLeaf)
            .Then(static value => value!.Amount);

    private sealed class Person : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _first = string.Empty;
        private string _last = string.Empty;
        private int _age;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        public string First
        {
            get => _first;
            set => SetField(ref _first, value);
        }

        public string Last
        {
            get => _last;
            set => SetField(ref _last, value);
        }

        public int Age
        {
            get => _age;
            set => SetField(ref _age, value);
        }

        private void SetField<T>(
            ref T field,
            T value,
            [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class Root
    {
        public ObservableValue<Leaf?> OptionalLeaf { get; } = new();
    }

    private sealed class Leaf(int amount)
    {
        public ObservableValue<int> Amount { get; } = new(amount);
    }

    private sealed class TestObject : MewObject
    {
        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<TestObject>(nameof(Value), 0);

        public static readonly MewProperty<string?> TextProperty =
            MewProperty<string?>.Register<TestObject>(
                nameof(Text),
                null,
                MewPropertyOptions.BindsTwoWayByDefault);

        public static readonly MewProperty<string> NameProperty =
            MewProperty<string>.Register<TestObject>(nameof(Name), string.Empty);

        public int Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public string? Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public string Name => GetValue(NameProperty);

        public void CommitText(string? value) => CommitTargetValue(TextProperty, value);

        public BindingStateSnapshot GetState(MewProperty property)
            => GetBindingState(property.Id)!.Value;
    }
}
