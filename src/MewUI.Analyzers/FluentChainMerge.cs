using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Aprillz.MewUI.Analyzers;

// Folds a `x = <expr>;` (or `var x = <expr>;`) statement and the consecutive statements that configure
// x into a single fluent chain. Shared by the MEW1103 refactoring (caret on the anchor) and the MEW1105
// diagnostic plus its fix (whole document at once), so both produce the same chain.
//
//   _btn = new Button().Tooltip("Min");       _btn = new Button()
//   _btn.Click += () => Minimize();      ->            .Tooltip("Min")
//                                                      .OnClick(() => Minimize());
internal static class FluentChainMerge
{
    /// <summary>The statements the anchor can absorb, or null when the anchor cannot start a chain.</summary>
    public static List<(ExpressionStatementSyntax Statement, ChainStatement Described)>? TryCollect(
        StatementSyntax anchor, SemanticModel model, CancellationToken cancellationToken)
    {
        if (!IsStatementContainer(anchor.Parent))
        {
            return null;
        }

        var (target, value) = GetAnchorTarget(anchor, model, cancellationToken);
        if (target is null || value is null || !IsChainableRoot(value))
        {
            return null;
        }

        var followUps = CollectFollowUps(anchor, target, model, cancellationToken);
        return followUps.Count == 0 ? null : followUps;
    }

    /// <summary>Where the anchor's diagnostic is reported: the name it assigns.</summary>
    public static Location AnchorLocation(StatementSyntax anchor)
    {
        if (anchor is LocalDeclarationStatementSyntax declaration && declaration.Declaration.Variables.Count == 1)
        {
            return declaration.Declaration.Variables[0].Identifier.GetLocation();
        }

        if (anchor is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment })
        {
            return assignment.Left.GetLocation();
        }

        return anchor.GetLocation();
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

    private static List<(ExpressionStatementSyntax Statement, ChainStatement Described)> CollectFollowUps(
        StatementSyntax anchor, ISymbol target, SemanticModel model, CancellationToken cancellationToken)
    {
        var result = new List<(ExpressionStatementSyntax, ChainStatement)>();

        foreach (var candidate in FollowingStatements(anchor))
        {
            if (candidate is not ExpressionStatementSyntax statement)
            {
                break;
            }

            if (DescribeAppend(statement, target, model, cancellationToken) is not ChainStatement described)
            {
                break;
            }

            result.Add((statement, described));
        }

        return result;
    }

    // A container whose statements are ordered siblings: a block, or the compilation unit that holds
    // top-level statements (each wrapped in a GlobalStatementSyntax).
    public static bool IsStatementContainer(SyntaxNode? parent)
        => parent is BlockSyntax
           || parent is GlobalStatementSyntax { Parent: CompilationUnitSyntax };

    // The statements after the anchor in source order. Collection stops at the first compilation unit
    // member that is not a top-level statement, so a trailing type declaration is never folded in.
    private static List<StatementSyntax> FollowingStatements(StatementSyntax anchor)
    {
        var result = new List<StatementSyntax>();

        if (anchor.Parent is BlockSyntax block)
        {
            for (var index = block.Statements.IndexOf(anchor) + 1; index < block.Statements.Count; index++)
            {
                result.Add(block.Statements[index]);
            }

            return result;
        }

        if (anchor.Parent is GlobalStatementSyntax global && global.Parent is CompilationUnitSyntax unit)
        {
            for (var index = unit.Members.IndexOf(global) + 1; index < unit.Members.Count; index++)
            {
                if (unit.Members[index] is not GlobalStatementSyntax next)
                {
                    break;
                }

                result.Add(next.Statement);
            }
        }

        return result;
    }

    /// <summary>The node to delete when a follow-up statement is folded in: a top-level statement goes with its wrapper.</summary>
    public static SyntaxNode RemovalNode(StatementSyntax statement)
        => statement.Parent is GlobalStatementSyntax global ? global : statement;

    // Calls are appended to the anchor value without parentheses, so only a value that already binds
    // tighter than member access can carry the chain (`new T()`, `Factory()`, `x`, `a.b`).
    private static bool IsChainableRoot(ExpressionSyntax value)
        => value is ObjectCreationExpressionSyntax
            or InvocationExpressionSyntax
            or IdentifierNameSyntax
            or MemberAccessExpressionSyntax;

    // The chain calls a follow-up statement appends to `x`, or null if the statement does not configure
    // `x` at all. Which statement shapes qualify is decided by ChainStatementDescriber, shared with MEW1104.
    private static ChainStatement? DescribeAppend(
        ExpressionStatementSyntax statement, ISymbol target, SemanticModel model, CancellationToken cancellationToken)
    {
        if (ChainStatementDescriber.Describe(statement, model, cancellationToken) is not ChainStatement described
            || described.Receiver is not IdentifierNameSyntax receiver
            || !SymbolEquals(model.GetSymbolInfo(receiver, cancellationToken).Symbol, target))
        {
            return null;
        }

        return described;
    }

    /// <summary>The single statement the anchor and its follow-ups become, or null if the anchor no longer qualifies.</summary>
    public static StatementSyntax? BuildMergedStatement(
        StatementSyntax anchor,
        List<(ExpressionStatementSyntax Statement, ChainStatement Described)> followUps,
        SemanticModel model,
        SourceText text,
        CancellationToken cancellationToken)
    {
        var (target, value) = GetAnchorTarget(anchor, model, cancellationToken);
        if (value is null)
        {
            return null;
        }

        // For a local declaration of a reference type, capture the reference inline with `.Ref(out var x)`
        // (the MewUI idiom) instead of keeping a `var x = ...;` statement, when a Ref extension exists.
        var refMethod = TryResolveRefForLocal(anchor, target, model, out var variableName);

        ExpressionSyntax accumulator = value.WithoutTrivia();
        if (refMethod is not null)
        {
            accumulator = InsertRefAtRoot(accumulator, refMethod, variableName!);
        }

        // Consecutive statements replaced by the same collection setter (`x.Add(a); x.AddRange(b, c);`)
        // become one call, since the setter takes the whole list at once.
        var previousKind = default(ChainStatementKind?);
        var previousName = string.Empty;

        foreach (var (_, described) in followUps)
        {
            foreach (var (name, arguments) in described.Calls)
            {
                if (described.Kind == ChainStatementKind.ReplacedMember
                    && previousKind == ChainStatementKind.ReplacedMember
                    && previousName == name.Identifier.ValueText
                    && accumulator is InvocationExpressionSyntax previous)
                {
                    accumulator = previous.WithArgumentList(
                        previous.ArgumentList.AddArguments(arguments.Arguments.ToArray()));
                }
                else
                {
                    accumulator = AppendCall(accumulator, name, arguments);
                }

                previousKind = described.Kind;
                previousName = name.Identifier.ValueText;
            }
        }

        var newline = text.ToString().Contains("\r\n") ? "\r\n" : "\n";
        var lineText = text.Lines.GetLineFromPosition(anchor.SpanStart).ToString();
        var baseIndent = lineText.Substring(0, lineText.Length - lineText.TrimStart().Length);

        if (accumulator is InvocationExpressionSyntax merged)
        {
            accumulator = FluentChainLayout.Format(
                merged, baseIndent, FluentChainLayout.ChainLength(merged) >= FluentChainLayout.MinLinks, newline, model);
        }

        return refMethod is not null
            ? SyntaxFactory.ExpressionStatement(accumulator)
                .WithLeadingTrivia(anchor.GetLeadingTrivia())
                .WithTrailingTrivia(anchor.GetTrailingTrivia())
            : WithChainValue(anchor, accumulator);
    }

    // `.Ref(out var x)` names the instance, so it goes right after the expression that creates it rather
    // than at the end of whatever configuration the anchor already had.
    private static ExpressionSyntax InsertRefAtRoot(ExpressionSyntax value, string refMethod, string variableName)
    {
        var calls = new List<(SimpleNameSyntax Name, ArgumentListSyntax Arguments)>();
        ExpressionSyntax root = value;
        while (root is InvocationExpressionSyntax invocation && invocation.Expression is MemberAccessExpressionSyntax access)
        {
            calls.Add((access.Name, invocation.ArgumentList));
            root = access.Expression;
        }

        calls.Reverse();

        ExpressionSyntax rebuilt = AppendCall(root, SyntaxFactory.IdentifierName(refMethod), RefArgumentList(variableName));
        foreach (var (name, arguments) in calls)
        {
            rebuilt = AppendCall(rebuilt, name, arguments);
        }

        return rebuilt;
    }

    private static InvocationExpressionSyntax AppendCall(ExpressionSyntax receiver, SimpleNameSyntax name, ArgumentListSyntax arguments)
        => SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, name.WithoutTrivia()),
            arguments);

    // `Ref` extension name to use for a `var x = ...` local of a reference type, or null to keep `var x = ...`.
    private static string? TryResolveRefForLocal(StatementSyntax anchor, ISymbol? target, SemanticModel model, out string? variableName)
    {
        variableName = null;
        if (anchor is not LocalDeclarationStatementSyntax declaration
            || declaration.Declaration.Variables.Count != 1
            || target is null)
        {
            return null;
        }

        var targetType = TargetType(target);
        if (targetType is null || !targetType.IsReferenceType)
        {
            return null;
        }

        var declarator = declaration.Declaration.Variables[0];
        foreach (var symbol in model.LookupSymbols(declarator.SpanStart, targetType, "Ref", includeReducedExtensionMethods: true))
        {
            if (symbol is IMethodSymbol method
                && method.ReducedFrom is not null
                && method.Parameters.Length == 1
                && method.Parameters[0].RefKind == RefKind.Out)
            {
                variableName = declarator.Identifier.Text;
                return method.Name;
            }
        }

        return null;
    }

    private static ArgumentListSyntax RefArgumentList(string variableName)
    {
        var declaration = SyntaxFactory.DeclarationExpression(
            SyntaxFactory.IdentifierName("var").WithTrailingTrivia(SyntaxFactory.Space),
            SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(variableName)));

        var argument = SyntaxFactory.Argument(
            null,
            SyntaxFactory.Token(SyntaxKind.OutKeyword).WithTrailingTrivia(SyntaxFactory.Space),
            declaration);

        return SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(argument));
    }

    private static StatementSyntax WithChainValue(StatementSyntax anchor, ExpressionSyntax value)
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
