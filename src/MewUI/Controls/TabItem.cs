namespace Aprillz.MewUI.Controls;

/// <summary>
/// Represents a tab item with header and content.
/// </summary>
public sealed class TabItem : MewObject
{
    public static readonly MewProperty<Element?> HeaderProperty =
        MewProperty<Element?>.Register<TabItem>(nameof(Header), null,
            changed: static (self, _, _) => self.NotifyChanged(TabItemChange.Header));

    public static readonly MewProperty<Element?> ContentProperty =
        MewProperty<Element?>.Register<TabItem>(nameof(Content), null,
            changed: static (self, _, _) => self.NotifyChanged(TabItemChange.Content),
            validate: static (self, value) => self.Owner?.ValidateTabContent(value));

    public static readonly MewProperty<bool> IsEnabledProperty =
        MewProperty<bool>.Register<TabItem>(nameof(IsEnabled), true,
            changed: static (self, _, _) => self.NotifyChanged(TabItemChange.IsEnabled));

    public static readonly MewProperty<string?> HeaderTextProperty =
        MewProperty<string?>.Register<TabItem>(nameof(HeaderText), null,
            changed: static (self, _, _) => self.NotifyChanged(TabItemChange.HeaderText));

    /// <summary>
    /// Gets or sets the tab header element.
    /// </summary>
    public Element? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the tab content element.
    /// </summary>
    public Element? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the tab is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the semantic header text used by overflow menus and accessibility surfaces.
    /// </summary>
    public string? HeaderText
    {
        get => GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    internal event Action<TabItem, TabItemChange>? Changed;

    // The owning TabControl while this tab is registered. The tab's Content is a logical
    // child of that control (offscreen tabs stay owned); the control tracks the attached
    // element here so a later Content change can release the previous one.
    internal TabControl? Owner { get; set; }

    internal Element? AttachedContent { get; set; }

    private void NotifyChanged(TabItemChange change) => Changed?.Invoke(this, change);
}

internal enum TabItemChange
{
    Header,
    Content,
    IsEnabled,
    HeaderText,
}
