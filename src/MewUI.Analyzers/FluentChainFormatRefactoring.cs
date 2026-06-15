using System.Composition;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

// MEW1102: format a fluent method chain. Offered as an explicit refactoring (Expand / Collapse),
// not a format-on-save hook: format-on-save calls the host's built-in formatter, never a code fix,
// so a refactoring is the only portable trigger across VS and VS Code. Purely syntactic. The actual
// layout lives in FluentChainLayout (shared with MEW1101).
[ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(FluentChainFormatRefactoring)), Shared]
public sealed class FluentChainFormatRefactoring : CodeRefactoringProvider
{
    public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        // FindChainTop already guarantees a member-access chain (at least one call), so even a
        // single-call chain (e.g. `new Border().Padding(...)`) is offered.
        var top = FindChainTop(root.FindNode(context.Span));
        if (top is null)
        {
            return;
        }

        // Both are offered regardless of current layout: Format is idempotent, so "Expand" also
        // re-formats an already-expanded chain in place (no need to collapse first).
        context.RegisterRefactoring(CodeAction.Create(
            "Expand fluent chain",
            cancellationToken => FormatAsync(context.Document, top, expand: true, cancellationToken),
            equivalenceKey: "MewUI.ExpandFluentChain"));

        context.RegisterRefactoring(CodeAction.Create(
            "Collapse fluent chain to one line",
            cancellationToken => FormatAsync(context.Document, top, expand: false, cancellationToken),
            equivalenceKey: "MewUI.CollapseFluentChain"));
    }

    // The outermost invocation of the chain at the caret, or null if it is not a member-access chain.
    private static InvocationExpressionSyntax? FindChainTop(SyntaxNode node)
    {
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return null;
        }

        while (invocation.Parent is MemberAccessExpressionSyntax parentAccess
               && parentAccess.Parent is InvocationExpressionSyntax parentInvocation)
        {
            invocation = parentInvocation;
        }

        return invocation.Expression is MemberAccessExpressionSyntax ? invocation : null;
    }

    private static async Task<Document> FormatAsync(Document document, InvocationExpressionSyntax top, bool expand, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return document;
        }

        var newline = text.ToString().Contains("\r\n") ? "\r\n" : "\n";
        var lineText = text.Lines.GetLineFromPosition(top.SpanStart).ToString();
        var baseIndent = lineText.Substring(0, lineText.Length - lineText.TrimStart().Length);

        var formatted = FluentChainLayout.Format(top, baseIndent, expand, newline, model)
            .WithLeadingTrivia(top.GetLeadingTrivia())
            .WithTrailingTrivia(top.GetTrailingTrivia());

        return document.WithSyntaxRoot(root.ReplaceNode(top, formatted));
    }
}
