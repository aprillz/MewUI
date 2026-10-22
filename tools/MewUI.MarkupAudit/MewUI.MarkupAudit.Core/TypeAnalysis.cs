using System.Reflection;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Applicability and conversion rules over metadata-only types; nothing here compares against runtime types.</summary>
internal static class TypeAnalysis
{
    private const string NULLABLE_DEFINITION = "System.Nullable`1";
    private const string IS_EXTERNAL_INIT = "System.Runtime.CompilerServices.IsExternalInit";
    private const string EXTENSION_ATTRIBUTE = "System.Runtime.CompilerServices.ExtensionAttribute";
    private const string PARAM_ARRAY_ATTRIBUTE = "System.ParamArrayAttribute";
    private const string PARAM_COLLECTION_ATTRIBUTE = "System.Runtime.CompilerServices.ParamCollectionAttribute";
    private const string DELEGATE_TYPE = "System.Delegate";
    private const string ENUMERABLE_INTERFACE = "System.Collections.IEnumerable";
    private const string STRING_TYPE = "System.String";
    private const string IMPLICIT_OPERATOR = "op_Implicit";

    private static readonly Dictionary<string, string[]> _numericWidening = new(StringComparer.Ordinal)
    {
        ["System.SByte"] = ["System.Int16", "System.Int32", "System.Int64", "System.Single", "System.Double", "System.Decimal"],
        ["System.Byte"] = ["System.Int16", "System.UInt16", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64", "System.Single", "System.Double", "System.Decimal"],
        ["System.Int16"] = ["System.Int32", "System.Int64", "System.Single", "System.Double", "System.Decimal"],
        ["System.UInt16"] = ["System.Int32", "System.UInt32", "System.Int64", "System.UInt64", "System.Single", "System.Double", "System.Decimal"],
        ["System.Int32"] = ["System.Int64", "System.Single", "System.Double", "System.Decimal"],
        ["System.UInt32"] = ["System.Int64", "System.UInt64", "System.Single", "System.Double", "System.Decimal"],
        ["System.Int64"] = ["System.Single", "System.Double", "System.Decimal"],
        ["System.UInt64"] = ["System.Single", "System.Double", "System.Decimal"],
        ["System.Char"] = ["System.UInt16", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64", "System.Single", "System.Double", "System.Decimal"],
        ["System.Single"] = ["System.Double"],
    };

    public static bool IsExtensionMethod(MethodInfo method) => HasAttribute(method.GetCustomAttributesData(), EXTENSION_ATTRIBUTE);

    public static bool IsParams(ParameterInfo parameter)
    {
        var attributes = parameter.GetCustomAttributesData();
        return HasAttribute(attributes, PARAM_ARRAY_ATTRIBUTE) || HasAttribute(attributes, PARAM_COLLECTION_ATTRIBUTE);
    }

    public static bool IsNullable(Type type)
    {
        return type.IsGenericType && !type.IsGenericTypeDefinition && type.GetGenericTypeDefinition().FullName == NULLABLE_DEFINITION;
    }

    public static Type UnwrapNullable(Type type) => IsNullable(type) ? type.GetGenericArguments()[0] : type;

    /// <summary>True when the type is, or derives from, the named base type.</summary>
    public static bool DerivesFrom(Type type, string baseFullName)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            if (current.FullName == baseFullName)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsDelegate(Type type) => !type.IsGenericParameter && DerivesFrom(type, DELEGATE_TYPE);

    public static bool IsCollectionLike(Type type)
    {
        if (type.FullName == STRING_TYPE || type.IsGenericParameter)
        {
            return false;
        }

        return type.IsArray || type.GetInterfaces().Any(static candidate => candidate.FullName == ENUMERABLE_INTERFACE);
    }

    public static bool IsInitOnly(PropertyInfo property)
    {
        var setter = property.SetMethod;
        if (setter == null)
        {
            return false;
        }

        try
        {
            return setter.ReturnParameter
                .GetRequiredCustomModifiers()
                .Any(static modifier => modifier.FullName == IS_EXTERNAL_INIT);
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public static Type? TryGetConcreteReceiverType(MethodInfo method)
    {
        var receiver = method.GetParameters()[0].ParameterType;
        if (receiver.IsGenericParameter)
        {
            return null;
        }

        return receiver.IsGenericType ? receiver.GetGenericTypeDefinition() : receiver;
    }

    /// <summary>Returns the type whose members an extension's hints refer to: the concrete receiver or the first class constraint of a generic receiver.</summary>
    public static Type? GetReceiverConstraintType(MethodInfo method)
    {
        var receiver = method.GetParameters()[0].ParameterType;
        if (!receiver.IsGenericParameter)
        {
            return receiver;
        }

        return receiver
            .GetGenericParameterConstraints()
            .FirstOrDefault(static constraint => constraint.IsClass && !constraint.IsGenericParameter);
    }

    public static bool IsExtensionApplicable(Type concreteType, MethodInfo method)
    {
        var receiver = method.GetParameters()[0].ParameterType;

        if (!method.IsGenericMethodDefinition)
        {
            return receiver.IsAssignableFrom(concreteType);
        }

        if (receiver.IsGenericParameter)
        {
            return SatisfiesConstraints(concreteType, receiver);
        }

        if (receiver.IsGenericType)
        {
            return MatchesOpenGenericReceiver(concreteType, receiver);
        }

        return false;
    }

    /// <summary>True when a property of the given type can be set through a value parameter of the given type, with nullable and user-defined implicit conversions tolerated.</summary>
    public static bool AcceptsPropertyValue(Type propertyType, Type valueType)
    {
        if (valueType.IsByRef)
        {
            valueType = valueType.GetElementType()!;
        }

        if (valueType.IsGenericParameter)
        {
            return SatisfiesValueConstraints(propertyType, valueType);
        }

        if (valueType.ContainsGenericParameters)
        {
            return valueType.IsGenericType &&
                   propertyType.IsGenericType &&
                   valueType.GetGenericTypeDefinition() == propertyType.GetGenericTypeDefinition();
        }

        var property = UnwrapNullable(propertyType);
        var value = UnwrapNullable(valueType);
        if (value.IsAssignableFrom(property) || IsNumericWidening(property, value))
        {
            return true;
        }

        return HasImplicitOperator(property, value) || HasImplicitOperator(value, property);
    }

    /// <summary>True when a value of <paramref name="from"/> converts implicitly to <paramref name="to"/> by identity, reference, boxing, nullable lifting, numeric widening, or a user-defined operator.</summary>
    public static bool IsImplicitlyConvertible(Type from, Type to)
    {
        if (from.IsByRef)
        {
            from = from.GetElementType()!;
        }

        if (from.IsGenericParameter || to.IsGenericParameter || from.ContainsGenericParameters || to.ContainsGenericParameters)
        {
            return TypeEquals(from, to) || (to.IsGenericParameter && SatisfiesValueConstraints(from, to));
        }

        var target = UnwrapNullable(to);
        var source = IsNullable(to) ? UnwrapNullable(from) : from;
        if (target.IsAssignableFrom(source) || IsNumericWidening(source, target))
        {
            return true;
        }

        return HasImplicitOperator(source, target) || HasImplicitOperator(target, source);
    }

    public static bool AcceptsEventHandler(EventInfo eventInfo, MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length < 2 || eventInfo.EventHandlerType == null)
        {
            return false;
        }

        var handlerType = parameters[1].ParameterType;
        return TypeEquals(handlerType, eventInfo.EventHandlerType) || handlerType.IsAssignableFrom(eventInfo.EventHandlerType);
    }

    public static bool HasAmbiguousSingleDelegateArgument(MethodInfo left, MethodInfo right)
    {
        var leftParameters = left.GetParameters();
        var rightParameters = right.GetParameters();

        if (leftParameters.Length != 2 || rightParameters.Length != 2)
        {
            return false;
        }

        var leftHandler = leftParameters[1].ParameterType;
        var rightHandler = rightParameters[1].ParameterType;
        if (TypeEquals(leftHandler, rightHandler))
        {
            return false;
        }

        var leftInvoke = leftHandler.IsGenericParameter ? null : leftHandler.GetMethod("Invoke");
        var rightInvoke = rightHandler.IsGenericParameter ? null : rightHandler.GetMethod("Invoke");
        if (leftInvoke == null || rightInvoke == null)
        {
            return false;
        }

        var leftLambdaParameters = leftInvoke.GetParameters();
        var rightLambdaParameters = rightInvoke.GetParameters();
        if (leftLambdaParameters.Length != rightLambdaParameters.Length ||
            !TypeEquals(leftInvoke.ReturnType, rightInvoke.ReturnType))
        {
            return false;
        }

        return leftLambdaParameters
            .Zip(rightLambdaParameters)
            .All(static pair =>
                pair.First.IsOut == pair.Second.IsOut &&
                pair.First.ParameterType.IsByRef == pair.Second.ParameterType.IsByRef);
    }

    /// <summary>Equality that survives metadata types materialized through different paths.</summary>
    public static bool TypeEquals(Type left, Type right)
    {
        if (ReferenceEquals(left, right) || left == right)
        {
            return true;
        }

        if (left.IsGenericParameter || right.IsGenericParameter)
        {
            return left.IsGenericParameter &&
                   right.IsGenericParameter &&
                   left.GenericParameterPosition == right.GenericParameterPosition &&
                   (left.DeclaringMethod == null) == (right.DeclaringMethod == null) &&
                   left.DeclaringType?.FullName == right.DeclaringType?.FullName &&
                   left.DeclaringMethod?.ToString() == right.DeclaringMethod?.ToString();
        }

        return TypeFormatter.FormatFullType(left) == TypeFormatter.FormatFullType(right) &&
               left.Assembly.GetName().Name == right.Assembly.GetName().Name;
    }

    private static bool SatisfiesValueConstraints(Type propertyType, Type genericParameter)
    {
        return genericParameter
            .GetGenericParameterConstraints()
            .All(constraint => constraint.ContainsGenericParameters || constraint.IsAssignableFrom(propertyType));
    }

    private static bool SatisfiesConstraints(Type concreteType, Type genericParameter)
    {
        var attributes = genericParameter.GenericParameterAttributes;
        var special = attributes & GenericParameterAttributes.SpecialConstraintMask;

        if ((special & GenericParameterAttributes.ReferenceTypeConstraint) != 0 && concreteType.IsValueType)
        {
            return false;
        }

        if ((special & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0 &&
            (!concreteType.IsValueType || IsNullable(concreteType)))
        {
            return false;
        }

        if ((special & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
            !concreteType.IsValueType &&
            concreteType.GetConstructor(Type.EmptyTypes) == null)
        {
            return false;
        }

        return genericParameter
            .GetGenericParameterConstraints()
            .All(constraint => SubstituteConstraint(constraint, genericParameter, concreteType).IsAssignableFrom(concreteType));
    }

    private static Type SubstituteConstraint(Type constraint, Type parameter, Type concreteType)
    {
        if (constraint == parameter)
        {
            return concreteType;
        }

        if (!constraint.IsGenericType)
        {
            return constraint;
        }

        var arguments = constraint
            .GetGenericArguments()
            .Select(argument => argument == parameter ? concreteType : argument)
            .ToArray();

        return constraint.GetGenericTypeDefinition().MakeGenericType(arguments);
    }

    private static bool MatchesOpenGenericReceiver(Type concreteType, Type receiver)
    {
        var receiverDefinition = receiver.GetGenericTypeDefinition();

        for (Type? current = concreteType; current != null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == receiverDefinition)
            {
                return true;
            }
        }

        return concreteType
            .GetInterfaces()
            .Any(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == receiverDefinition);
    }

    private static bool IsNumericWidening(Type from, Type to)
    {
        var fromName = from.FullName;
        var toName = to.FullName;
        return fromName != null &&
               toName != null &&
               _numericWidening.TryGetValue(fromName, out var targets) &&
               targets.Contains(toName, StringComparer.Ordinal);
    }

    private static bool HasImplicitOperator(Type from, Type to)
    {
        return DeclaresImplicitOperator(from, from, to) || DeclaresImplicitOperator(to, from, to);
    }

    private static bool DeclaresImplicitOperator(Type owner, Type from, Type to)
    {
        if (owner.IsGenericParameter || owner.IsArray || owner.IsByRef)
        {
            return false;
        }

        return owner
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Any(method =>
            {
                if (method.Name != IMPLICIT_OPERATOR)
                {
                    return false;
                }

                var parameters = method.GetParameters();
                return parameters.Length == 1 &&
                       parameters[0].ParameterType.IsAssignableFrom(from) &&
                       to.IsAssignableFrom(method.ReturnType);
            });
    }

    private static bool HasAttribute(IList<CustomAttributeData> attributes, string fullName)
    {
        return attributes.Any(attribute => attribute.AttributeType.FullName == fullName);
    }
}
