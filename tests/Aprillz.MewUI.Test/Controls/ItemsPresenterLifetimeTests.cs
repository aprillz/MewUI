using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class ItemsPresenterLifetimeTests
{
    [TestMethod]
    public void Presenters_UnsubscribeFromItemsView_WhenDisposed()
    {
        IItemsPresenter[] presenters =
        [
            new FixedHeightItemsPresenter(),
            new VariableHeightItemsPresenter(),
            new StackItemsPresenter(),
            new WrapItemsPresenter(),
        ];

        foreach (var presenter in presenters)
        {
            var source = new TrackingItemsView();
            presenter.ItemsSource = source;
            Assert.AreEqual(1, source.SubscriberCount, presenter.GetType().Name);

            ((FrameworkElement)presenter).Dispose();
            Assert.AreEqual(0, source.SubscriberCount, presenter.GetType().Name);
        }
    }

    [TestMethod]
    public void ItemsControl_UnsubscribesItselfAndPresenter_WhenDisposed()
    {
        var source = new TrackingItemsView();
        var control = new ItemsControl { ItemsSource = source };

        Assert.AreEqual(2, source.SubscriberCount);

        control.Dispose();

        Assert.AreEqual(0, source.SubscriberCount);
    }

    private sealed class TrackingItemsView : IItemsView
    {
        private Action<ItemsChange>? _changed;

        public int SubscriberCount { get; private set; }
        public int Count => 0;
        public Func<object?, object?>? KeySelector => null;

        public event Action<ItemsChange>? Changed
        {
            add
            {
                _changed += value;
                SubscriberCount++;
            }
            remove
            {
                _changed -= value;
                SubscriberCount--;
            }
        }

        public object? GetItem(int index) => null;
        public string GetText(int index) => string.Empty;
        public void Invalidate() => _changed?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, 0));
    }
}
