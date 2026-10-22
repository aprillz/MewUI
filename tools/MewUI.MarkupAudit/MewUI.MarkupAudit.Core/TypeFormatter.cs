using System.Reflection;
using System.Text;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>C#-like display names for metadata types and extension signatures.</summary>
public static class TypeFormatter
{
    private const string VALUE_TYPE = "System.ValueType";

    /// <summary>Short display name such as <c>GridViewColumn&lt;TItem&gt;</c> or <c>CursorType?</c>.</summary>
    public static string FormatType(Type type) => Format(type, qualified: false);

    /// <summary>Namespace-qualified display name such as <c>Aprillz.MewUI.Controls.GridViewColumn&lt;TItem&gt;</c>.</summary>
    public static string FormatFullType(Type type) => Format(type, qualified: true);

    /// <summary>Signature in declaration form, including the receiver, parameter modifiers, defaults, and generic constraints.</summary>
    public static string FormatSignature(MethodInfo method)
    {
        var builder = new StringBuilder();
        builder.Append(FormatType(method.ReturnType)).Append(' ').Append(method.Name);

        if (method.IsGenericMethodDefinition)
        {
            builder.Append('<').Append(string.Join(", ", method.GetGenericArguments().Select(static argument => argument.Name))).Append('>');
        }

        builder.Append('(');
        var parameters = method.GetParameters();
        for (var index = 0; index < parameters.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            if (index == 0)
            {
                builder.Append("this ");
            }

            AppendParameter(builder, parameters[index]);
        }

        builder.Append(')');

        if (method.IsGenericMethodDefinition)
        {
            foreach (var argument in method.GetGenericArguments())
            {
                var constraints = FormatConstraints(argument);
                if (constraints.Count > 0)
                {
                    builder.Append(" where ").Append(argument.Name).Append(" : ").Append(string.Join(", ", constraints));
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>Constraint list of a generic parameter in C# order: special constraints, then type constraints, then new().</summary>
    public static IReadOnlyList<string> FormatConstraints(Type genericParameter)
    {
        var constraints = new List<string>();
        var special = genericParameter.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask;

        if ((special & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
        {
            constraints.Add("class");
        }

        if ((special & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
        {
            constraints.Add("struct");
        }

        foreach (var constraint in genericParameter.GetGenericParameterConstraints())
        {
            if (constraint.FullName == VALUE_TYPE)
            {
                continue;
            }

            constraints.Add(FormatFullType(constraint));
        }

        if ((special & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
            (special & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
        {
            constraints.Add("new()");
        }

        return constraints;
    }

    /// <summary>Returns <c>ref</c>, <c>out</c>, or <c>in</c> for a by-reference parameter, else null.</summary>
    public static string? FormatModifier(ParameterInfo parameter)
    {
        if (!parameter.ParameterType.IsByRef)
        {
            return null;
        }

        return parameter.IsOut ? "out" : parameter.IsIn ? "in" : "ref";
    }

    /// <summary>Default value of an optional parameter in C# form; enum constants are printed as <c>Type.Member</c>.</summary>
    public static string FormatDefaultValue(ParameterInfo parameter)
    {
        var enumType = TypeAnalysis.UnwrapNullable(parameter.ParameterType);
        if (!enumType.IsEnum)
        {
            return FormatDefault(parameter);
        }

        object? value;
        try
        {
            value = parameter.RawDefaultValue;
        }
        catch (NotSupportedException)
        {
            return "default";
        }

        if (value == null || value == DBNull.Value)
        {
            return TypeAnalysis.IsNullable(parameter.ParameterType) ? "null" : "default";
        }

        var field = enumType
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(candidate => Equals(candidate.GetRawConstantValue(), value));
        return field == null ? FormatDefault(parameter) : $"{enumType.Name}.{field.Name}";
    }

    private static void AppendParameter(StringBuilder builder, ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (type.IsByRef)
        {
            builder.Append(parameter.IsOut ? "out " : parameter.IsIn ? "in " : "ref ");
            type = type.GetElementType()!;
        }

        if (TypeAnalysis.IsParams(parameter))
        {
            builder.Append("params ");
        }

        builder.Append(FormatType(type)).Append(' ').Append(parameter.Name);

        if (parameter.IsOptional)
        {
            builder.Append(" = ").Append(FormatDefault(parameter));
        }
    }

    private static string FormatDefault(ParameterInfo parameter)
    {
        object? value;
        try
        {
            value = parameter.RawDefaultValue;
        }
        catch (NotSupportedException)
        {
            return "default";
        }

        if (value == null || value == DBNull.Value)
        {
            return parameter.ParameterType.IsValueType && !TypeAnalysis.IsNullable(parameter.ParameterType) ? "default" : "null";
        }

        if (value is string text)
        {
            return $"\"{text}\"";
        }

        if (value is bool flag)
        {
            return flag ? "true" : "false";
        }

        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "default";
    }

    private static string Format(Type type, bool qualified)
    {
        if (type.IsByRef)
        {
            return $"{Format(type.GetElementType()!, qualified)}&";
        }

        if (type.IsArray)
        {
            return $"{Format(type.GetElementType()!, qualified)}[]";
        }

        if (type.IsPointer)
        {
            return $"{Format(type.GetElementType()!, qualified)}*";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (TypeAnalysis.IsNullable(type))
        {
            return $"{Format(type.GetGenericArguments()[0], qualified)}?";
        }

        var name = type.IsNested ? $"{Format(type.DeclaringType!, qualified)}.{type.Name}" : type.Name;
        if (qualified && !type.IsNested && !string.IsNullOrEmpty(type.Namespace))
        {
            name = $"{type.Namespace}.{name}";
        }

        if (!type.IsGenericType)
        {
            return name;
        }

        var tick = name.LastIndexOf('`');
        if (tick >= 0)
        {
            name = name[..tick];
        }

        // Nested generic types carry the outer arguments too; only the arguments this level declares are printed here.
        var arguments = type.GetGenericArguments();
        var outerCount = type.IsNested && type.DeclaringType!.IsGenericType ? type.DeclaringType.GetGenericArguments().Length : 0;
        var ownArguments = arguments.Skip(outerCount).Select(argument => Format(argument, qualified));
        return outerCount == arguments.Length ? name : $"{name}<{string.Join(", ", ownArguments)}>";
    }
}
