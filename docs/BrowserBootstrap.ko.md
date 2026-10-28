# 브라우저 부트스트랩 (WebAssembly)

이 문서는 MewUI 애플리케이션이 브라우저에서 실행되는 방식을 다룹니다. 템플릿으로 앱을 만드는 방법과, 앱이 파일을 고치거나 프로젝트를 직접 구성할 수 있도록 각 파일이 하는 일을 설명합니다. 실행이 시작되고 끝나는 방식은 [Application 수명 주기](ApplicationLifecycle.ko.md)의 2.5절을 참고합니다.

Gallery 브라우저 샘플은 같은 구조로 만든 더 큰 예제입니다: [samples/MewUI.Gallery.Browser](../samples/MewUI.Gallery.Browser).

---

## 1. 앱 만들기

WebAssembly 빌드 도구와 MewUI 템플릿을 장비마다 한 번 설치한 뒤 앱을 만들고 실행합니다:

```
dotnet workload install wasm-tools
dotnet new install Aprillz.MewUI.Templates
dotnet new mewui-browser -o MyBrowserApp
cd MyBrowserApp
dotnet run
```

템플릿이 만드는 파일은 다음과 같으며, 각각 아래 절에서 다룹니다:

| 파일 | 내용 | 절 |
|---|---|---|
| `MyBrowserApp.csproj` | WebAssembly 프로젝트와 브라우저 패키지 두 개 | 3 |
| `Program.cs` | 등록과 메인 창 | 4 |
| `BrowserExports.cs` | 페이지가 애플리케이션을 호출하는 진입점 | 5 |
| `wwwroot/index.html` | 캔버스, 숨은 텍스트 필드, 로딩 문구 | 6 |
| `wwwroot/main.js` | 런타임 시작, DOM 이벤트, 프레임 루프 | 6, 7, 8 |
| `Properties/launchSettings.json` | `dotnet run`이 앱을 제공하는 주소 | |

템플릿은 템플릿 패키지 자신과 같은 버전의 MewUI 패키지를 참조합니다.

---

## 2. 역할 분담

데스크톱 호스트는 창, 메시지 루프, 입력 큐를 소유합니다. 브라우저에서는 이 셋을 모두 페이지가 소유하므로 애플리케이션과 페이지가 일을 나눕니다:

| 소유자 | 담당 |
|---|---|
| 페이지 | 캔버스, 애니메이션 프레임 루프, DOM 이벤트, 리소스 fetch, 로딩 상태 숨기기 |
| MewUI | 레이아웃, WebGL을 통한 캔버스 렌더링, 입력 라우팅, 타이머, 테마 |

따라서 페이지가 애플리케이션을 호출하고, 애플리케이션이 페이지를 호출하는 경우는 클립보드 쓰기와 텍스트 필드를 캐럿 위치로 옮기는 두 가지뿐입니다.

---

## 3. 프로젝트 설정

템플릿의 프로젝트 파일은 다음과 같고, 직접 구성하는 프로젝트도 같은 설정이 필요합니다:

```xml
<Project Sdk="Microsoft.NET.Sdk.WebAssembly">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aprillz.MewUI.Platform.Browser" Version="MEWUI_VERSION" />
    <PackageReference Include="Aprillz.MewUI.Backend.MewVG.Browser" Version="MEWUI_VERSION" />
  </ItemGroup>

</Project>
```

- `MEWUI_VERSION`은 앱이 사용하는 MewUI 버전을 뜻합니다. 모든 MewUI 패키지는 같은 버전으로 발행되며, 두 패키지가 코어(`Aprillz.MewUI.Core`)를 함께 가져옵니다.
- `[JSImport]`와 `[JSExport]` 소스 생성기가 unsafe 코드를 만들므로 `AllowUnsafeBlocks`가 필요합니다.
- 백엔드 패키지는 작은 네이티브 WebGL shim을 앱의 런타임에 컴파일해 넣습니다. 백엔드가 호출하는 GL 진입점을 런타임이 스스로 내보내지 않기 때문입니다. WebGL 2 링크 플래그도 함께 추가합니다. 둘 다 `browser-wasm` 빌드에만 적용되며, 이 네이티브 단계 때문에 `wasm-tools` 워크로드가 필요합니다. 워크로드가 없으면 빌드가 워크로드 이름을 알려 주는 오류로 멈춥니다.
- 플랫폼 패키지는 `mewui-text-input.js`를 앱의 정적 파일 루트, 즉 페이지 옆에 추가하며 `main.js`가 이를 가져옵니다(8절). `dotnet run`과 `dotnet publish` 모두 그 위치에서 제공합니다.
- 페이지(`index.html`, `main.js`)와 export 클래스는 애플리케이션 코드이므로 패키지가 아니라 템플릿이 만듭니다. 직접 구성하는 프로젝트는 템플릿의 파일이나 Gallery 샘플의 [main.js](../samples/MewUI.Gallery.Browser/wwwroot/main.js), [BrowserExports.cs](../samples/MewUI.Gallery.Browser/BrowserExports.cs)에서 시작할 수 있습니다.

---

## 4. 시작

등록과 진입점은 일반적인 MewUI 코드입니다:

```csharp
BrowserPlatform.Register();
MewVGBrowserBackend.Register();

await Application.Create()
    .BuildMainWindow(() => new MainWindow())
    .RunAsync();
```

브라우저에서 `Application.Run`은 예외를 던집니다. `RunAsync`는 창이 표시되는 즉시 반환하고, 반환된 작업은 종료 시점에 완료됩니다.

브라우저 백엔드에는 `[SupportedOSPlatform("browser")]`가 지정되어 있습니다. 템플릿의 `Program.cs`는 앱 어셈블리에 같은 특성을 지정하므로, 플랫폼 분석기가 별도 분기 없이 호출을 허용합니다.

---

## 5. 페이지가 호출하는 export

페이지는 `[JSExport]` 메서드를 통해 애플리케이션에 접근합니다. 앱은 `BrowserPlatform`을 감싸는 얇은 래퍼를 선언하고, 플랫폼이 되호출하는 페이지 함수 두 개를 연결합니다:

```csharp
[SupportedOSPlatform("browser")]
internal static partial class BrowserExports
{
    [JSImport("writeClipboard", "main.js")]
    internal static partial void WriteClipboard(string text);

    [JSImport("moveTextInput", "main.js")]
    internal static partial void MoveTextInput(double x, double y, double height);

    static BrowserExports()
    {
        BrowserPlatform.ClipboardWriter = WriteClipboard;
        BrowserPlatform.CaretReporter = MoveTextInput;
    }

    [JSExport]
    internal static bool RenderFrame(double cssWidth, double cssHeight, double devicePixelRatio,
        int pixelWidth, int pixelHeight, double frameAgeMs)
        => BrowserPlatform.RenderFrame(cssWidth, cssHeight, devicePixelRatio, pixelWidth, pixelHeight, frameAgeMs);

    // 페이지가 쓰는 BrowserPlatform 멤버마다 래퍼 하나.
}
```

완전한 호스트가 쓰는 멤버는 다음과 같습니다:

| 묶음 | 멤버 |
|---|---|
| 프레임 | `RenderFrame`, `NextWakeDelayMs` |
| 마우스 | `PointerMove`, `PointerButton`, `PointerCancel()`, `PointerLeave` |
| 터치와 펜 | `PointerDown`, `PointerMove`, `PointerUp`, `PointerCancel(pointerId)` |
| 휠 | `PointerWheel` |
| 키보드 | `KeyDown`, `KeyUp`, `TextInput` |
| IME | `CompositionStart`, `CompositionUpdate`, `CompositionEnd`, `GetTextInputState`, `ReplaceText`, `WantsTextInput`, `SyncTextCaret` |
| 창 상태 | `FocusChanged`, `SetSystemDarkMode` |
| 클립보드 | `SetClipboardText`(붙여넣기), `ClipboardWriter`(복사) |
| 캐럿 | `CaretReporter` |

템플릿의 `main.js`는 `getAssemblyExports(config.mainAssemblyName)`로 얻은 객체에서 네임스페이스 경로로 이 클래스를 찾습니다. 클래스를 다른 어셈블리나 네임스페이스로 옮기면 그 줄도 함께 고쳐야 합니다.

애플리케이션이 색 구성표를 알 수 있는 경로는 `SetSystemDarkMode`뿐이므로, 페이지는 시작 시 한 번, 그리고 `prefers-color-scheme`가 바뀔 때마다 이를 알립니다.

---

## 6. 페이지

`index.html`에는 화면 전체 크기의 캔버스, 숨은 텍스트 필드, 로딩 문구가 있습니다:

```html
<canvas id="canvas"></canvas>
<input id="textinput" type="text" autocomplete="off" autocorrect="off" autocapitalize="off" spellcheck="false">
<div id="loader">Loading...</div>
<script type="module" src="./main.js"></script>
```

- `canvas { touch-action: none }`은 브라우저가 터치 드래그를 가져가지 못하게 합니다. 이 설정이 없으면 드래그가 중간에 `pointercancel`로 끝납니다.
- 텍스트 필드는 1픽셀 크기에 완전히 투명한 상태로 화면 안에 둡니다. 브라우저가 숨겨졌다고 판단한 필드는 IME 대상이 되지 않고, IME는 필드가 있는 위치에 후보 목록을 그리므로 `moveTextInput`이 필드를 캐럿 위치로 옮깁니다.
- 로딩 문구는 페이지가 직접 그립니다. 런타임 다운로드가 끝나기 전에는 managed 코드가 실행되지 않기 때문입니다. 템플릿의 `main.js`는 처음으로 무언가를 그린 프레임 뒤에 이 문구를 숨기고, 시작에 실패하면 그 자리에 실패 내용을 표시합니다.

`main.js`가 하는 일은 다섯 가지입니다:

1. **런타임 시작.** 자신의 쿼리 문자열을 이어 붙여 `./_framework/dotnet.js`를 가져온 뒤 `dotnet.create()`와 `getAssemblyExports`를 호출합니다.
2. **페이지 함수 등록.** `setModuleImports('main.js', { writeClipboard, moveTextInput })`를 호출합니다.
3. **캔버스 크기 맞추기.** 측정한 상자 크기에 `devicePixelRatio`를 곱한 값으로, 다시 그리는 프레임 안에서 맞춥니다. 백버퍼 크기를 바꾸면 내용이 지워지므로 다시 그리기 밖에서 바꾸면 화면이 깜빡입니다.
4. **DOM 이벤트 전달.** 이벤트를 export로 넘깁니다.
5. **프레임 루프 실행**(7절 참고).

---

## 7. 프레임 루프

```js
function frame(frameTimeMs) {
    const drew = app.RenderFrame(cssWidth, cssHeight, dpr, canvas.width, canvas.height,
        Math.max(0, performance.now() - frameTimeMs));
    idleFrames = drew ? 0 : idleFrames + 1;
    if (idleFrames < IDLE_FRAMES_BEFORE_SLEEP) {
        requestAnimationFrame(frame);
    } else {
        sleepUntilNextTimer();   // setTimeout(wake, app.NextWakeDelayMs())
    }
}
```

- `RenderFrame`은 무언가를 그렸는지 반환합니다. 그리지 않은 프레임이 몇 번 이어지면 루프가 멈추므로, 유휴 상태의 페이지는 비용이 들지 않습니다.
- `NextWakeDelayMs`는 다음 타이머까지 남은 시간을 반환하고, 예정된 타이머가 없으면 -1을 반환합니다. -1이면 페이지는 다음 이벤트를 기다립니다.
- 모든 이벤트 처리기는 전달하기 전에 `wake()`를 호출하므로, 입력이 멈춘 루프를 다시 시작합니다.
- 마지막 인자는 `requestAnimationFrame`이 넘겨준 타임스탬프로부터 지난 시간입니다. 프레임 안에서 시계를 읽으면 애니메이션 곡선이 계단처럼 끊깁니다.

---

## 8. 텍스트 입력과 IME

플랫폼은 텍스트 입력의 페이지 쪽 코드를 `mewui-text-input.js`로 제공합니다. 페이지는 이를 가져와 자신이 소유한 요소를 넘깁니다:

```js
const { createTextInputBridge } = await import('./mewui-text-input.js');
const textInputBridge = createTextInputBridge({ app, canvas, field: textInput, wake });
```

브리지는 5절의 export를 통해 조합 이벤트, 숨은 필드, 캐럿 위치를 처리합니다. 브리지를 쓰지 않는 호스트는 조합, 필드, 캐럿 배치를 직접 구현해야 합니다.

---

## 9. 리소스

브라우저에는 미리 읽어 둘 파일 시스템이 없으므로, 애플리케이션이 실행 중에 읽는 파일은 페이지가 fetch해서 넘깁니다. 템플릿은 아무것도 fetch하지 않으며, Gallery 샘플은 다음과 같이 합니다:

```js
for (const name of app.ResourceFileNames()) {
    const response = await fetch(`./Resources/${name}`);
    app.ApplyResource(name, new Uint8Array(await response.arrayBuffer()));
}
```

`ResourceFileNames`와 `ApplyResource`는 프레임워크 멤버가 아니라 애플리케이션 자신의 export입니다. 페이지에 무엇이 필요하고 받은 파일이 어디로 가는지는 앱이 정합니다. 이 백엔드는 브라우저의 글꼴로 텍스트를 그리므로 글꼴 파일은 fetch할 필요가 없습니다.

---

## 10. 게시

`dotnet publish`는 페이지 파일 옆에 `_framework`가 있는 `wwwroot`를 만듭니다. 이를 정적 파일로 제공하고, `.wasm`에는 `application/wasm`을 지정합니다.

캐시 고정이 중요합니다. `dotnet.js`는 SDK가 지문을 붙이지 않는 유일한 자산이고 다른 모든 자산의 해시 이름을 담고 있습니다. 이 파일을 캐시하는 호스트는 새 배포에 옛 목록을 넘기게 되고, 런타임은 더 이상 없는 파일을 요청합니다. 로더 스크립트에 버전 쿼리를 붙이면 템플릿의 `main.js`가 이를 런타임 import에 이어 붙입니다:

```html
<script type="module" src="./main.js?v=DEPLOY_ID"></script>
```

```js
const { dotnet } = await import(`./_framework/dotnet.js${new URL(import.meta.url).search}`);
```

AOT 컴파일(`-p:RunAOTCompilation=true`)은 다운로드 크기가 커지는 대신 첫 프레임이 빨라집니다.

---

## 11. 브라우저 호스트의 제약

- 최상위 `Window`는 하나입니다. 대화상자, 팝업, 메뉴는 캔버스 안에 그려집니다.
- `Window.ShowDialog`는 예외를 던지며, 모달은 `ShowDialogAsync`를 씁니다.
- 네이티브 메시지 상자와 파일 대화상자는 쓸 수 없습니다.
- 클립보드 읽기는 마지막 복사나 붙여넣기 동작에서 페이지가 넘긴 내용을 반환합니다.
- `Application.Shutdown()`은 실행을 끝내지만 페이지는 그대로 남습니다.

수명에 관한 세부 사항은 [Application 수명 주기](ApplicationLifecycle.ko.md)의 2.5절을 참고합니다.
