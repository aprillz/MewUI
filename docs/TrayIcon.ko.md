# 트레이 아이콘

`TrayIcon`은 Windows 알림 영역, macOS 메뉴 막대, Linux 데스크톱의 상태 영역에 앱의 아이콘을 보이고, 메뉴를 붙일 수 있습니다. 창이 필요 없으므로 메인 창 없이 실행하는 유틸리티([Application 및 Window 수명 주기](ApplicationLifecycle.ko.md) 참고)가 트레이에만 머물 수 있습니다. 그 밖의 플랫폼에서는 `TrayIcon.IsSupported`가 false이고 아이콘은 아무 일도 하지 않습니다.

Linux에서는 StatusNotifierItem 규약을 사용하며 KDE Plasma와 Xfce 패널이 이를 표시합니다. GNOME은 AppIndicator 확장을 설치해야 표시합니다. 데스크톱에 표시할 곳이 없는 동안 `TrayIcon.IsSupported`는 false이고, 보이도록 설정한 아이콘은 표시할 곳이 생기면 저절로 나타납니다.

```csharp
var show = new Command("app.show", "_Show");
var exit = new Command("app.exit", "E_xit");

var tray = new TrayIcon()
    .Icon(IconSource.FromResource<App>("App.app.ico"))
    .ToolTipText("My App")
    .ContextMenu(new ContextMenu()
        .Item(show)
        .Separator()
        .Item(exit))
    .Command(show)
    .IsVisible();

tray.Commands.Register(show, ShowMainWindow);
tray.Commands.Register(exit, Application.Shutdown);
```

## 보이기와 지우기

앱이 실행 중이고 `IsVisible`이 true가 되어야 아이콘이 나타납니다. 아이콘은 다음 디스패처 차례에 만들어지므로, 객체 초기화나 플루언트 체인에서 `IsVisible` 뒤에 정한 속성도 처음부터 반영됩니다. `TrayIcon`은 `Application.Run` 전에 만들어도 되고, 실행이 시작되면 나타납니다.

`IsVisible`을 false로 바꾸면 아이콘이 사라지고, 다시 true로 바꾸면 다시 나타납니다. `Dispose`는 아이콘을 영구히 지웁니다. 실행이 끝나면 모든 트레이 아이콘이 지워지고 해제되므로 알림 영역에 아이콘이 남지 않습니다.

`Icon`이 없으면 앱 자체의 아이콘을 보입니다. `IsMonochromeIcon`은 아이콘이 단색 글리프라서 플랫폼이 막대 색에 맞춰 칠해도 된다는 뜻입니다. macOS 메뉴 막대는 칠하고, 다른 플랫폼은 아이콘을 그대로 보입니다.

`TrayIcon`은 UI 스레드에서 사용합니다.

## 메뉴

메뉴는 요소의 컨텍스트 메뉴와 같은 방식으로 만드는 보통의 `ContextMenu`입니다. MewUI는 각 플랫폼의 트레이 메뉴 방식대로 보입니다.

| 플랫폼 | 표시 |
|---|---|
| Windows | `ContextMenu` 자체를 앱의 테마, 글꼴, 아이콘, 항목 배치 그대로 보입니다. 아이콘 위치에서 열리고 바깥 클릭, Esc, 다른 창 활성화로 닫힙니다. 키보드는 다른 MewUI 메뉴와 같게 동작합니다. |
| macOS | 열릴 때마다 `ContextMenu`를 읽어 네이티브 메뉴로 보입니다: 항목 텍스트(macOS 메뉴에는 접근 키가 없어 `_` 표시는 빠집니다), 구분선, 하위 메뉴, 사용 가능 상태, 체크 표시, 아이콘, 단축키 표시. 단색 아이콘은 메뉴의 색을 따릅니다. `ContextMenu` 자체의 외형(`ItemHeight`, `ItemPadding`, `MaxMenuHeight`, 글꼴)은 적용되지 않습니다. |
| Linux | 데스크톱 패널이 `ContextMenu`를 읽어 그립니다: 접근 키를 포함한 항목 텍스트, 구분선, 하위 메뉴, 사용 가능 상태, 체크 표시, 아이콘, 단축키 표시. `ContextMenu` 자체의 외형은 적용되지 않습니다. 항목 아이콘은 렌더링 컨텍스트가 필요하므로 GPU 백엔드에서는 앱이 창을 한 번 보인 뒤부터 나타납니다. |

`MenuOpening`은 메뉴가 열리기 직전에 발생합니다. 처리기는 메뉴를 채우거나 `ContextMenu`를 바꿀 수 있고, 열기를 취소할 수는 없습니다.

## 명령

아이콘의 `Command`와 메뉴 항목의 명령은 먼저 `TrayIcon.Commands`에서, 그다음 `Application.Current.Commands`에서 찾습니다. 실행은 `Application.Current.CommandRouter`가 하므로 열린 창이 없어도 동작하고, 처리기가 받는 `CommandContext`의 `Window`는 null입니다. 항목의 `CommandData`는 인자로 전달됩니다. [Command System](CommandSystem.ko.md#창-없이-실행하기)을 참고하세요.

항목은 그 명령에 실행할 수 있는 처리기가 있을 때만 사용할 수 있습니다. 메뉴 항목의 단축키 표시는 `Application.Current.InputMap`에서 가져옵니다.

## 클릭

플랫폼이 메뉴 열기와 별개로 아이콘의 기본 활성화를 알려 오면 `Click`이 발생하고 이어서 `Command`가 실행됩니다. `DoubleClick`은 플랫폼이 더블클릭을 알려 오는 곳에서 발생하며, 더블클릭을 이루는 클릭에서도 `Click`이 발생합니다. `TrayIconEventArgs.ScreenPosition`은 화면 위치(장치 픽셀)이고, 키보드로 활성화했으면 아이콘의 위치입니다.

| 플랫폼 | 클릭 | 더블클릭 | 메뉴 |
|---|---|---|---|
| Windows | 왼쪽 클릭, 또는 포커스가 있는 아이콘에서 Enter나 Space | 있음 | 오른쪽 클릭, Shift+F10, 메뉴 키 |
| macOS | 아이콘에 메뉴가 없을 때 클릭 | 없음 | 아이콘에 메뉴가 있을 때 클릭 |
| Linux | 패널이 알려 오는 왼쪽 클릭 | 없음 | 오른쪽 클릭. 일부 패널은 왼쪽 클릭에도 메뉴를 열고 `Click`을 알리지 않으므로, 모든 동작을 메뉴에도 넣어 주세요 |

## 앱 식별자

`WithApplicationId`는 데스크톱에 앱의 이름을 알립니다. 데스크톱은 이 이름으로 앱의 창, 트레이 아이콘, 실행 항목을 하나로 묶습니다.

```csharp
Application.Create()
    .WithApplicationId("com.example.MyApp")
    .BuildMainWindow(() => new MainWindow())
    .Run();
```

Linux에서는 창 클래스와 트레이 항목의 id로 쓰이며, 앱의 `.desktop` 파일 이름과 같아야 합니다. 지정하지 않으면 진입 어셈블리 이름을 씁니다. Windows에서는 프로세스의 AppUserModelID이고, 앱이 지정할 때만 설정합니다. macOS는 번들 식별자를 대신 사용합니다.
