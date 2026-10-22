using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Aprillz.MewUI.Generators;

/// <summary>
/// Immutable array with sequence equality, so incremental pipeline steps that carry one compare by
/// content instead of by reference.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items;

    public EquatableArray(ImmutableArray<T> items) => _items = items;

    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Length => Items.Length;

    public T this[int index] => Items[index];

    public bool Contains(T value)
    {
        foreach (var item in Items)
        {
            if (item.Equals(value))
            {
                return true;
            }
        }

        return false;
    }

    public bool Equals(EquatableArray<T> other)
    {
        var left = Items;
        var right = other.Items;
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (!left[index].Equals(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (var item in Items)
        {
            hash = unchecked(hash * 31 + item.GetHashCode());
        }

        return hash;
    }
}

/// <summary>Build properties the generator reads from the analyzer config.</summary>
internal sealed class FluentGeneratorOptions : IEquatable<FluentGeneratorOptions>
{
    public FluentGeneratorOptions(bool enabled, string? namespaceOverride)
    {
        Enabled = enabled;
        NamespaceOverride = namespaceOverride;
    }

    public bool Enabled { get; }

    /// <summary>Namespace every generated class is placed in, or null to use the owner's namespace.</summary>
    public string? NamespaceOverride { get; }

    public bool Equals(FluentGeneratorOptions? other)
        => other != null && Enabled == other.Enabled && NamespaceOverride == other.NamespaceOverride;

    public override bool Equals(object? obj) => Equals(obj as FluentGeneratorOptions);

    public override int GetHashCode() => unchecked((Enabled ? 1 : 0) * 397 ^ (NamespaceOverride?.GetHashCode() ?? 0));
}

/// <summary>A public settable property or public event declared on an owner type.</summary>
internal sealed class FluentMemberModel : IEquatable<FluentMemberModel>
{
    public FluentMemberModel(
        string name,
        bool isEvent,
        string typeDisplay,
        string typeKey,
        bool isBool,
        bool isDelegate,
        int delegateArity,
        bool isHiddenBySameTypedBase,
        string summaryXml)
    {
        Name = name;
        IsEvent = isEvent;
        TypeDisplay = typeDisplay;
        TypeKey = typeKey;
        IsBool = isBool;
        IsDelegate = isDelegate;
        DelegateArity = delegateArity;
        IsHiddenBySameTypedBase = isHiddenBySameTypedBase;
        SummaryXml = summaryXml;
    }

    public string Name { get; }

    public bool IsEvent { get; }

    /// <summary>Fully qualified type with nullable annotations, as written into the parameter.</summary>
    public string TypeDisplay { get; }

    /// <summary>Fully qualified type without nullable annotations, compared against existing extensions.</summary>
    public string TypeKey { get; }

    public bool IsBool { get; }

    public bool IsDelegate { get; }

    /// <summary>Parameter count of the delegate's Invoke, or -1 when the type is not a delegate.</summary>
    public int DelegateArity { get; }

    /// <summary>True when a base type declares the same member with the same type, so the base owner's extension already applies.</summary>
    public bool IsHiddenBySameTypedBase { get; }

    /// <summary>Inner XML of the member's summary with paramref references flattened, or empty.</summary>
    public string SummaryXml { get; }

    /// <summary>Name of the generated method: the member name, On + event name, or With + delegate property name.</summary>
    public string MethodName => IsEvent ? "On" + Name : IsDelegate ? "With" + Name : Name;

    public bool Equals(FluentMemberModel? other)
        => other != null
            && Name == other.Name
            && IsEvent == other.IsEvent
            && TypeDisplay == other.TypeDisplay
            && TypeKey == other.TypeKey
            && IsBool == other.IsBool
            && IsDelegate == other.IsDelegate
            && DelegateArity == other.DelegateArity
            && IsHiddenBySameTypedBase == other.IsHiddenBySameTypedBase
            && SummaryXml == other.SummaryXml;

    public override bool Equals(object? obj) => Equals(obj as FluentMemberModel);

    public override int GetHashCode()
        => unchecked(Name.GetHashCode() * 397 ^ TypeDisplay.GetHashCode() ^ SummaryXml.GetHashCode());
}

/// <summary>An owner type the generator emits one extension class for.</summary>
internal sealed class FluentOwnerModel : IEquatable<FluentOwnerModel>
{
    public FluentOwnerModel(
        string ownerNamespace,
        string name,
        string key,
        string display,
        string cref,
        bool isSealed,
        string typeParameterList,
        string constraintClauses,
        bool extensionClassExists,
        EquatableArray<string> chain,
        EquatableArray<string> instanceMethodNames,
        EquatableArray<FluentMemberModel> members)
    {
        Namespace = ownerNamespace;
        Name = name;
        Key = key;
        Display = display;
        Cref = cref;
        IsSealed = isSealed;
        TypeParameterList = typeParameterList;
        ConstraintClauses = constraintClauses;
        ExtensionClassExists = extensionClassExists;
        Chain = chain;
        InstanceMethodNames = instanceMethodNames;
        Members = members;
    }

    /// <summary>Namespace of the owner; empty for the global namespace.</summary>
    public string Namespace { get; }

    /// <summary>Simple name without generic arity.</summary>
    public string Name { get; }

    /// <summary>Fully qualified original definition, the identity used by the conflict index.</summary>
    public string Key { get; }

    /// <summary>Fully qualified display with the owner's own type parameters, used as the receiver type.</summary>
    public string Display { get; }

    /// <summary>Owner reference in cref syntax (generic arguments in braces).</summary>
    public string Cref { get; }

    public bool IsSealed { get; }

    /// <summary>"&lt;TItem&gt;" for a generic owner, else empty.</summary>
    public string TypeParameterList { get; }

    /// <summary>Constraint clauses of the owner's own type parameters, with a leading space, else empty.</summary>
    public string ConstraintClauses { get; }

    /// <summary>True when a type named like the generated class already exists in the compilation.</summary>
    public bool ExtensionClassExists { get; }

    /// <summary>Keys of the owner, its base types and all interfaces.</summary>
    public EquatableArray<string> Chain { get; }

    /// <summary>Names of ordinary instance methods visible on the owner, which would shadow a same-named extension.</summary>
    public EquatableArray<string> InstanceMethodNames { get; }

    public EquatableArray<FluentMemberModel> Members { get; }

    public bool IsGeneric => TypeParameterList.Length > 0;

    public string ClassName => Name + "MarkupExtensions";

    public bool Equals(FluentOwnerModel? other)
        => other != null
            && Namespace == other.Namespace
            && Name == other.Name
            && Key == other.Key
            && Display == other.Display
            && Cref == other.Cref
            && IsSealed == other.IsSealed
            && TypeParameterList == other.TypeParameterList
            && ConstraintClauses == other.ConstraintClauses
            && ExtensionClassExists == other.ExtensionClassExists
            && Chain.Equals(other.Chain)
            && InstanceMethodNames.Equals(other.InstanceMethodNames)
            && Members.Equals(other.Members);

    public override bool Equals(object? obj) => Equals(obj as FluentOwnerModel);

    public override int GetHashCode() => unchecked(Key.GetHashCode() * 397 ^ Members.GetHashCode());
}

/// <summary>An existing extension method the generator must not duplicate.</summary>
internal sealed class FluentExtensionEntry : IEquatable<FluentExtensionEntry>
{
    public FluentExtensionEntry(
        string name,
        string display,
        bool appliesToAnyReceiver,
        bool receiverIsTypeParameter,
        EquatableArray<string> receiverRoots,
        EquatableArray<string> receiverChain,
        int valueCount,
        string firstValueKey,
        bool firstValueIsDelegate,
        int firstValueDelegateArity,
        bool restOptional,
        string? hintedMember)
    {
        Name = name;
        Display = display;
        AppliesToAnyReceiver = appliesToAnyReceiver;
        ReceiverIsTypeParameter = receiverIsTypeParameter;
        ReceiverRoots = receiverRoots;
        ReceiverChain = receiverChain;
        ValueCount = valueCount;
        FirstValueKey = firstValueKey;
        FirstValueIsDelegate = firstValueIsDelegate;
        FirstValueDelegateArity = firstValueDelegateArity;
        RestOptional = restOptional;
        HintedMember = hintedMember;
    }

    public string Name { get; }

    /// <summary>Declaring class and name, for diagnostics.</summary>
    public string Display { get; }

    /// <summary>True when the receiver is a type parameter without a type constraint.</summary>
    public bool AppliesToAnyReceiver { get; }

    /// <summary>True when the receiver is a generic method type parameter, so the method is generic over the receiver like a generated one.</summary>
    public bool ReceiverIsTypeParameter { get; }

    /// <summary>Keys of the receiver type or of the receiver's constraint types.</summary>
    public EquatableArray<string> ReceiverRoots { get; }

    /// <summary>Keys of the roots, their base types and all interfaces.</summary>
    public EquatableArray<string> ReceiverChain { get; }

    /// <summary>Number of parameters after the receiver.</summary>
    public int ValueCount { get; }

    /// <summary>Key of the first value parameter type, or empty when there is none or it is a type parameter.</summary>
    public string FirstValueKey { get; }

    public bool FirstValueIsDelegate { get; }

    public int FirstValueDelegateArity { get; }

    /// <summary>True when every parameter after the first value parameter is optional or params.</summary>
    public bool RestOptional { get; }

    /// <summary>Member named by a FluentSetsMember hint on the single value parameter, or null.</summary>
    public string? HintedMember { get; }

    /// <summary>True when the extension applies to every instance of the owner: its receiver is the owner, a base, or an implemented interface.</summary>
    public bool AppliesToOwner(FluentOwnerModel owner)
    {
        if (AppliesToAnyReceiver)
        {
            return true;
        }

        foreach (var root in ReceiverRoots.Items)
        {
            if (owner.Chain.Contains(root))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the extension applies to some instances of the owner: its receiver is the owner, a base, a derived type, or an implemented interface.</summary>
    public bool IsRelatedTo(FluentOwnerModel owner) => AppliesToOwner(owner) || ReceiverChain.Contains(owner.Key);

    public bool Equals(FluentExtensionEntry? other)
        => other != null
            && Name == other.Name
            && Display == other.Display
            && AppliesToAnyReceiver == other.AppliesToAnyReceiver
            && ReceiverIsTypeParameter == other.ReceiverIsTypeParameter
            && ReceiverRoots.Equals(other.ReceiverRoots)
            && ReceiverChain.Equals(other.ReceiverChain)
            && ValueCount == other.ValueCount
            && FirstValueKey == other.FirstValueKey
            && FirstValueIsDelegate == other.FirstValueIsDelegate
            && FirstValueDelegateArity == other.FirstValueDelegateArity
            && RestOptional == other.RestOptional
            && HintedMember == other.HintedMember;

    public override bool Equals(object? obj) => Equals(obj as FluentExtensionEntry);

    public override int GetHashCode() => unchecked(Name.GetHashCode() * 397 ^ Display.GetHashCode());
}

/// <summary>Every extension method visible to the compilation that could collide with a generated one.</summary>
internal sealed class FluentExtensionIndex : IEquatable<FluentExtensionIndex>
{
    public static readonly FluentExtensionIndex Empty = new(new EquatableArray<FluentExtensionEntry>(ImmutableArray<FluentExtensionEntry>.Empty), true);

    public FluentExtensionIndex(EquatableArray<FluentExtensionEntry> entries, bool hintAttributesAvailable)
    {
        Entries = entries;
        HintAttributesAvailable = hintAttributesAvailable;
    }

    public EquatableArray<FluentExtensionEntry> Entries { get; }

    /// <summary>True when the compilation can already use Aprillz.MewUI.FluentSetsMemberAttribute.</summary>
    public bool HintAttributesAvailable { get; }

    public IEnumerable<FluentExtensionEntry> Named(string name)
    {
        foreach (var entry in Entries.Items)
        {
            if (entry.Name == name)
            {
                yield return entry;
            }
        }
    }

    public bool Equals(FluentExtensionIndex? other)
        => other != null && HintAttributesAvailable == other.HintAttributesAvailable && Entries.Equals(other.Entries);

    public override bool Equals(object? obj) => Equals(obj as FluentExtensionIndex);

    public override int GetHashCode() => Entries.GetHashCode();
}
