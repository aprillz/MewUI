namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// A part of the scene that moves as one, as the content of a scroll viewer does. A node under the frame
/// keeps the geometry of the pass that last staged it together with where the frame stood then; the frame's
/// own place is the one value a whole-pixel move of the subtree changes, and the node is read shifted by how
/// far the frame has gone since. The root frame has no parent and never moves.
/// </summary>
internal sealed class SceneFrame
{
    private Vector _pendingLocalOrigin;
    private Vector _pendingSurfaceOrigin;
    private bool _hasPending;
    private bool _forcesVisit;

    internal SceneFrame(SceneFrame? parent)
    {
        Parent = parent;
    }

    /// <summary>The frame this one stands in, or null for the root frame.</summary>
    internal SceneFrame? Parent { get; }

    /// <summary>Where the frame root stands in window coordinates, relative to the origin of the parent frame.</summary>
    internal Vector LocalOriginInParent { get; private set; }

    /// <summary>The same place in surface coordinates.</summary>
    internal Vector SurfaceOriginInParent { get; private set; }

    /// <summary>The committed origin in window coordinates, parent frames included.</summary>
    internal Point LocalOrigin => Parent == null ? Point.Zero : Parent.LocalOrigin + LocalOriginInParent;

    /// <summary>The committed origin in surface coordinates, parent frames included.</summary>
    internal Point SurfaceOrigin => Parent == null ? Point.Zero : Parent.SurfaceOrigin + SurfaceOriginInParent;

    /// <summary>The origin the frame takes when the pass under way lands, in window coordinates.</summary>
    internal Point PendingLocalOrigin
        => Parent == null ? Point.Zero : Parent.PendingLocalOrigin + (_hasPending ? _pendingLocalOrigin : LocalOriginInParent);

    /// <summary>The origin the frame takes when the pass under way lands, in surface coordinates.</summary>
    internal Point PendingSurfaceOrigin
        => Parent == null ? Point.Zero : Parent.PendingSurfaceOrigin + (_hasPending ? _pendingSurfaceOrigin : SurfaceOriginInParent);

    /// <summary>
    /// True while the pass under way has to visit every node under the frame: the frame moved by a fraction of
    /// a pixel, so no recording under it can be placed where it stands now.
    /// </summary>
    internal bool ForcesVisit => _forcesVisit || (Parent?.ForcesVisit ?? false);

    /// <summary>Stages where the frame stands in the pass under way; the root frame is never staged.</summary>
    internal void Stage(Point localOrigin, Point surfaceOrigin, bool forcesVisit)
    {
        if (Parent == null)
        {
            throw new InvalidOperationException("The root frame does not move.");
        }

        _pendingLocalOrigin = localOrigin - Parent.PendingLocalOrigin;
        _pendingSurfaceOrigin = surfaceOrigin - Parent.PendingSurfaceOrigin;
        _hasPending = true;
        _forcesVisit = forcesVisit;
    }

    internal void Commit()
    {
        if (_hasPending)
        {
            LocalOriginInParent = _pendingLocalOrigin;
            SurfaceOriginInParent = _pendingSurfaceOrigin;
        }

        Discard();
    }

    internal void Discard()
    {
        _hasPending = false;
        _forcesVisit = false;
    }
}
