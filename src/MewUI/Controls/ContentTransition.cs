using Aprillz.MewUI.Animation;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Specifies the kind of visual transition.
/// </summary>
public enum ContentTransitionKind
{
    /// <summary>No animation — content swaps immediately.</summary>
    None,

    /// <summary>Cross-fade between old and new content.</summary>
    Fade,

    /// <summary>Slide old content out and new content in.</summary>
    Slide,

    /// <summary>Scale old content down and new content up.</summary>
    Scale,

    /// <summary>Rotate old content out and new content in.</summary>
    Rotate,
}

/// <summary>
/// Specifies the direction for a slide transition.
/// </summary>
public enum SlideDirection
{
    Left,
    Right,
    Up,
    Down,
}

/// <summary>
/// Defines a visual transition applied when content changes in a <see cref="TransitionContentControl"/>.
/// </summary>
public sealed class ContentTransition
{
    /// <summary>
    /// Gets the transition kind.
    /// </summary>
    public ContentTransitionKind Kind { get; init; } = ContentTransitionKind.Fade;

    /// <summary>
    /// Gets the slide direction. Only used when <see cref="Kind"/> is <see cref="ContentTransitionKind.Slide"/>.
    /// </summary>
    public SlideDirection Direction { get; init; } = SlideDirection.Left;

    /// <summary>
    /// Gets the animation duration.
    /// </summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets the delay before the animation starts.
    /// </summary>
    public TimeSpan Delay { get; init; } = TimeSpan.Zero;

    /// <summary>
    /// Gets the easing function applied to the animation progress.
    /// </summary>
    public Func<double, double> Easing { get; init; } = Animation.Easing.Default;

    /// <summary>
    /// Creates a fade transition.
    /// </summary>
    public static ContentTransition CreateFade(int durationMs = 250, int delayMs = 0) =>
        new()
        {
            Kind = ContentTransitionKind.Fade,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Delay = TimeSpan.FromMilliseconds(delayMs),
        };

    /// <summary>
    /// Creates a slide transition in the specified direction.
    /// </summary>
    public static ContentTransition CreateSlide(SlideDirection direction, int durationMs = 250, int delayMs = 0) =>
        new()
        {
            Kind = ContentTransitionKind.Slide,
            Direction = direction,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Delay = TimeSpan.FromMilliseconds(delayMs),
        };

    /// <summary>
    /// Creates a scale transition (zoom out old, zoom in new).
    /// </summary>
    public static ContentTransition CreateScale(int durationMs = 250, int delayMs = 0) =>
        new()
        {
            Kind = ContentTransitionKind.Scale,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Delay = TimeSpan.FromMilliseconds(delayMs),
        };

    /// <summary>
    /// Creates a rotate transition.
    /// </summary>
    public static ContentTransition CreateRotate(int durationMs = 300, int delayMs = 0) =>
        new()
        {
            Kind = ContentTransitionKind.Rotate,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            Delay = TimeSpan.FromMilliseconds(delayMs),
        };

    /// <summary>
    /// A transition that swaps content immediately with no animation.
    /// </summary>
    public static ContentTransition CreateNone() => new() { Kind = ContentTransitionKind.None };
}
