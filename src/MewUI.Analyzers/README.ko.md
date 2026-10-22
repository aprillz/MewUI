# Aprillz.MewUI.Analyzers

> **상태 (2026-06-15): 초기 실험적 버전.** 진단 id, 동작, 포맷 출력은 아직 정제 중이며 바뀔 수 있습니다.

MewUI fluent markup용 Roslyn analyzer와 refactoring 모음. NuGet analyzer(`analyzers/dotnet/cs`)로 배포되어
참조 하나로 Visual Studio, VS Code(C# Dev Kit), Rider, CI에서 동작합니다. 빌드 타임 전용이라
런타임/NativeAOT 산출물에는 아무것도 들어가지 않습니다.

- 네임스페이스: `Aprillz.MewUI.Analyzers`
- 타깃: `netstandard2.0`, Roslyn은 호환성을 위해 4.8로 핀 고정.
- English: [README.md](README.md)

## 상태

| Id | 기능 | 종류 | 소스 |
|---|---|---|---|
| `MEW1101` | 이니셜라이저 -> fluent 체인 | analyzer + code fix | `InitializerToFluentAnalyzer.cs`, `InitializerToFluentCodeFix.cs` |
| `MEW1102` | fluent 체인 펼침 / 접기 | refactoring | `FluentChainFormatRefactoring.cs` |
| `MEW1103` | 문장들을 fluent 체인으로 병합 | refactoring | `MergeChainStatementsRefactoring.cs` |
| `MEW1104` | 구성 문장 -> fluent 호출 | refactoring | `AssignmentToFluentCallRefactoring.cs` |
| `MEW1105` | 문장들을 fluent 체인으로 병합 | analyzer + code fix | `ChainStatementAnalyzer.cs`, `ChainStatementCodeFix.cs` |
| `MEW1106` | 구성 문장 -> fluent 호출 | analyzer + code fix | `ChainStatementAnalyzer.cs`, `ChainStatementCodeFix.cs` |

공유 컴포넌트:

- `FluentMethodResolver.cs`: 속성/이벤트 이름을 fluent setter 확장으로 해석. **확장 메서드가
  진실의 출처**(별도 매핑 표 없음 -> drift 없음)이고, `On` 접두(`Click` -> `OnClick`)도 시도. 복합
  setter, 축약형, 별칭이 어떤 멤버를 대입하는지 선언한 힌트 특성("확장이 무엇을 설정하는지 선언하기"
  절)도 여기서 읽는다.
- `FluentInitializerPlanner.cs`: MEW1101용으로 object initializer 전체를 계획. 각 fluent 호출이 연속된
  멤버 중 무엇을 어떤 인자로 소비하고 무엇이 남는지 정한다. analyzer와 code fix가 같이 쓰므로 보고한
  이니셜라이저는 판정한 그대로 변환된다.
- `ChainStatementDescriber.cs`: 문장 하나가 수신자에 대한 어떤 체인 호출인지 판정(fluent 호출, 이벤트
  구독, 속성 대입, 정적 attached setter, 확장이 대체를 선언한 호출). MEW1103/1104/1105/1106이 모두 이
  판정을 거치므로 같은 문장을 서로 다르게 보지 않음.
- `FluentChainLayout.cs`: 공유 레이아웃 엔진(MEW1101/1102/1103/1105 공용). 체인을 구조에서 재구성하고,
  element 자식은 트리로 펼치고, 값은 inline 유지하며, 멀티라인 람다 본문을 재들여쓰기.

`tests/MewUI.Analyzers.Test`에 테스트 76개가 6개 기능을 모두 커버.

## `MEW1101` - 이니셜라이저를 fluent 체인으로

object initializer를 동등한 MewUI fluent setter 체인으로 변환(+펼침).

```csharp
new Border { CornerRadius = 8, BorderThickness = 1, Child = body }
// -> Convert to fluent chain
new Border()
    .CornerRadius(8)
    .BorderThickness(1)
    .Child(body)
```

심각도는 `Hidden`(squiggle 없음, lightbulb로만). 보이게 하려면 `.editorconfig`:
`dotnet_diagnostic.MEW1101.severity = suggestion`.

### 규칙

1. **트리거.** object initializer를 가진 생성식 중, 멤버 `Name = value` 하나 이상이 fluent 호출로
   해석될 때.
2. **fluent setter.** 생성 타입에 호출 가능한, 이름이 정확히 `Name`(이벤트는 `On` + `Name`)인 확장
   메서드로, `value`가 변환되는 단일 매개변수를 받는 것. 확장 메서드가 진실의 출처라, 추가한 setter는
   자동 인식.
3. **변환.** 매칭 멤버는 소스 순서대로 `.Name(value)`; `new T { ... }`엔 `()` 추가. 변환된 호출이 2개 이상이면
   줄바꿈해서 펼침.
4. **이벤트.** delegate 프로퍼티 `Click = handler`는 `On` 접두 규칙으로 `.OnClick(handler)`.

   ```csharp
   new MenuItem { Text = "Open", Click = OnOpen }
   // -> Convert to fluent chain
   new MenuItem()
       .Text("Open")
       .OnClick(OnOpen)
   ```

5. **힌트 호출.** 매개변수에 `[FluentSetsMember]`가 있거나 메서드에 `[FluentSetsConstant]`가 있는 확장("확장이
   무엇을 설정하는지 선언하기" 절)은 선언한 멤버들을 대체한다. 복합 setter는 여러 멤버를 인자로 받고,
   축약형은 상수 대입을 무인자 호출로 바꾸며, 별칭은 멤버 하나를 이름이 다른 setter로 보내고, 결합형은
   둘을 함께 한다(`ViewBox = r, ViewBoxUnits = ImageViewBoxUnits.Pixels` -> `.ViewBoxPixels(r)`).

   ```csharp
   new SplitButton { Command = save, CommandPresentationMode = CommandPresentationMode.TextAndIcon }
   // -> Convert to fluent chain
   new SplitButton().Command(save, CommandPresentationMode.TextAndIcon)

   new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
   // -> Convert to fluent chain
   new TextBlock().Center()
   ```

   힌트 호출은 선언한 멤버가 전부, 선언 순서대로 연속해 대입되어 있고, 각 값이 매개변수로 변환되거나
   선언한 상수와 같을 때만(enum은 숫자만이 아니라 타입까지 일치) 쓴다. `Command = save`만 있으면
   `.Command(save)`로 바꾸지 않는다. 생략한 선택 인자가 `CommandPresentationMode`를 기본값으로 덮어쓰기
   때문이다. 순서가 뒤집히거나 사이에 다른 대입이 있으면 멤버별로 따로 변환하고(`.CenterVertical()` 뒤
   `.CenterHorizontal()`), 상수가 다르면 동명 setter로 폴백한다(`.VerticalAlignment(VerticalAlignment.Top)`).
6. **우선순위.** 같은 멤버에서 시작할 수 있는 호출이 여럿이면 연속 멤버를 더 많이 소비하는 쪽이 이긴다.
   개수가 같으면 힌트 호출 > 동명 setter > `On` 접두 순이고, 힌트 호출끼리는 인자를 더 적게 넘기는 쪽이
   이긴다(`.ViewBox(r, ImageViewBoxUnits.Pixels)`보다 `.ViewBoxPixels(r)`). 그다음 생성될 호출을 일반 C#처럼 바인딩해
   선택한 확장으로 해석될 때만 쓴다. 같은 우선순위의 후보를 그 방법으로도 가를 수 없으면(이름이 다른 두
   축약형이 같은 상수를 선언하는 경우 등) 그 멤버들은 이니셜라이저에 남긴다.
7. **부분 변환.** 호출로 해석되지 않는 멤버는 잔여 이니셜라이저로 남음.

   ```csharp
   new Widget { Text = "hi", Tag = obj, Width = 5 }
   // -> Tag는 setter가 없어 잔여 이니셜라이저로
   new Widget() { Tag = obj }
       .Text("hi")
       .Width(5)
   ```

8. 호출로 해석되는 멤버가 없으면 **진단 없음**.

### 아직 미지원

- 컬렉션 멤버: `Children = { a, b }` -> `.Children(a, b)`.
- 중첩 object initializer 재귀.

## `MEW1102` - fluent 체인 펼침 / 접기

수동 refactoring(Ctrl+.)이며 format-on-save 훅이 아님(저장 시 포맷은 호스트 내장 포매터를 부르지 refactoring을
부르지 않음). 두 액션을 항상 제공 -> 이미 펼쳐진 체인도 "Expand"로 그 자리에서 재정렬.

```csharp
new Button().Content("OK").Width(80)
// -> Expand fluent chain
new Button()
    .Content("OK")
    .Width(80)
// -> Collapse fluent chain to one line  (역방향)
```

### 규칙

1. **트리거.** member-access 호출 체인(호출 1개 이상) 안에 캐럿.
2. **펼침.** 각 `.Method(...)`를 한 줄씩, 체인 시작 줄 기준 한 단계 들여쓰기.
3. **element 자식.** 인자가 **element 체인**이면 인자 리스트를 줄바꿈하고 각 element를 자기 트리로 펼침,
   형제 사이 빈 줄:

   ```csharp
   new StackPanel().Vertical().Children(new Button().Content("A").Width(80), new Button().Content("B").Width(80))
   // -> Expand fluent chain
   new StackPanel()
       .Vertical()
       .Children(
           new Button()
               .Content("A")
               .Width(80),

           new Button()
               .Content("B")
               .Width(80)
       )
   ```

   element 체인 = 루트가 `new X()`, 호출(`Factory()`), 또는 값(local/field)인 것. 루트가 **타입**인
   정적 팩토리(`Color.FromRgb(...)` 등)는 값이라 inline 유지(쪼개지 않음). 이 판정은 시맨틱이 필요해서
   MEW1102는 semantic model을 사용; MEW1101/1103은 합성 체인을 포맷하므로 이름 휴리스틱(타입은
   PascalCase)으로 폴백.

   ```csharp
   new ColorPicker().SelectedColor(Color.FromRgb(255, 0, 0)).Width(120)
   // -> Expand fluent chain   (Color.FromRgb은 inline 유지)
   new ColorPicker()
       .SelectedColor(Color.FromRgb(255, 0, 0))
       .Width(120)
   ```

4. **람다 블록.** 멀티라인 람다 본문을 새 위치에 맞춰 재들여쓰기:

   ```csharp
   new Window().Resizable(800, 600).OnMouseDown(e => { if (e.Button == MouseButton.Left) DragMove(); })
   // -> Expand fluent chain
   new Window()
       .Resizable(800, 600)
       .OnMouseDown(e =>
       {
           if (e.Button == MouseButton.Left)
               DragMove();
       })
   ```

5. **접기.** 역방향: 중첩 element 자식까지 한 줄로 평탄화.
6. **멱등.** 매번 구조에서 재구성하므로 반복 실행이 안정적.

### 아직 미지원

- `.editorconfig` 기반 최대 줄 길이/호출 수 임계값.

## `MEW1103` - 문장들을 fluent 체인으로 병합

`var x = ...;` / `x = ...;` 문장과, `x`를 구성하는 연속된 후속 문장들을 하나의 체인으로 합침. 캐럿은 anchor 문장에.

```csharp
_titleBar = new Border().MinHeight(40);
_titleBar.Child(body);
_titleBar.Click += () => OnClick();
// -> Merge into fluent chain
_titleBar = new Border()
    .MinHeight(40)
    .Child(body)
    .OnClick(() => OnClick());
```

### 규칙

1. **anchor.** 단일 지역 선언(`var x = ...`) 또는 단순 대입(`x = ...`). 대입 값은 object creation, 호출
   체인, 식별자, 멤버 접근이어야 함. 호출이 괄호 없이 덧붙으므로 조건식 같은 느슨한 식은 의미가 바뀜.
2. **후속.** `x`를 구성하는 연속된 문장(아래 문장 종류 표). 각 결과 호출이 `x`의 타입을 반환해야
   (체인/재대입 유효) 하고, 첫 비매칭 문장에서 수집 중단.
3. **최상위 문.** 최상위 문으로 작성된 파일도 동일하게 동작. 문장이 아닌 컴파일 단위 멤버가 나오면
   수집을 멈추므로 뒤따르는 타입 선언은 흡수되지 않음.
4. **`.Ref(out var x)`.** 참조 타입의 *지역 선언*이면 `var x = ...;` 대신 `.Ref(out var x)`로 인라인 캡처
   (MewUI 관용구), `Ref` 확장이 존재할 때. 이 호출은 인스턴스를 만드는 식 바로 뒤에 들어감.
   필드/속성 대입은 `x = chain;` 유지.

   ```csharp
   var panel = new StackPanel().Spacing(8);
   panel.Vertical();
   panel.Add(header);
   panel.Add(body);
   // -> Merge into fluent chain
   new StackPanel()
       .Ref(out var panel)
       .Spacing(8)
       .Vertical()
       .Children(header, body);
   ```

5. **컬렉션 setter.** 같은 컬렉션 setter로 대체되는 연속 호출은 위처럼 하나의 호출로 합쳐짐. setter가
   목록 전체를 한 번에 받기 때문.
6. 합쳐진 체인은 공유 레이아웃 엔진으로 펼침.

## `MEW1104` - 구성 문장을 fluent 호출로

문장 하나를 그와 동등한 fluent 호출로 변환. 캐럿은 그 문장에.

```csharp
_titleBar.Child = new DockPanel().Children(...);   // -> _titleBar.Child(new DockPanel()...)
_titleBar.Click += OnClick;                        // -> _titleBar.OnClick(OnClick)
Grid.SetColumn(_titleBar, 1);                      // -> _titleBar.Column(1)
panel.AddRange(a, b);                              // -> panel.Children(a, b)
```

### 문장 종류

MEW1103이 체인으로 접고 MEW1104가 단독 변환하는 형태들.

| 문장 | 결과 | 해석 근거 |
|---|---|---|
| `x.Prop = value;` | `.Prop(value)` | 속성 이름과 같은 확장 |
| `x.Event += handler;` | `.OnEvent(handler)` | `On` 접두 규약 |
| `Owner.SetProp(x, value);` | `.Prop(value)` | 매개변수 2개 정적 메서드의 `Set` 접두 규약 |
| `x.Add(a);` | `.Children(a)` | `Add`를 대체한다고 선언한 확장 (아래) |

해석되는 것이 없거나, 결과 호출이 수신자 타입을 반환하지 않거나, 이미 fluent 체인인 문장이면 미제공.

### 확장이 무엇을 설정하는지 선언하기

대부분의 확장은 위의 이름 규약으로 찾는다. 이름으로도 시그니처로도 대응시킬 수 없는 확장은 세 특성 중
하나로 무엇을 설정하는지 선언한다(`src/MewUI/Markup/Hints/FluentHintAttributes.cs`). 특성은 MewUI internal이고
분석기는 metadata name(`Aprillz.MewUI.FluentReplacesMemberAttribute`, `Aprillz.MewUI.FluentSetsMemberAttribute`,
`Aprillz.MewUI.FluentSetsConstantAttribute`)으로 읽으므로 분석기는 MewUI를 참조하지 않고, 다른 어셈블리도 같은
선언을 자기 internal 타입으로 컴파일할 수 있다. 멤버 자리에 확장을 넣었을 때 효과와 순서가 같아야 하며,
이를 보증하는 것은 이 선언뿐이므로 양쪽 본문을 사람이 대조한 뒤 추가한다.

**`FluentReplacesMember`** - 확장이 인스턴스 메서드 호출을 대체한다(MEW1103~MEW1106).

```csharp
[FluentReplacesMember(nameof(Panel.Add))]
[FluentReplacesMember(nameof(Panel.AddRange))]
public static T Children<T>(this T panel, params Element[] children) where T : Panel
```

확장의 유일한 값 매개변수는 대체 대상 호출의 모든 인자를 받는 `params` 배열이어야 한다.

**`FluentSetsMember`** - 매개변수가 이름을 적은 속성의 값을 받는다(MEW1101). 메서드의 값 매개변수 전부에
붙이면 복합 setter, 유일한 매개변수에 붙이면 별칭이다.

```csharp
public static T Command<T>(
    this T button,
    [FluentSetsMember(nameof(Button.Command))] Command? command,
    [FluentSetsMember(nameof(Button.CommandPresentationMode))] CommandPresentationMode presentation = CommandPresentationMode.None)
    where T : Button

public static T BorderThickness<T>(this T border, [FluentSetsMember(nameof(Border.NonUniformBorderThickness))] Thickness value) where T : Border
```

매개변수 순서가 대입 순서다. 특성 없는 값 매개변수가 하나라도 있으면(선택 매개변수 포함) 그 기본값이
다른 멤버를 덮어쓸 수 있으므로 후보가 아니다.

**`FluentSetsConstant`** - 무인자 축약형이 이름을 적은 속성에 상수를 대입한다(MEW1101). 한 메서드의 여러
선언은 `order`(0부터 연속) 순서의 계약 하나를 이룬다.

```csharp
[FluentSetsConstant(nameof(FrameworkElement.HorizontalAlignment), HorizontalAlignment.Center, order: 0)]
[FluentSetsConstant(nameof(FrameworkElement.VerticalAlignment), VerticalAlignment.Center, order: 1)]
public static T Center<T>(this T element) where T : FrameworkElement
```

**결합형** - 한 메서드에 둘을 함께 둘 수 있다. 값 매개변수 전부에 `FluentSetsMember`를, 메서드에
`FluentSetsConstant`를 하나 이상 붙인다. 계약은 매개변수 순서 뒤에 `order` 순서의 상수가 이어지며, 생성되는
호출은 매개변수 인자만 넘긴다.

```csharp
[FluentSetsConstant(nameof(Image.ViewBoxUnits), ImageViewBoxUnits.Pixels)]
public static Image ViewBoxPixels(this Image image, [FluentSetsMember(nameof(Image.ViewBox))] Rect? viewBox)
```

잘못된 선언(생성자 형상 불일치, 중복되거나 연속되지 않는 `order`, 중복 멤버, 빈 이름, 특성 없는
매개변수가 있는 메서드의 상수)은 무시하므로 변환을 만들지 않는다.

## `MEW1105` / `MEW1106` - 같은 둘의 진단 형태

MEW1103과 MEW1104는 캐럿을 정확한 문장에 놓아야 한다. MEW1105(병합)와 MEW1106(단일 문장)은 같은 대상을
`Hidden` 진단으로 보고하고 같은 수정을 제공하므로, Fix All로 파일 전체를 한 번에 변환할 수 있다.

MEW1105 병합이 흡수하는 문장은 MEW1106으로 중복 보고하지 않는다. 두 수정이 같은 문장을 건드리지 않게
하기 위해서다.

`Hidden`이라 lightbulb로만 보인다. 명령줄로 파일을 변환하려면 심각도를 올리고 `dotnet format`을 쓴다.

```ini
# .editorconfig
[*.cs]
dotnet_diagnostic.MEW1105.severity = suggestion
```

```
dotnet format analyzers <project> --severity info --diagnostics MEW1105
```

id는 한 번에 하나씩 준다. 여러 개를 묶으면 `dotnet format`이 Fix All 액션을 만들지 못하는 경우가 있다.
한 번의 실행은 겹치지 않는 수정만 적용하므로 파일이 더 이상 바뀌지 않을 때까지 반복한다.

## 테스트

`tests/MewUI.Analyzers.Test`는 `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing`와
`.CodeRefactoring.Testing`를 사용. 테스트는 자체 fluent API를 테스트 소스에 정의해서 MewUI 빌드 없이 해석을
실행.

```
dotnet test tests/MewUI.Analyzers.Test/MewUI.Analyzers.Test.csproj
```
