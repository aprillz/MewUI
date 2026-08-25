using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Commands;

[TestClass]
[DoNotParallelize]
public sealed class SegmentButtonCommandTests
{
    [TestMethod]
    public void ButtonGroupSegment_ExecutesCommandAndTracksCanExecute()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        bool canExecute = false;
        int executions = 0;
        var command = new Command("alignment.left", "Left");
        SegmentButton segment = null!;
        var group = new ButtonGroup()
            .Items(new[] { command }, static item => item.Text ?? item.Id)
            .PrepareContainer<Command>((button, item, _) =>
            {
                segment = button;
                button.Command(item);
            });
        var window = HeadlessWindow.Create();
        window.Commands.Register(command, () => executions++, () => canExecute);
        window.Content = group;
        window.PerformLayout();
        window.FocusManager.SetFocus(segment);

        Assert.IsFalse(segment.IsEffectivelyEnabled);
        window.SendKeyPress(Key.Space);
        Assert.AreEqual(0, executions);

        canExecute = true;
        window.EvaluateCommandStates();
        Assert.IsTrue(segment.IsEffectivelyEnabled);

        window.FocusManager.SetFocus(segment);
        window.SendKeyPress(Key.Space);
        Assert.AreEqual(1, executions);
    }

    [TestMethod]
    public void SegmentButton_BindCommandTracksSource()
    {
        var first = new Command("first");
        var second = new Command("second");
        var source = new ObservableValue<Command?>(first);
        var segment = new SegmentButton().BindCommand(source);

        source.Value = second;

        Assert.AreSame(second, segment.Command);
    }
}
