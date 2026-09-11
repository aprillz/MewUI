using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

namespace Aprillz.MewUI.Analyzers;

// Applies the merge and the single-statement conversion to a document. Kept apart from the modules that
// decide them: those run inside the analyzer, which must not touch Workspaces types (RS1022).
internal static class FluentChainEdits
{
    public static async Task<Document> MergeAsync(
        Document document, StatementSyntax anchor,
        List<(ExpressionStatementSyntax Statement, ChainStatement Described)> followUps,
        CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (model is null
            || FluentChainMerge.BuildMergedStatement(anchor, followUps, model, text, cancellationToken) is not StatementSyntax merged)
        {
            return document;
        }

        var editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        editor.ReplaceNode(anchor, merged);
        foreach (var (statement, _) in followUps)
        {
            editor.RemoveNode(FluentChainMerge.RemovalNode(statement), SyntaxRemoveOptions.KeepNoTrivia);
        }

        return editor.GetChangedDocument();
    }

    public static async Task<Document> ConvertAsync(
        Document document, ExpressionStatementSyntax statement, ChainStatement described, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        return document.WithSyntaxRoot(root.ReplaceNode(statement, FluentCallConversion.BuildReplacement(statement, described)));
    }
}
