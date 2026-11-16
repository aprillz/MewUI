using System.Diagnostics;
using System.Reflection;

namespace Aprillz.MewUI.HotReload;

/// <summary>
/// Records what app code runs where, with normalized-IL baselines, so a delta can be classified:
/// code a registered build runs (its calls, constructions and build-time delegates, followed through
/// app code), code the event handlers it subscribes run, and every other method of the types either
/// of them touched.
/// </summary>
internal sealed class HotReloadReachability
{
    // Bounds one closure walk; a build that reaches into a large debug-built library stops here
    // instead of hashing the whole library.
    private const int MAX_CLOSURE_METHODS = 5000;

    private const BindingFlags DECLARED_MEMBERS =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly object _lock = new();
    private readonly Func<Assembly, bool> _isAppCode;
    private readonly Dictionary<MethodKey, MethodBase> _methods = new();
    private readonly Dictionary<MethodKey, byte[]?> _baselines = new();
    private readonly Dictionary<TypeKey, List<MethodKey>> _methodsByType = new();
    private readonly HashSet<TypeKey> _wholeTypesRecorded = new();
    private readonly Dictionary<MethodKey, HashSet<MethodKey>> _buildClosures = new();
    private readonly HashSet<MethodKey> _handlerReach = new();
    private readonly Dictionary<Assembly, HashSet<TypeKey>?> _assemblyTypes = new();
    private readonly Dictionary<Assembly, Dictionary<MethodKey, List<MethodBase>>> _overrides = new();

    internal HotReloadReachability()
        : this(IsAppCodeAssembly)
    {
    }

    /// <summary>Creates a tracker that walks only into assemblies <paramref name="isAppCode"/> accepts.</summary>
    internal HotReloadReachability(Func<Assembly, bool> isAppCode)
    {
        _isAppCode = isAppCode;
    }

    internal static HotReloadReachability Shared { get; } = new();

    internal readonly record struct MethodKey(Module Module, int Token)
    {
        internal static MethodKey Of(MethodBase method) => new(method.Module, method.MetadataToken);
    }

    internal readonly record struct TypeKey(Module Module, int Token)
    {
        internal static TypeKey Of(Type type) => new(type.Module, type.MetadataToken);
    }

    /// <summary>What one delta changed among the recorded code.</summary>
    /// <param name="Methods">Recorded methods whose body changed.</param>
    /// <param name="UnrecordedTypes">Pre-existing app types the delta changed that no recorded code touched.</param>
    /// <param name="UnknownScope">The runtime did not say which types changed.</param>
    internal sealed record Changes(IReadOnlySet<MethodKey> Methods, IReadOnlyList<Type> UnrecordedTypes, bool UnknownScope)
    {
        internal bool IsEmpty => Methods.Count == 0 && UnrecordedTypes.Count == 0 && !UnknownScope;
    }

    /// <summary>Records the code <paramref name="root"/> runs and the handlers it subscribes, with baselines.</summary>
    internal void Track(MethodBase root)
    {
        lock (_lock)
        {
            var rootKey = MethodKey.Of(root);
            if (!_buildClosures.ContainsKey(rootKey))
            {
                RecordBuild(root);
            }
        }
    }

    /// <summary>Adds to <paramref name="reached"/> the changed methods the build of <paramref name="root"/> runs; true when any.</summary>
    internal bool CollectReached(MethodBase root, IReadOnlySet<MethodKey> changed, HashSet<MethodKey> reached)
    {
        lock (_lock)
        {
            var rootKey = MethodKey.Of(root);
            if (!_buildClosures.TryGetValue(rootKey, out var closure))
            {
                if (changed.Contains(rootKey))
                {
                    reached.Add(rootKey);
                    return true;
                }
                return false;
            }

            bool any = false;
            foreach (var key in changed)
            {
                if (closure.Contains(key))
                {
                    reached.Add(key);
                    any = true;
                }
            }
            return any;
        }
    }

    /// <summary>Whether <paramref name="key"/> runs only from event handlers the recorded builds subscribed.</summary>
    internal bool IsHandlerCode(MethodKey key)
    {
        lock (_lock)
        {
            return _handlerReach.Contains(key);
        }
    }

    /// <summary>
    /// Returns the type whose instances the framework calls <paramref name="key"/> on when it overrides a
    /// framework virtual (rendering, layout, input), or null when it does not.
    /// </summary>
    internal Type? FindFrameworkCallbackOwner(MethodKey key)
    {
        lock (_lock)
        {
            if (!_methods.TryGetValue(key, out var method) || method is not MethodInfo info || !info.IsVirtual)
            {
                return null;
            }

            var baseDefinition = info.GetBaseDefinition();
            var baseAssembly = baseDefinition.DeclaringType?.Assembly;
            if (MethodKey.Of(baseDefinition) == key || baseAssembly == null || _isAppCode(baseAssembly))
            {
                return null;
            }
            return info.DeclaringType;
        }
    }

    /// <summary>Returns a readable name of <paramref name="key"/> for diagnostics.</summary>
    internal string Describe(MethodKey key)
    {
        lock (_lock)
        {
            return _methods.TryGetValue(key, out var method)
                ? $"{method.DeclaringType?.FullName}.{method.Name}"
                : key.Token.ToString("X8");
        }
    }

    /// <summary>
    /// Rehashes the recorded methods of <paramref name="updatedTypes"/> (all of them when null), advances
    /// their baselines and reports what changed. Types added by the delta are left out: nothing ran
    /// them before, and the edited code that calls them shows up as a changed method.
    /// </summary>
    internal Changes DetectChanges(Type[]? updatedTypes)
    {
        lock (_lock)
        {
            var changed = new HashSet<MethodKey>();
            var unrecorded = new List<Type>();
            if (updatedTypes == null)
            {
                foreach (var key in _methods.Keys)
                {
                    Rehash(key, changed);
                }
            }
            else
            {
                foreach (var type in updatedTypes)
                {
                    var typeKey = TypeKey.Of(type);
                    if (_methodsByType.TryGetValue(typeKey, out var keys))
                    {
                        foreach (var key in keys)
                        {
                            Rehash(key, changed);
                        }
                    }
                    else if (IsPreexistingAppType(type, typeKey))
                    {
                        unrecorded.Add(type);
                    }
                }
            }

            if (changed.Count > 0)
            {
                // Edited code can call new methods and add overrides; rebuild what it touched.
                _overrides.Clear();
                var stale = new List<MethodKey>();
                foreach (var (rootKey, closure) in _buildClosures)
                {
                    if (closure.Overlaps(changed))
                    {
                        stale.Add(rootKey);
                    }
                }
                foreach (var rootKey in stale)
                {
                    RecordBuild(_methods[rootKey]);
                }
            }

            return new Changes(changed, unrecorded, updatedTypes == null);
        }
    }

    /// <summary>Overrides the recorded baseline of <paramref name="method"/>; lets tests stand in for a body edit.</summary>
    internal void SetBaseline(MethodBase method, byte[]? baseline)
    {
        lock (_lock)
        {
            _baselines[MethodKey.Of(method)] = baseline;
        }
    }

    private void Rehash(MethodKey key, HashSet<MethodKey> changed)
    {
        var current = IlNormalizer.Hash(_methods[key]);
        _baselines.TryGetValue(key, out var baseline);
        if (!BytesEqual(baseline, current))
        {
            changed.Add(key);
            _baselines[key] = current;
        }
    }

    private void RecordBuild(MethodBase root)
    {
        var handlerRoots = new List<MethodBase>();
        _buildClosures[MethodKey.Of(root)] = Walk(root, handlerRoots);

        // Code handlers run is followed through every edge: anything it reaches runs on an event.
        var nestedHandlers = new List<MethodBase>();
        foreach (var handler in handlerRoots)
        {
            if (!IsTracked(handler) || _handlerReach.Contains(MethodKey.Of(handler)))
            {
                continue;
            }

            _handlerReach.UnionWith(Walk(handler, nestedHandlers));
            foreach (var nested in nestedHandlers)
            {
                if (IsTracked(nested))
                {
                    _handlerReach.UnionWith(Walk(nested, new List<MethodBase>()));
                }
            }
            nestedHandlers.Clear();
        }
    }

    private HashSet<MethodKey> Walk(MethodBase root, List<MethodBase> handlerRoots)
    {
        var closure = new HashSet<MethodKey>();
        var pending = new Stack<MethodBase>();
        var edges = new List<MethodBase>();
        pending.Push(root);
        closure.Add(Remember(root));

        while (pending.Count > 0 && closure.Count < MAX_CLOSURE_METHODS)
        {
            edges.Clear();
            IlNormalizer.CollectBuildEdges(pending.Pop(), edges, handlerRoots);
            for (int index = 0; index < edges.Count; index++)
            {
                var target = edges[index];
                if (!IsTracked(target))
                {
                    continue;
                }

                if (closure.Add(Remember(target)))
                {
                    pending.Push(target);
                }

                if (target.IsVirtual && !target.IsFinal)
                {
                    foreach (var implementation in FindOverrides(target))
                    {
                        if (closure.Add(Remember(implementation)))
                        {
                            pending.Push(implementation);
                        }
                    }
                }
            }
        }

        return closure;
    }

    private MethodKey Remember(MethodBase method)
    {
        var key = MethodKey.Of(method);
        AddBaseline(key, method);
        if (method.DeclaringType != null)
        {
            RecordWholeType(method.DeclaringType);
        }
        return key;
    }

    /// <summary>
    /// Baselines every method of a touched type, so an edit to one no recorded path reaches is still
    /// seen as a change instead of passing unnoticed.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reached only under MetadataUpdater.IsSupported (JIT/debug Hot Reload); trimmed out of AOT/trimmed builds.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Reached only under MetadataUpdater.IsSupported (JIT/debug Hot Reload); trimmed out of AOT/trimmed builds.")]
    private void RecordWholeType(Type type)
    {
        var definition = type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericTypeDefinition() : type;
        if (!_wholeTypesRecorded.Add(TypeKey.Of(definition)))
        {
            return;
        }

        foreach (var method in definition.GetMethods(DECLARED_MEMBERS))
        {
            AddBaseline(MethodKey.Of(method), method);
        }
        foreach (var constructor in definition.GetConstructors(DECLARED_MEMBERS))
        {
            AddBaseline(MethodKey.Of(constructor), constructor);
        }
    }

    private void AddBaseline(MethodKey key, MethodBase method)
    {
        if (_methods.ContainsKey(key))
        {
            return;
        }

        _methods[key] = method;
        _baselines[key] = IlNormalizer.Hash(method);

        var declaringType = method.DeclaringType;
        if (declaringType != null)
        {
            var definition = declaringType.IsGenericType && !declaringType.IsGenericTypeDefinition
                ? declaringType.GetGenericTypeDefinition()
                : declaringType;
            var typeKey = TypeKey.Of(definition);
            if (!_methodsByType.TryGetValue(typeKey, out var keys))
            {
                keys = new List<MethodKey>();
                _methodsByType[typeKey] = keys;
            }
            keys.Add(key);
        }
    }

    private bool IsTracked(MethodBase method)
    {
        var assembly = method.DeclaringType?.Assembly;
        return assembly != null && IsTrackedAssembly(assembly);
    }

    private bool IsTrackedAssembly(Assembly assembly)
    {
        if (!_assemblyTypes.TryGetValue(assembly, out var types))
        {
            // The type list is snapshotted the first time an assembly is reached, which is before any
            // edit, so a type missing from it later was added by a delta.
            types = _isAppCode(assembly) ? SnapshotTypes(assembly) : null;
            _assemblyTypes[assembly] = types;
        }
        return types != null;
    }

    private bool IsPreexistingAppType(Type type, TypeKey typeKey)
    {
        var assembly = type.Assembly;
        bool firstSeen = !_assemblyTypes.ContainsKey(assembly);
        if (!IsTrackedAssembly(assembly))
        {
            return false;
        }

        // An app assembly first seen only now has no pre-edit snapshot; count its types as existing.
        return firstSeen || _assemblyTypes[assembly]!.Contains(typeKey);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reached only under MetadataUpdater.IsSupported (JIT/debug Hot Reload); trimmed out of AOT/trimmed builds.")]
    private static HashSet<TypeKey> SnapshotTypes(Assembly assembly)
    {
        var keys = new HashSet<TypeKey>();
        foreach (var type in LoadableTypes(assembly))
        {
            keys.Add(TypeKey.Of(type));
        }
        return keys;
    }

    /// <summary>
    /// App code is what Hot Reload can edit: debug-built (JIT optimizer disabled) assemblies other
    /// than the framework itself. Release-built packages and the runtime are never walked.
    /// </summary>
    private static bool IsAppCodeAssembly(Assembly assembly)
    {
        if (assembly.IsDynamic || assembly == typeof(Application).Assembly)
        {
            return false;
        }

        string name = assembly.GetName().Name ?? string.Empty;
        string frameworkName = typeof(Application).Assembly.GetName().Name ?? string.Empty;
        if (name.StartsWith("System", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || name is "mscorlib" or "netstandard"
            || name.StartsWith(frameworkName + ".Platform.", StringComparison.Ordinal)
            || name.StartsWith(frameworkName + ".Backend.", StringComparison.Ordinal))
        {
            return false;
        }

        var debuggable = assembly.GetCustomAttribute<DebuggableAttribute>();
        return debuggable != null && debuggable.IsJITOptimizerDisabled;
    }

    private List<MethodBase> FindOverrides(MethodBase target)
    {
        var found = new List<MethodBase>();
        if (target is not MethodInfo targetInfo)
        {
            return found;
        }

        var declaringType = targetInfo.DeclaringType;
        bool isInterfaceMethod = declaringType != null && declaringType.IsInterface;
        var baseKey = MethodKey.Of(isInterfaceMethod ? targetInfo : targetInfo.GetBaseDefinition());

        foreach (var (assembly, types) in _assemblyTypes.ToArray())
        {
            if (types == null)
            {
                continue;
            }

            if (!_overrides.TryGetValue(assembly, out var map))
            {
                map = BuildOverrideMap(assembly);
                _overrides[assembly] = map;
            }

            if (map.TryGetValue(baseKey, out var implementations))
            {
                found.AddRange(implementations);
            }
        }

        return found;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reached only under MetadataUpdater.IsSupported (JIT/debug Hot Reload); trimmed out of AOT/trimmed builds.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Reached only under MetadataUpdater.IsSupported (JIT/debug Hot Reload); trimmed out of AOT/trimmed builds.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Reached only under MetadataUpdater.IsSupported (JIT/debug Hot Reload); trimmed out of AOT/trimmed builds.")]
    private static Dictionary<MethodKey, List<MethodBase>> BuildOverrideMap(Assembly assembly)
    {
        var map = new Dictionary<MethodKey, List<MethodBase>>();
        const BindingFlags INSTANCE_MEMBERS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var type in LoadableTypes(assembly))
        {
            if (type.IsInterface)
            {
                continue;
            }

            foreach (var method in type.GetMethods(INSTANCE_MEMBERS))
            {
                if (!method.IsVirtual || method.IsAbstract)
                {
                    continue;
                }

                var baseDefinition = method.GetBaseDefinition();
                if (baseDefinition != method)
                {
                    Add(map, MethodKey.Of(baseDefinition), method);
                }
            }

            if (type.IsGenericTypeDefinition)
            {
                continue;
            }

            foreach (var interfaceType in type.GetInterfaces())
            {
                InterfaceMapping mapping;
                try
                {
                    mapping = type.GetInterfaceMap(interfaceType);
                }
                catch
                {
                    continue;
                }

                for (int index = 0; index < mapping.InterfaceMethods.Length; index++)
                {
                    var implementation = mapping.TargetMethods[index];
                    if (implementation.DeclaringType == type)
                    {
                        Add(map, MethodKey.Of(mapping.InterfaceMethods[index]), implementation);
                    }
                }
            }
        }

        return map;

        static void Add(Dictionary<MethodKey, List<MethodBase>> map, MethodKey key, MethodBase method)
        {
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<MethodBase>();
                map[key] = list;
            }
            list.Add(method);
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reached only under MetadataUpdater.IsSupported (JIT/debug Hot Reload); trimmed out of AOT/trimmed builds.")]
    private static Type[] LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(static type => type != null).ToArray()!;
        }
    }

    private static bool BytesEqual(byte[]? left, byte[]? right)
    {
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        return left.AsSpan().SequenceEqual(right);
    }
}
