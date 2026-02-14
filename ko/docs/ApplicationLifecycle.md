# Application 및 Window 수명 주기

이 문서는 **시작(Startup) 중심**으로 MewUI의 Application/Window 라이프사이클과 DX를 정리한 가이드이다.
결정된 사항을 기준으로 작성하며, “Run 전 설정”과 “Run 이후 동작”의 경계를 명확히 한다.

---

## 1. 시작 전 구성

이 장에서는 `Application.Run(...)` 호출 전에 필요한 플랫폼/그래픽스 백엔드 구성 방식을 정리한다.

MewUI의 플랫폼/그래픽스 백엔드는 **코어가 enum/switch로 선택하지 않고**, 각 패키지가 등록/기본값 선택을 제공하는 형태를 지향한다(Trim/AOT 친화).

### 1.1 권장 방식

플랫폼/백엔드 패키지의 `Register()`를 호출해 등록한 뒤, `Application.Run(...)`으로 진입한다.
```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Backends;
using Aprillz.MewUI.PlatformHosts;

// 런타임에서 현재 OS를 보고, 해당 OS에서만 유효한 플랫폼/백엔드를 "등록"한다.
// (현재 예시 기준: Windows=Win32, Linux=X11, macOS는 추후 지원)
if (OperatingSystem.IsWindows())
{
    Win32Platform.Register();
    Direct2DBackend.Register(); // 또는 GdiBackend.Register() / OpenGLWin32Backend.Register()
}
else if (OperatingSystem.IsLinux())
{
    X11Platform.Register();
    OpenGLX11Backend.Register();
}
else if (OperatingSystem.IsMacOS())
{
    // TODO: macOS 플랫폼 호스트/백엔드가 준비되면 여기서 등록
    throw new PlatformNotSupportedException("macOS platform host is not implemented yet.");
}
else
{
    throw new PlatformNotSupportedException("Unsupported OS.");
}

Application.Run(mainWindow);
```

### 1.2 단일 타깃 앱: Application.Create() 체인
한 앱이 **단일 플랫폼 + 단일 그래픽 백엔드로 고정**되어 있다면(예: Windows 전용), `Application.Create()` 체인을 쓰는 방식이 가장 간단하다.

이 방식은 아래 전제를 가진다:
- 프로젝트가 해당 플랫폼/백엔드 패키지를 **참조하고 있다**(그래서 `.UseWin32()`, `.UseDirect2D()` 같은 확장 메서드가 보인다).
- 런타임에서 OS를 고르지 않고, **빌드/패키지 참조가 이미 고정**되어 있다.

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Backends;
using Aprillz.MewUI.PlatformHosts;

Application.Create()
    .UseWin32()
    .UseDirect2D()
    .Run(mainWindow);
```

### 1.3 멀티 타깃 기반 체인 고정
멀티 플랫폼 앱에서 런타임 분기 대신, **csproj 조건(주로 RID/CI publish)로 심볼을 만들고 `#if`로 체인을 고정**할 수 있다.
이 방식은 “해당 빌드에 필요한 플랫폼/백엔드 패키지”만 참조하도록 구성하기 쉬워 트리밍/배포 관점에서도 유리하다.

#### 1.3.1 csproj에서 조건으로 심볼 정의 예시
```xml
<PropertyGroup>
  <TargetFrameworks>net10.0-windows;net10.0</TargetFrameworks>
  <!-- 배포/CI에서 publish -r ... 로 RID를 주입하는 형태를 가정 -->
  <RuntimeIdentifiers>win-x64;linux-x64;osx-x64;osx-arm64</RuntimeIdentifiers>
</PropertyGroup>

<!-- 개발(보통 RID가 비어 있음): 런타임 OS 분기 경로를 사용 -->
<PropertyGroup Condition="'$(RuntimeIdentifier)' == ''">
  <DefineConstants>$(DefineConstants);DEV</DefineConstants>
</PropertyGroup>

<!-- 배포/CI(RID가 지정됨): RID로 OS/아키텍처 심볼을 고정 -->
<PropertyGroup Condition="'$(RuntimeIdentifier)' != '' and $(RuntimeIdentifier.StartsWith('win-'))">
  <DefineConstants>$(DefineConstants);WINDOWS</DefineConstants>
</PropertyGroup>
<PropertyGroup Condition="'$(RuntimeIdentifier)' != '' and $(RuntimeIdentifier.StartsWith('linux-'))">
  <DefineConstants>$(DefineConstants);LINUX</DefineConstants>
</PropertyGroup>
<PropertyGroup Condition="'$(RuntimeIdentifier)' != '' and $(RuntimeIdentifier.StartsWith('osx-'))">
  <DefineConstants>$(DefineConstants);MACOS</DefineConstants>
</PropertyGroup>
```

#### 1.3.2 Program.cs에서 체인 고정 예시
```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Backends;
using Aprillz.MewUI.PlatformHosts;

Application.Create()

#if WINDOWS || DEV
    .UseWin32()
    .UseDirect2D()
#elif LINUX
    .UseX11()
    .UseOpenGL()
#elif MACOS
    .ThrowPlatformNotSupported("macOS platform host is not implemented yet.")
#else
    .ThrowPlatformNotSupported()
#endif
    .Run(mainWindow);
```

### 1.4 런타임 분기에서 체인 이어가기
런타임에서 OS를 판단해야 하는 경우에는, builder 변수를 통해 분기 후에도 체인을 계속 이어갈 수 있다.

### 참고 사항
- **Run 전에만 설정 가능**: Run 이후에는 변경 시 예외/무시(정책은 코드에서 통일).
- **플러그인 등록 기반**: 플랫폼/백엔드는 패키지에서 Register/Default 선택을 제공한다.

---

## 2. Application 시작 흐름

### 2.1 Application.Run
`Application.Run(Window)` 호출 시 아래 흐름이 진행된다.

1) `Application.Current` 지정
2) PlatformHost 생성 및 Dispatcher 초기화
3) Window 등록 및 Show
4) 메시지 루프 진입

#### 예시: 최소 구성
```csharp
var window = new Window()
    .Title("Hello")
    .Content(new TextBlock().Text("Hello, MewUI"));

Application.Run(window);
```

### 2.2 테마 설정 안내
ThemeVariant/Accent/ThemeSeed/ThemeMetrics 설정은 아래 문서를 참고한다.

- [Theme 문서](Theme.md)

---

## 3. Window 시작 흐름

### 3.1 Window 생성
`new Window()`는 단순 객체 생성이며 **플랫폼 핸들은 아직 없음**.

### 3.2 Show
`Window.Show()` 시점에:
1) Application에 등록
2) Backend(WindowHandle) 생성
3) Loaded 이벤트 발생
4) 첫 Layout & Render 실행

### 3.3 ShowDialogAsync (모달)
`ShowDialogAsync`는 창을 모달 다이얼로그로 띄우고 **닫힐 때 완료**된다.  
`owner`를 지정하면 다이얼로그가 열려 있는 동안 **owner가 비활성화**된다(플랫폼 의존).

```csharp
var dialog = new Window()
    .Title("Dialog")
    .Content(new TextBlock().Text("Hello from dialog"));

await dialog.ShowDialogAsync(owner: main);
```

#### 예시: 다중 창
```csharp
var main = new Window()
    .Title("Main")
    .Content(new TextBlock().Text("Main window"));

var tools = new Window()
    .Title("Tools")
    .Content(new TextBlock().Text("Tools window"));

main.OnLoaded(() => tools.Show());
Application.Run(main);
```

---

## 4. RenderLoopSettings

RenderLoop 동작은 `Application.Current.RenderLoopSettings`로 제어한다:

- `Mode`: `OnRequest` / `Continuous`
- `TargetFps`: 0이면 제한 없음
- `VSyncEnabled`: 백엔드 프레젠트/스왑 동작 제어

#### 예시: RenderLoop 설정
```csharp
Application.Current.RenderLoopSettings.SetContinuous(true);
Application.Current.RenderLoopSettings.VSyncEnabled = false;
Application.Current.RenderLoopSettings.TargetFps = 0; // unlimited
```

---

## 5. 종료 흐름

- `Window.Close()` → Backend 파괴 → Application 등록 해제
- 마지막 창이 닫히면 플랫폼 루프 종료로 이어질 수 있음(플랫폼 정책에 따름)
- `Application.Quit()`는 명시적으로 루프를 종료

---

## 6. 예외 처리

- UI 스레드에서 발생한 예외는 `Application.DispatcherUnhandledException`로 전달
- 미처리 예외는 치명적 종료로 간주

#### 예시: DispatcherUnhandledException 처리
```csharp
Application.DispatcherUnhandledException += e =>
{
    try
    {
        MessageBox.Show(e.Exception.ToString(), "Unhandled UI exception");
    }
    catch
    {
        // ignore
    }
    e.Handled = true;
};
```

---

## 7. 정리

- **Run 전 설정 → Run → 메시지 루프**가 핵심 흐름
- Theme/RenderLoop은 Run 전에 결정
- Window는 Show 시점에만 실제 플랫폼 리소스를 갖는다
