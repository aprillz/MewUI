# 프로퍼티 시스템

## 개요

MewUI의 프로퍼티 시스템은 WPF의 `DependencyProperty`와 유사한 역할을 하지만, 다음 목표에 맞게 재설계되었습니다:

| WPF | MewUI |
|-----|-------|
| `DependencyProperty` | `MewProperty<T>` |
| `DependencyObject` | `MewObject` |
| `DependencyPropertyKey` | `MewPropertyKey<T>` |
| `PropertyMetadata` | `MewPropertyOptions` (플래그 열거형) + 콜백 파라미터 |
| Reflection 기반 바인딩 | 델리게이트 기반, Reflection 없음 |

설계 원칙:
- **Native AOT 호환**: Reflection이나 런타임 코드 생성 없이 동작
- **최소 할당**: `PropertyValueStore`는 실제로 프로퍼티를 사용할 때만 생성 (lazy)
- **값 비교 기반 최적화**: 동일한 값 재설정 시 변경 통지를 생략

---

## 프로퍼티 선언

### 기본 패턴

프로퍼티는 `static readonly` 필드로 선언하고, CLR 프로퍼티로 래핑합니다:

```csharp
public static readonly MewProperty<bool> IsVisibleProperty =
    MewProperty<bool>.Register<UIElement>(nameof(IsVisible), true,
        MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsLayout);

public bool IsVisible
{
    get => GetValue(IsVisibleProperty);
    set => SetValue(IsVisibleProperty, value);
}
```

`Register<TOwner>` 시그니처:

```csharp
public static MewProperty<T> Register<TOwner>(
    string name,                          // 프로퍼티 이름 (진단용)
    T defaultValue,                       // 기본값
    MewPropertyOptions options = MewPropertyOptions.None,
    Action<TOwner, T, T>? changed = null, // 변경 콜백 (owner, oldValue, newValue)
    Func<TOwner, T, T>? coerce = null,    // 강제(coerce) 콜백 (owner, proposed) => stored
    Action<TOwner, T>? validate = null    // 검증(validate) 콜백 (owner, proposed), throw로 거부
)
```

### 변경 콜백이 있는 프로퍼티

부수 효과가 필요한 프로퍼티는 `changed` 콜백을 등록합니다.
콜백은 타입 안전하게 `TOwner`로 캐스팅된 소유자를 받습니다:

```csharp
public static readonly MewProperty<Element?> ContentProperty =
    MewProperty<Element?>.Register<Button>(nameof(Content), null,
        MewPropertyOptions.AffectsLayout,
        static (self, oldValue, newValue) =>
        {
            if (oldValue != null) oldValue.Parent = null;
            if (newValue != null) newValue.Parent = self;
        });
```

### 강제(coerce) 콜백이 있는 프로퍼티

`coerce` 콜백은 제안된 값이 저장되기 전에 호출되어 값을 정규화합니다.
모든 값 소스(로컬, 스타일, 트리거)에 적용됩니다:

```csharp
public static readonly MewProperty<double> ValueProperty =
    MewProperty<double>.Register<Slider>(nameof(Value), 0.0,
        MewPropertyOptions.AffectsRender,
        coerce: static (self, proposed) =>
            Math.Clamp(proposed, self.Minimum, self.Maximum));
```

강제 조건이 되는 외부 상태가 바뀌었을 때는 `CoerceValue`로 재평가를 요청합니다:

```csharp
// 예: 리사이즈 가능 여부가 바뀌면 CanMaximize를 다시 강제
CoerceValue(CanMaximizeProperty);
```

### 검증(validate) 콜백이 있는 프로퍼티

`validate` 콜백은 저장 직전 실행되는 pre-commit veto입니다. coerce 이후, 우선순위 가드를 통과하고 동일 값 no-op이 아닌 쓰기에 대해서만 실행됩니다. 콜백이 throw하면 set이 거부되어 아무것도 저장되지 않고 변경 통지도 발생하지 않습니다. `coerce`와 달리 `null`에도 실행됩니다.

```csharp
public static readonly MewProperty<Element?> ChildProperty =
    MewProperty<Element?>.Register<Border>(nameof(Child), null,
        MewPropertyOptions.AffectsLayout,
        validate: static (self, value) => self.ValidateLogicalChild(value, allowTransfer: true));
```

유효하지 않은 대입을 반영 전에 거부하는 데 사용합니다. 예를 들어 이미 다른 논리 부모에 속한 요소를 거부합니다. veto가 저장 이전에 실행되므로 저장소와 `changed` 콜백의 부수 효과(트리 변경 등)가 일관되게 유지됩니다.

### 읽기전용 프로퍼티

외부에서 설정하면 안 되는 상태 프로퍼티(`IsFocused`, `IsMouseOver` 등)는
`RegisterReadOnly`로 등록합니다. 반환된 `MewPropertyKey<T>`는 비공개로 보관하고,
`Key.Property`만 public으로 노출합니다:

```csharp
private static readonly MewPropertyKey<bool> IsFocusedPropertyKey =
    MewProperty<bool>.RegisterReadOnly<UIElement>(nameof(IsFocused), false,
        MewPropertyOptions.AffectsVisualState);

public static MewProperty<bool> IsFocusedProperty => IsFocusedPropertyKey.Property;

public bool IsFocused => GetValue(IsFocusedProperty);

// 소유자 내부에서만:
internal void SetFocused(bool value) => SetValue(IsFocusedPropertyKey, value);
```

읽기전용 프로퍼티는:
- 일반 `SetValue(MewProperty<T>, T)` 호출 시 `InvalidOperationException`
- `SetBinding` 대상이 될 수 없음 (같은 예외)
- 오직 `SetValue(MewPropertyKey<T>, T)` 오버로드로만 설정 가능

### 상속되는 프로퍼티

부모에서 자식으로 값이 전파되는 프로퍼티:

```csharp
public static readonly MewProperty<Color> ForegroundProperty =
    MewProperty<Color>.Register<Control>(nameof(Foreground), Color.Black,
        MewPropertyOptions.AffectsRender | MewPropertyOptions.Inherits);
```

---

## MewPropertyOptions

프로퍼티의 동작을 제어하는 플래그 열거형입니다:

| 플래그 | 효과 |
|--------|------|
| `None` | 특별한 동작 없음 |
| `AffectsRender` | 값 변경 시 `InvalidateVisual()` 호출 |
| `AffectsLayout` | 값 변경 시 `InvalidateMeasure()` 호출 |
| `Inherits` | 로컬/스타일 값이 없으면 부모 체인에서 값을 검색 |
| `BindsTwoWayByDefault` | `SetBinding()`/`Bind()` 호출 시 기본 모드가 `TwoWay` |
| `AffectsVisualState` | 값 변경 시 `InvalidateVisualState()` 호출: 다음 레이아웃/렌더 패스 시작 시 시각상태 재계산 큐잉. `ComputeVisualState`에 입력되는 프로퍼티(IsEnabled, IsMouseOver, IsFocused, IsPressed 등)에 사용 |

플래그는 조합할 수 있습니다:

```csharp
// 렌더링에 영향 + 부모에서 상속
MewPropertyOptions.AffectsRender | MewPropertyOptions.Inherits
```

### 자동 무효화

`UIElement.OnMewPropertyChanged`가 플래그를 확인하여 자동으로 무효화를 수행하므로,
컨트롤 코드에서 수동으로 `InvalidateMeasure()`나 `InvalidateVisual()`을 호출할
필요가 거의 없습니다.

---

## 값 해석 우선순위

프로퍼티 값은 다음 우선순위로 해석됩니다 (높은 것이 이김):

```
1. Local      ← SetValue() / CLR 프로퍼티 setter (사용자 명시 값)
2. Animated   ← 애니메이션 시스템 (프레임마다 갱신되는 보간 값)
3. Trigger    ← StateTrigger의 Setter (상태 조건 매칭 시)
4. Style      ← Style의 기본 Setter
5. Inherited  ← Inherits 플래그가 있을 때 부모 체인에서 해석된 값
6. Default    ← Register() 시 지정한 기본값 (타입별 오버라이드 반영)
```

- **Local** 값이 있으면 트리거/스타일 setter는 해당 프로퍼티에 대해 무시됩니다
  (WPF와 동일).
- **Animated** 값은 Local 아래에 있어 애니메이션이 사용자 명시 값을 덮지 않습니다.
- **Trigger**는 같은 프로퍼티의 **Style** 기본 setter를 덮습니다. 트리거가 더 이상
  매칭되지 않으면 해당 소스 값이 해제되어 스타일 기본값이 복원됩니다.
- **Inherited**는 Default보다 높습니다. 상속 값은 부모 체인에서 해석된 뒤 캐시되며,
  부모가 바뀌면 캐시가 무효화되어 다음 조회 때 재해석됩니다.

내부적으로 낮은 우선순위 소스의 쓰기는 무시됩니다: 예를 들어 로컬 값이 있는
프로퍼티에 스타일 setter가 값을 쓰려 하면 저장 자체가 거부됩니다.

참고: 로컬 값을 제거하고 스타일/기본값으로 되돌리는 공개 API는 현재 없습니다.
값 저장소(`PropertyValueStore`)와 그 `ClearLocal`은 internal이며 프레임워크
내부(트리거 해제, 애니메이션 복원 등)에서만 사용됩니다.

---

## 기본값 오버라이드

파생 타입에서 기존 프로퍼티의 기본값을 변경할 수 있습니다.
반드시 **static 생성자**에서 호출해야 합니다:

```csharp
public sealed class ProgressBar : RangeBase
{
    static ProgressBar()
    {
        // RangeBase.Maximum 기본값(double.MaxValue)을 100으로 변경
        MaximumProperty.OverrideDefaultValue<ProgressBar>(100.0);

        // FrameworkElement.Height 기본값을 10으로 변경
        HeightProperty.OverrideDefaultValue<ProgressBar>(10.0);
    }
}
```

기본값 검색은 **가장 파생된 타입부터** 상위로 올라가며, 오버라이드를 찾으면
그 값을 사용합니다:

```
ProgressBar → RangeBase → Control → FrameworkElement → UIElement → ...
     ↑
  여기서 오버라이드된 값을 찾으면 사용
```

---

## 프로퍼티 상속

`MewPropertyOptions.Inherits` 플래그가 설정된 프로퍼티는 자신에게 값이 없을 때
부모 체인에서 값을 검색합니다.

### 동작 방식

```csharp
// MewObject.GetValue 내부:
if (PropertyStore.HasOwnValue(property.Id) || !property.Inherits)
    return PropertyStore.GetValue(property);

return ResolveInheritedValue(property);
```

`ResolveInheritedValue`는 `MewObject`에서는 기본값을 반환하는 가상 메서드이고,
비주얼 트리에 참여하는 `Element`가 부모 체인 순회로 오버라이드합니다. 값이 설정된
첫 번째 조상을 찾으면 그 값을 쓰고, 없으면 타입별 기본값을 씁니다.

해석된 상속 값은 저장소에 캐시됩니다(변경 통지 없이). 부모가 바뀌면 이전 부모
체인에서 온 캐시가 모두 무효화됩니다. 상속 프로퍼티의 값이 바뀌면 자손 트리로
전파되는데, 자손이 자체 값(로컬/트리거/스타일)을 가지면 그 서브트리에서 전파가
중단됩니다.

### 대표 상속 프로퍼티

`Control`의 폰트 관련 프로퍼티들은 모두 상속됩니다:
`Foreground`, `FontFamily`, `FontSize`, `FontWeight`.

이를 통해 최상위에서 폰트를 한 번 설정하면 모든 자식 컨트롤에 자동 적용됩니다:

```csharp
new StackPanel()
    .FontSize(14)        // 이 패널 아래 모든 컨트롤에 적용
    .FontFamily("Pretendard")
    .Children(
        new Label().Text("상속된 폰트"),
        new Button().Content("상속된 폰트"),
        new TextBox()    // 역시 Pretendard 14
    )
```

---

## 변경 통지 파이프라인

프로퍼티 값이 실제로 변경되면 다음 순서로 처리됩니다:

```
SetValue / SetStyle / SetTrigger
  → 더 높은 우선순위 소스가 이미 값을 가지면 거부
  → coerce 콜백 적용 (null은 건너뜀)
  → 동일 값이면 여기서 중단 (통지 생략)
  → validate 콜백 실행 (pre-commit veto; throw 시 상태 변화 없이 거부)
  → 실행 중인 애니메이션 중단
  → 값 저장 후:

1. OnMewPropertyChanged(property)    ← virtual, 횡단 처리
   ├── AffectsLayout      → InvalidateMeasure()
   ├── AffectsRender      → InvalidateVisual()
   ├── AffectsVisualState → InvalidateVisualState()
   └── Inherits           → 자손 트리 전파

2. Property Forwards                 ← 프로퍼티-to-프로퍼티 전달 (약참조 대상)
3. Binding Callbacks                 ← ObservableValue 동기화
4. changed(owner, oldValue, newValue) ← Register() 시 등록한 콜백
```

### 동일 값 설정 방지

로컬 값 설정은 기존 값과 `EqualityComparer<T>.Default`로 비교해 같으면 박싱조차
하지 않고 반환합니다. 그 외 경로도 저장 직전에 동일 값이면 통지를 생략합니다.
이는 불필요한 레이아웃/렌더 무효화와 무한 무효화 루프를 방지합니다.

---

## 바인딩 연동

`MewProperty`는 `ObservableValue<T>` 또는 다른 `MewObject`의 프로퍼티와 직접
바인딩할 수 있습니다:

```csharp
var fontSize = new ObservableValue<double>(14);

// MewProperty ↔ ObservableValue 바인딩
element.SetBinding(FontSizeProperty, fontSize);

// 타입 변환 바인딩
var count = new ObservableValue<int>(0);
element.SetBinding(FontSizeProperty, count,
    convert: c => 12.0 + c,
    convertBack: fs => (int)(fs - 12));

// 프로퍼티-to-프로퍼티 바인딩
child.SetBinding(PaddingProperty, parent, PaddingProperty);
```

규칙:
- 기본 모드는 `property.BindsTwoWayByDefault ? TwoWay : OneWay`.
- 변환 바인딩에서 `TwoWay`인데 `convertBack`이 없으면 `OneWay`로 강등됩니다.
- 같은 프로퍼티에 재바인딩하면 기존 바인딩이 먼저 Dispose됩니다.
- `ClearBinding(property)`은 바인딩만 제거하고 현재 값은 보존합니다.
- 읽기전용 프로퍼티는 바인딩 대상이 될 수 없습니다.
- 프로퍼티-to-프로퍼티 바인딩에서 소스 변경은 대상의 **스타일(target) 계층**에
  기록됩니다. 대상에 로컬 값이 있으면 로컬이 계속 이깁니다.

> 바인딩에 대한 자세한 내용은 [Binding](Binding.ko.md) 문서를 참고하세요.

---

## 애니메이션 연동

애니메이션 값은 별도 계층이 아니라 **래퍼 방식**으로 처리됩니다. 애니메이션이
시작되면 해당 엔트리의 값이 `AnimatedEntry`(기존 base 값과 그 소스를 보존)로
감싸이고, 매 프레임 보간 값이 갱신됩니다:

- 프레임마다: `SetAnimatedValue(propertyId, value)` (내부 API)
- 완료 시: `ClearAnimatedValue(propertyId)`가 보존해 둔 base 값과 소스를 복원
- 애니메이션 중 어떤 소스로든 `SetValue`가 들어오면 실행 중인 애니메이션이
  중단되고 새 값이 저장됩니다

효과: 해석 관점에서 Animated는 Local보다 낮고 Trigger/Style보다 높습니다.
스타일 전환(Transition)이 이 메커니즘 위에서 동작합니다.

---

## 구현 노트

- `PropertyValueStore`는 소유자를 약참조하며, 소유자 타입별 기본값 해석을 위해
  타입만 강참조합니다.
- 저장소는 프로퍼티 id 기반 희소 배열(8개 이하)로 시작해, 실제로 많은 프로퍼티를
  쓰는 요소만 밀집 배열로 승격됩니다. 스타일이 많이 적용되는 컨트롤에서
  `GetValue/SetValue`가 O(1)이 되도록 하기 위한 구조입니다.
- 스타일/트리거 핫패스의 박싱을 줄이기 위해 작은 int(-1..8)와 0.0/1.0 double은
  캐시된 박스를 재사용합니다.
- 프로퍼티-to-프로퍼티 전달(forward)의 대상은 약참조로 보관되어, 대상이 수집되면
  전달 목록에서 자동 정리됩니다.
