using System.Numerics;

namespace Svg;

public partial struct SvgViewBox
{
    public Matrix3x2 GetViewBoxTransform(SvgAspectRatio aspectRatio, ISvgRenderer renderer, SvgFragment frag)
    {
        var x = frag is null ? 0f : frag.X.ToDeviceValue(renderer, UnitRenderingType.Horizontal, frag);
        var y = frag is null ? 0f : frag.Y.ToDeviceValue(renderer, UnitRenderingType.Vertical, frag);

        if (Equals(Empty))
        {
            return Matrix3x2.CreateTranslation(x, y);
        }

        var width = frag is null ? Width : frag.Width.ToDeviceValue(renderer, UnitRenderingType.Horizontal, frag);
        var height = frag is null ? Height : frag.Height.ToDeviceValue(renderer, UnitRenderingType.Vertical, frag);

        var scaleX = width / Width;
        var scaleY = height / Height;
        var minX = -MinX * scaleX;
        var minY = -MinY * scaleY;

        aspectRatio ??= new SvgAspectRatio(SvgPreserveAspectRatio.xMidYMid);
        if (aspectRatio.Align != SvgPreserveAspectRatio.none)
        {
            var scale = aspectRatio.Slice ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            scaleX = scale;
            scaleY = scale;

            var viewMidX = (Width / 2f) * scaleX;
            var viewMidY = (Height / 2f) * scaleY;
            var midX = width / 2f;
            var midY = height / 2f;
            minX = -MinX * scaleX;
            minY = -MinY * scaleY;

            switch (aspectRatio.Align)
            {
                case SvgPreserveAspectRatio.xMidYMin:
                    minX += midX - viewMidX;
                    break;
                case SvgPreserveAspectRatio.xMaxYMin:
                    minX += width - (Width * scaleX);
                    break;
                case SvgPreserveAspectRatio.xMinYMid:
                    minY += midY - viewMidY;
                    break;
                case SvgPreserveAspectRatio.xMidYMid:
                    minX += midX - viewMidX;
                    minY += midY - viewMidY;
                    break;
                case SvgPreserveAspectRatio.xMaxYMid:
                    minX += width - (Width * scaleX);
                    minY += midY - viewMidY;
                    break;
                case SvgPreserveAspectRatio.xMinYMax:
                    minY += height - (Height * scaleY);
                    break;
                case SvgPreserveAspectRatio.xMidYMax:
                    minX += midX - viewMidX;
                    minY += height - (Height * scaleY);
                    break;
                case SvgPreserveAspectRatio.xMaxYMax:
                    minX += width - (Width * scaleX);
                    minY += height - (Height * scaleY);
                    break;
            }
        }

        // System.Numerics row-vector convention: P * (Scale * Translate) yields
        //   (P.x * scaleX + tx, P.y * scaleY + ty)
        // We want: scaleX * (P.x - MinX) + x  (== scaleX*P.x + (-MinX*scaleX + x))
        // so the translate values must already include the scaled-MinX offset
        // *without* being multiplied by scale again. Putting Translate FIRST and
        // Scale LAST in the chain re-multiplied minX/minY by scale, producing a
        // viewBox offset of `MinY * scaleY * scaleY` instead of `MinY * scaleY`.
        return Matrix3x2.CreateScale(scaleX, scaleY) *
               Matrix3x2.CreateTranslation((float)(minX + x), (float)(minY + y));
    }
}
