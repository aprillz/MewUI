using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Contract coverage for the <see cref="Popup"/> control: anchor placement, focus neutrality and
/// the close policy it opts into through <see cref="Popup.StaysOpen"/>.
/// </summary>
[TestClass]
public sealed class PopupTests
{
    private const double WINDOW_WIDTH = 800;
    private const double WINDOW_HEIGHT = 600;

    [TestMethod]
    public void ShowAt_PlacesBelowTheAnchor()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, owner) = CreateWindow();
        var popup = CreatePopup(120, 60);
        var anchor = new Rect(100, 100, 40, 20);

        var bounds = popup.ShowAt(owner, anchor);

        Assert.AreEqual(anchor.Bottom, bounds.Y, "the popup opened somewhere other than under the anchor");
        Assert.AreEqual(anchor.X, bounds.X);
        Assert.IsTrue(popup.IsOpen);
        popup.Close();
    }

    [TestMethod]
    public void ShowAt_FlipsAboveWhenBelowHasNoRoom()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, owner) = CreateWindow();
        var popup = CreatePopup(120, 200);
        var anchor = new Rect(100, WINDOW_HEIGHT - 40, 40, 20);

        var bounds = popup.ShowAt(owner, anchor);

        Assert.IsLessThanOrEqualTo(anchor.Y, bounds.Bottom, "the popup stayed below an anchor with no room under it");
        popup.Close();
    }

    [TestMethod]
    public void ShowAt_DoesNotMoveTheFocus()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, owner) = CreateWindow();
        var focusable = new Button().Content("focus me");
        var popup = new Popup { Content = focusable };
        var focusHolder = new Button().Content("holder");
        owner.Child = focusHolder;
        window.PerformLayout();
        focusHolder.Focus();
        var focusedBefore = window.FocusManager.FocusedElement;

        popup.ShowAt(owner, new Rect(10, 10, 20, 20));

        Assert.AreSame(focusedBefore, window.FocusManager.FocusedElement, "opening the popup moved the keyboard focus");
        popup.Close();
    }

    [TestMethod]
    public void OutsidePress_ClosesOnlyWhenStaysOpenIsFalse()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, owner) = CreateWindow();
        var unrelated = new Border();
        window.ShowPopup(owner, unrelated, _ => new Rect(0, 0, 1, 1));
        window.ClosePopup(unrelated);

        var transient = CreatePopup(80, 40);
        transient.ShowAt(owner, new Rect(10, 10, 20, 20));
        window.RequestClosePopups(PopupCloseRequest.PointerDown(pointerLeaf: null));
        Assert.IsFalse(transient.IsOpen, "an outside press left a transient popup open");

        var pinned = CreatePopup(80, 40);
        pinned.StaysOpen = true;
        pinned.ShowAt(owner, new Rect(10, 10, 20, 20));
        window.RequestClosePopups(PopupCloseRequest.PointerDown(pointerLeaf: null));
        Assert.IsTrue(pinned.IsOpen, "an outside press closed a popup that opted out of the policy");
        pinned.Close();
    }

    [TestMethod]
    public void Close_RaisesClosedOnceWithTheCloseKind()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, owner) = CreateWindow();
        var popup = CreatePopup(80, 40);
        int closedCount = 0;
        PopupCloseKind observed = PopupCloseKind.Policy;
        popup.Closed += (_, e) => { closedCount++; observed = e.Kind; };

        popup.ShowAt(owner, new Rect(10, 10, 20, 20));
        popup.Close();
        popup.Close();

        Assert.AreEqual(1, closedCount, "Closed did not fire exactly once");
        Assert.AreEqual(PopupCloseKind.UserInitiated, observed);
        Assert.IsFalse(popup.IsOpen);
    }

    [TestMethod]
    public void LifecycleClose_ReportsLifecycleKind()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, owner) = CreateWindow();
        var popup = CreatePopup(80, 40);
        popup.StaysOpen = true;
        PopupCloseKind observed = PopupCloseKind.Policy;
        popup.Closed += (_, e) => observed = e.Kind;

        popup.ShowAt(owner, new Rect(10, 10, 20, 20));
        window.RequestClosePopups(PopupCloseRequest.Lifecycle());

        Assert.IsFalse(popup.IsOpen, "the lifecycle close left the popup open");
        Assert.AreEqual(PopupCloseKind.Lifecycle, observed);
    }

    [TestMethod]
    public void IsOpen_IsAReadOnlyPropertyThatTracksTheSurface()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, owner) = CreateWindow();
        var popup = CreatePopup(80, 40);

        Assert.IsTrue(Popup.IsOpenProperty.IsReadOnly, "a trigger could set IsOpen instead of observing it");
        Assert.AreEqual(nameof(Popup.IsOpen), Popup.IsOpenProperty.Name,
            "IsOpen is not backed by a property, so nothing can bind to or style off it");
        Assert.IsFalse(popup.IsOpen);

        popup.ShowAt(owner, new Rect(10, 10, 20, 20));
        Assert.IsTrue(popup.IsOpen, "the property did not follow the surface");

        popup.Close();
        Assert.IsFalse(popup.IsOpen);
    }

    [TestMethod]
    public void MoveTo_RepositionsAnOpenPopupAndIgnoresAClosedOne()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var (window, owner) = CreateWindow();
        var popup = CreatePopup(80, 40);

        Assert.AreEqual(default, popup.MoveTo(new Rect(10, 10, 20, 20)), "a closed popup reported a placement");

        popup.ShowAt(owner, new Rect(10, 10, 20, 20));
        var moved = popup.MoveTo(new Rect(200, 300, 20, 20));

        Assert.AreEqual(200, moved.X);
        Assert.AreEqual(320, moved.Y);
        popup.Close();
    }

    private static (Window window, Border owner) CreateWindow()
    {
        var window = HeadlessWindow.Create(WINDOW_WIDTH, WINDOW_HEIGHT);
        var owner = new Border();
        window.Content = owner;
        window.PerformLayout();
        return (window, owner);
    }

    private static Popup CreatePopup(double width, double height)
        => new() { Content = new Border { Width = width, Height = height } };
}
