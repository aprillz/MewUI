using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Core;

[TestClass]
public sealed class DpiReattachTests
{
    private sealed class DpiTracker : FrameworkElement
    {
        public readonly List<(uint OldDpi, uint NewDpi)> Changes = new();

        protected override void OnDpiChanged(uint oldDpi, uint newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Changes.Add((oldDpi, newDpi));
        }
    }

    [TestMethod]
    public void Reattach_AfterWindowDpiChanged_RaisesDpiChangedOnCachedSubtree()
    {
        var window = new Window();
        var tracker = new DpiTracker();
        var page = new StackPanel();
        page.Add(tracker);

        window.Content = page;
        Assert.AreEqual(96u, tracker.GetDpi());

        window.Content = null;
        window.SetDpi(144);
        window.Content = page;

        Assert.AreEqual(1, tracker.Changes.Count);
        Assert.AreEqual((96u, 144u), tracker.Changes[0]);
        Assert.AreEqual(144u, tracker.GetDpi());
        Assert.IsTrue(tracker.IsMeasureDirty);
    }

    [TestMethod]
    public void Reattach_WithSameDpi_DoesNotRaiseDpiChanged()
    {
        var window = new Window();
        var tracker = new DpiTracker();
        var page = new StackPanel();
        page.Add(tracker);

        window.Content = page;
        Assert.AreEqual(96u, tracker.GetDpi());

        window.Content = null;
        window.Content = page;

        Assert.AreEqual(0, tracker.Changes.Count);
    }

    [TestMethod]
    public void FirstAttach_WithoutPriorDpi_DoesNotRaiseDpiChanged()
    {
        var window = new Window();
        window.SetDpi(144);
        var tracker = new DpiTracker();

        window.Content = tracker;

        Assert.AreEqual(0, tracker.Changes.Count);
        Assert.AreEqual(144u, tracker.GetDpi());
    }
}
