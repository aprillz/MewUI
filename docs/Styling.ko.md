# 스타일링

MewUI의 스타일링 시스템은 코드 기반, AOT 친화적인 재사용 가능한 시각적 커스터마이징을 제공합니다.

---

## 1. 개요

MewUI 스타일링 시스템의 설계 원칙:

- **코드 기반**: 스타일은 C# 객체와 타입드 setter — XML이나 CSS 아님
- **AOT 친화**: 리플렉션 없음 — 제네릭 인터페이스와 static 람다
- **선언적**: 상태 기반 시각 효과는 `StateTrigger`로 정의, 이벤트 핸들러 불필요
- **조합 가능**: `BasedOn`으로 스타일 상속, `StyleSheet`로 컨테이너 범위 적용

### 값 해결 우선순위

```
Animation 값 (전환 진행 중)
  ↓ 유효 소스에 애니메이션이 없는 경우
Local 값 (control.Background = ...)
  ↓ 설정되지 않은 경우
ElementTrigger 값
  ↓ 해당 속성을 제공하는 element trigger가 없는 경우
Binding 값
  ↓ 해당 속성을 제공하는 binding이 없는 경우
Style 값 (매칭되는 StateTrigger, 그 다음 base setter)
  ↓ 두 스타일 레이어 모두 해당 속성을 제공하지 않는 경우
상속 값 (부모 체인)
  ↓ 상속되지 않는 경우
기본값
```

### 스타일 해결 우선순위

```
애플리케이션 스타일 (최고 우선순위):
  StyleName 지정   → 가장 가까운 이름 기반 스타일
  StyleName 미지정 → 가장 가까운 타입 기반 규칙
  (두 선택 방식은 상호 배타적)
    ↓ 애플리케이션 스타일이 제공하지 않은 값
컨트롤의 런타임 타입에 가장 가까운 프레임워크 DefaultStyle
    ↓ 두 스타일 레이어 모두 제공하지 않은 값
상속값 또는 속성 기본값
```

프레임워크 기본 스타일과 선택된 애플리케이션 스타일은 같은 `Style` 값 소스 안의 두
레이어를 구성합니다. Setter와 trigger는 아래 레이어부터 위로 누적되므로 애플리케이션
스타일이 속성을 정의하면 이기고, 정의하지 않으면 프레임워크 값을 이어받습니다.
Transition도 같은 우선순위를 따릅니다. 애플리케이션 스타일에서 먼저 찾고, 없을 때
프레임워크 기본 스타일을 조회합니다.

---

## 2. Style

`Style`은 컨트롤 타입에 대한 기본 속성값, 상태별 트리거, 전환 애니메이션을 정의합니다.

### 2.1 기본 스타일

```csharp
var flatButtonStyle = new Style(typeof(Button))
{
    Setters =
    [
        Setter.Create(Control.BackgroundProperty, Color.Transparent),
        Setter.Create(Control.BorderThicknessProperty, 0.0),
    ],
};
```

### 2.2 테마 반응 Setter

Setter에 `Func<Theme, T>`를 사용하면 현재 테마에 따라 동적으로 값이 결정됩니다. 스타일 인스턴스는 한 번만 생성되고 공유됩니다 — 테마 변경 시 재생성 불필요.

```csharp
var accentButton = new Style(typeof(Button))
{
    Setters =
    [
        Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent),
        Setter.Create(TextElement.ForegroundProperty, (Theme t) => t.Palette.AccentText),
        Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.Accent),
    ],
};
```

### 2.3 StateTrigger

트리거는 컨트롤의 시각 상태가 매칭될 때 조건부로 setter를 적용합니다. 같은 속성에 대해 base setter를 override합니다.

```csharp
var accentButton = new Style(typeof(Button))
{
    Setters =
    [
        Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent),
        Setter.Create(TextElement.ForegroundProperty, (Theme t) => t.Palette.AccentText),
    ],
    Triggers =
    [
        new StateTrigger
        {
            Match = VisualStateFlags.Hot,
            Setters = [Setter.Create(Control.BackgroundProperty,
                (Theme t) => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.15))],
        },
        new StateTrigger
        {
            Match = VisualStateFlags.Pressed,
            Setters = [Setter.Create(Control.BackgroundProperty,
                (Theme t) => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.25))],
        },
        new StateTrigger
        {
            Match = VisualStateFlags.None,
            Exclude = VisualStateFlags.Enabled,
            Setters = [
                Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.ButtonDisabledBackground),
                Setter.Create(TextElement.ForegroundProperty, (Theme t) => t.Palette.DisabledText),
            ],
        },
    ],
};
```

사용 가능한 플래그: `Enabled`, `Hot`, `Focused`, `Pressed`, `Checked`, `Indeterminate`, `Active`, `Selected`, `ReadOnly`.

### 2.4 Transition

Transition은 상태 간 속성 변경을 애니메이션합니다 (예: hover 색상 전환).

```csharp
var style = new Style(typeof(Button))
{
    Transitions =
    [
        Transition.Create(Control.BackgroundProperty),
        Transition.Create(Control.BorderBrushProperty),
        Transition.Create(TextElement.ForegroundProperty),
    ],
    Setters = [...],
    Triggers = [...],
};
```

### 2.5 BasedOn

스타일은 다른 스타일을 상속할 수 있습니다. 파생 스타일의 setter/trigger가 같은 속성에 대해 base를 override합니다.

```csharp
// 재사용 가능한 애플리케이션 스타일 상속
var myButton = new Style(typeof(Button))
{
    BasedOn = sharedButtonStyle,
    Setters =
    [
        // 필요한 것만 override — 나머지는 sharedButtonStyle에서 상속
        Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent),
    ],
};
```

`BasedOn`은 한 레이어 안에서 스타일을 합성합니다. 이와 별개로 일반 이름 스타일이나
타입 규칙 스타일은 컨트롤의 런타임 타입에 가장 가까운 프레임워크 기본 스타일 위에
자동으로 쌓입니다. `BasedOn = Style.ForType<T>()`도 계속 지원하지만 보통은 불필요합니다.
런타임 레이어가 선택한 것과 같은 기본 스타일을 지정해도 한 번만 적용됩니다.

### 2.6 프레임워크 기본 스타일 완전 교체

애플리케이션 스타일이 완전한 룩을 제공하고 프레임워크의 기본 setter, trigger,
transition을 하나도 이어받지 않아야 한다면 `OverridesDefaultStyle = true`를 지정합니다.
이 스타일 자체의 `BasedOn` 체인은 그대로 적용됩니다.

```csharp
var looklessButton = new Style(typeof(Button))
{
    OverridesDefaultStyle = true,
    Setters =
    [
        Setter.Create(Control.TemplateProperty, (ControlTemplate?)myButtonTemplate),
    ],
};
```

이 옵션은 컨트롤이 아니라 `Style`에 있습니다. 따라서 `StyleName`, 타입 규칙, 프레임워크
코드 중 어떤 경로로 스타일을 선택해도 동일하게 동작합니다. 기본 템플릿이 시각 요소를
제공하는 컨트롤(예: `NumericUpDown`, `DropDownButton`, `SplitButton`)을 완전히 교체할 때는
새 스타일이 `Template`을 제공해야 하며, 그렇지 않으면 룩이 없는 컨트롤이 됩니다.

### 2.7 Unset (스타일 값 되돌리기)

스타일 레이어링은 가산적입니다. 위 스타일은 보통 아래 값을 override하지만 제거하지는 않습니다. `Setter.Unset(property)`가 그 빈틈을 메웁니다. 선언된 지점에서 현재 Style 티어 후보를 제거하여, 어떤 스타일 레이어도 해당 속성을 설정하지 않은 것처럼 상속값(상속이 없으면 타입 기본값)으로 되돌립니다. CSS `unset`과 같은 의미입니다.

```csharp
// base의 chrome(배경, 테두리 등)는 유지하되 폰트만 앰비언트/상속값을 따르게 함
var menuDropDown = new Style(typeof(ContextMenu))
{
    Setters =
    [
        Setter.Unset(TextElement.FontFamilyProperty),
        Setter.Unset(TextElement.FontSizeProperty),
        Setter.Unset(TextElement.ForegroundProperty),
    ],
};
```

스코프:

- 해당 속성에 대해 아래 프레임워크 기본 레이어에서 이어받은 값까지 포함한 **Style 티어 전체**를 비웁니다. 더 높은 지속 소스(`Local`, `ElementTrigger`, `Binding`)는 건드리지 않습니다. 뒤에서 매칭되는 애플리케이션 trigger는 해당 속성을 다시 설정할 수 있습니다.
- 상속 속성(`Foreground`, `Font*`)이면 조상에서 상속된 값으로 복귀하고, 조상에 값이 없으면 타입 기본값(`OverrideDefaultValue` 포함)으로 복귀합니다.
- 중첩 `BasedOn` 체인에서는 더 파생된 레벨의 Unset이 아래 base setter를 이기고, 더더욱 파생된 레벨은 그 속성을 다시 set할 수 있습니다.
- `Unset`은 base setter와 trigger setter 모두에 사용할 수 있습니다. Trigger 안에서는 해당 trigger가 매칭되는 동안만 적용되며, 뒤의 활성 선언이 속성을 다시 설정할 수 있습니다.

---

## 3. StyleSheet

`StyleSheet`는 이름 기반 스타일과 타입 기반 스타일 규칙을 모두 지원하는 스타일 레지스트리입니다. 모든 `FrameworkElement`(일반적으로 `Window`)에 연결할 수 있습니다.

1. **이름 기반 스타일**: `StyleName`이 설정된 컨트롤은 부모 체인에서 가장 가까운 `StyleSheet`에서 이름으로 조회합니다.
2. **타입 기반 규칙**: 명시적 `StyleName` 없이 타입으로 자동 매칭합니다.

### 3.1 이름 기반 스타일

```csharp
// Window에 정의 (이름 기반 스타일은 팩토리를 받아 최초 조회 시 생성)
window.StyleSheet = new StyleSheet();
window.StyleSheet.Define("accent-button", () => accentButton);
window.StyleSheet.Define("flat-button", () => flatButtonStyle);

// 컨트롤에 적용
var btn = new Button { StyleName = "accent-button" };
btn.Content("Save");
```

`StyleName`이 설정되면 자기 자신부터 부모 체인을 올라가며 각 `FrameworkElement`의 `StyleSheet`에서 해당 이름을 조회합니다. 부모 체인에서 찾지 못하면 `Application.StyleSheet`를 마지막으로 조회합니다. 그래도 이름을 찾지 못했을 때 타입 규칙으로 fallback하지는 않습니다. 컨트롤이 연결되어 모든 scope가 확정된 뒤에는 찾지 못한 이름과 조회한 scope를 포함한 오류가 발생합니다.

### 3.2 타입 기반 규칙

```csharp
var toolbar = new StackPanel().Horizontal().Spacing(4);
toolbar.StyleSheet = new StyleSheet();
toolbar.StyleSheet.Define<Button>(flatButtonStyle);

// toolbar 내의 모든 Button에 flatButtonStyle 자동 적용
toolbar.Add(new Button().Content("Cut"));
toolbar.Add(new Button().Content("Copy"));
toolbar.Add(new Button().Content("Paste"));
toolbar.Add(new CheckBox().Content("Bold")); // 영향 없음 — Button만 매칭 대상
```

타입 매칭은 정확한 타입을 먼저, 그 다음 부모 타입을 매칭합니다. `Define<Button>(style)`은 `Button`과 그 하위 클래스에 적용됩니다.

타입 규칙은 `StyleName`이 지정되지 않았을 때만 조회합니다. 따라서 이름 스타일과 타입
규칙은 암시적으로 병합되지 않습니다. 이름 스타일이 다른 애플리케이션 스타일을
의도적으로 확장해야 한다면 `BasedOn`을 사용합니다.

### 3.3 중첩 StyleSheet

내부 StyleSheet가 같은 타입에 대해 외부를 override합니다. 다른 타입은 독립적으로 버블링됩니다.

```csharp
// 외부: 모든 Button을 flat으로
outerPanel.StyleSheet = new StyleSheet();
outerPanel.StyleSheet.Define<Button>(flatButtonStyle);

// 내부: 여기서는 Button을 accent로
innerPanel.StyleSheet = new StyleSheet();
innerPanel.StyleSheet.Define<Button>(accentButtonStyle);

// 결과:
// outerPanel > Button → flat
// innerPanel > Button → accent
// outerPanel > CheckBox → 영향 없음 (타입 규칙 없음)
```

---

## 4. 속성 값 소스

각 속성 값에는 우선순위를 결정하는 소스가 있습니다:

| 소스 | 우선순위 | 설명 |
|------|----------|------|
| `Local` | 최고 | 요소에 직접 설정 (예: `button.Background = Color.Red`) |
| `ElementTrigger` | 더 높음 | 요소에 직접 선언된 trigger가 설정 |
| `Binding` | 높음 | binding이 제공하는 현재 값 |
| `Style` | 중간 | 애플리케이션/기본 setter와 매칭되는 `StateTrigger`의 최종 후보 |
| `Inherited` | 낮음 | 부모에서 상속 (예: `Window`의 `Foreground`) |
| `Default` | 최저 | 속성의 기본값 |

Animation은 현재 유효한 소스 위에 값을 일시적으로 표시합니다. 따라서 표 안의 지속적인
후보 소스와는 별도의 overlay입니다.

### Local 값과 트리거

속성에 `Local` 값이 있으면 element trigger, binding, 스타일 후보는 유지되지만 가려집니다. Local 값을 지우면 다음 후보가 다시 드러납니다.

```csharp
var btn = new Button().Content("빨간 버튼");
btn.Background = Color.Red; // Local 값 — hover 트리거가 변경하지 않음
```

### Foreground와 폰트 상속

`Foreground`, `FontFamily`, `FontSize`, `FontWeight`는 텍스트를 갖는 요소의 기반 클래스인 `TextElement`(`Control`의 상위)에 `Inherits` 플래그로 선언됩니다. `Window` 기본 스타일이 이들을 설정하고, 모든 자손이 트리를 따라 상속받습니다. 개별 컨트롤은 기본 스타일에서 이들을 설정하지 않습니다. Button, TextBox 등 특정 컨트롤의 disabled 트리거만 `Foreground`를 `DisabledText`로 override합니다.

---

## 5. 테마 통합

스타일은 `Func<Theme, T>` setter를 사용하여 테마 변경에 자동으로 반응합니다:

```csharp
// 이 스타일은 Light/Dark 모두에서 재생성 없이 동작
Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.ButtonFace)
```

테마 변경 시:
1. 각 컨트롤에서 `ResolveAndApplyStyle()` 재실행
2. 같은 `Style` 인스턴스 재사용 (스타일은 static/공유)
3. `ResolveValue(newTheme)`이 새 팔레트에서 색상 생성
4. Transition이 색상 변경을 부드럽게 애니메이션

### Style.ForType

스타일이 전역 공유(테마별 아님)이므로 정적으로 참조 가능:

```csharp
// Theme 인스턴스 불필요
var baseStyle = Style.ForType<Button>();
```

실제 기본 스타일 객체를 명시적으로 합성하거나 조사할 때 이 API를 사용합니다. 일반적인
부분 애플리케이션 스타일은 런타임 기본 스타일을 자동으로 이어받습니다.

## 6. 교체 의미론에서 마이그레이션

이제 이름 스타일과 타입 규칙은 지정하지 않은 프레임워크 기본값을 보존합니다. `Control`
자체에서 새로 이어받는 범위는 현재 `CornerRadius`와 `BorderThickness`뿐이지만, 더 풍부한
컨트롤 기본 스타일에서는 템플릿, padding, 색상, trigger, transition도 이어받을 수
있습니다. 기존 스타일이 이 값을 전부 지우려는 목적이었다면
`OverridesDefaultStyle = true`를 추가하고, 기본 템플릿 컨트롤의 `Template`을 비롯해 필요한
값을 모두 제공해야 합니다. DEBUG 빌드의 속성 inspector는 스타일 후보를
`Framework default`와 `Application`으로 표시하고, 이번 cascade로 새로 상속된 프레임워크
값도 표시합니다.

---

## 7. 전체 예제

```csharp
// 스타일 정의 (static, 공유, 테마 반응)
var flatButton = new Style(typeof(Button))
{
    Setters =
    [
        Setter.Create(Control.BackgroundProperty,
            (Theme t) => t.Palette.ButtonHoverBackground.WithAlpha(0)),
        Setter.Create(Control.BorderBrushProperty, Color.Transparent),
        Setter.Create(Control.BorderThicknessProperty, 0.0),
    ],
    Triggers =
    [
        new StateTrigger
        {
            Match = VisualStateFlags.Hot,
            Setters = [Setter.Create(Control.BackgroundProperty,
                (Theme t) => t.Palette.ButtonHoverBackground)],
        },
    ],
};

var accentButton = new Style(typeof(Button))
{
    Setters =
    [
        Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent),
        Setter.Create(TextElement.ForegroundProperty, (Theme t) => t.Palette.AccentText),
        Setter.Create(Control.BorderBrushProperty, (Theme t) => t.Palette.Accent),
    ],
    Triggers =
    [
        new StateTrigger
        {
            Match = VisualStateFlags.Hot,
            Setters = [
                Setter.Create(Control.BackgroundProperty,
                    (Theme t) => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.15)),
            ],
        },
        new StateTrigger
        {
            Match = VisualStateFlags.Pressed,
            Setters = [
                Setter.Create(Control.BackgroundProperty,
                    (Theme t) => t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.25)),
            ],
        },
    ],
};

// StyleSheet에 등록
window.StyleSheet = new StyleSheet();
window.StyleSheet.Define("accent", () => accentButton);

// StyleSheet 타입 규칙으로 적용 (컨테이너 범위)
var toolbar = new StackPanel().Horizontal().Spacing(4);
toolbar.StyleSheet = new StyleSheet();
toolbar.StyleSheet.Define<Button>(flatButton);
toolbar.Add(new Button().Content("Cut"));
toolbar.Add(new Button().Content("Copy"));

// StyleName으로 적용 (개별 요소)
var saveBtn = new Button { StyleName = "accent" };
saveBtn.Content("Save");
toolbar.Add(saveBtn);

// Local override — 모든 스타일 트리거 무시
var customBtn = new Button().Content("Custom");
customBtn.Background = Color.FromRgb(200, 60, 60);
toolbar.Add(customBtn);
```
