using System.Composition;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

// MEW1103: merge the statements that configure a variable into one fluent chain, with the caret on the
// anchor statement. The same merge is offered document-wide by the MEW1105 diagnostic; both call
// FluentChainMerge, which decides which follow-up statements qualify.
[ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(MergeChainStatementsRefactoring)), Shared]
public sealed class MergeChainStatementsRefactoring : CodeRefactoringProvider
{
    public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var anchor = root.FindNode(context.Span).FirstAncestorOrSelf<StatementSyntax>();
        if (anchor is null)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return;
        }

        var followUps = FluentChainMerge.TryCollect(anchor, model, context.CancellationToken);
        if (followUps is null)
        {
            return;
        }

        context.RegisterRefactoring(CodeAction.Create(
            "Merge into fluent chain",
            cancellationToken => FluentChainEdits.MergeAsync(context.Document, anchor, followUps, cancellationToken),
            equivalenceKey: "MewUI.MergeChainStatements"));
    }
}
