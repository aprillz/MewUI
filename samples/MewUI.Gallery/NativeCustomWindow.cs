using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

/// <summary>
/// A custom chrome window using native frame extension (DWM on Win11, fullSizeContentView on macOS).
/// Rounded corners, shadow, and resize are handled by the OS.
/// </summary>
public class NativeCustomWindow : Window
{
    private const double DefaultTitleBarHeight = 28;
    private const double ButtonWidth = 46;
    private const double ChromeButtonSize = 4;

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

    private readonly Border _contentArea;
    private readonly Border _chromeBorder;
    private readonly AlphaTextPanel _titleBar;
    private readonly TextBlock _titleText;
    private readonly StackPanel _controlButtons;
    private readonly StackPanel _leftArea;
    private readonly StackPanel _rightArea;
    private readonly Button _minimizeBtn;
    private readonly Button _maximizeBtn;

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);

        if (ChromeCapabilities.HasFlag(WindowChromeCapabilities.NativeBorderColor)
            && property.Name == nameof(BorderBrush))
        {
            SetWindowBorderColor(BorderBrush);
        }
    }

    public NativeCustomWindow()
    {
        ExtendClientAreaTitleBarHeight = DefaultTitleBarHeight;
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

        // Hide custom chrome buttons on platforms with native ones (e.g. macOS traffic lights).
        Activated += UpdateChromeButtonVisibility;

        // Title bar areas
        _leftArea = new StackPanel { Orientation = Orientation.Horizontal };
        _rightArea = new StackPanel { Orientation = Orientation.Horizontal };

        // Title bar — uses AlphaTextPanel to enable alpha-correct text rendering
        // in the DWM-extended title bar region (GDI backend).
        var titleBarContent = new DockPanel().Children(
            new Border().DockRight().Child(_controlButtons),
            new Border().DockRight().Child(_rightArea),
            new Border().DockLeft().Child(_leftArea),
            titleText
        );
        _titleBar = new AlphaTextPanel
        {
            MinHeight = DefaultTitleBarHeight,
            Content = titleBarContent
        };
        _titleBar.SetBinding(BackgroundProperty, this, BackgroundProperty);

        // Title bar: double-click to maximize/restore
        _titleBar.MouseDoubleClick += e =>
        {
            if (e.Button == MouseButton.Left && CanMaximize)
            {
                if (WindowState == WindowState.Maximized) Restore();
                else Maximize();
                e.Handled = true;
            }
        };

        // Content area
        _contentArea = new Border { Padding = new Thickness(16) };

        _chromeBorder = new Border
        {
            BorderThickness = 0,
            Child = new DockPanel().Children(
                _titleBar.DockTop(),
                _contentArea
            ),
        };
        _chromeBorder.SetBinding(Border.BorderBrushProperty, this, BorderBrushProperty);
        base.Content = _chromeBorder;

        // WindowState -> glyph + chrome update
        ClientSizeChanged += _ =>
        {
            OnWindowStateVisualUpdate();
            UpdateChromeButtonVisibility();
        };

        // Active state -> border color
        Activated += UpdateChromeAppearance;
        Deactivated += UpdateChromeAppearance;
        Loaded += OnLoaded;
        this.WithTheme((_, _) => UpdateChromeAppearance());
    }

    private void OnLoaded()
    {
        if (BorderBrush.A > 0 && !ChromeCapabilities.HasFlag(WindowChromeCapabilities.NativeBorderColor)
                               && !ChromeCapabilities.HasFlag(WindowChromeCapabilities.NativeWindowBorder))
        {
            _chromeBorder.BorderThickness = 1;
        }
    }



    /// <summary>Left area of the title bar (e.g. MenuBar).</summary>
    public StackPanel TitleBarLeft => _leftArea;

    /// <summary>Right area of the title bar (e.g. theme toggle).</summary>
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

    private void UpdateChromeButtonVisibility()
    {
        bool hasExtend = ChromeCapabilities.HasFlag(WindowChromeCapabilities.ExtendClientArea);
        _titleBar.IsVisible = hasExtend;
        _controlButtons.IsVisible = !HasNativeChromeButtons;
        _titleBar.Padding = NativeChromeButtonInset;
    }

    private void OnWindowStateVisualUpdate()
    {
        bool maximized = WindowState == WindowState.Maximized;
        if (_maximizeBtn.Content is GlyphElement glyph)
            glyph.Kind = maximized ? GlyphKind.WindowRestore : GlyphKind.WindowMaximize;
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
            MinHeight = DefaultTitleBarHeight,
            StyleName = isClose ? "close" : "chrome",
        };
    }

    /// <summary>
    /// A ContentControl that enables <see cref="IGraphicsContext.EnableAlphaTextHint"/>
    /// for its subtree, ensuring correct text alpha in DWM-composited regions.
    /// </summary>
    internal sealed class AlphaTextPanel : ContentControl
    {
        protected override void RenderSubtree(IGraphicsContext context)
        {
            context.EnableAlphaTextHint = true;
            try
            {
                base.RenderSubtree(context);
            }
            finally
            {
                context.EnableAlphaTextHint = false;
            }
        }
    }
}

public static class NativeCustomWindowExtensions
{
    public static NativeCustomWindow Content(this NativeCustomWindow w, UIElement? content)
    {
        w.Content = content;
        return w;
    }
}
