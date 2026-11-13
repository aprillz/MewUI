# Aprillz.MewUI.Markdown.Math

[Aprillz.MewUI.Markdown](../MewUI.Markdown/README.ko.md) 컨트롤에 LaTeX 수식을 더하는 확장입니다.

[CSharpMath](https://github.com/verybadcat/CSharpMath)가 수식을 조판하고, 확장이 글리프 외곽선을 MewUI 그래픽 컨텍스트에 벡터 패스로 그립니다. 수식은 문서의 글꼴 크기와 글자색을 따르고, 어떤 DPI에서도 선명하며, 모든 MewUI 백엔드에서 그려집니다. SkiaSharp와 비트맵을 쓰지 않습니다.

## 설치

```
dotnet add package Aprillz.MewUI.Markdown.Math
```

`net8.0`과 `net10.0`을 대상으로 합니다.

## 빠른 시작

```csharp
using Aprillz.MewUI.Markdown;

var viewer = new MarkdownViewer()
    .Options(new MarkdownOptions { ConfigurePipeline = static builder => builder.UseMath() })
    .Renderers(new MarkdownRenderers().UseMath())
    .Markdown("Inline $E = mc^2$ and a display formula:\n\n$$\\int_0^1 x^2\\,dx = \\frac{1}{3}$$");
```

파이프라인의 `UseMath()`는 `$...$`, `$$...$$`, `\(...\)`, `\[...\]`를 수식으로 인식합니다. `UseMath(bracketDelimiters: false)`는 `\(`와 `\[`를 이스케이프된 괄호로 남깁니다. 렌더러의 `UseMath()`는 수식을 그립니다.

## 표시 방식

| 원본 | 표시 |
|---|---|
| `$...$`, `\(...\)` | 인라인, 텍스트 스타일, 글자 베이스라인에 정렬 |
| 줄 안의 `$$...$$`, `\[...\]` | 인라인, 디스플레이 스타일 |
| 줄을 혼자 차지하는 `$$...$$`, `\[...\]` (구분자가 수식과 같은 줄에 있어도 됨) | 가운데 정렬된 블록 |

CSharpMath가 해석하지 못한 수식은 그 자리에 CSharpMath의 오류 문구가 표시됩니다. 안전 한도(10,000자 또는 중괄호 50단계 중첩)를 넘는 수식은 원본 텍스트로 남습니다.

## LaTeX 지원 범위

CSharpMath는 분수, 근호, 합, 적분, 극한, 그리스 문자, `\mathbb`, `\mathcal`, `\mathrm`, `\mathbf`, `\text`, `\left`/`\right`, `\underbrace`, `\not`, 그리고 `aligned`, `cases`, `pmatrix`, `bmatrix`, `vmatrix`, `Vmatrix` 환경을 지원합니다. 확장은 CSharpMath에 없는 명령 몇 가지를 고쳐 씁니다. `\dfrac`과 `\tfrac`은 `\frac`으로, `\boldsymbol`은 `\mathbf`로 바꿉니다. 또한 `\overbrace{...}^{...}`, `\xrightarrow[...]{...}`, `\xleftarrow[...]{...}`를 추가합니다.

블록 수식은 문서 선택에 포함되지 않으며 복사 버튼이 없습니다.
