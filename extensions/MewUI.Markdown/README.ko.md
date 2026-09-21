# Aprillz.MewUI.Markdown

[MewUI](https://github.com/aprillz/MewUI) 요소로 직접 그리는 Markdown 컨트롤입니다.

**웹 뷰 비의존.** [Markdig](https://github.com/xoofx/markdig)가 문서를 파싱하고, 확장이 그 결과를 MewUI 텍스트 레이아웃과 컨트롤로 바꿉니다. 따라서 모든 MewUI 백엔드(Direct2D, GDI, MewVG/OpenGL)에서 그려지며 NativeAOT/트리밍 호환입니다.

## 설치

```
dotnet add package Aprillz.MewUI.Markdown
```

`net8.0`과 `net10.0`을 대상으로 합니다.

## 빠른 시작

```csharp
using Aprillz.MewUI.Markdown;

var viewer = new MarkdownViewer()
    .Markdown(File.ReadAllText("README.md"))
    .OnLinkRequested(args => status.Text = args.Url);
```

`MarkdownViewer`는 스스로 스크롤하고 가상화합니다. `MarkdownPresenter`는 뷰포트가 없는 같은 컨트롤로, 항상 전체 트리를 만듭니다. 따라서 작거나 중간 크기 문서에 한해 스크롤 컨테이너에 넣어 사용합니다. 두 컨트롤의 API는 같고, 아래 속성마다 대응하는 플루언트 확장 메서드가 있습니다.

| 속성 | 용도 |
|---|---|
| `Markdown` | 원본 텍스트 |
| `Options` | 사용할 문법 (`MarkdownOptions`) |
| `MarkdownTheme` | 블록 간격, 목록 들여쓰기, 코드 글꼴과 색 |
| `BaseUri` | 상대 링크와 이미지 URL의 기준 |
| `ImageResolver` | 이미지 공급자. 없으면 이미지를 불러오지 않음 |
| `CodeBlockFactory` | 기본 코드 블록 대체 |
| `Renderers` | Markdig 노드 타입별 사용자 정의 표현 |
| `ParseDelay` | 변경 디바운스와 UI 스레드 밖 파싱 |
| `IsSelectionEnabled` | 마우스 선택 끄기 |

이벤트: `LinkRequested`, `SelectionChanged`, `Copying`, `ParseFailed`.

## 문법

`MarkdownOptions`는 레코드입니다. 복사본을 만들어 항목 하나를 켜거나 끕니다.

```csharp
viewer.Options = new MarkdownOptions { UseHtmlFormatting = true };
```

| 옵션 | 기본값 |
|---|---|
| `UsePipeTables` | 켜짐 |
| `UseTaskLists` | 켜짐 (읽기 전용 체크박스) |
| `UseAutoLinks` | 켜짐 |
| `UseStrikethrough` | 켜짐 |
| `UseInserted` | 켜짐 |
| `UseMarked` | 켜짐 |
| `UseDefinitionLists` | 켜짐 |
| `UseFootnotes` | 켜짐 |
| `SoftBreakAsNewLine` | 꺼짐 |
| `UseHtmlFormatting` | 꺼짐 |

빽빽한 목록과 느슨한 목록은 서로 다른 블록 간격을 유지합니다.

**각주.** `[^label]` 참조는 처음 참조된 순서로 번호가 매겨지고 정의 목록으로 이동합니다. 각 정의에는 참조마다 되돌아가는 링크가 붙습니다. `UseFootnotes`를 끄면 각주 문법은 Markdig의 나머지 참조 링크 규칙이 처리합니다.

**HTML은 실행되지 않습니다.** 인라인 HTML과 HTML 블록은 글자 그대로 표시됩니다. `UseHtmlFormatting`을 켜면 제한된 태그 집합(`b`, `strong`, `i`, `em`, `u`, `s`, `del`, `tt`, `code`, `big`, `small`, `sub`, `sup`, `br`, `span`, `font`)을 네이티브 텍스트 서식으로 그리고 완결된 HTML 주석을 감춥니다. 이 모드는 고정 글꼴, 크기, 굵기, 색, 배경, 밑줄, 취소선 속성을 읽습니다. 구조 HTML, CSS, 링크, 이미지, script, iframe, 이벤트 속성, 깨진 주석을 비롯한 실행 가능한 내용은 그대로 글자로 남습니다.

## 코드 블록

내장 구문 강조는 없습니다. 기본 코드 블록은 정규화한 언어 이름과 겹쳐 놓은 복사 버튼을 보여 주며, 애플리케이션이 실행 중일 때 플랫폼 클립보드 서비스를 사용합니다.

`CodeBlockFactory`의 시그니처는 `Func<string, string?, FrameworkElement?>`입니다. 정규화한 코드 텍스트와 첫 언어 토큰을 받아, 기본 블록을 대체할 미부착 요소를 반환하거나 기본 표현을 쓰려면 `null`을 반환합니다. 호스트의 `SyntaxViewer`를 연결하는 지점입니다.

## 이미지

이미지 로딩은 애플리케이션이 리졸버를 제공할 때까지 꺼져 있습니다. 문서가 스스로 파일 시스템이나 네트워크에 닿을 수 없습니다. 리졸버는 URI, 바이트, 픽셀 한도를 강제하고 캐시를 둘 수 있습니다. 패키지는 이미지를 캐시하지 않습니다. 리졸버 작업은 비동기이며 동작 중인 UI 디스패처나 동기화 컨텍스트가 필요합니다.

```csharp
public sealed class CachedImages : IMarkdownImageResolver
{
    public ValueTask<MarkdownImageLease?> ResolveAsync(MarkdownImageRequest request, CancellationToken cancellationToken)
    {
        var decodedSource = imageCache.Rent(request.ResolvedUri);
        var lease = new MarkdownImageLease(decodedSource, () => imageCache.Return(decodedSource));
        return ValueTask.FromResult<MarkdownImageLease?>(lease);
    }
}
```

호스트가 소유권을 전부 갖는 소스라면 해제 콜백에 `null`을 넘깁니다. 컨트롤은 받은 리스를 모두 해제하지만 소스 자체를 Dispose하지는 않습니다. 이미지는 주변 텍스트와 함께 흐르고, 문단 너비에 맞춰 축소되며, 소스가 도착하면 다시 배치됩니다.

## 링크와 키보드

Tab과 Shift+Tab으로 링크 사이를 이동합니다. 블록을 넘나들고, 뷰어가 아직 만들지 않은 블록 안으로도 들어갑니다. Enter나 Space를 누르면 `LinkRequested`가 발생합니다. 이벤트는 원본 `Url`, `BaseUri` 기준으로 해석한 `ResolvedUri`, 링크 `Title`, 원본 구간을 전달합니다. 컨트롤이 스스로 무언가를 열지는 않습니다. 키보드 포커스를 가진 블록은 가상화 범위 밖으로 스크롤되어도 살아 있습니다.

## 선택

드래그하면 블록을 넘어 선택되고, 더블 클릭은 단어, 트리플 클릭은 블록을 선택합니다. Shift+클릭으로 확장하고 Escape로 해제합니다. 복사와 모두 선택은 `StandardCommands.Copy`와 `StandardCommands.SelectAll` 핸들러이므로, 플랫폼 단축키와 오른쪽 클릭 메뉴, 명령에 연결된 메뉴와 툴바가 모두 선택에 작용합니다.

`SelectedText`는 파싱된 문서에서 만들어지므로 화면 밖으로 스크롤된 블록도 포함합니다. 블록 사이와 목록 항목 사이에는 줄 바꿈, 표의 칸 사이에는 탭을 넣고, 항목이 선택 안에서 시작하면 목록 표식 뒤에 탭을 넣습니다. `Copying`은 클립보드에 쓰기 직전에 발생하며 텍스트를 바꾸거나 복사를 취소할 수 있습니다.

사용자 정의 렌더러나 `CodeBlockFactory`가 만든 요소는 강조 표시에 참여하지 않지만, 그 텍스트는 `SelectedText`에 나타납니다. 문서를 교체하면 선택이 해제되고, 테마와 너비 변경에는 선택이 유지됩니다.

## 가상화

`MarkdownViewer`는 보이는 범위의 앞뒤 한 뷰포트 정도에 드는 최상위 블록만 요소로 유지합니다. 범위를 벗어난 블록은 한동안 떼어 놓은 채로 보관하므로, 되돌아 스크롤하면 그 요소와 텍스트 레이아웃을 재사용합니다.

아직 도달하지 않은 블록은 추정 높이를 씁니다. 같은 종류로 측정된 블록들의 평균값입니다. 그래서 문서를 끝까지 훑기 전에는 스크롤 범위가 근사값이고, 추정이 자리를 잡는 동안 스크롤 막대 크기가 조금씩 달라질 수 있습니다. 뷰포트 근처 블록이 추정과 다르게 측정되면 뷰포트 맨 위 블록이 제자리에 남도록 스크롤 위치를 보정합니다. 문서 어디로 건너뛰어도 목표 주변 블록만큼만 비용이 들고, 테마와 너비가 바뀌어도 뷰포트 맨 위 블록은 그대로 유지됩니다.

## UI 스레드 밖 파싱

`ParseDelay`의 기본값은 0이고, 이때는 원본이 바뀔 때마다 UI 스레드에서 동기로 파싱합니다. 양수 값을 주면 원본 변경을 디바운스하고 작업자 스레드에서 파싱합니다. 최신 개정이 준비될 때까지 이전 문서가 계속 보이고, 밀려난 개정은 버려지며, 파싱이 실패하면 문서를 교체하는 대신 `ParseFailed`를 발생시킵니다. 백그라운드 파싱에는 동작 중인 UI 디스패처나 동기화 컨텍스트가 필요합니다.

## 문법과 표현 확장

`MarkdownOptions.ConfigurePipeline`은 Markdig 파이프라인 빌더를 받습니다. `UseMathematics()` 같은 Markdig 확장을 켤 수 있습니다. 렌더러가 없는 노드는 원본 텍스트로 표시됩니다.

`MarkdownRenderers`는 Markdig 노드 타입을 사용자 정의 표현에 연결합니다. `RegisterBlock<TBlock>`은 미부착 `FrameworkElement`를 반환하거나 기본 표현으로 넘기려면 `null`을 반환합니다. `RegisterInline<TInline>`은 노드의 텍스트 칸을 차지하는 `IInlineTextObject`를 반환합니다. 렌더러가 받는 `MarkdownRenderContext`는 프레젠터, 테마, 노드의 원본 텍스트, 그리고 자식 내용을 기본 파이프라인에 위임하는 `RenderBlocks`/`RenderInlines`를 제공합니다.

```csharp
var renderers = new MarkdownRenderers()
    .RegisterBlock<MathBlock>((block, context) => new MathView().Source(context.GetSource(block)));

viewer.Renderers = renderers;
```

구성을 마친 인스턴스를 `Renderers`에 대입합니다. 대입 후에 등록한 내용은 반영되지 않습니다.
