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

        // Report once if at least one member maps to a fluent call; the fix applies the same plan.
        var plan = FluentInitializerPlanner.Plan(context.SemanticModel, creation, context.CancellationToken);
        if (plan is not null && plan.Invocations.Length > 0)
        {
            var receiver = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
            context.ReportDiagnostic(Diagnostic.Create(
                FluentDiagnostics.InitializerToFluent, creation.Type.GetLocation(), receiver?.Name ?? creation.Type.ToString()));
        }
    }
}
