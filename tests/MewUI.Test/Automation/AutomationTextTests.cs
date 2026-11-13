using Aprillz.MewUI;
using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Automation;

/// <summary>The text pattern of edit boxes: units, selection, geometry, and the notifications readers follow.</summary>
[TestClass]
[DoNotParallelize]
public sealed class AutomationTextTests
{
    private const string SAMPLE = "hello world\nsecond line";

    private static (MultiLineTextBox Box, ITextProvider Text) Host()
    {
        var box = new MultiLineTextBox { Width = 240, Height = 80, Text = SAMPLE };
        var window = HeadlessWindow.Create();
        window.Content = new StackPanel().Children(box);
        window.PerformLayout();
        var text = box.GetAutomationPeer()?.GetProvider<ITextProvider>();
        Assert.IsNotNull(text, "an edit box supports the text pattern");
        return (box, text);
    }

    [TestMethod]
    public void Units_CoverCharactersWordsLinesAndTheDocument()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (_, text) = Host();

        Assert.AreEqual(SAMPLE, text.Text);
        Assert.AreEqual(new TextRange(4, 1), text.GetUnitAt(4, TextUnit.Character));
        Assert.AreEqual(new TextRange(0, 6), text.GetUnitAt(2, TextUnit.Word), "a word carries the space after it");
        Assert.AreEqual(new TextRange(6, 5), text.GetUnitAt(8, TextUnit.Word));
        Assert.AreEqual(new TextRange(0, 12), text.GetUnitAt(3, TextUnit.Line), "a line carries its break");
        Assert.AreEqual(new TextRange(12, 11), text.GetUnitAt(15, TextUnit.Line));
        Assert.AreEqual(new TextRange(0, SAMPLE.Length), text.GetUnitAt(5, TextUnit.Document));
    }

    [TestMethod]
    public void Selection_FollowsTheBox_AndSelectingAnnouncesIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (box, text) = Host();
        var peer = box.GetAutomationPeer()!;
        var recorder = new EventRecorder();
        AutomationEvents.AddSink(recorder);
        try
        {
            text.Select(6, 5);
            Assert.AreEqual(6, box.SelectionStart);
            Assert.AreEqual(5, box.SelectionLength);
            CollectionAssert.AreEqual(new[] { new TextRange(6, 5) }, text.GetSelection().ToArray());
            Assert.IsGreaterThan(0, recorder.Count(peer, AutomationEventKind.TextSelectionChanged));

            box.Text = "changed";
            Assert.IsGreaterThan(0, recorder.Count(peer, AutomationEventKind.TextChanged));
        }
        finally
        {
            AutomationEvents.RemoveSink(recorder);
        }
    }

    [TestMethod]
    public void Geometry_FindsCharactersAndTheirBounds()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var (_, text) = Host();

        var firstLine = text.GetBounds(0, 5);
        Assert.HasCount(1, firstLine);
        Assert.IsGreaterThan(0, firstLine[0].Width);
        var bothLines = text.GetBounds(0, SAMPLE.Length);
        Assert.HasCount(2, bothLines, "one rectangle per line");
        Assert.IsGreaterThan(bothLines[0].Bottom - 1, bothLines[1].Top);

        var seventh = text.GetBounds(7, 1)[0];
        Assert.AreEqual(7, text.GetOffsetAt(new Point(seventh.X + seventh.Width / 2, seventh.Y + seventh.Height / 2)));
        var second = text.GetBounds(14, 1)[0];
        Assert.AreEqual(14, text.GetOffsetAt(new Point(second.X + second.Width / 2, second.Y + second.Height / 2)));
    }

    private sealed class EventRecorder : IAutomationEventSink
    {
        private readonly List<(AutomationPeer Peer, AutomationEventKind Kind)> _events = [];

        public void OnPropertyChanged(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue)
        {
        }

        public void OnStructureChanged(AutomationPeer peer)
        {
        }

        public void OnFocusChanged(AutomationPeer peer)
        {
        }

        public void OnEvent(AutomationPeer peer, AutomationEventKind kind) => _events.Add((peer, kind));

        public int Count(AutomationPeer peer, AutomationEventKind kind) => _events.Count(entry => entry.Peer == peer && entry.Kind == kind);
    }
}
