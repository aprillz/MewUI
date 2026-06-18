# 레이아웃(Measure / Arrange / Render) & DPI 픽셀 스냅 규칙

이 문서는 MewUI의 레이아웃/렌더링 계약(Contract)과 DPI 변화에서도 “1px 잘림/헤어라인” 같은 아티팩트를 최소화하기 위한 계산 규칙을 정리합니다.

## 용어

- **DIP**: 장치 독립 픽셀(논리 단위). MewUI의 대부분 레이아웃 좌표/크기는 DIP 기준.
- **Px**: 장치 픽셀(물리 픽셀). `px = dip * dpiScale`.
- **dpiScale**: `Dpi / 96.0`.
- **Constraint(제약)**: `Measure(...)`에 전달되는 `availableSize`.
- **DesiredSize**: Measure 결과로 계산된 “원하는 크기”.
- **Bounds**: Arrange 결과로 확정된 배치 사각형.

## 파이프라인 개요

MewUI는 즉시 모드(매 프레임 그리기) 성향이지만, 레이아웃 트리는 유지(retained)하여 히트 테스트/레이아웃 결과 재사용을 합니다.

동작 순서는 다음과 같습니다.

1) **Measure**: 위→아래로 DesiredSize 계산
2) **Arrange**: 위→아래로 Bounds 확정
3) **Render**: Bounds와 상태로 실제 그리기

### 언제 어떤 패스가 실행되는가

- `InvalidateMeasure()` : Measure가 필요함(Arrange도 함께 invalid).
- `InvalidateArrange()` : Arrange만 필요함(Measure는 유효할 수 있음).
- `InvalidateVisual()` : 다시 그리기만 요청(레이아웃 변화 없음).

**원칙**:
- **스크롤**은 보통 *Arrange + Render*만 수행되어야 합니다(오프셋만 바뀌므로).
- 크기/콘텐츠에 영향을 주는 속성 변경은 **Measure**가 필요합니다.

## Measure

### 목적

Measure는 “주어진 제약(availableSize) 아래에서 요소가 원하는 크기(DesiredSize)가 무엇인지”를 계산합니다.

### 입력/출력

- 입력: `availableSize` (DIP)
- 출력: `DesiredSize` (DIP)

### 규칙

- Measure는 레이아웃 관점에서 **가능한 한 순수(pure)** 해야 합니다.
  - Measure 내부에서 레이아웃에 영향을 주는 속성을 매번 `set`하지 말고, 반드시 값 비교 후 변경이 있을 때만 `set` 해야 합니다(무한 invalidation 방지).
  - Arrange 결과(Bounds)에 의존하는 계산은 피합니다.
- Measure 스킵 조건(권장):
  - MeasureDirty가 아니고,
  - constraint가 동일하면,
  - 재측정을 생략할 수 있습니다.

### Measure 시 DPI 라운딩

Measure는 DIP를 반환합니다. 픽셀 스냅을 위해 각 컨트롤이 자체적으로 “부모 좌표까지 고려한” 픽셀 스냅을 제각각 수행하면 일관성이 깨지기 쉽습니다.

권장 원칙:
- Measure는 “픽셀 단위 맞춤”을 억지로 하려 하지 말고,
- 레이아웃 시스템 경계에서(예: DesiredSize 적용 시) 일관된 라운딩 정책을 적용합니다.

## Arrange

### 목적

Arrange는 “최종 위치/크기(Bounds)를 확정”하고, 자식 요소를 배치합니다.

### 입력/출력

- 입력: `finalRect` (DIP)
- 출력: `Bounds` (DIP)

### 규칙

- Arrange 스킵 조건:
  - ArrangeDirty가 아니고,
  - 이전 Bounds와 동일하면 생략 가능.
- Arrange는 자식 배치를 담당합니다(패널/컨텐츠 호스트 등).

### Arrange 시 DPI 픽셀 스냅(핵심)

1px 잘림/헤어라인 대부분은 아래 불일치에서 발생합니다.

- 부모가 계산한 child rect 라운딩 방식
- 자식이 자체적으로 다시 라운딩하는 방식
- Render 시 clip 라운딩 방식

이를 막기 위한 규칙:

- **한 패스(Measure/Arrange/Render)에서 동일한 dpiScale을 사용**합니다(윈도우 DPI).
- “폭/높이”만 라운딩하지 말고 **Rect의 양쪽 edge를 스냅**합니다.
  - 양쪽 edge를 각각 라운딩하면 결과적으로 1px 줄어드는(축소되는) 케이스가 생길 수 있습니다.
  - 따라서 스냅 방향(내향/외향)을 의도적으로 선택해야 합니다.

권장 의미론:

- **보더/백그라운드 등 실제 도형 외곽**: edge 스냅(안정적인 Bounds)
- **뷰포트/클립 영역**: 외향(outward) 스냅(줄어들어 잘리는 현상 방지)

## Render

### 목적

Render는 Bounds 기반으로 실제 픽셀을 그립니다.

### 규칙

- Render는 레이아웃을 수행하지 않습니다(Measure/Arrange 호출 금지).
- Render는 가능한 한 “이미 스냅된 geometry”를 사용합니다.

### 클립 규칙(1px 잘림 방지)

텍스트/1px 스트로크/안티앨리어싱은 논리 Bounds 밖으로 0.5px 정도 오버행할 수 있습니다. 상위에서 clip을 딱 맞춰 걸면 오버행이 잘려 “오른쪽/아래쪽 1px이 사라진 것처럼” 보일 수 있습니다.

권장 패턴:

1) 뷰포트 rect를 DIP로 계산
2) 픽셀에 스냅(클립은 외향 스냅 권장)
3) clip 적용
4) 필요하다면 **우/하단을 1 device pixel 만큼 확장**(오버행이 예상되는 컨트롤만)

이는 “clip 확장 helper”의 의도입니다.

## DPI 전파와 캐시

### DPI 소스

- Window가 최종 DPI(`Dpi`, `DpiScale`)의 기준입니다.
- 자식들은 동일 DPI로 라운딩/측정 결정을 해야 합니다.

### 캐시 무효화 조건

DPI 의존 캐시(텍스트 측정, 지오메트리 등)는 아래에서 무효화되어야 합니다.

- Effective DPI 변경
- 폰트 관련 속성 변경
- Wrap/constraint 변경(줄바꿈 텍스트)

## 스크롤: 기대되는 레이아웃 동작

### 오프셋

- 개념적으로 오프셋은 DIP 기반이지만, 안정적 스냅을 위해 내부는 px로 보관할 수 있습니다.
- 오프셋만 바뀌면 보통 Measure는 불필요합니다(Extent/Viewport가 변할 때만 Measure).

### 스크롤 중 업데이트되어야 하는 것

1) `viewport - offset`으로 자식 Arrange
2) 뷰포트 clip 아래에서 콘텐츠 Render
3) ScrollBar의 range/value를 현재 오프셋/뷰포트와 동기화

## 금지 패턴(성능/무한 루프 원인)

- Measure/Arrange 중 값 비교 없이 레이아웃 영향 속성을 계속 set
- Render에서 `InvalidateMeasure()`/`InvalidateArrange()`를 유발
- DPI를 매번 트리 상향 탐색으로 구하는 핫패스(캐시 없이)
- 스크롤처럼 오프셋만 바뀌는 케이스에서 Measure를 반복 수행

