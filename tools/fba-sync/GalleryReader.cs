using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aprillz.MewUI.FbaSync;

/// <summary>
/// Reads the project gallery and keeps what a file-based app can host unchanged: the usings, and the
/// type declarations themselves. The gallery's classes stay classes, so nothing about members,
/// overloads, accessibility, or inherited state has to be rewritten.
/// </summary>
internal sealed class GalleryReader
{
    private readonly List<string> _usings = new();
    private readonly List<string> _types = new();
    private readonly List<string> _files = new();

    public IReadOnlyList<string> Usings => _usings;

    /// <summary>Type declarations in file order, each already free of preprocessor directives.</summary>
    public IReadOnlyList<string> Types => _types;

    public IReadOnlyList<string> Files => _files;

    /// <summary>Parses one gallery file; disabled preprocessor branches are dropped by the parser.</summary>
    public void Read(string path)
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: Array.Empty<string>());
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), options, path);
        var root = (CompilationUnitSyntax)tree.GetRoot();

        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            throw new FbaSyncException($"{Path.GetFileName(path)}: {errors[0].GetMessage()} at {errors[0].Location.GetLineSpan().StartLinePosition}");
        }

        foreach (var directive in root.Usings)
        {
            _usings.Add(directive.ToString().Trim());
        }

        _files.Add(Path.GetFileName(path));
        foreach (var (member, dedent) in EnumerateTypes(root))
        {
            _types.Add(Emit.Type(member, dedent));
        }
    }

    /// <summary>
    /// Unwraps the namespace: a file-based app declares its types beside the top-level code, in the
    /// global namespace. A block-bodied namespace also costs its members one indent level.
    /// </summary>
    private static IEnumerable<(MemberDeclarationSyntax Member, int Dedent)> EnumerateTypes(CompilationUnitSyntax root)
    {
        foreach (var member in root.Members)
        {
            if (member is BaseNamespaceDeclarationSyntax ns)
            {
                int dedent = ns is NamespaceDeclarationSyntax ? 4 : 0;
                foreach (var nested in ns.Members)
                {
                    yield return (nested, dedent);
                }
            }
            else
            {
                yield return (member, 0);
            }
        }
    }
}
