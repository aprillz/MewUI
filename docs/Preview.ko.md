# 에디터 프리뷰

이 문서는 MewUI **에디터 프리뷰**(VS Code 확장)의 동작 방식과, 개발 중 프리뷰를 원활하게 운용하기 위한 코드 작성 지침을 설명합니다.

프리뷰는 앱을 직접 실행하지 않고도 `Window`와 `UserControl`의 렌더링 결과를 에디터 패널에서 실시간으로 확인하는 기능입니다. 세션을 열면 최초 한 번 빌드가 이루어지고, 이후에는 저장할 때마다 Hot Reload로 보통 1초 안에 화면이 갱신됩니다.

---

## 1. 시작하기

요구 사항:

- .NET SDK 8.0 이상 (10.0 권장)
- 신뢰된 워크스페이스 (프리뷰가 프로젝트 코드를 빌드/실행하므로)
- MewUI 메타패키지(`Aprillz.MewUI`, `Aprillz.MewUI.Windows` 등) 참조 프로젝트는 추가 설정 없이 동작합니다. 저장소를 프로젝트 참조로 쓰는 경우에만 실행 프로젝트에 targets를 직접 import 합니다:

```xml
<Import Project="..\..\MewUI\build\Aprillz.MewUI.targets" />
```

사용: C# 파일을 열고 명령 팔레트에서 **MewUI: Start Preview**를 실행합니다. 활성 파일이 속한 실행 프로젝트로 세션이 열립니다 (라이브러리 파일이면 그 라이브러리를 참조하는 실행 프로젝트를 찾습니다).

프리뷰 세션에서는 앱 창이 화면에 뜨지 않습니다. 모든 창은 오프스크린으로 렌더되어 패널에만 표시됩니다.

---

## 2. 프리뷰 대상

대상 드롭다운의 구성:

| 항목 | 의미 |
|---|---|
| 맨 위 `Application main window` | 앱 진입점이 `Application.Run(...)`에 넘긴 **실행 중인 그 창**. `Main`이 로드한 실제 설정과 상태가 반영됩니다 |
| `타입명 (window)` | 스캔된 Window 타입. 선택하면 **새 인스턴스를 생성**해서 표시합니다 |
| `타입명 (usercontrol)` | 스캔된 UserControl 타입. 자동 크기 래퍼 창에 담아 표시합니다 |

대상 스캔 규칙:

- 실행 어셈블리와 참조 어셈블리 전체에서 `Window`/`UserControl` 서브클래스를 찾습니다. `internal` 타입도 포함됩니다.
- **매개변수 없는 생성자 또는 모든 매개변수에 기본값이 있는 생성자**가 있어야 생성 가능합니다. 없는 타입도 목록에 남되 비활성으로 표시되고 사유가 나타납니다.
- 편집 중인 파일에 선언된 대상이 있으면 자동으로 선택됩니다 (`mewui.preview.autoSelectTarget` 설정으로 제어).

---

## 3. 프리뷰 친화적 코드 지침

### 3.1 부수 효과는 `Design.IsPreviewMode`로 가드

프리뷰는 **실제 앱을 그대로 실행**합니다. 진입점 `Main`이 통째로 실행되고, 대상 타입의 생성자도 실제로 호출됩니다. 따라서 소켓, 백그라운드 서비스, 트레이 아이콘, 전역 훅처럼 밖으로 드러나는 부수 효과는 프리뷰에서도 실제로 발생합니다. 예를 들어 서버 포트를 여는 앱은 실제 앱과 프리뷰 세션이 포트를 두고 충돌할 수 있습니다.

`Design.IsPreviewMode`는 프로세스 시작 시점(`Main` 이전)에 확정되는 값이므로 `Main` 첫 줄부터 신뢰할 수 있습니다:

```csharp
var config = LoadConfig();                  // 화면에 필요한 것은 그대로 실행
if (!Design.IsPreviewMode)
{
    trayIcon.Install();
    server.Start(config.Port);              // 부수 효과만 가드
}
Application.Run(BuildMainWindow(config));
```

가드 대상을 고르는 기준: **테마, 폰트, 스타일, 창 구성처럼 "보이는 것"은 실행되게 두고, 밖으로 나가는 효과만 막습니다.** 화면 구성까지 가드하면 프리뷰 충실도가 떨어집니다.

생성자 부수 효과는 대상 전환과 Hot Reload 재빌드 때마다 반복 실행된다는 점도 유의하세요. 생성자에서 타이머나 구독을 만드는 타입이라면 프리뷰 분기 또는 정리 로직이 필요합니다.

### 3.2 생성자 주입 인자는 옵셔널 + 폴백으로

의존성을 생성자로 받는 창/컨트롤은 그대로 두면 프리뷰 목록에서 비활성이 됩니다. **인자를 옵셔널로 바꾸고 내부에서 기본값을 만들어 폴백**하면 호출부 변경 없이 프리뷰가 활성화됩니다:

```csharp
public SettingsDialog(AppConfig? config = null)
{
    _config = config ?? new AppConfig();    // 프리뷰: 기본값 / 실제: 주입값
    ...
}
```

기본 생성이 부담스러운 무거운 의존성(예: 다른 창, 서비스)은 nullable 필드로 두고 사용처에서 `?.` 처리하는 편이 안전합니다:

```csharp
public SetupWizard(AppConfig? config = null, MainWindow? mainWindow = null)
{
    _config = config ?? new AppConfig();
    _mainWindow = mainWindow;               // 프리뷰에서는 null
}
...
_mainWindow?.UpdateZeroconfService();
```

### 3.3 샘플 데이터

프리뷰는 실제 코드를 실행하므로 WPF의 디자인 타임 DataContext 같은 별도 개념이 없습니다. 샘플 데이터가 필요하면 C# 관용구로 해결합니다:

```csharp
protected override Element? OnBuild()
{
    var items = Design.IsPreviewMode
        ? SampleData.Clients                // 프리뷰: 목업
        : _service.LoadClients();           // 실제: 서비스
    ...
}
```

여러 상태를 한 화면에서 비교하고 싶으면 상태들을 나열한 작은 UserControl을 만드세요. 그 컨트롤 자체가 프리뷰 대상이 됩니다:

```csharp
internal sealed class ButtonStates : UserControl
{
    protected override Element? OnBuild() =>
        new StackPanel().Spacing(8).Children(
            new Button().Content("Normal"),
            new Button().Content("Disabled").Enabled(false));
}
```

### 3.4 프리뷰 크기 힌트

컴포넌트는 기본적으로 내용에 맞는 크기(desired size, 패널 크기로 클램프)로 표시됩니다. 특정 크기로 보고 싶으면 힌트를 지정합니다:

```csharp
public ProductCard()
{
    this.DesignSize(400, 300);      // 양축 고정
    // this.DesignWidth(400);       // 너비만 고정, 높이는 내용에 맞춤
}
```

힌트는 프리뷰 세션에서만 기록되고 프로덕션 실행 비용은 없습니다. Window 대상은 자신의 `WindowSize` 로직을 그대로 따르며, DesignSize 힌트가 있으면 그 축만 덮어씁니다.

### 3.5 빌드 관용구: 오버라이드와 콜백

프리뷰 대상이 되는 명명된 창/컨트롤은 가상 `OnBuild()` 오버라이드로 (타입 스캔으로 개별 프리뷰 가능), 조합 지점의 일회성 창은 fluent `Build(x => ...)`로 빌드를 정의합니다. 빌드 소유권(콜백 우선)과 **재실행 가능성 규칙**(1회성 스펙과 이벤트 구독은 빌드 밖에 - 프리뷰 재빌드가 위반을 바로 드러냅니다)은 [Hot Reload](HotReload.ko.md) 문서의 "빌드 코드 등록" 절이 규범입니다.

UserControl은 생성자에서 `Build()`를 부르지 않아도 됩니다. `OnBuild` 콘텐츠는 첫 레이아웃 때 자동으로 빌드되고(지연 빌드), 레이아웃 전에 콘텐츠가 필요한 경우에만 명시 호출이 필요합니다.

---

## 4. 갱신 파이프라인 이해

| 편집 종류 | 동작 | 체감 |
|---|---|---|
| 메서드 본문 수정 | Hot Reload delta 적용 후 활성 대상 재빌드 | 저장 후 1초 내 |
| 타입/시그니처 추가 등 rude edit | 프로세스 자동 재시작 후 재접속 | 수 초, 마지막 프레임 유지 |
| 컴파일 오류 | 세션 유지, 상태 표시줄에 오류 표기 | 수정 후 저장하면 재개 |
| 새 타입 추가 | delta 적용 시 대상 목록 자동 갱신 | 재시작 불필요 |

상태 표시줄에 갱신 경로(delta/재시작)가 표기되므로 갱신이 느릴 때 원인을 구분할 수 있습니다. 편집 종류별 재빌드 범위와 유지되는 상태의 규범은 [Hot Reload](HotReload.ko.md) 문서를 참고하세요 (프리뷰는 delta마다 활성 대상을 무조건 재빌드한다는 점만 다릅니다).

수동 복구 2단계:

- **Refresh**: 현재 대상의 OnBuild 재실행 + 재장착. 프로세스는 유지되므로 빠릅니다. 화면이 이상하면 1차로 시도하세요.
- **Restart**: 프로세스 전체 재시작. static/싱글턴 등 프로세스 상태가 오염된 경우의 최종 수단입니다.

패널을 닫아도 세션은 기본 10분간 유지되어 다시 열면 즉시 복귀합니다 (`mewui.preview.keepSessionMinutes`). 세션을 완전히 끝내려면 **MewUI: Stop Preview**를 실행하세요.

---

## 5. 설정 요약

| 설정 | 기본값 | 설명 |
|---|---|---|
| `mewui.preview.autoSelectTarget` | `true` | 활성 파일의 대상 자동 선택 |
| `mewui.preview.keepSessionMinutes` | `10` | 패널 닫은 뒤 세션 유지 시간. `0`이면 즉시 종료 |
| `mewui.preview.reloadDriver` | `auto` | `watch`(Hot Reload) / `buildRestart`(저장 시 재시작) / `auto`(watch 실패 시 폴백) |
| `mewui.preview.sessionStartTimeoutSeconds` | `60` | 앱 출력이 멎은 뒤 이 시간 동안 접속이 없으면 shim 세션으로 폴백. `0`이면 폴백 없음 |

---

## 6. 문제 해결

**세션 시작이 느리다**: 최초 시작은 콜드 빌드 비용이 그대로 듭니다. 이후에는 증분 빌드 + 복원 생략으로 수 초 수준이며, 패널을 닫았다 열 때는 세션 유지 덕에 즉시 복귀합니다.

**"shim session (low fidelity)" 이라고 표시된다**: 앱의 `Main`이 `Application.Run`에 도달하지 못해(블로킹, 조기 종료, 예외) 프리뷰가 진입점 없이 재시작한 상태입니다. 이 모드에서는 앱 테마/폰트가 적용되지 않습니다. `Main`에서 `Application.Run` 이전에 블로킹하는 코드가 있는지 확인하고, `Design.IsPreviewMode` 가드를 적용하세요.

**대상이 비활성으로 표시된다**: 생성자에 필수 인자가 있는 타입입니다. 3.2절의 옵셔널 + 폴백 패턴을 적용하거나, 인자를 채워 주는 프리뷰 전용 래퍼 UserControl을 만드세요.

**프리뷰가 실제 앱과 간섭한다 (포트 충돌 등)**: 3.1절의 부수 효과 가드를 적용하세요. 프리뷰 세션도 하나의 실제 프로세스입니다.

**갱신이 매번 수 초씩 걸린다**: 모든 저장이 rude edit로 처리되고 있는지 상태 표시줄에서 확인하세요. 메서드 본문 수정만으로도 재시작이 반복된다면 프로젝트 설정(Hot Reload opt-out 여부 등)을 점검하세요. `<MewUIHotReload>false</MewUIHotReload>` 프로젝트는 프리뷰가 저장 시 재시작 방식으로 동작합니다.

**PublishAot 프로젝트**: 별도 조치가 필요 없습니다. 프리뷰 세션은 JIT로 실행되며 필요한 설정(StartupHookSupport)은 세션 빌드에만 자동 적용됩니다. 게시 출력에는 영향이 없습니다.
