using System;
using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg
{
    public interface ISvgRenderer : IDisposable
    {
        float DpiY { get; }
        IGraphicsContext GraphicsContext { get; }
        IGraphicsFactory GraphicsFactory { get; }
        void DrawImage(IImage image, Rect destRect, Rect srcRect, float opacity = 1f);
        void DrawImageUnscaled(IImage image, Point location, float opacity = 1f);
        void DrawPath(IPen pen, PathGeometry path);
        void FillPath(IBrush brush, PathGeometry path);
        ISvgBoundable GetBoundable();
        ISvgBoundable PopBoundable();
        void SetBoundable(ISvgBoundable boundable);
        void Save();
        void Restore();
        Matrix3x2 Transform { get; set; }
        float GlobalOpacity { get; set; }
        void SetClip(Rect rect);
        void IntersectClip(Rect rect);
    }
}
