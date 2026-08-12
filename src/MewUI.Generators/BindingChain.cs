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
    string expression,
    string? setterBody,
    string? mewPropertyReference)
{
    public BindingSegmentKind Kind => kind;

    /// <summary>Lambda body emitted for this segment, written against a parameter named value.</summary>
    public string Expression => expression;

    public string? SetterBody => setterBody;

    public string? MewPropertyReference => mewPropertyReference;
}

internal enum BindingStepKind
{
    Member,
    Indexer,
    Cast,
    AsCast,
}

internal sealed class BindingStep(BindingStepKind kind, ExpressionSyntax node, string? text)
{
    public BindingStepKind Kind => kind;

    public ExpressionSyntax Node => node;

    /// <summary>Index arguments for an indexer, or the target type for a cast.</summary>
    public string? Text => text;
}

/// <summary>
/// Turns a dotted getter lambda into the segment list the runtime path API expects.
/// </summary>
internal static class BindingChain
{
    private const string MEW_OBJECT = "Aprillz.MewUI.Controls.MewObject";
    private const string NOTIFY_INTERFACE = "System.ComponentModel.INotifyPropertyChanged";
    private const string OBSERVABLE_VALUE = "Aprillz.MewUI.ObservableValue<T>";
    private const string MEW_PROPERTY = "Aprillz.MewUI.MewProperty<T>";

    /// <summary>
    /// Reads the steps a lambda body walks through, or returns null when the body uses syntax
    /// that is not a path.
    /// </summary>
    internal static List<BindingStep>? TryReadSteps(
        SemanticModel semanticModel,
        AnonymousFunctionExpressionSyntax lambda)
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

        var steps = new List<BindingStep>();
        return Walk(semanticModel, body, parameterName, steps) ? steps : null;
    }

    private static bool Walk(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        string parameterName,
        List<BindingStep> steps)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return Walk(semanticModel, parenthesized.Expression, parameterName, steps);

            case PostfixUnaryExpressionSyntax postfix
                when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                return Walk(semanticModel, postfix.Operand, parameterName, steps);

            case IdentifierNameSyntax identifier:
                return identifier.Identifier.ValueText == parameterName;

            case MemberBindingExpressionSyntax memberBinding:
                steps.Add(new BindingStep(BindingStepKind.Member, memberBinding, null));
                return true;

            case ElementBindingExpressionSyntax elementBinding:
                return TryAddIndexer(semanticModel, elementBinding, elementBinding.ArgumentList, steps);

            case MemberAccessExpressionSyntax access
                when access.IsKind(SyntaxKind.SimpleMemberAccessExpression):
                if (!Walk(semanticModel, access.Expression, parameterName, steps))
                {
                    return false;
                }

                steps.Add(new BindingStep(BindingStepKind.Member, access, null));
                return true;

            case ElementAccessExpressionSyntax element:
                if (!Walk(semanticModel, element.Expression, parameterName, steps))
                {
                    return false;
                }

                return TryAddIndexer(semanticModel, element, element.ArgumentList, steps);

            case ConditionalAccessExpressionSyntax conditional:
                return Walk(semanticModel, conditional.Expression, parameterName, steps)
                    && Walk(semanticModel, conditional.WhenNotNull, parameterName, steps);

            case CastExpressionSyntax cast:
                if (!Walk(semanticModel, cast.Expression, parameterName, steps))
                {
                    return false;
                }

                return TryAddCast(semanticModel, cast.Type, BindingStepKind.Cast, cast, steps);

            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression):
                if (!Walk(semanticModel, binary.Left, parameterName, steps))
                {
                    return false;
                }

                return binary.Right is TypeSyntax asType
                    && TryAddCast(semanticModel, asType, BindingStepKind.AsCast, binary, steps);

            default:
                return false;
        }
    }

    private static bool TryAddCast(
        SemanticModel semanticModel,
        TypeSyntax type,
        BindingStepKind kind,
        ExpressionSyntax node,
        List<BindingStep> steps)
    {
        if (semanticModel.GetTypeInfo(type).Type is not ITypeSymbol target)
        {
            return false;
        }

        steps.Add(new BindingStep(kind, node, Display(target)));
        return true;
    }

    /// <summary>
    /// Accepts an indexer only when every argument is a constant, because the generated path is a
    /// static field and a lambda there cannot capture anything from the call site.
    /// </summary>
    private static bool TryAddIndexer(
        SemanticModel semanticModel,
        ExpressionSyntax node,
        BracketedArgumentListSyntax arguments,
        List<BindingStep> steps)
    {
        if (arguments.Arguments.Count == 0)
        {
            return false;
        }

        var literals = new List<string>(arguments.Arguments.Count);
        foreach (var argument in arguments.Arguments)
        {
            var constant = semanticModel.GetConstantValue(argument.Expression);
            if (!constant.HasValue)
            {
                return false;
            }

            literals.Add(argument.Expression.ToString());
        }

        steps.Add(new BindingStep(BindingStepKind.Indexer, node, string.Join(", ", literals)));
        return true;
    }

    /// <summary>
    /// Classifies every step, or returns null when a step cannot be expressed as a path segment.
    /// </summary>
    internal static List<BindingSegment>? TryResolveSegments(
        Compilation compilation,
        SemanticModel semanticModel,
        List<BindingStep> steps)
    {
        var notifyInterface = compilation.GetTypeByMetadataName(NOTIFY_INTERFACE);
        var mewObject = compilation.GetTypeByMetadataName(MEW_OBJECT);
        if (notifyInterface == null)
        {
            return null;
        }

        var segments = new List<BindingSegment>(steps.Count);
        for (int index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            bool isLeaf = index == steps.Count - 1;
            var segment = step.Kind switch
            {
                BindingStepKind.Cast => new BindingSegment(
                    BindingSegmentKind.Getter, $"({step.Text})value", null, null),
                BindingStepKind.AsCast => new BindingSegment(
                    BindingSegmentKind.Getter, $"value as {step.Text}", null, null),
                BindingStepKind.Indexer => new BindingSegment(
                    BindingSegmentKind.Getter, $"value[{step.Text}]", null, null),
                _ => ResolveMember(semanticModel, mewObject, notifyInterface, step, isLeaf),
            };

            if (segment == null)
            {
                return null;
            }

            segments.Add(segment);
        }

        return segments;
    }

    private static BindingSegment? ResolveMember(
        SemanticModel semanticModel,
        INamedTypeSymbol? mewObject,
        INamedTypeSymbol notifyInterface,
        BindingStep step,
        bool isLeaf)
    {
        var name = step.Node switch
        {
            MemberAccessExpressionSyntax access => access.Name,
            MemberBindingExpressionSyntax binding => binding.Name,
            _ => null,
        };

        if (name == null || semanticModel.GetSymbolInfo(name).Symbol is not IPropertySymbol property)
        {
            return null;
        }

        string expression = $"value.{property.Name}";
        var owner = property.ContainingType;
        bool canSet = property.SetMethod != null
            && property.SetMethod.DeclaredAccessibility == Accessibility.Public;

        if (property.Type.OriginalDefinition.ToDisplayString() == OBSERVABLE_VALUE)
        {
            return new BindingSegment(BindingSegmentKind.Observable, expression, null, null);
        }

        string? mewProperty = FindMewProperty(mewObject, owner, property);
        if (mewProperty != null)
        {
            return new BindingSegment(BindingSegmentKind.MewProperty, expression, null, mewProperty);
        }

        if (!Implements(owner, notifyInterface))
        {
            return new BindingSegment(BindingSegmentKind.Getter, expression, null, null);
        }

        string? setter = isLeaf && canSet ? $"value.{property.Name} = newValue" : null;
        return new BindingSegment(BindingSegmentKind.Notifying, expression, setter, null);
    }

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
    /// Finds a <c>{member}Property</c> static field, following the framework convention that pairs
    /// a CLR property with its MewProperty.
    /// </summary>
    private static string? FindMewProperty(
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
                    && fieldType.OriginalDefinition.ToDisplayString() == MEW_PROPERTY
                    && SymbolEqualityComparer.Default.Equals(fieldType.TypeArguments[0], property.Type))
                {
                    return $"{Display(candidate)}.{fieldName}";
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

    private static string Display(ITypeSymbol type)
        => type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
}
