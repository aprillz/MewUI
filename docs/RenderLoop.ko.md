# 렌더 루프 개념

이 문서는 MewUI의 렌더 루프 모델을 설명한다: 렌더링이 메시지 루프/레이아웃과 어떤 순서로
스케줄되는지, 연속(continuous)/요청 기반(on-request) 렌더링을 어떻게 제어하는지, 각 백엔드가
그 제어를 present/vsync 메커니즘에 어떻게 매핑하는지를 다룬다. 백엔드/플랫폼 동작과 스케줄링
기준을 이해하기 위한 내부 문서다.

---

## 1. 목표

- 메시지/레이아웃 처리와 렌더링을 분리하여 UI 응답성을 유지한다.
- 다음 두 방식을 모두 지원한다:
  - **요청 기반 렌더링** (기본값): 무언가 창을 invalidate했을 때만 렌더한다.
  - **연속 렌더링**: 매 루프마다 모든 창을 렌더한다. 애니메이션/Max FPS 시나리오용.
- Win32/X11/macOS 플랫폼 호스트와 Direct2D, MewVG(OpenGL/Metal), GDI 그래픽 백엔드 전반에서
  일관된 vsync 제어를 제공한다.

---

## 2. RenderLoopSettings

루프는 `Application.Current.RenderLoopSettings`(`Aprillz.MewUI.RenderLoopSettings`)로 설정한다:

- `Continuous` (`bool`) - 연속 렌더링을 강제하는 사용자 플래그. 연속 렌더링을 직접 요청하는
  유일한 방법이며, 애니메이션 시스템은 클록이 동작하는 동안 스스로 별도 요청을 건다(아래 참조).
  `SetContinuous(bool)`은 동일 플래그에 대한 편의 setter다.
- `TargetFps` (`int`) - 연속 렌더링의 프레임 상한. 기본값 `0`은 무제한을 뜻한다.
- `VSyncEnabled` (`bool`) - 백엔드 present/swap 동작. 기본값 `true`. 끄면 vsync로 페이싱할
  기준이 없어지므로 연속 렌더링도 함께 강제된다(아래 `IsContinuous` 참조).
- `IsContinuous` (`bool`, 읽기 전용) - 루프가 연속으로 동작해야 하면 `true`:

  ```
  IsContinuous = !VSyncEnabled || Continuous || AnimationActive
  ```

  플랫폼 호스트가 매 루프 반복마다 읽는 단일 값이며, 루프 모드를 직접 설정하는 곳은 없다.
- `AnimationActive` (internal) - `AnimationManager`가 구동: 애니메이션 클록이 하나라도
  활성화되어 있으면 설정되고, 유휴 상태가 되면 해제된다. 사용자가 직접 만지는 값이 아니다.

별도의 "모드" enum은 없다 - 요청 기반과 연속은 전적으로 계산된 `IsContinuous` 값으로 갈린다.
기본값(`Continuous = false`, `VSyncEnabled = true`, 활성 애니메이션 없음)에서 `IsContinuous`는
`false`, 즉 요청 기반 렌더링이다.

Gallery 샘플의 "Max FPS" 토글 실제 코드:

```csharp
var scheduler = Application.Current.RenderLoopSettings;
scheduler.TargetFps = 0;
scheduler.VSyncEnabled = !maxFpsEnabled;
scheduler.SetContinuous(maxFpsEnabled);
```

---

## 3. 요청 기반 vs 연속 스케줄링

Win32/X11 플랫폼 호스트(`Win32PlatformHost`, `X11PlatformHost`)는 프로세스당 단일
메시지/렌더 루프(`PumpLoop`)를 실행하며, 등록된 모든 창이 이 루프를 공유한다 - 창별 OS
루프는 없다. 매 반복마다 `RenderLoopSettings.IsContinuous` 값으로 분기한다:

### 3.1 요청 기반 (`IsContinuous == false`)

- 루프는 OS 메시지가 도착하거나 어떤 창이 렌더를 요청할 때까지 대기한다(Win32는
  `MsgWaitForMultipleObjectsEx`, X11은 그에 대응하는 대기).
- 깨어나면 백엔드의 `NeedsRender == true`인 창만 렌더하고(`RenderIfNeeded`), 이어서 대기 중인
  OS 메시지를 처리한다.
- 메시지 처리 중 새로 invalidate된 창이 있어 여전히 렌더가 필요하면, 다음 반복을 위해 렌더
  요청 이벤트를 다시 세팅한다.
- 다음 깨어남 전의 여러 invalidate는 하나의 렌더로 합쳐진다 - WPF 스타일의 렌더 요청 병합과
  유사하다.

### 3.2 연속 (`IsContinuous == true`)

- 매 반복마다 대기 메시지를 처리한 뒤, invalidate 여부와 무관하게 **모든** 창을
  렌더한다(`RenderNow`).
- `TargetFps > 0`이면 렌더 후 남은 프레임 예산만큼 대기한다. `TargetFps == 0`이면 루프가 도는
  대로 최대한 빠르게 렌더한다.
- 애니메이션(위 `AnimationActive` 참조)과 "Max FPS" 프로파일링 토글이 이 방식에 의존한다.

macOS 플랫폼 호스트(`MacOSPlatformHost`)도 동일한 `NeedsRender` / `RenderIfNeeded` /
`RenderNow` / `IsContinuous` / `TargetFps` 계약을 구현한다.

---

## 4. 메시지 루프, Update Pass, 렌더

창의 invalidate와 렌더는 UI 디스패처의 우선순위 큐를 거쳐 진행된다(`DispatcherPriority`:
`Idle` < `Background` < `Render` < `Layout` < `Input` < `Normal`, 값이 클수록 먼저 실행).
디스패처 드레인(`DispatcherQueue.Process`)은 큐를 높은 우선순위부터 낮은 순으로 처리하므로,
`Layout` 우선순위 액션이 그 안에서 `Render` 우선순위로 큐잉한 항목도 같은 드레인 안에서
처리된다:

- `UIElement.InvalidateVisualState()`는 해당 요소를 창에 큐잉한다
  (`Window.RegisterVisualStateDirty`), 이후 조정(reconcile) 대상이 된다.
- `Window.InvalidateMeasure()` / `InvalidateArrange()`는 `RequestUpdatePass()`를 호출하며,
  이는 `DispatcherPriority.Layout`으로 병합 액션을 포스팅한다. 이 액션은 `Window.PerformLayout()`
  을 실행한다 - 큐잉된 visual-state invalidation을 조정하고(`UpdateVisualStates()`), 창
  자신의 스타일을 해석하고, 템플릿을 적용한 뒤 measure/arrange를 수행하며, 마지막으로
  `RequestRender()`를 호출한다.
- `RequestRender()`(internal)는 `DispatcherPriority.Render`로 병합 액션을 포스팅하여
  `_backend.Invalidate(true)`를 호출한다 - 백엔드의 `NeedsRender` 플래그를 세우고 플랫폼
  호스트에 렌더 루프를 깨우도록 요청한다.
- `Window.Invalidate()`와 `Window.InvalidateVisual()`(공개 API)은 둘 다 레이아웃 단계를
  건너뛰고 `RequestRender()`를 바로 호출한다 - 레이아웃이 이미 최신임을 알고 있을 때 쓴다.

따라서 프레임당 실질적인 순서는: **OS/디스패처 메시지 -> update pass(뭔가 dirty할 때만
visual-state 조정, 이어서 measure/arrange) -> 플랫폼 호스트의 렌더 호출**이다. 실제 렌더
내부(`Window.RenderFrame` -> `RenderFrameCore`)에서는 그리기 순회 전에 `AnimationManager
.Instance.Update()`가 먼저 실행되어 활성 애니메이션 클록을 진행시키므로, 해당 프레임의
페인트에는 갱신된 애니메이션 값이 반영된다.

### Win32 특이사항

- `WM_PAINT`는 여전히 처리된다(`Win32WindowBackend.HandlePaint`) - `BeginPaint`/`EndPaint`
  안에서 즉시 렌더하며(픽셀 알파 창은 `UpdateLayeredWindow` 경로), 플랫폼 호스트 자신의
  요청 기반 렌더는 `WM_PAINT`를 강제로 발생시키지 않는다. present 후에는 `ValidateRect`를
  호출해 불필요한 재도장을 막는다.
- OS 주도 모달 루프(대화형 리사이즈/이동, 네이티브 메뉴 - `WM_ENTERSIZEMOVE` /
  `WM_ENTERMENULOOP`)는 `DefWindowProc` 내부에서 돌며 `PumpLoop`를 정지시킨다. 그동안에도
  애니메이션과 대기 중인 invalidate가 진행되도록, 백엔드는 진입 시 `WM_TIMER`(8ms)를 걸고
  `WM_EXITSIZEMOVE` / `WM_EXITMENULOOP`에서 해제한다.

---

## 5. 백엔드별 Vsync 동작

| 백엔드 | VSyncEnabled = true | VSyncEnabled = false |
|---|---|---|
| Direct2D (스왑체인 present) | DXGI `Present` sync interval `1` | sync interval `0` |
| Direct2D (HwndRenderTarget present) | `D2D1_PRESENT_OPTIONS.NONE` | `D2D1_PRESENT_OPTIONS.IMMEDIATELY` |
| MewVG / OpenGL (Win32 WGL, X11 GLX/EGL) | swap interval `1` | swap interval `0` |
| MewVG / Metal (macOS) | `CAMetalLayer.displaySyncEnabled = true` | `displaySyncEnabled = false` |
| GDI | 영향 없음 - GDI에는 vsync 개념이 없음 | 영향 없음 |

GDI도 요청 기반/연속 스케줄링에는 그대로 참여한다(그 메커니즘은 그래픽 백엔드가 아니라
플랫폼 호스트에 있다) - 다만 `VSyncEnabled`가 제어할 대상이 없을 뿐이다.

---

## 6. FPS 및 프레임 진단

- `Window.FrameRendered`(`event Action?`)는 두 스케줄링 방식 모두에서 프레임이 렌더될 때마다
  발생한다. `Window.FirstFrameRendered`는 최초로 프레임이 렌더될 때 한 번만 발생한다.
- `Window.LastFrameStats`(`RenderStats`)는 직전 프레임의 `DrawCalls`, `CullCount`,
  `RenderedCalls`, `CullRatio`를 담는다.
- Sample/Gallery는 `FrameRendered`로 1초 구간의 프레임 수를 누적해 FPS를 계산/표시하고,
  `LastFrameStats`로 draw/cull 카운트를 보여준다.

---

## 7. 설계 메모

- 렌더링을 invalidate와 분리해 두어, 연속/Max FPS 모드는 "모든 창을 항상 렌더가 필요한
  것으로 취급"하는 것만으로 구현되고 별도 코드 경로가 필요 없다.
- 요청 기반 렌더는 병합 디스패처 포스트와 `NeedsRender` 플래그로 합쳐져, 중복 invalidate가
  깨어날 때마다 하나의 렌더로 수렴한다.
- 연속 모드는 invalidate 없이도 렌더하므로 애니메이션 - 그리고 `AnimationActive`를 세우는
  다른 무엇이든 - 매 프레임 진행할 수 있다.
