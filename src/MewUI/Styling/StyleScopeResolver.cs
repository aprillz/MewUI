using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

internal static class StyleScopeResolver
{
    internal static Style? Resolve(
        Control control,
        string? styleName,
        StyleSheet? applicationStyleSheet)
        => Resolve(control, styleName, applicationStyleSheet, out _);

    /// <summary>
    /// Resolves the style for a control. <paramref name="unresolvedName"/> reports a style name a type
    /// rule asked for and no scope defined, so the caller treats it the way it treats a control's own
    /// StyleName that resolved to nothing.
    /// </summary>
    internal static Style? Resolve(
        Control control,
        string? styleName,
        StyleSheet? applicationStyleSheet,
        out string? unresolvedName)
    {
        unresolvedName = null;
        return Resolve(control, styleName, applicationStyleSheet, allowNamedTypeRules: true, ref unresolvedName);
    }

    private static Style? Resolve(
        Control control,
        string? styleName,
        StyleSheet? applicationStyleSheet,
        bool allowNamedTypeRules,
        ref string? unresolvedName)
    {
        ArgumentNullException.ThrowIfNull(control);

        Type controlType = control.GetType();
        bool liveLookup = control.FindVisualRoot() is Window;
        for (Element? current = control; current != null; current = current.ContextParent)
        {
            if (current is not FrameworkElement { StyleSheet: { } sheet })
            {
                continue;
            }

            Style? style = Lookup(sheet, styleName, controlType, liveLookup);
            if (style != null)
            {
                return style;
            }

            // A type rule that names its style rather than holding it: the name is resolved from the
            // control's own chain, so it reaches keys defined further out and a nearer scope can redefine
            // one. The flag, not the shape of the lookup, is what stops that walk naming a rule again.
            if (!allowNamedTypeRules || styleName != null)
            {
                continue;
            }

            if (sheet.GetTypeRuleName(controlType) is not string ruleName)
            {
                continue;
            }

            var named = Resolve(
                control, ruleName, applicationStyleSheet, allowNamedTypeRules: false, ref unresolvedName);
            if (named != null)
            {
                return named;
            }

            // Reported rather than thrown here: the caller knows whether the scope chain is complete yet,
            // and treats this the way it treats a control's own StyleName that resolved to nothing.
            unresolvedName = ruleName;
            return null;
        }

        if (applicationStyleSheet == null)
        {
            return null;
        }

        return Lookup(applicationStyleSheet, styleName, controlType, liveLookup);
    }

    private static Style? Lookup(
        StyleSheet sheet,
        string? styleName,
        Type controlType,
        bool liveLookup)
    {
        if (styleName != null)
        {
            return liveLookup ? sheet.GetLive(styleName) : sheet.Get(styleName);
        }

        return liveLookup ? sheet.GetLiveByType(controlType) : sheet.GetByType(controlType);
    }

    internal static string DescribeScopes(Control control, bool includesApplication)
    {
        var scopes = new List<string>(capacity: 4);
        for (Element? current = control; current != null; current = current.ContextParent)
        {
            if (current is FrameworkElement { StyleSheet: not null })
            {
                string suffix = ReferenceEquals(current, control) ? " (self)" : string.Empty;
                scopes.Add(current.GetType().Name + suffix);
            }
        }

        if (includesApplication)
        {
            scopes.Add(nameof(Application));
        }

        return scopes.Count == 0 ? "(none)" : string.Join(" -> ", scopes);
    }
}
