using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aprillz.MewUI.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BindingPathSegmentAnalyzer : DiagnosticAnalyzer
{
    private const string NOTIFY_INTERFACE = "System.ComponentModel.INotifyPropertyChanged";
    private const string PATH_TYPE = "BindingPath";
    private const string NOTIFYING_EXTENSIONS_TYPE = "InpcBindingPathExtensions";
    private const string OBSERVABLE_WRAPPER_TYPE = "ObservableValue";
    private const string GETTER_PARAMETER = "getter";

    private const string GENERATOR_MARKER = "Aprillz.MewUI.Generated.BindingPathGeneratorMarker";
    private const string BIND_EXTENSIONS_TYPE = "Aprillz.MewUI.Controls.BindingExtensions";
    private const string MEW_OBJECT_TYPE = "Aprillz.MewUI.Controls.MewObject";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            BindingDiagnostics.NonObservingThen,
            BindingDiagnostics.NotifyingGetterShape,
            BindingDiagnostics.GeneratorRequired);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method)
        {
            return;
        }

        if (method.Name == "ThenNotifying")
        {
            AnalyzeNotifyingSegment(context, invocation, method);
        }
        else if (method.Name == "Then")
        {
            AnalyzeGetterSegment(context, invocation, memberAccess, method);
        }
        else if (method.Name is "Bind" or "SetBinding")
        {
            AnalyzeSugarCall(context, invocation, method);
        }
    }

    private static void AnalyzeSugarCall(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        string ownerType = method.ContainingType?.ToDisplayString() ?? string.Empty;
        if (ownerType != BIND_EXTENSIONS_TYPE && ownerType != MEW_OBJECT_TYPE)
        {
            return;
        }

        if (FindGetterArgument(invocation, method) is not AnonymousFunctionExpressionSyntax getter ||
            CountChainMembers(getter) < 2)
        {
            return;
        }

        // The generator emits this type unconditionally, so its absence means this build cannot
        // rewrite the call and the runtime would reject the getter instead.
        if (context.Compilation.GetTypeByMetadataName(GENERATOR_MARKER) != null)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            BindingDiagnostics.GeneratorRequired, getter.GetLocation(), getter.ToString()));
    }

    private static int CountChainMembers(AnonymousFunctionExpressionSyntax lambda)
    {
        string? parameterName = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax parenthesized
                when parenthesized.ParameterList.Parameters.Count == 1
                => parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
            _ => null,
        };

        if (parameterName is null || lambda.Body is not ExpressionSyntax body)
        {
            return 0;
        }

        int count = 0;
        var current = Unwrap(body);
        while (current is MemberAccessExpressionSyntax access
            && access.IsKind(SyntaxKind.SimpleMemberAccessExpression))
        {
            count++;
            current = Unwrap(access.Expression);
        }

        return current is IdentifierNameSyntax receiver
            && receiver.Identifier.ValueText == parameterName
            ? count
            : 0;
    }

    private static void AnalyzeNotifyingSegment(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        if (!IsDeclaredIn(method, NOTIFYING_EXTENSIONS_TYPE))
        {
            return;
        }

        // A getter that is not written inline cannot be inspected; the runtime rejects it instead.
        if (FindGetterArgument(invocation, method) is not AnonymousFunctionExpressionSyntax getter)
        {
            return;
        }

        if (!TryGetObservedMember(getter, out _))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                BindingDiagnostics.NotifyingGetterShape, getter.GetLocation()));
        }
    }

    private static void AnalyzeGetterSegment(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        IMethodSymbol method)
    {
        if (!IsDeclaredIn(method, PATH_TYPE) ||
            method.ContainingType.TypeArguments.Length != 2 ||
            method.Parameters.Length != 1)
        {
            return;
        }

        // The sibling overload selects an ObservableValue wrapper, which already observes.
        if (method.Parameters[0].Type is not INamedTypeSymbol selector ||
            selector.TypeArguments.Length != 2 ||
            selector.TypeArguments[1].Name == OBSERVABLE_WRAPPER_TYPE)
        {
            return;
        }

        var owner = method.ContainingType.TypeArguments[1];
        if (!RaisesPropertyChanged(context.Compilation, owner))
        {
            return;
        }

        if (FindGetterArgument(invocation, method) is not AnonymousFunctionExpressionSyntax getter ||
            !TryGetObservedMember(getter, out string memberName))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            BindingDiagnostics.NonObservingThen,
            memberAccess.Name.GetLocation(),
            owner.Name,
            memberName));
    }

    private static bool RaisesPropertyChanged(Compilation compilation, ITypeSymbol owner)
    {
        var notifyInterface = compilation.GetTypeByMetadataName(NOTIFY_INTERFACE);
        if (notifyInterface is null)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(owner, notifyInterface)
            || owner.AllInterfaces.Any(
                candidate => SymbolEqualityComparer.Default.Equals(candidate, notifyInterface));
    }

    private static bool IsDeclaredIn(IMethodSymbol method, string typeName)
    {
        var declaringType = method.ContainingType;
        if (declaringType is null || declaringType.Name != typeName)
        {
            return false;
        }

        var innerNamespace = declaringType.ContainingNamespace;
        if (innerNamespace is null || innerNamespace.Name != "MewUI")
        {
            return false;
        }

        var outerNamespace = innerNamespace.ContainingNamespace;
        return outerNamespace is not null && outerNamespace.Name == "Aprillz";
    }

    private static ExpressionSyntax? FindGetterArgument(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method)
    {
        int target = -1;
        for (int index = 0; index < method.Parameters.Length; index++)
        {
            if (method.Parameters[index].Name == GETTER_PARAMETER)
            {
                target = index;
                break;
            }
        }

        if (target < 0)
        {
            return null;
        }

        var arguments = invocation.ArgumentList.Arguments;
        for (int index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument.NameColon is NameColonSyntax named)
            {
                if (named.Name.Identifier.ValueText == GETTER_PARAMETER)
                {
                    return argument.Expression;
                }
            }
            else if (index == target)
            {
                return argument.Expression;
            }
        }

        return null;
    }

    private static bool TryGetObservedMember(
        AnonymousFunctionExpressionSyntax lambda,
        out string memberName)
    {
        memberName = string.Empty;

        string? parameterName = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax parenthesized
                when parenthesized.ParameterList.Parameters.Count == 1
                => parenthesized.ParameterList.Parameters[0].Identifier.ValueText,
            _ => null,
        };

        if (parameterName is null || lambda.Body is not ExpressionSyntax body)
        {
            return false;
        }

        if (Unwrap(body) is not MemberAccessExpressionSyntax access ||
            !access.IsKind(SyntaxKind.SimpleMemberAccessExpression) ||
            access.Name is not IdentifierNameSyntax member)
        {
            return false;
        }

        if (Unwrap(access.Expression) is not IdentifierNameSyntax receiver ||
            receiver.Identifier.ValueText != parameterName)
        {
            return false;
        }

        memberName = member.Identifier.ValueText;
        return true;
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
            else
            {
                return expression;
            }
        }
    }
}
