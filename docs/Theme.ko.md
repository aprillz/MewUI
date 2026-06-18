# 테마

이 문서는 MewUI의 테마(Theme) 시스템을 설명합니다.

---

## 1. 테마 구성 요소

MewUI의 Theme는 아래 4가지 입력으로 결정됩니다.

- `ThemeVariant` : `System` / `Light` / `Dark`
- `Accent` 또는 `Color` : 강조색(Accent)
- `ThemeSeed` : 라이트/다크 베이스 팔레트 시드
- `ThemeMetrics` : 컨트롤 크기/패딩/두께/폰트 등 “룩앤필” 메트릭

아래 예제들은 모두 **Run 전에** 값을 지정하는 “기본값(Defaults)” 용도입니다.

### 1.1 ThemeVariant

포함하는 요소: “라이트/다크 모드” 또는 “OS 설정(System) 추종”을 선택합니다.

```csharp
using Aprillz.MewUI;

// Default는 기본값이 ThemeVariant.System 이므로,
// System을 그대로 쓸 거라면 이 설정은 생략해도 동작이 동일합니다.
ThemeManager.Default = ThemeVariant.System;
// ThemeManager.Default = ThemeVariant.Light;
// ThemeManager.Default = ThemeVariant.Dark;
```

### 1.2 Accent

포함하는 요소: 기본 제공 Accent(`Accent.*`) 또는 사용자 지정 `Color`로 “강조색”을 지정합니다.

```csharp
using Aprillz.MewUI;

ThemeManager.DefaultAccent = Accent.Blue;
```

참고: Custom `Color`는 보통 **런타임 변경**에서 많이 사용합니다(3.2 참고).

### 1.3 ThemeSeed

포함하는 요소: Light/Dark 각각의 “베이스 팔레트 시드”를 지정합니다. (기본 룩을 결정)

ThemeSeed에 포함되는 대표 속성:
- `WindowBackground`: 윈도우 배경
- `WindowText`: 윈도우 기본 텍스트 색
- `ControlBackground`: 컨트롤 배경
- `ButtonFace`: 버튼 기본 배경
- `ButtonDisabledBackground`: 비활성 버튼 배경

```csharp
using Aprillz.MewUI;

ThemeManager.DefaultLightSeed = ThemeSeed.DefaultLight;
ThemeManager.DefaultDarkSeed  = ThemeSeed.DefaultDark;
```

### 1.4 ThemeMetrics

포함하는 요소: 앱 전체의 “기본 폰트/크기” 및 컨트롤 기본 높이/패딩/스크롤바 두께 등 UI 메트릭을 지정합니다.

ThemeMetrics에 포함되는 대표 속성:
- `FontFamily`, `FontSize`, `FontWeight`
- `BaseControlHeight`
- `ControlCornerRadius`
- `ItemPadding`
- `ScrollBarThickness`, `ScrollBarHitThickness`, `ScrollBarMinThumbLength`
- `ScrollWheelStep`, `ScrollBarSmallChange`, `ScrollBarLargeChange`

```csharp
using Aprillz.MewUI;

ThemeManager.DefaultMetrics = ThemeMetrics.Default with
{
    ControlCornerRadius = 6,
    FontSize = 13,
    FontFamily = "Noto Sans"
};
```

---

## 2. 시작 시 테마 설정

**권장 순서:**  
1) `ThemeManager.Default*` 먼저 고정  
2) UI 빌드  
3) `Application.Run(...)`
 
### 2.1 ThemeSeed 사용자 지정 예제

```csharp
using Aprillz.MewUI;

// 기본값(Default)을 전부 다시 지정할 필요는 없습니다.
// 바꾸고 싶은 것만 선택적으로 설정하세요.

ThemeManager.DefaultLightSeed = ThemeSeed.DefaultLight with
{
    WindowText = Color.FromRgb(20, 20, 20)
};

ThemeManager.DefaultDarkSeed = ThemeSeed.DefaultDark with
{
    WindowText = Color.FromRgb(240, 240, 240)
};

var mainWindow = new Window()
    .Title("Theme Seed Demo")
    .Content(new TextBlock().Text("Hello, MewUI").Bold());

Application.Run(mainWindow);
```

### 2.2 ApplicationBuilder로 설정 적용

포함하는 요소:
- Builder의 `UseTheme/UseAccent/UseSeed/UseMetrics`로 테마 입력을 설정
- (선택) 플랫폼/백엔드 패키지의 `UseWin32/UseDirect2D` 등으로 등록/선택까지 체인으로 연결

참고:
- Builder는 내부적으로 `ThemeManager.Default*`를 Run 직전에 반영합니다.

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Backends;
using Aprillz.MewUI.PlatformHosts;

var mainWindow = new Window()
    .Title("Theme + Builder")
    .Content(new TextBlock().Text("Hello"));

Application.Create()
    .UseMetrics(ThemeMetrics.Default with { ControlCornerRadius = 6, FontSize = 13, FontFamily = "Noto Sans" })
    .UseSeed(
        ThemeSeed.DefaultLight with { WindowText = Color.FromRgb(20, 20, 20) },
        ThemeSeed.DefaultDark  with { WindowText = Color.FromRgb(240, 240, 240) })
    // 필요하면 아래처럼 모드/액센트도 설정 (System/Blue는 기본값이라 생략 가능)
    // .UseTheme(ThemeVariant.System)
    // .UseAccent(Accent.Blue)
    .UseWin32()
    .UseDirect2D()
    .Run(mainWindow);
```

---

## 3. 실행 중 테마 변경

일반적으로 런타임에서 즉시 적용/전파되는 변경은 아래 2개를 지원합니다.

### 3.1 ThemeVariant 변경

```csharp
Application.Current.SetTheme(ThemeVariant.Dark);
// Application.Current.SetTheme(ThemeVariant.Light);
// Application.Current.SetTheme(ThemeVariant.System);
```

### 3.2 Accent 변경

```csharp
Application.Current.SetAccent(Accent.Green);

// Custom color
Application.Current.SetAccent(new Color(0xFF, 0x22, 0x88, 0xFF));
```

---

## 4. 테마 변경 콜백

테마가 바뀔 때마다(사용자 전환, System 모드에서 OS 테마 변경 등) 특정 속성을 현재 테마 기준으로 다시 적용하고 싶다면,
`WithTheme((theme, control) => ...)` 패턴을 사용합니다.

```csharp
var accentButton = new Button()
    .Text("Accent Button")
    .WithTheme((theme, c) =>
    {
        c.Background(theme.Palette.Accent);
        c.Foreground(theme.Palette.AccentText);
    });
```

---

## 5. 테마 변경 이벤트

```csharp
Application.Current.ThemeChanged += (oldTheme, newTheme) =>
{
    // 로깅/설정 저장/통계 등
};
```
