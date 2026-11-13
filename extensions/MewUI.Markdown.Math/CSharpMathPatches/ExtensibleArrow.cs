namespace CSharpMath.Atom.Atoms;

/// <summary>
/// A relation arrow stretched to fit its labels, <c>\xrightarrow[below]{above}</c> and <c>\xleftarrow</c>; being a
/// <see cref="Relation"/>, it gets relation spacing and turns a neighboring binary operator into a unary one.
/// </summary>
internal sealed class ExtensibleArrow : Relation, IMathListContainer
{
    public ExtensibleArrow(string nucleus, MathList? overList = null, MathList? underList = null) : base(nucleus)
    {
        OverList = overList ?? new MathList();
        UnderList = underList ?? new MathList();
    }

    public MathList OverList { get; }

    public MathList UnderList { get; }

    IEnumerable<MathList> IMathListContainer.InnerLists => [OverList, UnderList];

    protected override MathAtom CloneInside(bool finalize) => new ExtensibleArrow(Nucleus, OverList.Clone(finalize), UnderList.Clone(finalize));
}
