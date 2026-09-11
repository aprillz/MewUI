using System.Collections.Generic;
using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Aprillz.MewUI.Analyzers;

// MEW1105 / MEW1106: the diagnostic form of the MEW1103 / MEW1104 refactorings, so a whole file can be
// converted at once with Fix All instead of one caret position at a time. Statements are examined per
// container: an anchor claims the follow-ups it absorbs, and only what is left over is reported as a
// standalone conversion, so the two fixes never target the same statement.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ChainStatementAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(FluentDiagnostics.MergeStatements, FluentDiagnostics.StatementToFluentCall);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeBlock, SyntaxKind.Block);
        context.RegisterSyntaxNodeAction(AnalyzeCompilationUnit, SyntaxKind.CompilationUnit);
    }

    private static void AnalyzeBlock(SyntaxNodeAnalysisContext context)
        => Analyze(context, ((BlockSyntax)context.Node).Statements);

    private static void AnalyzeCompilationUnit(SyntaxNodeAnalysisContext context)
    {
        var statements = new List<StatementSyntax>();
        foreach (var member in ((CompilationUnitSyntax)context.Node).Members)
        {
            if (member is GlobalStatementSyntax global)
            {
                statements.Add(global.Statement);
            }
        }

        Analyze(context, statements);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, IReadOnlyList<StatementSyntax> statements)
    {
        for (var index = 0; index < statements.Count; index++)
        {
            var statement = statements[index];
            var followUps = FluentChainMerge.TryCollect(statement, context.SemanticModel, context.CancellationToken);
            if (followUps is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FluentDiagnostics.MergeStatements,
                    FluentChainMerge.AnchorLocation(statement),
                    AnchorName(statement)));

                // The absorbed statements belong to this merge, so they are not reported on their own.
                index += followUps.Count;
                continue;
            }

            if (statement is ExpressionStatementSyntax expressionStatement
                && FluentCallConversion.TryDescribe(expressionStatement, context.SemanticModel, context.CancellationToken) is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FluentDiagnostics.StatementToFluentCall, expressionStatement.GetLocation()));
            }
        }
    }

    private static string AnchorName(StatementSyntax anchor)
    {
        if (anchor is LocalDeclarationStatementSyntax declaration && declaration.Declaration.Variables.Count == 1)
        {
            return declaration.Declaration.Variables[0].Identifier.ValueText;
        }

        if (anchor is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment })
        {
            return assignment.Left.ToString();
        }

        return anchor.ToString();
    }
}
