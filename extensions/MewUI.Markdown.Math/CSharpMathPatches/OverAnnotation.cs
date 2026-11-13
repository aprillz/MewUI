using System.Text;

namespace CSharpMath.Atom.Atoms;

/// <summary>A brace stretched over its argument, <c>\overbrace</c>; the atom's superscript is set above the brace as its label.</summary>
internal sealed class OverAnnotation : MathAtom, IMathListContainer
{
    public OverAnnotation(string nucleus, MathList? innerList = null) : base(nucleus)
    {
        InnerList = innerList ?? new MathList();
    }

    public MathList InnerList { get; }

    IEnumerable<MathList> IMathListContainer.InnerLists => [InnerList];

    public override bool ScriptsAllowed => true;

    public override string DebugString =>
        new StringBuilder(@"\overbrace").AppendInBracesOrLiteralNull(InnerList.DebugString).AppendDebugStringOfScripts(this).ToString();

    protected override MathAtom CloneInside(bool finalize) => new OverAnnotation(Nucleus, InnerList.Clone(finalize));
}
