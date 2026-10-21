// Ported from Windows Terminal src/cascadia/UnitTests_TerminalCore/TerminalApiTest.cpp (MIT).

using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Types;

namespace MewUI.Terminal.Test.Core;

[TestClass]
public sealed class TerminalApiTests
{
    private static TerminalCore CreateTerminal(int scrollback = 0)
    {
        var term = new TerminalCore(true);
        term.Create(new TilSize(100, 100), scrollback, new Renderer());
        return term;
    }

    [TestMethod]
    public void SetColorTableEntry()
    {
        var term = CreateTerminal();
        term.UpdateSettings(new TerminalSettings { HistorySize = 100, InitialRows = 100, InitialCols = 100 });

        term.RenderSettings.SetColorTableEntry(0, 100);
        term.RenderSettings.SetColorTableEntry(128, 100);
        term.RenderSettings.SetColorTableEntry(255, 100);

        Assert.ThrowsExactly<IndexOutOfRangeException>(() => term.RenderSettings.SetColorTableEntry(512, 100));
    }

    [TestMethod]
    public void PrintStringOfSurrogatePairs()
    {
        // Writing more code units than the buffer is wide used to loop forever.
        var term = CreateTerminal(3);

        var text = new System.Text.StringBuilder(600);
        for (int index = 0; index < 100; index++)
        {
            text.Append("\U0001040C\U0001041C\U0001042C");
        }

        var write = Task.Run(() => term.StateMachine.ProcessString(text.ToString()));
        Assert.IsTrue(write.Wait(TimeSpan.FromSeconds(2)), "The write got stuck.");
    }

    [TestMethod]
    public void CursorVisibilityViaStateMachine()
    {
        var term = CreateTerminal();
        var stateMachine = term.StateMachine;
        var cursor = term.MainBuffer.Cursor;

        stateMachine.ProcessString("Hello World");
        Assert.IsTrue(cursor.IsBlinking);
        Assert.IsTrue(cursor.IsVisible);

        stateMachine.ProcessString("\u001b[?12l");
        Assert.IsFalse(cursor.IsBlinking);
        Assert.IsTrue(cursor.IsVisible);

        stateMachine.ProcessString("\u001b[?12h");
        Assert.IsTrue(cursor.IsBlinking);
        Assert.IsTrue(cursor.IsVisible);

        stateMachine.ProcessString("\u001b[?12l");
        Assert.IsFalse(cursor.IsBlinking);
        Assert.IsTrue(cursor.IsVisible);

        stateMachine.ProcessString("\u001b[?12h");
        Assert.IsTrue(cursor.IsBlinking);
        Assert.IsTrue(cursor.IsVisible);

        stateMachine.ProcessString("\u001b[?25l");
        Assert.IsTrue(cursor.IsBlinking);
        Assert.IsFalse(cursor.IsVisible);

        stateMachine.ProcessString("\u001b[?25h");
        Assert.IsTrue(cursor.IsBlinking);
        Assert.IsTrue(cursor.IsVisible);

        stateMachine.ProcessString("\u001b[?12;25l");
        Assert.IsFalse(cursor.IsBlinking);
        Assert.IsFalse(cursor.IsVisible);
    }

    [TestMethod]
    public void AddHyperlink()
    {
        var term = CreateTerminal();
        var tbi = term.MainBuffer;
        var stateMachine = term.StateMachine;

        stateMachine.ProcessString("\u001b]8;;test.url\u001b\\");
        Assert.IsTrue(tbi.CurrentAttributes.IsHyperlink());
        Assert.AreEqual("test.url", tbi.GetHyperlinkUriFromId(tbi.CurrentAttributes.HyperlinkId));

        stateMachine.ProcessString("Hello World");
        Assert.IsTrue(tbi.CurrentAttributes.IsHyperlink());
        Assert.AreEqual("test.url", tbi.GetHyperlinkUriFromId(tbi.CurrentAttributes.HyperlinkId));

        stateMachine.ProcessString("\u001b]8;;\u001b\\");
        Assert.IsFalse(tbi.CurrentAttributes.IsHyperlink());
    }

    [TestMethod]
    public void AddHyperlinkCustomId()
    {
        var term = CreateTerminal();
        var tbi = term.MainBuffer;
        var stateMachine = term.StateMachine;

        stateMachine.ProcessString("\u001b]8;id=myId;test.url\u001b\\");
        Assert.IsTrue(tbi.CurrentAttributes.IsHyperlink());
        Assert.AreEqual("test.url", tbi.GetHyperlinkUriFromId(tbi.CurrentAttributes.HyperlinkId));
        Assert.AreEqual(tbi.GetHyperlinkId("test.url", "myId"), tbi.CurrentAttributes.HyperlinkId);

        stateMachine.ProcessString("Hello World");
        Assert.IsTrue(tbi.CurrentAttributes.IsHyperlink());
        Assert.AreEqual("test.url", tbi.GetHyperlinkUriFromId(tbi.CurrentAttributes.HyperlinkId));
        Assert.AreEqual(tbi.GetHyperlinkId("test.url", "myId"), tbi.CurrentAttributes.HyperlinkId);

        stateMachine.ProcessString("\u001b]8;;\u001b\\");
        Assert.IsFalse(tbi.CurrentAttributes.IsHyperlink());
    }

    [TestMethod]
    public void AddHyperlinkCustomIdDifferentUri()
    {
        var term = CreateTerminal();
        var tbi = term.MainBuffer;
        var stateMachine = term.StateMachine;

        stateMachine.ProcessString("\u001b]8;id=myId;test.url\u001b\\");
        Assert.IsTrue(tbi.CurrentAttributes.IsHyperlink());
        Assert.AreEqual("test.url", tbi.GetHyperlinkUriFromId(tbi.CurrentAttributes.HyperlinkId));
        Assert.AreEqual(tbi.GetHyperlinkId("test.url", "myId"), tbi.CurrentAttributes.HyperlinkId);

        var oldAttributes = tbi.CurrentAttributes;

        stateMachine.ProcessString("\u001b]8;id=myId;other.url\u001b\\");
        Assert.IsTrue(tbi.CurrentAttributes.IsHyperlink());
        Assert.AreEqual("other.url", tbi.GetHyperlinkUriFromId(tbi.CurrentAttributes.HyperlinkId));
        Assert.AreEqual(tbi.GetHyperlinkId("other.url", "myId"), tbi.CurrentAttributes.HyperlinkId);

        // The same custom id with another URI is another link; the first keeps its URI.
        Assert.AreEqual("test.url", tbi.GetHyperlinkUriFromId(oldAttributes.HyperlinkId));
        Assert.AreNotEqual(oldAttributes.HyperlinkId, tbi.CurrentAttributes.HyperlinkId);
    }

    [TestMethod]
    public void SetTaskbarProgress()
    {
        var term = CreateTerminal();
        var stateMachine = term.StateMachine;

        Assert.AreEqual(0, term.TaskbarState);
        Assert.AreEqual(0, term.TaskbarProgress);

        stateMachine.ProcessString("\u001b]9;4;1;50\u001b\\");
        Assert.AreEqual(1, term.TaskbarState);
        Assert.AreEqual(50, term.TaskbarProgress);

        stateMachine.ProcessString("\u001b]9;4;0;0\u001b\\");
        Assert.AreEqual(0, term.TaskbarState);
        Assert.AreEqual(0, term.TaskbarProgress);

        // An out of range state changes nothing.
        stateMachine.ProcessString("\u001b]9;4;5;50\u001b\\");
        Assert.AreEqual(0, term.TaskbarState);
        Assert.AreEqual(0, term.TaskbarProgress);

        // An out of range progress is clamped to 100.
        stateMachine.ProcessString("\u001b]9;4;1;999\u001b\\");
        Assert.AreEqual(1, term.TaskbarState);
        Assert.AreEqual(100, term.TaskbarProgress);

        // No parameters reset both.
        stateMachine.ProcessString("\u001b]9;4\u001b\\");
        Assert.AreEqual(0, term.TaskbarState);
        Assert.AreEqual(0, term.TaskbarProgress);

        // Extra parameters are ignored.
        stateMachine.ProcessString("\u001b]9;4;1;80;123\u001b\\");
        Assert.AreEqual(1, term.TaskbarState);
        Assert.AreEqual(80, term.TaskbarProgress);

        // A trailing semicolon is accepted, and the error state keeps the progress.
        stateMachine.ProcessString("\u001b]9;4;2;\u001b\\");
        Assert.AreEqual(2, term.TaskbarState);
        Assert.AreEqual(80, term.TaskbarProgress);

        // The indeterminate state ignores the progress.
        stateMachine.ProcessString("\u001b]9;4;3;75\u001b\\");
        Assert.AreEqual(3, term.TaskbarState);
        Assert.AreEqual(80, term.TaskbarProgress);

        // Clearing ignores the progress.
        stateMachine.ProcessString("\u001b]9;4;0;50\u001b\\");
        Assert.AreEqual(0, term.TaskbarState);
        Assert.AreEqual(0, term.TaskbarProgress);

        // The error state without progress shows a minimum amount.
        stateMachine.ProcessString("\u001b]9;4;2;\u001b\\");
        Assert.AreEqual(2, term.TaskbarState);
        Assert.IsGreaterThan(0, term.TaskbarProgress);
    }

    [TestMethod]
    public void SetWorkingDirectory()
    {
        var term = CreateTerminal();
        var stateMachine = term.StateMachine;

        Assert.AreEqual(string.Empty, term.WorkingDirectory);

        // Invalid sequences leave the directory empty.
        string[] invalidSequences =
        [
            "\u001b]9;9\u001b\\",
            "\u001b]9;9\"\u001b\\",
            "\u001b]9;9\"C:\\\"\u001b\\",
            "\u001b]9;9;\"C:\\invalid path \"with\" quotes\"\u001b\\",
            "\u001b]9;9;\"\u001b\\",
            "\u001b]9;9;\"\"\u001b\\",
            "\u001b]9;9;\"\"\"\u001b\\",
            "\u001b]9;9;\"\"\"\"\u001b\\",
            "\u001b]9;9;No quotes \"until\" later\u001b\\",
        ];
        foreach (string sequence in invalidSequences)
        {
            stateMachine.ProcessString(sequence);
            Assert.AreEqual(string.Empty, term.WorkingDirectory, $"Sequence {sequence.Replace("\u001b", "ESC")}");
        }

        stateMachine.ProcessString("\u001b]9;9;\"C:\\\"\u001b\\");
        Assert.AreEqual("C:\\", term.WorkingDirectory);

        stateMachine.ProcessString("\u001b]9;9;\"C:\\Program Files\"\u001b\\");
        Assert.AreEqual("C:\\Program Files", term.WorkingDirectory);

        stateMachine.ProcessString("\u001b]9;9;\"D:\\\u4E2D\u6587\"\u001b\\");
        Assert.AreEqual("D:\\\u4E2D\u6587", term.WorkingDirectory);

        // Without quotation marks.
        stateMachine.ProcessString("\u001b]9;9;C:\\\u001b\\");
        Assert.AreEqual("C:\\", term.WorkingDirectory);

        stateMachine.ProcessString("\u001b]9;9;C:\\Program Files\u001b\\");
        Assert.AreEqual("C:\\Program Files", term.WorkingDirectory);

        stateMachine.ProcessString("\u001b]9;9;D:\\\u4E2D\u6587\u001b\\");
        Assert.AreEqual("D:\\\u4E2D\u6587", term.WorkingDirectory);
    }
}
