using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aprillz.MewUI.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InitializerToFluentAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(FluentDiagnostics.InitializerToFluent);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ObjectCreationExpression);
    }

    static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;

        var initializer = creation.Initializer;
        if (initializer is null || !initializer.IsKind(SyntaxKind.ObjectInitializerExpression))
        {
            return;
        }

        var receiver = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
        if (receiver is null)
        {
            return;
        }

        // Report once if at least one member maps to a fluent setter; the fix handles the rest.
        foreach (var expression in initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax name } assignment
                && FluentMethodResolver.ResolveSetter(
                    context.SemanticModel, receiver, creation.SpanStart, name.Identifier.ValueText, assignment.Right) is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FluentDiagnostics.InitializerToFluent, creation.Type.GetLocation(), receiver.Name));
                return;
            }
        }
    }
}
