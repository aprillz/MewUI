using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.FbaSync;

/// <summary>Puts a gallery type into the form a single file holds: no directives, no namespace indent.</summary>
internal static class Emit
{
    public static string Type(MemberDeclarationSyntax member, int dedent)
    {
        var cleaned = new DirectiveStripper().Visit(member)!;
        string text = cleaned.ToFullString().Replace("\r\n", "\n").Trim('\n');

        if (dedent > 0)
        {
            string pad = new string(' ', dedent);
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].StartsWith(pad, StringComparison.Ordinal) ? lines[i][dedent..] : lines[i].TrimStart();
            }
            text = string.Join('\n', lines);
        }

        return text.TrimEnd();
    }

    /// <summary>
    /// Drops both the directives and the branches they disabled. The parser ran with no symbols
    /// defined, so a disabled branch is already trivia rather than code.
    /// </summary>
    private sealed class DirectiveStripper : CSharpSyntaxRewriter
    {
        public DirectiveStripper() : base(visitIntoStructuredTrivia: false) { }

        public override SyntaxTrivia VisitTrivia(SyntaxTrivia trivia)
            => trivia.IsDirective || trivia.IsKind(SyntaxKind.DisabledTextTrivia)
                ? default
                : base.VisitTrivia(trivia);
    }
}
