using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// The accent split button colours the chrome its two faces sit on. The faces are separate buttons, so
/// what matters is that they take the accent face look rather than hovering back to the ordinary button
/// face over an accent background.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AccentSplitButtonStyleTests
{
    private Window _window = null!;
    private SplitButton _button = null!;

    private Palette Palette => _button.ThemeInternal.Palette;

    private Button PrimaryFace => Faces()[0];

    private Button DropDownFace => Faces()[1];

    [TestInitialize]
    public void SetUp()
    {
        _button = new SplitButton
        {
            StyleName = BuiltInStyles.AccentSplitButton,
            Content = new TextBlock { Text = "Save" },
            DropDownMenu = new Menu().Item(new Command("test.accentSplit", "Item")),
            Width = 200,
            Height = 32,
        };

        _window = HeadlessWindow.Create();

        // A named style resolves from the nearest sheet up the tree, and a headless test has no
        // Application to hold the built-in one.
        var sheet = new StyleSheet();
        BuiltInStyles.Register(sheet);
        _window.StyleSheet = sheet;

        _window.Content = _button;
        _window.PerformLayout();
        _window.ForceStyleSnap();
    }

    [TestCleanup]
    public void TearDown() => _window.Close();

    private List<Button> Faces()
    {
        var faces = VisualTree.FindAll(_button, e => e is Button and not SplitButton).Cast<Button>().ToList();
        Assert.HasCount(2, faces, "the split button did not materialize both faces");
        return faces;
    }

    /// <summary>Hovers the given face and runs the fill transition out, which is what settles its colour.</summary>
    private void Hover(Button face)
    {
        _window.SetIsActive(true);
        _window.SendMouseMove(face.CenterOf());
        _window.UpdateVisualStates();

        long start = Stopwatch.GetTimestamp();
        long frame = Stopwatch.Frequency / 60;
        for (int step = 0; step <= 30; step++)
        {
            AnimationManager.Instance.UpdateAt(start + (frame * step));
        }
    }

    [TestMethod]
    public void TheChromeCarriesTheAccent()
    {
        Assert.AreEqual(Palette.Accent, _button.Background, "the split button is not accent-coloured");
        Assert.AreEqual(Palette.AccentText, _button.Foreground, "the text is not the accent's own");
        Assert.AreEqual(0, _button.BorderThickness, "the accent split button still draws a border");

        // Both faces rest transparent, so the chrome is what shows through.
        Assert.AreEqual(0, PrimaryFace.Background.A, "the primary face is painted at rest");
        Assert.AreEqual(0, DropDownFace.Background.A, "the drop-down face is painted at rest");
    }

    [TestMethod]
    public void OnlyTheHoveredHalfLightsUp_AndItStaysInTheAccent()
    {
        var primary = PrimaryFace;
        Hover(primary);

        Assert.AreEqual(
            Palette.Accent.Lerp(Palette.WindowBackground, 0.15),
            primary.Background,
            "the hovered face left the accent");
        Assert.AreNotEqual(
            Palette.ButtonHoverBackground,
            primary.Background,
            "the hovered face fell back to the ordinary button face");
        Assert.AreEqual(0, DropDownFace.Background.A, "hovering one half painted the other");
    }
}
