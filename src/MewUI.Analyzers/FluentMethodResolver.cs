using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

// Resolves the fluent setter for `receiver.MemberName(value)` by querying the compilation, so the
// hand-written extension methods themselves are the source of truth (no separate mapping table to drift).
internal static class FluentMethodResolver
{
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
