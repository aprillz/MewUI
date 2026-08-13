#if DEBUG
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Diagnostics;

/// <summary>
/// DevTools property list for the element selected in <see cref="DebugVisualTreeWindow"/>.
/// Lists MewProperty descriptors (with the source that won resolution) and plain CLR properties,
/// both discovered by reflection, and refreshes their values in place.
/// </summary>
internal sealed class DebugPropertyPanel : UserControl
{
    private const double NAME_COLUMN_WIDTH = 176.0;
    private const double SOURCE_COLUMN_WIDTH = 104.0;
    private const int MAX_VALUE_LENGTH = 120;

    private static readonly Dictionary<Type, PropertyEntry[]> _entryCache = new();

    private readonly TextBlock _headerLabel;
    private readonly TextBox _filterBox;
    private readonly CheckBox _setOnly;
    private readonly StackPanel _rowsPanel;
    private readonly List<PropertyRow> _rows = new();

    private UIElement? _element;

    public DebugPropertyPanel()
    {
        _headerLabel = new TextBlock { Text = "(no selection)" };

        _filterBox = new TextBox { Placeholder = "Filter" };
        _filterBox.TextChanged += _ => Rebuild();

        _setOnly = new CheckBox { Content = new TextBlock { Text = "Set values only", VerticalTextAlignment = TextAlignment.Center } };
        _setOnly.CheckedChanged += _ => Rebuild();

        _rowsPanel = new StackPanel().Padding(8, 4);

        Content = new DockPanel()
            .Spacing(4)
            .Children(
                new Border()
                    .DockTop()
                    .Padding(8, 4)
                    .Child(_headerLabel),
                new StackPanel()
                    .DockTop()
                    .Horizontal()
                    .Spacing(8)
                    .Padding(8, 0)
                    .Children(_filterBox.Width(180), _setOnly),
                new ScrollViewer { Content = _rowsPanel });
    }

    /// <summary>Selects the element to inspect; null clears the list.</summary>
    public void SetTarget(UIElement? element)
    {
        if (ReferenceEquals(_element, element))
        {
            return;
        }

        _element = element;
        Rebuild();
    }

    /// <summary>Rebuilds the row elements. Needed when the target, the filter, or the toggle changes.</summary>
    public void Rebuild()
    {
        _rows.Clear();
        _rowsPanel.Clear();

        var element = _element;
        if (element == null)
        {
            _headerLabel.Text = "(no selection)";
            return;
        }

        var entries = GetEntries(element.GetType());
        string filter = _filterBox.Text;
        bool setOnly = _setOnly.IsChecked == true;

        int mewCount = 0;
        int clrCount = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (entry.Property != null)
            {
                mewCount++;
            }
            else
            {
                clrCount++;
            }

            if (filter.Length > 0 && entry.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var value = ReadValue(element, entry);
            if (setOnly && !value.IsSet)
            {
                continue;
            }

            var row = new PropertyRow(entry);
            row.Apply(value);
            _rows.Add(row);
            _rowsPanel.Add(row.View);
        }

        _headerLabel.Text = $"{element.GetType().Name}  (MewProperty {mewCount} / CLR {clrCount}, shown {_rows.Count})";
    }

    /// <summary>
    /// Re-reads every visible row. Text is assigned only when it actually changed, so a periodic
    /// call does not invalidate layout and the scroll offset stays put.
    /// </summary>
    public void RefreshValues()
    {
        var element = _element;
        if (element == null)
        {
            return;
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            row.Apply(ReadValue(element, row.Entry));
        }
    }

    private static PropertyValueInfo ReadValue(UIElement element, PropertyEntry entry)
    {
        if (entry.Property is MewProperty property)
        {
            return ReadMewValue(element, property);
        }

        try
        {
            return new PropertyValueInfo(FormatValue(entry.ClrProperty!.GetValue(element)), "CLR", isSet: false);
        }
        catch (Exception exception)
        {
            var thrown = exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
            return new PropertyValueInfo($"({thrown.GetType().Name})", "CLR", isSet: false);
        }
    }

    private static PropertyValueInfo ReadMewValue(UIElement element, MewProperty property)
    {
        var trace = element.GetPropertyValueTrace(property);
        string source = trace.IsAnimated
            ? $"Animation/{trace.EffectiveSource}"
            : trace.EffectiveSource.ToString();
        if (trace.BindingState?.Error != null)
        {
            source += " !";
        }

        string value = FormatValue(trace.VisualValue);
        if (trace.IsAnimated)
        {
            value += $"  [base {FormatValue(trace.BaseValue)}]";
        }

        var bindingCandidate = trace.GetCandidate(ValueSource.Binding);
        if (bindingCandidate.IsSet && !bindingCandidate.IsWinner)
        {
            value += $"  [Binding raw {FormatValue(bindingCandidate.RawValue)} shadowed]";
        }

        if (trace.BindingState is { } bindingState)
        {
            if (bindingState.Error is { } error)
            {
                string last = bindingState.HasLastSuccessfulTargetValue
                    ? FormatValue(bindingState.LastSuccessfulTargetValue)
                    : "(none)";
                value +=
                    $"  [candidate {FormatValue(bindingState.CurrentCandidate)}; last {last}; " +
                    $"{error.Status}/{error.Stage}]";
            }
            else if (!bindingCandidate.IsSet && bindingState.HasLastSuccessfulTargetValue)
            {
                value += $"  [Binding last {FormatValue(bindingState.LastSuccessfulTargetValue)}]";
            }
        }

        if (element is Control control)
        {
            var styleTrace = control.GetStyleCascadeTrace(property);
            if (styleTrace.FinalEntry is { } finalStyleEntry)
            {
                string origin = finalStyleEntry.Trigger == null
                    ? "setter"
                    : $"trigger {FormatTrigger(finalStyleEntry.Trigger)}";
                string outcome;
                if (finalStyleEntry.IsUnset)
                {
                    outcome = "unset";
                }
                else if (!styleTrace.IsStyleEffective)
                {
                    outcome = $"shadowed by {styleTrace.EffectiveSource}";
                }
                else
                {
                    outcome = styleTrace.IsAnimated ? "winner under animation" : "winner";
                }
                string layer = finalStyleEntry.Layer == StyleCascadeLayer.FrameworkDefault
                    ? "framework default"
                    : "application";
                string newlyInherited = finalStyleEntry.IsNewlyInherited
                    ? ", newly inherited through default layering"
                    : string.Empty;
                value +=
                    $"  [Style {finalStyleEntry.DeclaringStyle.TargetType.Name}/{origin} " +
                    $"{outcome}, {layer}{newlyInherited}]";
            }
        }

        bool isSet = trace.BindingState != null || trace.HasNonDefaultCandidate;

        return new PropertyValueInfo(Truncate(value), source, isSet);
    }

    private static string FormatTrigger(StateTrigger trigger)
        => $"+{trigger.Match}/-{trigger.Exclude}";

    private static string FormatValue(object? value)
    {
        switch (value)
        {
            case null:
                return "(null)";
            case string text:
                return $"\"{Truncate(text)}\"";
            case double number:
                return FormatNumber(number);
            case float number:
                return FormatNumber(number);
            case Element child:
                return $"{child.GetType().Name}#{child.GetHashCode():X8}";
            case System.Collections.ICollection collection:
                return $"{collection.GetType().Name}({collection.Count})";
        }

        return Truncate(value.ToString() ?? string.Empty);
    }

    private static string FormatNumber(double number)
    {
        if (double.IsNaN(number))
        {
            return "NaN";
        }

        if (double.IsPositiveInfinity(number))
        {
            return "Inf";
        }

        if (double.IsNegativeInfinity(number))
        {
            return "-Inf";
        }

        return number.ToString("0.###");
    }

    private static string Truncate(string text)
        => text.Length > MAX_VALUE_LENGTH ? string.Concat(text.AsSpan(0, MAX_VALUE_LENGTH), "...") : text;

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "DEBUG-only dev tool. Reflects over live instances; the tool is compiled out of trimmed builds.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "DEBUG-only dev tool. Reflects over live instances; the tool is compiled out of trimmed builds.")]
    private static PropertyEntry[] GetEntries(Type type)
    {
        if (_entryCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        const BindingFlags STATIC_FLAGS = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        const BindingFlags INSTANCE_FLAGS = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var mewEntries = new List<PropertyEntry>(64);
        var seenProperties = new HashSet<MewProperty>();
        var mewNames = new HashSet<string>(StringComparer.Ordinal);

        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(STATIC_FLAGS))
            {
                if (!typeof(MewProperty).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                AddMewEntry(field.GetValue(null), mewEntries, seenProperties, mewNames);
            }

            // Read-only registrations are often exposed as `static MewProperty<T> XxxProperty => Key.Property`.
            foreach (var staticProperty in current.GetProperties(STATIC_FLAGS))
            {
                if (!staticProperty.CanRead || !typeof(MewProperty).IsAssignableFrom(staticProperty.PropertyType))
                {
                    continue;
                }

                AddMewEntry(staticProperty.GetValue(null), mewEntries, seenProperties, mewNames);
            }
        }

        var clrEntries = new List<PropertyEntry>(48);
        var seenClrNames = new HashSet<string>(StringComparer.Ordinal);

        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (var clrProperty in current.GetProperties(INSTANCE_FLAGS))
            {
                if (!clrProperty.CanRead || clrProperty.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                // A CLR wrapper over a MewProperty would just duplicate the row above it.
                if (mewNames.Contains(clrProperty.Name) || !seenClrNames.Add(clrProperty.Name))
                {
                    continue;
                }

                clrEntries.Add(PropertyEntry.ForClr(clrProperty));
            }
        }

        mewEntries.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        clrEntries.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        var result = new PropertyEntry[mewEntries.Count + clrEntries.Count];
        mewEntries.CopyTo(result, 0);
        clrEntries.CopyTo(result, mewEntries.Count);

        _entryCache[type] = result;
        return result;
    }

    private static void AddMewEntry(object? candidate, List<PropertyEntry> entries, HashSet<MewProperty> seen, HashSet<string> names)
    {
        if (candidate is not MewProperty property || !seen.Add(property))
        {
            return;
        }

        entries.Add(PropertyEntry.ForMew(property));
        names.Add(property.Name);
    }

    private sealed class PropertyEntry
    {
        private PropertyEntry(string name, MewProperty? property, PropertyInfo? clrProperty)
        {
            Name = name;
            Property = property;
            ClrProperty = clrProperty;
        }

        public string Name { get; }

        public MewProperty? Property { get; }

        public PropertyInfo? ClrProperty { get; }

        public static PropertyEntry ForMew(MewProperty property) => new(property.Name, property, null);

        public static PropertyEntry ForClr(PropertyInfo clrProperty) => new(clrProperty.Name, null, clrProperty);
    }

    private readonly struct PropertyValueInfo
    {
        public PropertyValueInfo(string text, string source, bool isSet)
        {
            Text = text;
            Source = source;
            IsSet = isSet;
        }

        public string Text { get; }

        public string Source { get; }

        /// <summary>False for defaults and for CLR properties, which the "Set values only" filter hides.</summary>
        public bool IsSet { get; }
    }

    private sealed class PropertyRow
    {
        private readonly TextBlock _valueText;
        private readonly TextBlock _sourceText;
        private string _lastValue = string.Empty;
        private string _lastSource = string.Empty;

        public PropertyRow(PropertyEntry entry)
        {
            Entry = entry;

            var nameText = new TextBlock
            {
                Text = entry.Name,
                VerticalTextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            _valueText = new TextBlock
            {
                VerticalTextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            _sourceText = new TextBlock { VerticalTextAlignment = TextAlignment.Center };

            View = new DockPanel()
                .Spacing(6)
                .Children(
                    nameText.Width(NAME_COLUMN_WIDTH).DockLeft(),
                    _sourceText.Width(SOURCE_COLUMN_WIDTH).DockRight(),
                    _valueText);
        }

        public PropertyEntry Entry { get; }

        public DockPanel View { get; }

        public void Apply(in PropertyValueInfo info)
        {
            if (!string.Equals(_lastValue, info.Text, StringComparison.Ordinal))
            {
                _lastValue = info.Text;
                _valueText.Text = info.Text;
            }

            if (!string.Equals(_lastSource, info.Source, StringComparison.Ordinal))
            {
                _lastSource = info.Source;
                _sourceText.Text = info.Source;
            }
        }
    }
}
#endif
