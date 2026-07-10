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

**😺 MewUI**는 별도 .NET 런타임 설치 없이 배포할 수 있는 **NativeAOT/Trim 친화적** 데스크톱 앱을 만들기 위한 **크로스플랫폼 코드 기반 경량 .NET GUI 프레임워크**입니다.

> [!NOTE]
> MewUI의 공식 발음은 “뮤아이”입니다.

### 프로젝트 상태: 활발한 개발 및 API 안정화 단계
  > [!IMPORTANT]
  > MewUI는 NuGet 패키지, 크로스플랫폼 호스트, 여러 렌더링 백엔드, 선택형 확장 패키지를 갖춘 활발히 개발 중인 프레임워크입니다.
  >
  > 다만 공개 API는 아직 안정화 중이므로 minor 릴리스 사이에도 breaking change가 발생할 수 있습니다. 실제 앱에서는 패키지 버전을 고정하고 업그레이드 전 릴리스 노트를 확인하는 것을 권장합니다.

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
curl -sL https://raw.githubusercontent.com/aprillz/MewUI/refs/heads/main/samples/FBASample/fba_gallery.cs -o - | dotnet run -
```


### 비디오
[https://github.com/user-attachments/assets/2e0c1e0e-3dcd-4b5a-8480-fa060475249a](https://github.com/user-attachments/assets/fc2d6ad8-3317-4784-a6e5-a00c68e9ed3b)

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
  - 참고: [설치 및 패키지 구성](docs/Installation.ko.md)

- 단일 파일로 빠르게 시작(VS Code 친화)
  - 참고: `samples/FBASample/fba_calculator.cs`
  - AOT/Trim 옵션을 제외한 최소 헤더:

    ```csharp
    #:sdk Microsoft.NET.Sdk
    #:property OutputType=Exe
    #:property TargetFramework=net10.0

    #:package Aprillz.MewUI

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

MewUI는 작고 명시적인 코어 위에 플랫폼 호스트와 렌더링 백엔드를 분리해 쌓는 코드 기반 GUI 프레임워크입니다.

- **NativeAOT + Trim 친화**
- 작은 크기, 빠른 시작시간, 적은 메모리 사용
- **XAML 없이 Fluent한 C# 마크업**으로 UI 트리 구성
- **AOT 친화적인 명시적 바인딩**
- 코어는 얇게 유지하고 큰 기능은 선택형 확장 패키지로 분리

### 지향하지 않는 것
- XAML/WPF 완전 호환
- WPF나 Avalonia와 동일한 API/동작을 제공하는 대체품
- 디자이너 중심 개발 워크플로우
- 복잡한 경로 기반 데이터 바인딩
- 모든 것을 담은 만능 컨트롤 카탈로그

코어는 일반적인 데스크톱 UI 패턴까지 담고, 차트나 도킹 같은 특화 기능은 확장 패키지로 제공합니다.

---
## ✂️ NativeAOT / Trim

- 기본적으로 trimming-safe를 지향합니다(명시적 코드 경로, 리플렉션 기반 바인딩 없음).
- Windows 상호운용은 NativeAOT 호환을 위해 소스 생성 P/Invoke(`LibraryImport`)를 사용합니다.
- Linux에서 NativeAOT로 빌드하려면, 일반 .NET SDK 외에 AOT 워크로드가 필요합니다(예: 배포판 패키지로 `dotnet-sdk-aot-10.0` 설치).
- 새로운 상호운용 코드나 동적 기능을 추가했다면, 위 publish 설정으로 반드시 검증하는 것을 권장합니다.

로컬에서 확인:
- Publish: `dotnet publish .\samples\MewUI.Gallery\MewUI.Gallery.csproj -c Release -p:PublishProfile=win-x64-trimmed`
- 출력 확인: `.artifacts\publish\MewUI.Gallery\win-x64-trimmed\`

NativeAOT 실행 파일 크기는 플랫폼 호스트, 렌더링 백엔드, 리소스, publish 옵션에 따라 달라집니다. 아래 표는 **main executable만** 측정한 값이며, ZIP은 같은 실행 파일을 기본 측정 설정으로 압축한 보조값입니다. 크기는 **1 MB = 1024 KB** 기준입니다.

| 샘플 | 플랫폼 / 백엔드 | 실행 파일 | ZIP |
|---|---|---:|---:|
| Hello World | Windows x64 / GDI | 2.907 MB | 1.324 MB |
| Hello World | Windows x64 / Direct2D | 3.046 MB | 1.381 MB |
| Hello World | Windows x64 / MewVG | 3.211 MB | 1.458 MB |
| Hello World | Linux x64 / X11 + MewVG | 4.414 MB | 2.126 MB |
| Hello World | macOS arm64 / MewVG | 2.635 MB | 1.186 MB |
| Gallery | Windows x64 / GDI | 5.838 MB | 2.564 MB |
| Gallery | Windows x64 / Direct2D | 5.959 MB | 2.612 MB |
| Gallery | Windows x64 / MewVG | 6.176 MB | 2.707 MB |
| Gallery | Linux x64 / X11 + MewVG | 7.420 MB | 3.518 MB |
| Gallery | macOS arm64 / MewVG | 5.625 MB | 2.555 MB |

Gallery는 full-featured showcase 샘플입니다. 최소 배포 크기의 기준은 Hello World 행을 참고하세요.
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
- `Button`, `ToggleButton`
- `Label`, `TextBlock`, `Image`
- `TextBox`, `MultiLineTextBox`, `PasswordBox`
- `CheckBox`, `RadioButton`, `ToggleSwitch`
- `ComboBox`, `ListBox`, `TreeView`, `GridView`
- `Slider`, `ProgressBar`, `ProgressRing`, `NumericUpDown`
- `TabControl`, `GroupBox`, `Expander`, `Border`
- `ColorPicker`, `DatePicker`, `Calendar`
- `MenuBar`, `ContextMenu`, `ToolTip` (창 내 팝업)
- `ScrollViewer`
- `Window`, `DispatcherTimer`

패널:
- `Grid` (row/column: `Auto`, `*`, 픽셀)
- `StackPanel` (가로/세로)
- `DockPanel` (도킹 + 마지막 채우기)
- `UniformGrid` (균등 셀)
- `WrapPanel` (줄바꿈 + Item size)
- `Canvas` (절대 위치)
- `SplitPanel` (드래그 분할)

> `Canvas`(절대 위치)와 `SplitPanel`을 제외한 모든 패널은 `Spacing`을 지원합니다.
---
## 🧩 확장 (Extensions)

코어 위에 선택적으로 얹는 패키지입니다. 필요한 것만 참조하세요.

| 확장 | 설명 | 패키지 |
|------|------|--------|
| [**MewDock**](extensions/MewUI.MewDock/README.ko.md) | Visual Studio 스타일 도킹 - 문서/툴 탭, 드래그 재배치, 분할, 자동 숨김, 최대화, 팝아웃 | `Aprillz.MewUI.MewDock` |
| [**SVG**](extensions/MewUI.Svg/README.ko.md) | 순수 C# SVG 파싱/렌더링 (System.Drawing 비의존, AOT 호환) | `Aprillz.MewUI.Svg` |
| [**Skia**](extensions/MewUI.Skia/README.ko.md) | `SkiaCanvasView` (SkiaSharp로 그리기) + GPU zero-copy 인터롭 | `Aprillz.MewUI.Skia` |
| [**MewCharts**](extensions/MewUI.MewCharts/README.ko.md) | LiveChartsCore 엔진 기반 차트 (Cartesian/Pie/Polar), SkiaSharp 비의존 | `Aprillz.MewUI.MewCharts` |
| [**WebView2**](extensions/MewUI.WebView2.Win32/README.md) | Win32 WebView2 컨트롤 (Microsoft Edge WebView2 런타임 필요, Windows 전용) | `Aprillz.MewUI.WebView2.Win32` |

**Skia 인터롭** - 사용 중인 백엔드에 맞는 zero-copy 브리지를 하나 추가하면 GPU 직행 경로가 켜집니다.

| 백엔드 | 패키지 |
|--------|--------|
| Direct2D | `Aprillz.MewUI.Skia.Interop.Direct2D` |
| GDI | `Aprillz.MewUI.Skia.Interop.Gdi` |
| MewVG / Win32 | `Aprillz.MewUI.Skia.Interop.MewVG.Win32` |
| MewVG / X11 | `Aprillz.MewUI.Skia.Interop.MewVG.X11` |
| MewVG / macOS | `Aprillz.MewUI.Skia.Interop.MewVG.MacOS` |

> 인터롭 없이도 Skia 콘텐츠는 CPU 업로드 폴백으로 렌더링됩니다. Skia는 메타패키지 `Aprillz.MewUI.Skia.Windows` / `.Linux` / `.MacOS` / `.All`로도 묶여 있습니다.

> **MewDock**은 [FlexLayout](https://github.com/caplin/FlexLayout)(MIT)의 C# 포팅입니다. 라이선스 고지는 `THIRD_PARTY_NOTICES.md` 참고.
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

메타패키지 사용 시 publish 단계에서 `-p:MewUIBackend=Direct2D`로 백엔드를 선택할 수 있습니다. 상세는 [설치 및 패키지 구성](docs/Installation.ko.md)을 참고하세요.

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

- [설치 및 패키지 구성](docs/Installation.ko.md)
- [C# Markup](docs/CSharpMarkup.ko.md)
- [Binding](docs/Binding.ko.md)
- [Items and Templates](docs/ItemsAndTemplates.ko.md)
- [Theme](docs/Theme.ko.md)
- [Application Lifecycle](docs/ApplicationLifecycle.ko.md)
- [Layout](docs/Layout.ko.md)
- [RenderLoop](docs/RenderLoop.ko.md)
- [Hot Reload](docs/HotReload.ko.md)
- [Custom Controls](docs/CustomControls.ko.md)
- [Control Template](docs/ControlTemplate.ko.md)

---
## 🤝 커뮤니티

- [Contributing](CONTRIBUTING.ko.md)
- [Code of Conduct](CODE_OF_CONDUCT.ko.md)

---
## 🧭 로드맵

**플랫폼**
- [ ] Linux/Wayland
- [ ] Linux 프레임버퍼

**툴링**
- [x] Hot Reload (실험적)
- [ ] 디자인 타임 미리보기

---
## 라이선스

MewUI는 [MIT 라이선스](LICENSE)에 따라 배포됩니다.

서드파티 소프트웨어 고지는
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)에서 확인할 수 있습니다.
