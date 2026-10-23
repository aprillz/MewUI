using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>Attached properties that describe an element to assistive technology and automation clients.</summary>
public static class AutomationProperties
{
    /// <summary>The name a reader announces for the element, used before any name the control derives itself.</summary>
    public static readonly MewProperty<string> NameProperty =
        MewProperty<string>.Register<UIElement>("AutomationProperties.Name", string.Empty);

    /// <summary>A stable identifier automation clients find the element by; it is not announced.</summary>
    public static readonly MewProperty<string> AutomationIdProperty =
        MewProperty<string>.Register<UIElement>("AutomationProperties.AutomationId", string.Empty);

    /// <summary>Extra guidance a reader can announce after the name.</summary>
    public static readonly MewProperty<string> HelpTextProperty =
        MewProperty<string>.Register<UIElement>("AutomationProperties.HelpText", string.Empty);

    /// <summary>An element whose text names this one, such as a label beside an edit box.</summary>
    public static readonly MewProperty<UIElement?> LabeledByProperty =
        MewProperty<UIElement?>.Register<UIElement>("AutomationProperties.LabeledBy", null);

    /// <summary>Which views of the automation tree the element appears in.</summary>
    public static readonly MewProperty<AccessibilityView> AccessibilityViewProperty =
        MewProperty<AccessibilityView>.Register<UIElement>("AutomationProperties.AccessibilityView", AccessibilityView.Control);

    public static string GetName(UIElement element) => element.GetAttachedValue(NameProperty);

    public static void SetName(UIElement element, string value) => element.SetAttachedValue(NameProperty, value ?? string.Empty);

    public static string GetAutomationId(UIElement element) => element.GetAttachedValue(AutomationIdProperty);

    public static void SetAutomationId(UIElement element, string value) => element.SetAttachedValue(AutomationIdProperty, value ?? string.Empty);

    public static string GetHelpText(UIElement element) => element.GetAttachedValue(HelpTextProperty);

    public static void SetHelpText(UIElement element, string value) => element.SetAttachedValue(HelpTextProperty, value ?? string.Empty);

    public static UIElement? GetLabeledBy(UIElement element) => element.GetAttachedValue(LabeledByProperty);

    public static void SetLabeledBy(UIElement element, UIElement? value) => element.SetAttachedValue(LabeledByProperty, value);

    public static AccessibilityView GetAccessibilityView(UIElement element) => element.GetAttachedValue(AccessibilityViewProperty);

    public static void SetAccessibilityView(UIElement element, AccessibilityView value) => element.SetAttachedValue(AccessibilityViewProperty, value);
}
