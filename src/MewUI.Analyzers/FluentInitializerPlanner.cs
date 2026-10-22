using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

internal enum FluentInvocationKind
{
    Convention,
    OnPrefix,
    AnnotatedParameters,
    AnnotatedConstants,
    AnnotatedCombined,
}

/// <summary>One fluent call that replaces a run of contiguous initializer members.</summary>
internal sealed class FluentInvocationPlan
{
    public FluentInvocationPlan(
        IMethodSymbol method,
        ImmutableArray<ExpressionSyntax> arguments,
        ImmutableArray<int> consumedInitializerIndexes,
        FluentInvocationKind kind)
    {
        Method = method;
        Arguments = arguments;
        ConsumedInitializerIndexes = consumedInitializerIndexes;
        Kind = kind;
    }

    public IMethodSymbol Method { get; }

    public ImmutableArray<ExpressionSyntax> Arguments { get; }

    public ImmutableArray<int> ConsumedInitializerIndexes { get; }

    public FluentInvocationKind Kind { get; }
}

/// <summary>The fluent calls an object initializer converts to, in source order, plus the members that stay in the initializer.</summary>
internal sealed class FluentInitializerPlan
{
    public FluentInitializerPlan(ImmutableArray<FluentInvocationPlan> invocations, ImmutableArray<int> residualIndexes)
    {
        Invocations = invocations;
        ResidualIndexes = residualIndexes;
    }

    public ImmutableArray<FluentInvocationPlan> Invocations { get; }

    public ImmutableArray<int> ResidualIndexes { get; }
}

// Plans the MEW1101 conversion for a whole initializer so the analyzer and the code fix never judge
// the same creation differently. Candidates at a member come from the hint attributes (composite
// setters and constant shorthands), the same-name single-argument setter, and the `On` prefix.
internal static class FluentInitializerPlanner
{
    private const string ON_PREFIX = "On";

    private readonly struct Candidate
    {
        public Candidate(IMethodSymbol method, FluentInvocationKind kind, ImmutableArray<ExpressionSyntax> arguments, int consumed)
        {
            Method = method;
            Kind = kind;
            Arguments = arguments;
            Consumed = consumed;
        }

        public IMethodSymbol Method { get; }

        public FluentInvocationKind Kind { get; }

        public ImmutableArray<ExpressionSyntax> Arguments { get; }

        public int Consumed { get; }
    }

    /// <summary>Plans the conversion, or returns null when the creation has no object initializer or no resolvable type.</summary>
    public static FluentInitializerPlan? Plan(SemanticModel model, ObjectCreationExpressionSyntax creation, CancellationToken cancellationToken)
    {
        var initializer = creation.Initializer;
        if (initializer is null || !initializer.IsKind(SyntaxKind.ObjectInitializerExpression))
        {
            return null;
        }

        var receiver = model.GetTypeInfo(creation, cancellationToken).Type;
        if (receiver is null)
        {
            return null;
        }

        var expressions = initializer.Expressions;
        var assignments = new AssignmentExpressionSyntax?[expressions.Count];
        var anyAssignment = false;
        for (var index = 0; index < expressions.Count; index++)
        {
            if (expressions[index] is AssignmentExpressionSyntax { Left: IdentifierNameSyntax } assignment)
            {
                assignments[index] = assignment;
                anyAssignment = true;
            }
        }

        var position = creation.SpanStart;
        var annotated = anyAssignment
            ? FluentMethodResolver.LookupAnnotatedSetters(model, receiver, position)
            : ImmutableArray<FluentAnnotatedSetter>.Empty;

        // The generated calls are bound against `new T(...)` without the initializer, which is what the
        // fix emits as the chain root.
        var root = creation
            .WithArgumentList(creation.ArgumentList ?? SyntaxFactory.ArgumentList())
            .WithInitializer(null)
            .WithoutTrivia();

        var invocations = ImmutableArray.CreateBuilder<FluentInvocationPlan>();
        var residual = ImmutableArray.CreateBuilder<int>();
        var current = 0;
        while (current < expressions.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (assignments[current] is null)
            {
                residual.Add(current);
                current++;
                continue;
            }

            var candidates = CollectCandidates(model, receiver, position, assignments, current, annotated);
            var consumed = 1;
            var chosen = Choose(model, position, root, candidates, out var tied);
            if (chosen is Candidate plan)
            {
                consumed = plan.Consumed;
                invocations.Add(new FluentInvocationPlan(
                    plan.Method, plan.Arguments, ImmutableArray.CreateRange(Enumerable.Range(current, consumed)), plan.Kind));
            }
            else
            {
                // An unresolvable tie keeps every member it covered in the initializer rather than
                // falling back to a lower-priority setter for part of them.
                consumed = tied;
                for (var offset = 0; offset < consumed; offset++)
                {
                    residual.Add(current + offset);
                }
            }

            current += consumed;
        }

        return new FluentInitializerPlan(invocations.ToImmutable(), residual.ToImmutable());
    }

    private static List<Candidate> CollectCandidates(
        SemanticModel model,
        ITypeSymbol receiver,
        int position,
        AssignmentExpressionSyntax?[] assignments,
        int start,
        ImmutableArray<FluentAnnotatedSetter> annotated)
    {
        var candidates = new List<Candidate>();
        foreach (var setter in annotated)
        {
            if (TryMatchContract(model, assignments, start, setter, out var arguments))
            {
                candidates.Add(new Candidate(setter.Method, setter.Kind, arguments, setter.Members.Length));
            }
        }

        var assignment = assignments[start]!;
        var memberName = ((IdentifierNameSyntax)assignment.Left).Identifier.ValueText;
        var value = assignment.Right;
        var single = ImmutableArray.Create(value);

        foreach (var method in FluentMethodResolver.LookupSingleArgSetters(model, receiver, position, memberName, value))
        {
            candidates.Add(new Candidate(method, FluentInvocationKind.Convention, single, 1));
        }

        foreach (var method in FluentMethodResolver.LookupSingleArgSetters(model, receiver, position, ON_PREFIX + memberName, value))
        {
            candidates.Add(new Candidate(method, FluentInvocationKind.OnPrefix, single, 1));
        }

        return candidates;
    }

    private static bool TryMatchContract(
        SemanticModel model,
        AssignmentExpressionSyntax?[] assignments,
        int start,
        FluentAnnotatedSetter setter,
        out ImmutableArray<ExpressionSyntax> arguments)
    {
        arguments = ImmutableArray<ExpressionSyntax>.Empty;
        var members = setter.Members;
        if (start + members.Length > assignments.Length)
        {
            return false;
        }

        // Members must be contiguous and in contract order: anything else would reorder the
        // assignments inside the generated call.
        var builder = ImmutableArray.CreateBuilder<ExpressionSyntax>();
        for (var offset = 0; offset < members.Length; offset++)
        {
            var assignment = assignments[start + offset];
            if (assignment is null
                || ((IdentifierNameSyntax)assignment.Left).Identifier.ValueText != members[offset].MemberName)
            {
                return false;
            }

            var member = members[offset];
            if (member.ParameterType is ITypeSymbol parameterType)
            {
                if (!model.ClassifyConversion(assignment.Right, parameterType).IsImplicit)
                {
                    return false;
                }

                builder.Add(assignment.Right);
            }
            else if (!MatchesConstant(model, assignment.Right, member.Constant))
            {
                return false;
            }
        }

        arguments = builder.ToImmutable();
        return true;
    }

    private static bool MatchesConstant(SemanticModel model, ExpressionSyntax value, TypedConstant constant)
    {
        var actual = model.GetConstantValue(value);
        if (!actual.HasValue)
        {
            return false;
        }

        if (constant.IsNull)
        {
            return actual.Value is null;
        }

        // An enum is compared by type as well as underlying value, so a different enum that happens to
        // share the number never matches; primitives are held to the same rule.
        var valueType = model.GetTypeInfo(value).Type;
        return actual.Value is not null
            && valueType is not null
            && constant.Type is not null
            && SymbolEqualityComparer.Default.Equals(valueType, constant.Type)
            && actual.Value.Equals(constant.Value);
    }

    private static Candidate? Choose(SemanticModel model, int position, ExpressionSyntax root, List<Candidate> candidates, out int tied)
    {
        tied = 1;
        if (candidates.Count == 0)
        {
            return null;
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.Consumed)
            .ThenBy(KindRank)
            .ThenBy(candidate => candidate.Arguments.Length)
            .ToList();
        var first = ordered[0];
        var group = ordered
            .Where(candidate => candidate.Consumed == first.Consumed
                && KindRank(candidate) == KindRank(first)
                && candidate.Arguments.Length == first.Arguments.Length)
            .ToList();
        tied = first.Consumed;

        // Within the winning rank only C# overload resolution may pick one, so differently named
        // candidates are a tie rather than a lookup-order choice.
        foreach (var other in group)
        {
            if (other.Method.Name != first.Method.Name)
            {
                return null;
            }
        }

        var bound = Bind(model, position, root, first.Method.Name, first.Arguments);
        if (bound is null)
        {
            return null;
        }

        foreach (var candidate in group)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.Method.ReducedFrom, bound))
            {
                return candidate;
            }
        }

        return null;
    }

    private static int KindRank(Candidate candidate)
        // After member count: annotated, then the same-name setter, then the `On` prefix; among
        // annotated candidates `Choose` then prefers fewer arguments (a combined shorthand over a composite).
        => candidate.Kind switch
        {
            FluentInvocationKind.Convention => 1,
            FluentInvocationKind.OnPrefix => 2,
            _ => 0,
        };

    private static IMethodSymbol? Bind(SemanticModel model, int position, ExpressionSyntax root, string name, ImmutableArray<ExpressionSyntax> arguments)
    {
        // The exact call the fix would emit is bound so a plan is only kept when the compiler agrees
        // with the chosen candidate (an instance method or a better overload would win otherwise).
        var invocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression, root, SyntaxFactory.IdentifierName(name)),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(arguments.Select(argument => SyntaxFactory.Argument(argument.WithoutTrivia())))));

        var info = model.GetSpeculativeSymbolInfo(position, invocation, SpeculativeBindingOption.BindAsExpression);
        if (info.Symbol is IMethodSymbol method)
        {
            return method.ReducedFrom ?? method;
        }

        return null;
    }
}
