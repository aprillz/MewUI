using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public interface IFontDefn : IDisposable
{
    double Size { get; }
    double SizeInPoints { get; }
    void AddStringToPath(ISvgRenderer renderer, PathGeometry path, string text, Point location);
    double Ascent(ISvgRenderer renderer);
    IList<Rect> MeasureCharacters(ISvgRenderer renderer, string text);
    Size MeasureString(ISvgRenderer renderer, string text);
}
