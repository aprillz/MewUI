# C# Markup Guide

MewUI의 C# Markup은 XAML 없이 순수 C# 코드로 UI를 선언적으로 구성할 수 있는 Fluent API입니다.
Native AOT 컴파일과 호환되며, Reflection을 사용하지 않습니다.
이 가이드는 개념과 명명 규칙을 다룹니다. 수신자별 메서드 표는 `src/MewUI/Markup`과 확장 패키지의 공개 확장 메서드에서 `docs/markup`으로 생성됩니다. [레퍼런스](#레퍼런스)를 참고하십시오.

## 컨셉

### 왜 C# Markup인가?

- **Native AOT 호환**: Reflection 없이 컴파일 타임에 모든 것이 결정됨
- **타입 안전성**: 컴파일러가 오류를 잡아줌
- **IntelliSense**: IDE 자동완성 지원
- **코드 재사용**: 일반 C# 메서드로 UI 컴포넌트 추출 가능

### 기본 패턴

<!-- snippet:basic-pattern -->
```csharp
var button = new Button()
    .Content("Click Me")
    .Width(100)
    .OnClick(() => Console.WriteLine("Clicked!"));
```
<!-- /snippet -->

모든 확장 메서드는 `this`를 반환하여 메서드 체이닝이 가능합니다.

## 네이밍 정책

### 속성 설정
| 패턴 | 설명 | 예시 |
|------|------|------|
| `PropertyName(value)` | 속성 직접 설정 | `.Width(100)`, `.Text("Hello")` |
| `PropertyName()` | bool 속성을 true로 설정 | `.Bold()`, `.IsChecked()` |

### 이벤트 핸들러
| 패턴 | 설명 | 예시 |
|------|------|------|
| `OnEventName(handler)` | 이벤트 핸들러 등록 | `.OnClick(...)`, `.OnTextChanged(...)` |

조건부 실행과 재사용 가능한 동작은 이벤트 확장이 아니라 `Command(...)`를 사용합니다. `BindCommand(...)`는 동적으로 제공되는 의미 `Command`를 위한 데이터 바인딩입니다.

같은 `On*` 메서드를 핸들러 delegate의 매개변수 타입만 바꿔 오버로드하지 않습니다. 타입이 명시되지 않은 람다는 두 오버로드에 모두 일치할 수 있으며, 기반 클래스용 확장과 파생 클래스용 확장 사이에서도 같은 문제가 발생합니다. `OnCheckStateChanged`, `OnLayoutSizeChanged`처럼 의미가 구분되는 이름을 사용합니다.

### 데이터 바인딩
| 패턴 | 설명 | 예시 |
|------|------|------|
| `BindPropertyName(source)` | ObservableValue 바인딩 | `.BindText(vm.Name)` |
| `BindPropertyName(source, convert)` | 단방향 변환 바인딩 | `.BindText(vm.Count, c => $"{c}개")` |
| `BindPropertyName(source, convert, convertBack)` | 양방향 변환 바인딩 | `.BindValue(vm.Level, x => (double)x, x => (int)x)` |

`Bind*` 편의 메서드는 컨버터 오버로드를 제공합니다. 기본 양방향 속성에서 `convertBack`을 생략하면 변환 바인딩은 의도적으로 단방향으로 동작합니다.

receiver가 `Command`인 `.BindText(source)`와 `.BindIcon(source)`도 같은 규칙을 따릅니다. 이 메서드들은 `Command.Presentation.AccessTextProperty`와 `IconProperty`에 실제 단방향 binding을 만듭니다. text source의 `_`는 AccessKey 표식이며, 니모닉을 지원하지 않는 소비자는 정규화된 `Command.Text`를 사용할 수 있습니다.

<!-- snippet:binding -->
```csharp
var nameBox = new TextBox()
    .BindText(viewModel.Name);
var countLabel = new Label()
    .BindText(viewModel.Count, count => $"{count} items");
var levelSlider = new Slider()
    .BindValue(viewModel.Level, level => (double)level, value => (int)value);
```
<!-- /snippet -->

### 속성 이름 별칭

Fluent 이름은 내부 저장 방식보다 공개 개념을 따릅니다. 복합 값은 같은 공개 메서드의 오버로드로 제공합니다.

<!-- snippet:naming-aliases -->
```csharp
var border = new Border()
    .BorderThickness(new Thickness(1, 2, 3, 4))
    .CornerRadius(new CornerRadius(4, 8, 12, 16));
```
<!-- /snippet -->

### 단축 메서드
자주 사용되는 속성은 간결한 단축 메서드 제공:
- `.Bold()` → `.FontWeight(FontWeight.Bold)`
- `.Horizontal()` → `.Orientation(Orientation.Horizontal)`
- `.Center()` → `.HorizontalAlignment(Center).VerticalAlignment(Center)`

<!-- snippet:shortcuts -->
```csharp
var title = new Label().Text("Title").Bold();
var row = new StackPanel().Horizontal();
var okButton = new Button().Content("OK").Center();
```
<!-- /snippet -->

### 참조 저장

`Ref(out field)`는 수신자를 변수에 저장한 뒤 그대로 반환하므로, 체인을 이어가면서도 요소를 나중에 다시 다룰 수 있습니다.

<!-- snippet:ref -->
```csharp
var textBox = new TextBox()
    .Ref(out var nameBox)
    .Text("Hello");
```
<!-- /snippet -->

### 델리게이트 타입 속성

타입이 델리게이트(`Func<...>`, `Action<...>`)인 속성은 그 자체가 호출 가능한 멤버라서 `element.Factory(value)`가 속성에 바인딩되고 동명 확장 메서드에는 닿지 않습니다. 이런 속성만 `With` 접두를 씁니다(`WithFactory(value)`). 매개변수에는 `[FluentSetsMember(nameof(Owner.Factory))]`를 선언해 분석기와 마크업 감사가 확장과 속성을 연결하게 합니다. 그 밖의 확장은 `With`나 `Set` 접두를 쓰지 않습니다.

```csharp
public static T WithCodeBlockFactory<T>(this T control, [FluentSetsMember(nameof(MarkdownPresenter.CodeBlockFactory))] Func<string, string?, FrameworkElement?>? factory) where T : MarkdownPresenter
```

### 확장 작성자를 위한 힌트

대부분의 확장에는 특성이 필요 없습니다. 동명 1인자 setter(`Width(double)`는 `Width`를 설정), `On` + 이벤트 이름 구독(`OnClick`은 `Click`을 구독), `Bind*` 메서드는 이름과 서명으로 인식됩니다. 이름으로 표현할 수 없는 관계는 `src/MewUI/Markup/Hints/FluentHintAttributes.cs`의 특성 네 가지로 선언합니다. 특성은 `internal`이고 메타데이터 이름으로 읽히므로, 확장 패키지는 같은 파일을 자기 어셈블리에 컴파일해 사용합니다.

| 특성 | 적용 대상 | 선언 내용 |
|---|---|---|
| `FluentSetsMember(memberName)` | 매개변수 | 이 인자가 해당 속성의 대입을 대체합니다. 핸들러 매개변수면 해당 이벤트를 구독합니다. 매개변수 순서가 대입 순서입니다 |
| `FluentSetsConstant(memberName, value, order)` | 메서드 | 무인자 단축 메서드가 속성에 상수를 대입합니다. `order`는 한 메서드의 상수들 사이에서 0부터 연속입니다 |
| `FluentReplacesMember(memberName)` | 메서드 | `params` 확장이 해당 인스턴스 메서드 호출을 대체합니다 |
| `FluentExcluded(reason)` | 속성 또는 이벤트 | 의도적으로 fluent 확장을 두지 않는 멤버입니다. 감사가 누락으로 세지 않습니다 |

```csharp
// FluentSetsMember: 이름이 다른 속성, 또는 복합 setter의 매개변수마다 하나씩
public static Border BorderThickness(this Border border, [FluentSetsMember(nameof(Border.NonUniformBorderThickness))] Thickness thickness)

// FluentSetsConstant: 고정값을 대입하는 단축 메서드
[FluentSetsConstant(nameof(FrameworkElement.HorizontalAlignment), MewUI.HorizontalAlignment.Center, 0)]
[FluentSetsConstant(nameof(FrameworkElement.VerticalAlignment), MewUI.VerticalAlignment.Center, 1)]
public static T Center<T>(this T element) where T : FrameworkElement

// FluentReplacesMember: 인스턴스 메서드 호출을 대신하는 params 확장
[FluentReplacesMember(nameof(Panel.Add))]
[FluentReplacesMember(nameof(Panel.AddRange))]
public static T Children<T>(this T panel, params Element[] children) where T : Panel

// FluentExcluded: 의도적으로 확장을 두지 않는 멤버
[FluentExcluded("Low-level platform interop event")]
public event Action<NativeMessageEventArgs>? NativeMessage;
```

마크업 감사(`tools/MewUI.MarkupAudit`, `tests/MewUI.Test/Markup/FluentExtensionAuditTests.cs`가 실행)는 구체 컨트롤마다 public settable 속성과 이벤트가 규약, 힌트, 제외 중 하나로 덮이는지, 그리고 각 힌트가 호환되는 타입의 실제 멤버를 가리키는지 검사합니다.

### 생성되는 확장

코어 컨트롤의 동명 setter와 `On*` 이벤트 구독은 손으로 쓰지 않습니다. `Aprillz.MewUI.Core` 패키지에 들어 있는 `Aprillz.MewUI.Generators` 소스 생성기가 빌드 시점에 컨트롤마다 `<Owner>MarkupExtensions` 클래스(`ButtonMarkupExtensions`, `UIElementMarkupExtensions`)를 해당 컨트롤의 네임스페이스에 만듭니다. public settable 속성마다 `Prop(value)`(`bool` 속성은 인자 기본값이 `true`), public 이벤트마다 `OnEvent(handler)`, 델리게이트 타입 속성은 `FluentSetsMember` 힌트가 붙은 `WithProp(value)`가 생성됩니다. `[FluentExcluded]`나 `[Obsolete]`가 붙은 멤버는 제외됩니다. 손으로 쓴 파일에는 규약으로 표현할 수 없는 것만 남습니다: 복합 setter, 상수 단축 메서드, 별칭, 컬렉션 빌더, `Bind*` 메서드, 헬퍼, 그리고 값 대입 이상의 일을 하는 setter입니다.

자기 컨트롤을 선언하는 프로젝트는 `MewUIGenerateFluentExtensions` 빌드 속성으로 생성기를 켭니다. Roslyn 4.12 이상(.NET SDK 9 이상)이 필요하며, 힌트 특성이 프로젝트에서 보이지 않으면 생성기가 함께 만들어 넣습니다:

```xml
<PropertyGroup>
  <MewUIGenerateFluentExtensions>true</MewUIGenerateFluentExtensions>
</PropertyGroup>
```

프로젝트에 손으로 쓴 확장이 우선합니다. 같은 이름과 같은 값 타입의 확장이 이미 수신자에 적용되거나, `FluentSetsMember` 힌트가 이미 그 멤버를 가리키거나, 람다 형상이 같은 `On*` 델리게이트 오버로드가 있어 람다가 모호해지거나, 같은 이름의 인스턴스 메서드가 있으면 생성기는 그 멤버를 건너뜁니다. 건너뛴 멤버는 정보 수준 진단 `MEWG101`~`MEWG104`로 보고되고 생성된 파일 머리에도 나열됩니다. `MewUIFluentExtensionsNamespace`를 지정하면 생성 클래스를 소유 타입의 네임스페이스 대신 하나의 네임스페이스에 모읍니다.

### IDE 변환 도구

`Aprillz.MewUI.Analyzers` 패키지는 개체 이니셜라이저와 구성 문장을 fluent 체인으로, 또 그 반대로 변환합니다. 규칙(`MEW1101`~`MEW1106`)과 힌트 계약은 [src/MewUI.Analyzers/README.ko.md](../src/MewUI.Analyzers/README.ko.md)에 있습니다.

## Apply 패턴

`Apply(action)`은 수신자에 동작을 실행한 뒤 수신자를 반환합니다. 전용 확장이 없는 초기화에 사용합니다.

<!-- snippet:apply -->
```csharp
var textBox = new TextBox()
    .OnTextChanged(text => Console.WriteLine(text))
    .Apply(box => box.MaxLength = 100);
```
<!-- /snippet -->

## 레퍼런스

아래 메서드 표는 컴파일된 어셈블리에서 `docs/markup`으로 생성되며, `MarkupReferenceDocsTests`가 생성 결과와 커밋된 파일을 대조합니다. 확장을 바꾼 뒤에는 `dotnet run --project tools/MewUI.MarkupAudit/MewUI.MarkupAudit -- src/MewUI/bin/Debug/net10.0/Aprillz.MewUI.dll <아래 표의 확장 패키지 전부의 Debug 빌드 출력> --docs docs/markup`을 실행하고 결과를 커밋합니다. 각 행은 수신자를 뺀 메서드 서명, 설정하는 멤버(`event X`는 구독, `bind X`는 바인딩, `X = value`는 단축 메서드), XML 요약입니다. 레퍼런스는 영문으로만 제공됩니다.

| 파일 | 범위 |
|---|---|
| [Element.md](markup/Element.md) | `Element`, `UIElement`, `FrameworkElement`, `Control`: 크기, 여백, 정렬, 표시 여부, 포커스·마우스·키보드·드래그 이벤트, Grid/Canvas/DockPanel attached property, 색상과 폰트 |
| [Content.md](markup/Content.md) | 콘텐츠 컨트롤, `Border`, `Expander`, `GroupBox`, `TabControl`, `ScrollViewer`, `Popup`, 데코레이터 |
| [Window.md](markup/Window.md) | `Window` 크기 모드, 위치, 수명 주기 이벤트, 서비스 |
| [Button.md](markup/Button.md) | `Button`, `DropDownButton`, `SplitButton`, `ToggleButton`, `CheckBox`, `RadioButton`, `ToggleSwitch`, `ButtonGroup`, `SegmentedControl` |
| [TextBox.md](markup/TextBox.md) | `TextBox`, `MultiLineTextBox`, `PasswordBox` |
| [Input.md](markup/Input.md) | `Slider`, `ProgressBar`, `ProgressRing`, `NumericUpDown`, `Calendar`, `DatePicker`, `ColorPicker` |
| [ListBox.md](markup/ListBox.md) | `ItemsControl`, `ListBox`, `ComboBox`, `NavigationList`, `NavigationView` |
| [GridView.md](markup/GridView.md) | `GridView`와 `GridViewColumn<TItem>` |
| [TreeView.md](markup/TreeView.md) | `TreeView` |
| [Menu.md](markup/Menu.md) | `Menu`, `MenuBar`, `MenuItem`, `ContextMenu` |
| [ToolBar.md](markup/ToolBar.md) | `ToolBar` 밴드, 그룹, 항목 |
| [Panel.md](markup/Panel.md) | `StackPanel`, `Grid`, `DockPanel`, `WrapPanel`, `UniformGrid`, `SplitPanel` |
| [Shape.md](markup/Shape.md) | `Shape`, `Line`, `Rectangle`, `PathShape`, `GlyphElement` |
| [Image.md](markup/Image.md) | `Image` |
| [Text.md](markup/Text.md) | `TextBlock`, `Run`, `MarkupTextBlock`, `Label` |
| [Binding.md](markup/Binding.md) | 범용 `Bind` 오버로드와 `BindingPath` 빌더 |
| [Misc.md](markup/Misc.md) | `DispatcherTimer`, `StyleSheet`, `Command`, `ApplicationBuilder`, 디자인 타임 크기, `Ref`, `Apply` |
| [MewDock.md](markup/MewDock.md) | `Aprillz.MewUI.MewDock` 패키지의 확장 |
| [Markdown.md](markup/Markdown.md) | `Aprillz.MewUI.Markdown` 패키지의 확장 |
| [Terminal.md](markup/Terminal.md) | `Aprillz.MewUI.Terminal` 패키지의 확장 |
| [MewvalonEdit.md](markup/MewvalonEdit.md) | `Aprillz.MewUI.MewvalonEdit` 패키지의 확장 |
| [Skia.md](markup/Skia.md) | `Aprillz.MewUI.Skia` 패키지의 확장 |
| [WebView2.Win32.md](markup/WebView2.Win32.md) | `Aprillz.MewUI.WebView2.Win32` 패키지의 확장 |
| [MewCharts.md](markup/MewCharts.md) | `Aprillz.MewUI.MewCharts` 패키지의 확장 |

### 참고

- `Window`에는 `Width`, `Height`, `Size`, `MinWidth`, `MinHeight`, `MaxWidth`, `MaxHeight`를 사용할 수 없습니다. 창 크기 모드를 명확히 지정하도록 `Resizable`, `Fixed`, `FitContent*` 메서드를 사용합니다.
- 드롭다운 버튼: `DropDownButton`은 항상 메뉴를 열며 primary Command가 없습니다. `SplitButton`은 primary face에서 상속된 Button Command를 실행합니다. `CanExecute`가 false이면 해당 face만 비활성화되고 메뉴는 계속 사용할 수 있습니다.
- `SegmentedControl`은 Command 소비자가 아니라 선택 컨트롤입니다. 독립 동작 묶음은 `ButtonGroup`의 각 `SegmentButton`에 Command를 지정하고, 단일 선택 값은 `SegmentedControl`의 선택 API에 바인딩합니다.
- `ProgressRing`은 고리 모양의 `ProgressBar`입니다. 호가 위쪽에서 시계 방향으로 채워지고, `IsIndeterminate`를 켜면 값 대신 회전하는 호를 보여 줍니다.
- `Grid.Rows(string)`과 `Grid.Columns(string)`은 쉼표로 구분한 `GridLength` 목록을 받습니다. `Auto`는 내용에 맞춤, `*`는 1 비율, `2*`는 2 비율, `100`은 100 픽셀입니다.
- 메뉴: `Add`, `Item`, `SubMenu`, `Separator`는 항목을 덧붙여 메뉴를 구성하고, `Items(...)`는 `ContextMenu`, `MenuBar`의 항목 전체를 교체합니다. `Item(Command)`는 Command의 정규화된 기본 텍스트와 AccessKey를 사용하고, `Item(string, Command)`와 `Text(string)`은 `_` 표식으로 MenuItem 텍스트와 AccessKey를 함께 override합니다. `MenuItem.Text`/`Icon`에 값 source가 없으면 Command의 기본 표시를 사용하며, 명시적인 빈 텍스트와 null 아이콘은 각각 Command 기본값을 숨깁니다. `IsEnabled` binding은 `CanExecute`에 의해 교체되지 않고 최종 활성 상태에서 AND로 결합됩니다.
