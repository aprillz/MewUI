# Hot Reload

이 문서는 MewUI C# 마크업 앱에서 **옵션으로 사용하는 Hot Reload 흐름**을 설명합니다.
Hot Reload는 **창 단위**로 등록된 빌드 콜백을 다시 실행해서 내용을 재구성합니다.

---

## 1. Hot Reload 활성화 (앱 어셈블리)

앱 어셈블리에 MetadataUpdateHandler를 추가합니다 (DEBUG 전용):

```csharp
#if DEBUG
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(
    typeof(Aprillz.MewUI.HotReload.MewUiMetadataUpdateHandler))]
#endif
```

`Program.cs` 또는 별도의 `AssemblyInfo.cs`에 넣으면 됩니다.

---

## 2. 빌드 콜백 등록

Hot Reload는 빌드 콜백이 등록된 **창**만 다시 빌드합니다.
C# 마크업의 `OnBuild(...)`는 콜백을 등록하고 초기 빌드도 실행합니다.

```csharp
var window = new Window()
    .OnBuild(w =>
    {
        w.Title("Hot Reload Demo");
        w.Content(new StackPanel()
            .Spacing(8)
            .Children(
                new TextBlock().Text("코드를 수정하고 Hot Reload 하세요."),
                new Button().Content("Click")
            ));
    });
```

---

## 3. 전체 예시

```csharp
#if DEBUG
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(
    typeof(Aprillz.MewUI.HotReload.MewUiMetadataUpdateHandler))]
#endif

using Aprillz.MewUI;
using Aprillz.MewUI.Markup;
using Aprillz.MewUI.Controls;

// 1) 창 빌드 로직을 등록 (코드 변경 후 Hot Reload 되면 DateTime이 갱신됨)
var window = new Window()
    .OnBuild(w => w
        .Title("Hot Reload Demo")
        .Content(new StackPanel()
            .Spacing(8)
            .Children(
                new TextBlock().Text($"Now: {DateTime.Now}"),
                new Button().Content("Click")
            )));

// 2) 플랫폼/백엔드 선택 후 실행
Application.Create()
    .UseWin32()
    .UseDirect2D()
    .Run(window);
```

---

## 4. 참고 사항

- Hot Reload는 **옵션 기능**입니다. 빌드 콜백이 없으면 동작하지 않습니다.
- UI 스레드에서 빌드 콜백을 다시 호출하는 방식입니다.
- 상태 유지가 필요하면 별도의 ViewModel/서비스에 보관하세요.
- NativeAOT에서는 Hot Reload가 지원되지 않습니다.
- `MewUiHotReload.RequestReload()`로 수동 트리거도 가능합니다.
