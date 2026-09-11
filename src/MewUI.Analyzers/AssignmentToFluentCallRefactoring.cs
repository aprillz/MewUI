using System.Composition;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

// MEW1104: rewrite a single configuration statement as the fluent call it is equivalent to, with the
// caret on the statement:
//   receiver.Prop = value;          ->  receiver.Prop(value);
//   receiver.Event += handler;      ->  receiver.OnEvent(handler);
//   Owner.SetProp(receiver, value); ->  receiver.Prop(value);
// The same conversion is offered document-wide by the MEW1106 diagnostic; both call FluentCallConversion.
[ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(AssignmentToFluentCallRefactoring)), Shared]
public sealed class AssignmentToFluentCallRefactoring : CodeRefactoringProvider
{
    public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        if (root.FindNode(context.Span).FirstAncestorOrSelf<ExpressionStatementSyntax>() is not ExpressionStatementSyntax statement)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return;
        }

        if (FluentCallConversion.TryDescribe(statement, model, context.CancellationToken) is not ChainStatement described)
        {
            return;
        }

        context.RegisterRefactoring(CodeAction.Create(
            "Convert to fluent call",
            cancellationToken => FluentChainEdits.ConvertAsync(context.Document, statement, described, cancellationToken),
            equivalenceKey: "MewUI.AssignmentToFluentCall"));
    }
}
