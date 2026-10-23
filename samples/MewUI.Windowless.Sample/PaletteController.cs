using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Windowless.Sample;

internal sealed class PaletteController(Action exit, IconSource icon, ObservableValue<bool> hideOnClose)
{
    private Window? _palette;
    private bool _paletteVisible;
    private bool _exiting;

    public void Toggle()
    {
        if (_palette is { } openPalette && _paletteVisible)
        {
            if (hideOnClose.Value)
            {
                Hide(openPalette);
            }
            else
            {
                _palette = null;
                _paletteVisible = false;
                openPalette.Close();
                Console.Error.WriteLine($"[windowless] palette closed; user windows={Application.Current.AllWindows.Count}");
            }
            return;
        }

        if (_palette != null)
        {
            _palette.Show();
            _paletteVisible = true;
            Console.Error.WriteLine($"[windowless] palette shown again; user windows={Application.Current.AllWindows.Count}");
            return;
        }

        var palette = new Window()
            .Title("MewUI Windowless Palette")
            .Icon(icon)
            .Resizable(420, 220)
            .Content(
                new StackPanel()
                    .Padding(20)
                    .Spacing(12)
                    .Children(
                        new TextBlock().Text("The application started without a main window."),
                        new TextBlock().Text("Press Ctrl+Alt+Space again to close and reopen this palette."),
                        new TextBlock().Text("The tray or menu bar icon toggles it too, and its menu exits."),
                        new CheckBox()
                            .Content("Hide instead of close")
                            .BindIsChecked(hideOnClose),
                        new Button().Content("Exit application").OnClick(exit)));

        palette.Closing += args =>
        {
            if (hideOnClose.Value && !_exiting)
            {
                args.Cancel = true;
                Hide(palette);
            }
        };
        palette.Closed += () =>
        {
            if (ReferenceEquals(_palette, palette))
            {
                _palette = null;
                _paletteVisible = false;
            }
        };
        _palette = palette;
        _paletteVisible = true;
        palette.Show();
        Console.Error.WriteLine($"[windowless] palette shown; user windows={Application.Current.AllWindows.Count}");
    }

    /// <summary>Whether the palette is open on screen.</summary>
    public bool IsVisible => _paletteVisible;

    public void PrepareToExit() => _exiting = true;

    private void Hide(Window palette)
    {
        // The global hotkey is raised while Space is still physically down. Drop the focused
        // checkbox before hiding so the trailing Space key-up cannot toggle it after a later Show.
        palette.FocusManager.ClearFocus();
        palette.Hide();
        _paletteVisible = false;
        Console.Error.WriteLine($"[windowless] palette hidden; user windows={Application.Current.AllWindows.Count}");
    }
}
