namespace Aprillz.MewUI.Markdown;

/// <summary>Provides fluent configuration for Markdown controls.</summary>
/// <remarks>
/// A delegate-typed property is invocable, so <c>presenter.CodeBlockFactory(x)</c> binds to the property and never reaches an
/// extension; such properties use the <c>With</c> prefix.
/// </remarks>
public static class MarkdownExtensions
{
    /// <summary>Sets <see cref="MarkdownPresenter.CodeBlockFactory"/> and returns the control.</summary>
    public static T WithCodeBlockFactory<T>(this T control, [FluentSetsMember(nameof(MarkdownPresenter.CodeBlockFactory))] Func<string, string?, Controls.FrameworkElement?>? factory) where T : MarkdownPresenter { control.CodeBlockFactory = factory; return control; }
}
