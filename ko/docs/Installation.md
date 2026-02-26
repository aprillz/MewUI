# 설치 및 패키지 구성

이 문서는 MewUI NuGet 패키지의 구성과 설치 방법, publish 시 백엔드 선택 방법을 정리한다.

---

## 1. 패키지 구조 개요

MewUI는 **메타패키지**(편의 번들)와 **개별 패키지**(세분화된 조합)로 구성된다.

```
Aprillz.MewUI                  ← 올인원 메타패키지 (모든 플랫폼 + 모든 백엔드)
├─ Aprillz.MewUI.Core          ← 코어 (컨트롤, 레이아웃, 마크업, 바인딩)
├─ Aprillz.MewUI.Platform.*    ← 플랫폼 호스트
│   ├─ .Platform.Win32
│   ├─ .Platform.X11
│   └─ .Platform.MacOS
└─ Aprillz.MewUI.Backend.*     ← 렌더링 백엔드
    ├─ .Backend.Direct2D        (Windows)
    ├─ .Backend.Gdi             (Windows)
    ├─ .Backend.MewVG.Win32     (Windows, NanoVG/OpenGL)
    ├─ .Backend.MewVG.X11       (Linux, NanoVG/OpenGL)
    └─ .Backend.MewVG.MacOS     (macOS, NanoVG/Metal)
```

별도 관리 패키지 (메타패키지에 포함되지 않음):
- `Aprillz.MewUI.Svg` — SVG 파싱/렌더링
- `Aprillz.MewUI.Win32.WebView2` — WebView2 통합 (Windows 전용)

---

## 2. 설치

### 2.1 빠른 시작 — 메타패키지

대부분의 경우 플랫폼별 메타패키지 하나만 추가하면 된다.

| 대상 플랫폼 | 패키지 | 포함 내용 |
|------------|--------|----------|
| **Windows** | `Aprillz.MewUI.Windows` | Core + Win32 + Direct2D + GDI + MewVG |
| **Linux** | `Aprillz.MewUI.Linux` | Core + X11 + MewVG |
| **macOS** | `Aprillz.MewUI.MacOS` | Core + MacOS + MewVG |
| **크로스플랫폼** | `Aprillz.MewUI` | 전 플랫폼 + 전 백엔드 |

```bash
# Windows 앱
dotnet add package Aprillz.MewUI.Windows

# 크로스플랫폼 앱
dotnet add package Aprillz.MewUI
```

### 2.2 개별 패키지 조합

메타패키지 대신 필요한 패키지만 직접 참조할 수 있다.

```xml
<ItemGroup>
  <PackageReference Include="Aprillz.MewUI.Core" Version="0.10.3" />
  <PackageReference Include="Aprillz.MewUI.Platform.Win32" Version="0.10.3" />
  <PackageReference Include="Aprillz.MewUI.Backend.Gdi" Version="0.10.3" />
</ItemGroup>
```

### 2.3 추가 패키지

SVG나 WebView2가 필요하면 별도로 추가한다.

```bash
dotnet add package Aprillz.MewUI.Svg
dotnet add package Aprillz.MewUI.Win32.WebView2
```

---

## 3. Publish 시 백엔드 선택

### 3.1 개요

메타패키지에는 해당 플랫폼의 모든 백엔드가 포함된다.
publish 시 `MewUIBackend` 프로퍼티로 사용할 백엔드 하나만 선택할 수 있다.
미지정 시 모든 백엔드가 publish 출력에 포함된다.

### 3.2 CLI에서 지정

```bash
# Direct2D만 포함
dotnet publish -r win-x64 -p:MewUIBackend=Direct2D

# GDI만 포함 (경량)
dotnet publish -r win-x64 -p:MewUIBackend=Gdi

# MewVG만 포함
dotnet publish -r win-x64 -p:MewUIBackend=MewVG
```

### 3.3 csproj에서 지정

```xml
<PropertyGroup>
  <MewUIBackend>Direct2D</MewUIBackend>
</PropertyGroup>
```

### 3.4 Publish Profile에서 지정

```xml
<!-- Properties/PublishProfiles/Win-Direct2D.pubxml -->
<Project>
  <PropertyGroup>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <MewUIBackend>Direct2D</MewUIBackend>
  </PropertyGroup>
</Project>
```

```bash
dotnet publish -p:PublishProfile=Win-Direct2D
```

### 3.5 MewUIBackend 값 목록

| 값 | 유지되는 백엔드 | 제거되는 백엔드 |
|----|---------------|---------------|
| `Direct2D` | Backend.Direct2D | Backend.Gdi, Backend.MewVG.Win32 |
| `Gdi` | Backend.Gdi | Backend.Direct2D, Backend.MewVG.Win32 |
| `MewVG` | Backend.MewVG.* | Backend.Direct2D, Backend.Gdi |
| *(미지정)* | 전부 | — |

> Linux와 macOS는 MewVG 백엔드만 포함하므로 `MewUIBackend` 설정이 불필요하다.

---

## 4. 크로스플랫폼 Publish (Aprillz.MewUI)

올인원 패키지(`Aprillz.MewUI`)는 모든 플랫폼의 어셈블리를 포함하지만,
`dotnet publish -r <rid>` 시 **대상 플랫폼이 아닌 어셈블리는 자동으로 제외**된다.

| RID | 유지 | 자동 제거 |
|-----|------|----------|
| `win-x64` | Core, Win32, Direct2D, Gdi, MewVG.Win32 | X11, MacOS, MewVG.X11, MewVG.MacOS |
| `linux-x64` | Core, X11, MewVG.X11 | Win32, MacOS, Direct2D, Gdi, MewVG.Win32, MewVG.MacOS |
| `osx-arm64` | Core, MacOS, MewVG.MacOS | Win32, X11, Direct2D, Gdi, MewVG.Win32, MewVG.X11 |

RID 필터링과 `MewUIBackend` 필터링은 함께 사용할 수 있다.

```bash
# Windows + Direct2D만 포함
dotnet publish -r win-x64 -p:MewUIBackend=Direct2D
```

---

## 5. 렌더링 백엔드 선택 가이드

| 백엔드 | 플랫폼 | 특징 |
|--------|--------|------|
| **Direct2D** | Windows | GPU 가속, 고품질 텍스트 렌더링. Windows 기본 권장 |
| **GDI** | Windows | CPU 기반, 초경량, 최소 의존성 |
| **MewVG** | Windows, Linux, macOS | NanoVG 기반 Managed 포트. OpenGL(Win32/X11) 또는 Metal(macOS) 사용 |

앱 코드에서의 백엔드 등록은 [Application Lifecycle](ApplicationLifecycle.md)을 참고한다.

---

## 6. 파일 기반 앱 (.NET 10+)

.NET 10의 파일 기반 앱에서는 `#:package` 지시어로 패키지를 참조한다.

```csharp
#:sdk Microsoft.NET.Sdk
#:property OutputType=Exe
#:property TargetFramework=net10.0

#:package Aprillz.MewUI@0.10.3

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

// ...
Application.Run(window);
```

---

## 7. 버전 호환성

- 모든 MewUI 패키지(Core, Platform.*, Backend.*, 메타패키지)는 **동일 버전**으로 발행된다.
- 메타패키지 하나만 참조하면 의존 패키지 버전이 자동으로 통일된다.
- 개별 패키지를 직접 조합할 경우 **모든 패키지의 버전을 동일하게** 맞춘다.
