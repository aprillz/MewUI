using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

/// <summary>
/// A custom chrome window built on a transparent Window with Border-based rendering.
/// Demonstrates DragMove, IsActive/WindowState binding, CanMinimize/CanMaximize, and themed chrome.
/// </summary>
/// <remarks>
/// Provides rounded borders on Windows 10 and earlier where the OS does not support rounded corners natively.
/// However, this approach uses AllowsTransparency with per-frame alpha compositing, which has higher
/// CPU/GPU overhead on Win32. Prefer <see cref="NativeCustomWindow"/> for better performance on Windows 11+
/// and macOS where the OS provides native frame support (rounded corners, shadow, DWM border color).
/// </remarks>
public class CustomWindow : Window
{
    private const double TitleBarHeight = 28;
    private const double ButtonWidth = 32;
    private const double ChromeButtonSize = 4;
    private const double ChromeCornerRadius = 8;
    private const double ShadowExtent = 12;
    private const double ShadowOffset = 2; 
    private readonly ShadowDecorator _shadow;
    private readonly Border _contentArea;
    private readonly Border _chromeBorder;
    private readonly Border _titleBar;
    private readonly TextBlock _titleText;
    private readonly StackPanel _controlButtons;
    private readonly StackPanel _leftArea;
    private readonly StackPanel _rightArea;
    private readonly Button _minimizeBtn;
    private readonly Button _maximizeBtn;

    private static readonly Style ChromeButtonStyle = new(typeof(Button))
    {
        Transitions = [Transition.Create(Control.BackgroundProperty)],
        Setters =
        [
            Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace.WithAlpha(0)),
            Setter.Create(Control.BorderThicknessProperty, 0.0),
            Setter.Create(Control.CornerRadiusProperty, 0.0),
            Setter.Create(Control.PaddingProperty, new Thickness(0)),
        ],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Hot,
                Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace)],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Pressed,
                Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonPressedBackground)],
            },
        ],
    };

    private static readonly Style CloseButtonStyle = new(typeof(Button))
    {
        Transitions = [Transition.Create(Control.BackgroundProperty)],
        Setters =
        [
            Setter.Create(Control.BackgroundProperty, Color.FromRgb(232, 17, 35).WithAlpha(0)),
            Setter.Create(Control.BorderThicknessProperty, 0.0),
            Setter.Create(Control.CornerRadiusProperty, 0.0),
            Setter.Create(Control.PaddingProperty, new Thickness(0)),
        ],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Hot,
                Setters = [
                    Setter.Create(Control.BackgroundProperty, Color.FromRgb(232, 17, 35)),
                    Setter.Create(Control.ForegroundProperty, Color.White),
                ],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Pressed,
                Setters = [
                    Setter.Create(Control.BackgroundProperty, Color.FromRgb(200, 12, 28)),
                    Setter.Create(Control.ForegroundProperty, Color.White),
                ],
            },
        ],
    };

    public CustomWindow()
    {
        AllowsTransparency = true;
        base.Background = Color.Transparent;
        base.Padding = new Thickness(0);

        StyleSheet = new StyleSheet();
        StyleSheet.Define("chrome", ChromeButtonStyle);
        StyleSheet.Define("close", CloseButtonStyle);

        // Title text
        var titleText = new TextBlock
        {
            IsHitTestVisible = false,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Margin = new Thickness(8, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleText.SetBinding(TextBlock.TextProperty, this, TitleProperty);
        _titleText = titleText;

        // Chrome buttons
        _minimizeBtn = CreateChromeButton(GlyphKind.WindowMinimize);
        _minimizeBtn.Click += () => Minimize();
        _minimizeBtn.SetBinding(UIElement.IsVisibleProperty, this, CanMinimizeProperty);

        var maxGlyph = new GlyphElement().Kind(GlyphKind.WindowMaximize).GlyphSize(ChromeButtonSize);
        _maximizeBtn = CreateChromeButton(maxGlyph);
        _maximizeBtn.Click += () =>
        {
            if (WindowState == WindowState.Maximized)
                Restore();
            else
                Maximize();
        };
        _maximizeBtn.SetBinding(UIElement.IsVisibleProperty, this, CanMaximizeProperty);

        var closeBtn = CreateChromeButton(GlyphKind.Cross, isClose: true);
        closeBtn.Click += () => Close();
        closeBtn.SetBinding(UIElement.IsVisibleProperty, this, CanCloseProperty);

        _controlButtons = new StackPanel { Orientation = Orientation.Horizontal };
        _controlButtons.Add(_minimizeBtn);
        _controlButtons.Add(_maximizeBtn);
        _controlButtons.Add(closeBtn);

        // Title bar areas
        _leftArea = new StackPanel { Orientation = Orientation.Horizontal };
        _rightArea = new StackPanel { Orientation = Orientation.Horizontal };

        // Title bar
        _titleBar = new Border
        {
            Padding = new Thickness(0),
            MinHeight = TitleBarHeight,
        };
        _titleBar.Child = new DockPanel().Children(
            new Border().DockRight().Child(_controlButtons),
            new Border().DockRight().Child(_rightArea),
            new Border().DockLeft().Child(_leftArea),
            titleText
        );

        // DragMove on title bar
        _titleBar.MouseDown += e =>
        {
            if (e.Button == MouseButton.Left)
            {
                DragMove();
                e.Handled = true;
            }
        };
        _titleBar.SetBinding(BackgroundProperty, this, BackgroundProperty);

        // Title bar: double-click to maximize/restore
        _titleBar.MouseDoubleClick += e =>
        {
            if (e.Button == MouseButton.Left && CanMaximize)
            {
                if (e.GetPosition(this) is Point p && (_leftArea.Bounds.Contains(p) || _rightArea.Bounds.Contains(p)))
                {
                    e.Handled = true;
                    return;
                }

                if (WindowState == WindowState.Maximized)
                {
                    Restore();
                }
                else
                {
                    Maximize();
                }
                e.Handled = true;
            }
        };

        // Content area
        _contentArea = new Border { Padding = new Thickness(16) };

        _chromeBorder = new Border
        {
            CornerRadius = ChromeCornerRadius,
            BorderThickness = 1,
            ClipToBounds = true,
            Child = new DockPanel().Children(
                _titleBar.DockTop(),
                _contentArea
			)
        };
        _chromeBorder.WithTheme((t, b) => b.Background = t.Palette.WindowBackground);
        _chromeBorder.SetBinding(Border.BorderBrushProperty, this, BorderBrushProperty);

        // WindowState -> glyph + chrome update
        WindowStateChanged += _ =>
        {
            OnWindowStateVisualUpdate();
            UpdateChromeButtonVisibility();
        };

        _shadow = new ShadowDecorator
        {
            BlurRadius = ShadowExtent,
            OffsetY = ShadowOffset,
            CornerRadius = ChromeCornerRadius,
            Child = _chromeBorder,
        };
        _shadow.WithTheme((t, s) =>
            s.ShadowColor = Color.FromArgb((byte)(t.IsDark ? 100 : 48), 0, 0, 0));

        base.Content = _shadow;

        // React to IsActive, WindowState, and Theme changes
        Activated += UpdateChromeAppearance;
        Deactivated += UpdateChromeAppearance;
        this.WithTheme((_, _) => UpdateChromeAppearance());

    }

    /// <summary>Left area of the title bar (e.g. MenuBar).</summary>
    public StackPanel TitleBarLeft => _leftArea;

    /// <summary>Right area of the title bar (e.g. theme toggle, search).</summary>
    public StackPanel TitleBarRight => _rightArea;

    public new UIElement? Content
    {
        get => _contentArea.Child;
        set => _contentArea.Child = value;
    }

    public new Thickness Padding
    {
        get => _contentArea.Padding;
        set => _contentArea.Padding = value;
    }

    private void UpdateChromeAppearance()
    {
        var p = Theme.Palette;
        var accentBorder = IsActive ? p.Accent : p.ControlBorder;

        BorderBrush = accentBorder;

        _titleText.Foreground = IsActive ? p.WindowText : p.DisabledText;
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        UpdateChromeAppearance();
    }

    private void UpdateChromeButtonVisibility()
    {
        // CustomWindow is fully borderless and always draws its own title bar, so it must stay visible. The
        // ExtendClientArea check belongs to NativeCustomWindow (which hides the custom bar in favour of the
        // native title bar); using it here wrongly hid the title bar on platforms reporting
        // ChromeCapabilities.None (e.g. X11) the first time WindowState changed (maximize), and it never came
        // back on restore.
        _titleBar.IsVisible = true;
        _controlButtons.IsVisible = !HasNativeChromeButtons;
        _titleBar.Padding = NativeChromeButtonInset;
    }

    private void OnWindowStateVisualUpdate()
    {
        bool maximized = WindowState == WindowState.Maximized;
        if (_maximizeBtn.Content is GlyphElement glyph)
            glyph.Kind = maximized ? GlyphKind.WindowRestore : GlyphKind.WindowMaximize;

        _chromeBorder.CornerRadius = maximized ? 0 : ChromeCornerRadius;
        _chromeBorder.BorderThickness = maximized ? 0 : 1;
        _shadow.BlurRadius = maximized ? 0 : ShadowExtent;
        _shadow.OffsetY = maximized ? 0 : ShadowOffset;
        _shadow.CornerRadius = maximized ? 0 : ChromeCornerRadius;
    }

    private static Button CreateChromeButton(GlyphKind kind, bool isClose = false)
    {
        var glyph = new GlyphElement().Kind(kind).GlyphSize(ChromeButtonSize);
        return CreateChromeButton(glyph, isClose);
    }

    private static Button CreateChromeButton(Element content, bool isClose = false)
    {
        return new Button
        {
            Content = content,
            MinWidth = ButtonWidth,
            MinHeight = TitleBarHeight,
            StyleName = isClose ? "close" : "chrome",
        };
    }
}

public static class CustomWindowExtensions
{
    public static CustomWindow Content(this CustomWindow cw, UIElement? content)
    {
        cw.Content = content;
        return cw;
    }
}

