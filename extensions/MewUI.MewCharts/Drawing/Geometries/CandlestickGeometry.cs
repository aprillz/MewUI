using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A candlestick (OHLC) geometry: high/low wicks plus the open-close body.</summary>
public class CandlestickGeometry : BaseCandlestickGeometry, IDrawnElement<MewDrawingContext>
{
    public virtual void Draw(MewDrawingContext context)
    {
        var width = Width;
        var cx = X + width * 0.5f;
        var high = Y;
        var open = Open;
        var close = Close;
        var low = Low;

        float yi, yj;
        if (open > close) { yi = close; yj = open; }
        else { yi = open; yj = close; }

        context.DrawLine(new Point(cx, high), new Point(cx, yi));
        context.DrawRectangle(new Rect(X, yi, width, Math.Abs(open - close)));
        context.DrawLine(new Point(cx, yj), new Point(cx, low));
    }
}
