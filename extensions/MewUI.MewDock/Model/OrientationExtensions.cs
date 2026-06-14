namespace Aprillz.MewUI.MewDock.Model;

/// <summary>Helpers for the core <see cref="Orientation"/> enum (port of FlexLayout's Orientation.flip).</summary>
internal static class OrientationExtensions
{
    internal static Orientation Flip(this Orientation orientation) =>
        orientation == Orientation.Horizontal ? Orientation.Vertical : Orientation.Horizontal;
}
