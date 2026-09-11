using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

internal enum ChainStatementKind
{
    // x.A(..).B(..);
    FluentCall,

    // x.Event += handler;
    EventSubscription,

    // x.Prop = value;
    PropertyAssignment,

    // Owner.SetProp(x, value);
    AttachedSetter,

    // x.Add(a); where an extension declares that it replaces Add.
    ReplacedMember,
}

// A configuration statement expressed as calls on its receiver.
internal readonly struct ChainStatement
{
    public ChainStatement(
        ChainStatementKind kind,
        ExpressionSyntax receiver,
        List<(SimpleNameSyntax Name, ArgumentListSyntax Arguments)> calls)
    {
        Kind = kind;
        Receiver = receiver;
        Calls = calls;
    }

    public ChainStatementKind Kind { get; }

    // The expression the calls apply to, as written in the statement.
    public ExpressionSyntax Receiver { get; }

    public List<(SimpleNameSyntax Name, ArgumentListSyntax Arguments)> Calls { get; }
}

// Decides what a statement contributes to a fluent chain on its receiver. MEW1103 appends the calls to
// the anchor's chain and MEW1104 rewrites the single statement, so routing both through here keeps the
// two from judging the same statement differently.
internal static class ChainStatementDescriber
{
    public static ChainStatement? Describe(ExpressionStatementSyntax statement, SemanticModel model, CancellationToken cancellationToken)
    {
        switch (statement.Expression)
        {
            case InvocationExpressionSyntax invocation:
                return DescribeAttachedSetter(invocation, model, cancellationToken)
                       ?? DescribeReplacedMember(invocation, model, cancellationToken)
                       ?? DescribeFluentCall(invocation, model, cancellationToken);

            case AssignmentExpressionSyntax assignment when assignment.Left is MemberAccessExpressionSyntax access:
                if (assignment.IsKind(SyntaxKind.AddAssignmentExpression))
                {
                    return DescribeSetter(ChainStatementKind.EventSubscription, access, assignment.Right, model, cancellationToken);
                }

                if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                {
                    return DescribeSetter(ChainStatementKind.PropertyAssignment, access, assignment.Right, model, cancellationToken);
                }

                return null;

            default:
                return null;
        }
    }

    // A chain already written on the receiver. It must return the receiver's own type, so re-rooting it
    // on another chain (or assigning it back to the receiver) stays valid.
    private static ChainStatement? DescribeFluentCall(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        var root = ChainRoot(invocation);
        if (root is null)
        {
            return null;
        }

        var rootType = model.GetTypeInfo(root, cancellationToken).Type;
        if (rootType is null || !SymbolEquals(model.GetTypeInfo(invocation, cancellationToken).Type, rootType))
        {
            return null;
        }

        return new ChainStatement(ChainStatementKind.FluentCall, root, Decompose(invocation));
    }

    // `x.Prop = value` / `x.Event += handler` -> `.Prop(value)` / `.OnEvent(handler)`.
    private static ChainStatement? DescribeSetter(
        ChainStatementKind kind, MemberAccessExpressionSyntax access, ExpressionSyntax value,
        SemanticModel model, CancellationToken cancellationToken)
    {
        if (!IsValueReceiver(access.Expression, model, cancellationToken))
        {
            return null;
        }

        var receiverType = model.GetTypeInfo(access.Expression, cancellationToken).Type;
        if (receiverType is null)
        {
            return null;
        }

        var setter = FluentMethodResolver.ResolveSetter(
            model, receiverType, access.SpanStart, access.Name.Identifier.ValueText, value);
        if (setter is null || !SymbolEquals(setter.ReturnType, receiverType))
        {
            return null;
        }

        return new ChainStatement(kind, access.Expression, SingleCall(setter.Name, value));
    }

    // `Owner.SetProp(x, value)` -> `.Prop(value)` on x.
    private static ChainStatement? DescribeAttachedSetter(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 2
            || model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol { IsStatic: true } method
            || method.Parameters.Length != 2)
        {
            return null;
        }

        foreach (var argument in arguments)
        {
            if (argument.NameColon is not null || !argument.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                return null;
            }
        }

        var receiver = arguments[0].Expression;
        var value = arguments[1].Expression;
        if (!IsValueReceiver(receiver, model, cancellationToken))
        {
            return null;
        }

        var receiverType = model.GetTypeInfo(receiver, cancellationToken).Type;
        if (receiverType is null)
        {
            return null;
        }

        var setter = FluentMethodResolver.ResolveAttachedSetter(
            model, receiverType, invocation.SpanStart, method.Name, value);
        if (setter is null || !SymbolEquals(setter.ReturnType, receiverType))
        {
            return null;
        }

        return new ChainStatement(ChainStatementKind.AttachedSetter, receiver, SingleCall(setter.Name, value));
    }

    // `x.Add(a)` -> `.Children(a)`, when an extension in scope declares that it replaces `Add`.
    private static ChainStatement? DescribeReplacedMember(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax access
            || model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol { IsStatic: false } method
            || !IsValueReceiver(access.Expression, model, cancellationToken))
        {
            return null;
        }

        var receiverType = model.GetTypeInfo(access.Expression, cancellationToken).Type;
        if (receiverType is null)
        {
            return null;
        }

        var replacement = FluentMethodResolver.ResolveMemberReplacement(
            model, receiverType, access.SpanStart, method.Name, invocation.ArgumentList.Arguments);
        if (replacement is null || !SymbolEquals(replacement.ReturnType, receiverType))
        {
            return null;
        }

        var arguments = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
            invocation.ArgumentList.Arguments.Select(argument => argument.WithoutTrivia())));
        var calls = new List<(SimpleNameSyntax, ArgumentListSyntax)>
        {
            (SyntaxFactory.IdentifierName(replacement.Name), arguments),
        };

        return new ChainStatement(ChainStatementKind.ReplacedMember, access.Expression, calls);
    }

    // The innermost receiver of a member-access invocation chain (`x` in `x.A().B()`), or null when the
    // expression is not such a chain.
    private static ExpressionSyntax? ChainRoot(InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax current = invocation;
        var isChain = false;
        while (current is InvocationExpressionSyntax inner && inner.Expression is MemberAccessExpressionSyntax access)
        {
            isChain = true;
            current = access.Expression;
        }

        return isChain ? current : null;
    }

    private static List<(SimpleNameSyntax Name, ArgumentListSyntax Arguments)> Decompose(InvocationExpressionSyntax invocation)
    {
        var calls = new List<(SimpleNameSyntax, ArgumentListSyntax)>();
        ExpressionSyntax current = invocation;
        while (current is InvocationExpressionSyntax inner && inner.Expression is MemberAccessExpressionSyntax access)
        {
            calls.Add((access.Name, inner.ArgumentList));
            current = access.Expression;
        }

        calls.Reverse();
        return calls;
    }

    private static List<(SimpleNameSyntax Name, ArgumentListSyntax Arguments)> SingleCall(string method, ExpressionSyntax value)
        => new List<(SimpleNameSyntax, ArgumentListSyntax)>
        {
            (SyntaxFactory.IdentifierName(method),
             SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                 SyntaxFactory.Argument(value.WithoutTrivia())))),
        };

    // A static member access such as `Grid.Column` names a type, not an instance to configure.
    private static bool IsValueReceiver(ExpressionSyntax receiver, SemanticModel model, CancellationToken cancellationToken)
        => model.GetSymbolInfo(receiver, cancellationToken).Symbol is not (ITypeSymbol or INamespaceSymbol);

    private static bool SymbolEquals(ISymbol? left, ISymbol? right)
        => left is not null && SymbolEqualityComparer.Default.Equals(left, right);
}
