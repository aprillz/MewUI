using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.X11;

namespace MewUI.Test.Input;

/// <summary>
/// The X11 host hands each event to the window it concerns; the answer to a selection conversion concerns the window
/// that asked for it, which is how a drop's data reaches the window it was dropped on.
/// </summary>
[TestClass]
public sealed class X11EventWindowTests
{
    private const int SELECTION_NOTIFY = 31;

    [TestMethod]
    public void ASelectionAnswerGoesToTheWindowThatAsked()
    {
        var ev = new XEvent { type = SELECTION_NOTIFY };
        ev.xselection.type = SELECTION_NOTIFY;
        ev.xselection.requestor = 0x1234;

        Assert.AreEqual((nint)0x1234, X11PlatformHost.GetEventWindow(ev), "the selection answer was not routed to the window that asked for it");
    }
}
