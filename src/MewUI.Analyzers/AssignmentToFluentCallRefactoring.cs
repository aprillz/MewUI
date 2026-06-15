using System.Composition;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

// MEW1104: convert a property assignment `receiver.Prop = value;` into the fluent call
// `receiver.Prop(value);` when a fluent setter for Prop exists on the receiver's type.
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

        if (root.FindNode(context.Span).FirstAncestorOrSelf<ExpressionStatementSyntax>() is not ExpressionStatementSyntax statement
            || statement.Expression is not AssignmentExpressionSyntax assignment
            || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            || assignment.Left is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        var receiverType = model?.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (model is null || receiverType is null)
        {
            return;
        }

        var setter = FluentMethodResolver.ResolveSetter(
            model, receiverType, memberAccess.SpanStart, memberAccess.Name.Identifier.ValueText, assignment.Right);
        if (setter is null)
        {
            return;
        }

        context.RegisterRefactoring(CodeAction.Create(
            "Convert to fluent call",
            cancellationToken => ConvertAsync(context.Document, statement, memberAccess.Expression, setter.Name, assignment.Right, cancellationToken),
            equivalenceKey: "MewUI.AssignmentToFluentCall"));
    }

    private static async Task<Document> ConvertAsync(
        Document document, ExpressionStatementSyntax statement, ExpressionSyntax receiver, string method,
        ExpressionSyntax value, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var call = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver.WithTrailingTrivia(),
                SyntaxFactory.IdentifierName(method)),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(value.WithLeadingTrivia()))));

        return document.WithSyntaxRoot(root.ReplaceNode(statement, statement.WithExpression(call)));
    }
}
