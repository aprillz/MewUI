# 커스텀 컨트롤

## 개요

- 이 문서는 MewUI에서 **커스텀 컨트롤을 구현하는 개발자 레퍼런스**입니다.
- 크기 계산은 **DIP**, 렌더링은 **픽셀 정렬**이 전제입니다.
- 공개 상태는 `MewProperty`로 선언하고, 상태별 모양은 `Style`의 `StateTrigger`로 선언하며, `OnRender`는 변경 시 비주얼이 무효화되는 값만 읽어서 그립니다.
- 아래 샘플은 직접 그리는 `SimpleNumericUpDown` 전체 코드이며, 주석은 **CustomControl 관점에서 각 지점이 담당하는 역할**을 설명합니다. 기본 제공 `NumericUpDown`은 템플릿으로 파트를 구성합니다([템플릿](#template) 참고). 샘플은 모든 오버라이드 지점을 한곳에서 보이기 위해 전부 직접 그립니다.

---

## 상세 설명

### <a id="scope"></a>범위와 규칙

- 크기 계산은 DIP, 렌더링은 픽셀 정렬이 전제입니다.
- Measure/Arrange는 **논리 좌표(DIP)**에서만 동작해야 하며, 픽셀 정렬은 Render에서만 적용합니다.
- DPI 변경 대응을 위해 `GetDpi()` / `context.DpiScale`를 사용합니다.
- **Measure 단계에서는 픽셀 연산을 하지 않습니다.** 픽셀 스냅을 섞으면 레이아웃 불일치가 발생합니다.
- `Bounds`는 부모 기준이 아니라 **창 좌표**입니다. 자식의 `Arrange`에 넘기는 rect와 `OnRender`에서 그리는 rect도 창 좌표입니다.

### <a id="base"></a>기반 클래스 선택

| 기반 클래스 | 사용하는 경우 |
|---|---|
| `Control` | 컨트롤이 직접 그리거나 템플릿으로 비주얼을 구성하는 경우입니다. `Background`, `BorderBrush`, `BorderThickness`, `CornerRadius`, `Padding`, 폰트 속성, `Foreground`, 스타일, 시각 상태, `Template`을 제공합니다. |
| `ContentControl` | 단일 `Content` 요소를 보여 주는 경우입니다. 콘텐츠의 측정, 배치, 렌더링, 히트 테스트가 이미 구현되어 있습니다. |
| `CommandSourceControl` | `Command`를 실행하는 `ContentControl`입니다(`Command`, `CommandData`, `QueryCommandCanExecute()`, `InvokeCommand()`). `Button`이 이 클래스에서 파생됩니다. |
| `RangeBase` | `Value`, `Minimum`, `Maximum`과 범위 제한이 필요한 경우입니다. `OnValueChanged(double value)`와 `OnCoerceValue(double value)`를 오버라이드하고, 입력으로 값을 바꿀 때는 `CommitValue(double value)`를 사용합니다. |
| `Panel` | 여러 자식을 배치하는 요소입니다(`Children`, `Add`, `Remove`). 패널은 `Control`이 아니라 `FrameworkElement`이므로 스타일, 템플릿, 시각 상태가 없습니다. |

### <a id="properties"></a>속성 (MewProperty)

- 공개 상태는 `MewProperty<T>.Register<TOwner>(name, defaultValue, options, changed, coerce, validate)`로 선언하고, `GetValue` / `SetValue`를 호출하는 CLR 속성으로 노출합니다. 그래야 값에 스타일, 바인딩, 애니메이션을 사용할 수 있습니다.
- `MewPropertyOptions`가 무효화를 자동으로 처리합니다. `AffectsLayout`은 `InvalidateMeasure()`를, `AffectsRender`는 `InvalidateVisual()`을, `AffectsVisualState`는 `InvalidateVisualState()`를 호출합니다. `Inherits`는 요소에 값이 없을 때 부모 체인에서 값을 가져오고, `BindsTwoWayByDefault`는 `Bind`의 기본 모드를 양방향으로 만듭니다.
- `changed`는 `(owner, oldValue, newValue)`를 받고, `coerce`는 `(owner, proposedValue)`를 받아 저장할 값을 반환하며, `validate`는 `(owner, proposedValue)`를 받아 거부할 때 예외를 던집니다. 강제 조건이 의존하는 값이 바뀌면 `CoerceValue(property)`를 호출합니다.
- 컨트롤이 스스로 도출하는 상태(눌림, 선택, 편집 중)는 **읽기 전용** 속성으로 만듭니다. `RegisterReadOnly`가 `MewPropertyKey<T>`를 반환하면 키는 private으로 보관하고 `Key.Property`만 공개하며, 컨트롤은 `SetValue(key, value)`로 값을 씁니다.
- 타입별 기본값은 static 생성자에서 `SomeProperty.OverrideDefaultValue<TOwner>(value)`로 지정합니다. 기본값이므로 `ClearLocalValue(property)`를 호출하면 이 값으로 돌아가고, 스타일이 이 값을 덮어쓸 수 있습니다.
- 값 우선순위는 `Local > Animated > Trigger > Style > Inherited > Default`입니다. 생성자에서 대입한 값은 로컬 값이므로 해당 속성의 **스타일 setter와 트리거를 막습니다.** 모양에 관한 기본값(색, 패딩, 최소 크기)은 생성자가 아니라 스타일에 둡니다.

```csharp
public static readonly MewProperty<string> FormatProperty =
    MewProperty<string>.Register<SimpleNumericUpDown>(nameof(Format), "0.##", MewPropertyOptions.AffectsLayout);

private static readonly MewPropertyKey<bool> IsSelectedPropertyKey =
    MewProperty<bool>.RegisterReadOnly<MyItem>(nameof(IsSelected), false,
        MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState);

public static readonly MewProperty<bool> IsSelectedProperty = IsSelectedPropertyKey.Property;

public bool IsSelected => GetValue(IsSelectedProperty);

private void SetIsSelected(bool value) => SetValue(IsSelectedPropertyKey, value);
```

### <a id="measure"></a>크기 계산 (MeasureContent)

- `MeasureContent`는 **DesiredSize의 유일한 출처**입니다. `Width`/`Height`, `MinWidth`/`MaxWidth`, `MinHeight`/`MaxHeight`, `Margin`은 프레임워크가 그 바깥에서 적용합니다.
- 이 단계는 “컨트롤이 필요로 하는 공간”만 계산하며, 실제 배치 위치는 다루지 않습니다.
- 텍스트는 `MeasureEngineText(text, maxWidth, wrapping)`로 측정합니다. 이 메서드는 컨트롤의 폰트 속성과 DPI를 사용합니다. 표시 문자열(포맷 반영)을 기준으로 측정합니다.
- `Padding`과 크롬(버튼 영역), `GetBorderVisualInset()`을 포함해 최종 크기를 결정합니다.
- 테마 기본 높이에 맞추려면 컨트롤의 스타일에서 테마 setter로 `MinHeight`를 `Theme.Metrics.BaseControlHeight`로 설정합니다.
- 이 경우 `MeasureContent`는 자연 높이를 반환하고, `MinHeight`가 기준 높이를 보장합니다.
- DesiredSize를 바꾸는 속성은 `AffectsLayout`으로 등록합니다. 그런 속성이 아닌 상태(여기서는 `RangeBase`에서 `AffectsRender`로만 등록된 `Value`)는 `InvalidateMeasure()`를 직접 호출해야 합니다.

예시:
```csharp
protected override Size MeasureContent(Size availableSize)
{
    var textSize = MeasureEngineText(Value.ToString(Format));
    double width = textSize.Width + Padding.HorizontalThickness;
    double height = textSize.Height + Padding.VerticalThickness;
    return new Size(width, height).Inflate(GetBorderVisualInset());
}
```

### <a id="arrange"></a>내부 배치 (ArrangeContent)와 자식 요소

- 이 예시는 `ArrangeContent`를 오버라이드하지 않고, **최종 `Bounds` 기준으로 내부 레이아웃을 계산**합니다.
- 자식이 있는 컨트롤은 `ArrangeContent(Rect bounds)`에서 child rect를 계산하고 자식마다 `Arrange`를 호출해야 합니다.
- Arrange는 “컨트롤이 받은 공간에서 자식을 어디에 둘지”를 확정하는 단계입니다.
- **Measure에서 계산한 DesiredSize**와 **실제 Bounds**가 다를 수 있다는 전제를 반드시 가져야 합니다.
- 자식 요소를 직접 호스팅하는 컨트롤은 네 가지를 합니다. `AttachChild` / `DetachChild`로 자식을 연결하고(`Parent` setter는 공개되어 있지 않습니다), `IVisualTreeHost.VisitChildren`을 구현해 현재 프레임에 참여하는 자식만 **가장 위에 그려지는 자식부터** 방문하게 하고, `MeasureContent` / `ArrangeContent`에서 자식을 측정하고 배치하며, `RenderSubtree`에서 자식을 그립니다. 기본 히트 테스트는 `VisitChildren` 순서대로 자식을 검사해 첫 번째 히트를 사용합니다.
- 논리적으로는 컨트롤에 속하지만 다른 곳에 표시될 수 있는 자식(템플릿이 투영하는 콘텐츠 슬롯)은 `AttachLogicalChild` / `DetachLogicalChild`로 소유하거나 `ChangeLogicalChild(oldChild, newChild)`로 교체합니다. 잘못된 후보는 속성의 `validate` 콜백에서 `ValidateLogicalChild`로 거부합니다.
- 자식을 직접 호스팅하기보다 `ContentControl`, `Panel`, 템플릿을 우선 사용합니다. 위 내용이 이미 구현되어 있습니다.

### <a id="render"></a>렌더링 (OnRender)

- `OnRender(IGraphicsContext context)`만 오버라이드합니다. `Render`는 sealed이며, 자식은 `OnRender` 다음에 실행되는 `RenderSubtree`에서 그립니다.
- `Control.OnRender`는 `Background`와, `BorderBrush`, `BorderThickness`, `CornerRadius`로 정해지는 보더를 그립니다(템플릿이 적용되어 있으면 아무것도 그리지 않습니다). 표준 크롬은 `base.OnRender(context)`를 호출해 그리거나 `DrawBackgroundAndBorder`를 직접 호출해 그립니다.
- `GetSnappedBorderBounds`와 `LayoutRounding.SnapBoundsRectToPixels`로 픽셀 정렬을 보장합니다.
- 렌더 순서: **배경 → 보더 → 콘텐츠**.
- 레이아웃 계산과 렌더 경로가 동일한 기준(rect)을 공유하도록 구조화합니다.
- Render에서는 **측정값을 새로 계산하지 않습니다.** 이미 확정된 Bounds만 사용합니다. `OnRender`에서 `InvalidateMeasure()`를 호출하지 않습니다.
- 텍스트는 `DrawEngineText(context, text, bounds, color, horizontalAlignment, verticalAlignment, wrapping, trimming)`로 그립니다.

**`OnRender`는 매 프레임 실행되지 않습니다.** 프레임워크는 `OnRender`가 그린 내용을 기록해 두고 이후 프레임에서는 그 기록을 재생합니다. `OnRender`는 요소가 무효화된 뒤에만 다시 실행됩니다. 무효화는 `InvalidateVisual()` 호출, `AffectsRender` 또는 `AffectsLayout` 속성의 변경, 크기 변경, 요소의 재배치로 일어납니다.

- `OnRender`가 읽는 값은 모두 변경 시 비주얼을 무효화하는 상태여야 합니다. `AffectsRender`로 등록한 `MewProperty`는 스스로 무효화합니다. private 필드나 외부 데이터(모델, 시계)는 값이 바뀌는 곳에서 `InvalidateVisual()`을 호출해야 합니다. 호출하지 않으면 화면에 이전 그림이 그대로 남습니다.
- 요소가 이동만 한 경우에는 `OnRender`를 호출하지 않고 기록을 새 위치에 재생합니다. 모든 좌표를 `Bounds` 기준으로 계산합니다.
- `OnRender`를 프레임마다 실행되는 틱으로 사용하지 않으며, `OnRender` 안에서 상태를 바꾸지 않습니다. 매 프레임 바뀌는 콘텐츠는 값이 바뀌는 곳에서 매 프레임 `InvalidateVisual()`을 호출합니다. 매 프레임 무효화되는 요소는 기록되지 않고 직접 그려지며, 추가 작업은 필요하지 않습니다.
- `OnRender` 안에서 `Save()` / `Restore()`의 짝을 맞추고, `BeginFrame`, `EndFrame`, `Clear`를 호출하지 않습니다. 그렇게 그린 내용은 기록할 수 없습니다.
- 자신의 `RenderSubtree` 오버라이드에서 자식을 그리는 컨트롤은 자식과 함께 하나의 그리기로 기록됩니다. 따라서 자식 하나가 무효화되면 컨트롤 전체를 다시 그립니다.

### <a id="visualstate"></a>시각 상태와 스타일

- 상태별 모양(호버, 눌림, 포커스, 선택, 비활성)은 `Style`의 `StateTrigger`로 선언합니다. 색 속성을 추가하거나 `OnRender`에서 상태를 검사하는 방식으로 구현하지 **않습니다.** `OnRender`는 `Background`, `BorderBrush`, `Foreground`를 그대로 그리고, 트리거가 그 값을 바꿉니다.
- `Control.ComputeVisualState()`가 `VisualStateFlags`를 만듭니다. `Enabled`(`IsEffectivelyEnabled`), `Hot`(마우스 오버 또는 캡처), `Focused`(활성 창에서 포커스 또는 포커스 포함), `Pressed`(`IsPressed`), `Invalid`(바인딩 검증 오류)입니다. 눌림 상태는 `SetPressed(bool)`로 설정합니다.
- `Selected`, `Checked`, `Active`, `ReadOnly` 같은 컨트롤별 플래그는 `ComputeVisualState()`를 오버라이드해 추가합니다. 오버라이드가 읽는 값은 모두 `AffectsVisualState`로 등록한 속성이어야 하며, 그렇지 않은 값이 바뀔 때는 컨트롤이 `InvalidateVisualState()`를 호출해야 합니다.
- `StateTrigger`는 `Match` 플래그가 모두 있고 `Exclude` 플래그가 모두 없을 때 일치합니다(비활성은 `Match = None, Exclude = Enabled`). 트리거는 선언 순서대로 평가되며 속성마다 마지막으로 일치한 선언이 적용됩니다. `Style.Transitions`는 값 변경을 애니메이션합니다.
- 테마 setter `Setter.Create(property, theme => value)`를 사용하면 스타일 인스턴스 하나가 테마 변경을 따라갑니다.
- 시각 상태가 바뀌는 것만으로는 `OnRender`가 호출되지 않습니다. 다시 그리기는 트리거가 바꾸는 `AffectsRender` 속성에서 일어납니다. `OnRender` 안에서 `CurrentVisualState`를 읽는 컨트롤은 `OnVisualStateChanged`를 오버라이드해 `InvalidateVisual()`을 호출합니다.
- `DefaultStyles`로 등록되는 기본 스타일은 프레임워크 자체 컨트롤 타입에 대한 것입니다. 코어 밖의 컨트롤은 자신의 `StyleSheet`에 타입 규칙을 정의해 기본 모양을 갖습니다. 이 스타일은 가장 가까운 프레임워크 기본 스타일 위에 겹쳐 적용됩니다(`Control` 스타일이 테마의 `CornerRadius`와 `BorderThickness`를 제공합니다). 애플리케이션은 `StyleName`으로 컨트롤의 스타일을 바꿀 수 있습니다.
- 직접 그리는 컨트롤의 **파트** 상태(두 버튼 중 어느 쪽에 마우스가 있는지)에는 플래그가 없습니다. 파트를 템플릿 안의 실제 컨트롤로 구성해 파트가 자체 시각 상태를 갖게 하거나, 샘플처럼 파트 상태를 필드에 두고 값이 바뀔 때마다 `InvalidateVisual()`을 호출합니다.

```csharp
protected override VisualState ComputeVisualState()
{
    var state = base.ComputeVisualState();
    var flags = state.Flags;
    if (IsSelected)
    {
        flags |= VisualStateFlags.Selected;
    }

    return state with { Flags = flags };
}

private static readonly Style _defaultStyle = CreateDefaultStyle();

private static Style CreateDefaultStyle() =>
    new(typeof(MyItem))
    {
        Transitions = [Transition.Create(BackgroundProperty)],
        Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.ControlBackground)],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Hot,
                Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.ButtonHoverBackground)],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Selected,
                Setters = [Setter.Create(BackgroundProperty, theme => theme.Palette.Accent)],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.None,
                Exclude = VisualStateFlags.Enabled,
                Setters =
                [
                    Setter.Create(BackgroundProperty, theme => theme.Palette.DisabledControlBackground),
                    Setter.Create(ForegroundProperty, theme => theme.Palette.DisabledText),
                ],
            },
        ],
    };

public MyItem()
{
    StyleSheet = new StyleSheet();
    StyleSheet.Define<MyItem>(_defaultStyle);
}
```

### <a id="template"></a>템플릿

- `Control.Template`(`ControlTemplate?`)은 컨트롤 자체의 비주얼을 빌드된 요소 트리로 대체합니다. 템플릿이 적용된 동안 `Control.OnRender`는 크롬을 그리지 않으며, 측정, 배치, 렌더링, 히트 테스트는 템플릿 루트로 전달됩니다.
- `DelegateControlTemplate<TControl>`은 빌드 함수 `(owner, context) => root`를 받습니다. 템플릿 객체 하나를 여러 컨트롤에 적용할 수 있으며, 적용할 때마다 독립된 트리를 빌드합니다.
- 빌드 함수에서 `context.Register(name, element)`는 이름 있는 파트를 등록하고, `context.Bind(target, targetProperty, sourceProperty)`는 파트의 속성을 소유 컨트롤의 속성과 동기화하며, `context.BindChrome(target)`은 `Background`, `BorderBrush`, `BorderThickness`, `CornerRadius`를 크롬을 그리는 파트로 전달합니다.
- 템플릿 안의 `ContentPresenter`는 소유 컨트롤의 요소 슬롯을 표시합니다. `ContentSource`를 지정하지 않으면 소유 컨트롤이 `ContentControl`일 때 그 표시 콘텐츠를 보여 주고, 콘텐츠 슬롯이 없는 컨트롤에서는 비어 있습니다. 다른 요소 타입 속성을 보여 주려면 `ContentSource`를 지정합니다.
- 컨트롤은 `OnApplyTemplate()`에서 `GetTemplateChild<T>(name)`으로 파트를 찾습니다. 파트가 없으면 `null`을 반환합니다. 템플릿은 첫 측정 때 빌드되고 `Template`, 테마, DPI가 바뀌면 **다시 빌드**되므로 `OnApplyTemplate`도 그때마다 다시 실행됩니다. 새 파트를 가져오기 전에 이전 파트에서 가져온 것(이벤트 핸들러)을 해제합니다.
- `Template`은 `MewProperty`입니다. 직접 대입할 수도 있고, 스타일에서 `Setter.Create(Control.TemplateProperty, (ControlTemplate?)template)`으로 제공할 수도 있습니다. 기본 제공 `NumericUpDown`은 스타일 setter로 템플릿을 받습니다. 직접 대입한 템플릿은 로컬 값이므로 스타일보다 우선합니다.

```csharp
public sealed class SearchBox : Control
{
    public const string PART_TEXT_BOX = "PART_TextBox";

    private TextBox? _textBox;

    public SearchBox()
    {
        Template = new DelegateControlTemplate<SearchBox>(BuildTemplate);
    }

    private static Element BuildTemplate(SearchBox owner, ControlTemplateContext context)
    {
        var textBox = new TextBox { BorderThickness = 0, Background = Color.Transparent };
        context.Register(PART_TEXT_BOX, textBox);

        // 템플릿이 적용된 컨트롤은 크롬을 직접 그리지 않으므로 파트가 그려야 한다.
        var chrome = new Border { Child = textBox, ClipToBounds = true };
        context.BindChrome(chrome);
        context.Bind(chrome, PaddingProperty);
        return chrome;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_textBox != null)
        {
            _textBox.TextChanged -= OnTextChanged;
        }

        _textBox = GetTemplateChild<TextBox>(PART_TEXT_BOX);
        if (_textBox != null)
        {
            _textBox.TextChanged += OnTextChanged;
        }
    }

    private void OnTextChanged(string text)
    {
    }
}
```

### <a id="state"></a>입력

- `OnMouseDown` / `OnMouseUp` / `OnMouseMove` / `OnMouseWheel` / `OnMouseEnter` / `OnMouseLeave` / `OnKeyDown` / `OnKeyUp`을 오버라이드하고 기반 메서드를 먼저 호출합니다. 마우스와 키 이벤트는 `e.Handled`가 설정될 때까지 대상에서 조상으로 버블링되므로, 컨트롤이 소비한 입력에는 `e.Handled = true`를 설정합니다. `KeyDown`을 처리하면 그 키 입력의 텍스트 입력도 버려집니다.
- `e.GetPosition(this)`는 요소 기준의 포인터 위치를 반환합니다. `Bounds`로 계산한 rect와 비교하려면 `Bounds`의 원점을 더합니다. `MouseWheelEventArgs.Delta`는 `Vector`이며 세로 스크롤은 `Delta.Y`입니다.
- MouseDown에서 캡처하고 MouseUp에서 해제하여 입력 일관성을 보장합니다. `FindVisualRoot()`로 얻은 창의 `Window.CaptureMouse(element)` / `Window.ReleaseMouseCapture()`를 사용합니다. 유효하게 활성화되지 않았거나 숨겨진 요소의 캡처 요청은 무시되며, 캡처를 가진 요소가 트리에서 빠지거나 비활성화되거나 숨겨지면 캡처는 스스로 끝납니다. 이 경우 MouseUp이 오지 않으므로 `IsMouseCaptured`가 false가 될 때 눌림 상태를 정리합니다(`OnMewPropertyChanged`에서 `IsMouseCapturedProperty` 확인).
- 키보드 입력을 받는 컨트롤은 포커스를 받을 수 있어야 합니다. static 생성자에서 `FocusableProperty.OverrideDefaultValue<T>(true)`를 호출하고, 마우스 다운에서 `Focus()`를 호출합니다. `IsTabStop = false`는 파트를 Tab 순서에서만 제외합니다.
- Hit‑test 로직은 **렌더와 동일한 분할 기준**을 사용해야 합니다.
- 입력 처리에서 상태가 바뀌면 **InvalidateVisual**로만 해결되는지, **InvalidateMeasure**가 필요한지 구분합니다.
- 입력 게이팅은 `IsEnabled`가 아니라 **`IsEffectivelyEnabled`**를 기준으로 합니다.
  - 부모 컨트롤이 비활성화되면 자식의 `IsEnabled`가 `true`여도 입력을 받아서는 안 됩니다.
  - 따라서 입력 처리/색상 결정/상태 전이 모두 `IsEffectivelyEnabled`에 맞추는 것이 안전합니다. 변경에는 `OnEnabledChanged()`에서 대응합니다.

### <a id="theme"></a>테마와 메트릭

- 색/크기는 `Theme.Palette.*`, `Theme.Metrics.*`를 사용합니다(`Theme`은 `FrameworkElement`의 protected 속성입니다).
- 스타일에서는 테마 setter로 테마를 읽습니다. 테마 setter는 테마가 바뀌면 다시 해석됩니다. `OnRender`에서는 `Theme`을 직접 읽습니다. 테마가 바뀌면 모든 요소의 측정과 비주얼이 무효화됩니다.
- `OnThemeChanged(Theme oldTheme, Theme newTheme)`는 컨트롤이 직접 캐시한 값이 있을 때만 오버라이드합니다. DPI에 대해서는 `OnDpiChanged(uint oldDpi, uint newDpi)`가 같은 역할을 합니다.
- 빈 `FontFamily`는 테마의 폰트 패밀리를 따릅니다. `Foreground`와 폰트 속성은 조상에서 상속됩니다.

### <a id="utils"></a>유틸리티 메서드 (상태, 보더, 텍스트, DIP)

- `GetDpi()`는 유효 DPI(`uint`)를 반환합니다. DIP→픽셀 변환은 보통 `dpiScale = GetDpi() / 96.0`를 사용합니다.
- `CurrentVisualState`는 컨트롤에 대해 해석된 시각 상태입니다(`IsEnabled`, `IsHot`, `IsFocused`, `IsPressed`, `IsActive`, `IsChecked`, `IsIndeterminate`, `Flags`).
- `PickAccentBorder(theme, baseBorder, state, hoverMix)`, `PickButtonBackground(state, normalBackground)`, `PickControlBackground(state, normalBackground)`는 스타일로 표현할 수 없는 그리기를 위해 시각 상태를 색으로 바꿉니다([시각 상태와 스타일](#visualstate)의 다시 그리기 항목을 참고합니다).
- `DrawBackgroundAndBorder(context, bounds, background, borderBrush, borderThicknessDip, cornerRadiusDip)`는 픽셀 스냅된 배경과 보더를 그립니다. 변마다 다른 두께와 모서리마다 다른 반경을 위해 `Thickness`와 `CornerRadius`를 받는 오버로드가 있습니다.
- `GetBorderVisualInset()`은 정수 디바이스 픽셀로 스냅된 보더 두께를 반환합니다. 측정과 내부 rect 계산에 같이 사용해 레이아웃과 렌더링이 어긋나지 않게 합니다.
- `GetSnappedBorderBounds(bounds)`는 박스를 디바이스 픽셀에 스냅합니다.
- `MeasureEngineText(...)` / `DrawEngineText(...)`는 컨트롤의 폰트 속성으로 텍스트를 측정하고 그립니다. `GetTextRunStyle()`은 그 속성을 텍스트 엔진 형식으로 반환합니다.
- `LayoutRounding`은 fractional DPI에서 흔한 1px 잘림/떨림을 줄이기 위한 유틸입니다.
- `LayoutRounding.SnapBoundsRectToPixels(...)`는 background/border 같은 “박스” 지오메트리에 사용합니다.
- `LayoutRounding.SnapViewportRectToPixels(...)`는 viewport에 사용합니다(줄어들지 않게).
- `LayoutRounding.MakeClipRect(...)`는 `SetClip` / `SetClipRoundedRect`에 넘기는 클립 rect에 사용합니다. 클립은 호출자가 스냅하며, 그래픽 컨텍스트는 스냅하지 않습니다.
- `LayoutRounding.SnapThicknessToPixels(...)`는 보더 두께를 정수 픽셀로 맞출 때 사용합니다.
- `LayoutRounding.RoundToPixel(...)`은 반경이나 오프셋 같은 스칼라 값에 사용합니다.
- `LayoutRounding.ExpandClipByDevicePixels(...)`는 클립이 마지막 1px을 누락하지 않게 확장할 때 사용합니다.

예: 표준 크롬 + 픽셀 스냅된 내부 클립

```csharp
var bounds = GetSnappedBorderBounds(Bounds);
DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, BorderThickness, CornerRadius);

var inner = bounds.Deflate(GetBorderVisualInset());
context.Save();
context.SetClip(LayoutRounding.MakeClipRect(inner, context.DpiScale));
// 콘텐츠를 그린다
context.Restore();
```

### <a id="invalidate"></a>Invalidate 기준

- `Format` 변경: `AffectsLayout`으로 등록했으므로 측정과 비주얼이 자동으로 무효화됩니다.
- `Value` 변경: `RangeBase`에서 `AffectsRender`입니다. 텍스트 폭 변화 가능 → `OnValueChanged`에서 `InvalidateMeasure()`. 요소의 `InvalidateMeasure()`는 그 요소의 비주얼도 무효화합니다.
- 컨트롤 전체의 Hover/Pressed: `IsMouseOver`, `IsPressed` 등 상태 속성은 `AffectsVisualState`이며, 스타일 트리거가 `AffectsRender` 속성을 바꿉니다.
- 필드에 둔 파트의 Hover/Pressed: `InvalidateVisual()`
- 레이아웃과 그리기 어느 쪽에도 영향이 없는 속성(`Step`): `MewPropertyOptions.None`

---

## 전체 샘플 코드

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

public sealed class SimpleNumericUpDown : RangeBase
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

    // 표시 방식은 측정되는 텍스트를 바꾸므로 레이아웃에 영향을 주는 속성이다.
    // 옵션이 측정과 비주얼을 무효화하므로 수동 invalidate가 필요 없다.
    public static readonly MewProperty<string> FormatProperty =
        MewProperty<string>.Register<SimpleNumericUpDown>(nameof(Format), "0.##", MewPropertyOptions.AffectsLayout);

    // 상호작용 단위는 레이아웃과 그리기 어느 쪽에도 영향이 없다.
    public static readonly MewProperty<double> StepProperty =
        MewProperty<double>.Register<SimpleNumericUpDown>(nameof(Step), 1.0);

    // 컨트롤 전체의 모든 상태를 포함한 기본 모양이다. 모든 인스턴스가 공유한다.
    private static readonly Style _defaultStyle = CreateDefaultStyle();

    // 파트 상태에는 시각 상태 플래그가 없다. 필드에 보관하고,
    // OnRender가 이 필드를 읽으므로 값이 바뀔 때마다 InvalidateVisual을 호출한다.
    private ButtonPart _hoverPart;
    private ButtonPart _pressedPart;

    // 타입별 기본값은 static 생성자에서 지정한다. ClearLocalValue 시 이 값으로 복원되고
    // 스타일이 이 값을 덮어쓸 수 있다.
    static SimpleNumericUpDown()
    {
        FocusableProperty.OverrideDefaultValue<SimpleNumericUpDown>(true);
        MaximumProperty.OverrideDefaultValue<SimpleNumericUpDown>(100.0);
    }

    public SimpleNumericUpDown()
    {
        // 컨트롤 자신의 StyleSheet에 정의한 타입 규칙이 기본 스타일이 된다.
        // 여기서 Background/Padding/MinHeight를 대입하지 않는다. 로컬 값은 스타일 setter와 트리거를 막는다.
        StyleSheet = new StyleSheet();
        StyleSheet.Define<SimpleNumericUpDown>(_defaultStyle);
    }

    public string Format
    {
        get => GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    private static Style CreateDefaultStyle() =>
        new(typeof(SimpleNumericUpDown))
        {
            Transitions =
            [
                Transition.Create(BackgroundProperty),
                Transition.Create(BorderBrushProperty),
            ],
            Setters =
            [
                // 테마 setter는 테마가 바뀌면 다시 해석된다.
                Setter.Create(BackgroundProperty, theme => theme.Palette.ControlBackground),
                Setter.Create(BorderBrushProperty, theme => theme.Palette.ControlBorder),
                Setter.Create(PaddingProperty, new Thickness(8, 4, 8, 4)),
                Setter.Create(MinHeightProperty, theme => theme.Metrics.BaseControlHeight),
            ],
            Triggers =
            [
                new StateTrigger
                {
                    Match = VisualStateFlags.Hot,
                    Setters =
                    [
                        Setter.Create(BorderBrushProperty,
                            theme => Color.Composite(theme.Palette.ControlBorder, theme.Palette.AccentBorderHotOverlay)),
                    ],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.Focused,
                    Setters = [Setter.Create(BorderBrushProperty, theme => theme.Palette.Accent)],
                },
                new StateTrigger
                {
                    Match = VisualStateFlags.None,
                    Exclude = VisualStateFlags.Enabled,
                    Setters =
                    [
                        Setter.Create(BackgroundProperty, theme => theme.Palette.DisabledControlBackground),
                        Setter.Create(ForegroundProperty, theme => theme.Palette.DisabledText),
                    ],
                },
            ],
        };

    protected override void OnValueChanged(double value)
    {
        // Value는 AffectsRender뿐이지만 표시 문자열의 폭이 달라질 수 있으므로 Measure를 재요청한다.
        // InvalidateMeasure는 비주얼도 무효화한다.
        InvalidateMeasure();
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);

        // MouseUp 없이 끝난 캡처(비활성화, 숨김, 제거)에서도 눌림을 끝내야 한다.
        if (property == IsMouseCapturedProperty && !IsMouseCaptured)
        {
            ClearPressedPart();
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        // MeasureContent는 커스텀 컨트롤의 원하는 크기를 확정하는 핵심 경로다.
        // DIP 단위로 계산하며, 픽셀 스냅은 Render 단계에서만 처리한다.
        // 실제 표시될 문자열을 컨트롤의 폰트 속성으로 측정한다.
        var textSize = MeasureEngineText(Value.ToString(Format));

        // 콘텐츠 + 패딩 + 크롬의 합으로 폭을 결정한다.
        double width = textSize.Width + Padding.HorizontalThickness + GetButtonAreaWidth();

        // 자연 높이를 사용하고, 스타일의 MinHeight가 기준 높이를 보장한다.
        double height = textSize.Height + Padding.VerticalThickness;

        // 보더 inset까지 포함해 DesiredSize를 확정한다.
        return new Size(width, height).Inflate(GetBorderVisualInset());
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // OnRender는 매 프레임이 아니라 비주얼이 무효화되었을 때 실행된다.
        // 여기서 읽는 값은 모두 AffectsRender/AffectsLayout 속성, 테마,
        // 또는 변경 시 InvalidateVisual을 호출하는 필드다.

        // 배경과 보더. 상태별 색은 스타일 트리거가 이미 해석해 두었다.
        base.OnRender(context);

        // 입력 처리와 렌더링이 동일한 분할 기준을 공유한다.
        GetPartRects(out var inner, out var textRect, out var decRect, out var incRect);

        if (decRect.Width > 0)
        {
            // 버튼 채우기를 둥근 내부 윤곽으로 클립한다. Save/Restore의 짝을 맞춘다.
            double dpiScale = GetDpi() / 96.0;
            double innerRadius = Math.Max(0, LayoutRounding.RoundToPixel(CornerRadius, dpiScale) - GetBorderVisualInset());
            context.Save();
            context.SetClipRoundedRect(LayoutRounding.MakeClipRect(inner, context.DpiScale), innerRadius, innerRadius);
            context.FillRectangle(decRect, GetPartBackground(ButtonPart.Decrement));
            context.FillRectangle(incRect, GetPartBackground(ButtonPart.Increment));
            context.Restore();

            // 시각적 분리를 위해 경계선을 그린다.
            double x = incRect.Left;
            context.DrawLine(new Point(x, incRect.Y + 2), new Point(x, incRect.Bottom - 2), Theme.Palette.ControlBorder, 1);

            x = decRect.Left;
            context.DrawLine(new Point(x, decRect.Y), new Point(x, decRect.Bottom), Theme.Palette.ControlBorder, 1);
        }

        // 텍스트는 마지막에 렌더하여 크롬 위에 올린다. 비활성 상태에서는 Foreground가 이미 비활성 색이다.
        DrawEngineText(context, Value.ToString(Format), textRect, Foreground,
            TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);

        if (decRect.Width > 0)
        {
            // 아이콘/글리프 크기는 테마 메트릭을 따른다.
            double chevronSize = Theme.Metrics.BaseControlHeight / 6;
            Glyph.Draw(context, decRect.Center, chevronSize, Foreground, GlyphKind.ChevronDown);
            Glyph.Draw(context, incRect.Center, chevronSize, Foreground, GlyphKind.ChevronUp);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        // 비활성 상태 입력을 차단한다.
        if (!IsEffectivelyEnabled || e.Delta.Y == 0)
        {
            return;
        }

        // Wheel 입력을 값 변경으로 매핑한다. 값 변경이 스스로 무효화한다.
        CommitValue(Value + (e.Delta.Y > 0 ? Step : -Step));
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        // 입력 시작 지점이다.
        // 커스텀 컨트롤은 “어떤 입력만 처리할지”를 명확히 제한해야 하고,
        // 이 단계에서 포커스/캡처/상태 초기화를 결정한다.
        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        // 키보드 입력을 받을 컨트롤임을 보장한다.
        Focus();

        var part = HitTestButtonPart(e);
        if (part == ButtonPart.None)
        {
            return;
        }

        // MouseUp 수신 보장을 위해 캡처를 사용한다. 창이 캡처를 거부할 수 있다.
        if (FindVisualRoot() is Window window)
        {
            window.CaptureMouse(this);
        }

        if (!IsMouseCaptured)
        {
            return;
        }

        // Hit‑test 결과를 상태로 저장한다. SetPressed는 Pressed 시각 상태에 반영된다.
        _pressedPart = part;
        SetPressed(true);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // Hover 상태를 갱신하여 시각 피드백을 제공한다.
        var part = HitTestButtonPart(e);
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
        if (e.Button != MouseButton.Left || _pressedPart == ButtonPart.None)
        {
            return;
        }

        var pressedPart = _pressedPart;
        var releasedPart = HitTestButtonPart(e);

        // 캡처를 해제하면 OnMewPropertyChanged를 통해 눌림 상태가 정리된다.
        if (FindVisualRoot() is Window window)
        {
            window.ReleaseMouseCapture();
        }

        ClearPressedPart();

        // Down과 Up이 동일 영역일 때만 동작 수행.
        if (releasedPart == pressedPart && IsEffectivelyEnabled)
        {
            CommitValue(Value + (pressedPart == ButtonPart.Increment ? Step : -Step));
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // 키보드 경로는 마우스 경로와 별개이므로 조건을 명확히 한다.
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            CommitValue(Value + Step);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            CommitValue(Value - Step);
            e.Handled = true;
        }
    }

    private void ClearPressedPart()
    {
        if (_pressedPart != ButtonPart.None)
        {
            _pressedPart = ButtonPart.None;
            InvalidateVisual();
        }

        SetPressed(false);
    }

    private Color GetPartBackground(ButtonPart part)
    {
        if (!IsEffectivelyEnabled)
        {
            return Theme.Palette.ButtonDisabledBackground;
        }
        else if (_pressedPart == part)
        {
            return Theme.Palette.ButtonPressedBackground;
        }
        else if (_hoverPart == part)
        {
            return Theme.Palette.ButtonHoverBackground;
        }
        else
        {
            return Theme.Palette.ButtonFace;
        }
    }

    // 크롬 폭 규칙을 중앙화한다.
    private double GetButtonAreaWidth() => Theme.Metrics.BaseControlHeight * 2;

    private void GetPartRects(out Rect inner, out Rect textRect, out Rect decRect, out Rect incRect)
    {
        // Hit‑test와 렌더가 동일한 기준을 공유하도록 분리한다. 모든 rect는 Bounds와 같은 창 좌표다.
        double dpiScale = GetDpi() / 96.0;
        inner = GetSnappedBorderBounds(Bounds).Deflate(GetBorderVisualInset());

        // 콘텐츠 영역과 크롬 영역을 분할하고, 서브 rect도 픽셀 스냅을 적용한다.
        double buttonAreaWidth = Math.Min(GetButtonAreaWidth(), inner.Width);
        var buttonRect = LayoutRounding.SnapBoundsRectToPixels(
            new Rect(inner.Right - buttonAreaWidth, inner.Y, buttonAreaWidth, inner.Height), dpiScale);
        textRect = LayoutRounding.SnapBoundsRectToPixels(
            new Rect(
                inner.X + Padding.Left,
                inner.Y + Padding.Top,
                Math.Max(0, inner.Width - buttonAreaWidth - Padding.HorizontalThickness),
                Math.Max(0, inner.Height - Padding.VerticalThickness)),
            dpiScale);

        decRect = new Rect(buttonRect.X, buttonRect.Y, buttonRect.Width / 2, buttonRect.Height);
        incRect = new Rect(buttonRect.X + buttonRect.Width / 2, buttonRect.Y, buttonRect.Width / 2, buttonRect.Height);
    }

    private ButtonPart HitTestButtonPart(MouseEventArgs e)
    {
        // GetPosition은 요소 기준이고, 파트 rect는 창 좌표다.
        var local = e.GetPosition(this);
        var point = new Point(Bounds.X + local.X, Bounds.Y + local.Y);

        GetPartRects(out _, out _, out var decRect, out var incRect);
        if (decRect.Contains(point))
        {
            return ButtonPart.Decrement;
        }
        else if (incRect.Contains(point))
        {
            return ButtonPart.Increment;
        }
        else
        {
            return ButtonPart.None;
        }
    }
}
```
