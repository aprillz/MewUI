using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// Provides bindable default presentation metadata for a <see cref="Command"/>.
/// </summary>
/// <remarks>
/// Presentation is independent of command routing, handlers, targets and input gestures. A single
/// underscore in <see cref="AccessText"/> marks the following character as an access key; a double
/// underscore represents a literal underscore.
/// </remarks>
public sealed class CommandPresentation : MewObject
{
    public static readonly MewProperty<string?> AccessTextProperty =
        MewProperty<string?>.Register<CommandPresentation>(nameof(AccessText), null,
            MewPropertyOptions.None,
            static (self, _, _) => self.OnAccessTextChanged());

    public static readonly MewProperty<IconTemplate?> IconProperty =
        MewProperty<IconTemplate?>.Register<CommandPresentation>(nameof(Icon), null,
            MewPropertyOptions.None,
            static (self, _, _) => self.Invalidated?.Invoke());

    // No invalidation: nothing materializes a description in advance, so there is nothing built from it
    // to throw away. A tooltip that carries one is composed when it is about to appear.
    public static readonly MewProperty<string?> DescriptionProperty =
        MewProperty<string?>.Register<CommandPresentation>(nameof(Description), null);

    private static readonly MewPropertyKey<string?> DisplayTextPropertyKey =
        MewProperty<string?>.RegisterReadOnly<CommandPresentation>(nameof(DisplayText), null);

    /// <summary>
    /// The label with access-key markers removed, as a property so a presenter can bind to it rather
    /// than re-read it whenever <see cref="AccessText"/> changes.
    /// </summary>
    public static readonly MewProperty<string?> DisplayTextProperty = DisplayTextPropertyKey.Property;

    private char _accessKey;
    private int _accessKeyIndex = -1;

    public CommandPresentation(string? accessText = null, IconTemplate? icon = null)
    {
        if (accessText != null)
        {
            AccessText = accessText;
        }

        if (icon != null)
        {
            Icon = icon;
        }
    }

    /// <summary>
    /// Gets or sets the default label including optional access-key markers.
    /// </summary>
    public string? AccessText
    {
        get => GetValue(AccessTextProperty);
        set => SetValue(AccessTextProperty, value);
    }

    /// <inheritdoc cref="DisplayTextProperty"/>
    public string? DisplayText => GetValue(DisplayTextProperty);

    /// <summary>
    /// Gets the current access key, or the null character when none is defined.
    /// </summary>
    public char AccessKey => _accessKey;

    /// <summary>
    /// Gets the underline index in <see cref="DisplayText"/>, or -1 when no access key exists.
    /// </summary>
    public int AccessKeyIndex => _accessKeyIndex;

    /// <summary>
    /// Gets or sets the reusable icon template. Each presenter builds its own visual at its own size.
    /// </summary>
    public IconTemplate? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets or sets a sentence saying what running the command does. Material for a presenter to use,
    /// not a tooltip: which surfaces show it is the presenter's choice.
    /// </summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// Raised when what a presenter built from this presentation is stale. Coarse on purpose: a presenter
    /// assembles one visual out of several of these values, so it has one thing to rebuild.
    /// </summary>
    internal event Action? Invalidated;

    private void OnAccessTextChanged()
    {
        var rawText = AccessText;
        if (rawText == null)
        {
            SetValue(DisplayTextPropertyKey, null);
            _accessKey = default;
            _accessKeyIndex = -1;
        }
        else
        {
            bool hasAccessKey = AccessKeyHelper.TryParse(rawText, out var accessKey, out var displayText);
            SetValue(DisplayTextPropertyKey, displayText);
            _accessKey = hasAccessKey ? accessKey : default;
            _accessKeyIndex = hasAccessKey ? AccessKeyHelper.GetUnderlineIndex(rawText) : -1;
        }

        Invalidated?.Invoke();
    }
}

internal static class CommandPresentationWeakEvents
{
    internal static readonly WeakEventKey<CommandPresentation, Action> Invalidated = new(
        static (source, handler) => source.Invalidated += handler,
        static (source, handler) => source.Invalidated -= handler);
}
