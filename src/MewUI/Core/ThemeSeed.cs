namespace Aprillz.MewUI;

public record ThemeSeed
{
    public required Color WindowBackground { get; init; }

    public required Color WindowText { get; init; }

    public required Color ControlBackground { get; init; }

    public required Color ButtonFace { get; init; }

    public required Color ButtonDisabledBackground { get; init; }
}
