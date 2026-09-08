namespace Aprillz.MewUI.Markdown;

/// <summary>Provides fluent configuration for Markdown controls.</summary>
public static class MarkdownExtensions
{
    /// <summary>Sets the Markdown source.</summary>
    public static T Markdown<T>(this T control, string markdown) where T : MarkdownPresenter { control.Markdown = markdown; return control; }
    /// <summary>Sets immutable parsing options.</summary>
    public static T Options<T>(this T control, MarkdownOptions options) where T : MarkdownPresenter { control.Options = options; return control; }
    /// <summary>Sets document metrics and color overrides.</summary>
    public static T MarkdownTheme<T>(this T control, MarkdownTheme theme) where T : MarkdownPresenter { control.MarkdownTheme = theme; return control; }
    /// <summary>Sets the URI resolution base.</summary>
    public static T BaseUri<T>(this T control, Uri? baseUri) where T : MarkdownPresenter { control.BaseUri = baseUri; return control; }
    /// <summary>Sets the host-authorized image resolver.</summary>
    public static T ImageResolver<T>(this T control, IMarkdownImageResolver? resolver) where T : MarkdownPresenter { control.ImageResolver = resolver; return control; }
    /// <summary>Sets the optional owned code block presentation factory.</summary>
    public static T CodeBlockFactory<T>(this T control, Func<string, string?, Controls.FrameworkElement?>? factory) where T : MarkdownPresenter { control.CodeBlockFactory = factory; return control; }
    /// <summary>Subscribes to host-handled link requests.</summary>
    public static T OnLinkRequested<T>(this T control, Action<MarkdownLinkRequestedEventArgs> handler) where T : MarkdownPresenter { control.LinkRequested += handler; return control; }
}
