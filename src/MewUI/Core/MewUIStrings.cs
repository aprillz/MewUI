using System.Globalization;

namespace Aprillz.MewUI;

/// <summary>
/// Centralized UI strings that can be resolved by a host-provided localizer.
/// </summary>
public static class MewUIStrings
{
    private static readonly object SyncRoot = new();
    private static readonly List<LocalizedStringEntry> Entries = [];
    private static Func<string, string, CultureInfo, string?>? _localizer;
    private static CultureInfo? _culture;

    // MessageBox
    public static ObservableValue<string> Information { get; } = Register(nameof(Information), "Information");

    public static ObservableValue<string> Warning { get; } = Register(nameof(Warning), "Warning");

    public static ObservableValue<string> Error { get; } = Register(nameof(Error), "Error");

    public static ObservableValue<string> Question { get; } = Register(nameof(Question), "Confirm");

    public static ObservableValue<string> Success { get; } = Register(nameof(Success), "Success");

    public static ObservableValue<string> Shield { get; } = Register(nameof(Shield), "Security");

    public static ObservableValue<string> Crash { get; } = Register(nameof(Crash), "Crash");

    public static ObservableValue<string> ShowDetail { get; } = Register(nameof(ShowDetail), "Show _Details");

    public static ObservableValue<string> OK { get; } = Register(nameof(OK), "_OK");

    public static ObservableValue<string> Cancel { get; } = Register(nameof(Cancel), "_Cancel");

    public static ObservableValue<string> Yes { get; } = Register(nameof(Yes), "_Yes");

    public static ObservableValue<string> No { get; } = Register(nameof(No), "_No");

    public static ObservableValue<string> Retry { get; } = Register(nameof(Retry), "_Retry");

    public static ObservableValue<string> Ignore { get; } = Register(nameof(Ignore), "_Ignore");

    // BusyIndicator
    public static ObservableValue<string> Abort { get; } = Register(nameof(Abort), "Abort");

    public static ObservableValue<string> AbortConfirmation { get; } = Register(nameof(AbortConfirmation), "Are you sure you want to abort this operation?");

    public static ObservableValue<string> Aborting { get; } = Register(nameof(Aborting), "Aborting...");

    // Text editing
    public static ObservableValue<string> Undo { get; } = Register(nameof(Undo), "Undo");

    public static ObservableValue<string> Redo { get; } = Register(nameof(Redo), "Redo");

    public static ObservableValue<string> Cut { get; } = Register(nameof(Cut), "Cut");

    public static ObservableValue<string> Copy { get; } = Register(nameof(Copy), "Copy");

    public static ObservableValue<string> Paste { get; } = Register(nameof(Paste), "Paste");

    public static ObservableValue<string> SelectAll { get; } = Register(nameof(SelectAll), "Select All");

    // File dialogs
    public static ObservableValue<string> OpenFileDialogTitle { get; } = Register(nameof(OpenFileDialogTitle), "Open");

    public static ObservableValue<string> SaveFileDialogTitle { get; } = Register(nameof(SaveFileDialogTitle), "Save");

    public static ObservableValue<string> SelectFolderDialogTitle { get; } = Register(nameof(SelectFolderDialogTitle), "Select folder");

    // Common controls
    public static ObservableValue<string> WindowTitle { get; } = Register(nameof(WindowTitle), "Window");

    public static ObservableValue<string> ColorPickerHexLabel { get; } = Register(nameof(ColorPickerHexLabel), "Hex");

    // Dev tools
    internal static ObservableValue<string> DevToolsInspectorLine { get; } = Register(nameof(DevToolsInspectorLine), "Inspector: Ctrl/Cmd+Shift+I");

    internal static ObservableValue<string> DevToolsVisualTreeLine { get; } = Register(nameof(DevToolsVisualTreeLine), "VisualTree: Ctrl/Cmd+Shift+T");

    internal static ObservableValue<string> DevToolsHoverPrefix { get; } = Register(nameof(DevToolsHoverPrefix), "Hover: ");

    internal static ObservableValue<string> DevToolsFocusPrefix { get; } = Register(nameof(DevToolsFocusPrefix), "Focus: ");

    internal static ObservableValue<string> DevToolsSelectedPrefix { get; } = Register(nameof(DevToolsSelectedPrefix), "Selected: ");

    internal static ObservableValue<string> DevToolsSelectedNone { get; } = Register(nameof(DevToolsSelectedNone), "Selected: (none)");

    internal static ObservableValue<string> DevToolsNonePlaceholder { get; } = Register(nameof(DevToolsNonePlaceholder), "(none)");

    internal static ObservableValue<string> DevToolsLiveVisualTreeTitle { get; } = Register(nameof(DevToolsLiveVisualTreeTitle), "Live Visual Tree");

    internal static ObservableValue<string> DevToolsModeFollowPeek { get; } = Register(nameof(DevToolsModeFollowPeek), "Mode: Follow/Peek");

    internal static ObservableValue<string> DevToolsFollowFocus { get; } = Register(nameof(DevToolsFollowFocus), "Follow Focus");

    internal static ObservableValue<string> DevToolsAutoExpandFocus { get; } = Register(nameof(DevToolsAutoExpandFocus), "Auto Expand Focus");

    internal static ObservableValue<string> DevToolsRefresh { get; } = Register(nameof(DevToolsRefresh), "Refresh");

    internal static ObservableValue<string> DevToolsGoFocus { get; } = Register(nameof(DevToolsGoFocus), "Go Focus");

    internal static ObservableValue<string> DevToolsPickClick { get; } = Register(nameof(DevToolsPickClick), "Pick (Click)");

    internal static ObservableValue<string> DevToolsClearSelection { get; } = Register(nameof(DevToolsClearSelection), "Clear Selection");

    internal static ObservableValue<string> DevToolsPickArmed { get; } = Register(nameof(DevToolsPickArmed), "Pick: ARMED (click target)");

    internal static ObservableValue<string> DevToolsModePick { get; } = Register(nameof(DevToolsModePick), "Mode: Pick (click in target window to select)");

    internal static ObservableValue<string> DevToolsContentRoot { get; } = Register(nameof(DevToolsContentRoot), "Content");

    internal static ObservableValue<string> DevToolsContentNullRoot { get; } = Register(nameof(DevToolsContentNullRoot), "Content (null)");

    internal static ObservableValue<string> DevToolsPopupsRoot { get; } = Register(nameof(DevToolsPopupsRoot), "Popups");

    internal static ObservableValue<string> DevToolsAdornersRoot { get; } = Register(nameof(DevToolsAdornersRoot), "Adorners");

    internal static ObservableValue<string> ProfilerTitle { get; } = Register(nameof(ProfilerTitle), "MewUI Profiler");

    internal static ObservableValue<string> ProfilerFpsLabel { get; } = Register(nameof(ProfilerFpsLabel), "FPS ");

    internal static ObservableValue<string> ProfilerFrameLabel { get; } = Register(nameof(ProfilerFrameLabel), "Frame ");

    internal static ObservableValue<string> ProfilerAverageLabel { get; } = Register(nameof(ProfilerAverageLabel), "Avg ");

    internal static ObservableValue<string> ProfilerMinLabel { get; } = Register(nameof(ProfilerMinLabel), "Min ");

    internal static ObservableValue<string> ProfilerMaxLabel { get; } = Register(nameof(ProfilerMaxLabel), "Max ");

    internal static ObservableValue<string> ProfilerLayoutLabel { get; } = Register(nameof(ProfilerLayoutLabel), "Layout ");

    internal static ObservableValue<string> ProfilerMeasureLabel { get; } = Register(nameof(ProfilerMeasureLabel), "Measure ");

    internal static ObservableValue<string> ProfilerArrangeLabel { get; } = Register(nameof(ProfilerArrangeLabel), "Arrange ");

    internal static ObservableValue<string> ProfilerAnimationShortLabel { get; } = Register(nameof(ProfilerAnimationShortLabel), "Anim ");

    internal static ObservableValue<string> ProfilerRenderLabel { get; } = Register(nameof(ProfilerRenderLabel), "Render ");

    internal static ObservableValue<string> ProfilerDevToolsShortLabel { get; } = Register(nameof(ProfilerDevToolsShortLabel), "Dev ");

    internal static ObservableValue<string> ProfilerEndFrameShortLabel { get; } = Register(nameof(ProfilerEndFrameShortLabel), "End ");

    internal static ObservableValue<string> ProfilerPresentLabel { get; } = Register(nameof(ProfilerPresentLabel), "Present ");

    internal static ObservableValue<string> ProfilerDrawLabel { get; } = Register(nameof(ProfilerDrawLabel), "Draw ");

    internal static ObservableValue<string> ProfilerCullLabel { get; } = Register(nameof(ProfilerCullLabel), "Cull ");

    internal static ObservableValue<string> ProfilerAllocatedLabel { get; } = Register(nameof(ProfilerAllocatedLabel), "Alloc ");

    internal static ObservableValue<string> ProfilerGcLabel { get; } = Register(nameof(ProfilerGcLabel), "GC ");

    internal static ObservableValue<string> ProfilerPrimitiveShapeLabel { get; } = Register(nameof(ProfilerPrimitiveShapeLabel), "Prim Shape ");

    internal static ObservableValue<string> ProfilerShapeLabel { get; } = Register(nameof(ProfilerShapeLabel), "Shape ");

    internal static ObservableValue<string> ProfilerTextPrimitiveLabel { get; } = Register(nameof(ProfilerTextPrimitiveLabel), "Text ");

    internal static ObservableValue<string> ProfilerImagePrimitiveLabel { get; } = Register(nameof(ProfilerImagePrimitiveLabel), "Img ");

    internal static ObservableValue<string> ProfilerClipLabel { get; } = Register(nameof(ProfilerClipLabel), "Clip ");

    internal static ObservableValue<string> ProfilerLoopLabel { get; } = Register(nameof(ProfilerLoopLabel), "Loop ");

    internal static ObservableValue<string> ProfilerVSyncLabel { get; } = Register(nameof(ProfilerVSyncLabel), "VSync ");

    internal static ObservableValue<string> ProfilerTargetLabel { get; } = Register(nameof(ProfilerTargetLabel), "Target ");

    internal static ObservableValue<string> ProfilerLoopNotRunning { get; } = Register(nameof(ProfilerLoopNotRunning), "Loop (not running)");

    internal static ObservableValue<string> ProfilerLivePauseHint { get; } = Register(nameof(ProfilerLivePauseHint), "Space: Live/Pause");

    internal static ObservableValue<string> ProfilerSpacePauseLive { get; } = Register(nameof(ProfilerSpacePauseLive), "Space: pause/live");

    internal static ObservableValue<string> ProfilerShortcutHint { get; } = Register(nameof(ProfilerShortcutHint), "Ctrl+Shift+Alt+P: Profiler");

    internal static ObservableValue<string> ProfilerNoFrames { get; } = Register(nameof(ProfilerNoFrames), "No profiler frames yet");

    internal static ObservableValue<string> ProfilerNoSelectedFrameSamples { get; } = Register(nameof(ProfilerNoSelectedFrameSamples), "No selected frame samples");

    internal static ObservableValue<string> ProfilerMainThread { get; } = Register(nameof(ProfilerMainThread), "Main Thread");

    internal static ObservableValue<string> ProfilerCpuLabel { get; } = Register(nameof(ProfilerCpuLabel), "CPU ");

    internal static ObservableValue<string> ProfilerGpuLabel { get; } = Register(nameof(ProfilerGpuLabel), "GPU ");

    internal static ObservableValue<string> ProfilerSamplesLabel { get; } = Register(nameof(ProfilerSamplesLabel), "Samples ");

    internal static ObservableValue<string> ProfilerOverflowLabel { get; } = Register(nameof(ProfilerOverflowLabel), "Overflow ");

    internal static ObservableValue<string> ProfilerStartLabel { get; } = Register(nameof(ProfilerStartLabel), "Start ");

    internal static ObservableValue<string> ProfilerCategoryLabel { get; } = Register(nameof(ProfilerCategoryLabel), "Category ");

    internal static ObservableValue<string> ProfilerDepthLabel { get; } = Register(nameof(ProfilerDepthLabel), "Depth ");

    internal static ObservableValue<string> ProfilerElementLabel { get; } = Register(nameof(ProfilerElementLabel), "Element ");

    internal static ObservableValue<string> ProfilerElementNone { get; } = Register(nameof(ProfilerElementNone), "Element (none)");

    internal static ObservableValue<string> ProfilerNameHeader { get; } = Register(nameof(ProfilerNameHeader), "Name");

    internal static ObservableValue<string> ProfilerTotalHeader { get; } = Register(nameof(ProfilerTotalHeader), "Total");

    internal static ObservableValue<string> ProfilerSelfHeader { get; } = Register(nameof(ProfilerSelfHeader), "Self");

    internal static ObservableValue<string> ProfilerCallsHeader { get; } = Register(nameof(ProfilerCallsHeader), "Calls");

    internal static ObservableValue<string> ProfilerLive { get; } = Register(nameof(ProfilerLive), "Live");

    internal static ObservableValue<string> ProfilerPaused { get; } = Register(nameof(ProfilerPaused), "Paused");

    internal static ObservableValue<string> ProfilerOn { get; } = Register(nameof(ProfilerOn), "On");

    internal static ObservableValue<string> ProfilerOff { get; } = Register(nameof(ProfilerOff), "Off");

    internal static ObservableValue<string> ProfilerRenderLoopOnRequest { get; } = Register(nameof(ProfilerRenderLoopOnRequest), "OnRequest");

    internal static ObservableValue<string> ProfilerRenderLoopContinuous { get; } = Register(nameof(ProfilerRenderLoopContinuous), "Continuous");

    internal static ObservableValue<string> ProfilerCategoryFrame { get; } = Register(nameof(ProfilerCategoryFrame), "Frame");

    internal static ObservableValue<string> ProfilerCategoryLayout { get; } = Register(nameof(ProfilerCategoryLayout), "Layout");

    internal static ObservableValue<string> ProfilerCategoryMeasure { get; } = Register(nameof(ProfilerCategoryMeasure), "Measure");

    internal static ObservableValue<string> ProfilerCategoryArrange { get; } = Register(nameof(ProfilerCategoryArrange), "Arrange");

    internal static ObservableValue<string> ProfilerCategoryRender { get; } = Register(nameof(ProfilerCategoryRender), "Render");

    internal static ObservableValue<string> ProfilerCategoryAnimation { get; } = Register(nameof(ProfilerCategoryAnimation), "Animation");

    internal static ObservableValue<string> ProfilerCategoryBackend { get; } = Register(nameof(ProfilerCategoryBackend), "Backend");

    internal static ObservableValue<string> ProfilerCategoryVSyncWait { get; } = Register(nameof(ProfilerCategoryVSyncWait), "VSyncWait");

    internal static ObservableValue<string> ProfilerCategoryDevTools { get; } = Register(nameof(ProfilerCategoryDevTools), "DevTools");

    internal static ObservableValue<string> ProfilerCategoryGc { get; } = Register(nameof(ProfilerCategoryGc), "GC");

    internal static ObservableValue<string> ProfilerCategoryOther { get; } = Register(nameof(ProfilerCategoryOther), "Other");

    /// <summary>
    /// Gets the explicitly applied culture. A null value means the last <see cref="SetCulture(CultureInfo?)"/>
    /// call used <see cref="CultureInfo.CurrentUICulture"/>.
    /// </summary>
    public static CultureInfo? Culture
    {
        get
        {
            lock (SyncRoot)
            {
                return _culture;
            }
        }
    }

    /// <summary>
    /// Configures the host-provided string resolver used by <see cref="SetCulture(CultureInfo?)"/>.
    /// Pass <c>null</c> to reset all entries to their default values.
    /// </summary>
    /// <param name="localizer">
    /// A resolver that receives the string key, default value, and resolved culture. Returning <c>null</c>
    /// uses the default value. The host owns any culture fallback and resource loading behavior.
    /// </param>
    /// <param name="culture">Culture to apply immediately, or null for the current UI culture.</param>
    public static void SetLocalizer(Func<string, string, CultureInfo, string?>? localizer, CultureInfo? culture = null)
    {
        lock (SyncRoot)
        {
            _localizer = localizer;
        }

        SetCulture(culture);
    }

    /// <summary>
    /// Applies values from the current host-provided localizer to the centralized string store.
    /// Pass <c>null</c> to use <see cref="CultureInfo.CurrentUICulture"/>.
    /// Controls that read these values after this call, or bind to these <see cref="ObservableValue{T}"/>
    /// entries, will observe the updated values. Controls that already copied a string value are not
    /// refreshed automatically.
    /// </summary>
    /// <param name="culture">Culture to apply, or null for the current UI culture.</param>
    public static void SetCulture(CultureInfo? culture = null)
    {
        CultureInfo resolvedCulture = culture ?? CultureInfo.CurrentUICulture;
        Func<string, string, CultureInfo, string?>? localizer;
        LocalizedStringEntry[] entries;

        lock (SyncRoot)
        {
            _culture = culture;
            localizer = _localizer;
            entries = [.. Entries];
        }

        for (int i = 0; i < entries.Length; i++)
        {
            LocalizedStringEntry entry = entries[i];
            entry.Value.Value = localizer?.Invoke(entry.Name, entry.DefaultValue, resolvedCulture) ?? entry.DefaultValue;
        }
    }

    private static ObservableValue<string> Register(string name, string defaultValue)
    {
        var value = new ObservableValue<string>(defaultValue);
        Entries.Add(new LocalizedStringEntry(name, defaultValue, value));
        return value;
    }

    private sealed record LocalizedStringEntry(
        string Name,
        string DefaultValue,
        ObservableValue<string> Value);
}
