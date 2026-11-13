using CSharpMath.Atom;
using CSharpMath.Atom.Atoms;

namespace Aprillz.MewUI.Markdown;

/// <summary>
/// Adds the commands the patched typesetter draws to CSharpMath's command table: <c>\overbrace</c>,
/// <c>\xrightarrow</c> and <c>\xleftarrow</c>. The table is static, so this runs once for every formula.
/// </summary>
internal static class MathCommands
{
    static MathCommands()
    {
        string overBrace = char.ConvertFromUtf32(0x23DE);
        string rightArrow = char.ConvertFromUtf32(0x2192);
        string leftArrow = char.ConvertFromUtf32(0x2190);

        LaTeXSettings.Commands.Add(@"\overbrace", (parser, accumulate, stopChar) =>
            parser.ReadArgument().Bind(inner => LaTeXSettings.Ok(new OverAnnotation(overBrace, inner))));
        LaTeXSettings.Commands.Add(@"\xrightarrow", (parser, accumulate, stopChar) => ReadArrow(parser, rightArrow));
        LaTeXSettings.Commands.Add(@"\xleftarrow", (parser, accumulate, stopChar) => ReadArrow(parser, leftArrow));
    }

    /// <summary>Makes sure the commands are registered before a formula is parsed.</summary>
    public static void EnsureRegistered()
    {
    }

    private static Result<(MathAtom? Atom, MathList? Return)> ReadArrow(LaTeXParser parser, string arrow) =>
        parser.ReadArgumentOptional().Bind(under =>
            parser.ReadArgument().Bind(over => LaTeXSettings.Ok(new ExtensibleArrow(arrow, over, under))));
}
