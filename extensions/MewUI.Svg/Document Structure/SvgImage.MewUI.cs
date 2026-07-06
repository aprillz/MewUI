using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

public partial class SvgImage
{
    private static readonly HttpClient ImageHttpClient = new();

    private bool _gettingBounds;
    private PathGeometry? _path;

    // Per-element decoded-image cache. Without this, every frame re-decodes the data URI
    // / file bytes and rebuilds an IImage (incl. GPU upload), then disposes it at the end
    // of Render. For SVG with many image references (patterns, repeated icons), the
    // decode+upload+dispose churn dominates the frame - observed ~60 ms for issue 015-01
    // (a single tiny pattern-embedded PNG that should render in <1 ms).
    // Keyed by Href; on Href change the previous entry is dropped.
    private string? _cachedHref;

    private LoadedImage? _cachedImage;

    public override Rect Bounds
    {
        get
        {
            if (_gettingBounds)
            {
                return Rect.Empty;
            }

            _gettingBounds = true;
            try
            {
                var bounds = new Rect(
                    Location.ToDeviceValue(null, this),
                    new Size(
                        Width.ToDeviceValue(null, UnitRenderingType.Horizontal, this),
                        Height.ToDeviceValue(null, UnitRenderingType.Vertical, this)));
                return TransformedBounds(bounds);
            }
            finally
            {
                _gettingBounds = false;
            }
        }
    }

    public override PathGeometry Path(ISvgRenderer renderer)
    {
        if (_path is null)
        {
            var rect = new Rect(
                Location.ToDeviceValue(renderer, this),
                SvgUnit.GetDeviceSize(Width, Height, renderer, this));
            _path = PathGeometry.FromRect(rect);
        }

        return _path;
    }

    protected override void Render(ISvgRenderer renderer)
    {
        if (!(Visible && Displayable && Width.Value > 0f && Height.Value > 0f && !string.IsNullOrWhiteSpace(Href)))
        {
            return;
        }

        var loaded = GetImageCached(Href);
        if (loaded is null)
        {
            return;
        }

        try
        {
            if (!PushTransforms(renderer))
            {
                return;
            }

            var srcRect = loaded switch
            {
                LoadedRaster raster => new Rect(0, 0, raster.Image.Size.Width, raster.Image.Size.Height),
                LoadedSvg svg => new Rect(0, 0, svg.Document.ViewBoxWidth, svg.Document.ViewBoxHeight),
                _ => Rect.Empty
            };

            var destClip = new Rect(
                Location.ToDeviceValue(renderer, this),
                new Size(
                    Width.ToDeviceValue(renderer, UnitRenderingType.Horizontal, this),
                    Height.ToDeviceValue(renderer, UnitRenderingType.Vertical, this)));
            var destRect = ComputeAspectRatioRect(srcRect, destClip, AspectRatio);

            renderer.Save();
            try
            {
                renderer.IntersectClip(destClip);
                var hasClip = SetClip(renderer);
                try
                {
                    switch (loaded)
                    {
                        case LoadedRaster raster:
                            renderer.DrawImage(raster.Image, destRect, srcRect, FixOpacityValue(Opacity));
                            break;

                        case LoadedSvg svg:
                            renderer.Save();
                            try
                            {
                                renderer.Transform =
                                    System.Numerics.Matrix3x2.CreateScale(
                                        (float)(destRect.Width / Math.Max(srcRect.Width, double.Epsilon)),
                                        (float)(destRect.Height / Math.Max(srcRect.Height, double.Epsilon))) *
                                    System.Numerics.Matrix3x2.CreateTranslation((float)destRect.X, (float)destRect.Y) *
                                    renderer.Transform;
                                renderer.SetBoundable(new GenericBoundable(srcRect));
                                try
                                {
                                    svg.Document.RenderElement(renderer);
                                }
                                finally
                                {
                                    renderer.PopBoundable();
                                }
                            }
                            finally
                            {
                                renderer.Restore();
                            }
                            break;
                    }
                }
                finally
                {
                    ResetClip(renderer, hasClip);
                }
            }
            finally
            {
                renderer.Restore();
            }
        }
        finally
        {
            PopTransforms(renderer);
            // Don't dispose - owned by the per-element cache and reused on the next frame.
        }
    }

    /// <summary>Cached version of <see cref="GetImage(string)"/>. Reuses the decoded image
    /// across frames keyed by Href; invalidates on Href change.</summary>
    private LoadedImage? GetImageCached(string uriString)
    {
        if (_cachedImage is not null && string.Equals(_cachedHref, uriString, StringComparison.Ordinal))
        {
            return _cachedImage;
        }

        // Href changed - drop previous decode.
        _cachedImage?.Dispose();
        _cachedImage = null;

        _cachedImage = GetImage(uriString);
        _cachedHref = uriString;
        return _cachedImage;
    }

    public object? GetImage()
    {
        var loaded = GetImage(Href);
        return loaded switch
        {
            LoadedRaster raster => raster.Image,
            LoadedSvg svg => svg.Document,
            _ => null
        };
    }

    private LoadedImage? GetImage(string uriString)
    {
        try
        {
            var safeUriString = uriString.Length > 65519 ? uriString[..65519] : uriString;
            var uri = new Uri(safeUriString, UriKind.RelativeOrAbsolute);

            if (uri.IsAbsoluteUri && uri.Scheme == "data")
            {
                return GetImageFromDataUri(uriString);
            }

            if (!uri.IsAbsoluteUri)
            {
                // OwnerDocument.BaseUri is set by SvgDocument.Open(path); but Parse(string)
                // doesn't know the source path so it stays null. NullReferenceException
                // here would crash the whole render - gracefully skip the image instead.
                if (OwnerDocument?.BaseUri is null)
                {
                    Trace.TraceWarning("Cannot resolve relative image href '{0}': OwnerDocument.BaseUri is null. Set it via SvgDocument.BaseUri or load via Open(path).", uriString);
                    return null;
                }
                uri = new Uri(OwnerDocument.BaseUri, uri);
            }

            if (!ResolveExternalImages.AllowsResolving(uri))
            {
                Trace.TraceWarning("Trying to resolve image from '{0}', but resolving external resources of that type is disabled.", uri);
                return null;
            }

            if (uri.IsFile)
            {
                return LoadFromBytes(File.ReadAllBytes(uri.LocalPath), uri);
            }

            if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            {
                var data = ImageHttpClient.GetByteArrayAsync(uri).GetAwaiter().GetResult();
                return LoadFromBytes(data, uri);
            }

            throw new NotSupportedException();
        }
        catch (Exception ex)
        {
            Trace.TraceError("Error loading image: '{0}', error: {1}", uriString, ex.Message);
            return null;
        }
    }

    private LoadedImage? GetImageFromDataUri(string uriString)
    {
        var headerStartIndex = 5;
        var headerEndIndex = uriString.IndexOf(',', headerStartIndex);
        if (headerEndIndex < 0 || headerEndIndex + 1 >= uriString.Length)
        {
            throw new Exception("Invalid data URI");
        }

        var mimeType = "text/plain";
        var charset = "US-ASCII";
        var base64 = false;

        var headers = new List<string>(uriString.Substring(headerStartIndex, headerEndIndex - headerStartIndex).Split(';'));
        if (headers[0].Contains('/'))
        {
            mimeType = headers[0].Trim();
            headers.RemoveAt(0);
            charset = string.Empty;
        }

        if (headers.Count > 0 && headers[^1].Trim().Equals("base64", StringComparison.InvariantCultureIgnoreCase))
        {
            base64 = true;
            headers.RemoveAt(headers.Count - 1);
        }

        foreach (var param in headers)
        {
            var parts = param.Split('=');
            if (parts.Length >= 2 && parts[0].Trim().Equals("charset", StringComparison.InvariantCultureIgnoreCase))
            {
                charset = parts[1].Trim();
            }
        }

        var data = uriString[(headerEndIndex + 1)..];
        if (mimeType.Equals(MimeTypeSvg, StringComparison.InvariantCultureIgnoreCase))
        {
            if (base64)
            {
                var encoding = string.IsNullOrEmpty(charset) ? Encoding.UTF8 : Encoding.GetEncoding(charset);
                data = encoding.GetString(Convert.FromBase64String(data));
            }

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
            return new LoadedSvg(LoadSvg(stream, OwnerDocument.BaseUri));
        }

        if (mimeType.StartsWith("image/", StringComparison.InvariantCultureIgnoreCase) ||
            mimeType.StartsWith("img/", StringComparison.InvariantCultureIgnoreCase))
        {
            var bytes = base64 ? Convert.FromBase64String(data) : Encoding.Default.GetBytes(data);
            return LoadRaster(bytes);
        }

        return null;
    }

    private LoadedImage? LoadFromBytes(byte[] data, Uri baseUri)
    {
        if (baseUri.LocalPath.EndsWith(".svg", StringComparison.InvariantCultureIgnoreCase))
        {
            using var stream = new MemoryStream(data, writable: false);
            return new LoadedSvg(LoadSvg(stream, baseUri));
        }

        return LoadRaster(data);
    }

    private LoadedRaster? LoadRaster(byte[] data)
    {
        try
        {
            return new LoadedRaster(Application.DefaultGraphicsFactory.CreateImageFromBytes(data));
        }
        catch
        {
            return null;
        }
    }

    private static Rect ComputeAspectRatioRect(Rect srcRect, Rect destClip, SvgAspectRatio aspectRatio)
    {
        if (srcRect.IsEmpty || aspectRatio.Align == SvgPreserveAspectRatio.none)
        {
            return destClip;
        }

        var scaleX = destClip.Width / Math.Max(srcRect.Width, double.Epsilon);
        var scaleY = destClip.Height / Math.Max(srcRect.Height, double.Epsilon);
        var scale = aspectRatio.Slice ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);

        var width = srcRect.Width * scale;
        var height = srcRect.Height * scale;
        var x = destClip.X;
        var y = destClip.Y;

        switch (aspectRatio.Align)
        {
            case SvgPreserveAspectRatio.xMidYMin:
                x += (destClip.Width - width) / 2;
                break;
            case SvgPreserveAspectRatio.xMaxYMin:
                x += destClip.Width - width;
                break;
            case SvgPreserveAspectRatio.xMinYMid:
                y += (destClip.Height - height) / 2;
                break;
            case SvgPreserveAspectRatio.xMidYMid:
                x += (destClip.Width - width) / 2;
                y += (destClip.Height - height) / 2;
                break;
            case SvgPreserveAspectRatio.xMaxYMid:
                x += destClip.Width - width;
                y += (destClip.Height - height) / 2;
                break;
            case SvgPreserveAspectRatio.xMinYMax:
                y += destClip.Height - height;
                break;
            case SvgPreserveAspectRatio.xMidYMax:
                x += (destClip.Width - width) / 2;
                y += destClip.Height - height;
                break;
            case SvgPreserveAspectRatio.xMaxYMax:
                x += destClip.Width - width;
                y += destClip.Height - height;
                break;
        }

        return new Rect(x, y, width, height);
    }

    private abstract record LoadedImage : IDisposable
    {
        public abstract void Dispose();
    }

    private sealed record LoadedRaster(IImage Image) : LoadedImage
    {
        public override void Dispose() => Image.Dispose();
    }

    private sealed record LoadedSvg(SvgDocument Document) : LoadedImage
    {
        public override void Dispose()
        {
        }
    }
}
