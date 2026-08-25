using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Paste offers itself only when there is something to paste. The clipboard is asked through
/// <see cref="IClipboardService.HasText"/> so a re-query costs a format probe, not a fetch.
/// </summary>
[TestClass]
public sealed class TextPasteAvailabilityTests
{
    [TestMethod]
    public void EmptyClipboard_LeavesPasteUnavailable()
    {
        var clipboard = new ProbeClipboard();
        var (window, textBox) = CreateHost(clipboard);

        Assert.IsFalse(window.CommandRouter.CanExecute(StandardCommands.Paste, window.CommandRouter.CaptureTarget(textBox)));
        Assert.AreEqual(0, clipboard.FetchCount, "asking whether text exists must not fetch it");
    }

    [TestMethod]
    public void ClipboardWithText_MakesPasteAvailable()
    {
        var clipboard = new ProbeClipboard { Text = "payload" };
        var (window, textBox) = CreateHost(clipboard);

        Assert.IsTrue(window.CommandRouter.CanExecute(StandardCommands.Paste, window.CommandRouter.CaptureTarget(textBox)));
        Assert.AreEqual(0, clipboard.FetchCount);
    }

    [TestMethod]
    public void ReadOnlyTarget_LeavesPasteUnavailable_EvenWithText()
    {
        var clipboard = new ProbeClipboard { Text = "payload" };
        var (window, textBox) = CreateHost(clipboard, readOnly: true);

        Assert.IsFalse(window.CommandRouter.CanExecute(StandardCommands.Paste, window.CommandRouter.CaptureTarget(textBox)));
    }

    private static (Window window, TextBox textBox) CreateHost(IClipboardService clipboard, bool readOnly = false)
    {
        var textBox = new TextBox
        {
            ClipboardService = clipboard,
            IsReadOnly = readOnly,
        };

        var window = new Window();
        window.AttachBackend(new HeadlessWindowBackend());
        window.Content = textBox;
        return (window, textBox);
    }

    private sealed class ProbeClipboard : IClipboardService
    {
        public string Text { get; set; } = string.Empty;
        public int FetchCount { get; private set; }

        public bool TrySetText(string text)
        {
            Text = text;
            return true;
        }

        public bool TryGetText(out string text)
        {
            FetchCount++;
            text = Text;
            return Text.Length > 0;
        }

        public bool HasText() => Text.Length > 0;
    }
}
