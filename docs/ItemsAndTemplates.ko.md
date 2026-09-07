# 아이템과 템플릿

이 문서는 MewUI에 현재 구현된 아이템/템플릿 시스템을 설명한다.

## 개요

템플릿은 아이템을 재사용 가능한 `FrameworkElement`로 변환한다. 기본 흐름은 다음과 같다.

1. 컨테이너 생성 시 뷰를 한 번 만든다.
2. 아이템이 연결될 때 데이터를 바인딩한다.
3. 재사용 시 추적된 리소스를 정리한다.

이 메커니즘은 `ListBox`, `ComboBox`, `TreeView`, `GridView` 등 아이템 컨트롤에서 사용된다.

## Items 개요

MewUI의 아이템 컨트롤은 `ItemsView` 추상화를 기반으로 동작한다.

1. `Items(...)`는 `ItemsView`를 생성하거나 래핑한다.
2. 컨트롤은 `ItemsView`에서 아이템 수, 텍스트, 선택 상태를 조회한다.
3. 템플릿은 보이는 항목의 뷰를 생성/바인딩한다.

`ItemsView`는 데이터 계층이고, 템플릿은 뷰 계층이다. 두 개는 함께 사용하도록 설계되어 있다.

## 핵심 타입

### IDataTemplate

`IDataTemplate`은 뷰 생성과 바인딩 계약을 정의한다.

```csharp
public interface IDataTemplate
{
    FrameworkElement Build(TemplateContext context);
    void Bind(FrameworkElement view, object? item, int index, TemplateContext context);
    void Unbind(FrameworkElement view, object? item, int index, TemplateContext context);
}
```

`Unbind`는 컨테이너가 다음 아이템을 받기 전에 불린다. 기본 구현은 비어 있으므로 `+=`로 직접 건 구독처럼 컨텍스트가 되돌리지 못하는 것만 여기서 뗀다.

`IDataTemplate<TItem>`은 타입 안전한 바인딩을 제공한다.

```csharp
public interface IDataTemplate<in TItem> : IDataTemplate
{
    void Bind(FrameworkElement view, TItem item, int index, TemplateContext context);
    void Unbind(FrameworkElement view, TItem item, int index, TemplateContext context);
}
```

### DelegateTemplate

`DelegateTemplate<TItem>`은 기본 구현체다. 기본 템플릿의 동작도 이 예제로 설명된다. 가장 단순한 형태는 `TextBlock` 하나를 만들고 `GetText` 또는 `ToString()` 결과를 바인딩하는 것이다.

```csharp
var template = new DelegateTemplate<Person>(
    build: ctx =>
    {
        // 기본 템플릿 형태: 단일 TextBlock
        // 이름 기반 접근이 필요하면 TemplateContext를 사용한다.
        return new TextBlock().Register(ctx, "Text");
    },
    bind: (view, item, index, ctx) =>
    {
        ctx.Get<TextBlock>("Text").Text = item.Name;
    });
```

`TemplateContext`가 필요 없으면 바로 뷰를 반환하는 형태도 가능하다.

```csharp
var template = new DelegateTemplate<Person>(
    build: _ => new TextBlock(),
    bind: (view, item, index, _) => ((TextBlock)view).Text = item.Name);
```

### TemplateContext

컨테이너 하나에 컨텍스트 하나가 붙어 컨테이너와 수명을 함께한다. 하는 일은 둘이다. 요소에 이름을 붙여 두는 것과, 아이템이 바뀔 때마다 걸었던 바인딩과 이벤트 구독을 되돌리는 것이다.

```csharp
public sealed class TemplateContext : IDisposable
{
    public void Register<T>(string name, T element) where T : UIElement;
    public T Get<T>(string name) where T : UIElement;

    public void Bind<T>(MewObject target, MewProperty<T> property, ObservableValue<T> source, BindingMode? mode = null);
    // 변환기 및 BindingPath 오버로드 있음

    public void Subscribe<TSource, THandler>(
        TSource source, Action<TSource, THandler> add, Action<TSource, THandler> remove, THandler handler)
        where TSource : class where THandler : Delegate;

    public void Reset();
}
```

이름은 `Build`에서 등록하고 `Bind`에서 조회한다.

```csharp
ctx.Get<TextBlock>("Name").Text = item.Name;
```

바인딩과 구독은 `Bind`에서 건다. 매번 걸어도 누적되지 않는다.

```csharp
ctx.Subscribe(
    item,
    static (source, handler) => source.Changed += handler,
    static (source, handler) => source.Changed -= handler,
    () => Refresh());
```

## 템플릿 생명주기

1. `Build`는 컨테이너 생성 시 한 번 호출된다.
2. `Bind`는 아이템이 연결될 때 호출된다. **직전에 이전 아이템에 대한 `Unbind`와 컨텍스트 정리가 먼저 일어난다.**
3. 컨테이너가 재사용되면 이 과정이 반복된다.

정리(`Reset`)가 되돌리는 것과 되돌리지 않는 것을 구분해야 한다.

| | 정리 대상인가 |
|---|---|
| `ctx.Bind`로 건 바인딩 | 예. `ClearBinding`이 실행된다 |
| `ctx.Subscribe`로 건 구독 | 예. 등록 시 넘긴 `remove`가 실행된다 |
| `ctx.Register`로 붙인 이름 | **아니다.** 컨테이너가 사는 동안 유효하다 |
| `+=`로 직접 건 구독 | **아니다.** `Unbind`에서 직접 떼어야 한다 |
| 요소에 직접 대입한 속성 값 | **아니다.** `Bind`에서 항상 덮어써야 한다 |

정리는 등록의 역순으로 실행된다. 하나가 예외를 던져도 나머지는 모두 실행되고, 첫 예외가 마지막에 다시 던져진다.

마지막 줄이 특히 중요하다. 속성을 **조건부로만** 대입하면 재사용된 컨테이너에 이전 아이템의 값이 남는다.

```csharp
// 잘못됨: 조건이 거짓인 아이템에 이전 아이템의 값이 남는다
bind: (view, item, _, _) => { if (item.IsUrgent) ((TextBlock)view).Foreground = Colors.Red; }

// 올바름: 두 경우 모두 대입한다
bind: (view, item, _, _) => ((TextBlock)view).Foreground = item.IsUrgent ? Colors.Red : Colors.Black;
```

## 재바인딩 시점

`Bind`는 컨테이너가 아이템에 대해 실현될 때마다 불린다. 아이템이 화면 밖으로 나가면 컨테이너가 회수되므로, 다시 스크롤해 들어오면 또 불린다. 아이템 컬렉션·템플릿·`ItemPadding`·테마 변경은 화면에 있는 컨테이너 전부를 재바인딩한다.

선택 변경은 재바인딩하지 않는다. 선택 배경은 컨트롤이 그리고, 컨테이너의 `IsSelected`만 갱신된다(아래 컨테이너 훅 참조). 선택에 따라 템플릿 안의 무엇이 달라져야 하면 `Bind`에서 분기하지 말고 그 값을 구독한다.

건너뛰는 경우는 하나뿐이다. 같은 아이템으로 화면에 남아 있고 바인딩을 무효화한 것이 없을 때다. 단순 재배치만으로는 재바인딩되지 않는다.

즉 `Bind`는 자주 불린다고 보고 짜야 한다. `Build`에서 준비할 수 있는 할당을 `Bind`에 두지 않는다.

## 컨테이너 훅: PrepareContainer

행 **전체**에 동작을 붙여야 할 때가 있다. 행 위 어디를 우클릭해도 뜨는 컨텍스트 메뉴, 행 툴팁, 행 단위 커서나 드래그가 그렇다. 템플릿 루트에 달면 템플릿이 차지한 영역에서만 동작하고 템플릿마다 같은 코드를 반복하게 된다. `PrepareContainer`는 컨트롤이 아이템마다 두는 컨테이너를 넘겨주므로 그 자리에 한 번만 붙이면 된다.

```csharp
list.PrepareContainer<ChatMessage>((container, message, index, ctx) => container.ContextMenu = messageMenu);
```

콜백은 `(container, item, index, context)`를 받고 템플릿의 `Bind`가 끝난 뒤 실행된다. `context`는 그 컨테이너의 `TemplateContext`다. 짝인 `ClearContainer`는 컨테이너가 다음 아이템을 받기 전에 불리며, 대부분의 훅은 이것이 필요 없다.

| 컨트롤 | 컨테이너 | 훅이 없을 때 |
|---|---|---|
| `ListBox`, `ItemsControl` | `ItemContainer`. 훅을 등록한 동안만 템플릿 루트를 감싼다 | 템플릿 루트가 곧 컨테이너. 래퍼 없음 |
| `TreeView` | `ItemContainer`. 들여쓰기와 확장기까지 행 전체를 덮고 콘텐츠를 그 뒤로 밀어 넣는다 | 템플릿 루트가 콘텐츠 구간에만 놓임 |
| `GridView` | `GridViewRow`. 항상 있는 행 요소를 그대로 넘긴다 | 변화 없음 |

훅을 쓰지 않는 앱은 요소를 하나도 더 만들지 않는다. 훅을 등록하거나 해제하면 컨테이너가 새로 만들어진다.

### 컨테이너가 알려주는 것

`ItemContainer`와 `GridViewRow`는 `Index`, `Item`, `IsSelected`를 읽기 전용으로 노출한다. `IsSelected`는 선택이 바뀔 때 갱신되지만 컨테이너가 그것을 그리지는 않는다.

`Item`은 명령 시스템의 인자 공급원이기도 하다. 컨테이너에 단 컨텍스트 메뉴나 컨테이너 안의 버튼이 명령을 실행하면, 타입을 적은 처리기가 그 아이템을 인자로 받는다. 자세한 것은 [CommandSystem.ko.md](CommandSystem.ko.md)의 인자 절에 있다.

### 되돌려지는 것

컨테이너는 아이템 사이에 재활용된다. 훅이 컨테이너에 직접 대입한 다음 속성은 다음 바인드 전에 기본값으로 돌아간다: `ContextMenu`, `ToolTip`, `IsEnabled`, `IsHitTestVisible`, `Cursor`, `Opacity`, `Tag`. 그 밖의 속성은 템플릿과 같은 규칙이다. 매번 대입하거나 `ctx.Bind`로 건다.

`ctx.Bind`와 `ctx.Subscribe`로 건 것은 컨텍스트가 정리한다. `+=`로 직접 건 구독만 `ClearContainer`에서 뗀다.

### 훅은 바인드 때만 돈다

훅은 `Bind`와 같은 시점에 불린다. 선택 변경이나 접힘/펼침 같은 컨트롤 상태 변화는 재바인딩을 일으키지 않으므로, 그에 따라 달라지는 값은 훅 안에서 구독으로 따라가게 한다.

```csharp
list.PrepareContainer<TodoItem>((container, todo, _, ctx) =>
{
    container.Opacity = todo.IsDone.Value ? 0.6 : 1;
    ctx.Subscribe(todo.IsDone,
        static (source, handler) => source.Changed += handler,
        static (source, handler) => source.Changed -= handler,
        () => container.Opacity = todo.IsDone.Value ? 0.6 : 1);
});
```

### TreeView 컨테이너

`TreeView`의 컨테이너는 행 전체를 덮으므로 들여쓰기와 확장기 위에서도 메뉴와 툴팁이 동작한다. 콘텐츠는 컨테이너의 `Padding`으로 들여쓰기만큼 밀리므로 훅에서 `Padding`을 바꾸지 않는다. 확장기 클릭과 키보드 탐색은 그대로 동작한다.

### ContextMenu와 ToolTip

`ContextMenu`와 `ToolTip`은 `FrameworkElement`의 속성이다. 컨테이너뿐 아니라 도형처럼 컨트롤이 아닌 요소에도 달 수 있다. 우클릭은 요소에서 조상 방향으로 전달되므로, 템플릿 안의 어느 요소를 눌러도 컨테이너에 단 메뉴가 열린다.

## TemplatedItemsHost와 가상화

`TemplatedItemsHost`는 아이템 컨트롤의 내부 헬퍼다.

역할:

1. `IDataTemplate.Build`로 컨테이너 생성
2. `IDataTemplate.Bind`로 아이템 바인딩
3. 재사용 시 `TemplateContext` 정리
4. 레이아웃과 가상화는 `VirtualizedItemsPresenter`에 위임

`ListBox`, `ComboBox`, `TreeView`, `GridView`가 이 경로를 사용한다.

## 컨트롤 사용

### ListBox

```csharp
new ListBox()
    .Items(people, p => p.Name)
    .ItemTemplate(template);
```

`ItemTemplate`을 지정하지 않으면 기본 템플릿(`TextBlock` + `GetText`/`ToString()`)이 사용된다.

```csharp
// 기본 템플릿 사용
new ListBox().Items(people, p => p.Name);
```

`Items(...)`의 두 번째 인자는 텍스트 선택기다. 기본 템플릿(`TextBlock`)이 각 아이템에서 표시할 문자열을 이 함수로 얻는다.

### ComboBox

```csharp
new ComboBox()
    .Items(people, p => p.Name)
    .ItemTemplate(template);
```

```csharp
// 기본 템플릿 사용
new ComboBox().Items(people, p => p.Name);
```

`Items(...)`의 두 번째 인자는 기본 템플릿에서 사용하는 텍스트 선택기다.

### TreeView

```csharp
new TreeView()
    .Items(treeItems)
    .ItemTemplate(template);
```

```csharp
// 기본 템플릿 사용
new TreeView().Items(treeItems);
```

계층 데이터를 바로 넘기는 오버로드도 사용할 수 있다.

```csharp
new TreeView().Items(
    roots,
    childrenSelector: n => n.Children,
    textSelector: n => n.Name,
    keySelector: n => n.Id);
```

### GridView

GridView는 열 단위로 셀 템플릿을 사용한다.

```csharp
var grid = new GridView();
grid.Columns(
    new GridViewColumn<Person>()
        .Header("Name")
        .Width(160)
        .Bind(
            build: ctx => new TextBlock().Register(ctx, "Text"),
            bind: (TextBlock t, Person p, int _, TemplateContext __) => t.Text = p.Name));
```

## 기본 템플릿

템플릿을 제공하지 않으면 위 예제와 동일하게 동작한다. `TextBlock`을 만들고 `GetText` 또는 `ToString()` 결과를 바인딩한다.

## 권장 패턴

1. `TemplateContext.Register`와 `Get`을 사용해 이름 기반 접근을 유지한다.
2. 구독은 `+=`가 아니라 `TemplateContext.Subscribe`로 건다. 해제가 따라온다.
3. `Bind`에서 무거운 객체 생성을 피하고 `Build`에서 준비한다.
4. `Bind`는 동일 컨테이너에 여러 번 호출될 수 있음을 전제로 한다. 모든 속성을 조건 없이 대입한다.

## 단일 뷰 오버로드 (기본)

템플릿이 단일 컨트롤만 만들고, 이름 조회나 리소스 추적이 필요 없으면 `TemplateContext`를 사용하지 않는 오버로드를 쓸 수 있다. 내부적으로는 컨텍스트가 생성되지만, 사용자 코드에서는 다루지 않는다.

```csharp
// 단일 뷰 생성 + 아이템만 바인딩 (context 사용 없음)
listBox.ItemTemplate(
    build: _ => new TextBlock(),
    bind: (TextBlock view, Person item) => view.Text = item.Name);
```

일반적인 케이스를 단순하게 유지하면서 동일한 템플릿 파이프라인을 사용한다.
