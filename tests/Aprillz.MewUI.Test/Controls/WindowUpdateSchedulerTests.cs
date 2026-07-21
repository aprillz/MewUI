using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;

namespace MewUI.Test.Controls;

/// <summary>
/// Window update-pass scheduler (issue #199): pass-internal invalidation must be merged into the
/// running pass instead of posting new work, convergence must not depend on tree dirty scans, and
/// synchronous client-size re-entry must not nest a layout pass.
/// </summary>
[TestClass]
public sealed class WindowUpdateSchedulerTests
{
    [TestMethod]
    public void PassInternalInvalidation_IsConsumedWithoutPostingNewWork()
    {
        var backend = new TrackingBackend();
        var window = CreateWindow(backend);
        var element = new InvalidateArrangeOnMeasure();
        window.Content = element;

        window.PerformLayout();

        // The mid-pass InvalidateArrange bump is consumed by the convergence loop; a settled pass
        // never runs the continuation branch.
        Assert.IsTrue(window.IsUpdatePassSettled);
        Assert.IsGreaterThanOrEqualTo(1, element.MeasureCount);
    }

    [TestMethod]
    public void MidPassMeasureInvalidation_IsAppliedWithinTheSamePass()
    {
        var backend = new TrackingBackend();
        var window = CreateWindow(backend);
        var element = new GrowOnFirstArrange
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = element;

        window.PerformLayout();

        // The first arrange invalidated measure with a new size; the same PerformLayout call must
        // re-run and settle on it (request preserved, not lost).
        Assert.AreEqual(new Size(200, 100), new Size(element.Bounds.Width, element.Bounds.Height));
        Assert.IsTrue(window.IsUpdatePassSettled);
    }

    [TestMethod]
    public void NonConvergingTree_StopsAtPassBudgetAndStaysUnsettled()
    {
        var backend = new TrackingBackend();
        var window = CreateWindow(backend);
        var element = new NeverSettles
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = element;

        window.PerformLayout();

        // One PerformLayout call is one bounded pass (8 rounds), never a chain.
        Assert.AreEqual(8, element.ArrangeCount);
        Assert.IsFalse(window.IsUpdatePassSettled);

        window.PerformLayout();

        Assert.AreEqual(16, element.ArrangeCount);
        Assert.IsFalse(window.IsUpdatePassSettled);
    }

    [TestMethod]
    public void CleanWindow_SecondLayoutEarlyOuts_EvenWithNeverMeasuredChild()
    {
        var backend = new TrackingBackend();
        var window = CreateWindow(backend);

        // The panel never measures its second child, so that child stays measure-dirty forever -
        // the overlay-chrome/hidden-element shape that kept the old full-tree dirty scan
        // permanently true and forced every layout through all 8 passes.
        var measured = new CountingElement();
        var neverMeasured = new CountingElement();
        var panel = new FirstChildOnlyPanel();
        panel.AddRange(measured, neverMeasured);
        window.Content = panel;

        window.PerformLayout();
        int measureCountAfterFirst = measured.MeasureCount;
        Assert.AreEqual(0, neverMeasured.MeasureCount);

        window.PerformLayout();

        Assert.AreEqual(measureCountAfterFirst, measured.MeasureCount);
        Assert.AreEqual(0, neverMeasured.MeasureCount);
    }

    [TestMethod]
    public void ContentChange_AfterEarlyOut_StillRelaysOut()
    {
        var backend = new TrackingBackend();
        var window = CreateWindow(backend);
        var element = new CountingElement
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Width = 100,
            Height = 50,
        };
        window.Content = element;

        window.PerformLayout();
        window.PerformLayout();
        Assert.AreEqual(new Size(100, 50), new Size(element.Bounds.Width, element.Bounds.Height));

        element.Width = 300;
        window.PerformLayout();

        Assert.AreEqual(300, element.Bounds.Width);
    }

    [TestMethod]
    public void FitContent_SynchronousResizeReentry_MergesIntoRunningPass()
    {
        var backend = new TrackingBackend { ReenterLayoutOnSetClientSize = true };
        var window = CreateWindow(backend);
        window.WindowSize = WindowSize.FitContentSize(1000, 1000);
        window.Content = new InvalidateArrangeOnMeasure
        {
            Width = 200,
            Height = 100,
        };

        backend.SetClientSizeCount = 0;
        window.PerformLayout();

        // The fit target is applied through a backend that re-enters PerformLayout synchronously
        // (Win32 WM_SIZE). The nested call must merge into the running pass, the pass must settle
        // on the applied size, and no continuation may run (the #199 spin engine).
        Assert.AreEqual(new Size(200, 100), window.ClientSize);
        Assert.IsTrue(window.IsUpdatePassSettled);
        Assert.AreEqual(1, backend.SetClientSizeCount);

        window.PerformLayout();

        // Target unchanged: no re-request, still settled.
        Assert.AreEqual(1, backend.SetClientSizeCount);
        Assert.IsTrue(window.IsUpdatePassSettled);
    }

    private static Window CreateWindow(TrackingBackend backend)
    {
        var window = new Window();
        window.Padding = new Thickness(0);
        window.AttachBackend(backend);
        backend.Window = window;
        window.SetClientSizeDip(800, 600);
        return window;
    }

    private sealed class TrackingBackend : IWindowBackend
    {
        public Window? Window;
        public int InvalidateCount;
        public int SetClientSizeCount;
        public bool ReenterLayoutOnSetClientSize;

        public nint Handle => 1;

        public void Invalidate(bool erase) => InvalidateCount++;

        public void SetClientSize(double widthDip, double heightDip)
        {
            SetClientSizeCount++;
            if (ReenterLayoutOnSetClientSize && Window != null)
            {
                // Win32 shape: SetWindowPos delivers WM_SIZE synchronously; the handler records the
                // applied size and calls back into PerformLayout.
                Window.SetClientSizeDip(widthDip, heightDip);
                Window.PerformLayout();
            }
        }

        public void SetResizable(bool resizable) { }
        public void PresentSurface() { }
        public void Hide() { }
        public void Close() { }
        public void SetTitle(string title) { }
        public void SetIcon(IconSource? icon) { }
        public Point GetPosition() => default;
        public void SetPosition(double leftDip, double topDip) { }
        public void CaptureMouse() { }
        public void ReleaseMouseCapture() { }
        public Point ClientToScreen(Point clientPointDip) => clientPointDip;
        public Point ScreenToClient(Point screenPointPx) => screenPointPx;
        public void CenterOnOwner() { }
        public void EnsureTheme(bool isDark) { }
        public void Activate() { }
        public void SetOwner(nint ownerHandle) { }
        public void SetEnabled(bool enabled) { }
        public void SetOpacity(double opacity) { }
        public void SetAllowsTransparency(bool allowsTransparency) { }
        public void SetCursor(CursorType cursorType) { }
        public void SetImeMode(ImeMode mode) { }
        public void CancelImeComposition() { }
        public void Dispose() { }
    }

    private sealed class CountingElement : FrameworkElement
    {
        public int MeasureCount;

        protected override Size MeasureContent(Size availableSize)
        {
            MeasureCount++;
            return new Size(100, 50);
        }
    }

    // The ScrollViewer shape: measuring repositions overlay state and invalidates arrange mid-pass.
    private sealed class InvalidateArrangeOnMeasure : FrameworkElement
    {
        public int MeasureCount;

        protected override Size MeasureContent(Size availableSize)
        {
            MeasureCount++;
            InvalidateArrange();
            return new Size(100, 50);
        }
    }

    private sealed class GrowOnFirstArrange : FrameworkElement
    {
        private bool _grown;

        protected override Size MeasureContent(Size availableSize)
            => _grown ? new Size(200, 100) : new Size(100, 50);

        protected override void ArrangeContent(Rect bounds)
        {
            if (!_grown)
            {
                _grown = true;
                InvalidateMeasure();
            }
        }
    }

    // Alternates its desired size and re-invalidates from arrange: every round produces a fresh
    // arrival, so the pass can never converge.
    private sealed class NeverSettles : FrameworkElement
    {
        public int ArrangeCount;
        private int _tick;

        protected override Size MeasureContent(Size availableSize)
        {
            _tick++;
            return new Size(100 + (_tick % 2), 50);
        }

        protected override void ArrangeContent(Rect bounds)
        {
            ArrangeCount++;
            InvalidateMeasure();
        }
    }

    // Measures only its first child; the second stays measure-dirty forever by design.
    private sealed class FirstChildOnlyPanel : Panel
    {
        protected override Size MeasureContent(Size availableSize)
        {
            if (Count > 0 && this[0] is UIElement first)
            {
                first.Measure(availableSize);
                return first.DesiredSize;
            }

            return Size.Empty;
        }

        protected override void ArrangeContent(Rect bounds)
        {
            if (Count > 0 && this[0] is UIElement first)
            {
                first.Arrange(bounds);
            }
        }
    }
}
