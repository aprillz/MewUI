using System.Runtime.ExceptionServices;

namespace Aprillz.MewUI;

/// <summary>
/// Style registry supporting both named styles and type-based style rules.
/// Attach to any <see cref="Controls.FrameworkElement"/> (typically a Window) to provide
/// scoped styles for descendant controls.
/// </summary>
public sealed class StyleSheet
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Style> _namedStyles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<Style>> _namedStyleFactories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExceptionDispatchInfo> _factoryFailures = new(StringComparer.Ordinal);
    private readonly HashSet<string> _materializingNames = new(StringComparer.Ordinal);
    private List<(Type Type, Style Style)>? _typeRules;
    private (Type Type, Style Style)[]? _frozenTypeRules;
    private List<(Type Type, string Name)>? _typeRuleNames;
    private bool _isFrozen;

    internal bool UsesFrameworkNamedStyles { get; init; }

    /// <summary>Gets whether this sheet has been frozen for live style lookup.</summary>
    public bool IsFrozen
    {
        get
        {
            lock (_sync)
            {
                return _isFrozen;
            }
        }
    }

    /// <summary>
    /// Defines a named style factory. The style is created on first lookup.
    /// </summary>
    /// <param name="name">The style name (matched via <c>Control.StyleName</c>).</param>
    /// <param name="factory">The style factory to invoke lazily.</param>
    public void Define(string name, Func<Style> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(factory);

        lock (_sync)
        {
            ThrowIfFrozen();
            _namedStyles.Remove(name);
            _factoryFailures.Remove(name);
            _namedStyleFactories[name] = factory;
        }
    }

    /// <summary>
    /// Defines a type-based style rule. All descendant controls of type <typeparamref name="T"/>
    /// (without an explicit <c>StyleName</c>) will receive this style.
    /// </summary>
    public void Define<T>(Style style) where T : Controls.Control
    {
        ArgumentNullException.ThrowIfNull(style);
        if (!style.TargetType.IsAssignableFrom(typeof(T)))
        {
            throw new ArgumentException(
                $"Style targeting '{style.TargetType.FullName}' cannot be registered for " +
                $"control type '{typeof(T).FullName}'.",
                nameof(style));
        }

        lock (_sync)
        {
            ThrowIfFrozen();
            _typeRules ??= new();
            _typeRules.Add((typeof(T), style));
        }
    }

    /// <summary>
    /// Defines a type-based style rule that names its style instead of holding it. The name is resolved
    /// where <c>Control.StyleName</c> is resolved, from the control's own scope chain, so a rule can point
    /// at a style defined further out - a built-in key among them - and a nearer scope can redefine it.
    /// </summary>
    /// <typeparam name="T">Control type the rule applies to.</typeparam>
    /// <param name="styleName">The style name to resolve when a control takes this rule.</param>
    public void Define<T>(string styleName) where T : Controls.Control
    {
        ArgumentException.ThrowIfNullOrEmpty(styleName);

        lock (_sync)
        {
            ThrowIfFrozen();
            _typeRuleNames ??= new();
            _typeRuleNames.Add((typeof(T), styleName));
        }
    }

    /// <summary>The name a rule gives the control type, or null when this sheet names none for it.</summary>
    internal string? GetTypeRuleName(Type controlType)
    {
        lock (_sync)
        {
            if (_typeRuleNames == null)
            {
                return null;
            }

            for (Type? candidate = controlType;
                 candidate != null && typeof(Controls.Control).IsAssignableFrom(candidate);
                 candidate = candidate.BaseType)
            {
                for (int i = _typeRuleNames.Count - 1; i >= 0; i--)
                {
                    if (_typeRuleNames[i].Type == candidate)
                    {
                        return _typeRuleNames[i].Name;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Freezes this sheet for live use. Registered type styles and already-materialized named
    /// styles are validated and snapshotted; named factories remain lazy.
    /// </summary>
    public void Freeze()
    {
        lock (_sync)
        {
            if (_isFrozen)
            {
                return;
            }

            foreach (var style in _namedStyles.Values)
            {
                style.Freeze();
            }

            _frozenTypeRules = _typeRules?.ToArray() ?? [];
            for (int i = 0; i < _frozenTypeRules.Length; i++)
            {
                _frozenTypeRules[i].Style.Freeze();
            }

            _isFrozen = true;
        }
    }

    /// <summary>
    /// Gets a named style, or <see langword="null"/> if not found.
    /// </summary>
    public Style? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_sync)
        {
            if (_namedStyles.TryGetValue(name, out var style))
            {
                return style;
            }

            if (_factoryFailures.TryGetValue(name, out var failure))
            {
                failure.Throw();
            }

            if (!_namedStyleFactories.TryGetValue(name, out var factory) &&
                (!UsesFrameworkNamedStyles || !FrameworkNamedStyles.TryGetFactory(name, out factory)))
            {
                return null;
            }

            if (!_materializingNames.Add(name))
            {
                throw new InvalidOperationException(
                    $"Named style factory '{name}' recursively requested itself while materializing.");
            }

            try
            {
                if (factory is null)
                {
                    return null;
                }

                style = factory() ?? throw new InvalidOperationException(
                    $"Named style factory '{name}' returned null.");
                if (_isFrozen)
                {
                    style.Freeze();
                }

                _namedStyles[name] = style;
                return style;
            }
            catch (Exception ex)
            {
                _factoryFailures[name] = ExceptionDispatchInfo.Capture(ex);
                throw;
            }
            finally
            {
                _materializingNames.Remove(name);
            }
        }
    }

    /// <summary>
    /// Gets the matching style for the given control type.
    /// Checks exact type first, then base types.
    /// </summary>
    public Style? GetByType(Type controlType)
    {
        ArgumentNullException.ThrowIfNull(controlType);
        if (!typeof(Controls.Control).IsAssignableFrom(controlType))
        {
            throw new ArgumentException(
                $"Type '{controlType.FullName}' must derive from Control.",
                nameof(controlType));
        }

        lock (_sync)
        {
            IReadOnlyList<(Type Type, Style Style)> rules = _isFrozen
                ? _frozenTypeRules!
                : _typeRules ?? [];

            for (Type? candidate = controlType;
                 candidate != null && typeof(Controls.Control).IsAssignableFrom(candidate);
                 candidate = candidate.BaseType)
            {
                for (int i = rules.Count - 1; i >= 0; i--)
                {
                    if (rules[i].Type == candidate)
                    {
                        return rules[i].Style;
                    }
                }
            }

            return null;
        }
    }

    internal Style? GetLive(string name)
    {
        Freeze();
        return Get(name);
    }

    internal Style? GetLiveByType(Type controlType)
    {
        Freeze();
        return GetByType(controlType);
    }

    /// <summary>
    /// Drops only results that can be recreated from retained named factories. Immediate type
    /// rules remain frozen and are refreshed by rebuilding and replacing their owning sheet.
    /// </summary>
    internal void InvalidateLazyCache()
    {
        lock (_sync)
        {
            _namedStyles.Clear();
            _factoryFailures.Clear();
        }
    }

    private void ThrowIfFrozen()
    {
        if (_isFrozen)
        {
            throw new InvalidOperationException(
                "This StyleSheet is frozen. Build and assign a new StyleSheet to change live styles.");
        }
    }
}
