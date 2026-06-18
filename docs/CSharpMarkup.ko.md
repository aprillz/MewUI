# C# Markup Guide

MewUI의 C# Markup은 XAML 없이 순수 C# 코드로 UI를 선언적으로 구성할 수 있는 Fluent API입니다.
Native AOT 컴파일과 호환되며, Reflection을 사용하지 않습니다.
이 가이드의 메서드 시그니처는 `src/MewUI/Markup`의 공개 확장 메서드를 기준으로 합니다.

## 컨셉

### 왜 C# Markup인가?

- **Native AOT 호환**: Reflection 없이 컴파일 타임에 모든 것이 결정됨
- **타입 안전성**: 컴파일러가 오류를 잡아줌
- **IntelliSense**: IDE 자동완성 지원
- **코드 재사용**: 일반 C# 메서드로 UI 컴포넌트 추출 가능

### 기본 패턴

```csharp
new Button()
    .Content("Click Me")
    .Width(100)
    .OnClick(() => Console.WriteLine("Clicked!"))
```

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
| `OnCanEventName(func)` | 조건부 실행 (Commanding) | `.OnCanClick(() => isValid)` |

같은 `On*` 메서드를 핸들러 delegate의 매개변수 타입만 바꿔 오버로드하지 않습니다. 타입이 명시되지 않은 람다는 두 오버로드에 모두 일치할 수 있으며, 기반 클래스용 확장과 파생 클래스용 확장 사이에서도 같은 문제가 발생합니다. `OnCheckStateChanged`, `OnLayoutSizeChanged`처럼 의미가 구분되는 이름을 사용합니다.

### 데이터 바인딩
| 패턴 | 설명 | 예시 |
|------|------|------|
| `BindPropertyName(source)` | ObservableValue 바인딩 | `.BindText(vm.Name)` |
| `BindPropertyName(source, convert)` | 단방향 변환 바인딩 | `.BindText(vm.Count, c => $"{c}개")` |
| `BindPropertyName(source, convert, convertBack)` | 양방향 변환 바인딩 | `.BindValue(vm.Level, x => (double)x, x => (int)x)` |

`Bind*` 편의 메서드는 컨버터 오버로드를 제공합니다. 기본 양방향 속성에서 `convertBack`을 생략하면 변환 바인딩은 의도적으로 단방향으로 동작합니다.

### 속성 이름 별칭

Fluent 이름은 내부 저장 방식보다 공개 개념을 따릅니다. 복합 값은 같은 공개 메서드의 오버로드로 제공합니다.

```csharp
new Border()
    .BorderThickness(new Thickness(1, 2, 3, 4))
    .CornerRadius(new CornerRadius(4, 8, 12, 16))
```

### 단축 메서드
자주 사용되는 속성은 간결한 단축 메서드 제공:
- `.Bold()` → `.FontWeight(FontWeight.Bold)`
- `.Horizontal()` → `.Orientation(Orientation.Horizontal)`
- `.Center()` → `.HorizontalAlignment(Center).VerticalAlignment(Center)`

---

## 공통 확장 메서드

### FluentExtensions (모든 참조 타입)

| 메서드 | 설명 |
|--------|------|
| `Ref(out T field)` | 변수에 참조 저장 |

```csharp
new TextBox()
    .Ref(out var nameBox)  // nameBox 변수에 참조 저장
    .Text("Hello")
```

---

## Element 확장 메서드

모든 UI 요소의 기본 클래스입니다.

### DockPanel Attached Properties

| 메서드 | 설명 |
|--------|------|
| `DockTo(Dock dock)` | Dock 위치 설정 |
| `DockLeft()` | 왼쪽 도킹 |
| `DockTop()` | 상단 도킹 |
| `DockRight()` | 오른쪽 도킹 |
| `DockBottom()` | 하단 도킹 |

### Grid Attached Properties

| 메서드 | 설명 |
|--------|------|
| `Row(int row)` | Grid 행 위치 |
| `Column(int column)` | Grid 열 위치 |
| `RowSpan(int rowSpan)` | 행 스팬 |
| `ColumnSpan(int columnSpan)` | 열 스팬 |
| `GridPosition(row, column)` | 행/열 동시 설정 |
| `GridPosition(row, column, rowSpan, columnSpan)` | 전체 위치 설정 |

### Canvas Attached Properties

| 메서드 | 설명 |
|--------|------|
| `CanvasLeft(double left)` | 왼쪽 오프셋 |
| `CanvasTop(double top)` | 상단 오프셋 |
| `CanvasRight(double right)` | 오른쪽 오프셋 |
| `CanvasBottom(double bottom)` | 하단 오프셋 |
| `CanvasPosition(left, top)` | 위치 설정 |

---

## FrameworkElement 확장 메서드

레이아웃 가능한 모든 요소의 기본 클래스입니다.

### 크기

| 메서드 | 설명 |
|--------|------|
| `Width(double)` | 너비 |
| `Height(double)` | 높이 |
| `Size(width, height)` | 너비/높이 동시 설정 |
| `Size(double)` | 정사각형 크기 |
| `MinWidth(double)` | 최소 너비 |
| `MinHeight(double)` | 최소 높이 |
| `MaxWidth(double)` | 최대 너비 |
| `MaxHeight(double)` | 최대 높이 |

### 여백

| 메서드 | 설명 |
|--------|------|
| `Margin(uniform)` | 균일 여백 |
| `Margin(horizontal, vertical)` | 수평/수직 여백 |
| `Margin(left, top, right, bottom)` | 개별 여백 |
| `Padding(uniform)` | 균일 패딩 |
| `Padding(horizontal, vertical)` | 수평/수직 패딩 |
| `Padding(left, top, right, bottom)` | 개별 패딩 |

### 정렬

| 메서드 | 설명 |
|--------|------|
| `HorizontalAlignment(alignment)` | 수평 정렬 |
| `VerticalAlignment(alignment)` | 수직 정렬 |
| `Center()` | 중앙 정렬 (수평+수직) |
| `CenterHorizontal()` | 수평 중앙 |
| `CenterVertical()` | 수직 중앙 |
| `Left()` | 왼쪽 정렬 |
| `Right()` | 오른쪽 정렬 |
| `Top()` | 상단 정렬 |
| `Bottom()` | 하단 정렬 |
| `StretchHorizontal()` | 수평 늘이기 |
| `StretchVertical()` | 수직 늘이기 |

---

## UIElement 확장 메서드

입력 이벤트를 처리하는 모든 요소의 기본 클래스입니다.

### 바인딩

| 메서드 | 설명 |
|--------|------|
| `BindIsVisible(ObservableValue<bool>)` | 가시성 바인딩 |
| `BindIsVisible(source, convert)` | 변환된 가시성 바인딩 |
| `BindIsEnabled(ObservableValue<bool>)` | 활성화 바인딩 |
| `BindIsEnabled(source, convert)` | 변환된 활성화 상태 바인딩 |

### 입력 및 드래그 앤 드롭

| 메서드 | 설명 |
|--------|------|
| `IsHitTestVisible(bool)` | 히트 테스트 참여 여부 |
| `AllowDrop(bool)` | 드롭 허용 |
| `CanDrag(bool)` | 드래그 시작 허용 |
| `OnDragEnter(...)`, `OnDragOver(...)`, `OnDragLeave(...)`, `OnDrop(...)` | 드롭 대상 이벤트 |
| `OnDragStarting(...)`, `OnDragCompleted(...)` | 드래그 소스 이벤트 |

### 포커스 이벤트

| 메서드 | 설명 |
|--------|------|
| `OnGotFocus(Action)` | 포커스 획득 |
| `OnLostFocus(Action)` | 포커스 손실 |

### 마우스 이벤트

| 메서드 | 설명 |
|--------|------|
| `OnMouseEnter(Action)` | 마우스 진입 |
| `OnMouseLeave(Action)` | 마우스 이탈 |
| `OnMouseDown(Action<MouseEventArgs>)` | 마우스 버튼 누름 |
| `OnMouseUp(Action<MouseEventArgs>)` | 마우스 버튼 뗌 |
| `OnMouseMove(Action<MouseEventArgs>)` | 마우스 이동 |
| `OnMouseWheel(Action<MouseWheelEventArgs>)` | 마우스 휠 |

### 키보드 이벤트

| 메서드 | 설명 |
|--------|------|
| `OnKeyDown(Action<KeyEventArgs>)` | 키 누름 |
| `OnKeyUp(Action<KeyEventArgs>)` | 키 뗌 |
| `OnTextInput(Action<TextInputEventArgs>)` | 텍스트 입력 |

---

## Control 확장 메서드

시각적 스타일을 가진 모든 컨트롤의 기본 클래스입니다.

### 색상

| 메서드 | 설명 |
|--------|------|
| `Background(Color)` | 배경색 |
| `Foreground(Color)` | 전경색 (텍스트) |
| `BorderBrush(Color)` | 테두리 색상 |
| `BorderThickness(double)` | 테두리 두께 |
| `BorderThickness(Thickness)` | 각 변의 테두리 두께 |
| `CornerRadius(double)` | 균일 모서리 반경 |
| `CornerRadius(CornerRadius)` | 모서리별 반경 |

### 폰트

| 메서드 | 설명 |
|--------|------|
| `FontFamily(string)` | 폰트 이름 |
| `FontSize(double)` | 폰트 크기 |
| `FontWeight(FontWeight)` | 폰트 굵기 |
| `Bold()` | 굵게 (단축) |

---

## 개별 컨트롤 확장 메서드

### Window

```csharp
new Window()
    .Title("My App")
    .Resizable(800, 600)
    .Content(...)
    .OnLoaded(() => ...)
    .OnClosed(() => ...)
```

| 메서드 | 설명 |
|--------|------|
| `Title(string)` | 창 제목 |
| `Resizable(width, height)` | 크기 조절 가능 |
| `Fixed(width, height)` | 고정 크기 |
| `FitContentWidth(fixedHeight, maxWidth)` | 콘텐츠에 맞춤 (너비) |
| `FitContentHeight(fixedWidth, maxHeight)` | 콘텐츠에 맞춤 (높이) |
| `FitContentSize(maxWidth, maxHeight)` | 콘텐츠에 맞춤 |
| `StartCenterScreen()` / `StartCenterOwner()` | 초기 중앙 위치 |
| `StartManualPosition(left, top)` | 초기 수동 위치 |
| `Icon(IconSource?)` | 창 아이콘 |
| `WindowState(WindowState)` | 창 상태 |
| `CanMinimize(bool)`, `CanMaximize(bool)`, `CanClose(bool)` | 캡션 기능 |
| `IsToolWindow(bool)`, `ShowInTaskbar(bool)` | 창 표시 방식 |
| `Content(Element)` | 창 내용 |
| `OnLoaded(Action)` | 로드 완료 |
| `OnClosed(Action)` | 창 닫힘 |
| `OnActivated(Action)` | 창 활성화 |
| `OnDeactivated(Action)` | 창 비활성화 |
| `OnSizeChanged(Action<Size>)` | 크기 변경 |
| `OnDpiChanged(Action<uint, uint>)` | DPI 변경 |
| `OnThemeChanged(Action<Theme, Theme>)` | 테마 변경 |
| `OnFirstFrameRendered(Action)` | 첫 프레임 렌더링 |
| `OnPreviewKeyDown(Action<KeyEventArgs>)` | 키 누름 (미리보기) |
| `OnPreviewKeyUp(Action<KeyEventArgs>)` | 키 뗌 (미리보기) |
| `OnPreviewTextInput(Action<TextInputEventArgs>)` | 텍스트 입력 (미리보기) |

`Window`에는 `Width`, `Height`, `Size`, `MinWidth`, `MinHeight`, `MaxWidth`, `MaxHeight`를 사용할 수 없습니다. 창 크기 모드를 명확히 지정하도록 `Resizable`, `Fixed`, `FitContent*` 메서드를 사용합니다.

### Label

```csharp
new Label()
    .Text("Hello World")
    .Bold()
    .FontSize(16)
```

| 메서드 | 설명 |
|--------|------|
| `Text(string)` | 텍스트 내용 |
| `TextAlignment(TextAlignment)` | 수평 텍스트 정렬 |
| `VerticalTextAlignment(TextAlignment)` | 수직 텍스트 정렬 |
| `TextWrapping(TextWrapping)` | 텍스트 줄바꿈 |
| `BindText(ObservableValue<string>)` | 텍스트 바인딩 |
| `BindText(source, converter)` | 변환 바인딩 |

### Button

```csharp
new Button()
    .Content("Click Me")
    .OnCanClick(() => isFormValid)
    .OnClick(() => Submit())
```

| 메서드 | 설명 |
|--------|------|
| `Content(string)` | 버튼 텍스트 |
| `OnClick(Action)` | 클릭 핸들러 |
| `OnCanClick(Func<bool>)` | 클릭 가능 조건 (Commanding) |
| `BindContent(ObservableValue<string>)` | 콘텐츠 바인딩 |
| `BindContent(source, convert)` | 변환된 텍스트 또는 요소 콘텐츠 바인딩 |

### TextBox

```csharp
new TextBox()
    .Placeholder("Enter name...")
    .BindText(vm.Name)
```

| 메서드 | 설명 |
|--------|------|
| `Text(string)` | 텍스트 내용 |
| `Placeholder(string)` | 플레이스홀더 |
| `IsReadOnly(bool)` | 읽기 전용 |
| `AcceptTab(bool)` | 탭 키 허용 |
| `AcceptReturn(bool)` | 리턴 문자 허용 |
| `CaretPosition(int)` | 캐럿 위치 |
| `ImeMode(ImeMode)` | 입력기 모드 |
| `MaxLength(int)` | 최대 텍스트 길이 |
| `OnTextChanged(Action<string>)` | 텍스트 변경 핸들러 |
| `BindText(ObservableValue<string>)` | 텍스트 바인딩 (양방향) |
| `BindText(source, convert, convertBack?)` | 변환된 텍스트 바인딩 |

### MultiLineTextBox

```csharp
new MultiLineTextBox()
    .Placeholder("Enter notes...")
    .Wrap(true)
    .Height(100)
```

| 메서드 | 설명 |
|--------|------|
| `Text(string)` | 텍스트 내용 |
| `Placeholder(string)` | 플레이스홀더 |
| `IsReadOnly(bool)` | 읽기 전용 |
| `AcceptTab(bool)` | 탭 키 허용 |
| `Wrap(bool)` | 줄바꿈 |
| `OnWrapChanged(Action<bool>)` | 줄바꿈 변경 핸들러 |
| `OnTextChanged(Action<string>)` | 텍스트 변경 핸들러 |
| `BindText(ObservableValue<string>)` | 텍스트 바인딩 |
| `BindText(source, convert, convertBack?)` | 변환된 텍스트 바인딩 |

### CheckBox

```csharp
new CheckBox()
    .Content("Enable feature")
    .BindIsChecked(vm.IsEnabled)
```

| 메서드 | 설명 |
|--------|------|
| `Content(string)` | 레이블 텍스트 |
| `IsChecked(bool?)` | 체크 상태 |
| `Check()` / `Uncheck()` | 체크/체크 해제 단축 메서드 |
| `Indeterminate()` | 미결정 상태 설정 |
| `ThreeState()` | 3상태 모드 활성화 |
| `OnCheckedChanged(Action<bool>)` | 체크 변경 핸들러 |
| `BindIsChecked(ObservableValue<bool>)` | 체크 바인딩 |
| `BindIsChecked(ObservableValue<bool?>)` | nullable 체크 바인딩 |
| `BindIsChecked(source, convert, convertBack?)` | 변환된 체크 상태 바인딩 |
| `OnCheckStateChanged(Action<bool?>)` | 3상태 변경 핸들러 |

### RadioButton

```csharp
new RadioButton()
    .Content("Option A")
    .GroupName("options")
    .IsChecked(true)
```

| 메서드 | 설명 |
|--------|------|
| `Content(string)` | 레이블 텍스트 |
| `GroupName(string?)` | 그룹 이름 (같은 그룹 내 하나만 선택) |
| `IsChecked(bool)` | 선택 상태 |
| `OnCheckedChanged(Action<bool>)` | 선택 변경 핸들러 |
| `BindIsChecked(ObservableValue<bool>)` | 선택 바인딩 |
| `BindIsChecked(source, convert, convertBack?)` | 변환된 선택 바인딩 |

### ToggleSwitch

```csharp
new ToggleSwitch()
    .Content("Dark Mode")
    .BindIsChecked(vm.IsDarkMode)
```

| 메서드 | 설명 |
|--------|------|
| `Content(string)` | 레이블 텍스트 |
| `IsChecked(bool)` | 토글 상태 |
| `OnCheckedChanged(Action<bool>)` | 토글 변경 핸들러 |
| `BindIsChecked(ObservableValue<bool>)` | 토글 바인딩 |
| `BindIsChecked(source, convert, convertBack?)` | 변환된 토글 바인딩 |

### ListBox

```csharp
new ListBox()
    .Items("Apple", "Banana", "Cherry")
    .SelectedIndex(0)
    .Height(120)
```

| 메서드 | 설명 |
|--------|------|
| `Items(params string[])` | 아이템 목록 |
| `ItemHeight(double)` | 아이템 높이 |
| `ItemPadding(Thickness)` | 아이템 패딩 |
| `SelectedIndex(int)` | 선택 인덱스 |
| `OnSelectionChanged(Action<object?>)` | 선택 변경 핸들러 |
| `BindSelectedIndex(ObservableValue<int>)` | 선택 바인딩 |
| `BindSelectedIndex(source, convert, convertBack?)` | 변환된 선택 바인딩 |

### ComboBox

```csharp
new ComboBox()
    .Items("Small", "Medium", "Large")
    .Placeholder("Select size...")
    .SelectedIndex(1)
```

| 메서드 | 설명 |
|--------|------|
| `Items(params string[])` | 아이템 목록 |
| `SelectedIndex(int)` | 선택 인덱스 |
| `Placeholder(string)` | 플레이스홀더 |
| `OnSelectionChanged(Action<object?>)` | 선택 변경 핸들러 |
| `BindSelectedIndex(ObservableValue<int>)` | 선택 바인딩 |
| `BindSelectedIndex(source, convert, convertBack?)` | 변환된 선택 바인딩 |

### GridView

| 메서드 | 설명 |
|--------|------|
| `RowHeight(double)` | 행 높이 |
| `HeaderHeight(double)` | 헤더 높이 |
| `CellPadding(Thickness)` | 셀 패딩 |
| `ZebraStriping(bool)` | 교대 행 배경 |
| `ShowGridLines(bool)` | 그리드 선 표시 |
| `Columns<TItem>(params GridViewColumn<TItem>[])` | 열 정의 |
| `ItemsSource<TItem>(IReadOnlyList<TItem>)` | 아이템 소스 |
| `ItemsSource<TItem>(ItemsView<TItem>)` | ItemsView 소스 |
| `FixedHeightPresenter()` | 고정 높이 가상화 |
| `VariableHeightPresenter()` | 가변 높이 가상화 |

GridView 열에는 `Header(string)`, `Width(double)`, `MinWidth(double)`, `Resizable(bool)`을 사용할 수 있습니다.

### TreeView

| 메서드 | 설명 |
|--------|------|
| `ItemsSource(IReadOnlyList<TreeViewNode>)` | 루트 노드 |
| `ItemsSource(ITreeItemsView)` | 트리 아이템 뷰 |
| `SelectedNode(TreeViewNode?)` | 선택 노드 |
| `ItemHeight(double)` | 아이템 높이 |
| `ItemPadding(Thickness)` | 아이템 패딩 |
| `ItemTemplate(IDataTemplate)` | 아이템 템플릿 |
| `Indent(double)` | 자식 들여쓰기 |
| `ExpandTrigger(TreeViewExpandTrigger)` | 확장 트리거 |
| `Expand(TreeViewNode)` / `Collapse(TreeViewNode)` | 확장 상태 변경 |
| `Toggle(TreeViewNode)` | 확장 상태 전환 |
| `OnSelectionChanged(Action<object?>)` | 선택 변경 핸들러 |
| `OnSelectedNodeChanged(Action<TreeViewNode?>)` | 선택 노드 변경 핸들러 |

### Slider

```csharp
new Slider()
    .Minimum(0)
    .Maximum(100)
    .BindValue(vm.Volume)
```

| 메서드 | 설명 |
|--------|------|
| `Minimum(double)` | 최소값 |
| `Maximum(double)` | 최대값 |
| `Value(double)` | 현재값 |
| `SmallChange(double)` | 작은 변경 단위 |
| `OnValueChanged(Action<double>)` | 값 변경 핸들러 |
| `BindValue(ObservableValue<double>)` | 값 바인딩 |
| `BindValue(source, convert, convertBack?)` | 변환된 값 바인딩 |

### ProgressBar

```csharp
new ProgressBar()
    .Minimum(0)
    .Maximum(100)
    .BindValue(vm.Progress)
```

| 메서드 | 설명 |
|--------|------|
| `Minimum(double)` | 최소값 |
| `Maximum(double)` | 최대값 |
| `Value(double)` | 현재값 |
| `BindValue(ObservableValue<double>)` | 값 바인딩 |
| `BindValue(source, convert)` | 변환된 단방향 값 바인딩 |

### Calendar

| 메서드 | 설명 |
|--------|------|
| `SelectedDate(DateTime?)` | 선택 날짜 |
| `DisplayDate(DateTime)` | 표시 날짜 |
| `DisplayMode(CalendarMode)` | 달력 표시 모드 |
| `FirstDayOfWeek(DayOfWeek)` | 주의 첫 요일 |
| `IsTodayHighlighted(bool)` | 오늘 강조 |
| `OnSelectedDateChanged(Action<DateTime?>)` | 선택 날짜 변경 핸들러 |
| `BindSelectedDate(ObservableValue<DateTime?>)` | 선택 날짜 바인딩 |
| `BindSelectedDate(source, convert, convertBack?)` | 변환된 선택 날짜 바인딩 |

### DatePicker

| 메서드 | 설명 |
|--------|------|
| `SelectedDate(DateTime?)` | 선택 날짜 |
| `Placeholder(string)` | 플레이스홀더 |
| `DateFormat(string)` | 날짜 형식 |
| `FirstDayOfWeek(DayOfWeek)` | 주의 첫 요일 |
| `OnSelectedDateChanged(Action<DateTime?>)` | 선택 날짜 변경 핸들러 |
| `BindSelectedDate(ObservableValue<DateTime?>)` | 선택 날짜 바인딩 |
| `BindSelectedDate(source, convert, convertBack?)` | 변환된 선택 날짜 바인딩 |

### ColorPicker

| 메서드 | 설명 |
|--------|------|
| `SelectedColor(Color)` | 선택 색상 |
| `OnSelectedColorChanged(Action<Color>)` | 선택 색상 변경 핸들러 |
| `Kind(ColorPickerKind)` | 피커 표시 방식 |
| `ShowAlpha(bool)` | 알파 컨트롤 표시 |

### Image

```csharp
new Image()
    .SourceFile("logo.png")
    .Size(64, 64)
    .StretchMode(Stretch.Uniform)
```

| 메서드 | 설명 |
|--------|------|
| `Source(IImageSource?)` | 이미지 소스 |
| `SourceFile(string path)` | 파일에서 로드 |
| `SourceResource(Assembly, string)` | 리소스에서 로드 |
| `SourceResource<TAnchor>(string)` | 리소스에서 로드 (제네릭) |
| `StretchMode(Stretch)` | 늘이기 모드 |
| `ImageScaleQuality(ImageScaleQuality)` | 스케일링 품질 |
| `ViewBox(Rect?, ImageViewBoxUnits)` | 소스 뷰박스 |
| `ViewBoxPixels(Rect?)` | 픽셀 기준 소스 뷰박스 |
| `ViewBoxRelative(Rect?)` | 상대 좌표 소스 뷰박스 |
| `AlignmentX(ImageAlignmentX)` | 수평 이미지 정렬 |
| `AlignmentY(ImageAlignmentY)` | 수직 이미지 정렬 |

### TabControl

```csharp
new TabControl()
    .TabItems(
        new TabItem().Header("Home").Content(...),
        new TabItem().Header("Settings").Content(...)
    )
```

| 메서드 | 설명 |
|--------|------|
| `TabItems(params TabItem[])` | 탭 아이템 목록 |
| `Tab(header, content)` | 탭 추가 (문자열 헤더) |
| `Tab(Element header, content)` | 탭 추가 (요소 헤더) |
| `SelectedIndex(int)` | 선택 탭 인덱스 |
| `TabPlacement(TabPlacement)` | 탭 헤더 위치 |
| `OnSelectionChanged(Action<object?>)` | 탭 변경 핸들러 |

### TabItem

```csharp
new TabItem()
    .Header("Settings")
    .Content(new StackPanel().Children(...))
    .IsEnabled(true)
```

| 메서드 | 설명 |
|--------|------|
| `Header(string)` | 헤더 텍스트 |
| `Header(Element)` | 헤더 요소 |
| `Content(Element)` | 탭 내용 |
| `IsEnabled(bool)` | 활성화 상태 |

### GroupBox (HeaderedContentControl)

```csharp
new GroupBox()
    .Header("Options")
    .Content(new StackPanel().Children(...))
```

| 메서드 | 설명 |
|--------|------|
| `Header(string)` | 헤더 텍스트 (Bold 스타일) |
| `Header(Element)` | 헤더 요소 |
| `HeaderSpacing(double)` | 헤더-콘텐츠 간격 |
| `Content(Element)` | 그룹 내용 |

### ScrollViewer

```csharp
new ScrollViewer()
    .AutoVerticalScroll()
    .NoHorizontalScroll()
    .Content(...)
```

| 메서드 | 설명 |
|--------|------|
| `VerticalScroll(ScrollMode)` | 수직 스크롤 모드 |
| `HorizontalScroll(ScrollMode)` | 수평 스크롤 모드 |
| `Scroll(vertical, horizontal)` | 스크롤 모드 동시 설정 |
| `NoVerticalScroll()` | 수직 스크롤 비활성화 |
| `AutoVerticalScroll()` | 자동 수직 스크롤 |
| `ShowVerticalScroll()` | 수직 스크롤 항상 표시 |
| `NoHorizontalScroll()` | 수평 스크롤 비활성화 |
| `AutoHorizontalScroll()` | 자동 수평 스크롤 |
| `ShowHorizontalScroll()` | 수평 스크롤 항상 표시 |
| `Content(Element)` | 스크롤할 내용 |

---

## Panel 확장 메서드

### Panel (공통)

| 메서드 | 설명 |
|--------|------|
| `Children(params Element[])` | 자식 요소 추가 |
| `Padding(Thickness)` | 패널 패딩 |
| `Padding(uniform)` | 균일 패널 패딩 |
| `Padding(horizontal, vertical)` | 수평/수직 패널 패딩 |
| `Padding(left, top, right, bottom)` | 개별 패널 패딩 |

### StackPanel

```csharp
new StackPanel()
    .Vertical()
    .Spacing(8)
    .Children(
        new Label().Text("First"),
        new Label().Text("Second")
    )
```

| 메서드 | 설명 |
|--------|------|
| `Orientation(Orientation)` | 방향 |
| `Horizontal()` | 수평 방향 (단축) |
| `Vertical()` | 수직 방향 (단축) |
| `Spacing(double)` | 요소 간 간격 |

### Grid

```csharp
new Grid()
    .Rows("Auto,*,Auto")
    .Columns("100,*")
    .Spacing(8)
    .AutoIndexing()
    .Children(
        new Label().Text("Name:"),
        new TextBox()
    )
```

| 메서드 | 설명 |
|--------|------|
| `Rows(params GridLength[])` | 행 정의 |
| `Columns(params GridLength[])` | 열 정의 |
| `Rows(string)` | 행 정의 (문자열: "Auto,*,2*,100") |
| `Columns(string)` | 열 정의 (문자열) |
| `Spacing(double)` | 셀 간 간격 |
| `AutoIndexing(bool)` | 자동 인덱싱 (Row/Column 자동 증가) |
| `ShowGridLine(bool)` | 레이아웃 그리드 선 표시 |
| `ShareStarSize(bool)` | 중첩 Grid 간 Star 크기 공유 |

**GridLength 문자열 문법:**
- `Auto` - 내용에 맞춤
- `*` - 1 비율
- `2*` - 2 비율
- `100` - 100 픽셀

### SplitPanel

```csharp
new SplitPanel()
    .Horizontal()
    .FirstLength(new GridLength(1, GridUnitType.Star))
    .SecondLength(new GridLength(2, GridUnitType.Star))
    .SplitterThickness(6)
    .First(leftPane)
    .Second(rightPane)
```

| 메서드 | 설명 |
|--------|------|
| `Orientation(Orientation)` | 분할 방향 |
| `Horizontal()` | 수평 분할 |
| `Vertical()` | 수직 분할 |
| `SplitterThickness(double)` | 분할선 두께 |
| `FirstLength(GridLength)` | 첫 번째 영역 길이 |
| `SecondLength(GridLength)` | 두 번째 영역 길이 |
| `MinFirst(double)` / `MaxFirst(double)` | 첫 번째 영역 크기 제한 |
| `MinSecond(double)` / `MaxSecond(double)` | 두 번째 영역 크기 제한 |
| `First(UIElement?)` | 첫 번째 영역 콘텐츠 |
| `Second(UIElement?)` | 두 번째 영역 콘텐츠 |

### UniformGrid

```csharp
new UniformGrid()
    .Columns(3)
    .Spacing(8)
    .Children(
        new Button().Content("1"),
        new Button().Content("2"),
        new Button().Content("3")
    )
```

| 메서드 | 설명 |
|--------|------|
| `Rows(int)` | 행 개수 |
| `Columns(int)` | 열 개수 |
| `Spacing(double)` | 셀 간 간격 |

### WrapPanel

```csharp
new WrapPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(8)
    .ItemWidth(100)
    .ItemHeight(100)
    .Children(...)
```

| 메서드 | 설명 |
|--------|------|
| `Orientation(Orientation)` | 방향 |
| `Horizontal()` | 가로 방향 |
| `Vertical()` | 세로 방향 |
| `Spacing(double)` | 요소 간 간격 |
| `ItemWidth(double)` | 아이템 너비 |
| `ItemHeight(double)` | 아이템 높이 |

### DockPanel

```csharp
new DockPanel()
    .LastChildFill()
    .Spacing(8)
    .Children(
        new Label().Text("Header").DockTop(),
        new Label().Text("Footer").DockBottom(),
        new Label().Text("Content")  // 남은 공간 채움
    )
```

| 메서드 | 설명 |
|--------|------|
| `LastChildFill(bool)` | 마지막 자식이 남은 공간 채움 |
| `Spacing(double)` | 요소 간 간격 |

---

## 추가 확장 API

아래 표는 나머지 공개 Markup 확장 메서드의 색인입니다. 여러 오버로드가 있는 메서드의 전체 매개변수는 IntelliSense 또는 XML API 문서를 참고합니다.

### 공통 Element 및 Control

| 메서드 | 용도 |
|--------|------|
| `Apply(...)`, `Register(...)`, `Template(...)` | 사용자 지정 초기화 및 템플릿 |
| `Bind(...)` | 일반 속성 바인딩 |
| `IsVisible(...)`, `Enable()`, `Disable()` | 표시 및 활성화 상태 |
| `ClipToBounds(...)`, `Cursor(...)`, `Opacity(...)`, `Rotation(...)` | 시각 및 입력 속성 |
| `CacheMode(...)`, `Cached()` | 렌더링 캐시 설정 |
| `StyleName(...)`, `WithTheme(...)` | 스타일 및 테마 선택 |
| `ToolTip(...)`, `ContextMenu(...)` | 보조 UI |
| `Child(...)` | Decorator 자식 콘텐츠 |
| `AccessKeyTarget(...)` | 액세스 키 대상 |
| `SemiBold()` | Semibold 글꼴 단축 메서드 |
| `TextTrimming(...)` | 텍스트 잘라내기 |

### 입력 및 조합 이벤트

| 메서드 | 용도 |
|--------|------|
| `OnDoubleClick(...)`, `OnMouseDoubleClick(...)` | 더블 클릭 핸들러 |
| `OnTextCompositionStart(...)`, `OnTextCompositionUpdate(...)`, `OnTextCompositionEnd(...)` | 텍스트 조합 핸들러 |
| `OnPreviewTextCompositionStart(...)`, `OnPreviewTextCompositionUpdate(...)`, `OnPreviewTextCompositionEnd(...)` | 미리보기 텍스트 조합 핸들러 |

### Window 및 서비스

| 메서드 | 용도 |
|--------|------|
| `Icon(...)`, `OnBuild(...)`, `OnClosing(...)`, `OnFrameRendered(...)` | 창 설정 및 수명 주기 |
| `OnWindowStateChanged(...)`, `Minimized()`, `Maximized()`, `Topmost(...)` | 창 상태 |
| `StartCenterScreen()`, `StartCenterOwner()`, `StartManualPosition(...)` | 초기 창 위치 |
| `ShowToast(...)`, `CreateBusyIndicator(...)` | 창 서비스 |

### 아이템 및 선택

| 메서드 | 용도 |
|--------|------|
| `AddColumn(...)` | GridView 열 추가 |
| `StackPresenter()`, `WrapPresenter(...)` | 아이템 Presenter 선택 |
| `ChangeOnWheel(...)` | 마우스 휠 값/선택 변경 |
| `MaxMenuHeight(...)` | ContextMenu 높이 제한 |
| `IsExpanded(...)`, `BindIsExpanded(...)`, `OnExpandedChanged(...)` | 확장 상태 |
| `IsActive(...)`, `BindIsActive(...)` | ProgressRing 실행 상태 |

### 입력 컨트롤

| 메서드 | 용도 |
|--------|------|
| `Password(...)`, `BindPassword(...)` | PasswordBox 값 및 바인딩 |
| `Range(...)`, `Step(...)`, `Format(...)`, `IsInteger(...)` | 숫자 입력 설정 |
| `OnChecked(...)`, `OnUnchecked(...)`, `IsThreeState(...)` | 체크/토글 상태 |

### 메뉴

| 메서드 | 용도 |
|--------|------|
| `Add(...)`, `Item(...)`, `SubMenu(...)`, `Separator()` | 메뉴 구성 |
| `Menu(...)`, `Shortcut(...)` | MenuItem 하위 메뉴 및 단축키 |

### Shape 및 Glyph

| 메서드 | 용도 |
|--------|------|
| `Fill(...)`, `Stroke(...)`, `StrokeStyle(...)`, `Stretch(...)` | 도형 모양 |
| `Data(...)`, `Points(...)`, `CornerRadius(...)` | 도형 지오메트리 |
| `GlyphSize(...)`, `StrokeThickness(...)` | Glyph 모양 |

### Timer 및 Style

| 메서드 | 용도 |
|--------|------|
| `Interval(...)`, `IntervalMs(...)`, `OnTick(...)`, `Start()`, `Stop()` | DispatcherTimer 설정 |
| `With(...)` | StyleSheet에 스타일 추가 |
| `HeaderInset(...)` | 헤더 레이아웃 인셋 |

---

## Commanding (CanExecute 패턴)

Button의 `OnCanClick`을 사용하여 WPF ICommand와 유사한 패턴을 구현할 수 있습니다.

```csharp
var text = new ObservableValue<string>("");

new TextBox()
    .BindText(text)
    .OnTextChanged(_ => window.RequerySuggested()),

new Button()
    .Content("Submit")
    .OnCanClick(() => !string.IsNullOrWhiteSpace(text.Value))
    .OnClick(() => Submit(text.Value))
```

### 자동 재평가 시점

`CanClick`은 다음 시점에 자동으로 재평가됩니다:
- **Focus 변경** - 포커스가 이동할 때
- **MouseUp** - 마우스 버튼을 뗄 때
- **KeyUp** - 키를 뗄 때

### 수동 재평가

상태가 변경된 후 수동으로 재평가가 필요한 경우:

```csharp
// 이벤트 핸들러 내에서 상태 변경 후
counter.Value++;
window.RequerySuggested();  // CanClick 재평가 트리거
```

---

## Apply 패턴

복잡한 초기화나 지원되지 않는 속성 설정 시 `Apply` 패턴을 사용합니다:

```csharp
public static T Apply<T>(this T obj, Action<T> action)
{
    action(obj);
    return obj;
}

// 사용 예
new TextBox()
    .OnTextChanged(text => Console.WriteLine(text))
    .Apply(tb => tb.MaxLength = 100)
```
