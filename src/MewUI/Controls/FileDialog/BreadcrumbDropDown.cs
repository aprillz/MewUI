using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A breadcrumb separator chevron that opens a list of the folder's subdirectories. It reuses the popup
/// lifecycle from <see cref="PopupOwnerBase"/> and renders only a bare chevron (no ComboBox chrome), so
/// it keeps the plain glyph footprint. The chevron rotates from "&gt;" to "v" as the popup opens.
/// </summary>
internal sealed class BreadcrumbDropDown : PopupOwnerBase
{
    private const double GLYPH_SIZE = 4;
    private const double ANIM_MS = 140;

    private readonly string _folderPath;
    private readonly Func<string, IReadOnlyList<string>> _listSubdirs;
    private readonly Action<string> _navigate;

    private IReadOnlyList<string> _subdirs = [];
    private AnimationClock? _clock;
    private double _progress;        // 0 closed .. 1 open (chevron rotation)
    private double _fromProgress;
    private double _toProgress;

    public BreadcrumbDropDown(string folderPath, Func<string, IReadOnlyList<string>> listSubdirs, Action<string> navigate)
    {
        _folderPath = folderPath;
        _listSubdirs = listSubdirs;
        _navigate = navigate;

        // Bare glyph: no chrome and no tab stop. It remains focusable so clicking the owner while its
        // popup is open does not clear focus, close the popup, and then reopen it with the same press.
        Background = Color.Transparent;
        BorderThickness = 0;
        // Same padding as the crumb buttons so chevron and crumbs read as consistent clickable segments.
        Padding = new Thickness(4);
        Focusable = true;
        IsTabStop = false;
        // Fill the crumb row height so the hover background is a comfortable target; width stays the glyph
        // footprint, so horizontal spacing is unchanged.
        VerticalAlignment = VerticalAlignment.Stretch;
    }

    // The glyph footprint (GlyphElement measures GlyphSize*2) plus padding; FrameworkElement does not add
    // padding around MeasureContent, so include it here to size the clickable/hover segment.
    protected override Size MeasureContent(Size availableSize)
        => new(GLYPH_SIZE * 2 + Padding.HorizontalThickness, GLYPH_SIZE * 2 + Padding.VerticalThickness);

    protected override bool PopupSizesToContent => true;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        // Only open when the folder has subfolders; an empty folder leaves the chevron inert.
        if (e.Button == MouseButton.Left && !IsDropDownOpen)
        {
            _subdirs = _listSubdirs(_folderPath);
            if (_subdirs.Count == 0)
            {
                return;
            }
        }

        base.OnMouseDown(e);
    }

    protected override void OnIsDropDownOpenChanged(bool oldValue, bool newValue)
    {
        base.OnIsDropDownOpenChanged(oldValue, newValue);
        AnimateTo(newValue ? 1.0 : 0.0);
    }

    protected override UIElement CreatePopupContent()
    {
        var list = new ListBox().Items(_subdirs.ToArray()).ZebraStriping(false);
        list.SelectionChanged += OnPopupSelectionChanged;
        return list;
    }

    protected override void SyncPopupContent(UIElement popup)
    {
        // Cheap and idempotent (called again per-frame while bounds are dirty): just clear a stale pick.
        if (popup is ListBox list && list.SelectedIndex >= 0)
        {
            list.SelectedIndex = -1;
        }
    }

    private void OnPopupSelectionChanged(object? item)
    {
        if (item is string name && name.Length > 0)
        {
            IsDropDownOpen = false;
            _navigate(Path.Combine(_folderPath, name));
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // Hover/open feedback matching the adjacent flat crumb buttons, so the chevron reads as clickable.
        var state = CurrentVisualState;
        if ((state.Flags & VisualStateFlags.Hot) != 0 || IsDropDownOpen)
        {
            double radius = LayoutRounding.RoundToPixel(Theme.Metrics.ControlCornerRadius, GetDpi() / 96.0);
            context.FillRoundedRectangle(Bounds, radius, radius, Theme.Palette.ButtonHoverBackground.WithAlpha(128));
        }

        var color = Theme.Palette.DisabledText;
        double angle = _progress * (Math.PI / 2);   // '>' rotates toward 'v' as the popup opens
        double centerX = Bounds.X + Bounds.Width / 2;
        double centerY = Bounds.Y + Bounds.Height / 2;

        var saved = context.GetTransform();
        context.Translate(centerX, centerY);
        context.Rotate(angle);
        Glyph.Draw(context, new Point(0, 0), GLYPH_SIZE, color, GlyphKind.ChevronRight);
        context.SetTransform(saved);

        // Popup bounds update.
        base.OnRender(context);
    }

    private void AnimateTo(double target)
    {
        _fromProgress = _progress;
        _toProgress = target;
        _clock ??= new AnimationClock(TimeSpan.FromMilliseconds(ANIM_MS), Easing.Default)
            .AttachTo(this);
        _clock.TickCallback = OnAnimationTick;
        _clock.Stop();
        _clock.Start();
    }

    private void OnAnimationTick(double easedProgress)
    {
        _progress = _fromProgress + (_toProgress - _fromProgress) * easedProgress;
        InvalidateVisual();
    }

    protected override void OnDispose()
    {
        _clock?.Stop();
        _clock = null;
        base.OnDispose();
    }
}
