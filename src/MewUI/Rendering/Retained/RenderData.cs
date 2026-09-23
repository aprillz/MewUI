using System.Numerics;
using System.Runtime.CompilerServices;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Completed drawing data of one visual's content slot. Immutable once built; owns the resource
/// leases its commands hold and releases them on <see cref="Dispose"/>.
/// </summary>
internal sealed class RenderData : IDisposable
{
    private readonly RenderCommand[] _commands;
    private readonly double[] _values;
    private readonly object?[] _resources;
    private readonly IDisposable[] _leases;

    // Where each command reaches in the owning visual's local space, under the transform and clip it was recorded in.
    private readonly Rect[] _extents;
    private bool _disposed;

    internal static RenderData Empty { get; } = new([], [], [], [], [], default);

    private RenderData(
        RenderCommand[] commands,
        double[] values,
        object?[] resources,
        IDisposable[] leases,
        Rect[] extents,
        Rect localBounds)
    {
        _extents = extents;
        _commands = commands;
        _values = values;
        _resources = resources;
        _leases = leases;
        LocalBounds = localBounds;
    }

    /// <summary>Union of the recorded command bounds in the owning visual's local space.</summary>
    internal Rect LocalBounds { get; }

    /// <summary>
    /// Where the owning visual stood when this was recorded. The commands carry that position, so a
    /// visual that has only moved since is replayed under the difference instead of being recorded again.
    /// </summary>
    internal Rect RecordedBounds { get; set; }

    /// <summary>Where this recording reached the surface in the last pass that looked at it.</summary>
    internal Rect SurfaceExtent { get; set; }

    /// <summary>The transform to the surface this was recorded under.</summary>
    internal Matrix3x2 SurfaceTransform { get; set; }

    /// <summary>False when the commands replace, rotate or scale the transform, which a moved replay cannot carry.</summary>
    internal bool CanBePlaced { get; set; } = true;

    internal int CommandCount => _commands.Length;

    internal bool IsEmpty => _commands.Length == 0;

    /// <summary>Bytes the command records, their arguments and their resource table take.</summary>
    internal long EstimatedBytes
        => ((long)_commands.Length * Unsafe.SizeOf<RenderCommand>()) +
            ((long)_values.Length * sizeof(double)) +
            ((long)_extents.Length * Unsafe.SizeOf<Rect>()) +
            ((long)_resources.Length * IntPtr.Size);

    /// <summary>
    /// Whether this draws exactly what <paramref name="other"/> draws: the same commands with the same
    /// arguments and the very same resources. A visual recorded again only because it was laid out again
    /// usually comes out identical, and then nothing on the surface has to change.
    /// </summary>
    internal bool DrawsTheSameAs(RenderData other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_commands.Length != other._commands.Length ||
            _values.Length != other._values.Length ||
            _resources.Length != other._resources.Length ||
            RecordedBounds != other.RecordedBounds ||
            CanBePlaced != other.CanBePlaced)
        {
            return false;
        }

        if (!System.Runtime.InteropServices.MemoryMarshal.AsBytes(_commands.AsSpan())
                .SequenceEqual(System.Runtime.InteropServices.MemoryMarshal.AsBytes(other._commands.AsSpan())) ||
            !_values.AsSpan().SequenceEqual(other._values))
        {
            return false;
        }

        for (int resourceIndex = 0; resourceIndex < _resources.Length; resourceIndex++)
        {
            if (!IsSameResource(_resources[resourceIndex], other._resources[resourceIndex]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Finds where this draws differently from <paramref name="previous"/>, in the owning visual's local
    /// space. Succeeds only when the two differ by drawing calls alone: a differing call that sets a clip,
    /// a transform or a scope changes what the calls after it do, which leaves the difference unbounded.
    /// </summary>
    internal bool TryFindChange(RenderData previous, out Rect change)
    {
        change = default;
        if (RecordedBounds != previous.RecordedBounds || CanBePlaced != previous.CanBePlaced)
        {
            return false;
        }

        int commonLength = Math.Min(_commands.Length, previous._commands.Length);
        int leading = 0;
        while (leading < commonLength && IsSameCommand(leading, previous, leading))
        {
            leading++;
        }

        int trailing = 0;
        while (trailing < commonLength - leading &&
            IsSameCommand(_commands.Length - 1 - trailing, previous, previous._commands.Length - 1 - trailing))
        {
            trailing++;
        }

        var accumulator = default(BoundsAccumulator);
        if (!TryAddDrawnExtents(leading, _commands.Length - trailing, ref accumulator) ||
            !previous.TryAddDrawnExtents(leading, previous._commands.Length - trailing, ref accumulator))
        {
            return false;
        }

        change = accumulator.Result;
        return true;
    }

    private bool TryAddDrawnExtents(int startIndex, int endIndex, ref BoundsAccumulator accumulator)
    {
        for (int commandIndex = startIndex; commandIndex < endIndex; commandIndex++)
        {
            if (_commands[commandIndex].Kind < RenderCommandKind.DrawLine)
            {
                return false;
            }

            accumulator.Add(_extents[commandIndex]);
        }

        return true;
    }

    private bool IsSameCommand(int commandIndex, RenderData other, int otherIndex)
    {
        ref readonly var command = ref _commands[commandIndex];
        ref readonly var otherCommand = ref other._commands[otherIndex];
        if (command.Kind != otherCommand.Kind ||
            command.Bounds != otherCommand.Bounds ||
            command.Color != otherCommand.Color ||
            command.Flags != otherCommand.Flags ||
            command.ResourcePolicy != otherCommand.ResourcePolicy ||
            command.ValueCount != otherCommand.ValueCount ||
            _extents[commandIndex] != other._extents[otherIndex])
        {
            return false;
        }

        if (!_values.AsSpan(command.ValueOffset, command.ValueCount)
                .SequenceEqual(other._values.AsSpan(otherCommand.ValueOffset, otherCommand.ValueCount)))
        {
            return false;
        }

        return IsSameResource(ResourceAt(command.ResourceIndex), other.ResourceAt(otherCommand.ResourceIndex)) &&
            IsSameResource(ResourceAt(command.PaintIndex), other.ResourceAt(otherCommand.PaintIndex));
    }

    private object? ResourceAt(int resourceIndex) => resourceIndex < 0 ? null : _resources[resourceIndex];

    private static bool IsSameResource(object? first, object? second)
    {
        if (Equals(first, second))
        {
            return true;
        }

        // Drawing code commonly builds its outline anew on every call, so paths are told apart by what they hold.
        return first is PathGeometry firstPath && second is PathGeometry secondPath && HoldSamePath(firstPath, secondPath);
    }

    private static bool HoldSamePath(PathGeometry first, PathGeometry second)
    {
        var firstCommands = first.Commands;
        var secondCommands = second.Commands;
        if (first.FillRule != second.FillRule || firstCommands.Length != secondCommands.Length)
        {
            return false;
        }

        for (int commandIndex = 0; commandIndex < firstCommands.Length; commandIndex++)
        {
            ref readonly var firstCommand = ref firstCommands[commandIndex];
            ref readonly var secondCommand = ref secondCommands[commandIndex];
            if (firstCommand.Type != secondCommand.Type ||
                firstCommand.X0 != secondCommand.X0 || firstCommand.Y0 != secondCommand.Y0 ||
                firstCommand.X1 != secondCommand.X1 || firstCommand.Y1 != secondCommand.Y1 ||
                firstCommand.X2 != secondCommand.X2 || firstCommand.Y2 != secondCommand.Y2)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Replays the recorded commands into <paramref name="context"/> as they were called.</summary>
    internal void Replay(IGraphicsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(_disposed, this);

        for (int commandIndex = 0; commandIndex < _commands.Length; commandIndex++)
        {
            RenderDataReplayer.Replay(context, in _commands[commandIndex], _values, _resources);
        }
    }

    /// <summary>Replays the commands under a context the caller has translated by the given move.</summary>
    internal void ReplayMoved(IGraphicsContext context, double offsetX, double offsetY)
    {
        ArgumentNullException.ThrowIfNull(context);
        ObjectDisposedException.ThrowIf(_disposed, this);

        for (int commandIndex = 0; commandIndex < _commands.Length; commandIndex++)
        {
            RenderDataReplayer.Replay(context, in _commands[commandIndex], _values, _resources, offsetX, offsetY);
        }
    }

    public void Dispose()
    {
        // Data without commands owns nothing, and the empty instance is shared by every visual that
        // draws nothing, so disposing it must not make it unusable.
        if (_disposed || _commands.Length == 0)
        {
            return;
        }

        _disposed = true;
        for (int leaseIndex = 0; leaseIndex < _leases.Length; leaseIndex++)
        {
            _leases[leaseIndex].Dispose();
        }

        for (int resourceIndex = 0; resourceIndex < _resources.Length; resourceIndex++)
        {
            // A frozen path is the visual's own; an unfrozen one is the copy this recording took.
            if (_resources[resourceIndex] is PathGeometry copy && !copy.IsFrozen)
            {
                _resources[resourceIndex] = null;
                RenderResourceSnapshot.ReturnPath(copy);
            }
        }
    }

    internal static RenderData Create(
        RenderCommand[] commands,
        double[] values,
        object?[] resources,
        IDisposable[] leases,
        Rect[] extents,
        Rect localBounds)
        => commands.Length == 0
            ? new RenderData([], [], [], [], [], default)
            : new RenderData(commands, values, resources, leases, extents, localBounds);
}

/// <summary>
/// Stages the commands of one content slot. A staged slot holds resource leases, so it must end in
/// either <see cref="TryBuild"/> or <see cref="Discard"/>.
/// </summary>
internal sealed class RenderDataBuilder
{
    private const int TRANSFORM_STACK_CAPACITY = 8;

    private readonly List<RenderCommand> _commands = [];
    private readonly List<double> _values = [];
    private readonly List<object?> _resources = [];
    private readonly List<IDisposable> _leases = [];
    private readonly List<Rect> _extents = [];
    private readonly Dictionary<object, int> _resourceIndices = new(ReferenceEqualityComparer.Instance);
    private Matrix3x2[] _transformStack = new Matrix3x2[TRANSFORM_STACK_CAPACITY];

    // The clip the recorded commands are drawn under, in the slot's own space, saved alongside the
    // transform. What a command asks to draw past it never reaches the surface, so it is not ink.
    private Rect?[] _clipStack = new Rect?[TRANSFORM_STACK_CAPACITY];
    private Rect? _clip;
    private Matrix3x2 _transform = Matrix3x2.Identity;
    // Undoes the transform the slot is drawn under, which a transform set outright already contains.
    private Matrix3x2 _fromAmbient = Matrix3x2.Identity;
    private Rect _localBounds;
    private bool _hasBounds;
    private int _transformDepth;
    private int _saveDepth;
    private int _opacityDepth;
    private int _backdropDepth;
    // A transform set outright ignores what the replay put around it, and text inside a rotation or a
    // scale cannot take a move added to its origin, so neither kind of content can be moved.
    private bool _replacesTransform;
    private string? _rejectionReason;

    internal bool IsRejected => _rejectionReason != null;

    internal string? RejectionReason => _rejectionReason;

    internal int CommandCount => _commands.Count;

    /// <summary>
    /// Starts a slot drawn under <paramref name="ambientTransform"/>, which a transform the slot sets
    /// outright is measured against.
    /// </summary>
    internal void Begin(Matrix3x2 ambientTransform)
    {
        if (Matrix3x2.Invert(ambientTransform, out var fromAmbient))
        {
            _fromAmbient = fromAmbient;
        }
        else
        {
            Reject("the slot is drawn under a transform that cannot be inverted");
        }
    }

    /// <summary>Records one command, snapshotting and interning the resources it references.</summary>
    internal void Add(
        RenderCommandKind kind,
        Rect bounds,
        RenderResourcePolicy resourcePolicy,
        RenderCommandFlags flags,
        Color color,
        object? resource,
        object? paint,
        ReadOnlySpan<double> values,
        Rect inkBounds)
    {
        if (IsRejected)
        {
            return;
        }

        if (!TryInternResource(kind, resourcePolicy, resource, out int resourceIndex) ||
            !TryInternPaint(kind, resourcePolicy, paint, out int paintIndex))
        {
            return;
        }

        Commit(kind, bounds, resourcePolicy, flags, color, resourceIndex, paintIndex, values, inkBounds);
    }

    /// <summary>Records one text call, keeping the layout and a self-contained copy of its options.</summary>
    internal void AddText(RenderCommandKind kind, Rect bounds, ITextLayout layout, in TextDrawOptions options)
    {
        if (IsRejected)
        {
            return;
        }

        if (layout is not ManagedTextLayout managedLayout || managedLayout.Snapshot.Inlines.Length != 0)
        {
            Reject($"{kind} draws a text layout that cannot be preserved");
            return;
        }

        if (!TryInternResource(kind, RenderResourcePolicy.Value, layout, out int layoutIndex))
        {
            return;
        }

        int optionsIndex = AddResource(RenderResourceSnapshot.SnapshotTextOptions(in options), null);
        Commit(
            kind,
            bounds,
            RenderResourcePolicy.TextLayoutLeaseRequired,
            RenderCommandFlags.None,
            default,
            layoutIndex,
            optionsIndex,
            default,
            TextInk(bounds));
    }

    /// <summary>
    /// Glyphs ink past the box they were measured in: a slanted stroke, an antialiased edge, a mark above
    /// the line. The layout does not say by how much until a backend has drawn it, so the extent allows
    /// for a share of the line height on every side.
    /// </summary>
    private static Rect TextInk(Rect measured)
    {
        if (measured.Width <= 0 || measured.Height <= 0)
        {
            return measured;
        }

        double overhang = Math.Ceiling(Math.Min(measured.Height, TEXT_INK_LINE_HEIGHT_LIMIT) * TEXT_INK_OVERHANG_RATIO) + 1;
        return measured.Inflate(overhang, overhang);
    }

    private const double TEXT_INK_OVERHANG_RATIO = 0.2;

    // A block of many lines is as tall as its text, but its glyphs overhang by what one line allows.
    private const double TEXT_INK_LINE_HEIGHT_LIMIT = 40;

    /// <summary>Marks the slot unrecordable; the reason is kept for diagnostics.</summary>
    internal void Reject(string? reason)
    {
        if (IsRejected)
        {
            return;
        }

        _rejectionReason = reason ?? "unspecified";
        ReleaseStagedLeases();
    }

    internal bool TryBuild(out RenderData data)
    {
        if (IsRejected)
        {
            data = RenderData.Empty;
            return false;
        }

        if (_saveDepth != 0 || _opacityDepth != 0 || _backdropDepth != 0)
        {
            Reject("the content slot leaves a graphics scope open");
            data = RenderData.Empty;
            return false;
        }

        data = RenderData.Create([.. _commands], [.. _values], [.. _resources], [.. _leases], [.. _extents], _localBounds);
        data.CanBePlaced = !_replacesTransform;

        // The built data owns the leases from here on, so they must be dropped without releasing.
        _leases.Clear();
        ClearStaging();
        Reset();
        return true;
    }

    internal void Discard()
    {
        ReleaseStagedLeases();
        Reset();
        _rejectionReason = null;
    }

    private void Reset()
    {
        _transform = Matrix3x2.Identity;
        _fromAmbient = Matrix3x2.Identity;
        _transformDepth = 0;
        _clip = null;
        _saveDepth = 0;
        _opacityDepth = 0;
        _backdropDepth = 0;
        _localBounds = default;
        _hasBounds = false;
        _replacesTransform = false;
    }

    private void ClearStaging()
    {
        _commands.Clear();
        _values.Clear();
        _extents.Clear();
        _resources.Clear();
        _resourceIndices.Clear();
    }

    private void ReleaseStagedLeases()
    {
        for (int leaseIndex = 0; leaseIndex < _leases.Count; leaseIndex++)
        {
            _leases[leaseIndex].Dispose();
        }

        _leases.Clear();
        ClearStaging();
    }

    private void Commit(
        RenderCommandKind kind,
        Rect bounds,
        RenderResourcePolicy resourcePolicy,
        RenderCommandFlags flags,
        Color color,
        int resourceIndex,
        int paintIndex,
        ReadOnlySpan<double> values,
        Rect inkBounds = default)
    {
        TrackScope(kind);
        if (IsRejected)
        {
            return;
        }

        TrackTransform(kind, values);

        // The stored rect is the shape to draw again; the ink rect is what it actually covers, which
        // is wider wherever a stroke straddles the shape's edge.
        var inkArea = inkBounds.IsEmpty ? bounds : inkBounds;
        bool isClip = TrackClip(kind, bounds);
        var extent = default(Rect);
        if (!isClip && !inkArea.IsEmpty)
        {
            var transformed = RetainedGeometry.TransformRect(inkArea, _transform);
            if (_clip is Rect activeClip)
            {
                transformed = transformed.Intersect(activeClip);
            }

            if (transformed.Width > 0 && transformed.Height > 0)
            {
                extent = transformed;
                if (_hasBounds)
                {
                    _localBounds = _localBounds.Union(transformed);
                }
                else
                {
                    _localBounds = transformed;
                    _hasBounds = true;
                }
            }
        }

        int valueOffset = _values.Count;
        for (int valueIndex = 0; valueIndex < values.Length; valueIndex++)
        {
            _values.Add(values[valueIndex]);
        }

        _extents.Add(extent);
        _commands.Add(new RenderCommand(
            kind,
            bounds,
            resourcePolicy,
            flags,
            color,
            valueOffset,
            (byte)values.Length,
            resourceIndex,
            paintIndex));
    }

    private int AddResource(object? snapshot, IDisposable? lease)
    {
        if (lease != null)
        {
            _leases.Add(lease);
        }

        _resources.Add(snapshot);
        return _resources.Count - 1;
    }

    private bool TryInternResource(
        RenderCommandKind kind,
        RenderResourcePolicy resourcePolicy,
        object? resource,
        out int index)
    {
        index = -1;
        if (resource == null)
        {
            return true;
        }

        // A path that can still change is often one object cleared and filled again for every outline,
        // so the same object does not mean the same outline: each use keeps a snapshot of its own.
        if (resource is PathGeometry changingPath && !changingPath.IsFrozen)
        {
            index = AddResource(RenderResourceSnapshot.SnapshotPath(changingPath), null);
            return true;
        }

        // Interning keys on the object the visual drew with, so one snapshot and one lease serve
        // every command that repeats it.
        if (_resourceIndices.TryGetValue(resource, out index))
        {
            return true;
        }

        object snapshot = resource;
        IDisposable? lease = null;
        if ((resourcePolicy & RenderResourcePolicy.ImageLeaseRequired) != 0)
        {
            if (resource is not IRetainableImage retainableImage)
            {
                Reject($"{kind} draws an image that cannot be retained");
                return false;
            }

            try
            {
                var retainedImage = retainableImage.Retain();
                snapshot = retainedImage;
                lease = retainedImage;
            }
            catch (ObjectDisposedException)
            {
                Reject($"{kind} draws an image that was already disposed");
                return false;
            }
            catch (NotSupportedException)
            {
                Reject($"{kind} draws an image whose backend refuses a lease");
                return false;
            }
        }

        index = AddResource(snapshot, lease);
        _resourceIndices.Add(resource, index);
        return true;
    }

    private bool TryInternPaint(
        RenderCommandKind kind,
        RenderResourcePolicy resourcePolicy,
        object? paint,
        out int index)
    {
        index = -1;
        if (paint == null)
        {
            return true;
        }

        if (_resourceIndices.TryGetValue(paint, out index))
        {
            return true;
        }

        if (!RenderResourceSnapshot.TrySnapshotPaint(paint, out object? snapshot, out var lease))
        {
            Reject($"{kind} paints with an image brush that cannot be retained");
            return false;
        }

        if (lease != null && (resourcePolicy & RenderResourcePolicy.ImageLeaseRequired) != 0)
        {
            Reject($"{kind} needs two image leases in one command");
            lease.Dispose();
            index = -1;
            return false;
        }

        index = AddResource(snapshot, lease);
        _resourceIndices.Add(paint, index);
        return true;
    }

    private void TrackScope(RenderCommandKind kind)
    {
        switch (kind)
        {
            case RenderCommandKind.Save:
                _saveDepth++;
                break;
            case RenderCommandKind.Restore:
                if (_saveDepth == 0)
                {
                    Reject("the content slot closes an unopened graphics state scope");
                    return;
                }

                _saveDepth--;
                break;
            case RenderCommandKind.BeginOpacity:
                _opacityDepth++;
                break;
            case RenderCommandKind.EndOpacity:
                if (_opacityDepth == 0)
                {
                    Reject("the content slot closes an unopened opacity scope");
                    return;
                }

                _opacityDepth--;
                break;
            case RenderCommandKind.BeginOpaqueBackdrop:
                _backdropDepth++;
                break;
            case RenderCommandKind.EndOpaqueBackdrop:
                if (_backdropDepth == 0)
                {
                    Reject("the content slot closes an unopened opaque backdrop scope");
                    return;
                }

                _backdropDepth--;
                break;
        }
    }

    /// <summary>
    /// Follows the clip the slot draws under and says whether the command is one. A clip narrows what
    /// the following commands ink; a reset goes back to the clip the last Save stood under.
    /// </summary>
    // In layout units. One covers a device pixel at every scale of one or more.
    private const double CLIP_ROUNDING_MARGIN = 1;

    private bool TrackClip(RenderCommandKind kind, Rect bounds)
    {
        switch (kind)
        {
            case RenderCommandKind.SetClip:
            case RenderCommandKind.IntersectClip:
            case RenderCommandKind.SetClipRoundedRect:
            case RenderCommandKind.SetClipPath:
                var clipped = RetainedGeometry.TransformRect(bounds, _transform);

                // A backend rounds a clip to whole pixels in its own way, and may let through a pixel
                // that the rectangle asked for only part of. The extent must not fall short of that.
                clipped = clipped.Inflate(CLIP_ROUNDING_MARGIN, CLIP_ROUNDING_MARGIN);
                _clip = _clip is Rect current ? current.Intersect(clipped) : clipped;
                return true;
            case RenderCommandKind.ResetClip:
                _clip = _transformDepth > 0 ? _clipStack[_transformDepth - 1] : null;
                return true;
            default:
                return false;
        }
    }

    private void TrackTransform(RenderCommandKind kind, ReadOnlySpan<double> values)
    {
        switch (kind)
        {
            case RenderCommandKind.Save:
                if (_transformDepth == _transformStack.Length)
                {
                    Array.Resize(ref _transformStack, _transformStack.Length * 2);
                    Array.Resize(ref _clipStack, _clipStack.Length * 2);
                }

                _clipStack[_transformDepth] = _clip;
                _transformStack[_transformDepth++] = _transform;
                break;
            case RenderCommandKind.Restore:
                if (_transformDepth > 0)
                {
                    _transform = _transformStack[--_transformDepth];
                    _clip = _clipStack[_transformDepth];
                }
                break;
            case RenderCommandKind.Translate:
                _transform = Matrix3x2.CreateTranslation((float)values[0], (float)values[1]) * _transform;
                break;
            case RenderCommandKind.Rotate:
                _replacesTransform = true;
                _transform = Matrix3x2.CreateRotation((float)values[0]) * _transform;
                break;
            case RenderCommandKind.Scale:
                _replacesTransform = true;
                _transform = Matrix3x2.CreateScale((float)values[0], (float)values[1]) * _transform;
                break;
            case RenderCommandKind.SetTransform:
                _replacesTransform = true;
                _transform = new Matrix3x2(
                    (float)values[0],
                    (float)values[1],
                    (float)values[2],
                    (float)values[3],
                    (float)values[4],
                    (float)values[5]) * _fromAmbient;
                break;
            case RenderCommandKind.ResetTransform:
                _replacesTransform = true;
                _transform = _fromAmbient;
                break;
        }
    }
}
