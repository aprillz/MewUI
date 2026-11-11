namespace Aprillz.MewUI.MewCharts.Painting.ImageFilters;

/// <summary>An effect applied to what a <see cref="MewPaint"/> draws.</summary>
public abstract class ImageFilter
{
    /// <summary>Returns a copy of this filter.</summary>
    public abstract ImageFilter Clone();
}
