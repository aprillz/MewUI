using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using MewUI.Test.Controls;

namespace MewUI.Test.Platform;

/// <summary>
/// A native menu shows what the MewUI menu would: access keys, enabled state from the command's handler, check
/// state, submenus and single-gesture shortcuts, with stray separators dropped; a chosen entry runs on a later turn.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeMenuSnapshotTests
{
    [TestMethod]
    public void ReadsTextAccessKeysAndCheckState()
    {
        Run((app, turns) =>
        {
            var menu = new Menu()
                .Add(new MenuItem("_Open"))
                .Add(new MenuItem("Save __as & close"))
                .Add(new MenuItem("Wrap").IsCheckable().IsChecked());

            var entries = Snapshot(app).Read(menu);

            Assert.AreEqual(3, entries.Count);
            Assert.AreEqual("Open", entries[0].Text);
            Assert.AreEqual("&Open", entries[0].FormatText('&'));
            Assert.AreEqual("_Open", entries[0].FormatText('_'));
            Assert.AreEqual("Open", entries[0].FormatText('\0'));
            Assert.AreEqual("Save _as & close", entries[1].Text);
            Assert.AreEqual("Save _as && close", entries[1].FormatText('&'), "a literal marker is doubled");
            Assert.AreEqual("Save __as & close", entries[1].FormatText('_'));
            Assert.IsTrue(entries[2].IsCheckable);
            Assert.IsTrue(entries[2].IsChecked);
        });
    }

    [TestMethod]
    public void DropsLeadingTrailingAndRepeatedSeparators()
    {
        Run((app, turns) =>
        {
            var menu = new Menu()
                .Separator()
                .Add(new MenuItem("First"))
                .Separator()
                .Separator()
                .Add(new MenuItem("Second"))
                .Separator();

            var kinds = Snapshot(app).Read(menu).Select(static entry => entry.Kind).ToArray();

            CollectionAssert.AreEqual(
                new[] { NativeMenuEntryKind.Item, NativeMenuEntryKind.Separator, NativeMenuEntryKind.Item },
                kinds);
        });
    }

    [TestMethod]
    public void EnablesAnItem_OnlyWhenItsCommandCanRunAndItsItemAllows()
    {
        var handled = new Command("test.nativeMenu.handled");
        var unhandled = new Command("test.nativeMenu.unhandled");

        Run((app, turns) =>
        {
            app.Commands.Register(handled, () => { });
            var menu = new Menu()
                .Add(new MenuItem(handled))
                .Add(new MenuItem(unhandled))
                .Add(new MenuItem("Disabled").IsEnabled(false))
                .Add(new MenuItem("Refused") { CanClick = static () => false });

            var entries = Snapshot(app).Read(menu);

            Assert.IsTrue(entries[0].IsEnabled);
            Assert.IsFalse(entries[1].IsEnabled, "a command without a handler cannot be chosen");
            Assert.IsFalse(entries[2].IsEnabled);
            Assert.IsFalse(entries[3].IsEnabled);
        });
    }

    [TestMethod]
    public void ReadsSubmenus()
    {
        Run((app, turns) =>
        {
            var menu = new Menu()
                .Add(new MenuItem("More").SubMenu(new Menu().Add(new MenuItem("Inner"))));

            var entries = Snapshot(app).Read(menu);

            Assert.AreEqual(NativeMenuEntryKind.SubMenu, entries[0].Kind);
            Assert.AreEqual(1, entries[0].Children.Count);
            Assert.AreEqual("Inner", entries[0].Children[0].Text);
        });
    }

    [TestMethod]
    public void ShowsTheApplicationGesture_ButNotALongerSequence()
    {
        var single = new Command("test.nativeMenu.single");
        var chord = new Command("test.nativeMenu.chord");

        Run((app, turns) =>
        {
            app.Commands.Register(single, () => { });
            app.Commands.Register(chord, () => { });
            app.InputMap.Map(single, new KeyGesture(Key.O, ModifierKeys.Control | ModifierKeys.Shift));
            app.InputMap.Map(chord, KeySequence.Parse("Ctrl+K, Ctrl+P"));

            var entries = Snapshot(app).Read(new Menu().Add(new MenuItem(single)).Add(new MenuItem(chord)));

            Assert.AreEqual(new KeyGesture(Key.O, ModifierKeys.Control | ModifierKeys.Shift), entries[0].Gesture);
            Assert.IsNull(entries[1].Gesture, "a sequence of strokes is not a gesture a native item can show");
        });
    }

    [TestMethod]
    public void RunsTheChosenEntry_OnALaterTurn()
    {
        var command = new Command("test.nativeMenu.invoke");
        object? received = null;

        Run((app, turns) =>
        {
            app.Commands.Register<string>(command, value => received = value);
            var entries = Snapshot(app).Read(new Menu().Add(new MenuItem("Run", command, "item data")));

            Snapshot(app).Invoke(entries[0]);
            Assert.IsNull(received, "the command ran inside the native menu callback");

            turns.RunAll();
            Assert.AreEqual("item data", received);
        });
    }

    [TestMethod]
    public void DrawsIcons_AndTellsAGlyphFromAColoredIcon()
    {
        Run((app, turns) =>
        {
            var glyph = new IconTemplate(static _ => new Border { Background = Color.Black });
            var colored = new IconTemplate(static _ => new Border { Background = Color.FromRgb(200, 30, 30) });
            var menu = new Menu()
                .Add(new MenuItem("Glyph").Icon(glyph))
                .Add(new MenuItem("Colored").Icon(colored))
                .Add(new MenuItem("Checkable").Icon(glyph).IsCheckable());

            var entries = new NativeMenuSnapshot(app.CommandRouter, CommandTarget.From(app.Commands), null, 16, 2).Read(menu);

            Assert.IsNotNull(entries[0].Icon);
            Assert.AreEqual(32, entries[0].Icon!.PixelSize, "the icon is drawn at the screen's scale");
            Assert.IsTrue(entries[0].Icon!.IsGlyph);
            Assert.IsNotNull(entries[1].Icon);
            Assert.IsFalse(entries[1].Icon!.IsGlyph);
            Assert.IsNull(entries[2].Icon, "a checkable item keeps the icon column for its check");
        });
    }

    private static NativeMenuSnapshot Snapshot(Application app)
        => new(app.CommandRouter, CommandTarget.From(app.Commands), gestureWindow: null, iconSizeDip: 16, iconScale: 1);

    private static void Run(Action<Application, TrayIconTests.QueuedDispatcher> body)
        => new TrayIconTests.TrayPlatformHost((app, turns, _) => body(app, turns)).Run();
}
