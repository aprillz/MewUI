using System.Collections.Immutable;
using System.Composition;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NonObservingThenCodeFix)), Shared]
public sealed class NonObservingThenCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(BindingDiagnostics.NonObservingThenId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        if (root.FindNode(diagnostic.Location.SourceSpan) is not SimpleNameSyntax name)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Use ThenNotifying",
                createChangedDocument: _ => Task.FromResult(Convert(context.Document, root, name)),
                equivalenceKey: BindingDiagnostics.NonObservingThenId),
            diagnostic);
    }

    private static Document Convert(Document document, SyntaxNode root, SimpleNameSyntax name)
    {
        var replacement = SyntaxFactory.IdentifierName("ThenNotifying").WithTriviaFrom(name);
        return document.WithSyntaxRoot(root.ReplaceNode(name, replacement));
    }
}
