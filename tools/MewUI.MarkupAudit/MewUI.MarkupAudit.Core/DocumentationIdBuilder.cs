using System.Reflection;
using System.Text;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Builds XML documentation member ids the way the C# compiler writes them.</summary>
public static class DocumentationIdBuilder
{
    private const string IMPLICIT_OPERATOR = "op_Implicit";
    private const string EXPLICIT_OPERATOR = "op_Explicit";

    /// <summary>Returns the "T:" id of a type; nested types use dots and generic types keep their arity suffix.</summary>
    public static string ForType(Type type)
    {
        var builder = new StringBuilder("T:");
        AppendDeclaredType(builder, type);
        return builder.ToString();
    }

    /// <summary>Returns the "M:" id of a method, including generic arity, the encoded parameter list, and the conversion target of op_Implicit/op_Explicit.</summary>
    public static string ForMethod(MethodInfo method)
    {
        var builder = new StringBuilder("M:");
        AppendDeclaredType(builder, method.DeclaringType!);
        builder.Append('.').Append(EscapeMemberName(method.Name));

        if (method.IsGenericMethodDefinition)
        {
            builder.Append("``").Append(method.GetGenericArguments().Length);
        }

        AppendParameters(builder, method.GetParameters());

        if (method.Name is IMPLICIT_OPERATOR or EXPLICIT_OPERATOR)
        {
            builder.Append('~');
            AppendParameterType(builder, method.ReturnType);
        }

        return builder.ToString();
    }

    /// <summary>Returns the "M:" id of a constructor ("#ctor" or "#cctor" plus the encoded parameter list).</summary>
    public static string ForConstructor(ConstructorInfo constructor)
    {
        var builder = new StringBuilder("M:");
        AppendDeclaredType(builder, constructor.DeclaringType!);
        builder.Append('.').Append(constructor.IsStatic ? "#cctor" : "#ctor");
        AppendParameters(builder, constructor.GetParameters());
        return builder.ToString();
    }

    /// <summary>Returns the "P:" id of a property, including the encoded index parameter list of an indexer.</summary>
    public static string ForProperty(PropertyInfo property)
    {
        var builder = new StringBuilder("P:");
        AppendDeclaredType(builder, property.DeclaringType!);
        builder.Append('.').Append(EscapeMemberName(property.Name));
        AppendParameters(builder, property.GetIndexParameters());
        return builder.ToString();
    }

    /// <summary>Returns the "E:" id of an event.</summary>
    public static string ForEvent(EventInfo eventInfo)
    {
        var builder = new StringBuilder("E:");
        AppendDeclaredType(builder, eventInfo.DeclaringType!);
        builder.Append('.').Append(EscapeMemberName(eventInfo.Name));
        return builder.ToString();
    }

    /// <summary>Returns the "F:" id of a field.</summary>
    public static string ForField(FieldInfo field)
    {
        var builder = new StringBuilder("F:");
        AppendDeclaredType(builder, field.DeclaringType!);
        builder.Append('.').Append(EscapeMemberName(field.Name));
        return builder.ToString();
    }

    private static void AppendDeclaredType(StringBuilder builder, Type type)
    {
        builder.Append(type.FullName!.Replace('+', '.'));
    }

    private static void AppendParameters(StringBuilder builder, ParameterInfo[] parameters)
    {
        if (parameters.Length == 0)
        {
            return;
        }

        builder.Append('(');
        for (var index = 0; index < parameters.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            AppendParameterType(builder, parameters[index].ParameterType);
        }

        builder.Append(')');
    }

    // Explicit interface implementations carry dots in the member name, which the compiler writes as '#'.
    private static string EscapeMemberName(string name) => name.Replace('.', '#');

    private static void AppendParameterType(StringBuilder builder, Type type)
    {
        if (type.IsByRef)
        {
            AppendParameterType(builder, type.GetElementType()!);
            builder.Append('@');
        }
        else if (type.IsPointer)
        {
            AppendParameterType(builder, type.GetElementType()!);
            builder.Append('*');
        }
        else if (type.IsArray)
        {
            AppendParameterType(builder, type.GetElementType()!);
            var rank = type.GetArrayRank();
            if (rank == 1)
            {
                builder.Append("[]");
            }
            else
            {
                builder.Append('[').Append(string.Join(",", Enumerable.Repeat("0:", rank))).Append(']');
            }
        }
        else if (type.IsGenericParameter)
        {
            builder.Append(type.DeclaringMethod != null ? "``" : "`").Append(type.GenericParameterPosition);
        }
        else if (type.IsGenericType)
        {
            AppendConstructedGeneric(builder, type);
        }
        else
        {
            builder.Append(type.FullName!.Replace('+', '.'));
        }
    }

    private static void AppendConstructedGeneric(StringBuilder builder, Type type)
    {
        var arguments = type.GetGenericArguments();
        var segments = type.GetGenericTypeDefinition().FullName!.Split('+');
        var consumed = 0;

        // Each nesting level owns the arguments its arity suffix declares, so the suffix is replaced level by level.
        for (var index = 0; index < segments.Length; index++)
        {
            if (index > 0)
            {
                builder.Append('.');
            }

            var segment = segments[index];
            var tick = segment.IndexOf('`');
            if (tick < 0)
            {
                builder.Append(segment);
                continue;
            }

            var arity = int.Parse(segment[(tick + 1)..]);
            builder.Append(segment[..tick]).Append('{');
            for (var argumentIndex = 0; argumentIndex < arity; argumentIndex++)
            {
                if (argumentIndex > 0)
                {
                    builder.Append(',');
                }

                AppendParameterType(builder, arguments[consumed + argumentIndex]);
            }

            builder.Append('}');
            consumed += arity;
        }
    }
}
