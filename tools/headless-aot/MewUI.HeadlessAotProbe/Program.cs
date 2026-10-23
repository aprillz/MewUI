using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;

// Gates 1-5 of the headless host plan, run as a NativeAOT console so the package proves it needs no
// reflection: button click, text input, Tab focus, virtual time and the in-surface drop-down.
#if MEWUI_DIRECT2D
Direct2DBackend.Register();
#elif MEWUI_WINDOWS
GdiBackend.Register();
#elif MEWUI_LINUX
MewVGX11Backend.Register();
#elif MEWUI_MACOS
MewVGMacOSBackend.Register();
#else
#error The probe needs a RuntimeIdentifier to choose a graphics backend.
#endif

int failures = 0;
using (var session = HeadlessSession.Start(new HeadlessOptions()))
{
    Check("gate 1 button click", () =>
    {
        var button = new Button().Content("OK");
        int clicks = 0;
        button.Click += () => clicks++;
        var window = Show(session, button);
        window.SendClick(button.CenterInWindow());
        session.Run(window.Close);
        return clicks == 1 ? null : $"clicks={clicks}";
    });

    Check("gate 2 text input", () =>
    {
        var source = new ObservableValue<string>(string.Empty);
        var textBox = new TextBox().BindText(source);
        var window = Show(session, textBox);
        session.Run(() => textBox.Focus());
        window.SendTextInput("Hello");
        session.Run(window.Close);
        return source.Value == "Hello" ? null : $"value={source.Value}";
    });

    Check("gate 3 tab focus", () =>
    {
        var first = new TextBox();
        var second = new TextBox();
        var window = Show(session, new StackPanel().Children(first, second));
        session.Run(() => first.Focus());
        window.SendKeyPress(Key.Tab);
        bool moved = session.Run(() => ReferenceEquals(window.FocusManager.FocusedElement, second));
        session.Run(window.Close);
        return moved ? null : "focus did not move to the second text box";
    });

    Check("gate 4 virtual time", () =>
    {
        int ticks = 0;
        var timer = session.Run(() =>
        {
            var created = new DispatcherTimer(TimeSpan.FromMilliseconds(300));
            created.Tick += () => ticks++;
            created.Start();
            return created;
        });
        session.AdvanceTime(TimeSpan.FromMilliseconds(299));
        int early = ticks;
        session.AdvanceTime(TimeSpan.FromMilliseconds(1));
        int late = ticks;
        session.Run(timer.Stop);
        return early == 0 && late == 1 ? null : $"early={early} late={late}";
    });

    Check("gate 5 popup", () =>
    {
        var combo = new ComboBox().Items("User", "Admin", "Guest");
        combo.HorizontalAlignment = HorizontalAlignment.Left;
        combo.VerticalAlignment = VerticalAlignment.Top;
        combo.Width = 160;
        var window = Show(session, combo);
        window.SendClick(combo.CenterInWindow());
        bool opened = session.Run(() => combo.IsDropDownOpen);
        if (!opened)
        {
            return "the click did not open the drop-down";
        }

        var row = session.Run(() => FindDropDownRow(window, combo, 1));
        if (row == null)
        {
            return "no drop-down row with index 1 under the combo box";
        }

        window.SendClick(row.CenterInWindow());
        int selected = session.Run(() => combo.SelectedIndex);
        bool closed = session.Run(() => !combo.IsDropDownOpen);
        session.Run(window.Close);
        return selected == 1 && closed ? null : $"selected={selected} closed={closed}";
    });
}

Console.WriteLine(failures == 0 ? "headless AOT probe: all gates passed" : $"headless AOT probe: {failures} gate(s) failed");
return failures == 0 ? 0 : 1;

void Check(string name, Func<string?> gate)
{
    string? failure;
    try
    {
        failure = gate();
    }
    catch (Exception ex)
    {
        failure = ex.ToString();
    }

    if (failure == null)
    {
        Console.WriteLine($"PASS {name}");
    }
    else
    {
        failures++;
        Console.WriteLine($"FAIL {name}: {failure}");
    }
}

static Window Show(HeadlessSession session, Element content)
    => session.Run(() =>
    {
        var window = new Window
        {
            WindowSize = WindowSize.Resizable(400, 300),
            Content = content,
        };
        window.Show();
        return window;
    });

static ItemContainer? FindDropDownRow(Window window, ComboBox combo, int index)
{
    var bounds = combo.Bounds;
    double x = bounds.X + (bounds.Width / 2);
    for (double y = bounds.Bottom + 1; y < window.ClientSize.Height; y += 2)
    {
        for (Element? current = window.HitTest(new Point(x, y)); current != null; current = current.Parent)
        {
            if (current is ItemContainer container && container.Index == index)
            {
                return container;
            }
        }
    }

    return null;
}
