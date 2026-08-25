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
    public void TwoWayWithoutSetter_IsRejected()
    {
        // The generator writes the setter from the getter syntax. Where it does not run, the
        // caller has to supply one rather than end up with a silently one-way binding.
        var source = new PersonViewModel { Name = "first" };
        var target = new TestObject();

        Assert.ThrowsExactly<ArgumentException>(() => target.SetBinding(
            TestObject.TextProperty,
            source,
            static value => value.Name,
            mode: BindingMode.TwoWay));
    }

    [TestMethod]
    public void OneWayWithoutSetter_IsAccepted()
    {
        var source = new PersonViewModel { Name = "first" };
        var target = new TestObject();

        target.SetBinding(
            TestObject.TextProperty,
            source,
            static value => value.Name,
            mode: BindingMode.OneWay);
        source.Name = "second";

        Assert.AreEqual("second", target.Text);
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
    public void IndexedSegment_FollowsElementReplacement()
    {
        var source = new CollectionViewModel();
        source.Items.Add("first");
        var target = new TestObject();

        target.SetBinding(TestObject.TextProperty, source, CreateFirstItemPath(), BindingMode.OneWay, fallbackValue: string.Empty);
        Assert.AreEqual("first", target.Text);

        source.Items[0] = "replaced";

        Assert.AreEqual("replaced", target.Text);
    }

    [TestMethod]
    public void IndexedSegment_FollowsInsertionAndRemoval()
    {
        var source = new CollectionViewModel();
        source.Items.Add("first");
        var target = new TestObject();

        target.SetBinding(TestObject.TextProperty, source, CreateFirstItemPath(), BindingMode.OneWay, fallbackValue: string.Empty);

        source.Items.Insert(0, "inserted");
        Assert.AreEqual("inserted", target.Text);

        source.Items.RemoveAt(0);
        Assert.AreEqual("first", target.Text);
    }

    [TestMethod]
    public void IntermediateIndex_UsesFallbackWhenOutOfRange()
    {
        var source = new CollectionViewModel();
        source.Items.Add("first");
        var target = new TestObject();
        var path = BindingPath
            .From<CollectionViewModel>()
            .ThenNotifying(static value => value.Items)
            .ThenIndexed(static value => value[0])
            .Then(static value => value.Length);

        target.SetBinding(TestObject.ValueProperty, source, path, BindingMode.OneWay, fallbackValue: -1);
        Assert.AreEqual(5, target.Value);

        source.Items.Clear();
        Assert.AreEqual(-1, target.Value);

        source.Items.Add("back");
        Assert.AreEqual(4, target.Value);
    }

    [TestMethod]
    public void LeafIndex_ReadsAsNullWhenOutOfRange()
    {
        // The observer treats a null from the last segment as the source value, so fallbackValue
        // does not apply there.
        var source = new CollectionViewModel();
        source.Items.Add("first");
        var target = new TestObject();

        target.SetBinding(
            TestObject.TextProperty,
            source,
            CreateFirstItemPath(),
            BindingMode.OneWay,
            fallbackValue: "(empty)");

        source.Items.Clear();
        Assert.IsNull(target.Text);

        source.Items.Add("back");
        Assert.AreEqual("back", target.Text);
    }

    [TestMethod]
    public void IndexedSegment_DoesNotThrowWhenTheCollectionIsEmpty()
    {
        var source = new CollectionViewModel();
        source.Items.Add("first");
        var target = new TestObject();
        int reads = 0;
        var path = BindingPath
            .From<CollectionViewModel>()
            .ThenNotifying(static value => value.Items)
            .ThenIndexed(value =>
            {
                reads++;
                return value[0];
            });

        target.SetBinding(TestObject.TextProperty, source, path, BindingMode.OneWay, fallbackValue: string.Empty);
        int readsWhilePopulated = reads;

        source.Items.Clear();

        // A range check replaces the getter call, so an emptied collection throws nothing.
        Assert.AreEqual(readsWhilePopulated, reads);
        Assert.IsNull(target.Text);
    }

    [TestMethod]
    public void IndexedSegment_ObservesAnIndexerOnlyNotifier()
    {
        var source = new SettingsViewModel();
        var target = new TestObject();
        var path = BindingPath
            .From<SettingsViewModel>()
            .ThenNotifying(static value => value.Entries)
            .ThenIndexed(static value => value["theme"]);

        target.SetBinding(TestObject.TextProperty, source, path, BindingMode.OneWay, fallbackValue: string.Empty);
        Assert.AreEqual(string.Empty, target.Text);

        source.Entries["theme"] = "dark";

        Assert.AreEqual("dark", target.Text);
    }

    [TestMethod]
    public void IndexedSegment_DoesNotKeepTargetAlive()
    {
        var source = new CollectionViewModel();
        source.Items.Add("first");
        var targetReference = CreateIndexedBinding(source);

        Collect(targetReference);

        Assert.IsFalse(targetReference.IsAlive);
        source.Items[0] = "replaced";
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

    private static BindingPath<CollectionViewModel, string> CreateFirstItemPath()
        => BindingPath
            .From<CollectionViewModel>()
            .ThenNotifying(static value => value.Items)
            .ThenIndexed(static value => value[0]);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateIndexedBinding(CollectionViewModel source)
    {
        var target = new TestObject();
        target.SetBinding(TestObject.TextProperty, source, CreateFirstItemPath(), BindingMode.OneWay, fallbackValue: string.Empty);
        return new WeakReference(target);
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

    private sealed class SettingsViewModel : NotifyingObject
    {
        public SettingsMap Entries { get; } = new();
    }

    /// <summary>
    /// An indexer owner that reports changes the way the framework convention expects, without
    /// implementing INotifyCollectionChanged.
    /// </summary>
    private sealed class SettingsMap : NotifyingObject
    {
        private readonly Dictionary<string, string> _values = [];

        public string this[string key]
        {
            get => _values.TryGetValue(key, out var value) ? value : string.Empty;
            set
            {
                _values[key] = value;
                RaisePropertyChanged("Item[]");
            }
        }
    }
}
