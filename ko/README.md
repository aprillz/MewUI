![Aprillz.MewUI](https://raw.githubusercontent.com/aprillz/MewUI/main/assets/logo/logo_h-1280.png)


![.NET](https://img.shields.io/badge/.NET-8%2B-512BD4?logo=dotnet&logoColor=white)
![Windows](https://img.shields.io/badge/Windows-10%2B-0078D4?logo=windows&logoColor=white)
![Linux](https://img.shields.io/badge/Linux-X11-FCC624?logo=linux&logoColor=black)
![macOS](https://img.shields.io/badge/macOS-12%2B-901DBA?logo=Apple&logoColor=white)
![NativeAOT](https://img.shields.io/badge/NativeAOT-Ready-2E7D32)
![License: MIT](https://img.shields.io/badge/License-MIT-000000)
[![NuGet](https://img.shields.io/nuget/v/Aprillz.MewUI.svg?label=NuGet)](https://www.nuget.org/packages/Aprillz.MewUI/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Aprillz.MewUI.svg?label=Downloads)](https://www.nuget.org/packages/Aprillz.MewUI/)

---

**😺 MewUI**는 **크로스플랫폼** **NativeAOT + Trim** 앱을 목표로 하는, 코드 기반(code-first) 경량 .NET GUI 라이브러리입니다.

### 🧪 실험적 프로토타입
  > [!IMPORTANT]
  > ⚠️본 프로젝트는 **개념 검증(Proof-of-Concept)** 을 위한 실험적 프로토타입입니다.  
  > ⚠️ **v1.0에 도달하는 과정에서 API, 내부 구조, 동작 방식이 크게 변경될 수 있습니다.**  
  > ⚠️ 현재 단계에서는 **하위 호환성을 보장하지 않습니다.**

### 🤖 AI 기반 개발 방식 
  > [!NOTE]
  > 본 프로젝트는 **AI 프롬프팅 중심의 개발 방식**으로 구현되었습니다.  
  > 직접적인 코드 수정 없이 **프롬프트 이터레이션을 통해 설계와 구현을 반복**하였으며,  
  > 각 단계는 개발자가 검토하고 조정하는 방식으로 진행되었습니다.


---

## 🚀 빠르게 실행해 보기

다음 명령어를 Windows 명령 프롬프트 또는 Linux 터미널에서 입력하면 즉시 실행할 수 있습니다.
(.NET 10 SDK가 필요합니다.)
> [!WARNING]
> 이 명령은 GitHub에서 코드를 직접 다운로드하여 실행합니다.
```bash
curl -sL https://raw.githubusercontent.com/aprillz/MewUI/refs/heads/main/samples/FBASample/fba_sample.cs -o - | dotnet run -
```


### 비디오
https://github.com/user-attachments/assets/2e0c1e0e-3dcd-4b5a-8480-fa060475249a

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

## 🚀 빠른 시작

- NuGet: https://www.nuget.org/packages/Aprillz.MewUI/
  - `Aprillz.MewUI`는 Core, 전 플랫폼 호스트, 전 렌더링 백엔드를 포함하는 **메타패키지**입니다.
  - 플랫폼별 패키지도 제공됩니다: `Aprillz.MewUI.Windows`, `.Linux`, `.MacOS`
  - 설치: `dotnet add package Aprillz.MewUI`
  - 참고: [설치 및 패키지 구성](docs/Installation.md)

- 단일 파일로 빠르게 시작(VS Code 친화)
  - 참고: `samples/FBASample/fba_calculator.cs`
  - AOT/Trim 옵션을 제외한 최소 헤더:

    ```csharp
    #:sdk Microsoft.NET.Sdk
    #:property OutputType=Exe
    #:property TargetFramework=net10.0

    #:package Aprillz.MewUI@0.10.3

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
- Publish: `dotnet publish .\samples\MewUI.Gallery\MewUI.Gallery.csproj -c Release -p:PublishProfile=win-x64-trimmed`
- 출력 확인: `.artifacts\publish\MewUI.Gallery\win-x64-trimmed\`

참고 (`Aprillz.MewUI.Gallery.exe` @v0.10.0)
- win-x64:  ~3,545 KB
- osx-arm64: ~2,664 KB
- linux-arm64: ~3939 KB
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

컨트롤(구현됨):
- `Button`
- `Label`, `Image`
- `TextBox`, `MultiLineTextBox`
- `CheckBox`, `RadioButton`, `ToggleSwitch`
- `ComboBox`, `ListBox`, `TreeView`, `GridView`
- `Slider`, `ProgressBar`, `NumericUpDown`
- `TabControl`, `GroupBox`
- `MenuBar`, `ContextMenu`, `ToolTip` (창 내 팝업)
- `ScrollViewer`
- `Window`, `DispatcherTimer`

패널:
- `Grid` (row/column: `Auto`, `*`, 픽셀)
- `StackPanel` (가로/세로)
- `DockPanel` (도킹 + 마지막 채우기)
- `UniformGrid` (균등 셀)
- `WrapPanel` (줄바꿈 + Item size)
- `SplitPanel` (드래그 분할)

> `SplitPanel`을 제외한 모든 패널은 `Spacing`을 지원합니다.
---
## 🎨 테마(Theme)
MewUI는 `Theme` 객체(색상 + 메트릭)와 `ThemeManager`를 사용하여 기본값 설정과 런타임 변경을 제어합니다.

- `ThemeManager.Default*`를 통해 `Application.Run(...)` 이전에 기본값을 설정합니다.
- `Application.Current.SetTheme(...)` /
  `Application.Current.SetAccent(...)`를 통해 런타임에 변경할 수 있습니다.

참고: `docs/Theme.md`

---
## 🖌️ 렌더링 백엔드

렌더링은 다음 인터페이스를 통해 추상화되어 있습니다.
- `IGraphicsFactory` / `IGraphicsContext`

백엔드:

| 백엔드 | 플랫폼 | 패키지 |
|--------|--------|--------|
| **Direct2D** | Windows | `Aprillz.MewUI.Backend.Direct2D` |
| **GDI** | Windows | `Aprillz.MewUI.Backend.Gdi` |
| **MewVG** | Windows | `Aprillz.MewUI.Backend.MewVG.Win32` |
| **MewVG** | Linux/X11 | `Aprillz.MewUI.Backend.MewVG.X11` |
| **MewVG** | macOS | `Aprillz.MewUI.Backend.MewVG.MacOS` |

> **MewVG**는 [NanoVG](https://github.com/memononen/nanovg)의 Managed 포트로, Windows/Linux에서는 OpenGL, macOS에서는 Metal을 사용합니다.

백엔드는 참조된 백엔드 패키지에 의해 등록됩니다 (Trim/AOT 친화적 구조).

애플리케이션 코드에서는 일반적으로 다음 중 하나를 사용합니다.
- `Application.Run(...)` 이전에 `*Backend.Register()`를 호출하거나
- 빌더 체인 방식인 `Application.Create().Use...().Run(...)`을 사용합니다.

메타패키지 사용 시 publish 단계에서 `-p:MewUIBackend=Direct2D`로 백엔드를 선택할 수 있습니다. 상세는 [설치 및 패키지 구성](docs/Installation.md)을 참고하세요.

---
## 🪟 플랫폼 추상화

윈도우 관리와 메시지 루프는 플랫폼 계층 뒤에서 추상화되어 있습니다.

현재 구현된 플랫폼:
- Windows (`Aprillz.MewUI.Platform.Win32`)
- Linux/X11 (`Aprillz.MewUI.Platform.X11`)
- macOS (`Aprillz.MewUI.Platform.MacOS`)

### Linux 대화상자 의존성
Linux에서는 `MessageBox`와 파일 대화상자가 현재 외부 도구를 통해 구현되어 있습니다.
- `zenity` (GNOME/GTK)
- `kdialog` (KDE)

`PATH`에 어느 것도 존재하지 않으면, MewUI는 다음 예외를 발생시킵니다.
`PlatformNotSupportedException: No supported Linux dialog tool found (zenity/kdialog).`

---
## 📄 문서

- [설치 및 패키지 구성](docs/Installation.md)
- [C# Markup](docs/CSharpMarkup.md)
- [Binding](docs/Binding.md)
- [Items and Templates](docs/ItemsAndTemplates.md)
- [Theme](docs/Theme.md)
- [Application Lifecycle](docs/ApplicationLifecycle.md)
- [Layout](docs/Layout.md)
- [RenderLoop](docs/RenderLoop.md)
- [Hot Reload](docs/HotReload.md)
- [Custom Controls](docs/CustomControls.md)

---
## 🧭 로드맵 (TODO)

**플랫폼**
- [ ] Linux/Wayland
- [x] macOS

**툴링**
- [x] Hot Reload
- [ ] 디자인 타임 미리보기
