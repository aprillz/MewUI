namespace Aprillz.MewUI.Diagnostics;

internal static class ProfilerMarkers
{
    public static readonly ProfilerMarker WindowFrame = ProfilerMarker.Register("Window.Frame", ProfilerSampleCategory.Frame);
    public static readonly ProfilerMarker WindowLayout = ProfilerMarker.Register("Window.Layout", ProfilerSampleCategory.Layout);
    public static readonly ProfilerMarker VisualStateUpdate = ProfilerMarker.Register("Window.VisualStateUpdate", ProfilerSampleCategory.Layout);
    public static readonly ProfilerMarker StyleResolve = ProfilerMarker.Register("Window.StyleResolve", ProfilerSampleCategory.Layout);
    public static readonly ProfilerMarker ContentMeasure = ProfilerMarker.Register("Content.Measure", ProfilerSampleCategory.Measure);
    public static readonly ProfilerMarker ContentArrange = ProfilerMarker.Register("Content.Arrange", ProfilerSampleCategory.Arrange);
    public static readonly ProfilerMarker OverlayLayout = ProfilerMarker.Register("Overlay.Layout", ProfilerSampleCategory.Layout);
    public static readonly ProfilerMarker AnimationUpdate = ProfilerMarker.Register("Animation.Update", ProfilerSampleCategory.Animation);
    public static readonly ProfilerMarker BeginFrame = ProfilerMarker.Register("Backend.BeginFrame", ProfilerSampleCategory.Backend);
    public static readonly ProfilerMarker Clear = ProfilerMarker.Register("Render.Clear", ProfilerSampleCategory.Render);
    public static readonly ProfilerMarker ContentRender = ProfilerMarker.Register("Content.Render", ProfilerSampleCategory.Render);
    public static readonly ProfilerMarker TextLayout = ProfilerMarker.Register("Text.Layout", ProfilerSampleCategory.Render);
    public static readonly ProfilerMarker TextDraw = ProfilerMarker.Register("Text.Draw", ProfilerSampleCategory.Render);
    public static readonly ProfilerMarker AccessTextUnderline = ProfilerMarker.Register("AccessText.Underline", ProfilerSampleCategory.Render);
    public static readonly ProfilerMarker GCCollect = ProfilerMarker.Register("GC.Collect", ProfilerSampleCategory.GC);
    public static readonly ProfilerMarker AdornerRender = ProfilerMarker.Register("Adorner.Render", ProfilerSampleCategory.Render);
    public static readonly ProfilerMarker PopupRender = ProfilerMarker.Register("Popup.Render", ProfilerSampleCategory.Render);
    public static readonly ProfilerMarker OverlayRender = ProfilerMarker.Register("Overlay.Render", ProfilerSampleCategory.Render);
    public static readonly ProfilerMarker DevToolsRender = ProfilerMarker.Register("DevTools.Render", ProfilerSampleCategory.DevTools);
    public static readonly ProfilerMarker EndFrame = ProfilerMarker.Register("Backend.EndFrame", ProfilerSampleCategory.Backend);
    public static readonly ProfilerMarker Present = ProfilerMarker.Register("Backend.Present", ProfilerSampleCategory.VSyncWait);
}
