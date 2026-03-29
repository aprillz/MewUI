using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

internal class NativeCustomWindowSample : NativeCustomWindow
{
    private static PathGeometry _lightIcon = PathGeometry.Parse(
        @"M8.462,15.537C7.487,14.563,7,13.383,7,12c0-1.383,0.487-2.563,1.462-3.538S10.617,7,12,7
            c1.383,0,2.563,0.487,3.537,1.462C16.513,9.438,17,10.617,17,12c0,1.383-0.487,2.563-1.463,3.537C14.563,16.513,13.383,17,12,17
            C10.617,17,9.438,16.513,8.462,15.537z M5,13H1v-2h4V13z M23,13h-4v-2h4V13z M11,5V1h2v4H11z M11,23v-4h2v4H11z M6.4,7.75
            L3.875,5.325L5.3,3.85l2.4,2.5L6.4,7.75z M18.7,20.15l-2.425-2.525L17.6,16.25l2.525,2.425L18.7,20.15z M16.25,6.4l2.425-2.525
            L20.15,5.3l-2.5,2.4L16.25,6.4z M3.85,18.7l2.525-2.425L7.75,17.6l-2.425,2.525L3.85,18.7z");

    private static PathGeometry _darkIcon = PathGeometry.Parse(
        @"M12.058,19.904c-2.222,0-4.111-0.777-5.667-2.334c-1.556-1.555-2.333-3.444-2.333-5.667
	        c0-2.025,0.66-3.782,1.981-5.27C7.359,5.147,8.994,4.269,10.942,4c0.054,0,0.106,0.002,0.159,0.006
	        c0.052,0.004,0.103,0.009,0.153,0.017c-0.337,0.471-0.604,0.994-0.801,1.57s-0.295,1.18-0.295,1.811
	        c0,1.778,0.622,3.289,1.867,4.533c1.244,1.245,2.755,1.867,4.533,1.867c0.635,0,1.239-0.099,1.813-0.296
	        c0.574-0.195,1.09-0.463,1.549-0.801c0.007,0.051,0.013,0.102,0.017,0.154c0.004,0.051,0.006,0.104,0.006,0.158
	        c-0.257,1.949-1.128,3.583-2.615,4.904C15.84,19.244,14.084,19.904,12.058,19.904z M12.058,18.904c1.467,0,2.784-0.404,3.95-1.213
	        s2.017-1.863,2.55-3.162c-0.333,0.083-0.667,0.149-1,0.199c-0.333,0.051-0.667,0.075-1,0.075c-2.05,0-3.796-0.721-5.237-2.163
	        C9.878,11.2,9.158,9.454,9.158,7.404c0-0.333,0.025-0.667,0.075-1c0.05-0.333,0.117-0.667,0.2-1c-1.3,0.533-2.354,1.383-3.163,2.55
	        c-0.808,1.167-1.212,2.483-1.212,3.95c0,1.934,0.684,3.583,2.05,4.95C8.475,18.221,10.125,18.904,12.058,18.904z");

    private ObservableValue<string> _stateText = new();
    private ObservableValue<string> _capText = new();

    public NativeCustomWindowSample()
    {
        this.OnBuild(OnBuild)
            .Resizable(600, 400, minWidth: 400, minHeight: 250)
            .OnActivated(UpdateStateLabel)
            .OnDeactivated(UpdateStateLabel)
            .OnWindowStateChanged(_ => UpdateStateLabel())
            .OnSizeChanged(_ => UpdateStateLabel())
            .StartCenterOwner();
    }

    private void UpdateStateLabel() =>
        _stateText.Value = $"WindowState: {WindowState} | IsActive: {IsActive} | Size: {ClientSize.Width:0}x{ClientSize.Height:0}";

    private void OnBuild(NativeCustomWindowSample window)
    {
        // Title bar left: MenuBar
        TitleBarLeft.Add(
            GalleryView.CreateMenu(_ => { })
                .Apply(x => x.DrawBottomSeparator = false)
                .Background(Color.Transparent));

        // Title bar right: Theme toggle
        var themeIcon = new PathShape()
            .Center()
            .Size(12)
            .Stretch(Stretch.Uniform)
            .WithTheme((t, s) => s.Data(t.IsDark ? _lightIcon : _darkIcon).Fill(t.Palette.WindowText));

        var themeBtn = new Button()
            .Content(themeIcon)
            .CornerRadius(0)
            .StyleName("chrome")
            .MinWidth(36)
            .MinHeight(32);

        themeBtn.OnClick(() =>
        {
            var isDark = Application.Current.Theme.IsDark;
            Application.Current.SetTheme(isDark ? ThemeVariant.Light : ThemeVariant.Dark);
        });

        TitleBarRight.Add(themeBtn);

        window
            .Title("Native Chrome Demo")
            .OnBuild(x => x
                .Content(new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .Text("Native chrome: DWM frame (Win11) / fullSizeContentView (macOS).\nRounded corners and shadow preserved by the OS.")
                            .TextWrapping(TextWrapping.Wrap),

                        new Border()
                            .Padding(8)
                            .CornerRadius(4)
                            .Child(new StackPanel()
                                .Vertical()
                                .Spacing(6)
                                .Children(
                                    new TextBlock().Text("Window Properties").Bold(),
                                    BoolCheckBox(this, "CanMinimize", Window.CanMinimizeProperty),
                                    BoolCheckBox(this, "CanMaximize", Window.CanMaximizeProperty),
                                    BoolCheckBox(this, "CanClose", Window.CanCloseProperty),
                                    BoolCheckBox(this, "Topmost", Window.TopmostProperty),
                                    BoolCheckBox(this, "ShowInTaskbar", Window.ShowInTaskbarProperty),
                                    new StackPanel()
                                        .Horizontal()
                                        .Spacing(6)
                                        .Children(
                                            new Button().Content("Minimize")
                                                .OnClick(() => Minimize())
                                                .OnCanClick(() => WindowState == WindowState.Normal || WindowState == WindowState.Maximized),
                                            new Button().Content("Maximize")
                                                .OnClick(() => Maximize())
                                                .OnCanClick(() => WindowState == WindowState.Normal),
                                            new Button().Content("Restore")
                                                .OnClick(() => Restore())
                                                .OnCanClick(() => WindowState != WindowState.Normal)
                                        ),
                                    new TextBlock().BindText(_stateText),
                                    new TextBlock().BindText(_capText)
                                )),

                        new TextBox(),

                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new Button().Content("OK"),
                                new Button().Content("Close").OnClick(() => Close())
                            )
                    )
                )
            ).OnLoaded(() => _capText.Value = $"ChromeCapabilities: {window.ChromeCapabilities}");
    }

    private static CheckBox BoolCheckBox(Window target, string label, MewProperty<bool> property)
    {
        bool initial = property == Window.CanMinimizeProperty ? target.CanMinimize
            : property == Window.CanMaximizeProperty ? target.CanMaximize
            : property == Window.CanCloseProperty ? target.CanClose
            : property == Window.TopmostProperty ? target.Topmost
            : property == Window.ShowInTaskbarProperty ? target.ShowInTaskbar
            : false;

        return new CheckBox()
            .Left()
            .IsChecked(initial)
            .Content(label)
            .OnCheckedChanged(v =>
            {
                bool val = v == true;
                if (property == Window.CanMinimizeProperty) target.CanMinimize = val;
                else if (property == Window.CanMaximizeProperty) target.CanMaximize = val;
                else if (property == Window.CanCloseProperty) target.CanClose = val;
                else if (property == Window.TopmostProperty) target.Topmost = val;
                else if (property == Window.ShowInTaskbarProperty) target.ShowInTaskbar = val;
            });
    }
}
