using System.Numerics;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Keeps the surfaces faded visuals were blended through, so that a later frame which would draw a group the
/// same way blends the kept surface again instead of replaying everything under it. A group draws the same when
/// every visual under it holds the same plan, recordings and place relative to the group, and the group stands
/// under the same scale and rotation, moved by whole device pixels at most.
/// </summary>
internal sealed class GroupLayerCache : IDisposable
{
    // A kept surface nobody drew for this many frames goes, so an animation that ended lets go of its memory.
    private const int MAX_IDLE_FRAMES = 120;

    // Kept surfaces together may hold this many times the pixels of the scene's surface.
    private const double BUDGET_SURFACE_MULTIPLE = 2.0;

    // A group whose content changed this many frames in a row is drawn through a passing surface for a while.
    private const int MISSES_BEFORE_VOLATILE = 3;
    private const int VOLATILE_FRAMES = 60;

    private readonly Dictionary<VisualNode, Layer> _layers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<VisualNode, long> _volatileUntil = new(ReferenceEqualityComparer.Instance);
    private readonly List<VisualNode> _expired = [];
    private long _frame;
    private long _budgetPixels;

    /// <summary>What a group's replay reads, collected for the comparison with the kept surface.</summary>
    internal GroupSignature Scratch { get; private set; } = new();

    /// <summary>Makes what <see cref="Scratch"/> holds the layer's own, after the group was drawn from it.</summary>
    internal void Keep(Layer layer)
    {
        (layer.Signature, Scratch) = (Scratch, layer.Signature);
    }

    internal bool IsVolatile(VisualNode node)
        => _volatileUntil.TryGetValue(node, out long until) && until > _frame;

    internal bool TryGet(VisualNode node, out Layer layer) => _layers.TryGetValue(node, out layer!);

    /// <summary>The layer for a node, created without a surface on first use.</summary>
    internal Layer GetOrAdd(VisualNode node)
    {
        if (!_layers.TryGetValue(node, out var layer))
        {
            layer = new Layer();
            _layers.Add(node, layer);
        }

        return layer;
    }

    /// <summary>Notes a frame that drew the layer from its kept surface.</summary>
    internal void NoteHit(Layer layer)
    {
        layer.LastUsedFrame = _frame;
        layer.Misses = 0;
    }

    /// <summary>
    /// Notes a frame that had to draw the group again. Returns false when the group changes too often to be
    /// worth keeping, in which case its layer is gone and the caller blends through a passing surface.
    /// </summary>
    internal bool NoteMiss(VisualNode node, Layer layer)
    {
        layer.LastUsedFrame = _frame;
        layer.Misses++;
        if (layer.Misses < MISSES_BEFORE_VOLATILE)
        {
            return true;
        }

        Remove(node);
        _volatileUntil[node] = _frame + VOLATILE_FRAMES;
        return false;
    }

    /// <summary>Lets the budget follow the size of the surface the scene is drawn to.</summary>
    internal void NoteSurfacePixels(long pixels)
        => _budgetPixels = Math.Max(_budgetPixels, (long)(pixels * BUDGET_SURFACE_MULTIPLE));

    /// <summary>
    /// Called once a frame is over: lets go of layers idle too long, then of the least recently drawn ones
    /// while the kept pixels exceed the budget. Layers drawn in the frame just over are kept.
    /// </summary>
    internal void EndFrame()
    {
        long keptPixels = 0;
        _expired.Clear();
        foreach (var (node, layer) in _layers)
        {
            if (_frame - layer.LastUsedFrame > MAX_IDLE_FRAMES)
            {
                _expired.Add(node);
            }
            else
            {
                keptPixels += layer.Pixels;
            }
        }

        foreach (var node in _expired)
        {
            Remove(node);
        }

        while (keptPixels > _budgetPixels && _budgetPixels > 0)
        {
            VisualNode? oldest = null;
            Layer? oldestLayer = null;
            foreach (var (node, layer) in _layers)
            {
                if (layer.LastUsedFrame < _frame && (oldestLayer == null || layer.LastUsedFrame < oldestLayer.LastUsedFrame))
                {
                    oldest = node;
                    oldestLayer = layer;
                }
            }

            if (oldest == null)
            {
                break;
            }

            keptPixels -= oldestLayer!.Pixels;
            Remove(oldest);
        }

        if (_volatileUntil.Count > 0)
        {
            _expired.Clear();
            foreach (var (node, until) in _volatileUntil)
            {
                if (until <= _frame)
                {
                    _expired.Add(node);
                }
            }

            foreach (var node in _expired)
            {
                _volatileUntil.Remove(node);
            }
        }

        _frame++;
    }

    /// <summary>Lets go of every kept surface, as a lost device or a scene that is going away needs.</summary>
    internal void Clear()
    {
        foreach (var layer in _layers.Values)
        {
            layer.ReleaseSurface();
        }

        _layers.Clear();
        _volatileUntil.Clear();
    }

    public void Dispose() => Clear();

    private void Remove(VisualNode node)
    {
        if (_layers.Remove(node, out var layer))
        {
            layer.ReleaseSurface();
        }
    }

    /// <summary>The kept surface of one faded visual and what it was drawn from.</summary>
    internal sealed class Layer
    {
        internal GroupSignature Signature { get; set; } = new();

        internal IGraphicsFactory? Factory { get; private set; }

        internal IRenderSurface? Surface { get; private set; }

        internal IImage? Image { get; private set; }

        internal int PixelWidth { get; private set; }

        internal int PixelHeight { get; private set; }

        internal long Pixels => (long)PixelWidth * PixelHeight;

        /// <summary>Where the surface was placed, in the coordinates of the surface the group was blended onto.</summary>
        internal Rect Placement { get; set; }

        /// <summary>The group's reach on that surface when it was drawn.</summary>
        internal Rect Reach { get; set; }

        /// <summary>The group's anchor, its own content origin, mapped onto that surface.</summary>
        internal Point SurfaceAnchor { get; set; }

        /// <summary>The scale and rotation the group was drawn under.</summary>
        internal Matrix3x2 Linear { get; set; }

        internal double Scale { get; set; }

        internal long LastUsedFrame { get; set; }

        internal int Misses { get; set; }

        /// <summary>Makes sure the layer holds a surface of exactly this size from this factory, keeping the one it has when it is.</summary>
        internal IRenderSurface EnsureSurface(IGraphicsFactory factory, int pixelWidth, int pixelHeight, double scale)
        {
            if (Surface != null && ReferenceEquals(Factory, factory) && PixelWidth == pixelWidth && PixelHeight == pixelHeight && Scale == scale)
            {
                return Surface;
            }

            ReleaseSurface();
            Factory = factory;
            Surface = factory.AcquireScratchSurface(pixelWidth, pixelHeight, scale, hasAlpha: true, debugName: "GroupLayer");
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            Scale = scale;
            return Surface;
        }

        /// <summary>The surface as an image to blend, made once per drawing of the surface.</summary>
        internal IImage GetImage() => Image ??= Factory!.CreateImageView(Surface!);

        /// <summary>Drops the image of the surface before it is drawn again; a backend may have copied what it showed.</summary>
        internal void ForgetImage()
        {
            Image?.Dispose();
            Image = null;
        }

        internal void ReleaseSurface()
        {
            Image?.Dispose();
            Image = null;
            if (Surface != null && Factory != null)
            {
                Factory.ReleaseScratchSurface(Surface);
            }

            Surface = null;
            Factory = null;
            PixelWidth = 0;
            PixelHeight = 0;
        }
    }
}

/// <summary>
/// What the replay of a group reads from the visuals under it: plans and recordings by identity, places and
/// states by value, all relative to the group's own anchor so that a group moved as a whole reads the same.
/// </summary>
internal sealed class GroupSignature
{
    private readonly List<object?> _references = [];
    private readonly List<double> _values = [];

    internal void Clear()
    {
        _references.Clear();
        _values.Clear();
    }

    internal void Add(object? reference) => _references.Add(reference);

    internal void Add(double value) => _values.Add(value);

    internal bool SameAs(GroupSignature other)
    {
        if (_references.Count != other._references.Count || _values.Count != other._values.Count)
        {
            return false;
        }

        for (int index = 0; index < _references.Count; index++)
        {
            if (!ReferenceEquals(_references[index], other._references[index]))
            {
                return false;
            }
        }

        for (int index = 0; index < _values.Count; index++)
        {
            if (_values[index] != other._values[index])
            {
                return false;
            }
        }

        return true;
    }
}
