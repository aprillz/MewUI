# Window 시각 레이어

`Window`는 일반 콘텐츠와 Window가 관리하는 시각 레이어를 함께 렌더링합니다. 각 레이어는
서로 다른 소유권과 배치 모델을 위한 것입니다.

- `AdornerLayer`는 특정 요소를 기준으로 시각 요소를 배치합니다.
- `OverlayLayer`는 Window 전체를 기준으로 시각 요소를 배치합니다.
- Popup은 popup 소유 컨트롤이 관리하는 일시적 시각 요소이며, 일반적으로 별도 네이티브
  표면을 사용합니다.

`OverlayLayer`와 `OverlayWindow`는 서로 다른 개념입니다. `OverlayLayer`는 일반 Window 내부의
공개 레이어입니다. 프레임워크 내부의 `OverlayWindow`는 드래그 미리보기처럼 여러 Window를
가로지르는 기능에 사용하는 클릭 통과형 네이티브 Window입니다.

## 표면과 적층 모델

다음 계층은 소유 Window의 표면과 기본 popup host가 사용하는 native 표면을 분리해서
보여줍니다. `렌더 스택` 아래의 항목은 앞쪽부터 뒤쪽 순서입니다.

```text
Window (소유자 및 visual root)
├── 소유 OS 표면
│   └── 렌더 스택 (앞 → 뒤)
│       ├── OverlayLayer
│       ├── In-surface popup host (폴백 및 headless 전용)
│       ├── AdornerLayer
│       └── Window 콘텐츠 / 템플릿
└── 소유 native 표면 (기본 popup host)
    └── PopupWindow (popup마다 하나의 표면)
        └── PopupChrome 및 popup 콘텐츠의 portal 렌더링
            └── Parent와 visual root는 소유 Window에 유지
```

렌더링은 반대 방향인 콘텐츠부터 Overlay 순서로 진행합니다. 적중 검사는 앞쪽부터
Overlay, in-surface popup, Adorner, 일반 콘텐츠 순서로 진행합니다. 각 레이어 안에서는 나중에
추가한 요소가 먼저 그린 요소 위에 렌더링되며 적중 검사도 먼저 받습니다.

Native popup은 이 적층 구조에 포함되지 않습니다. 기본 popup host는 별도의 OS 표면을 가진
비활성 `PopupWindow`를 생성하므로 dropdown, menu, tooltip이 소유 Window 밖으로 확장될 수
있습니다. 따라서 소유 Window의 Overlay가 렌더 순서상 나중이어도 별도 native popup 표면을
덮을 수는 없습니다.

디버그 전용 진단 시각 요소는 공개 레이어 뒤에 렌더링될 수 있으며 애플리케이션 레이어
계약에는 포함되지 않습니다.

## 레이어 비교

| 레이어 | 배치와 표면 | 입력 | 대표 용도 |
| --- | --- | --- | --- |
| Window 콘텐츠 | Window padding 안쪽에 배치되며 소유 표면에 렌더링 | 일반 시각 트리 라우팅 | 애플리케이션 콘텐츠와 컨트롤 템플릿 |
| `AdornerLayer` | 꾸미는 요소의 bounds에 맞춰 소유 표면에 배치 | 적중 검사에 참여하며 나중에 추가한 Adorner를 먼저 검사 | 선택 핸들, 유효성 표시, 크기 조절 핸들 |
| Popup | 소유 Window의 client DIP 좌표로 배치하며 일반적으로 별도 native 표면에 렌더링 | 입력이 popup 소유 컨트롤로 bubble되며 transient popup은 light-dismiss 지원 | dropdown, context menu, submenu, tooltip |
| `OverlayLayer` | 모든 요소를 전체 client 영역으로 measure/arrange하며 소유 표면에 렌더링 | 적중 검사에 참여하며 장식용 Overlay는 `IsHitTestVisible = false` 필요 | busy mask, toast, 전체 Window progress |

## OverlayLayer

모든 `Window`는 하나의 `OverlayLayer`를 제공합니다.

```csharp
var overlay = new ProgressIndicator
{
    IsActive = true,
    IsHitTestVisible = false,
};

window.OverlayLayer.Add(overlay);

// 나중에 제거:
window.OverlayLayer.Remove(overlay);
```

Overlay를 추가하거나 제거하면 layout과 render를 요청합니다. 레이어는 요소의 시각 부모를
Window로 설정하므로 Overlay는 Window의 visual root, theme, DPI, 상속 값을 사용합니다.

각 Overlay에는 전체 client 사각형이 주어집니다. 이 영역 안에서 자식의 위치를 정하는 것은
Overlay 자신의 책임입니다. 적중 검사가 활성화된 전체 Window Overlay는 아래 콘텐츠의 입력을
막는 장벽으로도 동작합니다. 장식용 Overlay는 적중 검사를 명시적으로 꺼야 합니다.

`OverlayLayer`에는 `IOverlayService`도 등록할 수 있습니다. 서비스는 자신의 presenter 요소를
소유하며 `GetService` 또는 `GetOrCreateService`로 가져올 수 있습니다. Toast와 busy indicator가
이 모델을 사용합니다.

`Remove`는 Overlay를 분리하지만 dispose하지 않습니다. 제거 후 필요한 dispose는 호출자가
담당합니다. Window 자체가 dispose될 때는 등록된 서비스와 레이어에 남아 있는 Overlay를
Window가 dispose합니다.

## AdornerLayer

Adorner는 `AdornedElement`와 연결되지만, 꾸미는 요소의 subtree clip을 벗어나 일반 콘텐츠 위에
렌더링할 수 있도록 Window에 직접 parent됩니다.

```csharp
var adorner = new Adorner(target, adornerContent);
var layer = AdornerLayer.GetAdornerLayer(target);

layer?.Add(adorner);

// 나중에 제거:
layer?.Remove(adorner);
```

대상이 `Window`에 연결되기 전에는 `GetAdornerLayer`가 `null`을 반환합니다. 대상과 같은 Window에
속한 레이어를 통해 Adorner를 추가해야 합니다.

Window layout 중 Adorner는 Window 좌표계에 있는 대상 요소의 bounds로 measure/arrange됩니다.
`Window` 자체를 꾸미면 전체 client 사각형을 사용합니다. 대상 요소나 Adorner가 숨겨져 있으면
해당 layout pass에서는 Adorner 배치를 건너뜁니다.

레이어는 추가 순서로 Adorner를 렌더링하고 역순으로 적중 검사합니다. 시각 효과만 제공하며
대상 요소의 입력을 가로채지 않아야 한다면 `IsHitTestVisible = false`를 사용합니다.

`Remove`는 Adorner를 분리하지만 dispose하지 않습니다. 계속 연결된 Adorner는 Window의 시각
트리를 dispose할 때 함께 dispose됩니다.

## Popup

공개 `Window.PopupLayer`는 없습니다. 애플리케이션 컨트롤은 일반적으로 `ComboBox`,
`ContextMenu`, `ToolTip` 또는 다른 popup 소유 컨트롤을 사용합니다. 사용자 정의 dropdown
컨트롤은 `PopupOwnerBase`를 상속하고 `CreatePopupContent`로 popup 콘텐츠를 제공하며 필요한
경우 배치 계산을 재정의할 수 있습니다.

`PopupManager`는 프레임워크 내부의 정책 레이어입니다. 열린 popup과 소유자, 배치, focus 복원,
light-dismiss, 닫힘 알림을 관리합니다. 내부 `staysOpen` 정책이 지정되지 않은 transient popup은
관련 없는 외부 pointer press, focus 변경, scroll, 명시적 요청, Window 비활성화 또는 Window
종료 시 닫힙니다.

### Native popup portal

일반 런타임 host는 popup 콘텐츠를 `PopupChrome`으로 감싸고 비활성 native `PopupWindow`에
그립니다. 하지만 popup subtree는 계속 소유 `Window`에 뿌리를 둡니다.

- theme, DPI, style, 상속 속성과 `FindVisualRoot()`는 계속 소유 Window를 사용합니다.
- `ContextParentOverride`는 popup 소유 요소를 통해 상속 context를 해석합니다.
- 렌더링과 pointer capture는 native popup 표면을 사용합니다.
- 입력 bubbling은 popup root를 넘어 소유 컨트롤로 이어집니다.

즉 시각 소유권은 소유 Window에 남고 픽셀과 native 입력만 다른 표면에 존재하는 portal입니다.
실제 입력/렌더 표면이 필요한 프레임워크 코드는 `FindVisualRoot()`가 표면이라고 가정하지 않고
`ResolveInputHostWindow()`를 사용합니다.

Headless 테스트와 in-surface 폴백은 동일한 popup 정책과 소유자 context를 사용하지만, popup을
소유 표면의 Adorner와 Overlay 사이에서 렌더링하고 적중 검사합니다.

## 레이어 선택 기준

- 일반 panel layout에 참여하는 시각 요소는 Window 콘텐츠를 사용합니다.
- 특정 요소의 위치를 따라야 하면 Adorner를 사용합니다.
- Window 전체를 덮거나 전체 영역을 기준으로 배치하면 Overlay를 사용합니다.
- 일시적이며 popup focus/dismiss 동작이 필요하거나 소유 Window 밖으로 확장될 수 있으면 Popup을
  사용합니다.
- Native 표면 경계나 popup 입력 의미가 필요할 때 `OverlayLayer`를 Popup 대용으로 사용하지
  않습니다.
