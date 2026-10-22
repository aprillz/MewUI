using System.Reflection;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Metadata names of the fluent hint attributes; they are matched by name because every assembly declares its own internal copy.</summary>
internal static class HintNames
{
    public const string REPLACES_MEMBER = "Aprillz.MewUI.FluentReplacesMemberAttribute";
    public const string SETS_MEMBER = "Aprillz.MewUI.FluentSetsMemberAttribute";
    public const string SETS_CONSTANT = "Aprillz.MewUI.FluentSetsConstantAttribute";
    public const string EXCLUDED = "Aprillz.MewUI.FluentExcludedAttribute";
}

/// <summary>A FluentSetsMember declaration on one value parameter.</summary>
internal sealed record ParameterHint(ParameterInfo Parameter, string MemberName);

/// <summary>A FluentSetsConstant declaration on a method; <see cref="ValueType"/> is the encoded type of the constant and null for a null constant.</summary>
internal sealed record ConstantHint(string MemberName, object? Value, Type? ValueType, int Order);

/// <summary>All hint declarations of one extension method.</summary>
internal sealed class MethodHints
{
    public static MethodHints None { get; } = new(Array.Empty<string>(), Array.Empty<ParameterHint>(), Array.Empty<ConstantHint>());

    public MethodHints(IReadOnlyList<string> replacedMembers, IReadOnlyList<ParameterHint> parameters, IReadOnlyList<ConstantHint> constants)
    {
        ReplacedMembers = replacedMembers;
        Parameters = parameters;
        Constants = constants;
    }

    public IReadOnlyList<string> ReplacedMembers { get; }

    public IReadOnlyList<ParameterHint> Parameters { get; }

    public IReadOnlyList<ConstantHint> Constants { get; }

    public bool HasAny => ReplacedMembers.Count > 0 || Parameters.Count > 0 || Constants.Count > 0;

    /// <summary>True when any declaration names the member.</summary>
    public bool Names(string memberName)
    {
        return ReplacedMembers.Contains(memberName, StringComparer.Ordinal) ||
               Parameters.Any(hint => hint.MemberName == memberName) ||
               Constants.Any(hint => hint.MemberName == memberName);
    }
}

/// <summary>Reads fluent hint attributes by metadata name and ignores declarations whose constructor shape does not match.</summary>
internal static class HintReader
{
    public static MethodHints Read(MethodInfo method)
    {
        var replaced = new List<string>();
        var constants = new List<ConstantHint>();

        foreach (var attribute in method.GetCustomAttributesData())
        {
            var name = attribute.AttributeType.FullName;
            if (name == HintNames.REPLACES_MEMBER)
            {
                var memberName = ReadString(attribute, 0);
                if (memberName != null)
                {
                    replaced.Add(memberName);
                }
            }
            else if (name == HintNames.SETS_CONSTANT)
            {
                var constant = ReadConstant(attribute);
                if (constant != null)
                {
                    constants.Add(constant);
                }
            }
        }

        var parameters = new List<ParameterHint>();
        foreach (var parameter in method.GetParameters().Skip(1))
        {
            foreach (var attribute in parameter.GetCustomAttributesData())
            {
                if (attribute.AttributeType.FullName != HintNames.SETS_MEMBER)
                {
                    continue;
                }

                var memberName = ReadString(attribute, 0);
                if (memberName != null)
                {
                    parameters.Add(new ParameterHint(parameter, memberName));
                }
            }
        }

        if (replaced.Count == 0 && parameters.Count == 0 && constants.Count == 0)
        {
            return MethodHints.None;
        }

        return new MethodHints(replaced, parameters, constants.OrderBy(static hint => hint.Order).ToArray());
    }

    /// <summary>Returns the FluentExcluded reason of a property or event, or null when the member is not excluded.</summary>
    public static string? ReadExclusion(MemberInfo member)
    {
        foreach (var attribute in member.GetCustomAttributesData())
        {
            if (attribute.AttributeType.FullName == HintNames.EXCLUDED)
            {
                return ReadString(attribute, 0) ?? string.Empty;
            }
        }

        return null;
    }

    private static string? ReadString(CustomAttributeData attribute, int index)
    {
        if (attribute.ConstructorArguments.Count <= index)
        {
            return null;
        }

        return attribute.ConstructorArguments[index].Value as string;
    }

    private static ConstantHint? ReadConstant(CustomAttributeData attribute)
    {
        var arguments = attribute.ConstructorArguments;
        if (arguments.Count < 2)
        {
            return null;
        }

        var memberName = arguments[0].Value as string;
        if (memberName == null)
        {
            return null;
        }

        // An object-typed argument carries the encoded value as a nested typed argument.
        var valueArgument = arguments[1];
        if (valueArgument.Value is CustomAttributeTypedArgument nested)
        {
            valueArgument = nested;
        }

        var order = 0;
        if (arguments.Count > 2 && arguments[2].Value is int explicitOrder)
        {
            order = explicitOrder;
        }

        foreach (var named in attribute.NamedArguments)
        {
            if (named.MemberName == "Order" && named.TypedValue.Value is int namedOrder)
            {
                order = namedOrder;
            }
        }

        var valueType = valueArgument.Value == null ? null : valueArgument.ArgumentType;
        return new ConstantHint(memberName, valueArgument.Value, valueType, order);
    }
}
