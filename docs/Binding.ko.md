# 데이터 바인딩 가이드

MewUI의 데이터 바인딩은 Native AOT와 호환되도록 리플렉션 없이 델리게이트 기반으로 설계되었습니다.

---

## 1. 핵심 개념

### 리플렉션 없는 바인딩

WPF/WinUI는 무엇에 바인딩할지를 **문자열**로 적고 런타임에 리플렉션으로 찾습니다. MewUI는 같은 것을 **코드**로 적습니다.

```xml
<!-- WPF -->
<TextBlock Text="{Binding UserName}" />
<TextBlock Text="{Binding Customer.City}" />
<TextBlock Text="{Binding Orders[0].Total}" />
```

```csharp
// MewUI
new TextBlock().Bind(TextBlock.TextProperty, vm, x => x.UserName);
new TextBlock().Bind(TextBlock.TextProperty, vm, x => x.Customer.City);
new TextBlock().Bind(TextBlock.TextProperty, vm, x => x.Orders[0].Total, total => $"{total:N0}");
```

중첩 경로도 문자열이 아니라 코드입니다. 점을 따라가는 각 단계를 컴파일러가 검사하고, 변경 알림도 단계마다 붙습니다. 중간 객체가 통째로 교체되면 그 아래가 다시 연결됩니다(4절).

문자열이 코드가 되면서 따라오는 것:

- **Native AOT 호환**: 리플렉션이 없으므로 트리밍해도 안전합니다
- **컴파일 타임 검증**: 속성명 오타나 타입 불일치가 빌드에서 걸립니다
- **IntelliSense와 리팩터링**: 자동 완성이 되고 이름을 바꾸면 바인딩도 함께 바뀝니다

### 바인딩 모드

```csharp
public enum BindingMode
{
    OneWay,   // 소스에서 컨트롤로
    TwoWay,   // 양방향
}
```

기본 모드는 대상 속성이 정합니다. 입력 속성(`TextBox.TextProperty` 등)은 TwoWay, 표시 속성(`Label.TextProperty` 등)은 OneWay입니다. 명시적으로 `mode`를 넘기면 그 값이 우선합니다.

TwoWay로 해석됐는데 소스에 쓸 수단이 없으면(setter 미지정, 읽기 전용 리프) OneWay로 내려갑니다. 경로 바인딩은 예외이며, 조용히 내려가지 않고 예외를 던집니다.

---

## 2. 소스 종류

바인딩 소스는 셋 중 하나입니다. 어느 것이든 아래 3절의 같은 API로 겁니다.

### 2.1 ObservableValue\<T>

값 하나를 담고 변경을 알리는 컨테이너입니다. 알림 코드를 직접 쓸 필요가 없습니다.

```csharp
var name = new ObservableValue<string>("기본값");

string current = name.Value;
name.Value = "새 값";

name.Changed += () => Console.WriteLine("변경됨");
```

`coerce`로 값을 제약할 수 있습니다.

```csharp
var percent = new ObservableValue<double>(50, v => Math.Clamp(v, 0, 100));
percent.Value = 150;  // 100
percent.Value = -10;  // 0
```

### 2.2 INotifyPropertyChanged 뷰모델

평범한 속성을 가진 뷰모델도 `INotifyPropertyChanged`를 구현하면 그대로 소스가 됩니다. 구독은 약참조라 오래 사는 뷰모델이 화면을 살려두지 않습니다.

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

`PropertyChanged`의 이름이 null이거나 빈 문자열이면 전체 변경으로 보고 해당 값을 다시 읽습니다.

### 2.3 다른 MewObject의 MewProperty

컨트롤의 속성을 다른 컨트롤에 직접 연결합니다.

```csharp
new ProgressBar().Bind(RangeBase.ValueProperty, slider, RangeBase.ValueProperty);
```

### 2.4 무엇을 고를까

`ObservableValue<T>`는 알림 코드를 대신 써주므로 **MewUI 전용 뷰모델**에 편합니다. `INotifyPropertyChanged`는 **이미 있는 MVVM 뷰모델이나 다른 프레임워크와 공유하는 모델**에 맞습니다. 둘을 한 뷰모델 안에 섞어도 되고, 4절의 한 경로 안에서 섞어도 됩니다.

알림이 없는 평범한 속성은 소스가 될 수 없습니다. 값을 한 번 읽고 그대로 멈춥니다.

---

## 3. 바인딩 거는 법

### 3.1 플루언트 단축 메서드

자주 쓰는 속성에는 전용 메서드가 있습니다. `ObservableValue<T>`를 받습니다.

```csharp
new TextBox().BindText(name)                       // 양방향
new Label().BindText(name)                         // 단방향
new Label().BindText(count, c => $"개수: {c}")      // 변환
new CheckBox().BindIsChecked(isChecked)
new Slider().BindValue(volume)
new Button().BindIsVisible(isVisible).BindIsEnabled(isEnabled)
```

### 3.2 Bind와 SetBinding

모든 `MewProperty<T>`에 쓸 수 있는 일반 API입니다. `Bind`는 요소를 돌려주므로 체이닝에 쓰고, `SetBinding`은 같은 오버로드를 제공하는 하위 API입니다.

```csharp
// ObservableValue
element.Bind(Control.BackgroundProperty, colorSource);

// INotifyPropertyChanged 뷰모델, 단방향
new Label().Bind(Label.TextProperty, vm, x => x.Name);

// 양방향은 setter를 함께 넘깁니다. 생략하면 OneWay입니다.
new TextBox().Bind(TextBox.TextProperty, vm,
    x => x.Name,
    (owner, value) => owner.Name = value);

// 소유자를 통해 도달하는 ObservableValue. 이 오버로드는 INotifyPropertyChanged를 요구하지 않습니다.
new Label().Bind(Label.TextProperty, settings, x => x.Caption);

// 다른 MewObject의 속성
element.Bind(TextBlock.TextProperty, otherElement, Window.TitleProperty);
```

INPC 소스의 getter는 **멤버 하나만** 읽어야 합니다. 구독할 속성 이름을 그 식에서 얻기 때문입니다. 이름과 읽는 값이 어긋날 수 없도록 이름을 직접 넘기는 매개변수는 제공하지 않습니다. 멤버를 여러 단계 지나야 하면 4절을 보세요.

### 3.3 변환

소스 타입과 대상 타입이 다르면 `convert`를 넘깁니다. TwoWay에는 `convertBack`도 필요합니다.

```csharp
new Label().Bind(Label.TextProperty, vm,
    x => x.Temperature,
    value => $"{value:0.0} C");

textBox.Bind(TextBase.TextProperty, intSource,
    convert: i => i.ToString(),
    convertBack: s => int.TryParse(s, out var v) ? v : 0);
```

계산은 전부 `convert`에서 합니다. getter에 계산을 넣으면 무엇을 구독해야 할지 판정할 수 없습니다.

### 3.4 한데 모아보기

```csharp
class LoginViewModel
{
    public ObservableValue<string> Username { get; } = new("");
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
    }
}
```

```csharp
new StackPanel()
    .Vertical()
    .Spacing(8)
    .Children(
        new TextBox()
            .Placeholder("사용자 이름")
            .BindText(vm.Username),

        new CheckBox()
            .Content("로그인 유지")
            .BindIsChecked(vm.RememberMe),

        new Label()
            .Foreground(Color.FromRgb(200, 60, 60))
            .BindText(vm.ErrorMessage),

        new Button()
            .Content("로그인")
            .OnCanClick(() => !vm.IsLoading.Value)
            .OnClick(() => vm.Login()))
```

---

## 4. 중첩 경로

소스가 한 단계 안쪽에 있으면 경로를 씁니다. 경로는 **세그먼트의 사슬**이며, 단계마다 그 단계의 소유자를 구독합니다. 중간 객체가 통째로 교체되면 하류를 다시 연결합니다.

경로는 소스 종류를 가리지 않습니다. 한 경로 안에 `ObservableValue`, `MewProperty`, INPC 속성이 섞여도 됩니다.

### 4.1 점 표기 한 줄

권장 형태입니다. 점으로 이어 쓰면 컴파일 타임에 세그먼트 사슬로 분해됩니다.

```csharp
new Label().Bind(Label.TextProperty, vm, x => x.CurrentUser.Profile.DisplayName);
```

단계마다 멤버의 타입을 보고 관찰 방식을 고릅니다. `INotifyPropertyChanged`면 `PropertyChanged`, `ObservableValue<T>`면 그 알림, `MewObject`의 `{이름}Property`면 그 속성, 어느 것도 아니면 비관찰 세그먼트입니다.

받는 문법은 여섯 가지입니다.

| 문법 | 예 |
|------|-----|
| 멤버 접근 | `x.A.B` |
| 널 조건 | `x.A?.B` |
| 하드 캐스팅 | `((User)x.Current).Name` |
| `as` 캐스팅 | `(x.Current as User).Name` |
| 널 관용 | `x.A!.B` |
| 상수 인덱서 | `x.Items[0].Name` |

계산식, 메서드 호출, 삼항 연산자는 경로가 아니므로 컴파일 에러입니다. 계산은 `convert`로 분리하세요. 인덱스는 상수여야 합니다. 생성되는 경로가 정적 필드라 호출부의 지역 변수를 담을 수 없기 때문입니다.

이 문법은 **.NET 9 이상 SDK로 빌드할 때** 동작합니다. Roslyn 4.12 미만에서는 소스 제너레이터를 적재할 수 없어 다단 접근이 컴파일 에러가 되고, 4.2의 명시적 체인을 씁니다. 잃는 것은 문법이지 기능이 아닙니다. 겨냥하는 타깃 프레임워크는 무관하며 빌드에 쓰는 SDK만 영향을 줍니다.

### 4.2 명시적 체인

경로를 정적 필드로 두고 여러 요소가 공유하거나, 제너레이터가 없는 빌드를 지원할 때 씁니다. `BindingPath`는 불변이며 붙기 전까지 루트 인스턴스를 보관하지 않습니다.

```csharp
static readonly BindingPath<AppViewModel, string> DisplayNamePath = BindingPath
    .From<AppViewModel>()
    .ThenNotifying(x => x.CurrentUser!)
    .ThenNotifying(x => x.DisplayName);

new Label().Bind(Label.TextProperty, vm, DisplayNamePath, fallbackValue: "-");
```

중간 노드가 널 허용이면 `!`를 붙여 다음 단계 소유자 타입을 비널로 맞춥니다. 이 표기는 런타임 null 검사를 끄지 않습니다.

### 4.3 세그먼트 종류

| 붙이는 방법 | 소유자 | 변경 관찰 | TwoWay 리프 |
|------------|--------|-----------|-------------|
| `Then(getter)` | 아무거나 | 안 함 | 불가능 |
| `Then(selector)` | `ObservableValue<T>`를 내놓는 소유자 | 함 | 가능 |
| `Then(property)` | `MewObject` | 함 | 읽기 전용이 아니면 가능 |
| `ThenNotifying(getter, setter?)` | `INotifyPropertyChanged` | 함 | setter를 넘기면 가능 |
| `ThenIndexed(getter)` | 통지하는 컬렉션 또는 인덱서 | 함 | 불가능 |

비관찰 `Then(getter)`은 최초 연결과 상류가 하류를 다시 만들 때만 평가합니다. getter 결과만 바뀌는 것은 알림이 없으므로 갱신되지 않습니다. 중간값이 생성 후 바뀌지 않는 경우에는 이것이 옳은 선택입니다.

### 4.4 null과 fallback

- 중간 값이 null이면 경로가 unavailable이 되고 `fallbackValue`를 적용합니다.
- 관찰 가능한 중간 값이 다시 non-null이 되면 경로를 자동으로 다시 연결합니다.
- **마지막 세그먼트의 null은 실제 소스 값**이며 fallback으로 대체하지 않습니다.
- selector가 null `ObservableValue`를 반환하면 잘못된 경로이므로 예외를 던집니다.

관찰자는 null 소유자로 하류 selector를 호출하지 않습니다. C#은 이 런타임 보장을 제네릭 시그니처로 표현할 수 없으므로, 널 허용 중간 매개변수에는 예제처럼 `!`를 씁니다.

### 4.5 TwoWay 경로

마지막 세그먼트가 쓰기 가능해야 합니다. 4.3 표의 마지막 열을 보세요. 변환 TwoWay 경로에는 `convertBack`이 필요하며, **경로 바인딩은 OneWay로 조용히 내려가지 않고 예외를 던집니다.**

경로가 unavailable인 동안 대상 변경은 보관하지 않습니다. 다시 연결되면 현재 소스 값이 fallback이나 임시 값을 덮어씁니다.

### 4.6 컬렉션

`ObservableCollection<T>`는 `INotifyPropertyChanged`도 구현하므로 컬렉션 자신의 속성이 관찰됩니다.

```csharp
new Label().Bind(Label.TextProperty, vm, x => x.Items.Count, count => $"{count}개");
```

인덱서도 관찰합니다. 소유자가 `INotifyCollectionChanged`를 구현하면 컬렉션 변경을, 그렇지 않고 `INotifyPropertyChanged`만 구현하면 `"Item[]"` 형태의 인덱서 알림을 구독합니다.

```csharp
// 0번 원소가 교체되거나 앞에 항목이 끼어들면 갱신됩니다
new Label().Bind(Label.TextProperty, vm, x => x.Items[0].Name);

// 명시적 체인
BindingPath.From<AppViewModel>()
    .ThenNotifying(x => x.Items)
    .ThenIndexed(x => x[0]);
```

점 표기에서는 **선언된 정적 타입**으로 관찰 여부가 정해집니다. `IReadOnlyList<T>`로 선언된 속성은 실제 인스턴스가 `ObservableCollection<T>`여도 관찰 세그먼트가 되지 않습니다. `ThenIndexed`를 직접 쓰면 실제 인스턴스를 보므로 그 제한이 없습니다.

인덱스가 범위를 벗어나면 경로가 unavailable이 되어 `fallbackValue`가 적용됩니다. 인덱서가 마지막 세그먼트면 4.4의 규칙대로 null이 실제 값으로 전달됩니다.

**목록 화면 자체는 `ItemsSource`를 쓰세요.** 항목 추가와 삭제는 목록 컨트롤이 직접 관찰합니다. 경로의 인덱서는 "특정 위치의 항목 하나"를 볼 때 쓰는 것입니다.

### 4.7 진단

| ID | 수준 | 내용 |
|----|------|------|
| MEW1201 | 경고 | 소유자가 `INotifyPropertyChanged`인데 비관찰 `Then`을 사용. `ThenNotifying`으로 바꾸는 코드 수정 제공 |
| MEW1202 | 에러 | `ThenNotifying`의 getter가 단일 멤버 접근이 아님 |
| MEW1203 | 에러 | 점 표기 다단 접근인데 이 빌드에서 제너레이터가 돌지 않음 |
| MEWG001 | 에러 | 경로로 분해할 수 없는 getter |

---

## 5. 여러 소스 결합

하나의 표시 값이 소스 둘 이상에 의존하면 경로로 표현할 수 없습니다. 경로는 한 단계에 구독 하나인 사슬이기 때문입니다. **뷰모델이 결합해서 알리는 것**이 답입니다.

```csharp
// 좋음: 뷰모델이 FullName을 계산해 알리고, 뷰는 하나만 봅니다
new Label().Bind(Label.TextProperty, vm, x => x.FullName);
```

뷰모델을 고칠 수 없다면 구독을 직접 엮습니다.

```csharp
new Label()
    .Apply(label =>
    {
        void Update() => label.Text = $"{firstName.Value} {lastName.Value}".Trim();
        firstName.Changed += Update;
        lastName.Changed += Update;
        Update();
    })
```

소스가 하나인 계산에는 이 패턴이 필요 없습니다. 3.3의 `convert`를 쓰세요.

---

## 6. 수명과 메모리

관찰하는 세그먼트는 모두 약구독입니다. 오래 사는 소스가 대상을 살려두지 않습니다. 반대로 대상은 활성 바인딩을 소유하므로 `ClearBinding`, 대상 dispose, `TemplateContext.Reset` 전까지 루트와 현재 경로 객체를 유지합니다.

바인딩은 컨트롤이 dispose될 때 자동으로 정리됩니다.

```csharp
var textBox = new TextBox().BindText(vm.Name);  // 창이 닫히면 함께 해제
```

`ClearBinding`은 바인딩과 **그 바인딩이 넣은 값까지** 제거하고 다음 하위 소스를 드러냅니다. 값이 남지 않는다는 점에 주의하세요.

`ObservableValue`를 직접 구독했다면 직접 해제합니다.

```csharp
counter.Subscribe(OnChanged);
counter.Unsubscribe(OnChanged);
```

경로에 넘기는 람다에 `static`은 필수가 아니라 권장입니다. 경로가 델리게이트를 보관하므로 캡처한 객체는 경로나 그것을 쓰는 바인딩만큼 삽니다.

---

## 7. 컨트롤별 메서드

### Label

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindText(ObservableValue<string>)` | 단방향 | 텍스트 |
| `BindText<T>(ObservableValue<T>, Func<T, string>)` | 단방향 | 변환 |

### TextBox / MultiLineTextBox

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindText(ObservableValue<string>)` | 양방향 | 텍스트 입력 |

### Button

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindContent(ObservableValue<string>)` | 단방향 | 버튼 텍스트 |
| `BindContent<T>(ObservableValue<T>, Func<T, string>)` | 단방향 | 변환 |

### CheckBox / RadioButton / ToggleSwitch

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindIsChecked(ObservableValue<bool>)` | 양방향 | 체크 상태 |

### ListBox / ComboBox

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindSelectedIndex(ObservableValue<int>)` | 양방향 | 선택 인덱스 |

### Slider / ProgressBar

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindValue(ObservableValue<double>)` | Slider 양방향, ProgressBar 단방향 | 값 |

### UIElement 공통

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `BindIsVisible(ObservableValue<bool>)` | 단방향 | 표시 상태 |
| `BindIsEnabled(ObservableValue<bool>)` | 단방향 | 활성화 상태 |

### 모든 MewProperty

| 메서드 | 방향 | 설명 |
|--------|------|------|
| `Bind(MewProperty<T>, ObservableValue<T>)` | 기본 | 직접 |
| `Bind(MewProperty<TProp>, ObservableValue<TSource>, convert, convertBack?)` | 기본 | 변환 |
| `Bind(MewProperty<T>, TSource, getter, setter?)` | setter 있으면 양방향 | INPC 소스 (2.2절) |
| `Bind(MewProperty<TProp>, TSource, getter, convert, setter?, convertBack?)` | 둘 다 있으면 양방향 | INPC 변환 |
| `Bind(MewProperty<T>, TSource, Func<TSource, ObservableValue<T>>)` | 기본 | 소유자를 통해 도달하는 `ObservableValue` |
| `Bind(MewProperty<T>, MewObject, MewProperty<T>)` | 기본 | 다른 요소의 속성 (2.3절) |
| `Bind(MewProperty<T>, TRoot, BindingPath<TRoot, T>, mode?, fallbackValue?)` | 기본 | 경로 (4절) |

`SetBinding`이 같은 조합을 제공하며 `Bind`는 그 위의 플루언트 래퍼입니다.

---

## 8. 모범 사례

### 알림을 내는 소스를 쓰세요

```csharp
// 좋음: ObservableValue
class ViewModel { public ObservableValue<string> Name { get; } = new(""); }

// 좋음: INotifyPropertyChanged
class ViewModel : INotifyPropertyChanged { public string Name { get; set; } }

// 나쁨: 알림이 없어 갱신되지 않음
class ViewModel { public string Name { get; set; } }
```

### 표시 로직은 UI 레이어에

```csharp
// 좋음: 바인딩에서 변환
new Label().BindText(vm.Price, p => $"${p:N0}");

// 나쁨: 뷰모델에서 포매팅
class ViewModel { public ObservableValue<string> FormattedPrice { get; } }
```

### 유효성은 coerce로

```csharp
var age = new ObservableValue<int>(0, v => Math.Clamp(v, 0, 150));
```

### 중첩은 점 표기로, 공유는 명시적 체인으로

```csharp
// 한 곳에서만 쓰는 경로
label.Bind(Label.TextProperty, vm, x => x.CurrentUser.Profile.DisplayName);

// 여러 요소가 공유하는 경로
static readonly BindingPath<AppViewModel, string> DisplayName = /* 4.2절 */;
```
