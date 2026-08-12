using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Generators;

internal enum BindingSegmentKind
{
    Getter,
    Notifying,
    Observable,
    MewProperty,
}

internal sealed class BindingSegment(
    BindingSegmentKind kind,
    string memberName,
    bool canSetMember,
    string? mewPropertyOwner)
{
    public BindingSegmentKind Kind => kind;

    public string MemberName => memberName;

    public bool CanSetMember => canSetMember;

    public string? MewPropertyOwner => mewPropertyOwner;
}

/// <summary>
/// Turns a dotted getter lambda into the segment list the runtime path API expects.
/// </summary>
internal static class BindingChain
{
    private const string OBSERVABLE_VALUE = "Aprillz.MewUI.ObservableValue`1";
    private const string MEW_OBJECT = "Aprillz.MewUI.Controls.MewObject";
    private const string MEW_PROPERTY = "Aprillz.MewUI.MewProperty`1";
    private const string NOTIFY_INTERFACE = "System.ComponentModel.INotifyPropertyChanged";

    /// <summary>
    /// Reads the member names a lambda body walks through, or returns null when the body uses
    /// syntax that is not a path.
    /// </summary>
    internal static List<SimpleNameSyntax>? TryReadMemberChain(AnonymousFunctionExpressionSyntax lambda)
    {
        string? parameterName = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax parenthesized
                when parenthesized.ParameterList.Parameters.Count == 1
                => parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
            _ => null,
        };

        if (parameterName == null || lambda.Body is not ExpressionSyntax body)
        {
            return null;
        }

        var chain = new List<SimpleNameSyntax>();
        var current = Unwrap(body);
        while (true)
        {
            if (current is MemberAccessExpressionSyntax access
                && access.IsKind(SyntaxKind.SimpleMemberAccessExpression)
                && access.Name is IdentifierNameSyntax member)
            {
                chain.Insert(0, member);
                current = Unwrap(access.Expression);
                continue;
            }

            if (current is MemberBindingExpressionSyntax binding
                && binding.Name is IdentifierNameSyntax conditionalMember)
            {
                chain.Insert(0, conditionalMember);
                return null;
            }

            break;
        }

        if (current is not IdentifierNameSyntax root || root.Identifier.ValueText != parameterName)
        {
            return null;
        }

        return chain.Count == 0 ? null : chain;
    }

    /// <summary>
    /// Classifies every member of the chain, or returns null when a member cannot be expressed as
    /// a path segment.
    /// </summary>
    internal static List<BindingSegment>? TryResolveSegments(
        Compilation compilation,
        SemanticModel semanticModel,
        List<SimpleNameSyntax> chain)
    {
        var notifyInterface = compilation.GetTypeByMetadataName(NOTIFY_INTERFACE);
        var mewObject = compilation.GetTypeByMetadataName(MEW_OBJECT);
        if (notifyInterface == null)
        {
            return null;
        }

        var segments = new List<BindingSegment>(chain.Count);
        for (int index = 0; index < chain.Count; index++)
        {
            var member = chain[index];
            if (semanticModel.GetSymbolInfo(member).Symbol is not IPropertySymbol property)
            {
                return null;
            }

            var owner = property.ContainingType;
            bool canSet = property.SetMethod != null
                && property.SetMethod.DeclaredAccessibility == Accessibility.Public;

            if (IsObservableValue(property.Type))
            {
                segments.Add(new BindingSegment(
                    BindingSegmentKind.Observable, property.Name, canSet, null));
                continue;
            }

            string? mewPropertyOwner = FindMewPropertyOwner(mewObject, owner, property);
            if (mewPropertyOwner != null)
            {
                segments.Add(new BindingSegment(
                    BindingSegmentKind.MewProperty, property.Name, canSet, mewPropertyOwner));
                continue;
            }

            var kind = Implements(owner, notifyInterface)
                ? BindingSegmentKind.Notifying
                : BindingSegmentKind.Getter;
            segments.Add(new BindingSegment(kind, property.Name, canSet, null));
        }

        return segments;
    }

    private static bool IsObservableValue(ITypeSymbol type)
        => type is INamedTypeSymbol named
            && named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                == "global::Aprillz.MewUI.ObservableValue<T>"
            || type.OriginalDefinition.ToDisplayString() == "Aprillz.MewUI.ObservableValue<T>";

    private static bool Implements(ITypeSymbol type, INamedTypeSymbol notifyInterface)
    {
        if (SymbolEqualityComparer.Default.Equals(type, notifyInterface))
        {
            return true;
        }

        foreach (var candidate in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, notifyInterface))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the declaring type of a <c>{member}Property</c> static field, following the framework
    /// convention that pairs a CLR property with its MewProperty.
    /// </summary>
    private static string? FindMewPropertyOwner(
        INamedTypeSymbol? mewObject,
        INamedTypeSymbol owner,
        IPropertySymbol property)
    {
        if (mewObject == null || !DerivesFrom(owner, mewObject))
        {
            return null;
        }

        string fieldName = property.Name + "Property";
        for (var candidate = owner; candidate != null; candidate = candidate.BaseType)
        {
            foreach (var symbol in candidate.GetMembers(fieldName))
            {
                if (symbol is IFieldSymbol { IsStatic: true } field
                    && field.Type is INamedTypeSymbol fieldType
                    && fieldType.OriginalDefinition.ToDisplayString() == "Aprillz.MewUI.MewProperty<T>"
                    && SymbolEqualityComparer.Default.Equals(fieldType.TypeArguments[0], property.Type))
                {
                    return candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }
        }

        return null;
    }

    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var candidate = type; candidate != null; candidate = candidate.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (true)
        {
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }
            else if (expression is PostfixUnaryExpressionSyntax postfix
                && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            {
                expression = postfix.Operand;
            }
            else if (expression is CastExpressionSyntax cast)
            {
                expression = cast.Expression;
            }
            else if (expression is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.AsExpression))
            {
                expression = binary.Left;
            }
            else
            {
                return expression;
            }
        }
    }
}
