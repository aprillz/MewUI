using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Hosts a modal dialog inside its owner's own surface, for a platform that cannot give the dialog
/// a window of its own. The dialog's content is centred over a scrim that swallows every pointer
/// that does not land on it, which is what makes the dialog modal without an owner window to disable.
/// </summary>
internal sealed class InSurfaceDialogHost : UIElement, IVisualTreeHost
{
    private const string CLOSE_BUTTON_STYLE = "in-surface-dialog-close";
    private const double CAPTION_HEIGHT = 28;
    private const double CLOSE_BUTTON_WIDTH = 32;
    private const double CLOSE_GLYPH_SIZE = 4;
    private const double CORNER_RADIUS = 8;
    private const double SHADOW_BLUR = 16;
    private const double SHADOW_OFFSET_Y = 4;

    // Without a static constructor the type is beforefieldinit, and nothing reads the field below,
    // so the registration can still be pending when the caption button resolves its style name.
    static InSurfaceDialogHost() { }

    private static readonly bool _closeButtonStyleRegistered =
        FrameworkNamedStyles.Register(CLOSE_BUTTON_STYLE, CreateCloseButtonStyle);

    // Dimming behind a modal is not a themed colour anywhere else, so it is stated here.
    private const byte SCRIM_ALPHA = 96;
    // Zero on either side means the scrim gets there in one frame. Where it does fade the shape has
    // to stay ease-in-out: the default ease-out front-loads a fade badly enough that a short one
    // reaches full strength almost at once and reads as no fade at all.
    private const int SCRIM_FADE_IN_MS = 150;
    private const int SCRIM_FADE_OUT_MS = 0;

    private static readonly TimeSpan _scrimFadeIn = TimeSpan.FromMilliseconds(SCRIM_FADE_IN_MS);
    private static readonly TimeSpan _scrimFadeOut = TimeSpan.FromMilliseconds(SCRIM_FADE_OUT_MS);

    private readonly Window _dialog;
    private readonly Window _rootOwner;
    private readonly ShadowDecorator _shadow;
    private Border? _frame;
    private AnimationClock? _scrimClock;
    private double _scrimProgress;
    private bool _fadingOut;

    internal InSurfaceDialogHost(Window dialog, UIElement content, Window owner)
    {
        _dialog = dialog;

        var root = owner;
        while (root.Owner != null)
        {
            root = root.Owner;
        }

        _rootOwner = root;
        _shadow = BuildChrome(dialog, content);
        Parent = owner;
        _shadow.Parent = this;

        // The owner does not move or dim on its own here the way a disabled window does, so the
        // dimming appearing in one frame reads as the whole page having been repainted.
        if (_scrimFadeIn <= TimeSpan.Zero)
        {
            _scrimProgress = 1;
        }
        else
        {
            EnsureScrimClock(_scrimFadeIn).Start();
        }
    }

    // A clock rejects a duration of zero, so one is made only for a side that actually fades.
    private AnimationClock EnsureScrimClock(TimeSpan duration)
    {
        _scrimClock ??= new AnimationClock(duration, Easing.EaseInOutCubic).AttachTo(this);
        _scrimClock.TickCallback = OnScrimTick;
        _scrimClock.Duration = duration;
        return _scrimClock;
    }

    private void OnScrimTick(double easedProgress)
    {
        _scrimProgress = _fadingOut ? 1 - easedProgress : easedProgress;
        InvalidateVisual();
    }

    /// <summary>
    /// Fades the dialog back out and calls <paramref name="remove"/> once it is gone. The owner is
    /// live again from the moment this starts, so the host stops taking pointers even though it is
    /// still on screen.
    /// </summary>
    internal void FadeOutAndRemove(Action remove)
    {
        IsHitTestVisible = false;
        _fadingOut = true;

        if (_scrimFadeOut <= TimeSpan.Zero)
        {
            _scrimProgress = 0;
            remove();
            return;
        }

        var clock = EnsureScrimClock(_scrimFadeOut);
        clock.CompletedCallback = remove;
        clock.Stop();
        clock.Start();
    }

    /// <summary>
    /// Releases the fade before the host leaves the overlay. A running clock holds the render loop
    /// awake and keeps invalidating an element nothing draws any more.
    /// </summary>
    internal void Detach()
    {
        if (_scrimClock == null)
        {
            return;
        }

        _scrimClock.CompletedCallback = null;
        _scrimClock.Stop();
    }

    internal Window Dialog => _dialog;

    /// <summary>Reapplies the border colour after the dialog's front-most or focused state changed.</summary>
    internal void RefreshActiveBorder()
    {
        if (_frame is Border frame)
        {
            frame.BorderBrush = ChromeBorderColor(frame.ThemeInternal);
        }
    }

    // The dialog has no window of its own to be activated, so the chrome borrows the custom chrome
    // rule: accent while it is the front dialog on a focused surface, the plain border otherwise.
    private Color ChromeBorderColor(Theme theme)
    {
        if (_dialog.ActiveInSurfaceDialog == null && _rootOwner.IsActive)
        {
            return theme.Palette.Accent;
        }
        else
        {
            return theme.Palette.ControlBorder;
        }
    }

    // Nothing paints behind the dialog here, so the frame has to supply what the window would have
    // had: an opaque surface, a border, rounded corners and a shadow to lift it off the owner.
    private ShadowDecorator BuildChrome(Window dialog, UIElement content)
    {
        var body = new Border { Padding = dialog.Padding, Child = content };
        var stack = new DockPanel();
        if (BuildCaption(dialog) is UIElement caption)
        {
            stack.Add(caption.DockTop());
        }

        stack.Add(body);

        var frame = new Border
        {
            CornerRadius = CORNER_RADIUS,
            BorderThickness = 1,
            ClipToBounds = true,
            Child = stack,
        }.WithTheme((theme, border) =>
        {
            border.Background = dialog.EffectiveOpaqueBackground;
            border.BorderBrush = ChromeBorderColor(theme);
        });
        _frame = frame;

        return new ShadowDecorator
        {
            BlurRadius = SHADOW_BLUR,
            OffsetY = SHADOW_OFFSET_Y,
            CornerRadius = CORNER_RADIUS,
            Child = frame,
        }.WithTheme((theme, shadow) =>
            shadow.ShadowColor = Color.FromArgb((byte)(theme.IsDark ? 128 : 56), 0, 0, 0));
    }

    private static UIElement? BuildCaption(Window dialog)
    {
        if (string.IsNullOrEmpty(dialog.Title))
        {
            return null;
        }

        var title = new TextBlock()
            .Text(dialog.Title!)
            .FontWeight(FontWeight.SemiBold)
            .CenterVertical()
            .Margin(new Thickness(12, 0, 12, 0));

        var bar = new DockPanel();
        if (dialog.CanClose)
        {
            bar.Add(new Button
            {
                Content = new GlyphElement().Kind(GlyphKind.Cross).GlyphSize(CLOSE_GLYPH_SIZE),
                MinWidth = CLOSE_BUTTON_WIDTH,
                MinHeight = CAPTION_HEIGHT,
                StyleName = CLOSE_BUTTON_STYLE,
            }.OnClick(dialog.Close).DockRight());
        }

        bar.Add(title);

        return new Border { MinHeight = CAPTION_HEIGHT, Child = bar }
            .WithTheme((theme, border) => border.Background = theme.Palette.ControlBackground);
    }

    private static Style CreateCloseButtonStyle()
        => new(typeof(Button))
        {
            Transitions = [Transition.Create(Control.BackgroundProperty)],
            Setters =
            [
                Setter.Create(Control.BackgroundProperty, Color.Transparent),
                Setter.Create(Control.BorderThicknessProperty, 0.0),
                Setter.Create(Control.CornerRadiusProperty, 0.0),
                Setter.Create(Control.PaddingProperty, new Thickness(0)),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, Color.FromRgb(232, 17, 35)),
                        Setter.Create(Control.ForegroundProperty, Color.White),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Pressed,
                    Setters =
                    [
                        Setter.Create(Control.BackgroundProperty, Color.FromRgb(200, 12, 28)),
                        Setter.Create(Control.ForegroundProperty, Color.White),
                    ],
                },
            ],
        };

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => visitor(_shadow);

    // Keys reach the owner window, not the dialog, because the dialog has no surface of its own to
    // route through. Bubbling passes through this host on its way out, which is where the dialog is
    // given the preview it would have received as a window.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled)
        {
            _dialog.RaisePreviewKeyDown(e);
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!e.Handled)
        {
            _dialog.RaisePreviewKeyUp(e);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _shadow.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var desired = _shadow.DesiredSize;
        double width = Math.Min(desired.Width, finalSize.Width);
        double height = Math.Min(desired.Height, finalSize.Height);
        _shadow.Arrange(new Rect(
            (finalSize.Width - width) / 2,
            (finalSize.Height - height) / 2,
            width,
            height));
        return finalSize;
    }

    // A pointer that misses the dialog is absorbed rather than passed through, so the owner stays
    // untouchable for as long as the dialog is up.
    protected override UIElement? OnHitTest(Point point)
        => _shadow.HitTest(point) ?? (Bounds.Contains(point) ? this : null);

    protected override void OnRender(IGraphicsContext context)
    {
        context.FillRectangle(Bounds, Color.FromArgb((byte)(SCRIM_ALPHA * _scrimProgress), 0, 0, 0));
        _shadow.Render(context);
    }
}
