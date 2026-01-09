![Aprillz.MewUI](https://raw.githubusercontent.com/aprillz/MewUI/main/assets/logo/logo-480.png)


![.NET](https://img.shields.io/badge/.NET-8%2B-512BD4?logo=dotnet&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-10%2B-0078D4?logo=windows&logoColor=white)
![Linux](https://img.shields.io/badge/Linux-X11-FCC624?logo=linux&logoColor=black)
![NativeAOT](https://img.shields.io/badge/NativeAOT-Ready-2E7D32)
![License: MIT](https://img.shields.io/badge/License-MIT-000000)
[![NuGet](https://img.shields.io/nuget/v/Aprillz.MewUI.svg?label=NuGet)](https://www.nuget.org/packages/Aprillz.MewUI/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Aprillz.MewUI.svg?label=Downloads)](https://www.nuget.org/packages/Aprillz.MewUI/)

---

**😺 MewUI**는 **크로스플랫폼** **NativeAOT + Trim** 앱을 목표로 하는, 코드 기반(code-first) 경량 .NET GUI 라이브러리입니다.

- ## 🧪 실험적 프로토타입
  > ⚠️본 프로젝트는 **개념 검증(Proof-of-Concept)** 을 위한 실험적 프로토타입입니다.  
  > ⚠️ **v1.0에 도달하는 과정에서 API, 내부 구조, 동작 방식이 크게 변경될 수 있습니다.**  
  > ⚠️ 현재 단계에서는 **하위 호환성을 보장하지 않습니다.**

- ## 🤖 AI 기반 개발 방식 
  > 본 프로젝트는 **AI 프롬프팅 중심의 개발 방식**으로 구현되었습니다.  
  > 직접적인 코드 수정 없이 **프롬프트 이터레이션을 통해 설계와 구현을 반복**하였으며,  
  > 각 단계는 개발자가 검토하고 조정하는 방식으로 진행되었습니다.


---

## 🚀 빠르게 실행해 보기

다음 명령어를 Windows 명령 프롬프트 또는 Linux 터미널에서 입력하면 즉시 실행할 수 있습니다.
(.NET 10 SDK가 필요합니다.)
> ⚠️ 이 명령은 GitHub에서 코드를 직접 다운로드하여 실행합니다.
```bahs
curl -sL https://raw.githubusercontent.com/aprillz/MewUI/refs/heads/main/samples/FBASample/fba_sample.cs -o - | dotnet run -
```


### 비디오
https://github.com/user-attachments/assets/876c1450-de36-4ccf-9cf8-b47f035a300f

### 스크린샷

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

## 🚀 빠른 시작

- NuGet: https://www.nuget.org/packages/Aprillz.MewUI/
  - 설치: `dotnet add package Aprillz.MewUI`

- 단일 파일로 빠르게 시작(VS Code 친화)
  - 참고: `samples/FBASample/fba_calculator.cs`
  - AOT/Trim 옵션을 제외한 최소 헤더:

    ```csharp
    #:sdk Microsoft.NET.Sdk
    #:property OutputType=Exe
    #:property TargetFramework=net10.0

    #:package Aprillz.MewUI@0.2.0
   
    //...
    ```

- 실행: `dotnet run your_app.cs`

---
## 🧪 C# 마크업 예시

- 샘플 소스: https://github.com/aprillz/MewUI/blob/main/samples/MewUI.Sample/Program.cs
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
- **NativeAOT + Trim 친화**
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
- Linux에서 NativeAOT로 빌드하려면, 일반 .NET SDK 외에 AOT 워크로드가 필요합니다(예: 배포판 패키지로 `dotnet-sdk-aot-10.0` 설치).
- interop/dynamic 기능을 추가했다면, 위 publish 설정으로 반드시 검증하는 것을 권장합니다.

로컬에서 확인:
- Publish: `dotnet publish .\samples\MewUI.Sample\MewUI.Sample.csproj -c Release -p:PublishProfile=win-x64-trimmed`
- 출력 확인: `.artifacts\publish\MewUI.Sample\win-x64-trimmed\`

참고(샘플, `win-x64-trimmed`):
- `Aprillz.MewUI.Sample.exe` 약 `2,257 KB`

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
                .BindText(
                    percent, 
                    convert: v => $"Percent ({v:P0})"); 
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

백엔드
- `Direct2D` (Windows)
- `GDI` (Windows)
- `OpenGL` (Windows / Linux)

샘플은 Windows에서는 `Direct2D`, Linux에서는 `OpenGL`을 기본으로 사용합니다.
- `Direct2D`: 초기엔 느리고 상주 메모리가 크지만, 복잡한 레이아웃/효과에 더 적합
- `GDI`: 가볍고 빠르게 시작되지만, CPU 사용이 커서 고해상도/큰 창/복잡한 UI에는 부적합
- `OpenGL`: Linux/X11 플랫폼 호스트에서 동작하며, 크로스플랫폼을 목표로 하지만 아직은 실험 단계입니다

---
## 🪟 플랫폼 추상화

윈도우/메시지 루프는 플랫폼 계층으로 추상화되어 있습니다.

현재 구현
- Windows (`Win32PlatformHost`)
- Linux/X11 (experimental)

---
## 🧭 로드맵 (TODO)

**컨트롤**
- [ ] `Image`
- [ ] `GroupBox`
- [ ] `TabControl`
- [ ] `ScrollViewer`

**렌더링**
- [ ] 폰트 셰이핑(HarfBuzz)
- [ ] 글리프 아틀라스/캐시 개선

**플랫폼**
- [ ] Wayland
- [ ] macOS

**툴링**
- [ ] Hot Reload 
- [ ] 디자인 타임 미리보기
