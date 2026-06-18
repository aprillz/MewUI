# 데이터 바인딩 가이드

MewUI의 데이터 바인딩 시스템은 Native AOT와 호환되도록 Reflection 없이 델리게이트 기반으로 설계되었습니다.

---

## 1. 핵심 개념

### Reflection 없는 바인딩

WPF/WinUI와 달리 MewUI는 Reflection을 사용하지 않습니다:

| WPF 방식 | MewUI 방식 |
|----------|-----------|
| `{Binding PropertyName}` | `.BindText(vm.Name)` 또는 `.Bind(property, source)` |
| `INotifyPropertyChanged` | `ObservableValue<T>` |
| PropertyPath 문자열 | 직접 속성 참조 |

장점:
- **Native AOT 호환**: 트리밍/AOT 안전
- **컴파일 타임 검증**: 속성명 오타 방지
- **IntelliSense 지원**: 자동 완성 가능
- **리팩토링 안전**: 이름 변경 자동 반영

### 바인딩 모드

```csharp
public enum BindingMode
{
    OneWay,   // Source → Control 단방향
    TwoWay,  // Source ↔ Control 양방향
}
```

기본 모드는 속성에 따라 결정됩니다: 입력 속성(예: `TextBox.TextProperty`)은 `TwoWay`, 표시 속성(예: `Label.TextProperty`)은 `OneWay`가 기본입니다.

---

## 2. ObservableValue\<T>

값 변경 시 UI를 자동으로 업데이트하는 반응형 값 컨테이너입니다.

### 기본 사용법

```csharp
var name = new ObservableValue<string>("기본값");
var count = new ObservableValue<int>(0);
var isEnabled = new ObservableValue<bool>(true);

// 읽기/쓰기
string current = name.Value;
name.Value = "새 값";

// 변경 알림
name.Changed += () => Console.WriteLine("이름 변경됨!");
```

### Coerce (값 제약)

```csharp
var percent = new ObservableValue<double>(50, v => Math.Clamp(v, 0, 100));
percent.Value = 150;  // → 100
percent.Value = -10;  // → 0

var text = new ObservableValue<string>("", v => v?.Trim() ?? "");
```

---

## 3. 바인딩 API

MewUI는 세 가지 수준의 바인딩을 제공합니다:

### 3.1 플루언트 확장 메서드 (권장)

컨트롤별 공통 속성에 대한 고수준 편의 메서드입니다.

```csharp
var name = new ObservableValue<string>("");
var count = new ObservableValue<int>(0);
var isChecked = new ObservableValue<bool>(false);

// 텍스트 바인딩 (TextBox은 양방향, Label은 단방향)
new TextBox().BindText(name)
new Label().BindText(name)

// 변환 바인딩
new Label().BindText(count, c => $"개수: {c}")

// CheckBox / ToggleSwitch
new CheckBox().BindIsChecked(isChecked)

// Slider / ProgressBar
new Slider().BindValue(volume)

// Visibility / Enabled
new Button().BindIsVisible(isVisible).BindIsEnabled(isEnabled)
```

### 3.2 제네릭 Bind\<T> (MewProperty 바인딩)

모든 `MewProperty<T>`를 `ObservableValue<T>`에 바인딩합니다. 모든 `MewObject`에서 사용 가능합니다.

```csharp
// 직접 타입 바인딩
element.Bind(Control.BackgroundProperty, colorSource)

// 변환 포함
element.Bind(Control.BackgroundProperty, temperatureSource,
    convert: temp => temp > 30 ? Color.Red : Color.Blue)

// 양방향 변환
textBox.Bind(TextBase.TextProperty, intSource,
    convert: i => i.ToString(),
    convertBack: s => int.TryParse(s, out var v) ? v : 0)
```

### 3.3 SetBinding (저수준)

플루언트 메서드가 내부적으로 호출하는 API입니다. 커스텀 컨트롤이나 고급 시나리오에 사용합니다.

```csharp
// ObservableValue 바인딩
element.SetBinding(property, source, mode: BindingMode.TwoWay);

// 변환 포함
element.SetBinding(property, source, convert, convertBack, mode);

// MewObject간 속성 바인딩
// 다른 MewObject의 속성을 이 객체의 속성에 바인딩합니다.
// style(target) 계층에서 업데이트 — Local 값이 여전히 우선합니다.
element.SetBinding(TextBlock.TextProperty, otherElement, Window.TitleProperty);
```

---

## 4. 컨트롤별 바인딩 메서드

### Label

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindText(ObservableValue<string>)` | 단방향 | 텍스트 바인딩 |
| `BindText<T>(ObservableValue<T>, Func<T, string>)` | 단방향 | 변환 바인딩 |

### TextBox / MultiLineTextBox

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindText(ObservableValue<string>)` | 양방향 | 텍스트 입력 바인딩 |

### Button

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindContent(ObservableValue<string>)` | 단방향 | 버튼 텍스트 바인딩 |
| `BindContent<T>(ObservableValue<T>, Func<T, string>)` | 단방향 | 변환 바인딩 |

### CheckBox / RadioButton / ToggleSwitch

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindIsChecked(ObservableValue<bool>)` | 양방향 | 체크 상태 바인딩 |

### ListBox / ComboBox

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindSelectedIndex(ObservableValue<int>)` | 양방향 | 선택 인덱스 바인딩 |

### Slider

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindValue(ObservableValue<double>)` | 양방향 | 값 바인딩 |

### ProgressBar

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindValue(ObservableValue<double>)` | 단방향 | 진행률 바인딩 |

### UIElement (공통)

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindIsVisible(ObservableValue<bool>)` | 단방향 | 표시 상태 바인딩 |
| `BindIsEnabled(ObservableValue<bool>)` | 단방향 | 활성화 상태 바인딩 |

### 제네릭 (모든 MewProperty)

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `Bind<TElement, T>(MewProperty<T>, ObservableValue<T>)` | 기본 | 직접 속성 바인딩 |
| `Bind<TElement, TProp, TSource>(MewProperty<TProp>, ObservableValue<TSource>, convert, convertBack?)` | 기본 | 변환 속성 바인딩 |

---

## 5. ViewModel 패턴

### 기본 ViewModel

```csharp
class LoginViewModel
{
    public ObservableValue<string> Username { get; } = new("");
    public ObservableValue<string> Password { get; } = new("");
    public ObservableValue<bool> RememberMe { get; } = new(false);
    public ObservableValue<string> ErrorMessage { get; } = new("");
    public ObservableValue<bool> IsLoading { get; } = new(false);

    public void Login()
    {
        if (string.IsNullOrEmpty(Username.Value))
        {
            ErrorMessage.Value = "사용자 이름을 입력하세요";
            return;
        }
        IsLoading.Value = true;
        // ... 로그인 로직
    }
}
```

### UI 바인딩

```csharp
var vm = new LoginViewModel();

new StackPanel()
    .Vertical()
    .Spacing(8)
    .Children(
        new TextBox()
            .Placeholder("사용자 이름")
            .BindText(vm.Username),

        new TextBox()
            .Placeholder("비밀번호")
            .BindText(vm.Password),

        new CheckBox()
            .Content("로그인 유지")
            .BindIsChecked(vm.RememberMe),

        new Label()
            .Foreground(Color.FromRgb(200, 60, 60))
            .BindText(vm.ErrorMessage),

        new Button()
            .Content("로그인")
            .OnCanClick(() => !vm.IsLoading.Value)
            .OnClick(() => vm.Login())
    )
```

---

## 6. 계산된 값

여러 ObservableValue를 결합하여 파생 값을 생성할 수 있습니다:

```csharp
var firstName = new ObservableValue<string>("");
var lastName = new ObservableValue<string>("");

new Label()
    .Apply(label =>
    {
        void Update() => label.Text = $"{firstName.Value} {lastName.Value}".Trim();
        firstName.Changed += Update;
        lastName.Changed += Update;
        Update();
    })
```

### 재사용 가능한 패턴

```csharp
public static Label BindFullName(this Label label,
    ObservableValue<string> firstName,
    ObservableValue<string> lastName)
{
    void Update() => label.Text = $"{firstName.Value} {lastName.Value}".Trim();
    firstName.Changed += Update;
    lastName.Changed += Update;
    Update();
    return label;
}

new Label().BindFullName(vm.FirstName, vm.LastName)
```

---

## 7. 메모리 관리

### 자동 정리

바인딩은 컨트롤이 dispose될 때 (예: Window 닫힘) 자동으로 정리됩니다:

```csharp
var textBox = new TextBox().BindText(vm.Name);
// dispose 시 바인딩 자동 해제
```

### 수동 정리

```csharp
var counter = new ObservableValue<int>(0);
void OnChanged() => Console.WriteLine(counter.Value);

counter.Subscribe(OnChanged);
counter.Unsubscribe(OnChanged);  // 수동 해제
```

---

## 8. 모범 사례

### ViewModel에서 ObservableValue 사용

```csharp
// 좋음 — 바인딩 가능
class ViewModel
{
    public ObservableValue<string> Name { get; } = new("");
}

// 나쁨 — 바인딩 불가
class ViewModel
{
    public string Name { get; set; }
}
```

### Coerce로 유효성 검증

```csharp
var age = new ObservableValue<int>(0, v => Math.Clamp(v, 0, 150));
```

### 표시 로직은 UI 레이어에

```csharp
// 좋음 — 바인딩 시 변환
new Label().BindText(vm.Price, p => $"${p:N0}")

// 나쁨 — ViewModel에서 포매팅
class ViewModel { public ObservableValue<string> FormattedPrice { get; } }
```

### 비표준 속성에는 Bind\<T> 사용

```csharp
// 일반 속성은 플루언트 메서드
new TextBox().BindText(vm.Name)

// 모든 MewProperty에 제네릭 Bind
new Border().Bind(Control.BackgroundProperty, vm.StatusColor)
```
