using System.Numerics;

using Aprillz.MewUI.Rendering;

using MewGeometry = Aprillz.MewUI.Rendering.Geometry;

namespace Aprillz.MewUI.Geometry;

/// <summary>Provides backend-independent geometric operations.</summary>
public static class PathGeometryOperations
{
    /// <summary>Returns the tight axis-aligned bounds of the geometry.</summary>
    public static Rect GetTightBounds(this MewGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return GeometryFlattener.GetTightBounds(ToPathGeometry(geometry));
    }

    /// <summary>Returns the tight axis-aligned bounds of the stroked geometry.</summary>
    public static Rect GetRenderBounds(this MewGeometry geometry, Pen pen) =>
        GetRenderBounds(geometry, pen, GeometryFlattener.DEFAULT_TOLERANCE, ToleranceType.Absolute);

    /// <summary>Returns the tight axis-aligned bounds of the stroked geometry using the specified tolerance.</summary>
    public static Rect GetRenderBounds(this MewGeometry geometry, Pen pen, double tolerance, ToleranceType type)
    {
        Validate(geometry, pen);
        return StrokeWidener.GetBounds(ToPathGeometry(geometry), pen, tolerance, type);
    }

    /// <summary>Returns a straight-line approximation of the geometry.</summary>
    public static PathGeometry GetFlattenedPathGeometry(this MewGeometry geometry) =>
        GetFlattenedPathGeometry(geometry, GeometryFlattener.DEFAULT_TOLERANCE, ToleranceType.Absolute);

    /// <summary>Returns a straight-line approximation of the geometry using the specified tolerance.</summary>
    public static PathGeometry GetFlattenedPathGeometry(
        this MewGeometry geometry,
        double tolerance,
        ToleranceType type)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return GeometryFlattener.ToPath(GeometryFlattener.Flatten(ToPathGeometry(geometry), tolerance, type));
    }

    /// <summary>Determines whether the filled geometry contains a point.</summary>
    public static bool FillContains(this MewGeometry geometry, Point hitPoint) =>
        FillContains(geometry, hitPoint, GeometryFlattener.DEFAULT_TOLERANCE, ToleranceType.Absolute);

    /// <summary>Determines whether the filled geometry contains a point using the specified tolerance.</summary>
    public static bool FillContains(
        this MewGeometry geometry,
        Point hitPoint,
        double tolerance,
        ToleranceType type)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return GeometryFlattener.FillContains(ToPathGeometry(geometry), hitPoint, tolerance, type);
    }

    /// <summary>Determines whether the filled geometry fully contains another geometry.</summary>
    public static bool FillContains(this MewGeometry geometry, MewGeometry other) =>
        FillContains(geometry, other, GeometryFlattener.DEFAULT_TOLERANCE, ToleranceType.Absolute);

    /// <summary>Determines whether the filled geometry fully contains another geometry using the specified tolerance.</summary>
    public static bool FillContains(
        this MewGeometry geometry,
        MewGeometry other,
        double tolerance,
        ToleranceType type) =>
        FillContainsWithDetail(geometry, other, tolerance, type) == IntersectionDetail.FullyContains;

    /// <summary>Returns the spatial relationship between two filled geometries.</summary>
    public static IntersectionDetail FillContainsWithDetail(this MewGeometry geometry, MewGeometry other) =>
        FillContainsWithDetail(
            geometry,
            other,
            GeometryFlattener.DEFAULT_TOLERANCE,
            ToleranceType.Absolute);

    /// <summary>Returns the spatial relationship between two filled geometries using the specified tolerance.</summary>
    public static IntersectionDetail FillContainsWithDetail(
        this MewGeometry geometry,
        MewGeometry other,
        double tolerance,
        ToleranceType type)
    {
        Validate(geometry, other);
        PathGeometry flattened = geometry.GetFlattenedPathGeometry(tolerance, type);
        PathGeometry flattenedOther = other.GetFlattenedPathGeometry(tolerance, type);
        return ToIntersectionDetail(ClipperGeometry.GetRelation(flattened, flattenedOther));
    }

    /// <summary>Determines whether two filled geometries have a non-empty intersection.</summary>
    public static bool Intersects(this MewGeometry geometry, MewGeometry other) =>
        FillContainsWithDetail(geometry, other) != IntersectionDetail.Empty;

    /// <summary>Determines whether the first filled geometry fully contains the second geometry.</summary>
    public static bool Encloses(this MewGeometry geometry, MewGeometry other) =>
        FillContainsWithDetail(geometry, other) == IntersectionDetail.FullyContains;

    /// <summary>Returns the area of the filled geometry.</summary>
    public static double GetArea(this MewGeometry geometry) =>
        GetArea(geometry, GeometryFlattener.DEFAULT_TOLERANCE, ToleranceType.Absolute);

    /// <summary>Returns the area of the filled geometry using the specified tolerance.</summary>
    public static double GetArea(this MewGeometry geometry, double tolerance, ToleranceType type)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return ClipperGeometry.GetArea(geometry.GetFlattenedPathGeometry(tolerance, type));
    }

    /// <summary>Determines whether the stroked geometry contains a point.</summary>
    public static bool StrokeContains(this MewGeometry geometry, Pen pen, Point hitPoint) =>
        StrokeContains(
            geometry,
            pen,
            hitPoint,
            GeometryFlattener.DEFAULT_TOLERANCE,
            ToleranceType.Absolute);

    /// <summary>Determines whether the stroked geometry contains a point using the specified tolerance.</summary>
    public static bool StrokeContains(
        this MewGeometry geometry,
        Pen pen,
        Point hitPoint,
        double tolerance,
        ToleranceType type)
    {
        Validate(geometry, pen);
        return StrokeWidener.Contains(ToPathGeometry(geometry), pen, hitPoint, tolerance, type);
    }

    /// <summary>Returns the spatial relationship between a stroked geometry and a filled geometry.</summary>
    public static IntersectionDetail StrokeContainsWithDetail(
        this MewGeometry geometry,
        Pen pen,
        MewGeometry other) =>
        StrokeContainsWithDetail(
            geometry,
            pen,
            other,
            GeometryFlattener.DEFAULT_TOLERANCE,
            ToleranceType.Absolute);

    /// <summary>Returns the spatial relationship between a stroked geometry and a filled geometry using the specified tolerance.</summary>
    public static IntersectionDetail StrokeContainsWithDetail(
        this MewGeometry geometry,
        Pen pen,
        MewGeometry other,
        double tolerance,
        ToleranceType type)
    {
        Validate(geometry, pen);
        ArgumentNullException.ThrowIfNull(other);
        PathGeometry widened = StrokeWidener.Widen(ToPathGeometry(geometry), pen, tolerance, type);
        PathGeometry flattenedOther = other.GetFlattenedPathGeometry(tolerance, type);
        return ToIntersectionDetail(ClipperGeometry.GetRelation(widened, flattenedOther));
    }

    /// <summary>Returns the filled outline of the stroked geometry.</summary>
    public static PathGeometry GetWidenedPathGeometry(this MewGeometry geometry, Pen pen) =>
        GetWidenedPathGeometry(
            geometry,
            pen,
            GeometryFlattener.DEFAULT_TOLERANCE,
            ToleranceType.Absolute);

    /// <summary>Returns the filled outline of the stroked geometry using the specified tolerance.</summary>
    public static PathGeometry GetWidenedPathGeometry(
        this MewGeometry geometry,
        Pen pen,
        double tolerance,
        ToleranceType type)
    {
        Validate(geometry, pen);
        return StrokeWidener.Widen(ToPathGeometry(geometry), pen, tolerance, type);
    }

    /// <summary>Returns the boundary of the filled geometry as closed straight-line figures.</summary>
    public static PathGeometry GetOutlinedPathGeometry(this MewGeometry geometry) =>
        GetOutlinedPathGeometry(geometry, GeometryFlattener.DEFAULT_TOLERANCE, ToleranceType.Absolute);

    /// <summary>Returns the boundary of the filled geometry using the specified tolerance.</summary>
    public static PathGeometry GetOutlinedPathGeometry(
        this MewGeometry geometry,
        double tolerance,
        ToleranceType type)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return ClipperGeometry.GetOutline(geometry.GetFlattenedPathGeometry(tolerance, type));
    }

    /// <summary>Combines two filled geometries with a Boolean operation.</summary>
    public static PathGeometry Combine(
        MewGeometry geometry1,
        MewGeometry geometry2,
        GeometryCombineMode mode) =>
        Combine(
            geometry1,
            geometry2,
            mode,
            Matrix3x2.Identity,
            GeometryFlattener.DEFAULT_TOLERANCE,
            ToleranceType.Absolute);

    /// <summary>Combines two transformed filled geometries with a Boolean operation.</summary>
    public static PathGeometry Combine(
        MewGeometry geometry1,
        MewGeometry geometry2,
        GeometryCombineMode mode,
        Matrix3x2 transform) =>
        Combine(
            geometry1,
            geometry2,
            mode,
            transform,
            GeometryFlattener.DEFAULT_TOLERANCE,
            ToleranceType.Absolute);

    /// <summary>Combines two transformed filled geometries with a Boolean operation using the specified tolerance.</summary>
    public static PathGeometry Combine(
        MewGeometry geometry1,
        MewGeometry geometry2,
        GeometryCombineMode mode,
        Matrix3x2 transform,
        double tolerance,
        ToleranceType type)
    {
        Validate(geometry1, geometry2);
        PathGeometry flattened1 = ToPathGeometry(geometry1).Transform(transform).GetFlattenedPathGeometry(tolerance, type);
        PathGeometry flattened2 = ToPathGeometry(geometry2).Transform(transform).GetFlattenedPathGeometry(tolerance, type);
        return ClipperGeometry.Combine(flattened1, flattened2, ToBooleanOperation(mode));
    }

    private static GeometryBooleanOperation ToBooleanOperation(GeometryCombineMode mode) => mode switch
    {
        GeometryCombineMode.Union => GeometryBooleanOperation.Union,
        GeometryCombineMode.Intersect => GeometryBooleanOperation.Intersect,
        GeometryCombineMode.Xor => GeometryBooleanOperation.Xor,
        GeometryCombineMode.Exclude => GeometryBooleanOperation.Exclude,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static IntersectionDetail ToIntersectionDetail(GeometryRelation relation) => relation switch
    {
        GeometryRelation.Empty => IntersectionDetail.Empty,
        GeometryRelation.FullyInside => IntersectionDetail.FullyInside,
        GeometryRelation.FullyContains => IntersectionDetail.FullyContains,
        _ => IntersectionDetail.Intersects,
    };

    private static void Validate(MewGeometry geometry, Pen pen)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(pen);
    }

    private static void Validate(MewGeometry geometry, MewGeometry other)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(other);
    }

    private static PathGeometry ToPathGeometry(MewGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return geometry.GetPathGeometry();
    }
}
