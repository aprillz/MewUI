# Application 및 Window 수명 주기

이 문서는 시작부터 종료까지 MewUI의 Application/Window 라이프사이클과 DX를 정리한 가이드이다.
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

- [Theme 문서](Theme.ko.md)

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

### 5.1 창 닫기
`Window.Close()`(및 플랫폼의 닫기 버튼)는 전체 닫기 라이프사이클을 수행한다:

1) `Closing` 발생. `args.Cancel = true`로 닫기를 취소할 수 있다
2) 네이티브 창 파괴
3) `Closed` 발생, Application에서 등록 해제

owner와 함께 표시한 창(`Show(owner)` / `ShowDialogAsync(owner)`)은 owner가 닫힐 때 함께 닫힌다.

#### 예제: 닫기 전 확인
```csharp
window.Closing += args =>
{
    if (hasUnsavedChanges && !ConfirmDiscard())
        args.Cancel = true;
};

window.Closed += () => SaveWindowPlacement();
```

### 5.2 애플리케이션이 종료되는 시점
메시지 루프는 **마지막 창**이 닫힐 때 종료되고, 이어서 `Application.Run(...)`이 반환된다.
다른 창이 열려 있는 동안에는 메인 창만 닫아도 앱이 종료되지 않는다.

메인 창을 닫으면 앱이 종료되게 하려면, 보조 창을 메인 창을 owner로 하여 표시(`tools.Show(main)`)해 함께 닫히게 하거나, 메인 창의 `Closed` 핸들러에서 보조 창을 닫는다.

### 5.3 Application.Quit
`Application.Quit()`는 메시지 루프를 즉시 종료한다:

- 열린 창에는 `Closing` 콜백이 전달되지 **않으므로** 종료를 취소할 방법이 없다
- 이 경로에서는 창별 닫기 라이프사이클이 보장되지 않는다. 저장 확인이나 상태 보존을 `Closing`/`Closed` 핸들러에 의존하지 말 것
- 플랫폼 리소스가 정리되고 `Application.Run(...)`이 반환된다

### 5.4 권장 패턴
- 기본: 사용자가 창을 닫게 두면 마지막 창과 함께 앱이 종료된다.
- 확인 절차를 존중해야 하는 "종료" 메뉴/버튼: `mainWindow.Close()`를 호출해 `Closing` 핸들러가 확인/취소를 결정하게 한다.
- 메인 창이 곧 앱 수명인 경우: 메인 창의 `Closed`에 `Application.Quit`를 구독한다.
- 확인 없이 즉시 종료(이미 저장된 상태, 워치독 재시작 등): `Application.Quit()`.

#### 예제: 메인 창 닫힘 = 앱 종료
앱 수명을 메인 창에 묶는 표준 레시피다. 모든 종료가 `main.Close()`를 거치므로 확인 절차가 한 곳에 모이고 취소가 그대로 존중된다. Quit은 닫기가 실제로 완료된 뒤에만 실행되므로, 도구/백그라운드 창이 남아 있어도 종료가 보장된다.

```csharp
// 1) 확인(선택): Closing 핸들러 하나가 모든 종료 경로를 지킨다.
main.Closing += args =>
{
    if (hasUnsavedChanges && !ConfirmDiscard())
        args.Cancel = true;
};

// 2) 상태 저장: 닫기가 허용됐을 때만 실행된다.
main.Closed += SaveSession;

// 3) 메인 창 = 앱 수명.
main.Closed += Application.Quit;

// 4) 모든 종료 명령은 Quit이 아니라 Close를 거친다.
new Button().Content("Exit").OnClick(() => main.Close());
```

조건 없는 즉시 종료(이미 저장된 상태, 워치독 재시작 등)만 `Application.Quit()`를 직접 호출한다. 이때 `Closing`/`Closed` 핸들러는 실행되지 않는다.

#### Close와 Quit의 순차 호출
```csharp
main.Close();
Application.Quit();
```

이 시퀀스는 "허용되면 곱게 닫되, 어쨌든 종료한다"는 의미다. 로그아웃, 치명 오류 후 종료, 업데이트 후 재시작 같은 흐름에 적합하다. 모든 플랫폼에서 `Close()`의 닫기 라이프사이클이 `Quit()`보다 먼저 실행된다(X11/macOS는 동기 실행, Win32는 post된 `WM_CLOSE`가 루프 종료 전에 드레인됨).

- `Closing`이 취소하지 않으면: `Closed` 정리가 실행된 뒤 앱이 종료된다.
- `Closing`이 취소하면: 종료는 그대로 진행되고, 그 창은 `Closed` 정리를 건너뛴 채 Quit 경로로 폐기된다.

동기 시퀀스는 어떤 `Closing` 핸들러도 deferral을 잡지 않을 때만 안전하다. deferral([5.5](#55-비동기-종료-closeasync와-closing-deferral) 참고)이 잡히면 `Close()`는 결정이 미해결인 채 반환되고, `Quit()`이 결정 전에 루프를 끝내버린다. `CloseAsync`는 같은 의도를 표현하면서 결정까지 기다린다:

```csharp
await main.CloseAsync();   // deferral 포함, 닫기 라이프사이클 완주
Application.Quit();        // 결과와 무관하게 종료
```

"무조건 종료" 흐름에는 이 형태를 우선한다.

의도에 따라 고른다: 확인 절차가 앱을 계속 살릴 수 있어야 하면 `main.Closed += Application.Quit` + `main.Close()`를, 무조건 종료해야 하면 이 시퀀스를 쓴다.

### 5.5 비동기 종료: CloseAsync와 Closing deferral
`Window.CloseAsync()`는 닫기를 요청하고 결과를 알려준다:

```csharp
bool closed = await window.CloseAsync();   // true = 닫힘, false = Closing이 취소
```

- 이미 닫힌 창은 즉시 `true`로 완료된다(멱등).
- 동시에 들어온 닫기 요청은 하나의 pending 결정에 합류하고, `Closing`은 한 번만 실행된다.

닫기 결정 자체가 비동기인 경우(확인 다이얼로그, 비동기 저장)에는 "Cancel 후 재-Close" 대신 `Closing`에서 deferral을 잡는다:

```csharp
window.Closing += async args =>
{
    using (args.GetDeferral())        // 첫 await 전에 잡을 것
    {
        if (!await ConfirmDiscardAsync())
            args.Cancel = true;
    }                                 // 여기서 deferral이 완료된다 - 결정이 제출되는 시점
};
```

- 모든 deferral이 완료될 때까지 창은 열린 채 유지되고, 이후 `Cancel`을 종합해 판정한다(하나라도 취소면 취소).
- 허용으로 판정되면 `Closing`을 다시 발생시키지 않고 닫기를 진행한다.
- `CloseAsync`는 deferral 해소 이후에 완료되므로, 비동기 핸들러가 있어도 결과가 항상 진실이다.
- 결정 대기 중 들어온 닫기 요청은 pending에 합류한다. 확인 프롬프트가 중복 표시되지 않는다.

다중 창 종료 오케스트레이션도 가능해진다:

```csharp
foreach (var w in openWindows)
    if (!await w.CloseAsync()) return;   // 하나라도 취소하면 종료 중단
// 전부 닫힘 - 마지막 창 규칙으로 앱 종료
```

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
- 앱은 마지막 창이 닫힐 때 종료된다. `Window.Close()`가 정상(취소 가능) 경로, `Application.Quit()`는 즉시 종료 경로
