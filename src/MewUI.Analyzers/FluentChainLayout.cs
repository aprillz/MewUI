using System.Collections.Generic;
using System.Linq;
using System.Text;

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

    public static ExpressionSyntax Format(InvocationExpressionSyntax top, string baseIndent, bool expand, string newline, SemanticModel model)
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
            built = SyntaxFactory.InvocationExpression(memberAccess, FormatArguments(arguments, dotIndent, expand, newline, model));
        }

        return built;
    }

    // An element chain (rooted in `new X()`, a call, or a value) is expanded as its own tree; a chain
    // rooted in a type (e.g. `Color.FromRgb(...)`, a static factory) is a value and stays inline.
    private static bool IsElementChain(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is not InvocationExpressionSyntax || ChainLength(expression) < 1)
        {
            return false;
        }

        ExpressionSyntax root = expression;
        while (root is InvocationExpressionSyntax invocation && invocation.Expression is MemberAccessExpressionSyntax access)
        {
            root = access.Expression;
        }

        if (root is ObjectCreationExpressionSyntax or InvocationExpressionSyntax)
        {
            return true;
        }

        // Identifier/member root: an element if it is a value, not a type. Use semantics when the node
        // belongs to the model's tree (MEW1102); for synthesized chains (MEW1101 / MEW1103 build new
        // nodes) GetSymbolInfo would throw, so fall back to a name heuristic (types are PascalCase).
        if (root.SyntaxTree == model.SyntaxTree)
        {
            var symbol = model.GetSymbolInfo(root).Symbol;
            return symbol is not null and not ITypeSymbol and not INamespaceSymbol;
        }

        return root is IdentifierNameSyntax identifier
            && (identifier.Identifier.ValueText.Length == 0 || !char.IsUpper(identifier.Identifier.ValueText[0]));
    }

    // Break the argument list onto its own lines when it has element children, expanding each as a
    // tree (separated by a blank line). Value arguments and type-rooted calls stay inline.
    private static ArgumentListSyntax FormatArguments(ArgumentListSyntax arguments, string callIndent, bool expand, string newline, SemanticModel model)
    {
        var list = arguments.Arguments;
        var breakList = expand && list.Any(argument => IsElementChain(argument.Expression, model));

        if (!breakList)
        {
            var inline = list.Select(argument => SyntaxFactory.Argument(
                argument.NameColon, argument.RefKindKeyword, InlineValue(argument.Expression, callIndent, newline, model)));
            var spacedCommas = Enumerable.Repeat(
                SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space),
                System.Math.Max(0, list.Count - 1));
            return SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(inline, spacedCommas));
        }

        var argIndent = callIndent + Unit;
        var firstLeading = Break(argIndent, newline);
        // Siblings are separated by a blank line.
        var siblingLeading = SyntaxFactory.TriviaList(
            SyntaxFactory.EndOfLine(newline), SyntaxFactory.EndOfLine(newline), SyntaxFactory.Whitespace(argIndent));

        var formatted = list.Select((argument, index) =>
        {
            var value = IsElementChain(argument.Expression, model)
                ? Format((InvocationExpressionSyntax)argument.Expression, argIndent, expand: true, newline, model)
                : InlineValue(argument.Expression, argIndent, newline, model);
            return SyntaxFactory.Argument(argument.NameColon, argument.RefKindKeyword, value)
                .WithLeadingTrivia(index == 0 ? firstLeading : siblingLeading);
        });

        var commas = Enumerable.Repeat(SyntaxFactory.Token(SyntaxKind.CommaToken), System.Math.Max(0, list.Count - 1));
        return SyntaxFactory.ArgumentList(
            SyntaxFactory.Token(SyntaxKind.OpenParenToken),
            SyntaxFactory.SeparatedList(formatted, commas),
            SyntaxFactory.Token(SyntaxKind.CloseParenToken).WithLeadingTrivia(Break(callIndent, newline)));
    }

    // An inline argument: collapse a nested chain back to one line, otherwise just strip outer trivia.
    // A multi-line value (e.g. a lambda block) is re-indented so its shallowest line sits at the call's
    // indent, shifting the whole body to match its new position in the chain.
    private static ExpressionSyntax InlineValue(ExpressionSyntax expression, string targetIndent, string newline, SemanticModel model)
    {
        if (expression is InvocationExpressionSyntax chain && ChainLength(expression) >= 1)
        {
            return Format(chain, string.Empty, expand: false, newline, model);
        }

        return Reindent(expression.WithoutTrivia(), targetIndent, newline);
    }

    private static ExpressionSyntax Reindent(ExpressionSyntax expression, string targetIndent, string newline)
    {
        var lines = expression.ToFullString().Replace("\r\n", "\n").Split('\n');
        if (lines.Length < 2)
        {
            return expression;
        }

        var minIndent = int.MaxValue;
        for (var index = 1; index < lines.Length; index++)
        {
            if (lines[index].Trim().Length == 0)
            {
                continue;
            }

            minIndent = System.Math.Min(minIndent, lines[index].Length - lines[index].TrimStart().Length);
        }

        var delta = minIndent == int.MaxValue ? 0 : targetIndent.Length - minIndent;
        if (delta == 0)
        {
            return expression;
        }

        var rebuilt = new StringBuilder(lines[0]);
        for (var index = 1; index < lines.Length; index++)
        {
            rebuilt.Append(newline);
            if (lines[index].Trim().Length == 0)
            {
                continue;
            }

            var indent = lines[index].Length - lines[index].TrimStart().Length;
            rebuilt.Append(' ', System.Math.Max(0, indent + delta)).Append(lines[index].TrimStart());
        }

        return SyntaxFactory.ParseExpression(rebuilt.ToString());
    }

    private static SyntaxTriviaList Break(string indent, string newline)
        => SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine(newline), SyntaxFactory.Whitespace(indent));
}
