using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Binding;

[TestClass]
public sealed class InpcBindingTests
{
    [TestMethod]
    public void SingleProperty_UpdatesTargetOnNotification()
    {
        var source = new PersonViewModel { Name = "first" };
        var target = new TestObject();

        target.SetBinding(TestObject.TextProperty, source, static value => value.Name);
        source.Name = "second";

        Assert.AreEqual("second", target.Text);
    }

    [TestMethod]
    public void SingleProperty_IgnoresUnrelatedPropertyNotifications()
    {
        var source = new PersonViewModel { Name = "first" };
        var target = new TestObject();

        target.SetBinding(TestObject.TextProperty, source, static value => value.Name);
        source.SetNameWithoutNotification("silent");
        source.Age = 42;

        Assert.AreEqual("first", target.Text);
    }

    [TestMethod]
    public void EmptyPropertyName_RefreshesTheSegment()
    {
        var source = new PersonViewModel { Name = "first" };
        var target = new TestObject();

        target.SetBinding(TestObject.TextProperty, source, static value => value.Name);
        source.SetNameWithoutNotification("bulk");
        source.RaisePropertyChanged(string.Empty);

        Assert.AreEqual("bulk", target.Text);
    }

    [TestMethod]
    public void TwoWay_WritesBackThroughSetter()
    {
        var source = new PersonViewModel { Name = "first" };
        var target = new TestObject();

        target.SetBinding(
            TestObject.TextProperty,
            source,
            static value => value.Name,
            static (owner, value) => owner.Name = value,
            BindingMode.TwoWay);
        target.Commit("typed");

        Assert.AreEqual("typed", source.Name);
    }

    [TestMethod]
    public void TwoWayWithoutSetter_DegradesToOneWayInsteadOfThrowing()
    {
        var source = new PersonViewModel { Name = "first" };
        var target = new TestObject();

        target.SetBinding(
            TestObject.TextProperty,
            source,
            static value => value.Name,
            mode: BindingMode.TwoWay);
        target.Commit("typed");

        Assert.AreEqual("first", source.Name);
    }

    [TestMethod]
    public void ConvertOverload_ProjectsTheObservedProperty()
    {
        var source = new PersonViewModel { Age = 1 };
        var target = new TestObject();

        target.SetBinding(
            TestObject.TextProperty,
            source,
            static value => value.Age,
            static age => $"age {age}");
        source.Age = 7;

        Assert.AreEqual("age 7", target.Text);
    }

    [TestMethod]
    public void ObservableValueLeafOverload_WorksWithoutANotifyingOwner()
    {
        var source = new PlainSettings();
        var target = new TestObject();

        target.SetBinding(TestObject.ValueProperty, source, static value => value.Zoom);
        source.Zoom.Value = 5;

        Assert.AreEqual(5, target.Value);
    }

    [TestMethod]
    public void NestedPath_FollowsTheReplacedIntermediate()
    {
        var firstProfile = new ProfileViewModel { DisplayName = "first" };
        var source = new PersonViewModel { Profile = firstProfile };
        var target = new TestObject();

        target.SetBinding(
            TestObject.TextProperty,
            source,
            CreateDisplayNamePath(),
            BindingMode.OneWay,
            fallbackValue: string.Empty);
        Assert.AreEqual("first", target.Text);

        source.Profile = new ProfileViewModel { DisplayName = "second" };
        Assert.AreEqual("second", target.Text);

        firstProfile.DisplayName = "stale";
        Assert.AreEqual("second", target.Text);
    }

    [TestMethod]
    public void NestedPath_UsesFallbackWhenIntermediateIsNull()
    {
        var source = new PersonViewModel { Profile = new ProfileViewModel { DisplayName = "first" } };
        var target = new TestObject();

        target.SetBinding(
            TestObject.TextProperty,
            source,
            CreateDisplayNamePath(),
            BindingMode.OneWay,
            fallbackValue: "(none)");
        source.Profile = null;

        Assert.AreEqual("(none)", target.Text);
    }

    [TestMethod]
    public void ObservableCollectionCount_UpdatesThroughNotifyingSegments()
    {
        var source = new CollectionViewModel();
        var target = new TestObject();
        var path = BindingPath
            .From<CollectionViewModel>()
            .ThenNotifying(static value => value.Items)
            .ThenNotifying(static value => value.Count);

        target.SetBinding(TestObject.ValueProperty, source, path, BindingMode.OneWay);
        Assert.AreEqual(0, target.Value);

        source.Items.Add("first");
        Assert.AreEqual(1, target.Value);

        source.Items.Add("second");
        Assert.AreEqual(2, target.Value);

        source.Items.Clear();
        Assert.AreEqual(0, target.Value);
    }

    [TestMethod]
    public void MultiStepGetter_IsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            BindingPath
                .From<PersonViewModel>()
                .ThenNotifying(static value => value.Profile!.DisplayName));
    }

    [TestMethod]
    public void ComputedGetter_IsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            BindingPath
                .From<PersonViewModel>()
                .ThenNotifying(static value => value.Age + 1));
    }

    [TestMethod]
    public void Rebinding_StopsObservingThePreviousSource()
    {
        var first = new PersonViewModel { Name = "first" };
        var second = new PersonViewModel { Name = "second" };
        var target = new TestObject();

        target.SetBinding(TestObject.TextProperty, first, static value => value.Name);
        target.SetBinding(TestObject.TextProperty, second, static value => value.Name);
        first.Name = "stale";

        Assert.AreEqual("second", target.Text);
    }

    [TestMethod]
    public void ClearBinding_StopsObservingTheSource()
    {
        var source = new PersonViewModel { Name = "first" };
        var target = new TestObject();

        target.SetBinding(TestObject.TextProperty, source, static value => value.Name);
        Assert.AreEqual("first", target.Text);

        // ClearBinding drops the binding-sourced value too, revealing the property default.
        target.ClearBinding(TestObject.TextProperty);
        source.Name = "stale";

        Assert.AreEqual(string.Empty, target.Text);
    }

    [TestMethod]
    public void Binding_SurvivesCollectionWhileTargetIsAlive()
    {
        var source = new PersonViewModel { Name = "first" };
        var target = new TestObject();
        target.SetBinding(TestObject.TextProperty, source, static value => value.Name);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        source.Name = "second";

        Assert.AreEqual("second", target.Text);
        GC.KeepAlive(target);
    }

    [TestMethod]
    public void Binding_DoesNotKeepTargetAlive()
    {
        var source = new PersonViewModel { Name = "first" };
        var targetReference = CreateBoundTarget(source);

        Collect(targetReference);

        Assert.IsFalse(targetReference.IsAlive);
        source.Name = "second";
    }

    [TestMethod]
    public void ReplacedIntermediate_IsReleased()
    {
        var oldProfileReference = CreateRewiredNestedPath(out var source, out var target);

        Collect(oldProfileReference);

        Assert.IsFalse(oldProfileReference.IsAlive);
        GC.KeepAlive(source);
        GC.KeepAlive(target);
    }

    private static BindingPath<PersonViewModel, string> CreateDisplayNamePath()
        => BindingPath
            .From<PersonViewModel>()
            .ThenNotifying(static value => value.Profile!)
            .ThenNotifying(static value => value.DisplayName);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateBoundTarget(PersonViewModel source)
    {
        var target = new TestObject();
        target.SetBinding(TestObject.TextProperty, source, static value => value.Name);
        return new WeakReference(target);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateRewiredNestedPath(
        out PersonViewModel source,
        out TestObject target)
    {
        var oldProfile = new ProfileViewModel { DisplayName = "first" };
        source = new PersonViewModel { Profile = oldProfile };
        target = new TestObject();
        target.SetBinding(
            TestObject.TextProperty,
            source,
            CreateDisplayNamePath(),
            BindingMode.OneWay,
            fallbackValue: string.Empty);

        source.Profile = new ProfileViewModel { DisplayName = "second" };
        return new WeakReference(oldProfile);
    }

    private static void Collect(WeakReference reference)
    {
        for (int i = 0; i < 5 && reference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class TestObject : MewObject
    {
        public static readonly MewProperty<string> TextProperty =
            MewProperty<string>.Register<TestObject>(nameof(Text), string.Empty);

        public static readonly MewProperty<int> ValueProperty =
            MewProperty<int>.Register<TestObject>(nameof(Value), 0);

        public string Text => GetValue(TextProperty);

        public int Value => GetValue(ValueProperty);

        public void Commit(string value) => CommitTargetValue(TextProperty, value);
    }

    private abstract class NotifyingObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public void RaisePropertyChanged(string? propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected void SetField<T>(
            ref T field,
            T value,
            [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            RaisePropertyChanged(propertyName);
        }
    }

    private sealed class PersonViewModel : NotifyingObject
    {
        private string _name = string.Empty;
        private int _age;
        private ProfileViewModel? _profile;

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public int Age
        {
            get => _age;
            set => SetField(ref _age, value);
        }

        public ProfileViewModel? Profile
        {
            get => _profile;
            set => SetField(ref _profile, value);
        }

        public void SetNameWithoutNotification(string value) => _name = value;
    }

    private sealed class ProfileViewModel : NotifyingObject
    {
        private string _displayName = string.Empty;

        public string DisplayName
        {
            get => _displayName;
            set => SetField(ref _displayName, value);
        }
    }

    private sealed class PlainSettings
    {
        public ObservableValue<int> Zoom { get; } = new(1);
    }

    private sealed class CollectionViewModel : NotifyingObject
    {
        public ObservableCollection<string> Items { get; } = [];
    }
}
