# 빌드 스위치

이 문서는 MewUI 기능을 켜고 끄는 MSBuild 속성, 각 속성이 설정하는 런타임 스위치, 그리고 트림·NativeAOT 게시에 미치는 영향을 정리한다.

---

## 1. 동작 방식

모든 스위치는 앱 프로젝트의 MSBuild 속성이다. 빌드 로직은 `Aprillz.MewUI.Core` 패키지에 들어 있으므로 어떤 MewUI 패키지 조합에서도 동작한다.

```xml
<PropertyGroup>
  <MewUIWin32TextEngine>Gdi</MewUIWin32TextEngine>
</PropertyGroup>
```

명령줄로도 전달할 수 있다: `dotnet publish -p:MewUIWin32TextEngine=Gdi`.

빌드 시 각 속성은 `runtimeconfig.json`에 `AppContext` 스위치로 기록되고, MewUI는 시작할 때 그 스위치를 한 번 읽는다. 대부분의 스위치는 트리머에도 전달되므로, 트림·NativeAOT 게시에서는 선택하지 않은 쪽의 코드가 비활성화되는 데 그치지 않고 출력에서 제거된다.

| 속성 | 값 | 기본값 | 런타임 스위치 | 끄면 트림으로 제거 |
|---|---|---|---|---|
| `MewUIBackend` | `Direct2D`, `Gdi`, `MewVG` | 모든 백엔드 | 없음 (게시 필터) | 해당 없음 |
| `MewUIWin32TextEngine` | `DirectWrite`, `Gdi` | `DirectWrite` | `Aprillz.MewUI.Win32.DirectWriteText.Enabled` | 예, 선택하지 않은 경로 |
| `MewUIManagedFileDialogs` | `true`, `false` | `true` | `Aprillz.MewUI.ManagedFileDialogs.Enabled` | 예 |
| `MewUIDevTools` | `true`, `false` | Debug는 `true`, Release는 `false` | `Aprillz.MewUI.DevTools.Enabled` | 예 |
| `MewUIHotReload` | `true`, `false` | `true` | `Aprillz.MewUI.HotReload.Enabled` | 아니오 |
| `MewUIEnvironmentDebugLogging` | `true`, `false` | Debug는 `true`, Release는 `false` | `Aprillz.MewUI.EnvironmentDebugLogging.Enabled` | 예 |

런타임 스위치가 아니라 MSBuild 속성을 설정한다. `runtimeconfig.json`을 직접 고치거나 `AppContext.SetSwitch`로 바꾼 값은 트리머에 전달되지 않으므로, 트림 게시에서는 그 스위치가 요구하는 코드가 이미 제거되어 있을 수 있다.

---

## 2. MewUIBackend

메타패키지를 참조하는 앱을 게시할 때 렌더링 백엔드 하나만 남긴다. 값과 예시는 [설치 및 패키지 구성](Installation.ko.md)을 참고한다.

---

## 3. MewUIWin32TextEngine

Windows에서는 모든 백엔드가 DirectWrite로 텍스트를 배치하고 래스터화한다. `Gdi`와 `MewVG` 백엔드는 이 속성을 `Gdi`로 지정하면 GDI 텍스트를 쓴다. `Direct2D` 백엔드는 항상 DirectWrite를 쓰므로 이 속성을 보지 않으며, Linux와 macOS에는 영향이 없다.

```xml
<PropertyGroup>
  <MewUIWin32TextEngine>Gdi</MewUIWin32TextEngine>
</PropertyGroup>
```

| 값 | 글꼴 매칭 | 컬러 글리프 |
|---|---|---|
| `DirectWrite` (기본값) | 타이포그래픽 패밀리 이름. 한 패밀리의 모든 굵기를 쓸 수 있다 | 있음 (컬러 이모지) |
| `Gdi` (`Gdi`, `MewVG` 백엔드만) | 레거시 패밀리 이름 | 없음 |

그 밖의 값을 주면 빌드가 오류로 실패한다.

`Gdi`로 NativeAOT 게시한 앱은 `DirectWrite`로 게시한 같은 앱보다 약 70~95 KB 작다. 텍스트를 그리지 않는 앱은 어느 쪽이든 크기가 같다.

---

## 4. MewUIManagedFileDialogs

MewUI에는 매니지드 파일·폴더 대화상자가 들어 있고, `PreferNative`가 `false`이거나 네이티브 대화상자를 쓸 수 없을 때 사용된다. 네이티브 대화상자만 쓰는 앱은 이 속성을 `false`로 두어 트림·NativeAOT 게시에서 매니지드 대화상자를 제거할 수 있다.

매니지드 대화상자를 끄면 네이티브 대화상자의 실패는 대체 없이 호출자에게 예외로 전달되고, `PreferNative = false` 요청은 `NotSupportedException`을 던진다.

---

## 5. MewUIDevTools

요소 인스펙터, 비주얼 트리 창, 프레임 통계 오버레이, 프로파일러를 켠다. [DevTools](DevTools.ko.md)를 참고한다.

이 도구들은 트림·NativeAOT 게시에 포함되지 않는다. 속성 패널이 리플렉션으로 멤버를 나열하는데 트림이 이를 깨뜨리기 때문이다. `MewUIDevTools`를 `true`로 둔 채 그런 앱을 게시하면 도구 없이 게시되고 빌드 경고가 나온다. 같은 앱이라도 `dotnet run`에서는 도구가 동작한다.

---

## 6. MewUIHotReload

Hot Reload는 선언 없이 `dotnet watch`나 IDE의 Hot Reload 세션에서 동작한다. 끄려면 속성을 `false`로 둔다. [Hot Reload](HotReload.ko.md)를 참고한다.

---

## 7. MewUIEnvironmentDebugLogging

실행 시 환경 변수로 켜는 진단 로그를 사용할 수 있게 한다. Release에서는 이 속성을 `true`로 두지 않는 한 로깅 코드가 포함되지 않는다.

앱을 시작하기 전에 변수를 `1` 또는 `true`로 설정한다. 로그는 표준 출력과 디버거 출력으로 나간다.

| 변수 | 로그 내용 |
|---|---|
| `MEWUI_GRAPHICS_DEBUG` | 그래픽 컨텍스트 상태 |
| `MEWUI_IME_DEBUG` | 모든 플랫폼의 입력기 동작 |
| `MEWUI_X11_DEBUG` | X11 오류 |
| `MEWUI_XI2_DEBUG` | XInput2 이벤트 |
| `MEWUI_ICON_DEBUG` | Linux 셸 아이콘 조회 |
| `MEWUI_MACOS_DEBUG` | macOS 창 클래스 등록 |
| `MEWUI_INTEROP_DEBUG` | macOS 네이티브 인터롭 |
