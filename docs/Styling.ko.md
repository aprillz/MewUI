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
Local 값 (control.Background = ...)
  ↓ 설정되지 않은 경우
Animation 값 (전환 진행 중)
  ↓ 애니메이션 없는 경우
Trigger 값 (StateTrigger 매칭)
  ↓ 매칭되는 트리거 없는 경우
Style base setter
  ↓ 스타일 setter 없는 경우
상속 값 (부모 체인)
  ↓ 상속되지 않는 경우
기본값
```

### 스타일 해결 우선순위

```
StyleName 지정 시:
  자신부터 부모 체인의 StyleSheet에서 이름 조회
    → Application.StyleSheet 조회
      → 못 찾으면 아래로 fallback

StyleName 미지정 또는 이름 못 찾은 경우:
  부모 체인의 StyleSheet에서 타입 기반 조회
    → 못 찾으면 아래로 fallback

DefaultStyles (Theme 기본 스타일)       (최저 우선순위)
```

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
        Setter.Create(Control.ForegroundProperty, (Theme t) => t.Palette.AccentText),
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
        Setter.Create(Control.ForegroundProperty, (Theme t) => t.Palette.AccentText),
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
                Setter.Create(Control.ForegroundProperty, (Theme t) => t.Palette.DisabledText),
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
        Transition.Create(Control.ForegroundProperty),
    ],
    Setters = [...],
    Triggers = [...],
};
```

### 2.5 BasedOn

스타일은 다른 스타일을 상속할 수 있습니다. 파생 스타일의 setter/trigger가 같은 속성에 대해 base를 override합니다.

```csharp
// 기본 Button 테마 스타일 상속
var myButton = new Style(typeof(Button))
{
    BasedOn = Style.ForType<Button>(),
    Setters =
    [
        // 필요한 것만 override — 나머지는 BasedOn에서 상속
        Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent),
    ],
};
```

`Style.ForType<T>()`는 Theme 인스턴스 없이 기본 테마 스타일을 반환합니다.

> **방침**: `BasedOn`을 설정하지 않으면 이 스타일에 정의된 setter/trigger만 적용됩니다. 프레임워크가 테마 스타일과 자동 병합하지 않습니다. WPF와 동일한 동작이며 스타일링을 예측 가능하게 유지합니다.

---

## 3. StyleSheet

`StyleSheet`는 이름 기반 스타일과 타입 기반 스타일 규칙을 모두 지원하는 스타일 레지스트리입니다. 모든 `FrameworkElement`(일반적으로 `Window`)에 연결할 수 있습니다.

1. **이름 기반 스타일**: `StyleName`이 설정된 컨트롤은 부모 체인에서 가장 가까운 `StyleSheet`에서 이름으로 조회합니다.
2. **타입 기반 규칙**: 명시적 `StyleName` 없이 타입으로 자동 매칭합니다.

### 3.1 이름 기반 스타일

```csharp
// Window에 정의
window.StyleSheet = new StyleSheet();
window.StyleSheet.Define("accent-button", accentButton);
window.StyleSheet.Define("flat-button", flatButtonStyle);

// 컨트롤에 적용
var btn = new Button { StyleName = "accent-button" };
btn.Content("Save");
```

`StyleName`이 설정되면 자기 자신부터 부모 체인을 올라가며 각 `FrameworkElement`의 `StyleSheet`에서 해당 이름을 조회합니다. 부모 체인에서 찾지 못하면 `Application.StyleSheet`를 마지막으로 조회합니다. 그래도 없으면 타입 기반 규칙 → Theme 기본 스타일(`DefaultStyles`) 순서로 fallback합니다.

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

## 5. 속성 값 소스

각 속성 값에는 우선순위를 결정하는 소스가 있습니다:

| 소스 | 우선순위 | 설명 |
|------|----------|------|
| `Local` | 최고 | 컨트롤에 직접 설정 (예: `button.Background = Color.Red`) |
| `Trigger` | 높음 | 매칭되는 `StateTrigger`에 의해 설정 |
| `Style` | 중간 | `Style` base setter에 의해 설정 |
| `Inherited` | 낮음 | 부모에서 상속 (예: `Window`의 `Foreground`) |
| `Default` | 최저 | 속성의 기본값 |

### Local 값과 트리거

속성에 `Local` 값이 있으면 트리거와 스타일 setter가 해당 속성에 대해 무시됩니다. WPF와 동일한 동작입니다.

```csharp
var btn = new Button().Content("빨간 버튼");
btn.Background = Color.Red; // Local 값 — hover 트리거가 변경하지 않음
```

### Foreground 상속

`Foreground`는 `Window`에 설정되어 모든 자손이 상속받습니다. 개별 컨트롤은 기본 스타일에서 `Foreground`를 설정하지 않습니다. Button, TextBox 등 특정 컨트롤의 disabled 트리거만 `DisabledText`로 override합니다.

---

## 6. 테마 통합

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

---

## 7. 전체 예제

```csharp
// 스타일 정의 (static, 공유, 테마 반응)
var flatButton = new Style(typeof(Button))
{
    BasedOn = Style.ForType<Button>(),
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
    BasedOn = Style.ForType<Button>(),
    Setters =
    [
        Setter.Create(Control.BackgroundProperty, (Theme t) => t.Palette.Accent),
        Setter.Create(Control.ForegroundProperty, (Theme t) => t.Palette.AccentText),
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
window.StyleSheet.Define("accent", accentButton);

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
