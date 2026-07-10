# 컨트롤 템플릿 (ControlTemplate)

## 개요

- `ControlTemplate`은 **컨트롤의 시각 트리를 교체 가능한 정의(definition)로 분리**하는 장치입니다.
- 컨트롤은 public 속성(logical 상태: `Content`, `Header` 등)을 유지한 채, 실제로 그려지고
  배치되는 트리(visual)를 템플릿이 소유합니다.
- **템플릿은 opt-in입니다.** `Template`이 null이면(기본값) 컨트롤은 기존의 자체 렌더링
  경로를 그대로 사용하며, 성능 특성도 변하지 않습니다. 템플릿을 설정한 컨트롤만
  템플릿 경로로 동작합니다.

```csharp
// 정의: 시각 트리의 청사진. 여러 컨트롤 인스턴스에 재사용 가능.
var template = new DelegateControlTemplate<ContentControl>((owner, ctx) =>
{
    var presenter = new ContentPresenter();
    var chrome = new Border { Child = presenter };
    ctx.Bind(chrome, Control.BackgroundProperty, Control.BackgroundProperty);
    return chrome;                       // 반환값이 visual root
});

host.Template = template;                // 적용. 빌드는 다음 measure에서 lazy로 일어남
```

---

## 상세 설명

### <a id="model"></a>정의와 인스턴스

- `ControlTemplate`(정의)은 상태를 갖지 않는 청사진입니다. 하나의 정의를 컨트롤 100개에
  적용하면 `Build`가 100번 호출되어 **서로 독립된 트리 100벌**이 만들어집니다.
- 빌드 산출물(visual root, 파트 명부)은 컨트롤 인스턴스가 소유합니다. 정의 객체에
  요소를 저장하면 여러 컨트롤이 한 요소를 공유하는 사고가 나므로 금지합니다.
- 예외적으로, 창 하나에만 쓰는 템플릿이라면 필드 요소를 델리게이트로 캡처하는
  **인스턴스 전용 템플릿** 패턴을 쓸 수 있습니다(아래 커스텀 chrome 예 참고).
  이 경우 그 정의는 다른 컨트롤에 재사용할 수 없습니다.

### <a id="lifecycle"></a>적용 수명주기

```text
host.Template = template
  -> 즉시 빌드하지 않음 (measure 무효화만)

다음 measure
  -> ApplyTemplate(): Build(owner, ctx) -> root를 visual child로 attach
     -> ContentPresenter 배선 -> OnApplyTemplate()

host.Template = other (교체)
  -> 이전 트리 즉시 detach (내부 포커스는 안전하게 해제됨)
  -> 다음 measure에서 새 템플릿 빌드
```

- 빌드가 measure 시점인 이유: 속성 설정 시점에는 컨트롤이 아직 트리에 붙기 전일 수
  있어 테마, DPI, 스타일 해석 컨텍스트가 없습니다.
- 템플릿이 적용되면 measure, arrange, render, hit test, 트리 순회가 전부
  **템플릿 루트 하나**를 통해 흐릅니다. 컨트롤의 기존 자체 구현은 실행되지 않습니다.

### <a id="parts"></a>이름 있는 파트

빌드 중 `ctx.Register(name, element)`로 파트에 이름을 붙이고, 컨트롤 구현부는
`OnApplyTemplate`에서 캐시합니다.

```csharp
public class Badge : Control
{
    private TextBlock? _label;

    protected override void OnApplyTemplate()
    {
        _label = GetTemplateChild<TextBlock>("Label");   // 템플릿에 없으면 null
    }
}
```

- `ctx.Get<T>(name)`은 템플릿 저자용(없으면 예외), `GetTemplateChild<T>`는 컨트롤용
  (없으면 null - 템플릿이 그 파트를 생략하는 것을 허용)입니다.
- 같은 이름을 두 번 등록하면 빌드 시점에 예외가 납니다.

### <a id="presenter"></a>ContentPresenter: logical 슬롯의 투영

`ContentPresenter`는 템플릿 트리 안에서 "여기에 컨트롤의 콘텐츠가 나타난다"를
표시하는 자리입니다.

```csharp
new DelegateControlTemplate<ContentControl>((owner, ctx) =>
    new Border { Child = new ContentPresenter() });
```

- 컨트롤의 `Content`는 계속 **컨트롤의 logical 소유**로 남고, presenter가 그 요소의
  visual 위치를 제공합니다. 즉 `host.Content`는 템플릿과 무관하게 항상 진짜 사용자
  콘텐츠를 돌려줍니다.
- **템플릿에 presenter가 없으면 콘텐츠는 화면에 나타나지 않습니다.** 프레임워크는
  콘텐츠를 임의 위치로 대신 붙여주지 않습니다. 이때도 logical 소유는 유지되므로
  presenter가 있는 템플릿으로 교체하면 다시 나타납니다.

#### 슬롯 매핑 메커니즘

presenter와 컨트롤 속성의 연결은 다음 순서로 일어납니다.

```text
1. 템플릿 빌드가 반환한 트리를 컨트롤에 attach
2. 프레임워크가 그 서브트리를 순회하며 아직 배선되지 않은 모든 ContentPresenter를
   찾아 주인 컨트롤에 연결 (이름 매칭이 아니라 "이 빌드에서 만들어진 presenter" 기준)
3. 각 presenter가 자기 ContentSource가 가리키는 주인 속성 값을 읽어
   visual child로 attach (pull 방식)
```

`ContentSource`는 문자열 이름이 아니라 **속성 디스크립터(`MewProperty<Element?>`)
참조**입니다. 어느 슬롯을 투영할지 형식적으로 지정합니다.

```csharp
var headerPresenter  = new ContentPresenter { ContentSource = HeaderedContentControl.HeaderProperty };
var contentPresenter = new ContentPresenter();   // 기본값: ContentControl.ContentProperty
```

이후 주인 컨트롤의 슬롯 속성이 바뀌면(`host.Content = other` 등), 컨트롤이 해당
속성을 `ContentSource`로 갖는 presenter만 골라 재투영합니다. Header 변경이 Content
presenter를 건드리지 않습니다.

- 슬롯이 다른 presenter 여러 개는 정상 조합입니다(Header + Content 등).
  **같은 슬롯을 두 presenter가 가리키는 구성은 지원하지 않습니다** - 요소는 한 곳에만
  존재할 수 있습니다.
- 투영된 콘텐츠의 정렬은 MewUI의 일반 규칙을 따릅니다: 콘텐츠 요소 자신의
  `HorizontalAlignment`/`VerticalAlignment`가 presenter 슬롯 안에서 적용되고,
  템플릿 저자가 정렬을 지시하려면 presenter 자체에 alignment를 지정합니다.

### <a id="template-binding"></a>템플릿 바인딩 (ctx.Bind)

템플릿 파트는 대개 주인 컨트롤의 속성을 **시각적으로 반영**해야 합니다. `ctx.Bind`가
그 연결입니다: 파트의 속성이 주인 컨트롤의 속성을 따라가는 단방향 바인딩으로,
WPF의 `TemplateBinding`에 대응하는 개념입니다.

```csharp
ctx.Bind(part, targetProperty, sourceProperty);   // 소스는 항상 템플릿의 주인 컨트롤
```

- 연결 시점에 현재 값을 즉시 적용하고, 이후 변경을 계속 전달합니다.
- 단방향(주인 -> 파트)이며 값 변환은 없습니다. 파트가 주인 컨트롤이 노출한 상태의
  반영이라는 의미론이므로, 역방향이 필요한 파트(예: 편집 텍스트)는 컨트롤 코드가
  `OnApplyTemplate`에서 파트 이벤트를 구독하는 쪽이 맞습니다.
- 템플릿 해체 시 자동 해제됩니다.
- 스타일 트리거와 트랜지션도 그대로 동작합니다. 트리거는 주인 컨트롤의 속성에 쓰고,
  바인딩이 매 애니메이션 틱을 파트로 전달하므로 색 전환 등이 끊기지 않습니다.

전형적인 사용처가 chrome입니다. 템플릿이 적용된 컨트롤은 **기본 Background/Border를
그리지 않으므로**(시각 전체를 템플릿이 소유해야 재템플릿이 성립), 배경과 테두리는
템플릿 안의 파트가 담당하고 컨트롤 속성에 바인딩합니다:

```csharp
_pathHost.Template = new DelegateControlTemplate<ContentControl>((host, ctx) =>
{
    var chrome = new Border { Child = /* ... */ };
    ctx.Bind(chrome, Control.BackgroundProperty,      Control.BackgroundProperty);
    ctx.Bind(chrome, Control.BorderBrushProperty,     Control.BorderBrushProperty);
    ctx.Bind(chrome, Control.BorderThicknessProperty, Control.BorderThicknessProperty);
    ctx.Bind(chrome, Control.CornerRadiusProperty,    Control.CornerRadiusProperty);
    return chrome;
});
```

### <a id="state-parts"></a>상태 파트 패턴: 여러 표현 상태의 전환

상태에 따라 다른 트리를 보여야 하는 컨트롤은 **콘텐츠 교체가 아니라 템플릿 파트의
가시성 토글**로 표현합니다. 파일 다이얼로그 주소창(브레드크럼 <-> 경로 입력)이 실례입니다.

```csharp
// 두 상태가 템플릿 파트로 상주하고, 상태 플래그가 가시성만 토글한다.
_pathHost.Template = new DelegateControlTemplate<ContentControl>((host, ctx) =>
{
    var chrome = new Border
    {
        Child = new Grid().Children(_breadcrumb.CenterVertical(), _pathBox),
    };
    // ...chrome 바인딩 생략...
    return chrome;
});

// 진입: _breadcrumb.IsVisible = false; _pathBox.IsVisible = true; _pathBox.Focus();
// 이탈: 역토글 + 포커스 이동 (숨김은 포커스를 해제하지 않으므로 명시적으로 옮긴다)
```

이 패턴의 이점:

- 전환이 detach를 유발하지 않아 파트 상태(텍스트, 캐럿 등)가 왕복 간 보존됩니다.
- 표현 상태(기계류)가 `Content` 슬롯을 점거하지 않습니다. `Content`는 사용자
  콘텐츠의 자리이지 컨트롤 자신의 표현을 담는 곳이 아닙니다.

주의: 숨김(`IsVisible = false`)은 포커스를 해제하지 않습니다. 포커스를 가진 파트를
숨길 때는 반드시 포커스를 명시적으로 옮기거나 해제해야 합니다.

### <a id="window"></a>Window 템플릿: 커스텀 chrome

`Window`도 ContentControl이므로 같은 방식으로 템플릿을 갖습니다. 자체 타이틀바를
그리는 창이 대표 사례입니다: chrome(타이틀바, 테두리)은 템플릿이 소유하고, 앱
콘텐츠는 `ContentPresenter` 자리로 투영됩니다.

```csharp
public class ChromeWindow : Window
{
    public ChromeWindow()
    {
        Template = new DelegateControlTemplate<ChromeWindow>((window, ctx) =>
        {
            var titleText = new TextBlock().CenterVertical().Margin(8, 0);
            titleText.SetBinding(TextBlock.TextProperty, window, TitleProperty);

            var titleBar = new Border { MinHeight = 32, Child = titleText };
            titleBar.OnMouseDown(e =>
            {
                if (e.Button == MouseButton.Left)
                {
                    window.DragMove();
                    e.Handled = true;
                }
            });

            var chrome = new Border
            {
                BorderThickness = 1,
                Child = new DockPanel().Children(
                    titleBar.DockTop(),
                    new ContentPresenter()),
            };
            ctx.Bind(chrome, Control.BorderBrushProperty, Control.BorderBrushProperty);
            return chrome;
        });
    }
}

// 사용하는 쪽은 일반 창과 똑같다: Content는 chrome과 무관하게 앱 콘텐츠의 자리.
var window = new ChromeWindow { Content = appRoot };
```

- `Window.Content`는 어떤 chrome 아래서든 **항상 앱 콘텐츠**입니다. chrome 트리를
  Content에 밀어 넣고 진짜 콘텐츠를 다른 속성으로 우회시키는 구조가 필요 없습니다.
- 최소화/최대화 버튼처럼 창 코드가 레이아웃 전에 만져야 하는 파트가 있다면, 파트를
  생성자에서 필드로 만들고 델리게이트가 캡처하는 인스턴스 전용 템플릿 패턴을
  씁니다("정의와 인스턴스" 절 참고).
- 템플릿 없는 창(기본)은 네이티브 프레임 그대로입니다. WPF와 같은 구도입니다:
  표준 창은 OS chrome, 커스텀 chrome 창만 템플릿이 시각을 소유합니다.

### <a id="logical-visual"></a>logical과 visual: 무엇이 어디에 남는가

| 관계 | 템플릿 없음 | 템플릿 적용 |
|---|---|---|
| `content.LogicalParent` | 컨트롤 | 컨트롤 (불변) |
| `content.Parent` (visual) | 컨트롤 | 템플릿 안의 `ContentPresenter` |
| chrome (배경, 테두리) | 컨트롤이 직접 그림 | 템플릿 파트가 담당 |

- 개발자 도구(`Ctrl/Cmd+Shift+T`)의 Logical Tree 모드는 사용자 소유 구조만 보여주고,
  visual 모드는 logical 소유가 없는 요소(템플릿 파트, presenter 등 기계류)를
  `[TypeName]`으로 표기합니다.

### <a id="pitfalls"></a>주의 사항

- **정의에 요소를 저장하지 마십시오.** `Build`가 매번 새 트리를 만들어야 합니다.
  (인스턴스 전용 템플릿은 예외 패턴이며 재사용 불가를 감수하는 선택입니다.)
- **기계류를 `Content`에 넣지 마십시오.** 컨트롤 자신의 표현(chrome, 상태별 트리)은
  템플릿 소유입니다. `Content`는 밖에서 주입되는 사용자 콘텐츠의 자리입니다.
- **chrome 억제를 기억하십시오.** 템플릿을 붙였는데 배경/테두리가 사라졌다면, 그
  시각을 템플릿 파트로 옮기고 `ctx.Bind`로 연결해야 한다는 신호입니다.
- **파트 조회는 `OnApplyTemplate` 이후에만 유효합니다.** 빌드는 lazy이므로 생성자
  시점에는 파트가 없습니다.
- **visual root는 하나입니다.** 템플릿 밖의 요소를 별도 경로로 그리거나 순회하는
  구현은 만들지 마십시오.
