using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Diagnostics;

internal sealed class DebugInspectorOverlay : Control
{
    private const byte OverlayAlpha = 160;
    private const double PanelMargin = 8.0;
    private const double PanelPadding = 8.0;
    private const double PanelMaxTextWidth = 420.0;
    private const double PanelCornerRadius = 6.0;
    private const double PanelAvoidSlop = 24.0;
    private const double PanelEstimatedHeight = 120.0;
    private static readonly Color HoverBoundsColor = Color.FromRgb(80, 160, 255).WithAlpha(OverlayAlpha);
    private static readonly Color FocusBoundsColor = Color.FromRgb(255, 120, 80).WithAlpha(OverlayAlpha);
    private static readonly Color SelectedBoundsColor = Color.FromRgb(255, 120, 80).WithAlpha(OverlayAlpha);
    private static readonly Color PanelBackgroundColor = Color.FromRgb(20, 20, 20).WithAlpha(OverlayAlpha);
    private static readonly Color PanelBorderColor = Color.FromRgb(80, 160, 255).WithAlpha(OverlayAlpha);
    private static readonly Color PanelTextColor = Color.FromRgb(255, 255, 255).WithAlpha(OverlayAlpha);

    private readonly Window _window;
    private string? _cachedText;
    private UIElement? _cachedHovered;
    private UIElement? _cachedFocused;
    private UIElement? _cachedPinned;

    public UIElement? HighlightedElement { get; set; }

    public DebugInspectorOverlay(Window window)
    {
        _window = window;
        Background = Color.Transparent;
    }

    public bool ShouldAvoidMouse(Point mousePosition)
        => IsPointNearTopLeftInfoPanel(mousePosition);

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        var mousePos = _window.LastMousePositionDip;
        var hovered = _window.HitTest(mousePos);

        // Don't highlight the inspector itself (it should not be hit-testable, but keep this defensive).
        if (hovered is Adorner)
        {
            hovered = null;
        }

        var focused = _window.FocusManager.FocusedElement;
        var pinned = HighlightedElement;

        if (hovered != null &&
            !ReferenceEquals(hovered, focused) &&
            !ReferenceEquals(hovered, pinned))
        {
            DrawElementBounds(context, hovered, HoverBoundsColor);
        }

        if (focused != null &&
            !ReferenceEquals(focused, pinned))
        {
            DrawElementBounds(context, focused, FocusBoundsColor);
        }

        if (pinned != null)
        {
            DrawElementBounds(context, pinned, SelectedBoundsColor);
        }

        DrawInfoPanel(context, hovered, focused, pinned);
    }

    private void DrawElementBounds(IGraphicsContext context, UIElement element, Color color)
    {
        var rect = GetElementRectInWindow(element);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        rect = LayoutRounding.SnapBoundsRectToPixels(rect, context.DpiScale);
        context.DrawRectangle(rect, color, thickness: 2, strokeInset: true);
    }

    private void DrawInfoPanel(IGraphicsContext context, UIElement? hovered, UIElement? focused, UIElement? pinned)
    {
        string text = GetOrBuildInspectorText(hovered, focused, pinned);
        var style = GetTextRunStyle();
        var layout = TextLayoutOperations.GetOrCreate(
            GetGraphicsFactory(), text, GetDpi(), in style, PanelMaxTextWidth, wrapping: TextWrapping.Wrap);
        var size = layout.MeasuredSize;
        var panelRect = GetInfoPanelRect(size, _window.LastMousePositionDip);
        panelRect = LayoutRounding.SnapBoundsRectToPixels(panelRect, context.DpiScale);
        context.FillRoundedRectangle(panelRect, PanelCornerRadius, PanelCornerRadius, PanelBackgroundColor);
        context.DrawRoundedRectangle(panelRect, PanelCornerRadius, PanelCornerRadius, PanelBorderColor, 1, strokeInset: true);
        TextLayoutOperations.DrawInBounds(
            context, layout, panelRect.Deflate(new Thickness(PanelPadding)), PanelTextColor, owner: this);
    }

    private Rect GetInfoPanelRect(Size contentSize, Point mousePosition)
    {
        double width = contentSize.Width + PanelPadding * 2;
        double height = contentSize.Height + PanelPadding * 2;

        if (IsPointNearTopLeftInfoPanel(mousePosition))
        {
            return new Rect(
                Math.Max(Bounds.X + PanelMargin, Bounds.Right - width - PanelMargin),
                Math.Max(Bounds.Y + PanelMargin, Bounds.Bottom - height - PanelMargin),
                width,
                height);
        }

        return new Rect(Bounds.X + PanelMargin, Bounds.Y + PanelMargin, width, height);
    }

    private bool IsPointNearTopLeftInfoPanel(Point point)
    {
        double width = PanelMaxTextWidth + PanelPadding * 2;
        double height = PanelEstimatedHeight + PanelPadding * 2;
        var topLeftPanelZone = new Rect(Bounds.X + PanelMargin, Bounds.Y + PanelMargin, width, height)
            .Inflate(new Thickness(PanelAvoidSlop));
        return topLeftPanelZone.Contains(point);
    }

    private string GetOrBuildInspectorText(UIElement? hovered, UIElement? focused, UIElement? pinned)
    {
        if (ReferenceEquals(_cachedHovered, hovered) &&
            ReferenceEquals(_cachedFocused, focused) &&
            ReferenceEquals(_cachedPinned, pinned) &&
            _cachedText != null)
        {
            return _cachedText;
        }

        _cachedHovered = hovered;
        _cachedFocused = focused;
        _cachedPinned = pinned;

        string hoverText = hovered != null ? $"{hovered.GetType().Name} {FormatRect(GetElementRectInWindow(hovered))}" : "(none)";
        string focusText = focused != null ? $"{focused.GetType().Name} {FormatRect(GetElementRectInWindow(focused))}" : "(none)";
        string pinText = pinned != null ? $"{pinned.GetType().Name} {FormatRect(GetElementRectInWindow(pinned))}" : "(none)";

        var sb = new System.Text.StringBuilder(512);
        sb.Append("Inspector: Ctrl/Cmd+Shift+I\n");
        sb.Append("VisualTree: Ctrl/Cmd+Shift+T\n");
        sb.Append("Hover: ").Append(hoverText).Append('\n');
        sb.Append("Focus: ").Append(focusText).Append('\n');
        sb.Append("Selected: ").Append(pinText);

        _cachedText = sb.ToString();
        return _cachedText;
    }

    private static Rect GetElementRectInWindow(UIElement element)
    {
        var size = element.RenderSize;
        var local = new Rect(0, 0, size.Width, size.Height);

        // Translate into Window coordinate space (what the overlay draws in).
        if (element.FindVisualRoot() is Window window)
        {
            return element.TranslateRect(local, window);
        }

        // Fallback to whatever we have (debug-only).
        return element.Bounds;
    }

    private static string FormatRect(Rect r)
        => $"[{r.X:0.#},{r.Y:0.#} {r.Width:0.#}x{r.Height:0.#}]";
}
