using System.Collections.Immutable;
using System.Composition;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

// Fixes for MEW1105 / MEW1106. Both delegate to the module the matching refactoring uses, so the
// document-wide Fix All and the single caret action produce the same code.
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ChainStatementCodeFix)), Shared]
public sealed class ChainStatementCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(FluentDiagnostics.MergeStatementsId, FluentDiagnostics.StatementToFluentCallId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        var statement = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<StatementSyntax>();
        if (statement is null)
        {
            return;
        }

        if (diagnostic.Id == FluentDiagnostics.MergeStatementsId)
        {
            var followUps = FluentChainMerge.TryCollect(statement, model, context.CancellationToken);
            if (followUps is null)
            {
                return;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Merge into fluent chain",
                    createChangedDocument: cancellationToken =>
                        FluentChainEdits.MergeAsync(context.Document, statement, followUps, cancellationToken),
                    equivalenceKey: FluentDiagnostics.MergeStatementsId),
                diagnostic);
            return;
        }

        if (statement is not ExpressionStatementSyntax expressionStatement
            || FluentCallConversion.TryDescribe(expressionStatement, model, context.CancellationToken) is not ChainStatement described)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to fluent call",
                createChangedDocument: cancellationToken =>
                    FluentChainEdits.ConvertAsync(context.Document, expressionStatement, described, cancellationToken),
                equivalenceKey: FluentDiagnostics.StatementToFluentCallId),
            diagnostic);
    }
}
