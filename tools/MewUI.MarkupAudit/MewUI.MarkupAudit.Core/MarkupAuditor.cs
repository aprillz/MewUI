using System.Reflection;

namespace Aprillz.MewUI.MarkupAudit;

/// <summary>Inventories fluent extension methods and audits every concrete UI receiver's settable members against them.</summary>
public static class MarkupAuditor
{
    /// <summary>Runs one audit over the assemblies named in the options.</summary>
    public static AuditResult Run(AuditOptions options)
    {
        using var assemblies = AuditAssemblySet.Load(options.AssemblyPaths);
        return new AuditSession(assemblies, options).Run();
    }
}

internal sealed class ExtensionMethod
{
    public required MethodInfo Method { get; init; }

    public required MethodHints Hints { get; init; }

    public Type? ConstraintType { get; init; }

    public IReadOnlyList<Type> ApplicableReceivers { get; set; } = Array.Empty<Type>();

    public string? Summary { get; set; }

    public string DisplayName => $"{Method.DeclaringType!.Name}.{Method.Name}";

    public string AssemblyName => Method.DeclaringType!.Assembly.GetName().Name ?? string.Empty;

    /// <summary>Types whose members name-based rules are resolved against: the constraint type, else every applicable receiver.</summary>
    public IReadOnlyList<Type> LookupTypes => ConstraintType != null ? new Type[] { ConstraintType } : ApplicableReceivers;

    public ParameterInfo[] ValueParameters => Method.GetParameters().Skip(1).ToArray();
}

internal sealed class AuditSession
{
    private const string ELEMENT_TYPE = "Aprillz.MewUI.Controls.Element";
    private const string ELEMENT_NAMESPACE = "Aprillz.MewUI.Controls";
    private const string ON_PREFIX = "On";
    private const string BIND_PREFIX = "Bind";
    private const string REF_NAME = "Ref";
    private const string WITH_PREFIX = "With";
    private const string SET_PREFIX = "Set";
    private const string OBSOLETE_ATTRIBUTE = "System.ObsoleteAttribute";

    private readonly AuditAssemblySet _assemblies;
    private readonly AuditOptions _options;
    private readonly List<string> _warnings = new();
    private readonly List<Finding> _findings = new();
    private readonly Dictionary<Type, List<ExtensionMethod>> _receiverExtensions = new();

    public AuditSession(AuditAssemblySet assemblies, AuditOptions options)
    {
        _assemblies = assemblies;
        _options = options;
    }

    public AuditResult Run()
    {
        var documentation = LoadDocumentation();
        var extensions = CollectExtensions();
        var receivers = CollectReceivers(extensions);
        ResolveApplicability(extensions, receivers);

        foreach (var extension in extensions)
        {
            documentation.TryGetValue(extension.Method.DeclaringType!.Assembly, out var index);
            extension.Summary = index?.FindSummary(DocumentationIdBuilder.ForMethod(extension.Method));
        }

        var inventory = extensions
            .Select(TryBuildRecord)
            .OfType<ExtensionMethodRecord>()
            .OrderBy(static record => record.Assembly, StringComparer.Ordinal)
            .ThenBy(static record => record.DeclaringType, StringComparer.Ordinal)
            .ThenBy(static record => record.Name, StringComparer.Ordinal)
            .ThenBy(static record => record.Signature, StringComparer.Ordinal)
            .ToArray();

        var properties = new List<MemberLedgerEntry>();
        var events = new List<MemberLedgerEntry>();
        foreach (var receiver in receivers)
        {
            AuditReceiver(receiver, properties, events);
        }

        var ambiguous = FindAmbiguousEventExtensions(receivers);
        foreach (var extension in extensions)
        {
            if (!IsUiExtension(extension))
            {
                continue;
            }

            try
            {
                CheckExtension(extension);
            }
            catch (Exception exception) when (IsResolutionFailure(exception))
            {
                _warnings.Add($"Skipped checks of {extension.DisplayName}: {exception.Message}");
            }
        }

        var orderedProperties = OrderLedger(properties);
        var orderedEvents = OrderLedger(events);
        var orderedFindings = _findings
            .Distinct()
            .OrderBy(static finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Assembly, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Subject, StringComparer.Ordinal)
            .ThenBy(static finding => finding.Message, StringComparer.Ordinal)
            .ToArray();

        var counts = new AuditCounts(
            receivers.Count,
            orderedProperties.Length,
            orderedEvents.Length,
            orderedProperties.Count(static entry => entry.IsMissing),
            orderedEvents.Count(static entry => entry.IsMissing),
            ambiguous.Length,
            inventory.Length,
            inventory.Count(static record => record.Summary == null),
            CountBy(orderedProperties, static entry => entry.Disposition),
            CountBy(orderedEvents, static entry => entry.Disposition),
            CountBy(inventory, static record => record.Kind),
            CountBy(orderedFindings, static finding => finding.Code));

        return new AuditResult(
            _assemblies.Paths,
            receivers.Select(TypeFormatter.FormatFullType).ToArray(),
            inventory,
            orderedProperties,
            orderedEvents,
            ambiguous,
            orderedFindings,
            counts,
            _warnings.ToArray());
    }

    private Dictionary<Assembly, XmlDocumentationIndex?> LoadDocumentation()
    {
        var documentation = new Dictionary<Assembly, XmlDocumentationIndex?>();
        for (var index = 0; index < _assemblies.Assemblies.Count; index++)
        {
            var path = _assemblies.Paths[index];
            var loaded = XmlDocumentationIndex.TryLoad(path);
            if (loaded == null)
            {
                _warnings.Add($"No XML documentation file next to {path}; summaries of its extensions are recorded as missing.");
            }

            documentation[_assemblies.Assemblies[index]] = loaded;
        }

        return documentation;
    }

    private List<ExtensionMethod> CollectExtensions()
    {
        var extensions = new List<ExtensionMethod>();
        foreach (var assembly in _assemblies.Assemblies)
        {
            foreach (var type in GetTypes(assembly))
            {
                if (!(type.IsPublic && type.IsClass && type.IsAbstract && type.IsSealed))
                {
                    continue;
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (method.GetParameters().Length == 0 || !TypeAnalysis.IsExtensionMethod(method))
                    {
                        continue;
                    }

                    extensions.Add(new ExtensionMethod
                    {
                        Method = method,
                        Hints = HintReader.Read(method),
                        ConstraintType = TypeAnalysis.GetReceiverConstraintType(method),
                    });
                }
            }
        }

        return extensions;
    }

    private List<Type> CollectReceivers(List<ExtensionMethod> extensions)
    {
        var explicitReceivers = extensions
            .Select(static extension => TypeAnalysis.TryGetConcreteReceiverType(extension.Method))
            .OfType<Type>()
            .ToHashSet();
        var excluded = new HashSet<string>(_options.ExcludedTypes, StringComparer.Ordinal);
        var receivers = new List<Type>();

        foreach (var assembly in _assemblies.Assemblies)
        {
            foreach (var type in GetTypes(assembly))
            {
                if (!type.IsPublic || !type.IsClass || type.IsAbstract || excluded.Contains(type.FullName ?? string.Empty))
                {
                    continue;
                }

                bool derivesFromElement;
                try
                {
                    derivesFromElement = TypeAnalysis.DerivesFrom(type, ELEMENT_TYPE);
                }
                catch (Exception exception) when (IsResolutionFailure(exception))
                {
                    _warnings.Add($"Skipped {type.FullName}: {exception.Message}");
                    continue;
                }

                if (derivesFromElement || (type.Namespace == ELEMENT_NAMESPACE && explicitReceivers.Contains(type)))
                {
                    receivers.Add(type);
                }
            }
        }

        return receivers
            .OrderBy(TypeFormatter.FormatFullType, StringComparer.Ordinal)
            .ToList();
    }

    private void ResolveApplicability(List<ExtensionMethod> extensions, List<Type> receivers)
    {
        foreach (var receiver in receivers)
        {
            _receiverExtensions[receiver] = new List<ExtensionMethod>();
        }

        foreach (var extension in extensions)
        {
            var applicable = new List<Type>();
            try
            {
                foreach (var receiver in receivers)
                {
                    if (TypeAnalysis.IsExtensionApplicable(receiver, extension.Method))
                    {
                        applicable.Add(receiver);
                    }
                }
            }
            catch (Exception exception) when (IsResolutionFailure(exception))
            {
                _warnings.Add($"Applicability of {extension.DisplayName} is unknown: {exception.Message}");
                applicable.Clear();
            }

            foreach (var receiver in applicable)
            {
                _receiverExtensions[receiver].Add(extension);
            }

            extension.ApplicableReceivers = applicable;
        }
    }

    /// <summary>Extension-side rules apply to fluent markup only: the receiver must reach an audited receiver or be an Element type itself.</summary>
    private static bool IsUiExtension(ExtensionMethod extension)
    {
        return extension.ApplicableReceivers.Count > 0 ||
               (extension.ConstraintType != null && TypeAnalysis.DerivesFrom(extension.ConstraintType, ELEMENT_TYPE));
    }

    private static bool IsResolutionFailure(Exception exception)
    {
        return exception is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException;
    }

    private IEnumerable<Type> GetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            _warnings.Add($"Some types of {assembly.GetName().Name} could not be loaded: {exception.Message}");
            return exception.Types.OfType<Type>();
        }
    }

    private ExtensionMethodRecord? TryBuildRecord(ExtensionMethod extension)
    {
        try
        {
            return BuildRecord(extension);
        }
        catch (Exception exception) when (IsResolutionFailure(exception))
        {
            _warnings.Add($"Skipped inventory of {extension.DisplayName}: {exception.Message}");
            return null;
        }
    }

    private ExtensionMethodRecord BuildRecord(ExtensionMethod extension)
    {
        var method = extension.Method;
        var receiverParameter = method.GetParameters()[0].ParameterType;
        var constraintType = extension.ConstraintType == null ? null : TypeFormatter.FormatFullType(extension.ConstraintType);
        var baseTypes = BaseChain(extension.ConstraintType);
        var receiver = receiverParameter.IsGenericParameter
            ? new ReceiverConstraintRecord(receiverParameter.Name, true, TypeFormatter.FormatConstraints(receiverParameter), constraintType, baseTypes)
            : new ReceiverConstraintRecord(TypeFormatter.FormatFullType(receiverParameter), false, Array.Empty<string>(), constraintType, baseTypes);

        var parameters = extension.ValueParameters
            .Select(parameter => new ValueParameterRecord(
                parameter.Name ?? string.Empty,
                TypeFormatter.FormatType(parameter.ParameterType),
                parameter.IsOptional,
                TypeAnalysis.IsParams(parameter),
                extension.Hints.Parameters.FirstOrDefault(hint => hint.Parameter == parameter)?.MemberName,
                TypeFormatter.FormatModifier(parameter),
                parameter.IsOptional ? TypeFormatter.FormatDefaultValue(parameter) : null))
            .ToArray();

        var (kind, targets) = Classify(extension);
        var constants = extension.Hints.Constants
            .Select(hint => new ConstantHintRecord(hint.MemberName, FormatConstant(hint), hint.ValueType?.FullName, hint.Order))
            .ToArray();

        return new ExtensionMethodRecord(
            extension.AssemblyName,
            TypeFormatter.FormatFullType(method.DeclaringType!),
            method.Name,
            TypeFormatter.FormatSignature(method),
            DocumentationIdBuilder.ForMethod(method),
            TypeFormatter.FormatType(method.ReturnType),
            receiver,
            parameters,
            kind,
            targets,
            constants,
            extension.Hints.ReplacedMembers,
            extension.Summary,
            ReadObsolete(method),
            extension.ApplicableReceivers.Select(TypeFormatter.FormatFullType).ToArray());
    }

    private static string? ReadObsolete(MethodInfo method)
    {
        foreach (var attribute in method.GetCustomAttributesData())
        {
            if (attribute.AttributeType.FullName == OBSOLETE_ATTRIBUTE)
            {
                return attribute.ConstructorArguments.Count > 0 ? attribute.ConstructorArguments[0].Value as string ?? string.Empty : string.Empty;
            }
        }

        return null;
    }

    private static (string Kind, IReadOnlyList<string> Targets) Classify(ExtensionMethod extension)
    {
        var method = extension.Method;
        var hints = extension.Hints;
        var valueParameters = extension.ValueParameters;
        var name = method.Name;

        if (hints.ReplacedMembers.Count > 0)
        {
            return (ExtensionKinds.COLLECTION, hints.ReplacedMembers);
        }

        if (hints.Parameters.Count == 1 && hints.Constants.Count == 0 && valueParameters.Length == 1 &&
            TypeAnalysis.IsDelegate(valueParameters[0].ParameterType) &&
            !HasProperty(extension, hints.Parameters[0].MemberName) &&
            HasEvent(extension, hints.Parameters[0].MemberName))
        {
            return (ExtensionKinds.EVENT, new[] { hints.Parameters[0].MemberName });
        }

        if (hints.Parameters.Count > 0 || hints.Constants.Count > 0)
        {
            var members = hints.Parameters.Select(static hint => hint.MemberName)
                .Concat(hints.Constants.Select(static hint => hint.MemberName))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (members.Length >= 2 || valueParameters.Length >= 2)
            {
                return (ExtensionKinds.COMPOSITE, members);
            }

            if (valueParameters.Length == 0)
            {
                return (ExtensionKinds.SHORTHAND, members);
            }

            return members[0] == name ? (ExtensionKinds.SETTER, members) : (ExtensionKinds.ALIAS, members);
        }

        if (IsSingleValueExtension(method) && HasProperty(extension, name))
        {
            return (ExtensionKinds.SETTER, new[] { name });
        }

        if (HasPrefix(name, ON_PREFIX) && valueParameters.Length == 1 && TypeAnalysis.IsDelegate(valueParameters[0].ParameterType))
        {
            return (ExtensionKinds.EVENT, new[] { name[ON_PREFIX.Length..] });
        }

        if (HasPrefix(name, BIND_PREFIX))
        {
            var target = name[BIND_PREFIX.Length..];
            return (ExtensionKinds.BIND, HasProperty(extension, target) ? new[] { target } : Array.Empty<string>());
        }

        if (valueParameters.Length > 0 && TypeAnalysis.IsParams(valueParameters[^1]) && HasCollectionMember(extension, name))
        {
            return (ExtensionKinds.COLLECTION, new[] { name });
        }

        return (ExtensionKinds.OTHER, Array.Empty<string>());
    }

    private void AuditReceiver(Type receiver, List<MemberLedgerEntry> properties, List<MemberLedgerEntry> events)
    {
        var applicable = _receiverExtensions[receiver];
        var receiverName = TypeFormatter.FormatFullType(receiver);
        var receiverDisplay = TypeFormatter.FormatType(receiver);

        PropertyInfo[] declaredProperties;
        EventInfo[] declaredEvents;
        try
        {
            declaredProperties = receiver.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            declaredEvents = receiver.GetEvents(BindingFlags.Public | BindingFlags.Instance);
        }
        catch (Exception exception) when (IsResolutionFailure(exception))
        {
            _warnings.Add($"Skipped members of {receiverName}: {exception.Message}");
            return;
        }

        var settable = declaredProperties
            .Where(static property =>
                property.GetIndexParameters().Length == 0 &&
                property.SetMethod?.IsPublic == true &&
                !TypeAnalysis.IsInitOnly(property))
            .GroupBy(static property => property.Name, StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(static property => Depth(property.DeclaringType!)).First())
            .OrderBy(static property => property.Name, StringComparer.Ordinal);

        foreach (var property in settable)
        {
            try
            {
                properties.Add(AuditProperty(receiver, receiverName, receiverDisplay, property, applicable));
            }
            catch (Exception exception) when (IsResolutionFailure(exception))
            {
                _warnings.Add($"Skipped {receiverName}.{property.Name}: {exception.Message}");
            }
        }

        var visibleEvents = declaredEvents
            .GroupBy(static eventInfo => eventInfo.Name, StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(static eventInfo => Depth(eventInfo.DeclaringType!)).First())
            .OrderBy(static eventInfo => eventInfo.Name, StringComparer.Ordinal);

        foreach (var eventInfo in visibleEvents)
        {
            try
            {
                events.Add(AuditEvent(receiverName, receiverDisplay, eventInfo, applicable));
            }
            catch (Exception exception) when (IsResolutionFailure(exception))
            {
                _warnings.Add($"Skipped {receiverName}.{eventInfo.Name}: {exception.Message}");
            }
        }
    }

    private MemberLedgerEntry AuditProperty(Type receiver, string receiverName, string receiverDisplay, PropertyInfo property, List<ExtensionMethod> applicable)
    {
        string[] conventional;
        string[] multiArgument;
        string[] annotated;
        if (TypeAnalysis.IsDelegate(property.PropertyType))
        {
            // A delegate-typed property is invocable, so x.Name(value) binds to the property and a same-name extension is never callable.
            conventional = Array.Empty<string>();
            multiArgument = Array.Empty<string>();
            annotated = Names(applicable.Where(extension => extension.Hints.Parameters.Any(hint => hint.MemberName == property.Name)));
        }
        else
        {
            var sameName = applicable.Where(extension => extension.Method.Name == property.Name).ToArray();
            conventional = Names(sameName.Where(extension =>
                IsSingleValueExtension(extension.Method) &&
                TypeAnalysis.AcceptsPropertyValue(property.PropertyType, extension.Method.GetParameters()[1].ParameterType)));
            multiArgument = Names(sameName.Where(extension =>
                !IsSingleValueExtension(extension.Method) &&
                extension.Method.GetParameters().Length >= 2 &&
                TypeAnalysis.AcceptsPropertyValue(property.PropertyType, extension.Method.GetParameters()[1].ParameterType)));
            annotated = Names(applicable.Where(extension => extension.Hints.Names(property.Name)));
        }

        var exclusion = HintReader.ReadExclusion(property);

        return BuildEntry(receiverName, receiverDisplay, property.Name, MemberKinds.PROPERTY,
            TypeFormatter.FormatType(property.PropertyType), property.DeclaringType!,
            conventional, Dispositions.CONVENTIONAL, annotated, multiArgument, exclusion);
    }

    private MemberLedgerEntry AuditEvent(string receiverName, string receiverDisplay, EventInfo eventInfo, List<ExtensionMethod> applicable)
    {
        var expectedName = ON_PREFIX + eventInfo.Name;
        var onPrefix = Names(applicable.Where(extension =>
            extension.Method.Name == expectedName &&
            TypeAnalysis.AcceptsEventHandler(eventInfo, extension.Method)));
        var annotated = Names(applicable.Where(extension => extension.Hints.Names(eventInfo.Name)));
        var exclusion = HintReader.ReadExclusion(eventInfo);
        var handlerType = eventInfo.EventHandlerType == null ? "?" : TypeFormatter.FormatType(eventInfo.EventHandlerType);

        return BuildEntry(receiverName, receiverDisplay, eventInfo.Name, MemberKinds.EVENT,
            handlerType, eventInfo.DeclaringType!,
            onPrefix, Dispositions.ON_PREFIX, annotated, Array.Empty<string>(), exclusion);
    }

    private MemberLedgerEntry BuildEntry(
        string receiverName, string receiverDisplay, string memberName, string memberKind, string memberType, Type declaringType,
        string[] convention, string conventionDisposition, string[] annotated, string[] multiArgument, string? exclusion)
    {
        string disposition;
        string[] extensions;
        if (convention.Length > 0)
        {
            disposition = conventionDisposition;
            extensions = convention;
        }
        else if (annotated.Length > 0)
        {
            disposition = Dispositions.ANNOTATED;
            extensions = annotated;
        }
        else if (exclusion != null)
        {
            disposition = Dispositions.EXCLUDED;
            extensions = Array.Empty<string>();
        }
        else
        {
            disposition = Dispositions.MISSING;
            extensions = Array.Empty<string>();
        }

        if (exclusion != null && (convention.Length > 0 || annotated.Length > 0))
        {
            _findings.Add(new Finding(
                FindingCodes.EXCLUDED_MEMBER_COVERED,
                declaringType.Assembly.GetName().Name ?? string.Empty,
                $"{TypeFormatter.FormatType(declaringType)}.{memberName}",
                $"FluentExcluded(\"{exclusion}\") but covered by {string.Join(", ", convention.Concat(annotated))}"));
        }

        return new MemberLedgerEntry(
            receiverName,
            receiverDisplay,
            memberName,
            memberKind,
            memberType,
            TypeFormatter.FormatFullType(declaringType),
            disposition,
            extensions,
            multiArgument,
            exclusion);
    }

    private AmbiguousEventExtensionRecord[] FindAmbiguousEventExtensions(List<Type> receivers)
    {
        var rows = new Dictionary<string, AmbiguousEventExtensionRecord>(StringComparer.Ordinal);
        foreach (var receiver in receivers)
        {
            var methods = _receiverExtensions[receiver]
                .Select(static extension => extension.Method)
                .Where(static method => method.Name.StartsWith(ON_PREFIX, StringComparison.Ordinal))
                .ToArray();

            for (var leftIndex = 0; leftIndex < methods.Length; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < methods.Length; rightIndex++)
                {
                    var left = methods[leftIndex];
                    var right = methods[rightIndex];
                    if (left.Name != right.Name || !TypeAnalysis.HasAmbiguousSingleDelegateArgument(left, right))
                    {
                        continue;
                    }

                    var ordered = new[] { left, right }
                        .OrderBy(static method => method.ToString(), StringComparer.Ordinal)
                        .ToArray();
                    var key = $"{receiver.FullName}|{string.Join("|", ordered.Select(static method => $"{method.DeclaringType!.FullName}.{method}"))}";
                    rows.TryAdd(key, new AmbiguousEventExtensionRecord(
                        TypeFormatter.FormatFullType(receiver),
                        TypeFormatter.FormatType(receiver),
                        left.Name,
                        TypeFormatter.FormatType(left.GetParameters()[1].ParameterType),
                        TypeFormatter.FormatType(right.GetParameters()[1].ParameterType),
                        TypeFormatter.FormatSignature(left),
                        TypeFormatter.FormatSignature(right)));
                }
            }
        }

        var result = rows.Values
            .OrderBy(static row => row.Receiver, StringComparer.Ordinal)
            .ThenBy(static row => row.Extension, StringComparer.Ordinal)
            .ThenBy(static row => row.FirstSignature, StringComparer.Ordinal)
            .ToArray();

        foreach (var row in result)
        {
            _findings.Add(new Finding(
                FindingCodes.AMBIGUOUS_EVENT_OVERLOAD,
                string.Empty,
                $"{row.ReceiverDisplay}.{row.Extension}",
                $"{row.FirstHandler} and {row.SecondHandler} accept the same lambda shape"));
        }

        return result;
    }

    private void CheckExtension(ExtensionMethod extension)
    {
        var method = extension.Method;
        var name = method.Name;
        var receiverType = method.GetParameters()[0].ParameterType;

        // Kind 'other' has no target member, so it is a helper rather than a chaining setter and is exempt from the chaining-shape checks.
        var targetsMember = Classify(extension).Kind != ExtensionKinds.OTHER;

        if (targetsMember && !HasPrefix(name, BIND_PREFIX) && name != REF_NAME && !TypeAnalysis.TypeEquals(method.ReturnType, receiverType))
        {
            Report(extension, FindingCodes.RETURN_TYPE,
                $"returns {TypeFormatter.FormatType(method.ReturnType)} instead of the receiver {TypeFormatter.FormatType(receiverType)}");
        }

        foreach (var hint in extension.Hints.Parameters)
        {
            var parameterType = hint.Parameter.ParameterType;
            var property = FindProperty(extension, hint.MemberName);
            var subscribedEvent = property == null && TypeAnalysis.IsDelegate(parameterType) ? FindEvent(extension, hint.MemberName) : null;
            if (property != null)
            {
                if (!TypeAnalysis.IsImplicitlyConvertible(parameterType, property.PropertyType))
                {
                    Report(extension, FindingCodes.HINT_PARAMETER_TYPE,
                        $"parameter '{hint.Parameter.Name}' of type {TypeFormatter.FormatType(parameterType)} does not convert implicitly to {hint.MemberName} of type {TypeFormatter.FormatType(property.PropertyType)}");
                }
            }
            else if (subscribedEvent != null)
            {
                if (subscribedEvent.EventHandlerType == null || !TypeAnalysis.IsImplicitlyConvertible(parameterType, subscribedEvent.EventHandlerType))
                {
                    Report(extension, FindingCodes.HINT_PARAMETER_TYPE,
                        $"handler '{hint.Parameter.Name}' of type {TypeFormatter.FormatType(parameterType)} does not convert implicitly to event {hint.MemberName} of type {(subscribedEvent.EventHandlerType == null ? "?" : TypeFormatter.FormatType(subscribedEvent.EventHandlerType))}");
                }
            }
            else
            {
                Report(extension, FindingCodes.HINT_MEMBER_MISSING,
                    $"FluentSetsMember(\"{hint.MemberName}\") on parameter '{hint.Parameter.Name}' names no public settable property of the receiver, nor a public event for a delegate parameter");
            }
        }

        foreach (var hint in extension.Hints.Constants)
        {
            var property = FindProperty(extension, hint.MemberName);
            if (property == null)
            {
                Report(extension, FindingCodes.HINT_MEMBER_MISSING,
                    $"FluentSetsConstant(\"{hint.MemberName}\") names no public settable property of the receiver");
            }
            else if (!ConstantMatches(hint, property.PropertyType))
            {
                Report(extension, FindingCodes.HINT_CONSTANT_TYPE,
                    $"constant {FormatConstant(hint)} of type {hint.ValueType?.FullName ?? "null"} does not match {hint.MemberName} of type {TypeFormatter.FormatType(property.PropertyType)}");
            }
        }

        var orders = extension.Hints.Constants.Select(static hint => hint.Order).OrderBy(static order => order).ToArray();
        if (!orders.SequenceEqual(Enumerable.Range(0, orders.Length)))
        {
            Report(extension, FindingCodes.HINT_ORDER,
                $"FluentSetsConstant orders [{string.Join(", ", orders)}] are not contiguous from 0");
        }

        foreach (var memberName in extension.Hints.ReplacedMembers)
        {
            if (!HasMember(extension, memberName))
            {
                Report(extension, FindingCodes.HINT_MEMBER_MISSING,
                    $"FluentReplacesMember(\"{memberName}\") names no public instance member of the receiver");
            }
        }

        if (extension.Summary == null)
        {
            Report(extension, FindingCodes.MISSING_SUMMARY, "public extension has no XML <summary>");
        }

        // 'With' is the convention for delegate-typed properties, whose same-name extension would be shadowed by the invocable member.
        var withPrefixAllowed = HasPrefix(name, WITH_PREFIX) && extension.Hints.Parameters.Any(hint => FindProperty(extension, hint.MemberName) is PropertyInfo target && TypeAnalysis.IsDelegate(target.PropertyType));
        if (targetsMember && ((HasPrefix(name, WITH_PREFIX) && !withPrefixAllowed) || HasPrefix(name, SET_PREFIX)))
        {
            Report(extension, FindingCodes.NAMING_PREFIX, $"uses the '{(HasPrefix(name, WITH_PREFIX) ? WITH_PREFIX : SET_PREFIX)}' prefix");
        }

        var valueParameters = extension.ValueParameters;
        if (!name.StartsWith(ON_PREFIX, StringComparison.Ordinal) &&
            valueParameters.Length == 1 &&
            TypeAnalysis.IsDelegate(valueParameters[0].ParameterType) &&
            HasEvent(extension, name))
        {
            Report(extension, FindingCodes.EVENT_NAME_PREFIX, $"subscribes event '{name}' but does not start with '{ON_PREFIX}'");
        }
    }

    private void Report(ExtensionMethod extension, string code, string message)
    {
        _findings.Add(new Finding(code, extension.AssemblyName, extension.DisplayName, $"{TypeFormatter.FormatSignature(extension.Method)}: {message}"));
    }

    private static bool ConstantMatches(ConstantHint hint, Type propertyType)
    {
        if (hint.ValueType == null)
        {
            return !propertyType.IsValueType || TypeAnalysis.IsNullable(propertyType);
        }

        var target = TypeAnalysis.UnwrapNullable(propertyType);
        if (TypeAnalysis.TypeEquals(hint.ValueType, target))
        {
            return true;
        }

        return !target.IsEnum && TypeAnalysis.IsImplicitlyConvertible(hint.ValueType, target);
    }

    private static string FormatConstant(ConstantHint hint)
    {
        if (hint.Value == null || hint.ValueType == null)
        {
            return "null";
        }

        if (hint.ValueType.IsEnum)
        {
            var field = hint.ValueType
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(field => Equals(field.GetRawConstantValue(), hint.Value));
            if (field != null)
            {
                return $"{hint.ValueType.Name}.{field.Name}";
            }
        }

        if (hint.Value is string text)
        {
            return $"\"{text}\"";
        }

        return Convert.ToString(hint.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "null";
    }

    private static bool IsSingleValueExtension(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length < 2 || TypeAnalysis.IsParams(parameters[1]))
        {
            return false;
        }

        return parameters.Skip(2).All(static parameter => parameter.IsOptional);
    }

    private static bool HasPrefix(string name, string prefix)
    {
        return name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal) && char.IsUpper(name[prefix.Length]);
    }

    private static PropertyInfo? FindProperty(ExtensionMethod extension, string name)
    {
        foreach (var type in extension.LookupTypes)
        {
            var property = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate => candidate.Name == name && candidate.SetMethod?.IsPublic == true);
            if (property != null)
            {
                return property;
            }
        }

        return null;
    }

    private static EventInfo? FindEvent(ExtensionMethod extension, string name)
    {
        foreach (var type in extension.LookupTypes)
        {
            var eventInfo = type
                .GetEvents(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(candidate => candidate.Name == name);
            if (eventInfo != null)
            {
                return eventInfo;
            }
        }

        return null;
    }

    private static bool HasProperty(ExtensionMethod extension, string name)
    {
        return extension.LookupTypes.Any(type => type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(property => property.Name == name));
    }

    private static bool HasEvent(ExtensionMethod extension, string name)
    {
        return extension.LookupTypes.Any(type => type
            .GetEvents(BindingFlags.Public | BindingFlags.Instance)
            .Any(eventInfo => eventInfo.Name == name));
    }

    private static bool HasMember(ExtensionMethod extension, string name)
    {
        return extension.LookupTypes.Any(type => type.GetMember(name, BindingFlags.Public | BindingFlags.Instance).Length > 0);
    }

    private static bool HasCollectionMember(ExtensionMethod extension, string name)
    {
        return extension.LookupTypes.Any(type =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(property => property.Name == name && TypeAnalysis.IsCollectionLike(property.PropertyType)) ||
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(method => method.Name == name));
    }

    private static string[] Names(IEnumerable<ExtensionMethod> extensions)
    {
        return extensions
            .Select(static extension => extension.DisplayName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] BaseChain(Type? type)
    {
        if (type == null)
        {
            return Array.Empty<string>();
        }

        var chain = new List<string>();
        for (var current = type.BaseType; current != null && current.BaseType != null; current = current.BaseType)
        {
            chain.Add(TypeFormatter.FormatFullType(current));
        }

        return chain.ToArray();
    }

    private static int Depth(Type type)
    {
        var depth = 0;
        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }

    private static MemberLedgerEntry[] OrderLedger(List<MemberLedgerEntry> entries)
    {
        return entries
            .OrderBy(static entry => entry.Receiver, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Member, StringComparer.Ordinal)
            .ToArray();
    }

    private static SortedDictionary<string, int> CountBy<TItem>(IEnumerable<TItem> items, Func<TItem, string> keySelector)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var key = keySelector(item);
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        return counts;
    }
}
