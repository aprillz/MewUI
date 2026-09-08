using Markdig;

namespace Aprillz.MewUI.Markdown;

/// <summary>Immutable parsing options; changing options reparses the document.</summary>
public sealed record MarkdownOptions
{
    /// <summary>Gets whether pipe tables are recognized.</summary>
    public bool UsePipeTables { get; init; } = true;
    /// <summary>Gets whether read-only task markers are recognized.</summary>
    public bool UseTaskLists { get; init; } = true;
    /// <summary>Gets whether bare URLs are recognized.</summary>
    public bool UseAutoLinks { get; init; } = true;
    /// <summary>Gets whether double-tilde strikethrough is recognized.</summary>
    public bool UseStrikethrough { get; init; } = true;
    /// <summary>Gets whether double-plus inserted text is recognized.</summary>
    public bool UseInserted { get; init; } = true;
    /// <summary>Gets whether double-equals marked text is recognized.</summary>
    public bool UseMarked { get; init; } = true;
    /// <summary>Gets whether definition lists are recognized.</summary>
    public bool UseDefinitionLists { get; init; } = true;
    /// <summary>Gets whether soft line breaks are displayed as new lines.</summary>
    public bool SoftBreakAsNewLine { get; init; }
    /// <summary>Gets an advanced hook that adds Markdig extensions to the pipeline; nodes it produces display as source text unless a renderer is registered for them.</summary>
    public Action<MarkdownPipelineBuilder>? ConfigurePipeline { get; init; }
}
