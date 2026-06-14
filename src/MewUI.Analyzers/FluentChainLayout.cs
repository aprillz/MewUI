using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

// Shared chain layout used by MEW1102 (expand / collapse refactoring) and MEW1101 (which expands the
// chain it produces). Rebuilds the chain from its structure, ignoring existing trivia, so it is
// idempotent; recurses into arguments that are themselves chains to produce a markup tree.
internal static class FluentChainLayout
{
    public const string Unit = "    ";
    public const int MinLinks = 2;

    // Number of chained `.Method(...)` calls in the expression (0 if it is not a member-access chain).
    public static int ChainLength(ExpressionSyntax expression)
    {
        var count = 0;
        var current = expression;
        while (current is InvocationExpressionSyntax invocation
               && invocation.Expression is MemberAccessExpressionSyntax access)
        {
            count++;
            current = access.Expression;
        }

        return count;
    }

    public static ExpressionSyntax Format(InvocationExpressionSyntax top, string baseIndent, bool expand, string newline)
    {
        var calls = new List<(SimpleNameSyntax Name, ArgumentListSyntax Arguments)>();
        ExpressionSyntax current = top;
        while (current is InvocationExpressionSyntax invocation
               && invocation.Expression is MemberAccessExpressionSyntax access)
        {
            calls.Add((access.Name, invocation.ArgumentList));
            current = access.Expression;
        }

        calls.Reverse();

        var dotIndent = baseIndent + Unit;
        var dotLeading = expand ? Break(dotIndent, newline) : default;

        ExpressionSyntax built = current.WithoutTrivia();
        foreach (var (name, arguments) in calls)
        {
            var memberAccess = SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                built,
                SyntaxFactory.Token(SyntaxKind.DotToken).WithLeadingTrivia(dotLeading),
                name.WithoutTrivia());
            built = SyntaxFactory.InvocationExpression(memberAccess, FormatArguments(arguments, dotIndent, expand, newline));
        }

        return built;
    }

    // Break the argument list onto its own lines only when an argument is itself an expandable chain
    // (the nested markup tree). Simple argument lists stay inline.
    private static ArgumentListSyntax FormatArguments(ArgumentListSyntax arguments, string callIndent, bool expand, string newline)
    {
        var list = arguments.Arguments;
        var breakList = expand && list.Any(argument => ChainLength(argument.Expression) >= MinLinks);

        if (!breakList)
        {
            var inline = list.Select(argument => SyntaxFactory.Argument(
                argument.NameColon, argument.RefKindKeyword, InlineValue(argument.Expression, newline)));
            var spacedCommas = Enumerable.Repeat(
                SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space),
                System.Math.Max(0, list.Count - 1));
            return SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(inline, spacedCommas));
        }

        var argIndent = callIndent + Unit;
        var formatted = list.Select(argument =>
        {
            var value = ChainLength(argument.Expression) >= MinLinks
                ? Format((InvocationExpressionSyntax)argument.Expression, argIndent, expand: true, newline)
                : argument.Expression.WithoutTrivia();
            return SyntaxFactory.Argument(argument.NameColon, argument.RefKindKeyword, value)
                .WithLeadingTrivia(Break(argIndent, newline));
        });

        var commas = Enumerable.Repeat(SyntaxFactory.Token(SyntaxKind.CommaToken), System.Math.Max(0, list.Count - 1));
        return SyntaxFactory.ArgumentList(
            SyntaxFactory.Token(SyntaxKind.OpenParenToken),
            SyntaxFactory.SeparatedList(formatted, commas),
            SyntaxFactory.Token(SyntaxKind.CloseParenToken).WithLeadingTrivia(Break(callIndent, newline)));
    }

    // An inline argument: collapse a nested chain back to one line, otherwise just strip outer trivia.
    private static ExpressionSyntax InlineValue(ExpressionSyntax expression, string newline)
        => expression is InvocationExpressionSyntax chain && ChainLength(expression) >= 1
            ? Format(chain, string.Empty, expand: false, newline)
            : expression.WithoutTrivia();

    private static SyntaxTriviaList Break(string indent, string newline)
        => SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(newline), SyntaxFactory.Whitespace(indent));
}
