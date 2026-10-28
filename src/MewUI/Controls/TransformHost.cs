using System.Numerics;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Hit testing and layout math shared by the decorators that draw one child under a transform. Every
/// matrix here maps window coordinates before the transform to window coordinates after it.
/// </summary>
internal static class TransformHost
{
    private const double DOUBLE_EPSILON = 2.2204460492503131e-16;

    // Matches the zero tolerance of the WPF layout math these helpers reproduce.
    private const double ZERO_TOLERANCE = 10 * DOUBLE_EPSILON;

    /// <summary>Returns <paramref name="transform"/> applied about the window point (<paramref name="originX"/>, <paramref name="originY"/>).</summary>
    internal static Matrix3x2 About(Matrix3x2 transform, double originX, double originY)
        => Matrix3x2.CreateTranslation((float)-originX, (float)-originY) *
            transform *
            Matrix3x2.CreateTranslation((float)originX, (float)originY);

    /// <summary>Returns <paramref name="transform"/> without its translation.</summary>
    internal static Matrix3x2 WithoutTranslation(Matrix3x2 transform)
        => new(transform.M11, transform.M12, transform.M21, transform.M22, 0, 0);

    /// <summary>True when glyphs drawn under <paramref name="transform"/> still land on whole pixels: translation and positive whole-number scale only.</summary>
    internal static bool KeepsTextPixelSnap(Matrix3x2 transform)
        => transform.M12 == 0 && transform.M21 == 0 && IsPositiveWholeNumber(transform.M11) && IsPositiveWholeNumber(transform.M22);

    /// <summary>
    /// Hit tests <paramref name="child"/> at the point the transform carried to <paramref name="point"/>, including the parts
    /// the transform draws outside the host; a point inside the host that misses the child hits the host itself.
    /// </summary>
    internal static UIElement? HitTest(UIElement host, UIElement? child, Matrix3x2 transform, Point point)
    {
        if (!host.IsVisible || !host.IsHitTestVisible)
        {
            return null;
        }

        // The child is drawn unclipped, so what it draws past the host must stay clickable.
        if (child != null && MatrixGeneralTransform.FromMatrix(transform).Invert().TryTransform(point, out var mapped))
        {
            var childHit = child.HitTest(mapped);
            if (childHit != null)
            {
                return childHit;
            }
        }

        if (host.Bounds.Contains(point))
        {
            return host;
        }
        else
        {
            return null;
        }
    }

    /// <summary>Size of the axis-aligned bounding box of a <paramref name="size"/> box at the origin after <paramref name="linear"/>.</summary>
    internal static Size TransformedSize(Matrix3x2 linear, Size size)
    {
        var bounds = MatrixGeneralTransform.FromMatrix(WithoutTranslation(linear)).TransformBounds(new Rect(size));
        return bounds.Size;
    }

    /// <summary>
    /// The largest-area untransformed size whose transformed bounding box fits <paramref name="transformSpaceBounds"/>;
    /// an infinite side is treated as equal to the other side. Same result as WPF's layout transform measure.
    /// </summary>
    internal static Size FindMaximalAreaLocalSpaceRect(Matrix3x2 linear, Size transformSpaceBounds)
    {
        double xConstraint = transformSpaceBounds.Width;
        double yConstraint = transformSpaceBounds.Height;

        if (IsZero(xConstraint) || IsZero(yConstraint))
        {
            return new Size(0, 0);
        }

        bool xConstraintInfinite = double.IsInfinity(xConstraint);
        bool yConstraintInfinite = double.IsInfinity(yConstraint);

        if (xConstraintInfinite && yConstraintInfinite)
        {
            return new Size(double.PositiveInfinity, double.PositiveInfinity);
        }
        else if (xConstraintInfinite)
        {
            xConstraint = yConstraint;
        }
        else if (yConstraintInfinite)
        {
            yConstraint = xConstraint;
        }

        double m11 = linear.M11;
        double m12 = linear.M12;
        double m21 = linear.M21;
        double m22 = linear.M22;

        if (m11 * m22 - m12 * m21 == 0)
        {
            return new Size(0, 0);
        }

        double width;
        double height;

        if (IsZero(m12) || IsZero(m21))
        {
            double yCoverD = yConstraintInfinite ? double.PositiveInfinity : Math.Abs(yConstraint / m22);
            double xCoverA = xConstraintInfinite ? double.PositiveInfinity : Math.Abs(xConstraint / m11);

            if (IsZero(m12))
            {
                if (IsZero(m21))
                {
                    height = yCoverD;
                    width = xCoverA;
                }
                else
                {
                    height = Math.Min(0.5 * Math.Abs(xConstraint / m21), yCoverD);
                    width = xCoverA - ((m21 * height) / m11);
                }
            }
            else
            {
                width = Math.Min(0.5 * Math.Abs(yConstraint / m12), xCoverA);
                height = yCoverD - ((m12 * width) / m22);
            }
        }
        else if (IsZero(m11) || IsZero(m22))
        {
            double yCoverB = Math.Abs(yConstraint / m12);
            double xCoverC = Math.Abs(xConstraint / m21);

            if (IsZero(m11))
            {
                if (IsZero(m22))
                {
                    height = xCoverC;
                    width = yCoverB;
                }
                else
                {
                    height = Math.Min(0.5 * Math.Abs(yConstraint / m22), xCoverC);
                    width = yCoverB - ((m22 * height) / m12);
                }
            }
            else
            {
                width = Math.Min(0.5 * Math.Abs(xConstraint / m11), yCoverB);
                height = xCoverC - ((m11 * width) / m21);
            }
        }
        else
        {
            double xCoverA = Math.Abs(xConstraint / m11);
            double xCoverC = Math.Abs(xConstraint / m21);
            double yCoverB = Math.Abs(yConstraint / m12);
            double yCoverD = Math.Abs(yConstraint / m22);

            width = Math.Min(yCoverB, xCoverA) * 0.5;
            height = Math.Min(xCoverC, yCoverD) * 0.5;

            if ((GreaterThanOrClose(xCoverA, yCoverB) && LessThanOrClose(xCoverC, yCoverD)) ||
                (LessThanOrClose(xCoverA, yCoverB) && GreaterThanOrClose(xCoverC, yCoverD)))
            {
                // The constraint lines cross, so the midpoint box leaves room; grow it until one side touches.
                var transformed = TransformedSize(linear, new Size(width, height));
                double expandFactor = Math.Min(xConstraint / transformed.Width, yConstraint / transformed.Height);
                if (!double.IsNaN(expandFactor) && !double.IsInfinity(expandFactor))
                {
                    width *= expandFactor;
                    height *= expandFactor;
                }
            }
        }

        return new Size(width, height);
    }

    private static bool IsPositiveWholeNumber(float value) => value >= 1 && value == MathF.Round(value);

    internal static bool IsZero(double value) => Math.Abs(value) < ZERO_TOLERANCE;

    private static bool AreClose(double first, double second)
    {
        if (first == second)
        {
            return true;
        }

        double tolerance = (Math.Abs(first) + Math.Abs(second) + 10.0) * DOUBLE_EPSILON;
        double difference = first - second;
        return -tolerance < difference && tolerance > difference;
    }

    private static bool GreaterThanOrClose(double first, double second) => first > second || AreClose(first, second);

    private static bool LessThanOrClose(double first, double second) => first < second || AreClose(first, second);
}
