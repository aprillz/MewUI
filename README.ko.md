<img src="https://raw.githubusercontent.com/aprillz/MewUI/main/assets/logo/logo-256.png" alt="Aprillz.MewUI" width="256" height="256" />


![.NET](https://img.shields.io/badge/.NET-8%2B-512BD4?logo=dotnet&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-10%2B-0078D4?logo=windows&logoColor=white)
![NativeAOT](https://img.shields.io/badge/NativeAOT-Ready-2E7D32)
![License: MIT](https://img.shields.io/badge/License-MIT-000000)

---

**😺 MewUI**는 **NativeAOT + Trim** 앱을 목표로 하는, 코드 기반(code-first) 경량 .NET GUI 라이브러리입니다.

- **상태:** 🧪 실험적 프로토타입 버전입니다(기능/동작/API는 변경될 수 있습니다).

- **참고:** 🤖 이 저장소의 대부분의 코드는 GPT의 도움으로 작성되었습니다.
---

## 스크린샷

| Light | Dark |
|---|---|
| ![Light (screenshot)](https://raw.githubusercontent.com/aprillz/MewUI/main/assets/screenshots/light.png) | ![Dark (screenshot)](https://raw.githubusercontent.com/aprillz/MewUI/main/assets/screenshots/dark.png) |

---
## ✨ 주요 특징

- 📦 **NativeAOT + Trim** 우선
- 🪶 **빠르고 가볍게** (가벼운 실행 파일 크기, 낮은 메모리 풋프린트, 빠른 시작)
- 🧩 Fluent **C# 마크업**

---
## 🪶 경량(Lightweight)

- **실행 파일 크기:** NativeAOT + Trim 중심(샘플 `win-x64-trimmed` 약 `2.2 MB`)
- **샘플 런타임 벤치마크** (NativeAOT + Trimmed, 50회 실행):

| 백엔드 | Loaded avg/p95 (ms) | FirstFrame avg/p95 (ms) | WS avg/p95 (MB) | PS avg/p95 (MB) |
|---|---:|---:|---:|---:|
| Direct2D | 10 / 11 | 178 / 190 | 40.0 / 40.1 | 54.8 / 55.8 |
| GDI | 15 / 21 | 54 / 67 | 15.2 / 15.3 | 4.6 / 4.8 |

---
## 🧪 C# 마크업 예시

```csharp
var window = new Window()
    .Title("Hello MewUI")
    .Size(520, 360)
    .Padding(12)
    .Content(
        new StackPanel()
            .Spacing(8)
            .Children(
                new Label()
                    .Text("Hello, Aprillz.MewUI")
                    .FontSize(18)
                    .Bold(),
                new Button()
                    .Content("Quit")
                    .OnClick(() => Application.Quit())
            )
    );

Application.Run(window);
```

---
## 🎯 컨셉

### MewUI는 아래 4가지를 최우선으로 둔 code-first UI 라이브러리
- **NativeAOT + Trim 친화**(interop는 `LibraryImport`)
- 작은 크기, 빠른 시작시간, 적은 메모리 사용
- **XAML 없이 Fluent한 C# 마크업**으로 UI 트리 구성
- **AOT 친화적 바인딩**

### 지향하지 않는 것
- WPF처럼 **애니메이션**, **화려한 이펙트**, 무거운 컴포지션 파이프라인
- “다 들어있는” 리치 컨트롤 카탈로그
- 복잡한 경로 기반 데이터 바인딩
- XAML/WPF 완전 호환이나 디자이너 중심 워크플로우

---
## ✂️ NativeAOT / Trim

- 기본적으로 trimming-safe를 지향합니다(명시적 코드 경로, 리플렉션 기반 바인딩 없음).
- Windows interop은 NativeAOT 호환을 위해 소스 생성 P/Invoke(`LibraryImport`)를 사용합니다.
- interop/dynamic 기능을 추가했다면, 위 publish 설정으로 반드시 검증하는 것을 권장합니다.

로컬에서 확인:
- Publish: `dotnet publish .\samples\MewUI.Sample\MewUI.Sample.csproj -c Release -p:PublishProfile=win-x64-trimmed`
- 출력 확인: `.artifacts\publish\MewUI.Sample\win-x64-trimmed\`

참고(샘플, `win-x64-trimmed`):
- `Aprillz.MewUI Demo.exe` 약 `2,257 KB`

---
## 🔗 상태/바인딩(AOT 친화)

바인딩은 리플렉션 없이, 명시적/델리게이트 기반입니다:

```csharp
var percent = new ObservableValue<double>(
    initialValue: 0.25,
    coerce: v => Math.Clamp(v, 0, 1));

var slider = new Slider()
                .BindValue(percent);

var label  = new Label()
                .BindText(percent, v => $"Percent ({v:P0})"); // Converter
```

---
## 🧱 컨트롤 / 패널

컨트롤:
- `Label`, `Button`, `TextBox`
- `CheckBox`, `RadioButton`
- `ListBox`, `ComboBox`
- `Slider`, `ProgressBar`
- `Window`

패널:
- `Grid` (row/column: `Auto`, `*`, pixel)
- `StackPanel` (가로/세로 + Spacing)
- `DockPanel` (도킹 + 마지막 자식 채우기)
- `UniformGrid` (균등 셀)
- `WrapPanel` (줄바꿈 + Item size + Spacing)
---
## 🎨 테마(Theme)

테마는 두 부분으로 구성됩니다:
- `Palette` - 색상(배경/Accent 기반 파생 색 포함)
- `Theme` - 색 이외의 파라미터(코너 라디우스, 기본 폰트 등 + `Palette`)

Accent 변경:

```csharp
Theme.Current = Theme.Current.WithAccent(Color.FromRgb(214, 176, 82));
```

---
## 🖌️ 렌더링 백엔드

렌더링은 아래 추상화로 분리됩니다:
- `IGraphicsFactory` / `IGraphicsContext`

샘플은 기본적으로 `Direct2D`를 사용하며, `GDI` 백엔드도 제공됩니다.

---
## 🪟 플랫폼 추상화

윈도우/메시지 루프는 플랫폼 계층으로 추상화되어 있으며, 현재는 Windows 구현(`Win32PlatformHost`)을 제공합니다.
추후 Linux/macOS 포팅 시 이 계층

---
## 🧭 로드맵 (TODO)

**컨트롤**
- [ ] `Image`
- [ ] `GroupBox`
- [ ] `TabControl`
- [ ] `ScrollViewer`

**렌더링**
- [ ] OpenGL 백엔드

**플랫폼**
- [ ] Linux
- [ ] macOS

**툴링**
- [ ] Hot Reload 
- [ ] 디자인 타임 미리보기
