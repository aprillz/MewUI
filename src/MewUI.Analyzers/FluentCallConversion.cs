using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

// Rewrites one configuration statement as the fluent call it is equivalent to. Shared by the MEW1104
// refactoring (caret on the statement) and the MEW1106 diagnostic plus its fix.
internal static class FluentCallConversion
{
    /// <summary>The conversion for this statement, or null when it is not a configuration statement.</summary>
    public static ChainStatement? TryDescribe(ExpressionStatementSyntax statement, SemanticModel model, CancellationToken cancellationToken)
    {
        // A statement that is already a chain has nothing to convert.
        if (ChainStatementDescriber.Describe(statement, model, cancellationToken) is not ChainStatement described
            || described.Kind == ChainStatementKind.FluentCall)
        {
            return null;
        }

        return described;
    }

    public static ExpressionStatementSyntax BuildReplacement(ExpressionStatementSyntax statement, ChainStatement described)
    {
        ExpressionSyntax call = described.Receiver.WithoutTrivia();
        foreach (var (name, arguments) in described.Calls)
        {
            call = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression, call, name.WithoutTrivia()),
                arguments);
        }

        // The statement's indentation sits on the first token of its expression, and the receiver is not
        // always that token (an attached setter names the owner type first), so re-attach it here.
        return statement.WithExpression(call.WithLeadingTrivia(statement.Expression.GetLeadingTrivia()));
    }
}
