namespace Aprillz.MewUI;

/// <summary>
/// Declares that a fluent extension method replaces calls to the named instance member.
/// </summary>
/// <remarks>
/// Read by the MewUI analyzers to fold configuration statements into a fluent chain. The extension must
/// accept every argument of the member it replaces, and applying it in the member's place must have the
/// same effect and ordering; the declaration is the only guarantee of that, so the two are verified by
/// hand when the attribute is added.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class FluentReplacesMemberAttribute : Attribute
{
    public FluentReplacesMemberAttribute(string memberName) => MemberName = memberName;

    /// <summary>Name of the instance member this extension replaces.</summary>
    public string MemberName { get; }
}
