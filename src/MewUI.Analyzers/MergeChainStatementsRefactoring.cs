using System.Collections.Generic;
using System.Composition;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Aprillz.MewUI.Analyzers;

// MEW1103: merge a `x = <expr>;` (or `var x = <expr>;`) statement with the consecutive statements
// that follow it into a single fluent chain, then expand it. Each follow-up may be either:
//   - a fluent call chain  `x.A(..).B(..);`            -> appends `.A(..).B(..)`
//   - an event subscription `x.Click += handler;`      -> appends `.OnClick(handler)`
//
//   _btn = new Button().Tooltip("Min");       _btn = new Button()
//   _btn.Click += () => Minimize();      ->        .Tooltip("Min")
//                                                  .OnClick(() => Minimize());
//
// Each appended call must return x's own type, so chaining and assigning back to x stays valid.
// Offered as a refactoring (caret on the anchor statement).
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
        if (anchor is null || anchor.Parent is not BlockSyntax)
        {
            return;
        }

        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return;
        }

        var (target, _) = GetAnchorTarget(anchor, model, context.CancellationToken);
        if (target is null)
        {
            return;
        }

        var followUps = CollectFollowUps(anchor, target, TargetType(target), model, context.CancellationToken);
        if (followUps.Count == 0)
        {
            return;
        }

        context.RegisterRefactoring(CodeAction.Create(
            "Merge into fluent chain",
            cancellationToken => MergeAsync(context.Document, anchor, followUps, cancellationToken),
            equivalenceKey: "MewUI.MergeChainStatements"));
    }

    // The variable/field assigned by the anchor, plus its initializer value. Both null if the
    // statement is not a single local declaration or a simple `identifier = value` assignment.
    private static (ISymbol? Target, ExpressionSyntax? Value) GetAnchorTarget(StatementSyntax anchor, SemanticModel model, CancellationToken cancellationToken)
    {
        if (anchor is LocalDeclarationStatementSyntax declaration
            && declaration.Declaration.Variables.Count == 1)
        {
            var declarator = declaration.Declaration.Variables[0];
            if (declarator.Initializer is null)
            {
                return (null, null);
            }

            return (model.GetDeclaredSymbol(declarator, cancellationToken), declarator.Initializer.Value);
        }

        if (anchor is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment }
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && assignment.Left is IdentifierNameSyntax)
        {
            return (model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol, assignment.Right);
        }

        return (null, null);
    }

    private static List<(ExpressionStatementSyntax Statement, List<(SimpleNameSyntax Name, ArgumentListSyntax Arguments)> Calls)> CollectFollowUps(
        StatementSyntax anchor, ISymbol target, ITypeSymbol? targetType, SemanticModel model, CancellationToken cancellationToken)
    {
        var result = new List<(ExpressionStatementSyntax, List<(SimpleNameSyntax, ArgumentListSyntax)>)>();
        var statements = ((BlockSyntax)anchor.Parent!).Statements;

        for (var index = statements.IndexOf(anchor) + 1; index < statements.Count; index++)
        {
            if (statements[index] is not ExpressionStatementSyntax statement)
            {
                break;
            }

            var calls = DescribeAppend(statement.Expression, target, targetType, model, cancellationToken);
            if (calls is null)
            {
                break;
            }

            result.Add((statement, calls));
        }

        return result;
    }

    // Maps a follow-up statement to the chain calls it appends to `x`, or null if it does not apply.
    private static List<(SimpleNameSyntax Name, ArgumentListSyntax Arguments)>? DescribeAppend(
        ExpressionSyntax expression, ISymbol target, ITypeSymbol? targetType, SemanticModel model, CancellationToken cancellationToken)
    {
        // (a) fluent call chain: x.A(..).B(..)
        if (expression is InvocationExpressionSyntax invocation
            && ChainRootIdentifier(invocation) is IdentifierNameSyntax invocationRoot
            && SymbolEquals(model.GetSymbolInfo(invocationRoot, cancellationToken).Symbol, target)
            && SymbolEquals(model.GetTypeInfo(invocation, cancellationToken).Type, targetType))
        {
            return DecomposeChain(invocation);
        }

        // (b) event subscription: x.Event += handler  ->  .OnEvent(handler)
        if (expression is AssignmentExpressionSyntax assignment
            && assignment.IsKind(SyntaxKind.AddAssignmentExpression)
            && assignment.Left is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax eventRoot } memberAccess
            && targetType is not null
            && SymbolEquals(model.GetSymbolInfo(eventRoot, cancellationToken).Symbol, target))
        {
            var setter = FluentMethodResolver.ResolveSetter(
                model, targetType, memberAccess.SpanStart, memberAccess.Name.Identifier.ValueText, assignment.Right);
            if (setter is not null && SymbolEquals(setter.ReturnType, targetType))
            {
                return new List<(SimpleNameSyntax, ArgumentListSyntax)>
                {
                    ((SimpleNameSyntax)SyntaxFactory.IdentifierName(setter.Name),
                     SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                         SyntaxFactory.Argument(assignment.Right.WithoutTrivia())))),
                };
            }
        }

        return null;
    }

    private static List<(SimpleNameSyntax Name, ArgumentListSyntax Arguments)> DecomposeChain(InvocationExpressionSyntax invocation)
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

    private static async Task<Document> MergeAsync(
        Document document, StatementSyntax anchor,
        List<(ExpressionStatementSyntax Statement, List<(SimpleNameSyntax Name, ArgumentListSyntax Arguments)> Calls)> followUps,
        CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return document;
        }

        var (_, value) = GetAnchorTarget(anchor, model, cancellationToken);
        if (value is null)
        {
            return document;
        }

        ExpressionSyntax accumulator = value.WithoutTrivia();
        foreach (var (_, calls) in followUps)
        {
            foreach (var (name, arguments) in calls)
            {
                accumulator = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression, accumulator, name.WithoutTrivia()),
                    arguments);
            }
        }

        var newline = text.ToString().Contains("\r\n") ? "\r\n" : "\n";
        var lineText = text.Lines.GetLineFromPosition(anchor.SpanStart).ToString();
        var baseIndent = lineText.Substring(0, lineText.Length - lineText.TrimStart().Length);

        if (accumulator is InvocationExpressionSyntax merged)
        {
            accumulator = FluentChainLayout.Format(
                merged, baseIndent, FluentChainLayout.ChainLength(merged) >= FluentChainLayout.MinLinks, newline);
        }

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.ReplaceNode(anchor, BuildMergedStatement(anchor, accumulator));
        foreach (var (statement, _) in followUps)
        {
            editor.RemoveNode(statement, SyntaxRemoveOptions.KeepNoTrivia);
        }

        return editor.GetChangedDocument();
    }

    private static StatementSyntax BuildMergedStatement(StatementSyntax anchor, ExpressionSyntax value)
    {
        if (anchor is LocalDeclarationStatementSyntax declaration)
        {
            var declarator = declaration.Declaration.Variables[0];
            var newDeclarator = declarator.WithInitializer(declarator.Initializer!.WithValue(value));
            return declaration.WithDeclaration(
                declaration.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(newDeclarator)));
        }

        var statement = (ExpressionStatementSyntax)anchor;
        var assignment = (AssignmentExpressionSyntax)statement.Expression;
        return statement.WithExpression(assignment.WithRight(value));
    }

    // The innermost receiver of an invocation chain, if it is a plain identifier (`x` in `x.A().B()`).
    private static IdentifierNameSyntax? ChainRootIdentifier(InvocationExpressionSyntax invocation)
    {
        ExpressionSyntax current = invocation;
        while (current is InvocationExpressionSyntax inner && inner.Expression is MemberAccessExpressionSyntax access)
        {
            current = access.Expression;
        }

        return current as IdentifierNameSyntax;
    }

    private static ITypeSymbol? TargetType(ISymbol symbol)
        => symbol switch
        {
            ILocalSymbol local => local.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null,
        };

    private static bool SymbolEquals(ISymbol? left, ISymbol? right)
        => left is not null && SymbolEqualityComparer.Default.Equals(left, right);
}
