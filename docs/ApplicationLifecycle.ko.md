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

// 런타임에서 현재 OS를 보고, 해당 OS에서만 유효한 플랫폼/백엔드를 "등록"한다.
if (OperatingSystem.IsWindows())
{
    Win32Platform.Register();
    Direct2DBackend.Register(); // 또는 GdiBackend.Register() / MewVGWin32Backend.Register()
}
else if (OperatingSystem.IsLinux())
{
    X11Platform.Register();
    MewVGX11Backend.Register();
}
else if (OperatingSystem.IsMacOS())
{
    MacOSPlatform.Register();
    MewVGMacOSBackend.Register();
}
else
{
    throw new PlatformNotSupportedException("Unsupported OS.");
}

Application.Run(mainWindow);
```

WebAssembly 빌드는 `BrowserPlatform`과 `MewVGBrowserBackend`를 등록하고 `Application.RunAsync`로 시작한다. [2.5](#25-브라우저-수명webassembly)를 참고한다.

### 1.2 단일 타깃 앱: Application.Create() 체인
한 앱이 **단일 플랫폼 + 단일 그래픽 백엔드로 고정**되어 있다면(예: Windows 전용), `Application.Create()` 체인을 쓰는 방식이 가장 간단하다.

이 방식은 아래 전제를 가진다:
- 프로젝트가 해당 플랫폼/백엔드 패키지를 **참조하고 있다**(그래서 `.UseWin32()`, `.UseDirect2D()` 같은 확장 메서드가 보인다).
- 런타임에서 OS를 고르지 않고, **빌드/패키지 참조가 이미 고정**되어 있다.

```csharp
using Aprillz.MewUI;

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

Application.Create()

#if WINDOWS || DEV
    .UseWin32()
    .UseDirect2D()
#elif LINUX
    .UseX11()
    .UseMewVGX11()
#elif MACOS
    .UseMacOS()
    .UseMewVGMetal()
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
// 플랫폼 호스트와 백엔드를 먼저 등록한다(1.1 참고). 등록하지 않으면 텍스트를 측정하는
// 첫 컨트롤에서 InvalidOperationException이 발생한다.
Win32Platform.Register();
Direct2DBackend.Register();

var window = new Window()
    .Title("Hello")
    .Content(new TextBlock().Text("Hello, MewUI"));

Application.Run(window);
```

startup 콜백이 필요하면 창 표시 전에 실행되는 오버로드를 사용한다.

```csharp
Application.Run(window, () =>
{
    // Dispatcher와 UI SynchronizationContext가 설치된 상태이고,
    // window는 아직 표시되지 않았다.
    InitializeServices();
});
```

### 2.2 메인 창 없이 시작

`Application.Run(Action)`은 메인 창을 만들거나 표시하지 않고 dispatcher와 플랫폼 메시지 루프를 시작한다. startup은 필수이며 dispatcher 설치 후 UI 스레드에서 한 번 호출된다. startup이나 이후 dispatcher 작업에서 일반 `Window.Show()`를 호출해 창을 열 수 있다.

```csharp
Application.Run(() =>
{
    Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    RegisterGlobalHotkey(() =>
        Application.Current.Dispatcher!.BeginInvoke(
            () => new PaletteWindow().Show()));
});
```

백그라운드 실행에서 `OnExplicitShutdown`은 자동으로 설정되지 않는다. 팔레트 같은 마지막 창을 닫아도 프로세스를 유지해야 한다면 사용자가 위 예제처럼 명시해야 한다.

빌더에서는 `OnStartup`을 설정하고 main window factory를 생략한다. `WithShutdownMode`는 startup 실행 전에 정책을 적용한다.

```csharp
Application.Create()
    .WithShutdownMode(ShutdownMode.OnExplicitShutdown)
    .OnStartup(RegisterGlobalHotkey)
    .Run();
```

`BuildMainWindow(...)`와 `OnStartup(...)`을 함께 설정하면 startup 호출 후 factory가 만든 메인 창을 표시한다. `OnStartup`을 여러 번 호출하면 마지막 콜백이 이전 설정을 대체한다.

### 2.3 startup 콜백 인자

startup 진입점에는 모두 `Action<string[]>` 오버로드가 있다. 프레임워크가 실행 파일 경로를 제외한 명령행 인자를 공급하므로, 진입점 바깥에서 조립한 startup 로직도 `args`를 직접 넘겨받을 필요가 없다.

```csharp
Application.Create()
    .OnStartup(args =>
    {
        if (args.Contains("--minimized"))
            StartMinimized();
    })
    .Run<MainWindow>();
```

### 2.4 테마 설정 안내
ThemeVariant/Accent/ThemeSeed/ThemeMetrics 설정은 아래 문서를 참고한다.

- [Theme 문서](Theme.ko.md)

기본 폰트는 플랫폼 패키지 등록 시점(`Win32Platform.Register()`, `UseWin32()` 및 X11/macOS/브라우저 대응 API)에 확정된다. 이 시점은 `Application.Create()`와 창 생성보다 앞서므로, 그 이후에 `ThemeMetrics.Default.FontFamily`나 `ThemeManager.DefaultMetrics.FontFamily`를 읽으면 실제 적용될 폰트명을 얻는다. 폰트를 지정하지 않았을 때 시스템 UI 폰트를 따르는 규칙은 [Theme 문서](Theme.ko.md)를 참고한다.

### 2.5 브라우저 수명(WebAssembly)

브라우저 호스트는 실행을 시작하고 끝내는 방식과 프레임을 그리는 주체가 데스크톱 호스트와 다르다.

- 진입점은 `Application.RunAsync`다. `Application.Run`은 `NotSupportedException`을 던진다. 애플리케이션이 실행되는 동안 자바스크립트 이벤트 루프가 계속 돌아야 하기 때문이다.
- `RunAsync`는 디스패처를 설치하고 startup 콜백을 실행하고 메인 창을 표시한 뒤 곧바로 반환한다. 반환한 Task는 종료 시점에 완료된다.
- 프레임은 호스트 페이지가 돌린다. 페이지가 자신의 애니메이션 프레임 루프에서 렌더링 진입점을 호출하므로, MewUI는 자체 루프를 돌리지 않는다.
- 최상위 `Window`는 하나다. 페이지의 표면이 유일하므로 두 번째 창을 표시하면 `NotSupportedException`이 발생한다.
- 모달은 `ShowDialogAsync`뿐이다. 이 호스트는 중첩 루프를 돌리지 않으므로 `ShowDialog`는 `NotSupportedException`을 던진다. 다이얼로그와 팝업, 메뉴는 그 표면 안에 그려진다.
- `Application.Shutdown()`은 `RunAsync`가 반환한 Task를 완료하고 프레임을 멈춘다. 탭은 스스로 닫을 수 없으므로 페이지 자체는 그대로 남는다.
- 창 하나를 닫으면 `ShutdownMode`와 무관하게 애플리케이션이 종료된다.

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
브라우저에서는 이 방식이 유일한 모달이다([2.5](#25-브라우저-수명webassembly) 참고).

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

브라우저에서는 `Continuous`와 `VSyncEnabled`가 매 프레임 다시 그릴지를 그대로 결정하지만, `TargetFps`는 효과가 없다. 프레임 간격을 페이지의 애니메이션 프레임이 정하기 때문이다.

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
창이 닫힐 때 메시지 루프를 종료할지는 `Application.ShutdownMode`가 결정한다.

- `OnLastWindowClose`(기본값): 마지막 창이 닫히면 종료한다. 메인 창 없이 시작했더라도 나중에 연 마지막 창을 닫으면 종료한다.
- `OnMainWindowClose`: 메인 창이 닫히면 종료한다. 창을 받는 `Run` 오버로드가 그 정체성을 기록하며, 메인 창 없이 시작한 실행은 대입하기 전까지 정체성이 없다(5.3 참고).
- `OnExplicitShutdown`: 창 닫힘으로 종료하지 않고 `Application.Shutdown()`을 기다린다.

브라우저 호스트는 창이 하나이고 그 창이 닫힐 때 실행이 끝나므로, 이 정책이 구분을 만들지 않는다.

이 정책은 실행 하나에만 적용되고 매번 `OnLastWindowClose`에서 시작한다. 실행 전에는 빌더로 지정하고, 실행 중에는 startup 콜백에서 `Application.Current.ShutdownMode`에 대입한다.

```csharp
Application.Create()
    .WithShutdownMode(ShutdownMode.OnExplicitShutdown)
    .OnStartup(StartBackgroundApplication)
    .Run();
```

### 5.3 Application.MainWindow
`OnMainWindowClose`가 지켜보는 정체성이 `Application.Current.MainWindow`다. 창을 받는 `Run` 오버로드(`Run(Window)`, `BuildMainWindow(...)`, `Run<TWindow>()`)가 이 값을 설정하며, 실행 중에도 대입할 수 있다.

메인 창 없이 시작한 실행이 `OnMainWindowClose`를 쓰려면 이 대입이 유일한 방법이다. 나중에 연 창을 메인으로 승격한다.

```csharp
Application.Create()
    .WithShutdownMode(ShutdownMode.OnMainWindowClose)
    .OnStartup(() =>
    {
        var dashboard = new DashboardWindow();
        dashboard.Show();
        Application.Current.MainWindow = dashboard;   // 이 창을 닫으면 실행이 끝난다
    })
    .Run();
```

- 이 정체성은 실행 하나에만 적용되고 다음 실행은 정체성 없이 시작한다.
- startup은 실행 창이 기록된 뒤에 돌므로, startup에서 대입하면 빌더가 준 창을 덮어쓴다.
- `null`을 대입하면 정체성이 해제되고, `OnMainWindowClose`는 발동할 대상을 잃는다.
- 빌더 경로는 정체성과 "시작 시 표시"를 함께 준다. 속성 대입은 정체성만 주므로 표시 여부를 직접 정할 수 있다.
- 프리뷰 호스트는 세션 시작 시점의 메인 창을 캡처하므로, 나중에 승격한 창은 프리뷰 대상에 반영되지 않는다. [프리뷰 문서](Preview.ko.md) 참고.

### 5.4 Application.Shutdown
`Application.Shutdown()`는 메시지 루프를 즉시 종료한다:

- 열린 창에는 `Closing` 콜백이 전달되지 **않으므로** 종료를 취소할 방법이 없다
- 이 경로에서는 창별 닫기 라이프사이클이 보장되지 않는다. 저장 확인이나 상태 보존을 `Closing`/`Closed` 핸들러에 의존하지 말 것
- 플랫폼 리소스가 정리되고 `Application.Run(...)`이 반환된다

`Shutdown(int exitCode)`는 `Environment.ExitCode`에도 대입한다. `Run`의 반환형은 `void`로 유지되므로, UI 스레드의 치명적 예외로 `Run`이 반환 대신 예외를 던지는 경우에도 종료 코드가 남는다.

```csharp
Application.Shutdown(2);   // 루프 종료, 프로세스 종료 코드 2
```

### 5.5 권장 패턴
- 기본: 사용자가 창을 닫게 두면 마지막 창과 함께 앱이 종료된다.
- 확인 절차를 존중해야 하는 "종료" 메뉴/버튼: `mainWindow.Close()`를 호출해 `Closing` 핸들러가 확인/취소를 결정하게 한다.
- 메인 창이 곧 앱 수명인 경우: 메인 창의 `Closed`에 `Application.Shutdown`를 구독한다.
- 확인 없이 즉시 종료(이미 저장된 상태, 워치독 재시작 등): `Application.Shutdown()`.

#### 예제: 메인 창 닫힘 = 앱 종료
앱 수명을 메인 창에 묶는 표준 레시피다. 모든 종료가 `main.Close()`를 거치므로 확인 절차가 한 곳에 모이고 취소가 그대로 존중된다. Shutdown은 닫기가 실제로 완료된 뒤에만 실행되므로, 도구/백그라운드 창이 남아 있어도 종료가 보장된다.

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
main.Closed += Application.Shutdown;

// 4) 모든 종료 명령은 Shutdown이 아니라 Close를 거친다.
new Button().Content("Exit").OnClick(() => main.Close());
```

조건 없는 즉시 종료(이미 저장된 상태, 워치독 재시작 등)만 `Application.Shutdown()`를 직접 호출한다. 이때 `Closing`/`Closed` 핸들러는 실행되지 않는다.

#### Close와 Shutdown의 순차 호출
```csharp
main.Close();
Application.Shutdown();
```

이 시퀀스는 "허용되면 곱게 닫되, 어쨌든 종료한다"는 의미다. 로그아웃, 치명 오류 후 종료, 업데이트 후 재시작 같은 흐름에 적합하다. 모든 플랫폼에서 `Close()`의 닫기 라이프사이클이 `Shutdown()`보다 먼저 실행된다(X11/macOS는 동기 실행, Win32는 post된 `WM_CLOSE`가 루프 종료 전에 드레인됨).

- `Closing`이 취소하지 않으면: `Closed` 정리가 실행된 뒤 앱이 종료된다.
- `Closing`이 취소하면: 종료는 그대로 진행되고, 그 창은 `Closed` 정리를 건너뛴 채 Shutdown 경로로 폐기된다.

동기 시퀀스는 어떤 `Closing` 핸들러도 deferral을 잡지 않을 때만 안전하다. deferral([5.6](#56-비동기-종료-closeasync와-closing-deferral) 참고)이 잡히면 `Close()`는 결정이 미해결인 채 반환되고, `Shutdown()`이 결정 전에 루프를 끝내버린다. `CloseAsync`는 같은 의도를 표현하면서 결정까지 기다린다:

```csharp
await main.CloseAsync();   // deferral 포함, 닫기 라이프사이클 완주
Application.Shutdown();        // 결과와 무관하게 종료
```

"무조건 종료" 흐름에는 이 형태를 우선한다.

의도에 따라 고른다: 확인 절차가 앱을 계속 살릴 수 있어야 하면 `main.Closed += Application.Shutdown` + `main.Close()`를, 무조건 종료해야 하면 이 시퀀스를 쓴다.

### 5.6 비동기 종료: CloseAsync와 Closing deferral
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
- 기본적으로 마지막 창이 닫힐 때 종료되며 `ShutdownMode`로 수명을 선택한다. `Window.Close()`가 정상(취소 가능) 경로, `Application.Shutdown()`는 즉시 종료 경로
