using Aprillz.MewUI;

namespace Svg;

public partial struct SvgUnit
{
    public float ToDeviceValue(ISvgRenderer renderer, UnitRenderingType renderType, SvgElement owner)
    {
        if (_deviceValue.HasValue)
        {
            return _deviceValue.Value;
        }

        if (_value == 0f)
        {
            _deviceValue = 0f;
            return 0f;
        }

        const float cmInInch = 2.54f;
        // SVG spec: absolute units (mm/cm/in/pt/pc) and font/percentage calculations
        // must use 96 CSS pixels per inch - NOT the system DPI. The legacy GDI+ path
        // (SvgUnit.Drawing.cs) uses SvgDocument.PointsPerInch which falls back to
        // GetSystemDpi(), so on a 144-dpi (1.5×) display `148mm` resolved to 839 px
        // instead of the spec-correct 559 px, causing viewBox transforms to be 1.5×
        // too large and content to fall outside the viewport.
        const float ppi = 96f;
        var value = Value;

        switch (Type)
        {
            case SvgUnitType.Em:
            case SvgUnitType.Ex:
                using (var fontManager = owner?.OwnerDocument?.FontManager == null ? new SvgFontManager() : null)
                using (var currFont = GetFont(renderer, owner, fontManager ?? owner?.OwnerDocument?.FontManager))
                {
                    if (currFont == null)
                    {
                        var points = value * 9f;
                        _deviceValue = (Type == SvgUnitType.Ex ? points * 0.5f : points) / 72f * ppi;
                    }
                    else
                    {
                        var scale = Type == SvgUnitType.Ex ? 0.5f : 1f;
                        _deviceValue = value * scale * (float)(currFont.SizeInPoints / 72.0) * ppi;
                    }
                }
                break;
            case SvgUnitType.Centimeter:
                _deviceValue = (float)((value / cmInInch) * ppi);
                break;
            case SvgUnitType.Inch:
                _deviceValue = value * ppi;
                break;
            case SvgUnitType.Millimeter:
                _deviceValue = (float)((value / 10f) / cmInInch) * ppi;
                break;
            case SvgUnitType.Pica:
                _deviceValue = ((value * 12f) / 72f) * ppi;
                break;
            case SvgUnitType.Point:
                _deviceValue = (value / 72f) * ppi;
                break;
            case SvgUnitType.Pixel:
            case SvgUnitType.User:
                _deviceValue = value;
                break;
            case SvgUnitType.Percentage:
                var boundable = renderer?.GetBoundable() ?? owner?.OwnerDocument as ISvgBoundable;
                if (boundable is null)
                {
                    _deviceValue = value;
                    break;
                }

                var size = boundable.Bounds.Size;
                switch (renderType)
                {
                    case UnitRenderingType.Horizontal:
                        _deviceValue = (float)(size.Width * value / 100.0);
                        break;
                    case UnitRenderingType.HorizontalOffset:
                        _deviceValue = (float)(boundable.Location.X + (size.Width * value / 100.0));
                        break;
                    case UnitRenderingType.Vertical:
                        _deviceValue = (float)(size.Height * value / 100.0);
                        break;
                    case UnitRenderingType.VerticalOffset:
                        _deviceValue = (float)(boundable.Location.Y + (size.Height * value / 100.0));
                        break;
                    case UnitRenderingType.Other:
                        if (owner?.OwnerDocument != null
                            && owner.OwnerDocument.ViewBox.Width != 0
                            && owner.OwnerDocument.ViewBox.Height != 0)
                        {
                            _deviceValue =
                                (float)(Math.Sqrt((owner.OwnerDocument.ViewBox.Width * owner.OwnerDocument.ViewBox.Width) +
                                                  (owner.OwnerDocument.ViewBox.Height * owner.OwnerDocument.ViewBox.Height)) /
                                        Math.Sqrt(2.0) * value / 100.0);
                        }
                        else
                        {
                            _deviceValue = (float)(Math.Sqrt((size.Width * size.Width) + (size.Height * size.Height)) /
                                                   Math.Sqrt(2.0) * value / 100.0);
                        }
                        break;
                    default:
                        _deviceValue = value;
                        break;
                }
                break;
            default:
                _deviceValue = value;
                break;
        }

        return _deviceValue ?? 0f;
    }

    public static implicit operator float(SvgUnit value) => value.ToDeviceValue(null, UnitRenderingType.Other, null);

    public static Point GetDevicePoint(SvgUnit x, SvgUnit y, ISvgRenderer renderer, SvgElement owner)
    {
        return new Point(
            x.ToDeviceValue(renderer, UnitRenderingType.Horizontal, owner),
            y.ToDeviceValue(renderer, UnitRenderingType.Vertical, owner));
    }

    public static Point GetDevicePointOffset(SvgUnit x, SvgUnit y, ISvgRenderer renderer, SvgElement owner)
    {
        return new Point(
            x.ToDeviceValue(renderer, UnitRenderingType.HorizontalOffset, owner),
            y.ToDeviceValue(renderer, UnitRenderingType.VerticalOffset, owner));
    }

    public static Size GetDeviceSize(SvgUnit width, SvgUnit height, ISvgRenderer renderer, SvgElement owner)
    {
        return new Size(
            width.ToDeviceValue(renderer, UnitRenderingType.Horizontal, owner),
            height.ToDeviceValue(renderer, UnitRenderingType.Vertical, owner));
    }

    private static IFontDefn? GetFont(ISvgRenderer renderer, SvgElement owner, SvgFontManager? fontManager)
    {
        var visual = owner?.Parents.OfType<SvgVisualElement>().FirstOrDefault();
        return visual?.GetFont(renderer, fontManager);
    }
}
