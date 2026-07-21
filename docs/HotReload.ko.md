# Hot Reload

이 문서는 MewUI C# 마크업 앱의 **Hot Reload** 동작을 설명합니다. 코드를 편집하면
`dotnet watch`(또는 IDE의 Hot Reload)가 실행 중인 앱에 변경을 적용하고, MewUI는 **바뀐
빌드 코드에 해당하는 노드만** 다시 빌드합니다.

Hot Reload는 디버그(JIT) 세션에서만 동작합니다. 릴리스/NativeAOT 빌드에서는 완전히
비활성화됩니다.

---

## 1. 활성화 (설정 불필요)

MewUI가 Hot Reload 핸들러를 자체 어셈블리에 등록하므로 **앱에 별도 선언이 필요 없습니다.**
`dotnet watch`로 실행하면 바로 동작합니다.

```
dotnet watch run
```

> 이전 버전에서 쓰던 `#if DEBUG [assembly: MetadataUpdateHandler(...)]` 수동 선언은 더 이상
> 필요 없습니다. 남아 있으면 중복 등록이 되므로 제거하세요.

### 끄기 (opt-out)

프로젝트에서 다음 속성으로 Hot Reload를 비활성화할 수 있습니다.

```xml
<PropertyGroup>
  <MewUIHotReload>false</MewUIHotReload>
</PropertyGroup>
```

---

## 2. 편집 종류별 반영과 동작 방식

무엇을 편집하느냐에 따라 **다시 빌드하는 범위**와 **유지되는 상태**가 달라집니다.

| 편집한 것 | 화면 반영 | 다시 빌드하는 범위 | 유지되는 상태 |
| --- | --- | --- | --- |
| Window `.OnBuild(...)` / UserControl `OnBuild()` 본문 | 저장 즉시 | 그 창/컨트롤 하나 | 그 노드 **바깥**(형제·다른 창) |
| 이벤트 핸들러 `.OnClick(...)` 등의 본문 | 다음 이벤트부터 | **없음** | **전부** 유지 |
| 아이템 템플릿 `ItemTemplate` 등의 빌드 함수 | 저장 즉시 | 그 템플릿을 쓰는 컨트롤 | 그 컨트롤 바깥 |
| 기본 스타일 정의 | 다음 재빌드 때 | (재빌드에 편승) | — |
| 시그니처·구조 변경 (rude edit) | 앱 재시작 후 | 전체 | 없음 |

### 왜 이렇게 되나

**1) 변경 감지는 타입이 아니라 빌드 함수의 본문 단위입니다.**
런타임은 "어떤 타입이 바뀌었는지"만 알려줍니다. 그런데 C# 마크업에서는 빌드 코드와 이벤트
핸들러가 보통 같은 타입(같은 파일)에 함께 있으므로, 타입만 보면 핸들러를 고쳐도 빌드가
바뀐 것으로 오해합니다. MewUI는 대신 **등록된 빌드 함수의 본문 자체가 실제로 바뀌었는지**를
비교합니다. 컴파일러가 만드는 잡음(메타데이터 토큰 재배치 등)은 무시하므로, 같은 타입 안에서
핸들러만 고친 편집은 빌드 함수의 본문을 바꾸지 않아 재빌드로 이어지지 않습니다.

**2) 핸들러 편집은 재빌드가 필요 없습니다.**
.NET은 메서드 본문을 제자리에서 교체합니다. 빌드 시점에 연결된 이벤트 델리게이트는 그대로
남아 있고, 다음 이벤트가 발생하면 교체된 새 본문이 실행됩니다. 그래서 창을 다시 만들지
않아도 되고, 입력 중인 값·스크롤·선택 같은 상태가 유지됩니다.

**3) 영향받은 가장 얕은 노드만 다시 빌드합니다.**
빌드 함수가 바뀐 노드가 여럿이면, 조상 노드를 다시 빌드할 때 그 자식들이 새로 생성되므로
조상만 다시 빌드합니다. 바뀌지 않은 부분과 다른 창은 그대로 둡니다.

이 원리에서 다음 한계도 따라옵니다.

- 빌드 함수가 **다른 타입의 헬퍼**만 호출하고 그 헬퍼만 편집하면, 빌드 함수 본문은 그대로라
  감지되지 않을 수 있습니다. 빌드 함수(또는 같은 타입 안의 코드)를 편집하면 반영됩니다.
- 시그니처·베이스 타입·구조 변경 등은 런타임이 변경 델타를 애초에 적용하지 못해(rude edit)
  재시작이 필요합니다.

---

## 3. 빌드 코드 등록

Hot Reload가 다시 빌드하는 대상은 빌드 함수가 등록된 노드입니다. 다음 두 가지가 자동으로
등록됩니다.

- **Window의 `.OnBuild(...)`**: 콜백을 등록하고 초기 빌드도 실행합니다.
- **UserControl의 `OnBuild()` override**: 컨트롤이 콘텐츠를 빌드할 때 등록됩니다.

```csharp
var window = new Window()
    .OnBuild(w => w
        .Title("Hot Reload Demo")
        .Content(new StackPanel()
            .Spacing(8)
            .Children(
                new TextBlock().Text("코드를 수정하고 저장하세요."),
                new Button()
                    .Content("Click")
                    .OnClick(() => Console.WriteLine("clicked"))   // 이 본문 편집 → 재빌드 없음
            )));
```

위에서 `TextBlock`의 텍스트를 바꾸면 창이 다시 빌드되고, `OnClick` 본문을 바꾸면 재빌드
없이 다음 클릭부터 새 동작이 적용됩니다.

---

## 4. 전체 예시

```csharp
using Aprillz.MewUI;
using Aprillz.MewUI.Markup;
using Aprillz.MewUI.Controls;

var window = new Window()
    .OnBuild(w => w
        .Title("Hot Reload Demo")
        .Content(new StackPanel()
            .Spacing(8)
            .Children(
                new TextBlock().Text($"Now: {DateTime.Now}"),
                new Button().Content("Click")
            )));

Application.Create()
    .UseWin32()
    .UseDirect2D()
    .Run(window);
```

`dotnet watch run`으로 실행한 뒤 `TextBlock` 텍스트를 편집하면 변경이 즉시 반영됩니다.

---

## 5. 참고 사항

- **다시 빌드된 노드 내부의 상태는 초기화됩니다.** 유지해야 하는 상태는 빌드 함수 밖의
  ViewModel/서비스에 두세요. (핸들러 편집은 재빌드가 없으므로 상태가 보존됩니다.)
- Hot Reload는 디버그(JIT)에서만 동작하며, 릴리스·NativeAOT에서는 아무 동작도 하지 않습니다.
- `Application`/`Dispatcher`가 아직 없을 때 도착한 변경은 무시됩니다.
