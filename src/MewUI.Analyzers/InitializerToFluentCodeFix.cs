using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InitializerToFluentCodeFix)), Shared]
public sealed class InitializerToFluentCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(FluentDiagnostics.InitializerToFluentId);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        var creation = root.FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<ObjectCreationExpressionSyntax>();
        if (creation is null || creation.Initializer is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to fluent chain",
                createChangedDocument: cancellationToken => ConvertAsync(context.Document, creation, cancellationToken),
                equivalenceKey: FluentDiagnostics.InitializerToFluentId),
            diagnostic);
    }

    static async Task<Document> ConvertAsync(Document document, ObjectCreationExpressionSyntax creation, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var receiver = model?.GetTypeInfo(creation, cancellationToken).Type;
        var initializer = creation.Initializer;
        if (model is null || root is null || receiver is null || initializer is null)
        {
            return document;
        }

        var convertible = new List<(string Method, ExpressionSyntax Value)>();
        var residual = new List<ExpressionSyntax>();

        foreach (var expression in initializer.Expressions)
        {
            if (expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax name } assignment)
            {
                var setter = FluentMethodResolver.ResolveSetter(
                    model, receiver, creation.SpanStart, name.Identifier.ValueText, assignment.Right);
                if (setter is not null)
                {
                    convertible.Add((setter.Name, assignment.Right.WithoutTrivia()));
                    continue;
                }
            }

            residual.Add(expression.WithoutTrivia());
        }

        if (convertible.Count == 0)
        {
            return document;
        }

        // Base `new T(...)`: strip the type's trailing trivia (e.g. the newline before a multi-line
        // initializer) so the parens are not stranded, and keep any unmapped members inline.
        var baseCreation = creation
            .WithType(creation.Type.WithoutTrailingTrivia())
            .WithArgumentList((creation.ArgumentList ?? SyntaxFactory.ArgumentList()).WithoutTrivia())
            .WithInitializer(residual.Count == 0 ? null : BuildResidualInitializer(residual));

        ExpressionSyntax chain = baseCreation;
        foreach (var (method, value) in convertible)
        {
            chain = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression, chain, SyntaxFactory.IdentifierName(method)),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(value))));
        }

        // Auto-format: expand into the markup tree once there are two or more calls.
        var newline = text.ToString().Contains("\r\n") ? "\r\n" : "\n";
        var lineText = text.Lines.GetLineFromPosition(creation.SpanStart).ToString();
        var baseIndent = lineText.Substring(0, lineText.Length - lineText.TrimStart().Length);

        var formatted = FluentChainLayout
            .Format((InvocationExpressionSyntax)chain, baseIndent, convertible.Count >= FluentChainLayout.MinLinks, newline)
            .WithTriviaFrom(creation);

        return document.WithSyntaxRoot(root.ReplaceNode(creation, formatted));
    }

    static InitializerExpressionSyntax BuildResidualInitializer(List<ExpressionSyntax> residual)
    {
        var spacedCommas = Enumerable.Repeat(
            SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space),
            System.Math.Max(0, residual.Count - 1));

        var braced = SyntaxFactory.InitializerExpression(
            SyntaxKind.ObjectInitializerExpression, SyntaxFactory.SeparatedList(residual, spacedCommas));

        // `new T() { a, b }` - a space after `)` and inside both braces.
        return braced
            .WithOpenBraceToken(braced.OpenBraceToken
                .WithLeadingTrivia(SyntaxFactory.Space)
                .WithTrailingTrivia(SyntaxFactory.Space))
            .WithCloseBraceToken(braced.CloseBraceToken.WithLeadingTrivia(SyntaxFactory.Space));
    }
}
