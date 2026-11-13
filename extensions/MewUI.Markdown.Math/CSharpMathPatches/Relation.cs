// Patched copy of CSharpMath 67d5b474 CSharpMath/Atom/Atoms/Relation.cs: not sealed, so ExtensibleArrow can derive from it.
namespace CSharpMath.Atom.Atoms {
  /// <summary>AMSMath class 3: Relation/Comparison (verb), e.g. =, &lt;, \subset</summary>
  /// <summary>A relation -- =, &lt; etc.</summary>
  public class Relation : MathAtom {
    public Relation(string nucleus) : base(nucleus) { }
    public override bool ScriptsAllowed => true;
    public new Relation Clone(bool finalize) => (Relation)base.Clone(finalize);
    protected override MathAtom CloneInside(bool finalize) => new Relation(Nucleus);
  }
}