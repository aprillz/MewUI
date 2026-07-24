using System.Reflection;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Preview;

/// <summary>
/// Reflection discovery of previewable types: non-abstract, non-generic UserControl/Window
/// subclasses from every loaded assembly that references MewUI. Types without a parameterless
/// (or all-defaults) constructor are still listed, marked unavailable with a reason, so they do
/// not silently disappear from the IDE target list.
/// </summary>
internal static class PreviewTargetScanner
{
    internal const string MAIN_WINDOW_ID = "(main)";

    internal sealed class TargetDescriptor
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required string Kind { get; init; }
        public Type? Type { get; init; }
        public ConstructorInfo? Constructor { get; init; }
        public string? UnavailableReason { get; init; }
        public string? SourcePath { get; init; }
        public int? SourceLine { get; init; }

        public bool Available => UnavailableReason == null;
    }

    internal static List<TargetDescriptor> Scan()
    {
        var targets = new List<TargetDescriptor>
        {
            new() { Id = MAIN_WINDOW_ID, DisplayName = "Application main window", Kind = "main" },
        };

        var mewUiName = typeof(Application).Assembly.GetName().Name;
        EnsureReferenceClosureLoaded(mewUiName);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || assembly == typeof(Application).Assembly || !ReferencesMewUi(assembly, mewUiName))
            {
                continue;
            }

            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                string kind;
                if (typeof(UserControl).IsAssignableFrom(type))
                {
                    kind = "usercontrol";
                }
                else if (typeof(Window).IsAssignableFrom(type))
                {
                    kind = "window";
                }
                else
                {
                    continue;
                }

                var constructor = FindCreatableConstructor(type);
                var source = PreviewSourceLocator.TryLocate(type);
                targets.Add(new TargetDescriptor
                {
                    Id = type.FullName ?? type.Name,
                    DisplayName = type.Name,
                    Kind = kind,
                    Type = type,
                    Constructor = constructor,
                    UnavailableReason = constructor == null
                        ? "requires constructor arguments (add a parameterless preview wrapper)"
                        : null,
                    SourcePath = source?.Path,
                    SourceLine = source?.Line,
                });
            }
        }

        targets.Sort(static (left, right) => string.CompareOrdinal(left.Id, right.Id));
        return targets;
    }

    internal static object CreateInstance(TargetDescriptor descriptor)
    {
        var constructor = descriptor.Constructor
            ?? throw new InvalidOperationException($"{descriptor.Id} has no preview-creatable constructor.");

        var parameters = constructor.GetParameters();
        if (parameters.Length == 0)
        {
            return constructor.Invoke(null);
        }

        var arguments = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            arguments[i] = parameters[i].DefaultValue;
        }
        return constructor.Invoke(arguments);
    }

    /// <summary>
    /// Loads the entry assembly's reference closure so library components appear in the target
    /// list even before any of their code has executed (plan.md 4.5.1: scan covers every
    /// MewUI-referencing assembly, not just what the CLR loaded lazily).
    /// </summary>
    private static void EnsureReferenceClosureLoaded(string? mewUiName)
    {
        var entry = Assembly.GetEntryAssembly();
        if (entry == null)
        {
            return;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<Assembly>();
        queue.Enqueue(entry);

        while (queue.Count > 0)
        {
            foreach (var reference in queue.Dequeue().GetReferencedAssemblies())
            {
                if (string.Equals(reference.Name, mewUiName, StringComparison.Ordinal)
                    || IsFrameworkAssembly(reference.Name)
                    || !visited.Add(reference.FullName))
                {
                    continue;
                }

                try
                {
                    queue.Enqueue(Assembly.Load(reference));
                }
                catch
                {
                    // Reference-only or platform-specific assemblies may be unloadable; the scan
                    // simply proceeds with what did load.
                }
            }
        }
    }

    private static bool IsFrameworkAssembly(string? name) =>
        name != null
        && (name.StartsWith("System", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || string.Equals(name, "mscorlib", StringComparison.Ordinal)
            || string.Equals(name, "netstandard", StringComparison.Ordinal));

    private static bool ReferencesMewUi(Assembly assembly, string? mewUiName)
    {
        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            if (string.Equals(reference.Name, mewUiName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ConstructorInfo? FindCreatableConstructor(Type type)
    {
        ConstructorInfo? allDefaults = null;
        foreach (var constructor in type.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length == 0)
            {
                return constructor;
            }

            if (allDefaults == null && Array.TrueForAll(parameters, static parameter => parameter.HasDefaultValue))
            {
                allDefaults = constructor;
            }
        }

        return allDefaults;
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var loaded = new List<Type>();
            foreach (var type in ex.Types)
            {
                if (type != null)
                {
                    loaded.Add(type);
                }
            }
            return [.. loaded];
        }
    }
}
