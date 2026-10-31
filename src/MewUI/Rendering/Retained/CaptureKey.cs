using System.Numerics;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// Everything the scene's view of a subtree depends on from outside it: where the visual stands, the
/// transform and clip it was reached under, the scale, and the version that any change under it bumps.
/// The bounds are relative to the origin of the visual's frame and the clip to the origin of the frame it
/// was opened in, so a whole-pixel move of a frame leaves the key as it was.
/// </summary>
internal readonly record struct CaptureKey(
    bool IsValid,
    int SubtreeVersion,
    Rect Bounds,
    Matrix3x2 Transform,
    Rect? AmbientClip,
    double DpiScale);
