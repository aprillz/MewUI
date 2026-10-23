# 헤드리스 테스트

`Aprillz.MewUI.Headless`는 디스플레이 서버 없이 단위·통합 테스트에서 MewUI 창을 돌립니다. 창, 디스패처, 타이머, 레이아웃, 바인딩, 팝업이 실행 중인 앱과 같게 동작하고, 입력은 테스트가 넣으며, 렌더 모드를 켜면 매 프레임을 오프스크린 서피스에 그려 픽셀을 읽을 수 있습니다.

이 패키지는 Avalonia.Headless를 모델로 합니다. API는 명시적 호출뿐이고 테스트 프레임워크 특성과 리플렉션을 쓰지 않으므로, 테스트 프로젝트를 트리밍과 NativeAOT로 게시할 수 있습니다.

---

## 1. 시작하기

패키지와 그래픽 백엔드 하나를 참조합니다. 글꼴 측정은 백엔드가 맡으므로 헤드리스 테스트의 레이아웃은 실제 앱과 같은 글꼴 지표로 계산됩니다:

| 운영체제 | 백엔드 패키지 | 등록 호출 |
|---|---|---|
| Windows | `Aprillz.MewUI.Backend.Gdi` 또는 `Aprillz.MewUI.Backend.Direct2D` | `GdiBackend.Register()` / `Direct2DBackend.Register()` |
| Linux | `Aprillz.MewUI.Backend.MewVG.X11` | `MewVGX11Backend.Register()` |
| macOS | `Aprillz.MewUI.Backend.MewVG.MacOS` | `MewVGMacOSBackend.Register()` |

플랫폼 패키지(`Aprillz.MewUI.Platform.*`)는 참조하지 않습니다. 헤드리스 패키지가 플랫폼 호스트입니다. 백엔드는 프로세스당 하나를 세션 시작 전에 등록합니다.

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;

GdiBackend.Register();

using var session = HeadlessSession.Start(new HeadlessOptions
{
    Rendering = HeadlessRendering.MeasureOnly,   // 또는 Offscreen
    Dpi = 96,
    Clock = HeadlessClock.Virtual,               // 또는 RealTime
});

var button = new Button().Content("OK");
int clicks = 0;
button.Click += () => clicks++;

var window = session.Run(() =>
{
    var created = new Window { WindowSize = WindowSize.Resizable(400, 300), Content = button };
    created.Show();
    return created;
});

window.SendClick(button.CenterInWindow());
Debug.Assert(clicks == 1);
```

`HeadlessSession.Start`는 전용 UI 스레드에서 실제 `Application`을 띄우고, 앱 루프가 돌기 시작하면 돌아옵니다. 세션은 프로세스당 한 번에 하나만 있을 수 있고, 세션을 Dispose하면 모든 창을 닫고 실행을 끝내고 스레드를 놓으므로 그 뒤에 새 세션을 시작할 수 있습니다.

Linux에서는 `DISPLAY` 없이 돕니다. MewVG 백엔드는 FreeType으로 글꼴을 재고 측정 전용 모드에서는 X 연결을 열지 않으며, 렌더 모드는 서피스 없는 EGL 컨텍스트에 그립니다(7절). macOS에서는 렌더 모드까지 SSH 세션만으로 충분합니다.

---

## 2. UI 스레드에서 코드 실행

요소를 만지는 코드는 모두 세션의 UI 스레드에서 돕니다. `Run`은 델리게이트를 그 스레드에서 실행하고 결과를 돌려주며, 안에서 던진 예외는 호출자에게 다시 던져집니다:

```csharp
session.Run(() => textBox.Focus());
string text = session.Run(() => textBox.Text);
```

`RunAsync`는 비동기 델리게이트를 받고, 안의 모든 `await`는 UI 스레드로 돌아옵니다. `await`하는 뷰모델 코드가 기대하는 그대로입니다:

```csharp
await session.RunAsync(async () =>
{
    await viewModel.LoadAsync();
    Assert.Equal(3, listBox.ItemsSource.Count);
});
```

UI 스레드에서 `Run`을 부르면 델리게이트를 그 자리에서 실행하므로 도우미를 어느 쪽에서든 쓸 수 있습니다.

---

## 3. 창

창은 앱에서처럼 `Show()`로 띄웁니다. 헤드리스 호스트는 네이티브 창이 없는 창 백엔드를 붙입니다. 크기는 `WindowSize`(또는 창 기본값)에서, DPI는 `HeadlessOptions.Dpi`에서 오고, `ClientToScreen`은 항등이라 화면 좌표가 창 좌표와 같습니다.

팝업(`ComboBox` 드롭다운, 컨텍스트 메뉴, 툴팁)은 소유 창의 표면 안에서 열립니다. 디스플레이 없이는 네이티브 팝업 창을 만들 수 없기 때문입니다. 다른 내용처럼 히트 테스트되고 입력으로 닫힙니다.

모달 대화상자(`ShowDialog`)는 같은 펌프 위에서 중첩 루프로 돌므로, 한 `Run`에서 대화상자를 열고 다른 `Run` 호출로 조작할 수 있습니다.

---

## 4. 입력

입력은 `Window`의 확장 메서드로 보냅니다. 플랫폼 백엔드가 쓰는 것과 같은 `WindowInputRouter` 진입점을 부르므로 히트 테스트, 마우스 오버, 포커스, 팝업 닫기 정책, 이벤트 버블링이 실행 중인 앱과 똑같이 동작합니다. 원시 입력 이벤트를 합성하는 Avalonia.Headless와 다른 점입니다. 각 메서드는 입력 뒤 대기 중인 디스패처 작업을 비우므로(`RunJobs`) 테스트의 다음 줄이 결과를 봅니다.

| 메서드 | 동작 |
|---|---|
| `SendMouseMove(point)` | 포인터를 옮기고 마우스 오버·호버 상태를 갱신합니다. |
| `SendMouseDown(point, button)` / `SendMouseUp(point, button)` | 그 지점에서 버튼을 누르거나 뗍니다. |
| `SendClick(point, button)` | 이동, 누름, 뗌을 클릭 횟수 1로 보냅니다. |
| `SendDoubleClick(point, button)` | 클릭 뒤에 클릭 횟수 2의 누름과 뗌을 보냅니다. |
| `SendMouseWheel(point, delta)` | 휠을 노치 단위로 돌립니다. 양의 Y가 위로 스크롤입니다. |
| `SendKeyDown(key, modifiers)` / `SendKeyUp(key, modifiers)` | 프리뷰, 포커스 요소부터의 버블링, 액세스 키, 처리되지 않은 Tab의 포커스 이동을 플랫폼이 올리는 순서대로 수행합니다. |
| `SendKeyPress(key, modifiers)` | 키 누름 뒤 키 뗌입니다. |
| `SendTextInput(text)` | 포커스된 텍스트 입력에 글자를 전달합니다. 키 이벤트 뒤에 타이핑이 하는 그대로입니다. |
| `SendDragDrop(point, data, effects)` | 외부 드래그가 그 지점에 들어오고, 지나가고, 떨어뜨리는 과정입니다. 대상이 고른 효과를 돌려줍니다. |

좌표는 창 클라이언트 좌표의 DIP입니다. `element.CenterInWindow()`는 요소의 배치된 사각형 중심을 돌려주므로 컨트롤 클릭에 그대로 쓰면 됩니다. 메서드는 테스트 스레드에서 불러도 되고, UI 스레드로 넘긴 뒤 입력이 처리되면 돌아옵니다.

키 이벤트는 플랫폼 키 코드와 글자를 싣지 않습니다. 입력된 글자가 필요한 테스트는 키 뒤에 `SendTextInput`을 보냅니다. 모든 플랫폼이 글자를 그렇게 전달합니다. 터치와 펜 입력은 흉내 내지 않습니다.

---

## 5. 작업과 시간

`session.RunJobs()`는 디스패처 큐와 만료된 타이머를 비운 다음 프레임을 요청한 모든 창을 레이아웃하고 그리며, 남는 것이 없을 때까지 반복합니다. 입력 메서드가 대신 불러 주고, `BeginInvoke`·바인딩 변경·애니메이션 시작 뒤에는 직접 부릅니다.

기본값 `HeadlessClock.Virtual`에서는 시간이 저절로 가지 않습니다. `session.AdvanceTime(TimeSpan)`이 시계를 옮기면서 도중의 모든 타이머와 애니메이션 프레임을 돌리고, 타이머의 만료 시각마다 멈추므로 다시 예약하는 타이머도 제 간격대로 불립니다:

```csharp
var timer = session.Run(() =>
{
    var created = new DispatcherTimer(TimeSpan.FromMilliseconds(300));
    created.Tick += () => ticks++;
    created.Start();
    return created;
});

session.AdvanceTime(TimeSpan.FromMilliseconds(299));   // ticks == 0
session.AdvanceTime(TimeSpan.FromMilliseconds(1));     // ticks == 1
```

애니메이션도 같은 시계로 펄스됩니다. 세션에서 시작한 `AnimationClock`은 `AdvanceTime`으로만, 최대 16 ms 프레임 단위로 진행합니다. 애니메이션 시계의 일시 정지와 재개 사이의 시간은 실제 시계로 잽니다.

`HeadlessClock.RealTime`은 앱에서처럼 타이머를 실제 시계로 만료시키고, 그런 세션에서는 `AdvanceTime`이 거부됩니다.

---

## 6. 서비스

앱이 보는 플랫폼 서비스는 세션이 갖고 있으므로, 대화상자가 뜨는 대신 테스트가 답을 미리 정합니다:

| 멤버 | 동작 |
|---|---|
| `session.Clipboard` | 메모리 클립보드입니다. `Text`로 읽고 쓰며, `Application.Current.PlatformServices.Clipboard`가 같은 값을 봅니다. |
| `session.MessageBoxes` | `Enqueue(bool?)`가 다음 `NativeMessageBox.Show`의 답(true, false, 취소는 null)을 정합니다. 큐가 비면 취소로 답합니다. `Calls`에 본문, 캡션, 버튼, 결과가 요청마다 기록됩니다. |
| `session.FileDialogs` | `EnqueueOpen(paths)`, `EnqueueSave(path)`, `EnqueueFolder(path)`가 다음 네이티브 파일 대화상자의 답을 정합니다. 큐가 비면 취소로 답합니다. `Calls`에 요청이 기록됩니다. |

셸 아이콘과 마운트된 볼륨은 비어 있고, 시스템 테마는 `HeadlessOptions.SystemTheme`(기본 밝은 테마)입니다.

---

## 7. 렌더 모드

`HeadlessRendering.MeasureOnly`(기본값)는 레이아웃과 렌더 패스를 돌리되 모든 그리기 호출을 버립니다. 테스트가 빠르고 백엔드에 매이지 않습니다.

`HeadlessRendering.Offscreen`은 매 프레임을 등록된 백엔드의 오프스크린 서피스에 그립니다. `window.CaptureFrame()`이 마지막 프레임의 픽셀을 `HeadlessFrame`으로 돌려줍니다. 장치 픽셀과 바이트 단위의 `Width`, `Height`, `Stride`, `DpiScale`, BGRA32 `Pixels` 스팬, 한 점의 색을 주는 `GetPixel(x, y)`가 있습니다:

```csharp
using var session = HeadlessSession.Start(new HeadlessOptions { Rendering = HeadlessRendering.Offscreen });
...
HeadlessFrame frame = window.CaptureFrame();
Assert.Equal(Color.FromArgb(255, 40, 120, 200), frame.GetPixel(30, 30));
```

렌더 모드는 Windows(GDI, Direct2D), macOS(MewVG Metal), Linux(MewVG)에서 제공됩니다. 프레임은 화면의 창이 쓰는 것과 같은 리테인드 경로로 그려지므로, 변경 뒤 캡처한 프레임은 사용자가 보게 될 재페인트 결과 그대로입니다.

Linux에서 MewVG 백엔드는 Mesa의 서피스 없는 EGL 플랫폼(`EGL_MESA_platform_surfaceless`)의 OpenGL 컨텍스트에 프레임을 그리므로 X 서버, Wayland 컴포지터, DRM 장치가 모두 필요 없습니다. 장비에는 Mesa의 `libEGL.so.1`과 `libGL.so.1`, 그리고 데스크톱 OpenGL 3.1 이상을 제공하는 드라이버가 있어야 하고, GPU가 없는 장비에서는 Mesa의 llvmpipe 래스터라이저가 그 역할을 합니다(Debian·Ubuntu: `libegl1`, `libgl1`, `libgl1-mesa-dri`). 서피스 없는 플랫폼이 없으면 `HeadlessSession.Start`가 무엇이 없는지 밝히는 `PlatformNotSupportedException`을 던집니다. GPU가 있으면 Mesa가 렌더 노드(`/dev/dri/renderD*`)로 그 GPU에 그리고, 테스트가 GPU에 의존하지 않아야 하면 `LIBGL_ALWAYS_SOFTWARE=1`로 llvmpipe를 강제할 수 있습니다.

---

## 8. 테스트 프레임워크 픽스처

패키지에는 프레임워크 어댑터가 없습니다. 픽스처가 테스트 어셈블리마다 세션을 한 번 시작하고, 테스트는 `session.Run`을 씁니다. 세션은 프로세스 단위이므로 세션을 공유하는 테스트 클래스는 병렬로 돌리지 않습니다.

### xUnit

```csharp
public sealed class HeadlessFixture : IDisposable
{
    public HeadlessFixture()
    {
        GdiBackend.Register();
        Session = HeadlessSession.Start();
    }

    public HeadlessSession Session { get; }

    public void Dispose() => Session.Dispose();
}

[CollectionDefinition("headless", DisableParallelization = true)]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessFixture>
{
}

[Collection("headless")]
public sealed class ButtonTests(HeadlessFixture fixture)
{
    [Fact]
    public void Click_RaisesClick()
    {
        var button = new Button().Content("OK");
        int clicks = 0;
        button.Click += () => clicks++;
        var window = fixture.Session.Run(() =>
        {
            var created = new Window { Content = button };
            created.Show();
            return created;
        });

        window.SendClick(button.CenterInWindow());

        Assert.Equal(1, clicks);
        fixture.Session.Run(window.Close);
    }
}
```

### NUnit

```csharp
[SetUpFixture]
public sealed class HeadlessSetup
{
    public static HeadlessSession Session { get; private set; } = null!;

    [OneTimeSetUp]
    public void Start()
    {
        GdiBackend.Register();
        Session = HeadlessSession.Start();
    }

    [OneTimeTearDown]
    public void Stop() => Session.Dispose();
}

[TestFixture, NonParallelizable]
public sealed class ButtonTests
{
    [Test]
    public void Click_RaisesClick()
    {
        var session = HeadlessSetup.Session;
        ...
    }
}
```

### MSTest

```csharp
[TestClass]
public static class AssemblyFixture
{
    public static HeadlessSession Session { get; private set; } = null!;

    [AssemblyInitialize]
    public static void Start(TestContext context)
    {
        GdiBackend.Register();
        Session = HeadlessSession.Start();
    }

    [AssemblyCleanup]
    public static void Stop() => Session.Dispose();
}
```

MSTest는 `[assembly: Parallelize]`를 선언하지 않으면 클래스를 순서대로 돌립니다. `[assembly: DoNotParallelize]`로 명시할 수 있습니다.

### 운영체제별 백엔드 선택

모든 운영체제에서 도는 테스트 어셈블리는 실행 중인 운영체제의 백엔드를 등록합니다:

```csharp
if (OperatingSystem.IsWindows()) GdiBackend.Register();
else if (OperatingSystem.IsMacOS()) MewVGMacOSBackend.Register();
else MewVGX11Backend.Register();
```

---

## 9. Avalonia.Headless와 비교

| Avalonia.Headless | MewUI.Headless | 비고 |
|---|---|---|
| `HeadlessUnitTestSession.StartNew(typeof(App))` | `HeadlessSession.Start(options)` | MewUI는 자체 스레드에서 실제 `Application`을 띄우므로 앱 클래스가 필요 없습니다. |
| `session.Dispatch(...)` | `session.Run(...)`, `session.RunAsync(...)` | `RunAsync`는 await를 UI 스레드에 붙들어 둡니다. |
| `Dispatcher.UIThread.RunJobs()` | `session.RunJobs()` | 대기 중인 창의 레이아웃과 그리기까지 합니다. |
| `AvaloniaHeadlessPlatform.ForceRenderTimerTick()` | `session.AdvanceTime(...)` / `RunJobs()` | 타이머와 애니메이션이 기본으로 가상 시계에서 돕니다. |
| `UseHeadlessDrawing = true` / `false` | `HeadlessRendering.MeasureOnly` / `Offscreen` | 측정 전용도 실제 백엔드로 글꼴을 잽니다. |
| `window.CaptureRenderedFrame()` | `window.CaptureFrame()` | 스트라이드와 DPI 배율이 있는 BGRA32 픽셀입니다. |
| `MouseDown`, `MouseMove`, `MouseUp`, `MouseWheel` | `SendMouseDown`, `SendMouseMove`, `SendMouseUp`, `SendMouseWheel`, 추가로 `SendClick`과 `SendDoubleClick` | MewUI는 창 입력 라우터를 거치므로 포커스, 호버, 팝업 정책이 실제 그대로입니다. |
| `KeyPress`, `KeyRelease`, `KeyTextInput` | `SendKeyDown`, `SendKeyUp`, `SendKeyPress`, `SendTextInput` | 키 누름에 액세스 키와 Tab 포커스 이동이 포함됩니다. 모든 플랫폼 백엔드가 그렇게 합니다. |
| `DragDrop(point, type, data, effects)` | `SendDragDrop(point, data, effects)` | 한 호출이 들어옴, 지나감, 떨어뜨림을 수행합니다. |
| `[AvaloniaFact]`, `[AvaloniaTest]` | 8절의 픽스처 | 프레임워크 어댑터와 리플렉션이 없습니다. |
| 헤드리스 클립보드, 대화상자 없음 | `session.Clipboard`, `session.MessageBoxes`, `session.FileDialogs` | 대화상자의 답을 미리 정하고 기록합니다. |
