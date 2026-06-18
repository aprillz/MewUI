# MewDock

MewUI용 도킹 프레임워크: 도킹 가능한 문서 탭과 툴 패널, 드래그앤드롭 재배치, 분할, 자동 숨김 에지,
최대화, 창 분리(popout)를 제공합니다. [FlexLayout](https://github.com/caplin/FlexLayout)을 C# 관용 스타일로
포팅하고 그 위에 Visual Studio 방식의 도킹 레이어를 올린 것입니다.

- 네임스페이스: `Aprillz.MewUI.MewDock`
- 대상 프레임워크: `net8.0`, `net10.0`
- NativeAOT 친화적(소스 생성 기반, 리플렉션 없는 JSON).
- [MewUI](https://github.com/aprillz/MewUI)의 일부. [FlexLayout](https://github.com/caplin/FlexLayout)(MIT — `THIRD_PARTY_NOTICES.md` 참조)의 C# 포팅.

## 개념

- **문서 패널(document pane)** - 가운데 문서 영역에 위치하며, 상단 탭 스트립과 최대화 버튼을 갖습니다.
  탭의 기본값입니다.
- **패널(pane, 툴)** - 에지에 도킹하거나 자동 숨김 보더(클릭하면 펼쳐지는 얇은 띠)로 접힙니다.
  상단 탭 스트립 대신 캡션 바를 두르며, 툴이 하나뿐인 그룹은 탭 스트립을 아예 숨깁니다.
- **그룹(tabset)** - 하나의 프레임을 공유하는 패널 스택. 그룹을 분할하면 행/열 형태의 그룹 묶음이 됩니다.
- **창 분리(popout)** - 패널이나 그룹을 별도 OS 창으로 떼어낸 것. 분리 창은 호스트 창에 종속되어
  (작업표시줄에 표시되지 않음) 제목이 없습니다.
- **가운데 콘텐츠(center content)** - 레이아웃의 중앙. 기본값은 문서 호스트이며, 호스트가 임의 요소로
  교체할 수 있습니다. 이때도 툴은 그 주위에 그대로 도킹됩니다.

## 빠른 시작

```csharp
using Aprillz.MewUI.MewDock;

var manager = new DockingManager
{
    // 저장된 레이아웃에서 복원되는 패널의 콘텐츠를 만든다. DockPane.Component 키로 구분.
    ContentFactory = pane => new TextBlock { Text = pane.Title },
};

// 레이아웃 로드(FlexLayout 호환 JSON).
manager.LoadLayout(layoutJson);

// 런타임에 패널 추가. 명시적 콘텐츠는 ContentFactory를 거치지 않는다.
manager.AddDocumentPane("보고서", new ReportView());
manager.AddToolPane("탐색기", new ExplorerView(), DockEdge.Left);

// DockingManager는 Panel이다: 창에 그대로 넣으면 된다.
var window = new Window().Resizable(1100, 740).Content(manager);
Application.Run(window);
```

## `DockingManager` (파사드)

도킹 공간 전체를 호스팅하는 단일 컨트롤. 창에 추가하면 모델과 레이아웃 뷰를 자체적으로 소유합니다.

| 멤버 | 설명 |
|---|---|
| `LoadLayout(string json)` | JSON으로 레이아웃을 교체. |
| `SaveLayout() : string` | 현재 레이아웃 직렬화(도킹 서브레이아웃, 분리 창 포함). |
| `AddDocumentPane(string title, UIElement content, string? component = null) : DockPane` | 가운데에 문서 탭 추가. |
| `AddToolPane(string title, UIElement content, DockEdge edge = Left, string? component = null) : DockPane` | 에지에 도킹된 툴 패널 추가. |
| `ContentFactory : Func<DockPane, UIElement?>?` | 레이아웃에서 복원되는 패널의 콘텐츠 생성. |
| `HeaderFactory : Func<DockPane, UIElement?>?` | 커스텀 탭 헤더 콘텐츠. null이면 기본 헤더. |
| `TabMenuOpening : EventHandler<DockTabMenuEventArgs>` | 탭 우클릭 메뉴가 열릴 때마다 발생; `e.Menu`에 항목 추가. |
| `GroupMenuOpening : EventHandler<DockGroupMenuEventArgs>` | 그룹(탭 스트립) 메뉴가 열릴 때마다 발생. |
| `CenterContent : UIElement?` | 문서 호스트를 커스텀 가운데 요소로 교체. |
| `ActivePane : DockPane?` / `ActiveGroup : DockGroup?` | 포커스된 패널과 그 그룹. |
| `DocumentPanes` / `Panes : IReadOnlyList<DockPane>` | 문서 패널 / 툴 패널. |
| `Groups : IReadOnlyList<DockGroup>` | 모든 탭 그룹. |
| `ActivePaneChanged : EventHandler<DockPane?>` | 포커스가 다른 패널로 이동할 때 발생. |
| `Changed : EventHandler` | 레이아웃이 바뀐 뒤 발생. |

## `DockPane` (핸들)

패널 하나에 대한 가벼운 핸들. 식별자와 공통 동사를 담을 뿐, 레이아웃 자체는 매니저 안에 남아 있습니다.

| 멤버 | 설명 |
|---|---|
| `Title` (get/set) / `Component` | 표시 제목(설정 시 탭 이름 변경)과 직렬화 키. |
| `IsDocument` / `IsActive` | 문서 패널 vs 툴 패널 / 활성 패널 여부. |
| `Group : DockGroup?` / `Edge : DockEdge?` | 소속 그룹(자동 숨김이면 null)과 붙어 있는 에지. |
| `Content` | `AddDocumentPane`/`AddToolPane`에 넘긴 요소(팩토리 복원 패널은 null). |
| `Activate()` / `Close()` | 패널 선택 또는 닫기. |
| `Float()` / `FloatGroup()` | 패널, 또는 그 그룹 전체를 창으로 분리. |
| `SplitOff(DockEdge)` | 이 패널을 자기 그룹에서 해당 에지 방향으로 분리. |
| `MoveInto(DockGroup)` / `DockInto(DockGroup, DockEdge)` | 다른 그룹에 탭으로 합류 / 그 그룹 기준 분할 도킹. |
| `Pin()` / `Unpin()` | 자동 숨김 패널을 도킹 그룹으로 고정 / 도킹 그룹을 자동 숨김으로 되돌림. |

```csharp
manager.ActivePane?.FloatGroup();      // 활성 그룹을 창으로 분리
manager.Panes[0].Unpin();              // 툴을 자동 숨김 에지로 되돌림
```

`DockEdge`는 `Left | Top | Right | Bottom`입니다.

## `DockGroup` (핸들)

탭 그룹(탭셋) 하나에 대한 핸들. 자동 숨김 보더는 그룹이 아니므로, 자동 숨김된 패널의 `Group`은 null입니다.

| 멤버 | 설명 |
|---|---|
| `IsDocument` / `IsMaximized` | 문서 그룹 vs 툴 그룹 / 최대화 상태 여부. |
| `Edge : DockEdge?` | 그룹이 도킹된 에지(문서/플로팅 그룹이면 null). |
| `Panes : IReadOnlyList<DockPane>` / `ActivePane : DockPane?` | 그룹의 탭들; 선택된 탭. |
| `AddPane(string title, UIElement content, string? component = null) : DockPane` | 이 그룹에 탭 추가(종류는 그룹의 `IsDocument`를 따름). |
| `Activate()` / `Close()` | 그룹 포커스; 그룹과 모든 탭 닫기. |
| `Float()` | 그룹 전체를 창으로 분리. |
| `ToggleMaximize()` | 최대화/원복(문서 그룹만). |
| `Unpin()` | 도킹된 툴 그룹을 자동 숨김 에지로 되돌림(툴 그룹만). |

## 패널 배치와 탐색

**부트스트랩 vs 그룹 단위.** 그룹이 하나도 없어도 `DockingManager.AddDocumentPane`/`AddToolPane`은 동작합니다 -
없으면 그룹을 만들어 줍니다. 그래서 처음 시작은 항상 매니저 레벨 Add이고, 반환된 `DockPane`의 `Group`이 그때
생긴 그룹입니다. 빈 그룹은 만들 수 없으므로(탭 0개 그룹은 자동 정리), "그룹 생성"은 곧 "패널 추가"입니다.

```csharp
var pane = manager.AddDocumentPane("First", new View()); // 빈 레이아웃에서도 OK, 그룹 생성됨
var group = pane.Group;                                   // 이제 그룹이 생김
group.AddPane("Second", new View());                      // 같은 그룹에 추가(종류는 그룹을 따름)
```

이미 있는 그룹/패널을 타깃할 때:
- `DockGroup.AddPane(...)` - 그 그룹에 탭 추가. 새 탭의 종류(문서/툴)는 그룹의 `IsDocument`를 따릅니다.
- `DockPane.MoveInto(group)` / `DockInto(group, edge)` - 다른 그룹으로 이동 / 그 그룹 기준 분할 도킹.

**탐색용 별도 API는 없습니다.** `Panes`/`DocumentPanes`/`Groups`가 열거형이라 LINQ로 찾습니다:

```csharp
var grid = manager.Panes.FirstOrDefault(p => p.Component == "grid");
```

## 탭 컨텍스트 메뉴

탭을 우클릭하면 컨텍스트 메뉴가 열리고, 기본 항목은 탭 종류에 따라 다릅니다:

- **문서 탭**: Close / Close Others / Close All / Float / New Vertical Tab Group / New Horizontal Tab Group /
  Move to Next/Previous Tab Group / Maximize-Restore. 닫기 변형과 분할은 그 탭이 속한 그룹으로 한정되고,
  "Move to ... Tab Group"은 순회 순서상 인접 그룹을 대상으로 합니다(양 끝에서는 비활성).
- **툴 탭**: 툴 캡션을 미러링 - 고정된 툴은 Float / Auto Hide / Close, 자동 숨김(보더) 탭은 Dock / Float / Close.

**빈 탭 스트립 영역**을 우클릭하면 그룹 메뉴(Float / Maximize-Restore 또는 Auto Hide / Close All)가 열리며,
`GroupMenuOpening` 이벤트로 커스터마이즈합니다.

앱 고유 명령은 `TabMenuOpening` 이벤트로 덧붙입니다(메뉴가 열릴 때마다, 기본 항목 뒤에 발생):

```csharp
manager.TabMenuOpening += (s, e) =>
{
    e.Menu.AddSeparator();
    e.Menu.AddItem("Copy Path", () => CopyPath(e.Pane));
};
```

기본 라벨은 `MewUIDockString`에서 오므로 나머지와 함께 지역화됩니다.

## 레이아웃 JSON

`LoadLayout` / `SaveLayout`은 FlexLayout 호환 모델을 사용합니다. 탭은 기본적으로 문서이며,
`"isDocument": false`로 패널로 표시합니다(자동 숨김 보더 탭은 자동으로 패널로 표시됨).

```json
{
  "global": {},
  "borders": [
    { "location": "left", "children": [
      { "type": "tab", "name": "탐색기", "component": "grid" }
    ]}
  ],
  "layout": {
    "type": "row",
    "children": [
      { "type": "tabset", "weight": 60, "children": [
        { "type": "tab", "name": "보고서", "component": "chart" }
      ]},
      { "type": "tabset", "weight": 40, "children": [
        { "type": "tab", "name": "노트", "component": "notes", "enableClose": false }
      ]}
    ]
  }
}
```

## 콘텐츠 해석과 영속성

패널이 콘텐츠를 얻는 경로는 둘입니다:

1. **명시적 콘텐츠** - `AddDocumentPane` / `AddToolPane` / `DockGroup.AddPane`에 살아있는 요소를 넘기면, 그 패널에
   묶여 그대로 표시됩니다. `ContentFactory`를 거치지 않습니다.
2. **팩토리 콘텐츠** - JSON에서 로드된 탭은 `component` 키를 갖습니다. 뷰가 본문을 그릴 때 `ContentFactory(pane)`를
   호출하고, 호스트가 `pane.Component`를 읽어 요소를 만듭니다.

`component`는 프레임워크가 노드에 실어 직렬화하는 문자열일 뿐, 그 자체로 콘텐츠에 **매핑되지 않습니다.**
매핑은 `ContentFactory` 안(즉 `pane.Component`에 대한 `switch`나 딕셔너리)에 있습니다:

```csharp
manager.ContentFactory = pane => pane.Component switch
{
    "grid"  => new GridView(),
    "chart" => new ChartView(),
    _       => new TextBlock { Text = pane.Title },
};
```

영속성은 여기서 따라옵니다: `SaveLayout`은 각 탭의 `name`과 `component`를 기록할 뿐 살아있는 요소를 저장하지
않습니다. 따라서 패널은 `ContentFactory`가 다시 만들 수 있는 `component`를 가질 때만 `Save` -> `Load`를
견딥니다. Add 메서드들은 `component` 인자를 받으므로, **추가할 때 키를 함께 주면** 즉시 표시(명시적 콘텐츠)와
저장/복원(키로 `ContentFactory` 재생성)을 한 번에 얻습니다:

```csharp
manager.AddDocumentPane("보고서", new ReportView(), component: "report"); // 표시 + 영속
```

`component`를 생략하면 패널은 즉시 표시되지만 왕복 후에는 **복원되지 않습니다**(로드 후 다시 추가해야 함).

## 지역화

사용자 표시 문자열은 `MewUIDockString`에 `ObservableValue<string>`으로 들어 있습니다(메뉴, 툴팁, 드래그 칩,
이름 없는 탭 폴백). 기본값은 영어입니다. `.Value`를 설정하면 번역되며, 바인딩 소비처(툴팁)는 즉시,
일시적 소비처(메뉴, 드래그 칩)는 다음 표시 시점에 반영됩니다.

```csharp
MewUIDockString.MenuClose.Value = "닫기";
MewUIDockString.ToolTipClose.Value = "닫기";
```

빈 가운데 영역에는 아무 텍스트도 두지 않습니다. 시작 페이지나 브랜딩이 필요하면 `CenterContent`로 넣으세요.
