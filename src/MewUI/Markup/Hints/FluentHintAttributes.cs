namespace Aprillz.MewUI;

/// <summary>
/// Declares that a fluent extension method replaces calls to the named instance member.
/// </summary>
/// <remarks>
/// Read by the MewUI analyzers to fold configuration statements into a fluent chain, and by the markup audit.
/// Both read it by metadata name, so the same declaration may be compiled into other assemblies as internal.
/// The extension must accept every argument of the member it replaces, and applying it in the member's place
/// must have the same effect and ordering; the declaration is the only guarantee of that, so the two are
/// verified by hand when the attribute is added.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class FluentReplacesMemberAttribute : Attribute
{
    public FluentReplacesMemberAttribute(string memberName) => MemberName = memberName;

    /// <summary>Name of the instance member this extension replaces.</summary>
    public string MemberName { get; }
}

/// <summary>
/// Declares that a value parameter of a fluent extension method replaces the assignment of the named instance
/// property, or the subscription of the named event when the parameter is a handler.
/// </summary>
/// <remarks>
/// Read by the MewUI analyzers and the markup audit by metadata name, so the same declaration may be compiled
/// into other assemblies as internal. The parameter order is the assignment order, and applying the extension in
/// place of the assignments must have the same effect and ordering; a handler parameter must subscribe the named
/// event unchanged. The declaration is the only guarantee of that, so the two are verified by hand when the
/// attribute is added.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
internal sealed class FluentSetsMemberAttribute : Attribute
{
    public FluentSetsMemberAttribute(string memberName) => MemberName = memberName;

    /// <summary>Name of the instance property this parameter assigns, or of the event it subscribes.</summary>
    public string MemberName { get; }
}

/// <summary>
/// Declares that a parameterless fluent shorthand assigns the constant value to the named instance property.
/// </summary>
/// <remarks>
/// Read by the MewUI analyzers and the markup audit by metadata name, so the same declaration may be compiled
/// into other assemblies as internal. The order is the 0-based contiguous assignment order among the constants
/// declared on the same method; the declaration is the only guarantee that the shorthand matches the
/// assignments, so the two are verified by hand when the attribute is added.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class FluentSetsConstantAttribute : Attribute
{
    public FluentSetsConstantAttribute(string memberName, object? value, int order = 0)
    {
        MemberName = memberName;
        Value = value;
        Order = order;
    }

    /// <summary>Name of the instance property the shorthand assigns.</summary>
    public string MemberName { get; }

    /// <summary>Constant assigned to the property.</summary>
    public object? Value { get; }

    /// <summary>0-based assignment order among the constants of the same method.</summary>
    public int Order { get; }
}

/// <summary>
/// Declares that the property or event intentionally has no fluent extension.
/// </summary>
/// <remarks>
/// Read by the MewUI analyzers and the markup audit by metadata name, so the same declaration may be compiled
/// into other assemblies as internal. The audit does not count an excluded member as missing; the declaration
/// is the only record of the exclusion, so the reason is verified by hand when the attribute is added.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Event, AllowMultiple = false, Inherited = false)]
internal sealed class FluentExcludedAttribute : Attribute
{
    public FluentExcludedAttribute(string reason) => Reason = reason;

    /// <summary>Why the member has no fluent extension.</summary>
    public string Reason { get; }
}
