using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Input;

/// <summary>
/// A drag is routed once along one path: from the element under the pointer up to the window, each element with
/// <see cref="UIElement.AllowDrop"/> getting each event once, whether the drag comes from another application or from
/// this one. A target that says nothing gets the default (a standard format is accepted); a target that refuses keeps
/// its refusal.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DragRoutingTests
{
    private static readonly Point _inside = new(50, 20);

    [TestMethod]
    public void AnExternalDragReachesTheWindowOncePerEvent()
    {
        var (window, _, counts) = CreateWindow();

        WindowDragDropRouter.OnExternalDragEnter(window, FileDrag(_inside));
        WindowDragDropRouter.OnExternalDragOver(window, FileDrag(_inside));
        WindowDragDropRouter.OnExternalDragLeave(window, FileDrag(_inside));
        WindowDragDropRouter.OnExternalDragEnter(window, FileDrag(_inside));
        var effect = WindowDragDropRouter.OnExternalDrop(window, FileDrag(_inside));

        Assert.AreEqual("enter=2 over=3 leave=1 drop=1", counts.ToString(), "an event reached the window more than once");
        Assert.AreEqual(DragDropEffects.Copy, effect, "a standard format with no handler is not accepted by default");
    }

    [TestMethod]
    public void AnExternalDragUnhandledByAnElementReachesTheWindowOnce()
    {
        var (window, target, counts) = CreateWindow();
        target.AllowDrop = true;
        int targetDrops = 0;
        target.Drop += _ => targetDrops++;

        WindowDragDropRouter.OnExternalDragEnter(window, FileDrag(_inside));
        _ = WindowDragDropRouter.OnExternalDrop(window, FileDrag(_inside));

        Assert.AreEqual(1, targetDrops);
        Assert.AreEqual("enter=1 over=1 leave=0 drop=1", counts.ToString());
    }

    [TestMethod]
    public void AWindowThatRefusesAnExternalDropKeepsItsRefusal()
    {
        var (window, _, _) = CreateWindow();
        window.DragOver += args => args.Accepted = false;
        window.Drop += args => args.Accepted = false;

        var over = FileDrag(_inside);
        WindowDragDropRouter.OnExternalDragEnter(window, over);
        var effect = WindowDragDropRouter.OnExternalDrop(window, FileDrag(_inside));

        Assert.IsFalse(over.Accepted, "the drag over was accepted although the window refused it");
        Assert.AreEqual(DragDropEffects.None, effect, "the drop was accepted although the window refused it");
    }

    [TestMethod]
    public void AnExternalDropOutsideEveryElementReachesTheWindow()
    {
        var (window, _, counts) = CreateWindow();
        var outside = new Point(350, 250);

        WindowDragDropRouter.OnExternalDragEnter(window, FileDrag(outside));
        var effect = WindowDragDropRouter.OnExternalDrop(window, FileDrag(outside));

        Assert.AreEqual("enter=1 over=1 leave=0 drop=1", counts.ToString());
        Assert.AreEqual(DragDropEffects.Copy, effect);
    }

    [TestMethod]
    public void AnInternalDragFollowsTheSameRules()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create(400, 300);
        var panel = new StackPanel();
        var source = new Border { Width = 200, Height = 40, CanDrag = true };
        var target = new Border { Width = 200, Height = 100, AllowDrop = true };
        panel.Add(source);
        panel.Add(target);
        window.Content = panel;
        window.AllowDrop = true;
        window.PerformLayout();

        source.DragStarting += args => args.Data = new DataObject(new Dictionary<string, object> { [StandardDataFormats.Text] = "moved" });
        DragDropEffects? completed = null;
        source.DragCompleted += args => completed = args.FinalEffect;
        var counts = new Counts(window);
        int targetDrops = 0;
        target.Drop += _ => targetDrops++;

        var start = new Point(50, 20);
        var over = new Point(50, 90);
        window.SendMouseMove(start);
        window.SendMouseDown(start);
        window.SendMouseDrag(new Point(50, 60));
        window.SendMouseDrag(over);
        window.SendMouseUp(over);

        Assert.AreEqual(1, targetDrops, "the drop did not reach the element under the pointer");
        Assert.AreEqual(1, counts.Drop, "the drop did not bubble to the window once");
        Assert.AreEqual(DragDropEffects.Copy, completed, "a standard format with no handler is not accepted by default");
    }

    private static (Window Window, Border Target, Counts Counts) CreateWindow()
    {
        var window = HeadlessWindow.Create(400, 300);
        var target = new Border { Width = 200, Height = 100, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        window.Content = target;
        window.AllowDrop = true;
        window.PerformLayout();
        return (window, target, new Counts(window));
    }

    private static DragEventArgs FileDrag(Point position) => new(
        new DataObject(new Dictionary<string, object> { [StandardDataFormats.StorageItems] = new[] { "/tmp/a.txt" } }),
        position,
        position,
        DragDropEffects.Copy | DragDropEffects.Move);

    private sealed class Counts
    {
        public Counts(UIElement element)
        {
            element.DragEnter += _ => Enter++;
            element.DragOver += _ => Over++;
            element.DragLeave += _ => Leave++;
            element.Drop += _ => Drop++;
        }

        public int Enter { get; private set; }

        public int Over { get; private set; }

        public int Leave { get; private set; }

        public int Drop { get; private set; }

        public override string ToString() => $"enter={Enter} over={Over} leave={Leave} drop={Drop}";
    }
}
