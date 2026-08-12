# 데이터 바인딩 가이드

MewUI의 데이터 바인딩 시스템은 Native AOT와 호환되도록 Reflection 없이 델리게이트 기반으로 설계되었습니다.

---

## 1. 핵심 개념

### Reflection 없는 바인딩

WPF/WinUI와 달리 MewUI는 Reflection을 사용하지 않습니다:

| WPF 방식 | MewUI 방식 |
|----------|-----------|
| `{Binding PropertyName}` | `.BindText(vm.Name)` 또는 `.Bind(property, source)` |
| `INotifyPropertyChanged` | `ObservableValue<T>` 또는 `INotifyPropertyChanged` (3.5절) |
| PropertyPath 문자열 | 직접 속성 참조 또는 람다 (3.5절) |

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

### 3.4 BindingPath (중첩 source)

`BindingPath<TRoot, TValue>`는 property 이름 문자열, reflection, 생성 코드 없이 재사용 가능한
중첩 source를 표현합니다. 각 `Then`은 새로운 immutable descriptor를 반환하며 target
binding에 attach하기 전까지 path는 root instance를 보관하지 않습니다.

```csharp
sealed class OrderViewModel
{
    public ObservableValue<CustomerViewModel?> Customer { get; } = new();
}

sealed class CustomerViewModel
{
    public ObservableValue<string> City { get; } = new();
}

static readonly BindingPath<OrderViewModel, string> CityPath = BindingPath
    .From<OrderViewModel>()
    .Then(static order => order.Customer)
    .Then(static customer => customer!.City);

var city = new TextBlock().Bind(
    TextBlock.TextProperty,
    order,
    CityPath,
    mode: BindingMode.OneWay,
    fallbackValue: "-");
```

`Then`은 argument 타입에 따라 동작을 선택합니다.

| Segment | 변경 관찰 | writable TwoWay leaf |
|---------|-----------|----------------------|
| `Func<TCurrent, TNext>` getter | 안 함 | 불가능 |
| `Func<TCurrent, ObservableValue<TNext>>` | 함 | 가능 |
| `MewObject`의 `MewProperty<TNext>` | 함 | read-only가 아니면 가능 |
| `ThenNotifying`의 getter (소유자가 `INotifyPropertyChanged`) | 함 | setter를 넘기면 가능 |

일반 getter는 최초 attach와 관찰 가능한 upstream segment가 downstream 경로를 다시 구성할
때 평가합니다. getter 결과만 바뀌어도 알림이 없으므로 binding은 자동으로 갱신되지 않습니다.

```csharp
var statusPath = BindingPath
    .From<MyControl>()
    .Then(MyControl.StatusProperty);
```

#### Null과 fallback

- 중간 값이 null이면 경로가 unavailable이 되고 `fallbackValue`를 적용합니다.
- 관찰 가능한 중간 값이 non-null로 돌아오면 경로를 자동으로 다시 연결합니다.
- 마지막 leaf의 null은 실제 source 값이며 fallback으로 대체하지 않습니다.
- selector가 null `ObservableValue`를 반환하면 잘못된 경로이므로 예외를 던집니다.

observer는 null owner로 downstream selector를 호출하지 않습니다. C#은 마지막 leaf의
nullability를 보존하면서 이 runtime 보장을 모든 generic `Then` 호출에 표현할 수 없으므로,
예제의 `customer!.City`처럼 nullable 중간 parameter에 null-forgiving operator를 사용합니다.
이 표기는 BindingPath의 runtime null 검사를 비활성화하지 않습니다.

#### TwoWay path

TwoWay binding의 마지막 segment는 writable `ObservableValue<T>` 또는 read-only가 아닌
`MewProperty<T>`여야 합니다. 일반 getter나 read-only property leaf는 TwoWay binding을
거부합니다. 변환 TwoWay path에는 `convertBack`이 필요하며, 기존 변환 binding overload와
달리 path binding은 OneWay로 조용히 강등하지 않습니다.

```csharp
editor.Bind(
    TextBase.TextProperty,
    order,
    CityPath,
    convert: static value => value,
    convertBack: static value => value,
    mode: BindingMode.TwoWay,
    fallbackValue: "");
```

경로가 unavailable인 동안 target 변경은 보관하지 않습니다. 경로가 다시 연결되면 현재
source 값이 fallback 또는 임시 target 값을 덮어씁니다.

#### 수명과 capture

관찰 가능한 segment는 weak subscription을 사용하므로 long-lived source가 target을 살려
두지 않습니다. target은 활성 binding을 소유하므로 `ClearBinding`, target disposal 또는
`TemplateContext.Reset` 전까지 root와 현재 경로 객체를 유지합니다.

`static` lambda는 필수가 아니라 권장 사항입니다. path descriptor가 delegate를 보관하므로
capture된 객체는 descriptor 또는 이를 사용하는 활성 binding의 수명만큼 유지됩니다.

### 3.5 INotifyPropertyChanged 소스

`ObservableValue<T>`로 감싸지 않은 뷰모델도 `INotifyPropertyChanged`를 구현하면 그대로 바인딩할 수 있습니다. 구독은 약참조이므로 오래 사는 뷰모델이 화면을 살려두지 않습니다.

```csharp
sealed class UserViewModel : INotifyPropertyChanged
{
    private string _name = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
}
```

```csharp
// 단방향
new Label().Bind(Label.TextProperty, vm, x => x.Name);

// 양방향은 setter를 함께 넘깁니다. 생략하면 OneWay로 동작합니다.
new TextBox().Bind(TextBox.TextProperty, vm,
    x => x.Name,
    (owner, value) => owner.Name = value);

// 변환
new Label().Bind(Label.TextProperty, vm,
    x => x.Temperature,
    value => $"{value:0.0} C");

// 소유자가 ObservableValue를 노출하는 경우. 이 오버로드는 INotifyPropertyChanged를 요구하지 않습니다.
new Label().Bind(Label.TextProperty, settings, x => x.Caption);
```

getter는 **멤버 하나만** 읽어야 합니다. 구독할 속성 이름을 그 식에서 얻기 때문입니다. 이름이 어긋날 수 없도록 이름을 직접 넘기는 매개변수는 제공하지 않습니다.

`PropertyChanged`의 이름이 null이거나 빈 문자열이면 전체 변경으로 보고 해당 세그먼트를 다시 읽습니다.

#### 중첩 경로: 점 표기 한 줄

```csharp
new Label().Bind(Label.TextProperty, vm, x => x.CurrentUser.Profile.DisplayName);
```

컴파일 타임에 세그먼트 체인으로 분해됩니다. 단계마다 타입을 보고 관찰 방식을 고릅니다. `INotifyPropertyChanged`면 `PropertyChanged`, `ObservableValue<T>`면 그 알림, `MewObject`의 `{이름}Property`면 그 속성, 어느 것도 아니면 비관찰 세그먼트입니다.

받는 문법은 여섯 가지입니다.

| 문법 | 예 |
|------|-----|
| 멤버 접근 | `x.A.B` |
| 널 조건 | `x.A?.B` |
| 하드 캐스팅 | `((User)x.Current).Name` |
| `as` 캐스팅 | `(x.Current as User).Name` |
| 널 관용 | `x.A!.B` |
| 상수 인덱서 | `x.Items[0].Name` |

계산식, 메서드 호출, 삼항 연산자는 경로가 아니므로 컴파일 에러입니다. 계산은 `convert`로 분리하세요. 인덱스는 상수여야 합니다. 생성되는 경로가 정적 필드라 호출부 지역 변수를 담을 수 없기 때문입니다.

이 한 줄 문법은 **.NET 9 이상 SDK로 빌드할 때** 동작합니다. Roslyn 4.12 미만에서는 소스 제너레이터를 적재할 수 없으므로 다단 접근이 컴파일 에러가 되고, 아래 명시적 체인을 사용합니다. 잃는 것은 문법이지 기능이 아닙니다. 겨냥하는 타깃 프레임워크는 무관하며 빌드에 쓰는 SDK만 영향을 줍니다.

#### 중첩 경로: 명시적 체인

```csharp
static readonly BindingPath<AppViewModel, string> DisplayNamePath = BindingPath
    .From<AppViewModel>()
    .ThenNotifying(x => x.CurrentUser!)
    .ThenNotifying(x => x.DisplayName);

new Label().Bind(Label.TextProperty, vm, DisplayNamePath, fallbackValue: "-");
```

중간 노드가 널 허용이면 `!`를 붙여 다음 단계 소유자 타입을 비널로 맞춥니다. 이 표기는 런타임 null 검사를 끄지 않습니다.

`ThenNotifying`은 `ObservableValue`나 `MewProperty` 세그먼트와 한 경로에서 섞어 쓸 수 있습니다.

#### 진단

| ID | 수준 | 내용 |
|----|------|------|
| MEW1201 | 경고 | 소유자가 `INotifyPropertyChanged`인데 비관찰 `Then`을 사용. `ThenNotifying`으로 바꾸는 코드 수정 제공 |
| MEW1202 | 에러 | `ThenNotifying`의 getter가 단일 멤버 접근이 아님 |
| MEW1203 | 에러 | 점 표기 다단 접근인데 이 빌드에서 제너레이터가 돌지 않음 |
| MEWG001 | 에러 | 경로로 분해할 수 없는 getter |

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

### 알림을 내는 소스를 쓰세요

```csharp
// 좋음 — ObservableValue
class ViewModel
{
    public ObservableValue<string> Name { get; } = new("");
}

// 좋음 — INotifyPropertyChanged (3.5절)
class ViewModel : INotifyPropertyChanged { public string Name { get; set; } }

// 나쁨 — 알림이 없어 갱신되지 않음
class ViewModel
{
    public string Name { get; set; }
}
```

둘 중 무엇을 고를지는 뷰모델을 다른 곳과 공유하는지로 정합니다. MewUI 전용이면 `ObservableValue<T>`가 알림 코드를 줄여주고, 기존 MVVM 뷰모델을 그대로 쓰거나 다른 프레임워크와 공유한다면 `INotifyPropertyChanged`가 낫습니다.

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
