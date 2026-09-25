namespace Aprillz.MewUI.Rendering;

/// <summary>Describes a shape that can be represented as a path.</summary>
public abstract class Geometry : IFreezable
{
    private bool _isFrozen;

    /// <inheritdoc/>
    public bool IsFrozen => _isFrozen;

    /// <inheritdoc/>
    public void Freeze()
    {
        if (_isFrozen)
        {
            return;
        }

        _isFrozen = true;
        OnFreeze();
    }

    /// <summary>Returns this geometry as a path in its local coordinate space.</summary>
    public abstract PathGeometry GetPathGeometry();

    /// <summary>Runs when the geometry becomes immutable.</summary>
    protected virtual void OnFreeze() { }
}

/// <summary>Describes a rectangle with optional rounded corners.</summary>
public sealed class RectangleGeometry : Geometry
{
    private Rect _rect;
    private double _radiusX;
    private double _radiusY;

    /// <summary>Gets or sets the rectangle.</summary>
    public Rect Rect
    {
        get => _rect;
        set
        {
            FreezableHelper.ThrowIfFrozen(this);
            _rect = value;
        }
    }

    /// <summary>Gets or sets the horizontal corner radius.</summary>
    public double RadiusX
    {
        get => _radiusX;
        set
        {
            FreezableHelper.ThrowIfFrozen(this);
            _radiusX = value;
        }
    }

    /// <summary>Gets or sets the vertical corner radius.</summary>
    public double RadiusY
    {
        get => _radiusY;
        set
        {
            FreezableHelper.ThrowIfFrozen(this);
            _radiusY = value;
        }
    }

    /// <summary>Creates a rectangle geometry.</summary>
    public RectangleGeometry() { }

    /// <summary>Creates a rectangle geometry for the specified rectangle.</summary>
    public RectangleGeometry(Rect rect)
    {
        _rect = rect;
    }

    /// <inheritdoc/>
    public override PathGeometry GetPathGeometry()
    {
        if (_rect.Width <= 0 || _rect.Height <= 0)
        {
            return new PathGeometry();
        }

        if (_radiusX > 0 || _radiusY > 0)
        {
            return PathGeometry.FromRoundedRect(_rect, _radiusX, _radiusY);
        }

        return PathGeometry.FromRect(_rect);
    }
}

/// <summary>Describes an ellipse bounded by a rectangle.</summary>
public sealed class EllipseGeometry : Geometry
{
    private Rect _bounds;

    /// <summary>Gets or sets the rectangle that bounds the ellipse.</summary>
    public Rect Bounds
    {
        get => _bounds;
        set
        {
            FreezableHelper.ThrowIfFrozen(this);
            _bounds = value;
        }
    }

    /// <summary>Creates an ellipse geometry.</summary>
    public EllipseGeometry() { }

    /// <summary>Creates an ellipse geometry for the specified bounds.</summary>
    public EllipseGeometry(Rect bounds)
    {
        _bounds = bounds;
    }

    /// <inheritdoc/>
    public override PathGeometry GetPathGeometry()
    {
        if (_bounds.Width <= 0 || _bounds.Height <= 0)
        {
            return new PathGeometry();
        }

        return PathGeometry.FromEllipse(_bounds);
    }
}

/// <summary>Describes a straight line segment.</summary>
public sealed class LineGeometry : Geometry
{
    private Point _startPoint;
    private Point _endPoint;

    /// <summary>Gets or sets the line's start point.</summary>
    public Point StartPoint
    {
        get => _startPoint;
        set
        {
            FreezableHelper.ThrowIfFrozen(this);
            _startPoint = value;
        }
    }

    /// <summary>Gets or sets the line's end point.</summary>
    public Point EndPoint
    {
        get => _endPoint;
        set
        {
            FreezableHelper.ThrowIfFrozen(this);
            _endPoint = value;
        }
    }

    /// <summary>Creates a line geometry.</summary>
    public LineGeometry() { }

    /// <summary>Creates a line geometry between two points.</summary>
    public LineGeometry(Point startPoint, Point endPoint)
    {
        _startPoint = startPoint;
        _endPoint = endPoint;
    }

    /// <inheritdoc/>
    public override PathGeometry GetPathGeometry()
    {
        var geometry = new PathGeometry();
        geometry.MoveTo(_startPoint);
        geometry.LineTo(_endPoint);
        geometry.Freeze();
        return geometry;
    }
}

/// <summary>Combines child geometries under one fill rule.</summary>
public sealed class GeometryGroup : Geometry
{
    private readonly List<Geometry> _children = [];
    private FillRule _fillRule = FillRule.NonZero;

    /// <summary>Gets or sets the fill rule applied to the child geometries.</summary>
    public FillRule FillRule
    {
        get => _fillRule;
        set
        {
            FreezableHelper.ThrowIfFrozen(this);
            _fillRule = value;
        }
    }

    /// <summary>Gets the child geometries.</summary>
    public IReadOnlyList<Geometry> Children => _children;

    /// <summary>Adds a child geometry.</summary>
    public void Add(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        FreezableHelper.ThrowIfFrozen(this);
        _children.Add(geometry);
    }

    /// <inheritdoc/>
    public override PathGeometry GetPathGeometry()
    {
        var geometry = new PathGeometry { FillRule = _fillRule };
        foreach (Geometry child in _children)
        {
            Append(geometry, child.GetPathGeometry());
        }

        geometry.Freeze();
        return geometry;
    }

    /// <inheritdoc/>
    protected override void OnFreeze()
    {
        foreach (Geometry child in _children)
        {
            child.Freeze();
        }
    }

    private static void Append(PathGeometry destination, PathGeometry source)
    {
        foreach (PathCommand command in source.Commands)
        {
            switch (command.Type)
            {
                case PathCommandType.MoveTo:
                    destination.MoveTo(command.X0, command.Y0);
                    break;
                case PathCommandType.LineTo:
                    destination.LineTo(command.X0, command.Y0);
                    break;
                case PathCommandType.BezierTo:
                    destination.BezierTo(command.X0, command.Y0, command.X1, command.Y1, command.X2, command.Y2);
                    break;
                case PathCommandType.Close:
                    destination.Close();
                    break;
            }
        }
    }
}
