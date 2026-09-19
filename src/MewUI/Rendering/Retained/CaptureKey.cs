using System.Numerics;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Everything the scene's view of a subtree depends on from outside it: where the visual stands, the
/// transform and clip it was reached under, the scale, and the version that any change under it bumps.
/// </summary>
internal readonly record struct CaptureKey(
    bool IsValid,
    int SubtreeVersion,
    Rect Bounds,
    Matrix3x2 Transform,
    Rect? AmbientClip,
    double DpiScale);
