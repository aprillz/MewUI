# 포커스와 Tab 내비게이션

MewUI의 키보드 포커스 모델과 Tab 순회 규칙을 설명한다.

## 포커스 모델

포커스는 창 단위로 관리된다. 각 `Window`는 하나의 `FocusManager`를 가지며, 창 안에서
키보드 포커스를 가진 요소는 항상 하나 이하다 (`FocusManager.FocusedElement`).

```csharp
window.FocusManager.FocusedElement   // 현재 포커스 요소 (없으면 null)
element.Focus();                     // 프로그램으로 포커스 이동
window.FocusManager.ClearFocus();    // 포커스 해제
```

### 포커스를 받을 조건

요소가 포커스를 받으려면 다음을 모두 만족해야 한다.

- `Focusable == true` : 컨트롤 타입이 기본값을 정한다 (`Button`, `TextBox`, `ListBox`
  등은 true, Label/TextBlock/Panel 등 비대화형 요소는 false). 필요하면 인스턴스별로
  덮어쓸 수 있다 (아래 참고).
- `IsEffectivelyEnabled == true` : 자신과 조상이 모두 활성 상태.
- `IsVisible == true`

### 포커스 상태와 스타일

| 속성 | 의미 |
|---|---|
| `IsFocused` | 이 요소가 키보드 포커스를 가짐 (읽기 전용) |
| `IsFocusWithin` | 이 요소 또는 자손이 포커스를 가짐 (읽기 전용) |

두 속성 모두 스타일의 상태 트리거에서 사용할 수 있다 (포커스 링 등).
`GotFocus` / `LostFocus` 이벤트로 변화를 구독한다.

```csharp
new TextBox()
    .OnGotFocus(() => Console.WriteLine("focus in"))
    .OnLostFocus(() => Console.WriteLine("focus out"));
```

### 마우스와 포커스

- 클릭하면 히트된 요소에서 가장 가까운 focusable 조상이 포커스를 받는다
  (Label을 클릭해도 그것을 감싼 컨트롤이 포커스됨).
- 창의 빈 배경을 클릭하면 포커스가 해제된다.
- 포커스가 없을 때 키 입력은 창(Window)에서 버블링을 시작하므로, 창 레벨
  `OnKeyDown` 처리(다이얼로그의 Escape 취소 등)는 포커스 유무와 무관하게 동작한다.

## Tab 내비게이션

Tab은 다음 focusable 요소로, Shift+Tab은 이전 요소로 포커스를 옮긴다. 끝에 도달하면
처음으로 순환한다. 순서 규칙:

1. 기본 순서는 **비주얼 트리 순서**(자식 추가 순서)다.
2. `TabIndex`가 지정된 요소는 그 값의 **오름차순으로 먼저** 방문하고, 지정되지 않은
   요소들이 트리 순서로 뒤따른다.
3. 같은 `TabIndex` 값끼리는 트리 순서를 유지한다.

### TabIndex (double)

```csharp
new Button().TabIndex(1);      // 명시 순서
new Button().TabIndex(1.5);    // 1과 2 사이에 삽입 - 재번호 불필요
new Button().TabIndex(0);      // 유효한 첫 순서 (WPF/WinForms와 동일)
```

- 기본값은 `double.NaN`이며 "자동(트리 순서)"을 뜻한다.
- **0 이상의 유한값만 명시 순서**로 취급한다. 음수, NaN, 무한대는 자동과 같다.
- 자동으로 되돌리려면 0이 아니라 `double.NaN`을 대입한다.
- HTML과의 차이: HTML에서 `tabindex="0"`은 문서 순서지만 MewUI에서 `TabIndex = 0`은
  "맨 앞 배치"다. Tab에서 제외하려면 음수가 아니라 `IsTabStop = false`를 사용한다.

### IsTabStop (bool)

```csharp
new Button().IsTabStop(false);   // Tab 순회에서만 제외
```

- `false`면 Tab 순회에서 제외될 뿐, 클릭이나 `Focus()` 호출로는 여전히 포커스된다.
- 필터 전용이다. focusable하지 않은 요소(Label 등)에 `IsTabStop = true`를 지정해도
  Tab 대상이 되지 않는다 (게이트는 `Focusable`).
- 용도 구분: 컨트롤 내부 파트 제외는 컨트롤 작성자가(생성자/스타일),
  개별 인스턴스 제외는 앱이 결정한다.

### Focusable (bool)

```csharp
new Button().Focusable(false);   // 이 인스턴스는 포커스 자체를 못 받게
```

- 컨트롤 타입이 기본값을 정하지만(대화형 컨트롤은 true), 인스턴스별로 덮어쓸 수 있다.
  주로 조합 컨트롤에서 내부 파트를 포커스 대상에서 빼는 데 쓴다.
- `IsTabStop`과의 차이: `Focusable = false`는 클릭·`Focus()`를 포함해 **모든** 포커스를
  막는다. `IsTabStop = false`는 Tab 순회에서만 뺀다.
- 이미 포커스를 가진 요소에 `Focusable = false`를 지정해도 포커스가 즉시 옮겨가지는
  않는다. 다음 Tab 이동이나 클릭에서 자연스럽게 해소된다.

### 순회 스코프

- `TabControl`은 **활성 탭의 컨텐츠만** 순회에 포함한다. 비활성 탭은 건너뛴다.
  활성 탭에 focusable 요소가 없으면 TabControl 자신이 탭스톱이 된다.
- 스코프 내부의 `TabIndex`는 그 스코프 안에서만 비교된다. 중첩 스코프는 바깥 순서에서
  하나의 블록으로 움직이며, 내부 순서가 바깥 요소와 섞이지 않는다.
- 가상화 리스트(ListBox, TreeView, GridView)는 화면 밖 아이템으로도 Tab 이동이
  가능하며, 필요하면 자동으로 스크롤된다.

## 요약

| 하고 싶은 것 | 방법 |
|---|---|
| 탭 순서 지정 | `TabIndex(1)`, `TabIndex(2)` ... |
| 두 요소 사이에 끼워넣기 | `TabIndex(1.5)` |
| Tab에서만 제외 (클릭 포커스는 유지) | `IsTabStop(false)` |
| 포커스 자체를 막기 (클릭 포함) | `Focusable(false)` |
| 자동 순서로 복귀 | `TabIndex = double.NaN` |
