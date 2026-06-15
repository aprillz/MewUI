# Aprillz.MewUI.Analyzers

MewUI fluent markup용 Roslyn analyzer와 refactoring 모음. NuGet analyzer(`analyzers/dotnet/cs`)로 배포되어
참조 하나로 Visual Studio, VS Code(C# Dev Kit), Rider, CI에서 동작합니다. 빌드 타임 전용이라
런타임/NativeAOT 산출물에는 아무것도 들어가지 않습니다.

- 네임스페이스: `Aprillz.MewUI.Analyzers`
- 타깃: `netstandard2.0`, Roslyn은 호환성을 위해 4.8로 핀 고정.
- English: [README.md](../README.md)

## 상태

| Id | 기능 | 종류 | 소스 |
|---|---|---|---|
| `MEW1101` | 이니셜라이저 -> fluent 체인 | analyzer + code fix | `InitializerToFluentAnalyzer.cs`, `InitializerToFluentCodeFix.cs` |
| `MEW1102` | fluent 체인 펼침 / 접기 | refactoring | `FluentChainFormatRefactoring.cs` |
| `MEW1103` | 문장들을 fluent 체인으로 병합 | refactoring | `MergeChainStatementsRefactoring.cs` |
| `MEW1104` | 속성 대입 -> fluent 호출 | refactoring | `AssignmentToFluentCallRefactoring.cs` |

공유 컴포넌트:

- `FluentMethodResolver.cs`: 속성/이벤트 이름을 fluent setter 확장으로 해석. **extension 메서드가
  source of truth**(별도 매핑 표 없음 -> drift 없음)이고, `On`-prefix(`Click` -> `OnClick`)도 시도.
- `FluentChainLayout.cs`: 공유 레이아웃 엔진(MEW1101/1102/1103 공용). 체인을 구조에서 재구성하고,
  element 자식은 트리로 펼치고, 값은 inline 유지하며, 멀티라인 람다 본문을 재들여쓰기.

`tests/MewUI.Analyzers.Test`에 테스트 20개가 4개 기능을 모두 커버.

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

1. **트리거.** object initializer를 가진 생성식 중, 멤버 `Name = value` 하나 이상이 fluent setter로
   해석될 때.
2. **fluent setter.** 생성 타입에 호출 가능한, 이름이 정확히 `Name`(이벤트는 `On` + `Name`)인 extension
   메서드로, `value`가 변환되는 단일 파라미터를 받는 것. extension 메서드가 진실의 출처라, 추가한 setter는
   자동 인식.
3. **변환.** 매칭 멤버는 소스 순서대로 `.Name(value)`; `new T { ... }`엔 `()` 추가. 변환된 멤버가 2개 이상이면
   줄바꿈해서 펼침.
4. **이벤트.** delegate 프로퍼티 `Click = handler`는 `On`-prefix 규칙으로 `.OnClick(handler)`.

   ```csharp
   new MenuItem { Text = "Open", Click = OnOpen }
   // -> Convert to fluent chain
   new MenuItem()
       .Text("Open")
       .OnClick(OnOpen)
   ```

5. **부분 변환.** setter 없는 멤버는 잔여 이니셜라이저로 남음.

   ```csharp
   new Widget { Text = "hi", Tag = obj, Width = 5 }
   // -> Tag는 setter가 없어 잔여 이니셜라이저로
   new Widget() { Tag = obj }
       .Text("hi")
       .Width(5)
   ```

6. setter로 해석되는 멤버가 없으면 **진단 없음**.

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
           new Button().Content("A").Width(80),

           new Button().Content("B").Width(80)
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

1. **anchor.** 단일 지역 선언(`var x = ...`) 또는 단순 대입(`x = ...`).
2. **후속.** 연속된 `x.Method(...);`(fluent 호출) 또는 `x.Event += handler;`(이벤트 구독 ->
   `.OnEvent(handler)`로 흡수). 각각 `x`의 타입을 반환해야(체인/재대입 유효) 하고, 첫 비매칭 문장에서 수집 중단.
3. **`.Ref(out var x)`.** 참조 타입의 *지역 선언*이면 `var x = ...;` 대신 `.Ref(out var x)`로 인라인 캡처
   (MewUI 관용구), `Ref` 확장이 존재할 때. 필드/속성 대입은 `x = chain;` 유지.

   ```csharp
   var panel = new StackPanel();
   panel.Vertical();
   panel.Spacing(8);
   // -> Merge into fluent chain
   new StackPanel()
       .Ref(out var panel)
       .Vertical()
       .Spacing(8);
   ```

4. 합쳐진 체인은 공유 레이아웃 엔진으로 펼침.

## `MEW1104` - 속성 대입을 fluent 호출로

`receiver.Prop = value;`를, 그 타입에 `Prop` fluent setter가 있으면 `receiver.Prop(value);`로 변환.
캐럿은 대입문에.

```csharp
_titleBar.Child = new DockPanel().Children(...);
// -> Convert to fluent call
_titleBar.Child(new DockPanel().Children(...));
```

대입 멤버에 대한 fluent setter가 해석 안 되면 미제공.

## 테스트

`tests/MewUI.Analyzers.Test`는 `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing`와
`.CodeRefactoring.Testing`를 사용. 테스트는 자체 fluent API를 테스트 소스에 정의해서 MewUI 빌드 없이 해석을
실행.

```
dotnet test tests/MewUI.Analyzers.Test/MewUI.Analyzers.Test.csproj
```

전체 설계는 [agent/fluent-formatter/plan.md](../../agent/fluent-formatter/plan.md), nuget 동봉 방식은
[agent/fluent-formatter/packaging-plan.md](../../agent/fluent-formatter/packaging-plan.md) 참고.
