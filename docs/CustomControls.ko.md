# 커스텀 컨트롤

## 개요

- 이 문서는 MewUI에서 **커스텀 컨트롤을 구현하는 개발자 레퍼런스**입니다.
- 크기 계산은 **DIP**, 렌더링은 **픽셀 정렬**이 전제입니다.
- 아래 샘플은 실제 동작하는 `NumericUpDown` 전체 코드이며, 주석은 **CustomControl 관점에서 각 지점이 담당하는 역할**을 설명합니다.

---

## 상세 설명

### <a id="scope"></a>범위와 규칙

- 크기 계산은 DIP, 렌더링은 픽셀 정렬이 전제입니다.
- Measure/Arrange는 **논리 좌표(DIP)**에서만 동작해야 하며, 픽셀 정렬은 Render에서만 적용합니다.
- DPI 변경 대응을 위해 `GetDpi()` / `context.DpiScale`를 사용합니다.
- **Measure 단계에서는 픽셀 연산을 하지 않습니다.** 픽셀 스냅을 섞으면 레이아웃 불일치가 발생합니다.

### <a id="measure"></a>크기 계산 (MeasureContent)

- `MeasureContent`는 **DesiredSize의 유일한 출처**입니다.
- 이 단계는 “컨트롤이 필요로 하는 공간”만 계산하며, 실제 배치 위치는 다루지 않습니다.
- 표시 문자열(포맷 반영)을 기준으로 텍스트를 측정합니다.
- `Padding`과 크롬(버튼 영역), `GetBorderVisualInset()`을 포함해 최종 크기를 결정합니다.
- 테마 기본 높이에 맞추려면 `DefaultMinHeight`를 `Theme.Metrics.BaseControlHeight`로 설정합니다.
- 이 경우 `MeasureContent`는 자연 높이를 반환하고, `MinHeight`가 기준 높이를 보장합니다.
- `Format`, `Value` 변경은 텍스트 폭이 바뀔 수 있으므로 **Measure 무효화**가 필요합니다.
- 캐시가 있는 경우라도 **측정 입력(폰트, DPI, 문자열, 래핑 정책)**이 바뀌면 즉시 무효화합니다.

예시:
```csharp
protected override double DefaultMinHeight => Theme.Metrics.BaseControlHeight;

protected override Size MeasureContent(Size available)
{
    var textHeight = /* 텍스트 높이 측정 */;
    double height = textHeight + Padding.VerticalThickness;
    return new Size(Width, height);
}
```

### <a id="arrange"></a>내부 배치 (ArrangeContent)

- 이 예시는 `ArrangeContent`를 오버라이드하지 않고, **최종 `Bounds` 기준으로 내부 레이아웃을 계산**합니다.
- 자식이 있는 컨트롤은 이 단계에서 child rect를 계산하고 `Arrange`해야 합니다.
- Arrange는 “컨트롤이 받은 공간에서 자식을 어디에 둘지”를 확정하는 단계입니다.
- **Measure에서 계산한 DesiredSize**와 **실제 Bounds**가 다를 수 있다는 전제를 반드시 가져야 합니다.

### <a id="render"></a>렌더링 (OnRender)

- `GetSnappedBorderBounds`와 `LayoutRounding.SnapBoundsRectToPixels`로 픽셀 정렬을 보장합니다.
- 렌더 순서: **배경 → 보더 → 콘텐츠**.
- 레이아웃 계산과 렌더 경로가 동일한 기준(rect)을 공유하도록 구조화합니다.
- Render에서는 **측정값을 새로 계산하지 않습니다.** 이미 확정된 Bounds만 사용합니다.

### <a id="state"></a>상태와 입력

- 상호작용 상태(hover/pressed)는 **컨트롤 내부 상태**로 관리합니다.
- MouseDown에서 캡처하고 MouseUp에서 해제하여 입력 일관성을 보장합니다.
- Hit‑test 로직은 **렌더와 동일한 분할 기준**을 사용해야 합니다.
- 입력 처리에서 상태가 바뀌면 **InvalidateVisual**로만 해결되는지, **InvalidateMeasure**가 필요한지 구분합니다.
- 입력 게이팅은 `IsEnabled`가 아니라 **`IsEffectivelyEnabled`**를 기준으로 합니다.
  - 부모 컨트롤이 비활성화되면 자식의 `IsEnabled`가 `true`여도 입력을 받아서는 안 됩니다.
  - 따라서 입력 처리/색상 결정/상태 전이 모두 `IsEffectivelyEnabled`에 맞추는 것이 안전합니다.

### <a id="theme"></a>테마와 메트릭

- 색/크기는 `Theme.Palette.*`, `Theme.Metrics.*`를 사용합니다.
- 테마 변경 시 텍스트 측정 캐시를 무효화합니다.
- 테마 변화는 **색상뿐 아니라 폰트/크기/패딩 규칙**까지 바꿀 수 있으므로 Measure 재계산이 안전합니다.

### <a id="utils"></a>유틸리티 메서드 (상태, 보더, DIP)

- `GetDpi()`는 유효 DPI(`uint`)를 반환합니다. DIP→픽셀 변환은 보통 `dpiScale = GetDpi() / 96.0`를 사용합니다.
- `GetVisualState(...)`는 `enabled/hot/focused/pressed/active` 상태를 한 번에 계산한 스냅샷을 만듭니다.
- `PickAccentBorder(theme, baseBorder, state, hoverMix)`는 상태에 따라 보더 색을 결정합니다(포커스/눌림/활성은 Accent, hover는 tint).
- `DrawBackgroundAndBorder(context, bounds, background, borderBrush, cornerRadiusDip)`는 백엔드에 상관없이 일관된 배경/보더 렌더링을 제공합니다.
- `GetBorderRenderMetrics(bounds, cornerRadiusDip)`는 픽셀 스냅된 border 두께/코너 반경을 계산해 레이아웃과 렌더링이 어긋나지 않게 합니다.
- `LayoutRounding`은 fractional DPI에서 흔한 1px 잘림/떨림을 줄이기 위한 유틸입니다.
- `LayoutRounding.SnapBoundsRectToPixels(...)`는 background/border 같은 “박스” 지오메트리에 사용합니다.
- `LayoutRounding.SnapViewportRectToPixels(...)`는 viewport/clip 같은 “잘라야 하는 영역”에 사용합니다(줄어들지 않게).
- `LayoutRounding.SnapThicknessToPixels(...)`는 보더 두께를 정수 픽셀로 맞출 때 사용합니다.
- `LayoutRounding.ExpandClipByDevicePixels(...)`는 클립이 마지막 1px을 누락하지 않게 확장할 때 사용합니다.

예: 상태 기반 보더 + 픽셀 스냅

```csharp
var dpiScale = GetDpi() / 96.0;
var state = GetVisualState(isPressed: isPressed, isActive: isActive);
var border = PickAccentBorder(Theme, BorderBrush, state, hoverMix: 0.6);

var bounds = LayoutRounding.SnapBoundsRectToPixels(Bounds, dpiScale);
DrawBackgroundAndBorder(context, bounds, Background, border, cornerRadiusDip: 0);
```

### <a id="invalidate"></a>Invalidate 기준

- `Format` 변경: `InvalidateMeasure()` + `InvalidateVisual()`
- `Value` 변경: 텍스트 폭 변화 가능 → `InvalidateMeasure()`
- Hover/Pressed 변경: `InvalidateVisual()`

---

## 전체 샘플 코드

```csharp
public sealed class NumericUpDown : RangeBase
{
    // 컨트롤 내부의 상호작용 상태를 한 곳에 모으기 위한 타입이다.
    // 커스텀 컨트롤은 입력 처리와 렌더링이 같은 상태를 공유해야 하므로
    // 외부에 노출하지 않는 UI 상태는 내부 타입으로 묶어두는 것이 안전하다.
    private enum ButtonPart
    {
        None,
        Decrement,
        Increment
    }

    // 표시 방식은 레이아웃 크기에 직접 영향을 주는 상태다.
    // 커스텀 컨트롤에서는 표시 문자열이 바뀌면 Measure 결과가 바뀔 수 있으므로
    // 레이아웃 경로와 연결되는 상태로 취급해야 한다.
    private string _format = "0.##";

    // 상호작용 단위는 레이아웃과 무관하지만 입력 처리에는 필수다.
    // 입력 로직에서 반복 참조되는 기준 값은 상태로 보관한다.
    private double _step = 1;

    // 텍스트 측정 비용을 줄이고 레이아웃 안정성을 확보한다.
    // 커스텀 컨트롤은 Measure 경로가 자주 호출되므로 캐시가 필요하다.
    private TextMeasureCache _measureCache;

    // 시각적 상태를 보관한다.
    // Hover/Pressed는 렌더링 결과만 바꾸며 레이아웃에는 영향이 없다.
    private ButtonPart _hoverPart;
    private ButtonPart _pressedPart;

    public NumericUpDown()
    {
        // 컨트롤의 기본 크기 규칙을 확정한다.
        // Border는 Measure에 포함되어야 하며, 기본값을 지정해두는 편이 안전하다.
        BorderThickness = 1;

        // 기본 범위를 지정한다.
        Maximum = 100;

        // 콘텐츠와 크롬 영역을 분리한다.
        // 커스텀 컨트롤은 콘텐츠 영역과 장식/조작 영역을 분리해 설계하는 편이 일관적이다.
        Padding = new Thickness(8, 4, 8, 4);
    }

    // 기본 테마 색을 제공해 일관된 기본 스타일을 보장한다.
    protected override Color DefaultBackground => Theme.Palette.ControlBackground;
    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;
    protected override double DefaultMinHeight => Theme.Metrics.BaseControlHeight;

    // 키보드 입력을 수용하는 컨트롤임을 명시한다.
    public override bool Focusable => true;

    public string Format
    {
        get => _format;
        set
        {
            // 불필요한 invalidation을 방지한다.
            if (_format == value)
            {
                return;
            }

            // 표시 방식 변경을 반영한다.
            _format = value;

            // 텍스트 측정 캐시를 무효화한다.
            _measureCache.Invalidate();

            // DesiredSize 재계산이 필요함을 알린다.
            InvalidateMeasure();

            // 시각 갱신이 필요함을 알린다.
            InvalidateVisual();
        }
    }

    public double Step
    {
        get => _step;
        set
        {
            if (_step.Equals(value))
            {
                return;
            }

            // 입력/상호작용 기준값을 갱신한다.
            _step = value;

            // 레이아웃과 시각에 영향을 주지 않는 상태라면
            // 여기서 invalidate를 호출하지 않는다.
        }
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        // 테마 변경으로 텍스트 메트릭이 바뀔 수 있으므로 캐시 무효화.
        _measureCache.Invalidate();
    }

    protected override void OnValueChanged(double value, bool fromUser)
    {
        // 표시 문자열이 바뀌면 폭이 달라질 수 있으므로 Measure 재요청.
        _measureCache.Invalidate();
        InvalidateMeasure();

        // 필요 시 Visual 갱신도 추가 가능.
    }

    protected override Size MeasureContent(Size available)
    {
        // MeasureContent는 커스텀 컨트롤의 원하는 크기를 확정하는 핵심 경로다.
        // DIP 단위로 계산하며, 픽셀 스냅은 Render 단계에서만 처리한다.
        var factory = GetGraphicsFactory();
        var font = GetFont(factory);

        // 실제 표시될 문자열을 기준으로 측정한다.
        string text = Value.ToString(_format);
        var textSize = _measureCache.Measure(factory, GetDpi(), font, text, TextWrapping.NoWrap, 0);

        // 컨트롤의 크롬 영역(조작/장식)을 크기 계산에 포함한다.
        double buttonAreaWidth = GetButtonAreaWidth();

        // 콘텐츠 + 패딩 + 크롬의 합으로 폭을 결정한다.
        double width = textSize.Width + Padding.HorizontalThickness + buttonAreaWidth;

        // 자연 높이를 사용하고, MinHeight가 기준 높이를 보장한다.
        double height = textSize.Height + Padding.VerticalThickness;

        // 보더 inset까지 포함해 DesiredSize를 확정한다.
        return new Size(width, height).Inflate(new Thickness(GetBorderVisualInset()));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // OnRender는 최종 Bounds가 확정된 뒤 그려지는 단계다.
        // 커스텀 컨트롤은 여기서 픽셀 스냅된 기하를 사용해야 흔들림이 없다.
        var bounds = GetSnappedBorderBounds(Bounds);

        // 모서리 반경 등 스타일은 테마에서 가져온다.
        double radius = Theme.Metrics.ControlCornerRadius;

        // 상태에 따른 색상 결정은 렌더 전에 정리한다.
        bool isEnabled = IsEffectivelyEnabled;
        Color bg = isEnabled ? Background : Theme.Palette.DisabledControlBackground;
        Color baseBorder = isEnabled ? BorderBrush : Theme.Palette.ControlBorder;
        var state = GetVisualState(isPressed: _pressedPart != ButtonPart.None, isActive: _pressedPart != ButtonPart.None);
        Color border = PickAccentBorder(Theme, baseBorder, state, hoverMix: 0.6);

        // 크롬(배경/보더)을 먼저 렌더한다.
        DrawBackgroundAndBorder(context, bounds, bg, border, radius);

        // 내부 레이아웃을 bounds 기준으로 계산한다.
        var inner = bounds.Deflate(new Thickness(GetBorderVisualInset()));

        // 콘텐츠 영역과 크롬 영역을 분할한다.
        double buttonAreaWidth = Math.Min(GetButtonAreaWidth(), inner.Width);
        var buttonRect = new Rect(inner.Right - buttonAreaWidth, inner.Y, buttonAreaWidth, inner.Height);
        var textRect = new Rect(
            inner.X + Padding.Left,
            inner.Y + Padding.Top,
            Math.Max(0, inner.Width - buttonAreaWidth - Padding.HorizontalThickness),
            Math.Max(0, inner.Height - Padding.VerticalThickness));

        // 서브 rect도 픽셀 스냅을 적용한다.
        textRect = LayoutRounding.SnapBoundsRectToPixels(textRect, context.DpiScale);
        buttonRect = LayoutRounding.SnapBoundsRectToPixels(buttonRect, context.DpiScale);

        // 입력 처리와 렌더링이 동일한 분할 기준을 공유한다.
        var decRect = new Rect(buttonRect.X, buttonRect.Y, buttonRect.Width / 2, buttonRect.Height);
        var incRect = new Rect(buttonRect.X + buttonRect.Width / 2, buttonRect.Y, buttonRect.Width / 2, buttonRect.Height);

        // 상태에 따라 서브 영역 색상을 결정한다.
        Color baseButton = Theme.Palette.ButtonFace;
        Color hoverButton = Theme.Palette.ButtonHoverBackground;
        Color pressedButton = Theme.Palette.ButtonPressedBackground;
        Color disabledButton = Theme.Palette.ButtonDisabledBackground;

        Color decBg = !isEnabled
            ? disabledButton
            : _pressedPart == ButtonPart.Decrement ? pressedButton
            : _hoverPart == ButtonPart.Decrement ? hoverButton
            : baseButton;

        Color incBg = !isEnabled
            ? disabledButton
            : _pressedPart == ButtonPart.Increment ? pressedButton
            : _hoverPart == ButtonPart.Increment ? hoverButton
            : baseButton;

        if (buttonRect.Width > 0)
        {
            // 크롬 영역을 렌더한다.
            context.FillRectangle(decRect, decBg);

            var innerRadius = Math.Max(0, radius - GetBorderVisualInset());
            context.Save();
            context.SetClipRoundedRect(
                LayoutRounding.MakeClipRect(inner, context.DpiScale, rightPx: 0, bottomPx: 0),
                innerRadius,
                innerRadius);
            context.FillRectangle(incRect, incBg);
            context.Restore();

            // 시각적 분리를 위해 경계선을 그린다.
            var x = decRect.Right;
            context.DrawLine(new Point(x, decRect.Y + 4), new Point(x, decRect.Bottom - 4), Theme.Palette.ControlBorder, 1);

            x = decRect.Left;
            context.DrawLine(new Point(x, decRect.Y + 1), new Point(x, decRect.Bottom - 1), Theme.Palette.ControlBorder, 1);
        }

        // 텍스트는 마지막에 렌더하여 크롬 위에 올린다.
        var font = GetFont();
        var textColor = isEnabled ? Foreground : Theme.Palette.DisabledText;
        context.DrawText(Value.ToString(_format), textRect, font, textColor, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

        if (buttonRect.Width > 0)
        {
            // 아이콘/글리프 크기는 테마 메트릭을 따른다.
            var chevronSize = Theme.Metrics.BaseControlHeight / 6;
            Glyph.Draw(context, decRect.Center, chevronSize, textColor, GlyphKind.ChevronDown);
            Glyph.Draw(context, incRect.Center, chevronSize, textColor, GlyphKind.ChevronUp);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        // 비활성 상태 입력을 차단한다.
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        // Wheel 입력을 도메인 값 변경으로 매핑한다.
        double delta = e.Delta > 0 ? _step : -_step;
        Value += delta;

        // 값 변경이 즉시 시각에 반영되어야 하므로 Visual 갱신.
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        // 입력 시작 지점이다.
        // 커스텀 컨트롤은 “어떤 입력만 처리할지”를 명확히 제한해야 하고,
        // 이 단계에서 포커스/캡처/상태 초기화를 결정한다.
        // 처리 조건을 명확히 한다(좌클릭 + 활성).
        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        // 키보드 입력을 받을 컨트롤임을 보장한다.
        Focus();

        // Hit‑test 결과를 상태로 저장한다.
        var part = HitTestButtonPart(e.Position);
        if (part == ButtonPart.None)
        {
            return;
        }

        _pressedPart = part;

        // MouseUp 수신 보장을 위해 캡처를 사용한다.
        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // Hover 상태를 갱신하여 시각 피드백을 제공한다.
        var part = HitTestButtonPart(e.Position);
        if (_hoverPart != part)
        {
            _hoverPart = part;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();

        // 캡처 중이 아닐 때만 hover 해제.
        if (_hoverPart != ButtonPart.None && !IsMouseCaptured)
        {
            _hoverPart = ButtonPart.None;
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        // 입력 종료 지점이다.
        // 캡처 해제와 상태 정리를 여기에서 일관되게 수행해야 한다.
        // 관련 없는 입력은 무시한다.
        if (e.Button != MouseButton.Left || _pressedPart == ButtonPart.None)
        {
            return;
        }

        // 캡처 해제.
        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }

        // Down과 Up이 동일 영역일 때만 동작 수행.
        var releasedPart = HitTestButtonPart(e.Position);
        if (releasedPart == _pressedPart && IsEffectivelyEnabled)
        {
            Value += _pressedPart == ButtonPart.Increment ? _step : -_step;
        }

        _pressedPart = ButtonPart.None;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // 키보드 경로는 마우스 경로와 별개이므로 조건을 명확히 한다.
        // 포커스/활성 여부를 먼저 확인하고, 상태 변화에 맞게 invalidate한다.
        // 키보드 입력 조건을 명확히 한다.
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            Value += _step;
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            Value -= _step;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    // 크롬 폭 규칙을 중앙화한다.
    private double GetButtonAreaWidth() => Theme.Metrics.BaseControlHeight * 2;

    private (Rect decRect, Rect incRect) GetButtonRects()
    {
        // Hit‑test와 렌더가 동일한 기준을 공유하도록 분리한다.
        var inner = GetSnappedBorderBounds(Bounds).Deflate(new Thickness(GetBorderVisualInset()));
        double buttonAreaWidth = Math.Min(GetButtonAreaWidth(), inner.Width);
        var buttonRect = new Rect(inner.Right - buttonAreaWidth, inner.Y, buttonAreaWidth, inner.Height);
        var decRect = new Rect(buttonRect.X, buttonRect.Y, buttonRect.Width / 2, buttonRect.Height);
        var incRect = new Rect(buttonRect.X + buttonRect.Width / 2, buttonRect.Y, buttonRect.Width / 2, buttonRect.Height);
        return (decRect, incRect);
    }

    private ButtonPart HitTestButtonPart(Point position)
    {
        // 입력 경로에서 동일한 hit‑test 로직을 재사용한다.
        var (decRect, incRect) = GetButtonRects();
        if (decRect.Contains(position))
        {
            return ButtonPart.Decrement;
        }
        if (incRect.Contains(position))
        {
            return ButtonPart.Increment;
        }
        return ButtonPart.None;
    }
}
```
