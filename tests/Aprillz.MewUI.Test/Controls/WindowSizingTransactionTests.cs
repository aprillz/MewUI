using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Window sizing transaction (issue #199 Fix C): the fit branch submits its target once per
/// change, accepts the applied (possibly clamped) client size, derives fixed axes from the
/// WindowSize spec, and floors degenerate targets at one pixel.
/// </summary>
[TestClass]
public sealed class WindowSizingTransactionTests
{
    [TestMethod]
    public void ClampedTarget_IsAccepted_AndNeverResubmitted()
    {
        var backend = new ApplyingWindowBackend { MinWidth = 300, MinHeight = 200 };
        var window = CreateFitWindow(backend, new PlainElement { Width = 50, Height = 40 });

        backend.SetClientSizeCount = 0;
        window.PerformLayout();

        Assert.AreEqual(new Size(300, 200), window.ClientSize, "the clamped applied size is accepted");
        Assert.AreEqual(1, backend.SetClientSizeCount);
        Assert.IsTrue(window.IsUpdatePassSettled);

        window.PerformLayout();

        // Applied != target stays that way; the same target must not be re-fought.
        Assert.AreEqual(1, backend.SetClientSizeCount);
    }

    [TestMethod]
    public void TargetChange_ResubmitsExactlyOnce()
    {
        var backend = new ApplyingWindowBackend();
        var element = new PlainElement { Width = 350, Height = 250 };
        var window = CreateFitWindow(backend, element);

        backend.SetClientSizeCount = 0;
        window.PerformLayout();
        Assert.AreEqual(new Size(350, 250), window.ClientSize);
        Assert.AreEqual(1, backend.SetClientSizeCount);

        element.Width = 420;
        window.PerformLayout();

        Assert.AreEqual(new Size(420, 250), window.ClientSize);
        Assert.AreEqual(2, backend.SetClientSizeCount);
    }

    [TestMethod]
    public void WindowSizeChange_ResetsTheTransaction()
    {
        var backend = new ApplyingWindowBackend();
        var window = CreateFitWindow(backend, new PlainElement { Width = 400, Height = 300 });

        window.PerformLayout();
        Assert.AreEqual(new Size(400, 300), window.ClientSize);

        window.WindowSize = WindowSize.Resizable(500, 400);
        window.PerformLayout();
        Assert.AreEqual(new Size(500, 400), window.ClientSize);

        // Returning to fit with the same content must re-submit the (value-identical) target.
        window.WindowSize = WindowSize.FitContentSize(1000, 1000);
        window.PerformLayout();

        Assert.AreEqual(new Size(400, 300), window.ClientSize);
    }

    [TestMethod]
    public void FitContentHeight_FixedWidthComesFromTheSpec()
    {
        var backend = new ApplyingWindowBackend();
        var window = CreateWindow(backend, new PlainElement { Width = 100, Height = 50 });
        window.WindowSize = WindowSize.FitContentHeight(fixedWidth: 300, maxHeight: 1000);

        window.PerformLayout();

        Assert.AreEqual(new Size(300, 50), window.ClientSize);
    }

    [TestMethod]
    public void FitContentWidth_FixedHeightComesFromTheSpec()
    {
        var backend = new ApplyingWindowBackend();
        var window = CreateWindow(backend, new PlainElement { Width = 120, Height = 50 });
        window.WindowSize = WindowSize.FitContentWidth(maxWidth: 1000, fixedHeight: 400);

        window.PerformLayout();

        Assert.AreEqual(new Size(120, 400), window.ClientSize);
    }

    [TestMethod]
    public void DegenerateContent_FloorsAtOnePixel()
    {
        var backend = new ApplyingWindowBackend();
        var window = CreateFitWindow(backend, new PlainElement { Width = 0, Height = 0 });

        window.PerformLayout();

        Assert.IsGreaterThanOrEqualTo(1, window.ClientSize.Width);
        Assert.IsGreaterThanOrEqualTo(1, window.ClientSize.Height);
    }

    private static Window CreateFitWindow(ApplyingWindowBackend backend, Element content)
    {
        var window = CreateWindow(backend, content);
        window.WindowSize = WindowSize.FitContentSize(1000, 1000);
        return window;
    }

    private static Window CreateWindow(ApplyingWindowBackend backend, Element content)
    {
        var window = new Window();
        window.Padding = new Thickness(0);
        window.AttachBackend(backend);
        backend.Window = window;
        window.SetClientSizeDip(800, 600);
        window.Content = content;
        return window;
    }

    private sealed class PlainElement : FrameworkElement
    {
        public PlainElement()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
        }

        protected override Size MeasureContent(Size availableSize) => Size.Empty;
    }
}
