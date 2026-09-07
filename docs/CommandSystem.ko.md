# Command System

MewUI의 명령 시스템은 키보드, 버튼, 메뉴, 툴바, 코드 호출을 하나의 의미 기반 실행 경로로 통합합니다. 다섯 가지 질문을 각각 다른 축이 답하도록 나뉘어 있습니다. 무슨 동작인지(`Command`), 누가 실행하는지(스코프), 어디서 시작하는지(타깃), 무엇에 대해 실행하는지(인자), 어떤 값을 고르는지(데이터)입니다.

```text
표면 (Button / MenuItem / ToolBar 항목 / InputMap)   "이 명령을 실행해 달라" + 항목이 선언한 값(CommandData)
        │
        ▼ 타깃(CommandTarget): 어디서 시작할지
   CommandRouter ── 컨텍스트 사슬을 올라가며 스코프를 찾음
        │
        ▼ 스코프(CommandScope): 등록된 처리기
   처리기 ── 인자를 받아 실행
```

## Command: 정체성

`Command`는 동작의 정체성과 표시(`CommandPresentation`)만 가집니다. 무엇을 하는지, 지금 할 수 있는지는 전부 `Command` 바깥에 있습니다. 같은 `Command` 인스턴스를 메뉴, 툴바, 단축키가 공유하고 그 의미는 실행되는 문맥이 정합니다. 같은 `Id`를 쓰더라도 서로 다른 인스턴스는 다른 명령입니다.

```csharp
var save = new Command("file.save", "_Save", saveIcon);

window.Commands.Register(save, () => document.Save(), () => document.IsDirty);
window.InputMap.Map(save, new KeyGesture(Key.S, ModifierKeys.Primary));
```

생성자의 `text`는 `_Save` 형식의 액세스 키 표식을 허용하며 `__`는 실제 `_` 문자 하나를 뜻합니다. 원문은 `Command.Presentation.AccessText`에 보관되고, `save.Text`는 표식이 제거된 `"Save"`를 돌려줍니다. 메뉴처럼 액세스 키를 지원하는 표면은 `AccessKey`와 `AccessKeyIndex`도 사용합니다. 툴바, 명령 팔레트, 툴팁처럼 니모닉을 표시하지 않는 소비자는 `Command.Text`를 그대로 표시해도 `_`가 노출되지 않습니다.

## 스코프: 누가 실행하는가

스코프(`CommandScope`)는 명령과 처리기를 짝지어 두는 곳입니다. 모든 요소와 창은 `Commands`로 자기 스코프를 가지며 `Application`에도 하나가 있습니다. 한 스코프에는 명령당 처리기가 하나뿐이고, `Register`가 돌려주는 `CommandRegistration`을 dispose하거나 `Unregister`를 부르면 등록이 제거됩니다. `CommandScope.Parent`로 시각 트리와 독립적인 의미 스코프 사슬을 만들 수도 있습니다.

등록 모양은 무엇을 결합하느냐로 갈립니다. 이름은 전부 `Register`입니다.

| 결합하는 것 | 모양 | 뜻 |
|---|---|---|
| 델리게이트 | `Register(cmd, Action, Func<bool>?)` | 실행과 가능 여부 |
| 호출 문맥 | `Register(cmd, Action<CommandContext>, Func<CommandContext, bool>?)` | 창, 호출 출처 요소, 취소 토큰이 필요할 때. 비동기 형태 `Func<CommandContext, ValueTask>`도 있습니다 |
| 대상 객체 | `Register(cmd, target, static t => ..., static t => ...)` | 클로저 없는 정적 람다 |
| 인자 타입 | `Register(cmd, (Item item) => ..., (Item item) => ...)` | 호출 인자를 받는 처리기 (인자 절) |

처리기는 **그 상태를 소유한 요소**의 스코프에 둡니다. 문서를 편집하는 명령은 편집기에, 채팅 메시지에 작용하는 명령은 메시지 목록을 담은 카드에, 도형의 채우기를 바꾸는 명령은 그 도형에 둡니다. 항목 컨테이너처럼 재활용되는 요소에는 두지 않습니다. 컨테이너가 다른 항목을 받으면 등록이 엉뚱한 항목을 가리키게 됩니다. 어느 항목에서 호출됐는지는 인자 절의 방법으로 받습니다.

명령을 쓸지 바인딩을 쓸지는 소비 표면의 수로 정합니다. 표면 하나가 상태 하나를 편집하면(폼의 체크박스, 라디오 그룹, 세그먼트 컨트롤) 바인딩입니다. 같은 의미를 표면 둘 이상이 공유하거나(메뉴와 툴바와 단축키), 포커스에 따라 뜻이 달라지면(활성 문서의 저장) 명령입니다.

## 타깃: 어디서 시작하는가

타깃(`CommandTarget`)은 라우터가 처리기를 찾기 시작하는 지점입니다. 요소이거나 독립 스코프이며 내용을 열어 보지 않는 불투명한 값입니다. 라우터는 타깃 요소의 컨텍스트 사슬을 올라가며 스코프를 찾고, 없으면 `CommandRouter.FallbackTarget`, 창 스코프, `Application` 스코프 순으로 봅니다. 키 제스처도 같은 순서로 가장 가까운 `InputMap`이 의미를 정합니다.

가장 가까운 처리기가 명령을 소유합니다. 그 처리기의 `CanExecute`가 거짓이어도 더 먼 스코프로 넘어가지 않습니다. 이 규칙 덕분에 하위 스코프가 상위의 명령을 "지금은 안 됨"으로 가릴 수 있습니다.

표면마다 타깃을 잡는 방식이 다릅니다.

| 표면 | 타깃 |
|---|---|
| Button, ToggleButton, 툴바 항목 | 자기 자신 |
| 단축키 | 포커스된 요소 |
| ContextMenu | 열릴 때의 배치 타깃(`PlacementTarget`)을 캡처. 메뉴가 포커스를 가져가도 원래 요소를 대상으로 실행하며 하위 메뉴는 같은 타깃을 물려받음 |
| MenuBar | 메뉴를 열기 직전의 포커스 요소 |
| 독립 스코프를 쓰는 동적 메뉴 | `menu.SetCommandTarget(CommandTarget.From(scope))` |

툴바처럼 문서 밖에 있는 표면이 활성 문서의 처리기에 닿으려면 셸이 `CommandRouter.FallbackTarget`을 활성 문서로 가리켜야 합니다. 처리기는 타깃이 무엇이었는지 알지 못하고 알 필요도 없습니다. 같은 처리기가 메뉴에서 호출되든 단축키로 호출되든 같은 뜻으로 실행되는 것이 목표이며, 그래서 `CommandContext`는 타깃을 노출하지 않습니다. 호출된 대상이 필요하면 인자 절의 방법을 씁니다.

```csharp
var menu = new ContextMenu();
var scope = new CommandScope();
var select = new Command("document.select", "Select");

scope.Register(select, SelectDocument, CanSelectDocument);
menu.Item(select);
menu.SetCommandTarget(CommandTarget.From(scope));
menu.Show(owner);
```

## 인자: 무엇에 대해 실행하는가

목록 위에 뜬 컨텍스트 메뉴에서 "삭제"를 고르면 처리기는 어느 항목인지 알아야 합니다. 이 값을 인자라고 부르며 프레임워크가 찾아서 처리기에 넘깁니다.

인자를 제공하는 요소는 `ICommandArgumentSource`를 구현합니다. 항목 컨트롤이 만드는 `ItemContainer`와 `GridViewRow`가 이미 구현하고 있어 둘의 `Item`이 인자가 됩니다. 규칙은 하나입니다. **호출 앵커에서 가장 가까운 제공자의 값이 인자입니다.** 앵커는 타깃 절의 표와 같습니다. 메뉴는 배치 타깃에서, 단축키는 포커스 요소에서, 버튼은 자기 자신에서 올라갑니다. 항목 템플릿 안의 버튼은 그래서 자기 항목을 인자로 받습니다.

받는 쪽은 타입을 적은 처리기입니다.

```csharp
var delete = new Command("chat.delete", "Delete");
var reply = new Command("chat.reply", "Reply");

card.Commands.Register(delete, (ChatMessage msg) => messages.Remove(msg), (ChatMessage msg) => msg.Mine);
card.Commands.Register(reply, (ChatMessage msg) => input.Value = $"@{msg.Sender} ");

list.PrepareContainer<ChatMessage>((container, _, _, _) => container.ContextMenu = messageMenu);
```

처리기에는 메뉴 참조도, 조상 탐색도, 캐스팅도 없습니다. 람다의 파라미터 타입은 반드시 적습니다. `msg => ...`처럼 타입을 생략하면 `CommandContext`를 받는 모양으로 해석됩니다.

인자가 없거나 타입이 다르면 그 처리기는 실행할 수 없는 상태로 평가되어 메뉴 항목이나 버튼이 비활성으로 그려집니다. 위 예제의 `msg.Mine` 조건처럼 항목별로 활성화가 달라지는 것도 같은 경로입니다. 가장 가까운 처리기가 명령을 소유한다는 타깃 절의 규칙은 여기에도 적용됩니다. 타입을 적은 처리기가 있는 사슬 안에서 인자 없이 호출하면 바깥 스코프의 인자 없는 처리기로 넘어가지 않습니다.

ContextMenu는 열리는 순간 인자를 캡처합니다. 메뉴가 열려 있는 동안 목록이 스크롤되어 컨테이너가 다른 항목을 받아도, 항목을 고르면 메뉴가 열렸던 항목에 작용합니다. 메뉴가 열린 뒤 그 항목이 제거됐다면 처리기는 이미 없는 항목을 받으므로 제거 처리기는 아무 일도 하지 않습니다.

## 데이터: 어떤 값을 고르는가

정렬(왼쪽/가운데/오른쪽)이나 채우기 색처럼 값 하나를 고르는 명령은 항목마다 값이 다릅니다. 이 값은 명령이 아니라 **표면 항목이 `CommandData`로 싣습니다.** 명령은 "채우기를 바꾼다"는 동사 하나이고 어느 값인지는 항목의 데이터입니다.

```csharp
var setFill = new Command("shape.fill", "Fill");
sharp.Commands.Register(setFill, (Color color) => sharp.Fill(color));
rounded.Commands.Register(setFill, (Color color) => rounded.Fill(color));

var fillMenu = new ContextMenu()
    .Item("Blue", setFill, Color.FromRgb(70, 130, 230))
    .Item("Green", setFill, Color.FromRgb(100, 200, 120));

sharp.ContextMenu(fillMenu);
rounded.ContextMenu(fillMenu);
```

항목이 데이터를 선언하면 그 값이 호출 인자가 되어 타입을 적은 처리기가 받습니다. 값 집합이 커도, 실행 중에 바뀌어도 명령은 하나입니다. 심볼 목록이나 폰트 목록 메뉴를 명령 하나와 생성된 항목으로 만들 수 있습니다. 다만 값을 편집기처럼 보여 주는 표면(콤보 박스, 슬라이더)은 여전히 바인딩의 영역입니다.

데이터는 모든 호출 표면이 실을 수 있습니다.

| 표면 | 선언 |
|---|---|
| 메뉴 항목 | `Item(text, command, data)`, `MenuItem.CommandData` |
| Button, ToggleButton | `CommandData` 속성, 플루언트 `.CommandData(value)` |
| 툴바 항목 | `Item(command, data, icon)`, `Toggle(command, data, icon)`, `ToolBarItem.CommandData` |
| 단축키 | `Map(command, data, gesture)`. 같은 명령을 데이터마다 다른 제스처에 매핑할 수 있습니다 |

항목이 데이터를 선언하면 인자 절의 피연산자 대신 그 데이터가 인자로 갑니다. 호출 하나는 인자 하나를 받습니다.

값 항목들이 명령 하나를 공유하므로 텍스트와 아이콘은 항목마다 적습니다. 메뉴 항목의 텍스트, 툴바 항목의 `Text`와 `Icon` 오버라이드가 그것입니다. 단축키 표시는 명령과 데이터의 짝으로 찾으므로 "Left" 항목 옆에는 Ctrl+L만 붙습니다.

## 체크 상태

켜짐/꺼짐 상태는 명령이 아니라 표면의 것입니다. `ToggleButton.IsChecked`는 컨트롤의 것이고, 툴바 `Toggle` 항목의 체크 상태는 `ToolBarToggleItem.IsChecked`의 것입니다. 만들어진 컨트롤이 사용자의 변경을 그 값에 다시 씁니다. 메뉴 항목에는 체크 표시가 없습니다. 같은 상태를 여러 표면이 보여 주어야 하면 각 표면에 그 상태를 바인딩합니다.

## C# Markup에서 사용

Button은 `Command(...)` 또는 `BindCommand(...)`로 의미 동작에 연결합니다. 기본값은 명시적인 `Content`를 사용하며, Command의 표시를 자동 생성하려면 `CommandPresentationMode`를 함께 지정합니다.

```csharp
new Button()
    .Command(save, presentation: CommandPresentationMode.TextAndIcon)
```

명시적으로 설정하거나 바인딩한 `Content`가 있으면 자동 생성한 Command 표시보다 우선합니다.

`DropDownButton`은 의도적으로 Command 소비자가 아닙니다. 어느 부분을 활성화해도 `DropDownMenu`를 열며 Command는 메뉴 항목이 가집니다. `SplitButton`은 Button이므로 primary face에서 상속된 `Command`를 실행하고 drop-down face는 메뉴만 엽니다.

```csharp
var more = new DropDownButton
{
    Content = new TextBlock().Text("More"),
    DropDownMenu = new Menu().Item(exportPdf).Item(print),
};

var saveSplit = new SplitButton
{
    Content = new TextBlock().Text("Save"),
    Command = save,
    DropDownMenu = new Menu().Item(saveAs).Item(saveAll),
};
```

`save`를 실행할 수 없으면 `SplitButton`의 primary face만 비활성화되고 drop-down은 계속 열 수 있습니다. 소유 컨트롤만 primary Command 소스이며, 템플릿 내부 버튼은 활성화를 전달할 뿐 Command를 직접 실행하지 않습니다.

`ButtonGroup`의 각 컨테이너인 `SegmentButton`도 독립적인 Command 소비자입니다. 항목별 Command는 `PrepareContainer`에서 연결하며 `CanExecute`는 해당 세그먼트의 유효 enabled 상태에 반영됩니다.

```csharp
new ButtonGroup()
    .Items(alignmentCommands, command => command.Text)
    .PrepareContainer<Command>((segment, command, _) =>
        segment.Command(command));
```

`SegmentedControl`은 값 하나를 선택하는 선택 컨트롤이므로 Command 소비자로 바뀌지 않습니다. 정렬의 좌/중/우/분배처럼 각 항목이 독립 동작이면 위와 같이 `ButtonGroup`을 쓰고, 현재 정렬 값을 선택 상태로 바인딩하려면 `SegmentedControl.SelectedIndex`/`SelectedItem`을 씁니다. 항목마다 실행할 Command 자체를 받으므로 이 연결에는 `CommandData`가 필요하지 않습니다.

### ToolBar

`ToolBar`는 band를, band는 group을, group은 entry를 담습니다. entry는 컨트롤이 아니라 모델이며, 툴바가 entry마다 컨트롤 하나를 만들고 band가 담지 못한 group은 그 band의 overflow 버튼 뒤로 숨깁니다. `Item`, `Toggle`, `Split`은 Command를 받고 `Menu`, `Label`, `Splitter`, `Host`는 받지 않습니다.

```csharp
var save = new Command("file.save", "_Save", saveIcon);
var wrap = new Command("view.wordWrap", "_Word wrap", wrapIcon);

var bar = new ToolBar()
    .Band(
        new ToolBarGroup()
            .Item(new Command("file.new", "_New", newIcon))
            .Split(save, new Menu().Item(saveAs).Item(saveAll)),
        new ToolBarGroup()
            .Label("View")
            .Toggle(wrap, isChecked: true)
            .Menu("Zoom", new Menu().Item(zoomIn).Item(zoomOut), zoomIcon)
            .Separator()
            .Host(new TextBox().Width(140).Placeholder("Search")));

bar.Commands.Register(save, () => document.Save(), () => document.IsDirty);
```

`Item`은 버튼이 되고, `Split`은 primary face가 Command를 실행하고 chevron이 메뉴를 여는 `SplitButton`이 되며, `Menu`는 자체 Command가 없는 `DropDownButton`이 됩니다. `Host`는 band가 감출 수 없는 유일한 entry입니다. 임의의 요소는 접혀 들어갈 자리가 없으므로 대신 자신의 최소 크기까지 줄어듭니다.

`Toggle`은 Command를 실행하면서 켜짐/꺼짐 상태도 표시합니다. 체크 상태는 Command가 아니라 entry의 것입니다. `ToolBarToggleItem.IsChecked`가 원본이고, 만들어진 컨트롤이 사용자의 변경을 그 값에 다시 씁니다.

값을 고르는 항목은 데이터와 항목별 아이콘을 함께 선언합니다. 명령 하나를 세 항목이 공유하므로 아이콘은 명령이 아니라 entry에서 옵니다.

```csharp
new ToolBarGroup()
    .Toggle(setAlignment, TextAlignment.Left, alignLeftIcon)
    .Toggle(setAlignment, TextAlignment.Center, alignCenterIcon)
    .Toggle(setAlignment, TextAlignment.Right, alignRightIcon)
```

entry는 `ToolBar.ItemPresentation`에 따라 Command를 표시하며 기본값은 `CommandPresentationMode.Icon`입니다. entry 하나만 `ToolBarItem.Presentation`으로 덮어쓸 수 있고, `Text`와 `Icon`으로 명령의 표시 대신 자기 것을 보일 수 있습니다. 아이콘 전용 entry에 아이콘이 없으면 비어 보이는 대신 텍스트로 대체합니다. 아이콘은 메뉴와 같은 크기인 `ThemeMetrics.CommandIconSize`로 그립니다.

### 반응형 표시와 지역화

`Command.Presentation.AccessText`와 `Icon`은 MewProperty입니다. `Command.BindText(...)`와 `BindIcon(...)`은 값을 한 번 복사하는 편의 메서드가 아니라 각각 `AccessTextProperty`와 `IconProperty`에 실제 단방향 바인딩을 만듭니다.

```csharp
var save = new Command("file.save", icon: saveIcon)
    .BindText(AppStrings.Save); // ObservableValue<string>, 예: "_Save"
```

값이 바뀌면 Command의 `Text`/AccessKey가 다시 계산되고, 해당 Command를 기본 표시로 쓰는 열린 메뉴와 opt-in Button도 갱신됩니다. `CommandPresentation`에는 `CanExecute`, 선택/체크 상태, 단축키, 호출 값을 넣지 않습니다. 이 값들은 각각 실행 상태, 소비자 상태, `InputMap`, 항목의 `CommandData`에 속합니다.

메뉴도 callback이나 자체 단축키를 소유하지 않습니다. Command의 기본 텍스트와 AccessKey를 함께 사용하거나, 항목이 표시되는 문맥에 맞춰 둘을 함께 덮어쓸 수 있습니다.

```csharp
var fileMenu = new Menu()
    .Item(save)
    .Item("Save _As...", saveAs)
    .Separator()
    .Item("Unavailable", isEnabled: false); // 표시 전용 항목
```

`Item(string, Command)`과 `MenuItem.Text`의 문자열도 같은 `_` 규칙을 사용합니다. 명시적인 항목 텍스트는 Command의 기본 텍스트와 AccessKey를 모두 덮어쓰므로, 같은 명령을 다른 메뉴에서 다른 AccessKey로 표시할 수 있습니다.

메뉴의 단축키 열은 현재 command target에서 실제로 유효한 `InputMap` 제스처를 역조회해 표시합니다. 따라서 메뉴에 별도 단축키를 중복 선언하지 않습니다.

Command 아이콘은 표시 위치가 요청한 크기로 새 visual을 만드는 `IconTemplate`로 정의합니다. `IconTemplateSize.Dip`은 레이아웃 크기이며 `Pixel`은 현재 DPI에서 필요한 물리 픽셀 크기입니다. ContextMenu와 MenuBar dropdown, ToolBar entry 모두 `ThemeMetrics.CommandIconSize`(16 DIP)를 요청합니다.

```csharp
var copyGeometry = PathGeometry.Parse(copyPathData);
copyGeometry.Freeze();

var copyIcon = new IconTemplate(
    size => new PathShape()
        .Data(copyGeometry)
        .Size(size.Dip)
        .Stretch(Stretch.Uniform));

var copy = new Command("edit.copy", "Copy", copyIcon);
```

`IconTemplate.Build`는 호출될 때마다 parent가 없는 새로운 `FrameworkElement`를 반환해야 합니다. 따라서 여러 표면이 같은 Command를 동시에 표시해도 visual parent가 충돌하지 않습니다. `ImageSource`, `SvgImageSource`, freeze한 `PathGeometry`처럼 visual이 아닌 리소스는 factory 밖에서 만들어 공유합니다. 렌더링 프레임이나 `CanExecute` 평가마다 생성하는 것이 아니라 표면이 만들어질 때 한 번 생성합니다. 비트맵 factory는 `size.Pixel` 이상인 가장 작은 source를 선택하고 visual은 `size.Dip`으로 배치할 수 있습니다. DPI가 변경되면 활성 표면은 새 크기로 icon visual을 다시 생성합니다.

현재 코어에서 Command 아이콘을 자동 materialize하는 소비자는 ContextMenu, MenuBar dropdown, ToolBar 항목, 표시 모드를 지정한 Button(`SplitButton`의 primary face 포함)입니다. Button의 기본 동작은 여전히 명시적 `Content`입니다.

MenuItem별로 Command 아이콘을 덮어쓸 수도 있습니다.

```csharp
new MenuItem("_Copy", copy)
    .Icon(compactCopyIcon);
```

MenuItem의 `Text`와 `Icon`은 **placement override**입니다. 속성에 값 source가 없을 때만 Command 기본값을 사용합니다. 따라서 명시적인 빈 문자열은 텍스트를 숨기고, 명시적인 `null` 아이콘은 Command 아이콘을 숨깁니다. `BindText`, `BindIcon`, `BindCommand`, `BindIsEnabled`는 각각 해당 MenuItem MewProperty에 실제 바인딩을 만듭니다. 로컬 `IsEnabled`는 `CanExecute`와 AND로 결합되며 바인딩이 덮어써지지 않습니다.

## ContextMenu 배치

`ContextMenu`는 `Show(placementTarget)`로 엽니다. 어디에 뜰지는 `Placement`가 정합니다.

| `Placement` | 위치 |
|---|---|
| `Pointer` (기본) | 포인터 위치. `Show(target, positionInWindow)`로 좌표를 넘깁니다 |
| `Below` / `Above` | 타깃의 아래/위 모서리. 자리가 없으면 반대쪽으로 뒤집습니다 |
| `Right` / `Left` | 타깃의 오른쪽/왼쪽 옆. 자리가 없으면 반대쪽으로 뒤집습니다 |

`PlacementOffset`으로 그 위치에서 밀 수 있고, 열린 뒤에는 `PlacementTarget`이 어느 요소 위에 열렸는지 알려 줍니다. 요소의 `ContextMenu` 속성에 단 메뉴는 우클릭에 자동으로 열리며, `Placement`가 `Pointer`면 포인터 위치에, 아니면 그 요소를 기준으로 엽니다. 하위 메뉴는 부모의 배치 타깃과 명령 타깃을 물려받습니다.

```csharp
new Button()
    .Content("Options")
    .ContextMenu(new ContextMenu { Placement = MenuPlacement.Below }
        .Item(exportPdf)
        .Item(print));
```

## 표준 편집 명령

`StandardCommands`는 `Cut`, `Copy`, `Paste`, `Delete`, `Undo`, `Redo`, `SelectAll`을 제공합니다. TextBox 계열은 이 명령의 처리기를 자신의 스코프에 등록합니다. 기본 키는 Application `InputMap`에 매핑되므로 로컬 또는 Window `InputMap`에서 재매핑하거나 가릴 수 있습니다.

```csharp
editor.InputMap.Map(StandardCommands.Copy, new KeyGesture(Key.Insert, ModifierKeys.Control));
```

## TextBox, ContextMenu, InputMap과 Edit 메뉴

다음 그림은 타입 상속 관계나 실제 visual tree를 나타내지 않습니다. 같은 편집 의미가 여러 UI 진입점에서 어떻게 하나의 명령 실행으로 합쳐지는지 보여 주는 **논리적 구성도**입니다.

```text
키보드 Primary+X/C/V
  └─ Application InputMap ───────────────┐
                                         │
TextBox 우클릭 ContextMenu                ├─ StandardCommands.Cut/Copy/Paste
  └─ Cut / Copy / Paste 메뉴 항목 ───────┤             │
                                         │             ▼
MenuBar의 Edit 메뉴                       │    현재 command target에서
  └─ Cut / Copy / Paste 메뉴 항목 ───────┘    TextBox의 처리기 실행
                                                       │
                                                       ▼
                                               선택 영역/클립보드 변경
```

`TextBox`는 생성될 때 `Cut`, `Copy`, `Paste`의 실행과 `CanExecute` 처리기를 자신의 `Commands`에 등록합니다. Application의 기본 `InputMap`은 `Primary+X`, `Primary+C`, `Primary+V`를 각각 같은 표준 명령으로 변환합니다. ContextMenu와 Edit 메뉴는 실행 delegate를 따로 갖지 않고 그 `Command`만 참조합니다.

```csharp
var editor = new TextBox()
    .Text("Select text, then use Cut or Copy.")
    .ContextMenu(
        new ContextMenu()
            .Item(StandardCommands.Cut)
            .Item(StandardCommands.Copy)
            .Item(StandardCommands.Paste));

var editMenu = new Menu()
    .Item(StandardCommands.Cut)
    .Item(StandardCommands.Copy)
    .Item(StandardCommands.Paste);

var menuBar = new MenuBar()
    .Items(new MenuItem("_Edit").Menu(editMenu));
```

이 예제에서 메뉴 객체가 TextBox의 명령 처리기를 복제하는 것은 아닙니다. 실제 연결은 메뉴를 열거나 키를 누른 시점의 **command target**으로 결정됩니다. 키보드는 포커스된 TextBox에서 시작해 유효한 `InputMap`을 찾고, TextBox의 ContextMenu는 우클릭한 TextBox를 타깃으로 캡처하며, MenuBar의 Edit 메뉴는 메뉴를 열기 직전의 포커스 타깃을 보존합니다. 세 경로는 같은 `CanExecute` 결과도 공유합니다. 선택 영역이 없으면 `Cut`과 `Copy`가 ContextMenu와 Edit 메뉴에서 모두 비활성화되고, `IsReadOnly`인 TextBox에서는 `Cut`과 `Paste`가 비활성화됩니다. 메뉴의 단축키 표시는 현재 타깃에서 유효한 `InputMap`을 역조회하므로 키를 재매핑해도 메뉴에 별도 단축키 문자열을 수정할 필요가 없습니다.

## 갱신 시점

`CanExecute`와 인자 술어는 부작용이 없는 빠른 함수여야 합니다. 프레임워크는 값을 저장하지 않고 필요할 때 다시 묻습니다. 묻는 시점은 다음과 같습니다.

- 디스패처가 작업을 처리한 턴이 끝날 때
- 마우스 버튼을 뗐을 때, 포커스가 바뀌었을 때, 창 상태가 바뀌었을 때
- 메뉴가 열릴 때, 그리고 열려 있는 동안 위의 시점마다
- 앱이 `window.RequerySuggested()`를 불렀을 때

연결된 Button과 열린 메뉴처럼 추적 중인 표면만 다시 평가하며 전체 트리를 훑지 않습니다. 실행 직전에는 표시 상태와 관계없이 `CanExecute`를 다시 확인합니다.

일반 필드처럼 프레임워크가 변경을 관찰할 수 없는 상태를 위 시점 밖에서 바꾼 경우에는 `RequerySuggested()`를 부르거나 디스패처를 통해 바꿔야 표면이 따라옵니다. `Button.CanClick`과 `MenuItem.CanClick`은 명령이 과하다 싶은 로컬 조건을 위한 술어이며, 로컬 `IsEnabled`, 명령의 `CanExecute`와 AND로 결합되어 셋 중 하나만 거짓이어도 비활성이 됩니다. 이 술어도 같은 시점에 다시 물어봅니다.

## 수명 관리

Button은 visual root에 연결된 동안에만 command source로 추적됩니다. ContextMenu는 열린 동안만 추적됩니다. Window가 닫히면 해당 Window의 source tracker가 비워집니다. 장기 생존 스코프에 임시 처리기를 등록했다면 `CommandRegistration`을 dispose해 캡처한 객체가 불필요하게 유지되지 않도록 합니다.

## 제거된 레거시 API

다음 경로는 Command System과 중복 실행 또는 서로 다른 활성 상태를 만들기 때문에 제거되었습니다.

- `Window.KeyBindings`, `Window.ProcessKeyBindings`, 코어 `KeyBinding`
- `MenuItem.Click`, `MenuItem.Shortcut`
- callback 기반 `Menu.Item`/`ContextMenu.Item` 및 shortcut 인자
- `ContextMenu.ShowAt`는 `Show`와 `Placement`로 대체되어 obsolete입니다

단순 UI 클릭 이벤트인 `Button.Click`/`OnClick`은 그대로 사용할 수 있지만, 재사용 동작·활성 조건·단축키·메뉴와 공유되는 동작에는 Command를 사용합니다.

## 아이콘 수명과 크기

`Command.Icon`과 `MenuItem.Icon`의 타입은 `IconTemplate?`입니다. `MenuItem.Icon`에 값 source가 없을 때만 Command 아이콘을 사용하며 명시적으로 설정한 null은 아이콘을 숨깁니다. ContextMenu는 열릴 때 각 command item의 template을 16 DIP로 build하고 닫힐 때 생성한 visual의 parent를 해제합니다. 다시 열면 새 visual을 만듭니다.

factory에는 DIP와 현재 DPI에서 계산된 목표 pixel 크기가 함께 전달됩니다. DPI 변환과 disabled opacity는 표면이 담당합니다. 아이콘 source를 factory 안에서 매번 파싱하지 말고 공유 가능한 source를 캡처해야 합니다. factory가 반환한 element는 표면이 정사각형 slot으로 제한하므로 vector와 bitmap에는 `Stretch.Uniform`을 권장합니다.

## 한눈에

| 축 | 질문 | 담는 곳 | 표면이 아는 것 |
|---|---|---|---|
| Command | 무슨 동작인가 | `Command` 인스턴스 | 정체성과 표시 |
| 스코프 | 누가 실행하는가 | `Element.Commands`, `Window.Commands`, `Application` | 모름 |
| 타깃 | 어디서 시작하는가 | 표면이 호출 시점에 잡음 | 자기 방식대로 잡음 |
| 인자 | 무엇에 대해 | 앵커에서 가장 가까운 `ICommandArgumentSource` | 메뉴는 열릴 때 캡처 |
| 데이터 | 어떤 값인가 | 표면 항목의 `CommandData` | 자기 값을 선언 |
