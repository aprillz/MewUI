using Aprillz.MewUI;
using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>The window the automation provider tests read and operate through each platform's accessibility client.</summary>
internal sealed class AutomationForm
{
    public int Saves;

    public AutomationForm(string title)
    {
        Save = new Button().Content("Save");
        Save.Click += () => Saves++;
        NameBox = new TextBox { Width = 160 };
        AutomationProperties.SetAutomationId(NameBox, "name");
        Agree = new CheckBox().Content("Agree");
        Colors = new ListBox { Height = 90, Width = 160 }.Items("Red", "Green", "Blue");
        Colors.SelectedIndex = 0;
        SizeBox = new ComboBox { Width = 160 }.Items("One", "Two");
        AutomationProperties.SetName(SizeBox, "Size");
        Volume = new Slider { Minimum = 0, Maximum = 10, Width = 160 };
        AutomationProperties.SetName(Volume, "Volume");
        Window = new Window
        {
            Title = title,
            StartupLocation = WindowStartupLocation.Manual,
            WindowSize = WindowSize.Fixed(320, 420),
            Content = new StackPanel { Margin = new Thickness(12) }.Children(Save, NameBox, Agree, Colors, SizeBox, Volume),
        };
    }

    public Window Window { get; }

    public Button Save { get; }

    public TextBox NameBox { get; }

    public CheckBox Agree { get; }

    public ListBox Colors { get; }

    public ComboBox SizeBox { get; }

    public Slider Volume { get; }
}
