using System.Collections.Generic;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

// Resolves the fluent setter for `receiver.MemberName(value)` by querying the compilation, so the
// hand-written extension methods themselves are the source of truth (no separate mapping table to drift).
internal static class FluentMethodResolver
{
    private const string REPLACES_MEMBER_ATTRIBUTE = "FluentReplacesMemberAttribute";
    private const string SETS_MEMBER_ATTRIBUTE = "Aprillz.MewUI.FluentSetsMemberAttribute";
    private const string SETS_CONSTANT_ATTRIBUTE = "Aprillz.MewUI.FluentSetsConstantAttribute";

    public static IMethodSymbol? ResolveSetter(
        SemanticModel model,
        ITypeSymbol receiver,
        int position,
        string memberName,
        ExpressionSyntax value)
        // Prefer a setter named exactly after the member; fall back to the `On`-prefixed convention
        // (e.g. a delegate property `Click = handler` -> `.OnClick(handler)`).
        => LookupSingleArgSetter(model, receiver, position, memberName, value)
           ?? LookupSingleArgSetter(model, receiver, position, "On" + memberName, value);

    /// <summary>Resolves the fluent setter that replaces a static <c>Owner.SetProp(element, value)</c> call.</summary>
    public static IMethodSymbol? ResolveAttachedSetter(
        SemanticModel model,
        ITypeSymbol receiver,
        int position,
        string staticMethodName,
        ExpressionSyntax value)
    {
        const string SET_PREFIX = "Set";

        // The property name is what follows `Set`, the same naming convention the `On` prefix follows.
        if (staticMethodName.Length <= SET_PREFIX.Length
            || !staticMethodName.StartsWith(SET_PREFIX, StringComparison.Ordinal))
        {
            return null;
        }

        return ResolveSetter(model, receiver, position, staticMethodName.Substring(SET_PREFIX.Length), value);
    }

    /// <summary>Resolves the fluent extension declared to replace calls to <paramref name="memberName"/>.</summary>
    public static IMethodSymbol? ResolveMemberReplacement(
        SemanticModel model,
        ITypeSymbol receiver,
        int position,
        string memberName,
        SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        // The replacement is declared on the extension, so every extension in scope has to be examined;
        // nothing about the member's own name points at it.
        foreach (var symbol in model.LookupSymbols(position, receiver, includeReducedExtensionMethods: true))
        {
            if (symbol is IMethodSymbol method
                && method.ReducedFrom is not null
                && method.Parameters.Length == 1
                && method.Parameters[0] is { IsParams: true, Type: IArrayTypeSymbol array }
                && Replaces(method.ReducedFrom, memberName)
                && AllConvertible(model, arguments, array.ElementType))
            {
                return method;
            }
        }

        return null;
    }

    /// <summary>Returns every in-scope single-parameter extension named <paramref name="name"/> that accepts <paramref name="value"/>.</summary>
    public static ImmutableArray<IMethodSymbol> LookupSingleArgSetters(
        SemanticModel model,
        ITypeSymbol receiver,
        int position,
        string name,
        ExpressionSyntax value)
    {
        var setters = ImmutableArray.CreateBuilder<IMethodSymbol>();
        foreach (var symbol in model.LookupSymbols(position, receiver, name, includeReducedExtensionMethods: true))
        {
            // Only reduced extension methods that take a single value compatible with the assignment.
            if (symbol is IMethodSymbol method
                && method.ReducedFrom is not null
                && method.Parameters.Length == 1
                && model.ClassifyConversion(value, method.Parameters[0].Type).IsImplicit)
            {
                setters.Add(method);
            }
        }

        return setters.ToImmutable();
    }

    /// <summary>Returns every in-scope extension whose hint attributes declare the members it assigns, in lookup order.</summary>
    public static ImmutableArray<FluentAnnotatedSetter> LookupAnnotatedSetters(SemanticModel model, ITypeSymbol receiver, int position)
    {
        var setters = ImmutableArray.CreateBuilder<FluentAnnotatedSetter>();
        // A shorthand such as `CenterVertical` shares no name with the member it sets, so the whole
        // extension scope is enumerated once and the declarations are read from the hints.
        foreach (var symbol in model.LookupSymbols(position, receiver, includeReducedExtensionMethods: true))
        {
            if (symbol is IMethodSymbol method && method.ReducedFrom is not null)
            {
                var setter = ReadAnnotatedSetter(method);
                if (setter is not null)
                {
                    setters.Add(setter);
                }
            }
        }

        return setters.ToImmutable();
    }

    private static FluentAnnotatedSetter? ReadAnnotatedSetter(IMethodSymbol method)
    {
        // Attributes are read from the unreduced declaration so parameter indexes line up with the
        // source declaration regardless of how the host represents the reduced symbol.
        var declaration = method.ReducedFrom!;
        if (!TryReadConstants(declaration, out var constants) || !TryReadParameters(method, declaration, out var parameters))
        {
            return null;
        }

        if (constants.Length > 0 && parameters.Length > 0)
        {
            // Combined form: the parameters are assigned first, then the constants, as one contract.
            return new FluentAnnotatedSetter(method, FluentInvocationKind.AnnotatedCombined, parameters.AddRange(constants));
        }

        if (constants.Length > 0 && method.Parameters.Length == 0)
        {
            return new FluentAnnotatedSetter(method, FluentInvocationKind.AnnotatedConstants, constants);
        }

        if (constants.Length == 0 && parameters.Length > 0)
        {
            return new FluentAnnotatedSetter(method, FluentInvocationKind.AnnotatedParameters, parameters);
        }

        // A constant hint on a method with unannotated parameters has no defined contract.
        return null;
    }

    private static bool TryReadParameters(IMethodSymbol method, IMethodSymbol declaration, out ImmutableArray<FluentHintMember> members)
    {
        // Every value parameter must carry exactly one well-formed hint; an unannotated parameter,
        // even an optional one, disqualifies the method because its default could overwrite a member.
        members = ImmutableArray<FluentHintMember>.Empty;
        var builder = ImmutableArray.CreateBuilder<FluentHintMember>(method.Parameters.Length);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var annotated = 0;

        for (var index = 0; index < method.Parameters.Length; index++)
        {
            var reduced = method.Parameters[index];
            var original = declaration.Parameters[index + 1];
            string? memberName = null;
            var hintCount = 0;

            foreach (var attribute in original.GetAttributes())
            {
                if (IsAttribute(attribute, SETS_MEMBER_ATTRIBUTE))
                {
                    hintCount++;
                    memberName = ReadMemberName(attribute, expectedArgumentCount: 1);
                }
            }

            if (hintCount == 0)
            {
                continue;
            }

            if (hintCount > 1 || memberName is null || reduced.IsParams || reduced.RefKind != RefKind.None || !names.Add(memberName))
            {
                return false;
            }

            annotated++;
            builder.Add(new FluentHintMember(memberName, reduced.Type, default));
        }

        if (annotated == 0)
        {
            return true;
        }

        if (annotated != method.Parameters.Length)
        {
            return false;
        }

        members = builder.ToImmutable();
        return true;
    }

    private static bool TryReadConstants(IMethodSymbol declaration, out ImmutableArray<FluentHintMember> members)
    {
        // The constants of one method form a single contract ordered by `order`, which must be a
        // permutation of 0..n-1 over distinct members; anything else is a malformed hint.
        members = ImmutableArray<FluentHintMember>.Empty;
        var ordered = new SortedDictionary<int, FluentHintMember>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in declaration.GetAttributes())
        {
            if (!IsAttribute(attribute, SETS_CONSTANT_ATTRIBUTE))
            {
                continue;
            }

            var memberName = ReadMemberName(attribute, expectedArgumentCount: 3);
            if (memberName is null)
            {
                return false;
            }

            var constant = attribute.ConstructorArguments[1];
            var order = attribute.ConstructorArguments[2];
            if ((constant.Kind != TypedConstantKind.Primitive && constant.Kind != TypedConstantKind.Enum)
                || order.Kind != TypedConstantKind.Primitive
                || order.Value is not int orderValue
                || !names.Add(memberName)
                || ordered.ContainsKey(orderValue))
            {
                return false;
            }

            ordered.Add(orderValue, new FluentHintMember(memberName, null, constant));
        }

        if (ordered.Count == 0)
        {
            return true;
        }

        var expected = 0;
        var builder = ImmutableArray.CreateBuilder<FluentHintMember>(ordered.Count);
        foreach (var pair in ordered)
        {
            if (pair.Key != expected)
            {
                return false;
            }

            expected++;
            builder.Add(pair.Value);
        }

        members = builder.ToImmutable();
        return true;
    }

    private static string? ReadMemberName(AttributeData attribute, int expectedArgumentCount)
    {
        if (attribute.ConstructorArguments.Length != expectedArgumentCount)
        {
            return null;
        }

        var first = attribute.ConstructorArguments[0];
        if (first.Kind == TypedConstantKind.Primitive && first.Value is string name && name.Length > 0)
        {
            return name;
        }

        return null;
    }

    private static bool IsAttribute(AttributeData attribute, string metadataName)
        // Matched by metadata name so the analyzer does not reference the annotated assembly.
        => attribute.AttributeClass is INamedTypeSymbol attributeClass
           && attributeClass.Name.Length > 0
           && attributeClass.ToDisplayString() == metadataName;

    private static bool Replaces(IMethodSymbol declaration, string memberName)
    {
        foreach (var attribute in declaration.GetAttributes())
        {
            // Matched by metadata name so the analyzer does not reference the annotated assembly.
            if (attribute.AttributeClass?.Name == REPLACES_MEMBER_ATTRIBUTE
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value as string == memberName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool AllConvertible(SemanticModel model, SeparatedSyntaxList<ArgumentSyntax> arguments, ITypeSymbol elementType)
    {
        foreach (var argument in arguments)
        {
            if (argument.NameColon is not null
                || !argument.RefKindKeyword.IsKind(SyntaxKind.None)
                || !model.ClassifyConversion(argument.Expression, elementType).IsImplicit)
            {
                return false;
            }
        }

        return true;
    }

    private static IMethodSymbol? LookupSingleArgSetter(
        SemanticModel model,
        ITypeSymbol receiver,
        int position,
        string name,
        ExpressionSyntax value)
    {
        var setters = LookupSingleArgSetters(model, receiver, position, name, value);
        return setters.Length == 0 ? null : setters[0];
    }
}

/// <summary>One initializer member an annotated extension consumes: the value parameter that receives it, or the constant the shorthand assigns.</summary>
internal readonly struct FluentHintMember
{
    public FluentHintMember(string memberName, ITypeSymbol? parameterType, TypedConstant constant)
    {
        MemberName = memberName;
        ParameterType = parameterType;
        Constant = constant;
    }

    public string MemberName { get; }

    // Null for a constant shorthand member.
    public ITypeSymbol? ParameterType { get; }

    public TypedConstant Constant { get; }
}

/// <summary>A reduced extension whose hint attributes declare the initializer members it assigns, in contract order.</summary>
internal sealed class FluentAnnotatedSetter
{
    public FluentAnnotatedSetter(IMethodSymbol method, FluentInvocationKind kind, ImmutableArray<FluentHintMember> members)
    {
        Method = method;
        Kind = kind;
        Members = members;
    }

    public IMethodSymbol Method { get; }

    public FluentInvocationKind Kind { get; }

    public ImmutableArray<FluentHintMember> Members { get; }
}
