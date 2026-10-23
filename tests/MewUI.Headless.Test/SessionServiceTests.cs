using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;
using Aprillz.MewUI.Platform;

namespace MewUI.Headless.Test;

/// <summary>The session's clipboard, dialogs, drag-drop, wheel and async entry points.</summary>
[TestClass]
public sealed class SessionServiceTests
{
    [TestMethod]
    public void Clipboard_IsSharedWithTheApplication()
    {
        var session = HeadlessTestHost.Shared;
        session.Clipboard.Text = "from test";

        Assert.IsTrue(session.Run(() => Application.Current.PlatformServices.Clipboard!.TryGetText(out string text) && text == "from test"));

        session.Run(() => Application.Current.PlatformServices.Clipboard!.TrySetText("from app"));
        Assert.AreEqual("from app", session.Clipboard.Text);
    }

    [TestMethod]
    public void MessageBoxes_AnswerFromTheQueueAndRecordTheCall()
    {
        var session = HeadlessTestHost.Shared;
        session.MessageBoxes.Clear();
        session.MessageBoxes.Enqueue(true);

        bool? answered = session.Run(() => NativeMessageBox.Show("Save changes?", "Editor", NativeMessageBoxButtons.YesNo, NativeMessageBoxIcon.Question));
        bool? unanswered = session.Run(() => NativeMessageBox.Show("Again?", "Editor", NativeMessageBoxButtons.OkCancel, NativeMessageBoxIcon.None));

        Assert.IsTrue(answered);
        Assert.IsNull(unanswered);
        var calls = session.MessageBoxes.Calls;
        Assert.HasCount(2, calls);
        Assert.AreEqual("Save changes?", calls[0].Text);
        Assert.AreEqual(NativeMessageBoxButtons.YesNo, calls[0].Buttons);
        Assert.IsTrue(calls[0].Result);
        Assert.IsNull(calls[1].Result);
    }

    [TestMethod]
    public void FileDialogs_AnswerFromTheQueuesAndCancelWhenEmpty()
    {
        var session = HeadlessTestHost.Shared;
        session.FileDialogs.Clear();
        session.FileDialogs.EnqueueOpen("/tmp/a.txt", "/tmp/b.txt");
        session.FileDialogs.EnqueueSave("/tmp/out.txt");

        string? opened = session.Run(() => FileDialog.OpenFile());
        string? saved = session.Run(() => FileDialog.SaveFile());
        string? folder = session.Run(() => FileDialog.SelectFolder());

        Assert.AreEqual("/tmp/a.txt", opened);
        Assert.AreEqual("/tmp/out.txt", saved);
        Assert.IsNull(folder);
        var calls = session.FileDialogs.Calls;
        Assert.HasCount(3, calls);
        Assert.AreEqual(HeadlessFileDialogKind.Open, calls[0].Kind);
        Assert.AreEqual(HeadlessFileDialogKind.Folder, calls[2].Kind);
        Assert.IsEmpty(calls[2].Paths);
    }

    [TestMethod]
    public void DragDrop_DeliversTheDataToTheElementUnderThePoint()
    {
        var target = new Border { Width = 100, Height = 100, AllowDrop = true };
        string? dropped = null;
        target.Drop += args =>
        {
            args.Data.TryGetData(StandardDataFormats.Text, out dropped);
            args.Accepted = true;
            args.Effect = DragDropEffects.Copy;
        };
        var window = HeadlessTestHost.ShowWindow(target);
        try
        {
            var data = new DataObject();
            data.SetText("payload");

            var effect = window.SendDragDrop(target.CenterInWindow(), data);

            Assert.AreEqual(DragDropEffects.Copy, effect);
            Assert.AreEqual("payload", dropped);
        }
        finally
        {
            HeadlessTestHost.Close(window);
        }
    }

    [TestMethod]
    public void MouseWheel_ScrollsTheScrollViewerUnderThePoint()
    {
        var viewer = new ScrollViewer { Content = new Border { Height = 2000 } };
        var window = HeadlessTestHost.ShowWindow(viewer);
        try
        {
            window.SendMouseWheel(viewer.CenterInWindow(), -3);

            Assert.IsGreaterThan(0, HeadlessTestHost.Shared.Run(() => viewer.VerticalOffset));
        }
        finally
        {
            HeadlessTestHost.Close(window);
        }
    }

    [TestMethod]
    public async Task RunAsync_ResumesOnTheUIThreadAfterAwait()
    {
        var session = HeadlessTestHost.Shared;
        bool onUiThreadAfterAwait = await session.RunAsync(async () =>
        {
            await Task.Delay(10);
            return session.IsOnUIThread;
        });

        Assert.IsTrue(onUiThreadAfterAwait);
        Assert.IsFalse(session.IsOnUIThread);
    }

    [TestMethod]
    public void Run_RethrowsTheActionException()
    {
        var session = HeadlessTestHost.Shared;

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => session.Run(() => throw new InvalidOperationException("from UI thread")));

        Assert.AreEqual("from UI thread", error.Message);
        Assert.IsNotNull(HeadlessSession.Current, "the session must survive an exception in a job");
    }

    [TestMethod]
    public void ShowDialog_RunsANestedLoopThatOtherRunCallsCanDrive()
    {
        var session = HeadlessTestHost.Shared;
        var owner = HeadlessTestHost.ShowWindow(new Border());
        var dialog = new Window { WindowSize = WindowSize.Resizable(200, 120), Content = new Border() };
        bool returned = false;
        try
        {
            session.Run(() => Application.Current.Dispatcher!.BeginInvoke(() =>
            {
                dialog.ShowDialog(owner);
                returned = true;
            }));
            // The nested loop keeps RunJobs on the UI thread until the dialog closes, so the close comes from elsewhere.
            var closer = Task.Run(() =>
            {
                Thread.Sleep(50);
                session.Run(dialog.Close);
            });

            session.RunJobs();
            closer.GetAwaiter().GetResult();
            // The close ran inside the nested loop; the loop unwinds and ShowDialog returns before the next job.
            session.RunJobs();

            Assert.IsTrue(returned, "ShowDialog did not return after the dialog closed");
        }
        finally
        {
            HeadlessTestHost.Close(owner);
        }
    }

    [TestMethod]
    public void Start_RejectsASecondSession()
    {
        _ = HeadlessTestHost.Shared;

        Assert.ThrowsExactly<InvalidOperationException>(() => HeadlessSession.Start());
    }

    [TestMethod]
    public void Window_ReportsTheSessionDpi()
    {
        HeadlessTestHost.WithSession(new HeadlessOptions { Dpi = 144 }, session =>
        {
            var window = session.Run(() =>
            {
                var shown = new Window { WindowSize = WindowSize.Resizable(200, 100), Content = new Border() };
                shown.Show();
                return shown;
            });

            Assert.AreEqual(144u, session.Run(() => window.Dpi));
            Assert.AreEqual(new Size(200, 100), session.Run(() => window.ClientSize));
        });
    }
}
