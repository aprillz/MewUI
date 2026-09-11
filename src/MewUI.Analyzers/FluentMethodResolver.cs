using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

// Resolves the fluent setter for `receiver.MemberName(value)` by querying the compilation, so the
// hand-written extension methods themselves are the source of truth (no separate mapping table to drift).
internal static class FluentMethodResolver
{
    private const string REPLACES_MEMBER_ATTRIBUTE = "FluentReplacesMemberAttribute";

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
        foreach (var symbol in model.LookupSymbols(position, receiver, name, includeReducedExtensionMethods: true))
        {
            // Only reduced extension methods that take a single value compatible with the assignment.
            if (symbol is IMethodSymbol method
                && method.ReducedFrom is not null
                && method.Parameters.Length == 1
                && model.ClassifyConversion(value, method.Parameters[0].Type).IsImplicit)
            {
                return method;
            }
        }

        return null;
    }
}
