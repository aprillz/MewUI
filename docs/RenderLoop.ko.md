# 렌더 루프 개념

이 문서는 최근 변경된 MewUI 렌더 루프 모델을 정리한 내부 문서이다. 플랫폼/백엔드 동작과 스케줄링 기준을 이해하기 위한 용도다.

---

## 1. 목표

- 메시지 처리와 렌더링을 분리하여 UI 응답성을 유지한다.
- 다음 두 모드를 지원한다:
  - **요청 기반 렌더링(OnRequest)**: Invalidate가 있을 때만 렌더
  - **연속 렌더링(Continuous)**: 애니메이션/Max FPS를 위한 반복 렌더
- D2D/OpenGL/GDI/Win32/X11에서 일관된 동작을 유지한다.

---

## 2. 모드

### 2.1 OnRequest (기본)

- `Window.Invalidate()` / `RequestRender()`가 렌더 요청을 등록한다.
- 플랫폼 호스트는 렌더 요청 또는 OS 메시지를 기다린다.
- 요청이 들어오면 **invalidate된 창만 렌더**한다.
- WPF처럼 “렌더 요청을 합치는(coalescing)” 방식에 가깝다.

### 2.2 Continuous

- invalidate 여부와 관계없이 **모든 창을 매 프레임 렌더**한다.
- `TargetFps`로 제한 가능 (0이면 무제한).
- 애니메이션/프로파일링/Max FPS 모드를 위해 사용한다.

---

## 3. RenderLoopSettings

루프 설정은 `Application.Current.RenderLoopSettings`로 제공된다:

- `Mode` → `OnRequest` 또는 `Continuous`
- `TargetFps` → FPS 제한 (0 = 무제한)
- `VSyncEnabled` → 백엔드 swap/present 동작

플랫폼 호스트와 그래픽 백엔드가 이 값을 읽어 동작한다.

---

## 4. 백엔드 동작

### 4.1 Direct2D

- DXGI Present 옵션으로 vsync 제어.
- `VSyncEnabled = false` → `PresentOptions = IMMEDIATELY`
- `VSyncEnabled = true` → 기본 present (DWM 영향 포함)

### 4.2 OpenGL

- WGL/GLX swap interval 사용.
- `VSyncEnabled = false` → `SwapInterval = 0`
- `VSyncEnabled = true` → 기본 vsync (보통 1)

### 4.3 GDI

- GDI는 vsync 개념이 없다.
- `VSyncEnabled`는 동작에 영향 없음.

---

## 5. 렌더링과 메시지 루프

- **OnRequest**: 렌더 요청 플래그가 있을 때만 렌더
- **Continuous**: 매 루프마다 렌더 (FPS 제한 적용 가능)
- 두 모드 모두 OS 메시지 처리는 유지됨

플랫폼 호스트는 WM_PAINT에 의존하지 않고 `RenderIfNeeded`/`RenderNow`로 직접 렌더링한다.

---

## 6. FPS 및 디버그

- `Window.FrameRendered`는 렌더 프레임 끝에 항상 발생한다.
- Sample/Gallery는 이를 이용해 FPS를 표시한다.
- Continuous 모드에서는 매 프레임 갱신되어야 한다.

---

## 7. 설계 메모

- OnRequest에서는 요청을 합쳐 불필요한 렌더링을 줄인다.
- Continuous는 invalidate 없이도 렌더하므로 애니메이션에 유리하다.
- WM_PAINT 폭주를 방지하기 위해 직접 렌더링 경로를 사용한다.

---

## 8. 확장 방향

- 리테인/컴포지션 렌더링은 동일한 루프에 붙일 수 있다.
- 애니메이션은 `TargetFps`에 맞춰 안정적 타이밍으로 스케줄 가능하다.
- “활성 창만 continuous” 같은 정책 확장도 가능하다.

