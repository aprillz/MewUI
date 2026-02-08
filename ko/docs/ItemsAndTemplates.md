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
}
```

`IDataTemplate<TItem>`은 타입 안전한 바인딩을 제공한다.

```csharp
public interface IDataTemplate<in TItem> : IDataTemplate
{
    void Bind(FrameworkElement view, TItem item, int index, TemplateContext context);
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

`TemplateContext`는 다음을 위해 사용한다.

1. 이름 기반 요소 등록과 조회

```csharp
public sealed class TemplateContext : IDisposable
{
    public void Register<T>(string name, T element) where T : UIElement;
    public T Get<T>(string name) where T : UIElement;
    // 내부 수명 관리는 공개 계약에 포함되지 않는다.
}
```

사용 예시:

```csharp
ctx.Get<TextBlock>("Name").Text = item.Name;
```

## 템플릿 생명주기

1. `Build`는 컨테이너 생성 시 한 번 호출된다.
2. `Bind`는 아이템이 연결될 때 호출된다.
3. 컨텍스트 정리는 `Bind` 직전과 재사용 시점에 내부적으로 처리된다.

이 구조로 컨테이너 재사용과 구독 해제가 안전해진다.

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
2. `TemplateContext.Track`으로 구독과 리소스를 추적한다.
3. `Bind`에서 무거운 객체 생성을 피하고 `Build`에서 준비한다.
4. `Bind`는 동일 컨테이너에 여러 번 호출될 수 있음을 전제로 한다.

## 단일 뷰 오버로드 (기본)

템플릿이 단일 컨트롤만 만들고, 이름 조회나 리소스 추적이 필요 없으면 `TemplateContext`를 사용하지 않는 오버로드를 쓸 수 있다. 내부적으로는 컨텍스트가 생성되지만, 사용자 코드에서는 다루지 않는다.

```csharp
// 단일 뷰 생성 + 아이템만 바인딩 (context 사용 없음)
listBox.ItemTemplate(
    build: _ => new TextBlock(),
    bind: (TextBlock view, Person item) => view.Text = item.Name);
```

일반적인 케이스를 단순하게 유지하면서 동일한 템플릿 파이프라인을 사용한다.
