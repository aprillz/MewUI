namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Inventory kind names assigned to every extension method.</summary>
public static class ExtensionKinds
{
    public const string SETTER = "setter";
    public const string EVENT = "event";
    public const string BIND = "bind";
    public const string SHORTHAND = "shorthand";
    public const string COMPOSITE = "composite";
    public const string COLLECTION = "collection";
    public const string ALIAS = "alias";
    public const string OTHER = "other";
}

/// <summary>Disposition names assigned to every (concrete receiver, member) pair.</summary>
public static class Dispositions
{
    public const string CONVENTIONAL = "conventional";
    public const string ON_PREFIX = "on-prefix";
    public const string ANNOTATED = "annotated";
    public const string EXCLUDED = "excluded";
    public const string MISSING = "missing";
}

/// <summary>Stable codes of the extension-side findings.</summary>
public static class FindingCodes
{
    public const string RETURN_TYPE = "return-type";
    public const string AMBIGUOUS_EVENT_OVERLOAD = "ambiguous-event-overload";
    public const string HINT_MEMBER_MISSING = "hint-member-missing";
    public const string HINT_PARAMETER_TYPE = "hint-parameter-type";
    public const string HINT_CONSTANT_TYPE = "hint-constant-type";
    public const string HINT_ORDER = "hint-order";
    public const string MISSING_SUMMARY = "missing-summary";
    public const string EVENT_NAME_PREFIX = "event-name-prefix";
    public const string NAMING_PREFIX = "naming-prefix";
    public const string EXCLUDED_MEMBER_COVERED = "excluded-member-covered";
}

/// <summary>Member kind names used by the ledger.</summary>
public static class MemberKinds
{
    public const string PROPERTY = "property";
    public const string EVENT = "event";
}

/// <summary>Input of one audit run.</summary>
public sealed class AuditOptions
{
    /// <summary>Paths of the assemblies to inventory and audit; all are loaded into one metadata context.</summary>
    public required IReadOnlyList<string> AssemblyPaths { get; init; }

    /// <summary>Full names of receiver types that produce no ledger rows.</summary>
    public IReadOnlyCollection<string> ExcludedTypes { get; init; } = Array.Empty<string>();
}

/// <summary>A value parameter of an extension method (the receiver parameter is not listed); <see cref="Modifier"/> is <c>ref</c>, <c>out</c>, <c>in</c>, or null.</summary>
public sealed record ValueParameterRecord(
    string Name,
    string Type,
    bool IsOptional,
    bool IsParams,
    string? SetsMember,
    string? Modifier,
    string? DefaultValue);

/// <summary>
/// Receiver constraint of an extension method: a concrete type or a generic parameter with its constraints.
/// <see cref="ConstraintType"/> is the full name of the concrete receiver or of the class constraint (null when unconstrained),
/// and <see cref="BaseTypes"/> is its base chain from the direct base type up to, but excluding, <c>System.Object</c>.
/// </summary>
public sealed record ReceiverConstraintRecord(
    string Type,
    bool IsGenericParameter,
    IReadOnlyList<string> Constraints,
    string? ConstraintType,
    IReadOnlyList<string> BaseTypes);

/// <summary>A FluentSetsConstant declaration read from an extension method.</summary>
public sealed record ConstantHintRecord(
    string Member,
    string? Value,
    string? ValueType,
    int Order);

/// <summary>One inventory row per public extension method; <see cref="Obsolete"/> is the ObsoleteAttribute message (empty when none was given) or null.</summary>
public sealed record ExtensionMethodRecord(
    string Assembly,
    string DeclaringType,
    string Name,
    string Signature,
    string DocumentationId,
    string ReturnType,
    ReceiverConstraintRecord Receiver,
    IReadOnlyList<ValueParameterRecord> Parameters,
    string Kind,
    IReadOnlyList<string> TargetMembers,
    IReadOnlyList<ConstantHintRecord> Constants,
    IReadOnlyList<string> ReplacedMembers,
    string? Summary,
    string? Obsolete,
    IReadOnlyList<string> ApplicableReceivers);

/// <summary>One ledger row per (concrete receiver, settable property or event) pair.</summary>
public sealed record MemberLedgerEntry(
    string Receiver,
    string ReceiverDisplay,
    string Member,
    string MemberKind,
    string MemberType,
    string DeclaringType,
    string Disposition,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> MultiArgumentExtensions,
    string? ExclusionReason)
{
    /// <summary>True when no extension, hint, or exclusion covers the member.</summary>
    public bool IsMissing => Disposition == Dispositions.MISSING;
}

/// <summary>Two same-name On* overloads applicable to one receiver whose single delegate parameters have the same lambda shape.</summary>
public sealed record AmbiguousEventExtensionRecord(
    string Receiver,
    string ReceiverDisplay,
    string Extension,
    string FirstHandler,
    string SecondHandler,
    string FirstSignature,
    string SecondSignature);

/// <summary>A rule violation; <see cref="Subject"/> is <c>Class.Method</c> for extension checks and <c>Receiver.Member</c> for member checks.</summary>
public sealed record Finding(
    string Code,
    string Assembly,
    string Subject,
    string Message);

/// <summary>Aggregate counts of one audit run.</summary>
public sealed record AuditCounts(
    int Receivers,
    int Properties,
    int Events,
    int MissingProperties,
    int MissingEvents,
    int AmbiguousEventExtensions,
    int Extensions,
    int ExtensionsWithoutSummary,
    IReadOnlyDictionary<string, int> PropertyDispositions,
    IReadOnlyDictionary<string, int> EventDispositions,
    IReadOnlyDictionary<string, int> ExtensionKinds,
    IReadOnlyDictionary<string, int> FindingsByCode);

/// <summary>Complete output of one audit run, ordered deterministically by assembly, type, and member.</summary>
public sealed record AuditResult(
    IReadOnlyList<string> Assemblies,
    IReadOnlyList<string> Receivers,
    IReadOnlyList<ExtensionMethodRecord> Inventory,
    IReadOnlyList<MemberLedgerEntry> Properties,
    IReadOnlyList<MemberLedgerEntry> Events,
    IReadOnlyList<AmbiguousEventExtensionRecord> AmbiguousEventExtensions,
    IReadOnlyList<Finding> Findings,
    AuditCounts Counts,
    IReadOnlyList<string> Warnings);
