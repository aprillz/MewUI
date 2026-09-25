namespace Aprillz.MewUI.Geometry;

/// <summary>Specifies how a geometry-operation tolerance is interpreted.</summary>
public enum ToleranceType
{
    /// <summary>The tolerance is measured in device-independent pixels.</summary>
    Absolute,

    /// <summary>The tolerance is a fraction of the geometry's largest bounds dimension.</summary>
    Relative,
}

/// <summary>Describes the spatial relationship between two filled geometries.</summary>
public enum IntersectionDetail
{
    /// <summary>The geometries do not intersect.</summary>
    Empty,

    /// <summary>The first geometry is entirely inside the second geometry.</summary>
    FullyInside,

    /// <summary>The first geometry entirely contains the second geometry.</summary>
    FullyContains,

    /// <summary>The geometries overlap without either one containing the other.</summary>
    Intersects,
}

/// <summary>Specifies the Boolean operation used to combine two geometries.</summary>
public enum GeometryCombineMode
{
    /// <summary>Keeps points contained by either geometry.</summary>
    Union,

    /// <summary>Keeps points contained by both geometries.</summary>
    Intersect,

    /// <summary>Keeps points contained by exactly one geometry.</summary>
    Xor,

    /// <summary>Keeps points in the first geometry that are not in the second geometry.</summary>
    Exclude,
}
