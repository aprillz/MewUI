# DevTools

MewUI는 요소 인스펙터, 비주얼 트리 창, 프레임 통계 오버레이, 프로파일러 타임라인을 함께 제공한다. 기본은 꺼짐이며 MSBuild 속성 하나로 켠다.

---

## 1. 켜기

```xml
<PropertyGroup>
  <MewUIDevTools>true</MewUIDevTools>
</PropertyGroup>
```

기본값은 `Debug` 구성에서 `true`, `Release`에서 `false`다. 따라서 그냥 `dotnet run` 하면 이미 켜져 있다. 최적화된 코드를 프로파일링하는 경우처럼 릴리스 구성에서 쓰고 싶을 때 이 속성을 명시한다.

트리밍과 NativeAOT 게시(`PublishTrimmed`, `PublishAot`)에서는 DevTools를 쓸 수 없다. 속성 패널이 요소의 CLR 멤버를 반사로 열거하는데 트리밍된 멤버 목록은 있는 멤버를 조용히 빠뜨리기 때문에, 거짓말하는 도구를 넣느니 빌드가 기능을 끄고 경고를 낸다. 이때 DevTools가 쓰던 코드는 출력에서 통째로 제거되며, 그래서 이 기능을 켜지 않는 앱은 프레임워크에 그것이 들어 있다는 이유로 치르는 비용이 없다.

## 2. 열기

| 도구 | 단축키 |
| --- | --- |
| 요소 인스펙터 | `Ctrl/Cmd+Shift+I` |
| 비주얼 트리 창 | `Ctrl/Cmd+Shift+T` |
| 성능 모니터 | `Ctrl/Cmd+Shift+P` |
| 프로파일러 타임라인 | `Ctrl/Cmd+Shift+Alt+P` |

`WindowDevTools.IsSupported`는 이 빌드가 도구를 담을 수 있는지를 창 없이 알려준다. 트리밍과 NativeAOT 빌드에서는 트리머가 이 값을 상수로 접으므로, 이 값으로 감싼 코드도 도구와 함께 제거된다.

도구 자체는 `Window.DevTools`로 연다. 빌드가 기능을 켜지 않았으면 이 값이 `null`이다.

```csharp
window.DevTools?.ToggleInspector();

if (window.DevTools is WindowDevTools devTools)
{
    devTools.InspectorVisibleChanged += visible => status.Text = visible ? "인스펙터 켜짐" : "인스펙터 꺼짐";
}
```

`WindowDevTools`는 도구마다 토글, 상태 프로퍼티, 변경 이벤트를 하나씩 갖는다.

| 도구 | 토글 | 상태 | 이벤트 |
| --- | --- | --- | --- |
| 요소 인스펙터 | `ToggleInspector()` | `InspectorIsVisible` | `InspectorVisibleChanged` |
| 비주얼 트리 창 | `ToggleVisualTree()` | `VisualTreeIsOpen` | `VisualTreeOpenChanged` |
| 성능 모니터 | `TogglePerformanceMonitor()` | `PerformanceMonitorIsVisible` | `PerformanceMonitorVisibleChanged` |
| 프로파일러 타임라인 | `ToggleProfiler()` | `ProfilerIsOpen` | `ProfilerOpenChanged` |

인스펙터와 성능 모니터는 창 위에 직접 그려지므로 보이거나 숨는다. 비주얼 트리와 프로파일러는 그 자체가 창이므로 열리거나 닫힌다.

## 3. 도구별로 보여주는 것

**요소 인스펙터**는 커서 아래 요소를 강조하고 그 요소의 경계, 레이아웃 슬롯, 겉모습을 결정한 속성을 보여준다. `Control`이면 각 속성을 어느 스타일 계층이 이겼는지도 함께 보여준다.

**비주얼 트리 창**은 창의 요소 트리를 팝업과 어도너까지 포함해 나열하고, 대상 창에서 클릭한 요소를 선택한다. 노드를 선택하면 인스펙터 오버레이로 강조되므로, 트리를 열면 인스펙터도 함께 켜진다.

**성능 모니터**는 최근 프레임 시간, 드로우 콜, 컬 비율을 보여주는 오버레이다. 프레임에서 가장 마지막에 그려지므로 자기 자신의 비용도 표시되는 수치에 들어간다.

**프로파일러 타임라인**은 레이아웃, 렌더, 텍스트, 백엔드 단계의 프레임별 샘플을 기록하며 `Space`로 멈출 수 있다. 요소에 귀속된 샘플을 클릭하면 대상 창에서 그 요소가 강조된다.

## 4. 껐을 때의 비용

DevTools 코드는 MewUI 어셈블리 안에 있으므로 별도 패키지나 참조가 필요 없다. 속성이 꺼져 있으면 게이트 하나가 false가 되고, 프레임 루프와 입력 경로와 프로파일러 수집이 아무것도 할당하거나 측정하지 않고 건너뛴다. 트리밍과 NativeAOT 게시에서는 트리머가 그 게이트를 상수로 접어 도구를 통째로 제거한다.
