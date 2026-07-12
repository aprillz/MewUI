# 레이아웃(Measure / Arrange / Render) & DPI 픽셀 스냅 규칙

이 문서는 MewUI의 레이아웃/렌더링 계약(Contract)과 DPI 변화에서도 1px 잘림/헤어라인 같은 아티팩트를 최소화하기 위한 계산 규칙을 정리합니다. 대상 독자는 `Element` / `FrameworkElement` / `UIElement`를 상속해 커스텀 컨트롤이나 패널을 작성하는 개발자입니다.

## 용어

- **DIP**: 장치 독립 픽셀(논리 단위). 대부분의 레이아웃 좌표/크기는 DIP 기준입니다.
- **Px**: 장치 픽셀(물리 픽셀). `px = dip * dpiScale`.
- **dpiScale**: `Dpi / 96.0` (`Window.DpiScale`).
- **Constraint(제약)**: `Measure(...)`에 전달되는 `availableSize`.
- **DesiredSize**: Measure 결과로 계산된 "원하는 크기", DIP 단위.
- **Bounds**: Arrange 결과로 확정된 배치 사각형, DIP 단위.

### 좌표 공간: Bounds는 부모 상대가 아니라 윈도우 절대 좌표

`Element.Bounds`는 부모의 로컬 좌표 공간이 아니라 **윈도우 절대 좌표**로 표현됩니다. 패널은 자신의 절대 `Bounds`를 기준으로 오프셋을 더해 자식을 배치합니다(예: `child.Arrange(new Rect(Bounds.X + offsetX, Bounds.Y + offsetY, ...))`). 자식마다 `(0, 0)`으로 리셋하지 않습니다. Element 변환은 이동(translate)만 지원하므로 트리 전체에서 이 규칙이 일관되게 유지됩니다. 커스텀 패널은 `Arrange`에 윈도우 절대 좌표 사각형을 넘겨야 합니다.

부모 상대적인 접근이 필요하면 `RenderSize`(원점 없는 `Size`만)를 사용하거나, `TranslatePoint` / `TranslateRect` / `TransformToAncestor` / `TransformToDescendant`로 두 요소의 좌표 공간을 서로 변환합니다.

## 파이프라인 개요

MewUI는 즉시 모드로 렌더링합니다(매 프레임 다시 그림). 다만 히트 테스트와 Measure/Arrange 결과를 프레임 간 재사용하기 위해 레이아웃/컨트롤 트리는 유지(retained)됩니다.

패스 순서는 다음과 같습니다.

1) **Measure**: DesiredSize 계산. 위에서 아래로 재귀합니다(부모의 `MeasureOverride`/`MeasureContent`가 자신의 크기를 반환하기 전에 자식들의 `Measure`를 호출).
2) **Arrange**: Bounds 확정. 같은 방식으로 위에서 아래로 재귀합니다.
3) **Render**: Bounds와 현재 visual 상태로 그리기(요소 자신의 시각 요소는 `OnRender`, 자식은 이어서 `RenderSubtree`).

### 언제 어떤 패스가 실행되는가

- `InvalidateMeasure()`: `IsMeasureDirty`와 `IsArrangeDirty`를 함께 true로 표시한 뒤, 이미 dirty였더라도 `Parent.InvalidateMeasure()`를 무조건 다시 호출합니다(오래된 조상의 플래그도 다시 통지되어야 하고, 이 호출 자체가 `Window`를 깨우는 역할도 합니다). 마지막에 `InvalidateVisual()`을 호출합니다.
- `InvalidateArrange()`: `IsArrangeDirty`만 true로 표시하고 같은 방식으로 `Parent.InvalidateArrange()`에 전파합니다. Measure가 invalid임을 의미하지 **않습니다**.
- `InvalidateVisual()`: `Parent.InvalidateVisual()`로만 전파합니다. `Window`에서는 다시 그리기만 예약합니다(`RequestRender()`). 레이아웃 패스는 예약하지 **않습니다**.
- `InvalidateVisualState()`(`UIElement`/`Control`): 스타일 트리거/상태 전환 등 visual state 재계산 대상으로 요소를 등록하고 `Window.RequestUpdatePass()`를 호출합니다. 위 두 가지와 다른 지점입니다. `AffectsVisualState`만 있고 레이아웃/렌더에는 영향이 없는 속성이라도, 트리거로 결정되는 값이 아직 실행되지 않은 레이아웃/렌더에 반영되어야 하므로 update pass 자체는 돌아야 합니다.

`Window.InvalidateMeasure()`와 `Window.InvalidateArrange()`는 위 동작에 더해 `RequestUpdatePass()`도 호출합니다.

**원칙**:
- **스크롤**은 *Arrange + Render*만 수행되어야 합니다. [스크롤](#스크롤-기대되는-레이아웃-동작) 절 참고.
- 크기/콘텐츠에 영향을 주는 속성 변경은 **Measure**가 필요합니다.

### Update pass (`Window.PerformLayout`)

`RequestUpdatePass()`는 디스패처에 `DispatcherPriority.Layout`으로 병합된 콜백 하나를 게시합니다(같은 틱 안의 반복 호출은 `PerformLayout()` 한 번과 뒤이은 렌더 요청으로 합쳐집니다). `PerformLayout()`은 순서대로 다음을 수행합니다.

1) `UpdateVisualStates()`: 레이아웃이 상태 의존 값을 읽기 전에 큐에 쌓인 visual state 변경(스타일 트리거/애니메이션)을 먼저 반영합니다(예: 트리거가 Padding을 바꾸는 경우).
2) 윈도우 자신의 스타일을 해석하고 템플릿을 적용합니다(`Window` 자신은 `MeasureOverride`를 우회하므로 템플릿 적용이 여기서 일어남).
3) `FitContent*` 계열 윈도우 크기 모드는 먼저 최대 제약으로 콘텐츠를 측정한 뒤 그 결과에 맞춰 윈도우 크기를 조정합니다.
4) 스킵 검사: 클라이언트 크기/패딩/콘텐츠 참조가 이전과 같고, 트리 안의 어떤 요소도 `IsMeasureDirty`/`IsArrangeDirty`가 아니며(컨테이너가 자신의 dirty 플래그를 지웠어도 가상화된 자손이 여전히 dirty일 수 있으므로 전체 트리 순회로 확인), 팝업/adorner/오버레이 레이아웃도 dirty가 아니면 Measure/Arrange를 다시 돌리지 않고 반환합니다.
5) 그렇지 않으면 Measure와 Arrange를 최대 8회로 제한된 루프로 반복하며, 더 이상 dirty가 없으면 조기 종료합니다. 같은 패스 도중 발생한 재배치 요청(예: `ScrollIntoView`)이 별도의 프레임 없이 이 루프 안에서 수렴하도록 하기 위함입니다. 수렴하지 않을 때 어떤 일이 벌어지는지는 [금지 패턴](#금지-패턴성능-문제나-레이아웃-스래싱-원인)을 참고하세요.
6) Adorner, 팝업, 오버레이 레이어는 마지막에 배치됩니다. 이들은 `Window.Parent`에 매달려 있지만 `Content` 트리에는 속하지 않으므로 자체 dirty 검사를 따로 거칩니다.

### 커스텀 합성 호스트

템플릿 바깥에서 자체적으로 자식 요소를 합성하는 커스텀 컨트롤(예: `ScrollViewer`나 가상화 아이템 프레젠터가 하는 것과 비슷한 내부 프레젠터)은 `IVisualTreeHost`에 더해 마커 인터페이스 `ISubtreeInvalidationHost`를 구현해야 합니다. 이 인터페이스가 있으면 `InvalidateMeasure()`/`InvalidateArrange()`가 내부 visual 서브트리까지 무조건 dirty 플래그를 전파하므로, 해당 자식들이 Measure의 constraint 동일 스킵 조건에 의해 조용히 누락되지 않습니다.

## Measure

### 목적

Measure는 주어진 제약(availableSize) 아래에서 요소가 원하는 크기(DesiredSize)가 무엇인지를 계산합니다.

### 입력/출력

- 입력: `availableSize` (DIP)
- 출력: `DesiredSize` (DIP) - 각 축이 유한하면 `availableSize`로 clamp되고(무한 축은 측정값을 그대로 통과), 그다음 픽셀 라운딩됩니다(아래 참고).

### 규칙

- Measure는 레이아웃 관점에서 **가능한 한 순수(pure)** 해야 합니다.
  - `MeasureOverride`/`MeasureContent` 안에서 레이아웃에 영향을 주는 속성을 조건 없이 set하지 않는 게 좋습니다. Measure 도중 다시 dirty가 걸려도, `Measure()`가 호출 마지막에 `IsMeasureDirty`를 무조건 false로 되돌리기 때문에 그 dirty 표시는 무한 루프가 아니라 조용히 버려집니다. 실제로 발생하는 문제는 "다음 재측정이 누락되는 것"이지 무한 루프가 아닙니다.
  - Arrange 결과(Bounds)에 의존하는 계산은 피합니다.
- Measure 스킵 조건(정확한 조건): `Measure(availableSize)` 호출은 요소가 measure-dirty가 아니고, 이전에 기록된 constraint가 있으며, 그 값이 `availableSize`와 같을 때만 `MeasureCore`/`MeasureOverride` 호출 없이 캐시된 `DesiredSize`를 반환합니다. 새 요소는 기본값이 measure-dirty이므로 첫 호출은 항상 측정합니다.
- **템플릿 컨트롤**: `Control`에 템플릿이 적용된 경우 `MeasureContent`/`ArrangeContent`는 컨트롤 자신의 콘텐츠 측정 로직 대신 템플릿의 visual root로 위임합니다(`root.Measure(availableSize)` 호출, `root.DesiredSize` 반환). `MeasureContent`를 오버라이드하는 컨트롤 저자는 템플릿이 없는(코드 전용) 경로만 신경 쓰면 됩니다.

### Measure 시 DPI 라운딩

`DesiredSize`는 프레임워크가 자동으로 픽셀 라운딩합니다. `Window.UseLayoutRounding`이 true(기본값)이면 `Element.Measure`가 clamp된 크기를 `Window.DpiScale` 기준의 크기 전용 라운딩 헬퍼로 반올림합니다. 컨트롤/패널 저자는 `MeasureOverride`/`MeasureContent`의 반환값을 직접 라운딩할 필요가 없습니다.

저자가 신경 써야 하는 부분은 자식에게 넘길 중간값을 라운딩해서, Measure 패스가 자식에게 준 제약과 이후 Arrange가 같은 자식에 대해 계산하는 값이 서로 어긋나지 않게 하는 것입니다. `ScrollViewer.MeasureContent`는 콘텐츠를 측정하기 전에 뷰포트 크기를 이렇게 라운딩하며, `ArrangeContent`에서 재사용할 것과 같은 `dpiScale`을 사용합니다. 이렇게 하지 않으면 분수 DPI에서 Measure가 계산한 뷰포트와 Arrange가 계산한 뷰포트가 장치 픽셀 하나만큼 어긋나 콘텐츠가 잘리는 문제가 생깁니다.

## Arrange

### 목적

Arrange는 각 요소의 최종 위치/크기(윈도우 절대 좌표의 `Bounds`)를 확정하고 자식을 배치합니다.

### 입력/출력

- 입력: `finalRect`, 윈도우 절대 좌표, DIP
- 출력: `Bounds`, 윈도우 절대 좌표, DIP

### 규칙

- Arrange 스킵 조건(정확한 조건): `Arrange(finalRect)`는 `finalRect`로부터 배치 사각형을 다시 계산하고(`GetArrangedBounds`가 `FrameworkElement`에서 `Margin`/정렬/`Min`/`Max`를 해석), 라운딩합니다. 이 결과가 요소가 arrange-dirty가 아니면서 현재 `Bounds`와 같으면 `ArrangeCore` 호출을 생략합니다. 이 비교는 항상 후보 사각형을 먼저 다시 계산한다는 점에 주의하세요. `GetArrangedBounds`는 dirty 플래그와 무관하게 매번 실행됩니다.
- Arrange는 자식 배치를 담당합니다(패널/콘텐츠 호스트의 `ArrangeContent`/`ArrangeCore`).

### Arrange 시 DPI 픽셀 스냅(핵심)

`Bounds`도 `DesiredSize`와 같은 방식으로 프레임워크가 자동으로 픽셀 라운딩합니다. `UseLayoutRounding`이 true이면 `Element.Arrange`가 `Window.DpiScale` 기준으로 배치 사각형을 라운딩합니다. 중요한 점은 이 라운딩이 **위치와 크기를 각각 독립적으로** 반올림한다는 것입니다(`x`, `y`, `width`, `height`를 각자 반올림). 좌/우 edge를 각각 반올림하고 그 차이로 폭을 구하는 방식이 아닙니다. 후자는 edge가 어디서 반올림되느냐에 따라 크기가 최대 1px 늘거나 줄어들 수 있어, 요소가 트리 안에서 움직일 때 흔들림(jitter)으로 나타납니다. 커스텀 `Arrange`/`ArrangeCore` 오버라이드는 자신의 `Bounds`를 다시 라운딩할 필요가 없습니다. 프레임워크가 이미 처리했습니다.

저자가 직접 스냅해야 하는 것은 그리기나 클리핑을 위해 별도로 계산하는 사각형들입니다. "edge를 반올림"과 "절대 줄어들지 않게"는 서로 다른 요구사항이므로, `LayoutRounding`은 목적별로 다른 헬퍼를 제공합니다.

| 헬퍼 | 라운딩 방식 | 용도 |
|---|---|---|
| `SnapBoundsRectToPixels(rect, dpiScale)` | 각 edge를 독립적으로 반올림(최대 1px 늘거나 줄 수 있음) | 보더/배경 등 그리기 지오메트리, 예: `FrameworkElement.GetSnappedBorderBounds` |
| `SnapConstraintRectToPixels(rect, dpiScale)` | 위와 같은 알고리즘 | Measure 시점의 constraint 사각형(`ScrollViewer.MeasureContent` 참고) |
| `SnapViewportRectToPixels(rect, dpiScale)` | 좌/상단은 floor, 우/하단은 ceil(절대 줄어들지 않음) | 스크롤 뷰포트, 클립 사각형, 예: `ScrollViewer.GetContentViewportBounds` |
| `MakeClipRect(rect, dpiScale, rightPx = 0, bottomPx = 0)` | 외향(outward) 스냅, 선택적으로 우/하단을 정수 device pixel만큼 확장 | Render 시점의 클립 사각형. `TextBase`, `ContextMenu`, `GridView`는 기본값(순수 외향 스냅)으로 사용 |
| `SnapThicknessToPixels(thicknessDip, dpiScale, minPixels)` | 최소값을 보장하며 정수 픽셀 개수로 반올림 | 분수 DPI에서도 사라지지 않아야 하는 보더/스트로크 두께 |
| `RoundSizeToPixels` / `RoundRectToPixels` | 위치와 크기를 독립적으로 반올림 | 프레임워크가 `DesiredSize`/`Bounds`에 내부적으로 사용하는 방식 |

## Render

### 목적

Render는 Bounds와 현재 visual 상태로 실제 픽셀을 그립니다.

### 규칙

- Render는 레이아웃을 수행하지 않습니다. `UIElement.Render`는 `sealed`이며 `Measure`/`Arrange`를 호출하지 않습니다. `OnRender` 안에서 `InvalidateMeasure()`/`InvalidateArrange()`를 유발하는 것은 피하세요. 현재 프레임을 망가뜨리지는 않지만, 이 렌더가 끝나자마자 또 다른 update pass를 예약하게 되어 조건 없이 매 프레임 반복될 수 있습니다.
- Render는 가능한 한 이미 스냅된 지오메트리를 사용합니다(위 Arrange 라운딩 표 참고).
- 윈도우 클라이언트 뷰포트 밖의 요소는 컬링됩니다. 요소의 `Bounds`가 `new Rect(root.ClientSize)`와 교차하지 않으면 `Render`는 비트맵 캐시를 해제하고 `OnRender`를 호출하지 않은 채 반환합니다. 부모가 적용한 변환(transform) 아래에서 렌더링되는 서브트리는 `Bounds`가 실제 보이는 영역을 반영하지 않으므로, 잘못 컬링되지 않도록 상속 속성인 `SkipViewportCull`을 설정하세요.

### 클립 규칙(1px 잘림 방지)

텍스트/스트로크/안티앨리어싱은 논리 Bounds 밖으로 0.5px 정도 오버행할 수 있습니다. 상위에서 자식의 Bounds에 정확히 맞춰 클립을 걸면 그 오버행이 잘려 오른쪽/아래쪽 1px이 사라진 것처럼 보입니다.

실제로 쓰이는 패턴은 다음과 같습니다(`ScrollViewer.GetContentClipBounds` 참고).

1) 뷰포트/콘텐츠 사각형을 DIP로 계산합니다.
2) 외향으로 스냅합니다(`SnapViewportRectToPixels`, 절대 줄어들지 않음).
3) 오버행이 예상되면(예: 자식의 보더 스트로크가 뷰포트 edge에 정확히 걸치는 경우), 해당 방향에 남는 여유(padding 등)만큼 최대 1 device pixel까지 사각형을 확장한 뒤 `MakeClipRect`로 다시 외향 스냅합니다. 확장량은 실제 남은 여유로 bound해야 합니다(`Math.Min(onePx, room)`). 바깥쪽 chrome 경계를 넘어서거나 음수 좌표로 확장하면 클립이 밀려 엉뚱한 픽셀이 잘릴 수 있습니다.
4) 계산된 사각형을 클립으로 적용합니다.

`MakeClipRect` 자체의 `rightPx`/`bottomPx` 인자로도 확장을 직접 수행할 수 있지만, 현재 코드베이스의 호출부는 모두 기본값(`0, 0`, 순수 외향 스냅)만 사용하고 확장은 3단계처럼 사각형을 미리 부풀리는 방식으로 처리합니다.

## DPI 전파와 캐시

### DPI 소스

- `Window.Dpi`(기본값 96)와 `Window.DpiScale => Dpi / 96.0`이 최종 기준입니다.
- 요소는 소유 `Window`까지 부모 체인을 타고 올라가 실효 DPI를 구합니다(`GetDpiCached`/`FrameworkElement.GetDpi()`). 결과는 context-version 단위로 캐시되어 렌더 루프에서 반복 호출해도 O(1)입니다. 부모 체인이 실제로 바뀐 뒤에만 다시 체인을 탐색합니다. 어떤 `Window`에도 붙어있지 않으면 OS 시스템 DPI로 대체합니다.
- OS가 DPI 변경을 통지하면 `Window.RaiseDpiChanged`가 visual 트리 전체의 DPI 캐시를 지운 뒤, 붙어있는 모든 `FrameworkElement`(그리고 팝업, adorner, 오버레이 레이어)에 대해 `NotifyDpiChanged`(내부에서 `OnDpiChanged` 호출 후 `InvalidateMeasure()` + `InvalidateVisual()`)를 호출합니다.

### 캐시

`TextMeasureCache`의 캐시 키는 `(text, family, size, weight, wrapping, maxWidthDip, dpi)`입니다. 텍스트/폰트/줄바꿈/줄바꿈 제약/DPI 중 어느 것이든 바뀌면 키가 달라지므로 자연히 캐시가 무효화됩니다. 별도로 기억해야 할 수동 무효화 절차는 없습니다. 이와 별개로 `TextBlock.OnDpiChanged`는 자체 `IFont`를 dispose해서 새 DPI로 글리프가 다시 만들어지게 합니다(Measure 캐시가 아니라 리소스 수명 관련 처리입니다).

## 스크롤: 기대되는 레이아웃 동작

### 오프셋

`ScrollViewer.HorizontalOffset`/`VerticalOffset` setter는 `InvalidateArrange()`만 호출하고 `InvalidateMeasure()`는 호출하지 않습니다. 즉 순수 오프셋 변경은 Arrange+Render로만 처리됩니다. Extent/Viewport(Measure가 필요한 값)는 콘텐츠나 가용 크기가 실제로 바뀔 때만 다시 계산됩니다.

`ScrollViewer.MeasureContent`는 의도적으로 `_scroll` 상태(메트릭, 오프셋)나 스크롤바의 `IsVisible`/`ViewportSize`/`Max`를 건드리지 않습니다. Measure는 가상의/제약 없는 크기로 호출될 수 있으므로(예: 팝업 소유자가 매 프레임 natural size를 확인하는 경우), 거기서 공유 스크롤 상태를 바꾸면 화면에 표시된 스크롤바가 깨지거나 사용자의 스크롤 오프셋이 리셋될 수 있습니다. 이런 변경은 모두 `ArrangeContent`에서 이루어지며, 이 시점의 뷰포트는 실제로 표시되는 크기를 반영합니다.

### 스크롤 중 업데이트되는 것

1) `ArrangeContent`가 일반 콘텐츠는 `viewport - offset` 위치에 자식을 배치하고, 가상화/스크롤 인지 콘텐츠(`IScrollContent`)는 `SetViewport`/`SetOffset`을 호출한 뒤 뷰포트 사각형 그대로 자식을 배치합니다(이 경우 자식은 Arrange로 이동되는 게 아니라 주어진 오프셋을 바탕으로 내부적으로 스스로 위치를 잡습니다).
2) 콘텐츠는 뷰포트 클립 아래에서 렌더링됩니다(위 클립 규칙 참고).
3) 스크롤바의 range/value가 현재 오프셋/뷰포트와 동기화됩니다(`SyncBars`).

## 금지 패턴(성능 문제나 레이아웃 스래싱 원인)

- Measure/Arrange 중 값 비교 없이 레이아웃 영향 속성을 계속 set: 잘해야 재측정 결과가 버려지는 낭비고([Measure 규칙](#규칙) 참고), 최악의 경우 값이 안정화되지 않아 요소가 매 프레임 스스로를 다시 dirty로 만들어 update pass가 계속 반복됩니다.
- `OnRender`에서 `InvalidateMeasure()`/`InvalidateArrange()`를 유발: 현재 프레임 자체는 안전하지만(Render는 중단되지 않음) 바로 다음에 새 update pass가 예약되며, 조건 없이 이렇게 하면 매 프레임 반복됩니다.
- 캐시된 `GetDpi()`/`GetDpiCached()`를 쓰지 않고 핫 패스에서 직접 `Parent`를 타고 올라가며 DPI를 다시 계산.
- 오프셋만 바뀌었는데 스크롤할 때마다 `InvalidateMeasure()`를 호출: `ScrollViewer`처럼 오프셋 전용 변경(`InvalidateArrange()`)만 사용해야 합니다.
