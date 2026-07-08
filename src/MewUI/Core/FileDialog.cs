namespace Aprillz.MewUI;

/// <summary>
/// Selects how a file dialog is presented: <see cref="Auto"/> (native when available, else managed),
/// <see cref="Native"/> (OS dialog), or <see cref="Managed"/> (in-framework MewUI dialog).
/// </summary>
public enum FileDialogBackend
{
    Auto,
    Native,
    Managed,
}

/// <summary>
/// A structured file filter: a display name plus one or more glob patterns (e.g. "*.png").
/// Native backends translate this into their platform filter format; the managed backend uses it directly.
/// </summary>
public sealed record FileFilter(string Name, params string[] Patterns)
{
    /// <summary>True when <paramref name="fileName"/> matches any of this filter's patterns.</summary>
    public bool Matches(string fileName)
    {
        foreach (var pattern in Patterns)
        {
            if (pattern is "*" or "*.*")
            {
                return true;
            }
            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                if (fileName.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(pattern, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Parses the legacy "Name|*.a;*.b|Name2|*.c" pipe string (alternating display name and
    /// semicolon-separated patterns) into structured filters, easing migration from the old string form.
    /// </summary>
    public static IReadOnlyList<FileFilter> Parse(string? legacy)
    {
        if (string.IsNullOrEmpty(legacy))
        {
            return Array.Empty<FileFilter>();
        }

        var tokens = legacy.Split('|');
        var result = new List<FileFilter>(tokens.Length / 2);
        for (int i = 0; i + 1 < tokens.Length; i += 2)
        {
            string name = tokens[i].Trim();
            var patterns = tokens[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (name.Length == 0 || patterns.Length == 0)
            {
                continue;
            }
            result.Add(new FileFilter(name, patterns));
        }

        return result;
    }
}

/// <summary>
/// Managed-only advanced options. Native backends ignore these; setting this on an option object with
/// <see cref="FileDialogBackend.Auto"/> biases resolution toward the managed backend.
/// </summary>
public sealed class ManagedDialogExtras
{
    /// <summary>Show hidden files and folders in the managed browser.</summary>
    public bool ShowHiddenFiles { get; set; }
}

/// <summary>
/// Options for opening a single file or multiple files.
/// </summary>
public sealed class OpenFileDialogOptions
{
    /// <summary>Owner window the dialog is parented to (optional; the active window is used when null).</summary>
    public Window? Owner { get; set; }
    /// <summary>Dialog title.</summary>
    public string Title { get; set; } = "Open";
    /// <summary>Initial directory (optional).</summary>
    public string? InitialDirectory { get; set; }
    /// <summary>Structured file filters (optional).</summary>
    public IReadOnlyList<FileFilter>? Filters { get; set; }
    /// <summary>Zero-based index of the filter selected by default.</summary>
    public int FilterIndex { get; set; }
    /// <summary>Whether multi-selection is enabled.</summary>
    public bool Multiselect { get; set; }
    /// <summary>Requested backend (default <see cref="FileDialogBackend.Auto"/>).</summary>
    public FileDialogBackend Backend { get; set; } = FileDialogBackend.Auto;
    /// <summary>Managed-only advanced options (null uses defaults; setting biases Auto toward managed).</summary>
    public ManagedDialogExtras? Managed { get; set; }
}

/// <summary>
/// Options for saving a file.
/// </summary>
public sealed class SaveFileDialogOptions
{
    /// <summary>Owner window the dialog is parented to (optional; the active window is used when null).</summary>
    public Window? Owner { get; set; }
    /// <summary>Dialog title.</summary>
    public string Title { get; set; } = "Save";
    /// <summary>Initial directory (optional).</summary>
    public string? InitialDirectory { get; set; }
    /// <summary>Structured file filters (optional).</summary>
    public IReadOnlyList<FileFilter>? Filters { get; set; }
    /// <summary>Zero-based index of the filter selected by default.</summary>
    public int FilterIndex { get; set; }
    /// <summary>Initial file name (optional).</summary>
    public string? FileName { get; set; }
    /// <summary>Default extension (optional).</summary>
    public string? DefaultExtension { get; set; }
    /// <summary>Whether to prompt before overwriting an existing file.</summary>
    public bool OverwritePrompt { get; set; } = true;
    /// <summary>Requested backend (default <see cref="FileDialogBackend.Auto"/>).</summary>
    public FileDialogBackend Backend { get; set; } = FileDialogBackend.Auto;
    /// <summary>Managed-only advanced options (null uses defaults; setting biases Auto toward managed).</summary>
    public ManagedDialogExtras? Managed { get; set; }
}

/// <summary>
/// Options for selecting a folder.
/// </summary>
public sealed class FolderDialogOptions
{
    /// <summary>Owner window the dialog is parented to (optional; the active window is used when null).</summary>
    public Window? Owner { get; set; }
    /// <summary>Dialog title.</summary>
    public string Title { get; set; } = "Select folder";
    /// <summary>Initial directory (optional).</summary>
    public string? InitialDirectory { get; set; }
    /// <summary>Requested backend (default <see cref="FileDialogBackend.Auto"/>).</summary>
    public FileDialogBackend Backend { get; set; } = FileDialogBackend.Auto;
    /// <summary>Managed-only advanced options (null uses defaults; setting biases Auto toward managed).</summary>
    public ManagedDialogExtras? Managed { get; set; }
}

/// <summary>
/// Provides file dialogs (open/save/select folder), routed to the native OS dialog or the in-framework
/// managed dialog depending on the requested <see cref="FileDialogBackend"/> and native availability.
/// </summary>
public static class FileDialog
{
    /// <summary>Global backend preference applied when a call leaves <c>Backend</c> as Auto. Default: Auto.</summary>
    public static FileDialogBackend PreferredBackend { get; set; } = FileDialogBackend.Auto;

    /// <summary>Opens a dialog for selecting a single file.</summary>
    public static string? OpenFile(OpenFileDialogOptions? options = null)
    {
        options ??= new OpenFileDialogOptions();
        options.Multiselect = false;
        var result = RunOpen(options, FileDialogMode.OpenSingle);
        return result is { Length: > 0 } ? result[0] : null;
    }

    /// <summary>Opens a dialog for selecting multiple files.</summary>
    public static string[]? OpenFiles(OpenFileDialogOptions? options = null)
    {
        options ??= new OpenFileDialogOptions();
        options.Multiselect = true;
        return RunOpen(options, FileDialogMode.OpenMultiple);
    }

    /// <summary>Opens a dialog for choosing a save path.</summary>
    public static string? SaveFile(SaveFileDialogOptions? options = null)
    {
        options ??= new SaveFileDialogOptions();
        if (UseManaged(options.Backend, options.Managed))
        {
            var managed = RunManaged(FileDialogMode.Save, options.Owner, options.InitialDirectory, options.Filters, options.Managed);
            return managed is { Length: > 0 } ? managed[0] : null;
        }

        options.Owner = ResolveOwnerWindow(options.Owner);
        return Host.FileDialog.SaveFile(options);
    }

    /// <summary>Opens a dialog for selecting a folder.</summary>
    public static string? SelectFolder(FolderDialogOptions? options = null)
    {
        options ??= new FolderDialogOptions();
        if (UseManaged(options.Backend, options.Managed))
        {
            var managed = RunManaged(FileDialogMode.SelectFolder, options.Owner, options.InitialDirectory, null, options.Managed);
            return managed is { Length: > 0 } ? managed[0] : null;
        }

        options.Owner = ResolveOwnerWindow(options.Owner);
        return Host.FileDialog.SelectFolder(options);
    }

    /// <summary>Asynchronously opens a dialog for selecting a single file.</summary>
    public static async Task<string?> OpenFileAsync(OpenFileDialogOptions? options = null, CancellationToken cancellationToken = default)
    {
        var result = await OpenFilesAsync(options ?? new OpenFileDialogOptions(), cancellationToken).ConfigureAwait(true);
        return result is { Length: > 0 } ? result[0] : null;
    }

    /// <summary>Asynchronously opens a dialog for selecting multiple files.</summary>
    public static async Task<string[]?> OpenFilesAsync(OpenFileDialogOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new OpenFileDialogOptions();
        cancellationToken.ThrowIfCancellationRequested();
        var mode = options.Multiselect ? FileDialogMode.OpenMultiple : FileDialogMode.OpenSingle;
        if (UseManaged(options.Backend, options.Managed))
        {
            return await RunManagedAsync(mode, options.Owner, options.InitialDirectory, options.Filters, options.Managed).ConfigureAwait(true);
        }

        options.Owner = ResolveOwnerWindow(options.Owner);
        return Host.FileDialog.OpenFile(options);
    }

    /// <summary>Asynchronously opens a dialog for choosing a save path.</summary>
    public static async Task<string?> SaveFileAsync(SaveFileDialogOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new SaveFileDialogOptions();
        cancellationToken.ThrowIfCancellationRequested();
        if (UseManaged(options.Backend, options.Managed))
        {
            var managed = await RunManagedAsync(FileDialogMode.Save, options.Owner, options.InitialDirectory, options.Filters, options.Managed).ConfigureAwait(true);
            return managed is { Length: > 0 } ? managed[0] : null;
        }

        options.Owner = ResolveOwnerWindow(options.Owner);
        return Host.FileDialog.SaveFile(options);
    }

    /// <summary>Asynchronously opens a dialog for selecting a folder.</summary>
    public static async Task<string?> SelectFolderAsync(FolderDialogOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new FolderDialogOptions();
        cancellationToken.ThrowIfCancellationRequested();
        if (UseManaged(options.Backend, options.Managed))
        {
            var managed = await RunManagedAsync(FileDialogMode.SelectFolder, options.Owner, options.InitialDirectory, null, options.Managed).ConfigureAwait(true);
            return managed is { Length: > 0 } ? managed[0] : null;
        }

        options.Owner = ResolveOwnerWindow(options.Owner);
        return Host.FileDialog.SelectFolder(options);
    }

    private static string[]? RunOpen(OpenFileDialogOptions options, FileDialogMode mode)
    {
        if (UseManaged(options.Backend, options.Managed))
        {
            return RunManaged(mode, options.Owner, options.InitialDirectory, options.Filters, options.Managed);
        }

        options.Owner = ResolveOwnerWindow(options.Owner);
        return Host.FileDialog.OpenFile(options);
    }

    private static string[]? RunManaged(FileDialogMode mode, Window? owner, string? initialDirectory, IReadOnlyList<FileFilter>? filters, ManagedDialogExtras? extras)
    {
        var dialog = new ManagedFileDialogWindow(mode, initialDirectory, filters, extras);
        dialog.ShowDialog(ResolveOwnerWindow(owner));
        return dialog.Accepted ? dialog.SelectedPaths.ToArray() : null;
    }

    private static async Task<string[]?> RunManagedAsync(FileDialogMode mode, Window? owner, string? initialDirectory, IReadOnlyList<FileFilter>? filters, ManagedDialogExtras? extras)
    {
        var dialog = new ManagedFileDialogWindow(mode, initialDirectory, filters, extras);
        await dialog.ShowDialogAsync(ResolveOwnerWindow(owner)).ConfigureAwait(true);
        return dialog.Accepted ? dialog.SelectedPaths.ToArray() : null;
    }

    // Resolves the effective backend (per-call Backend, then PreferredBackend, then Auto) and decides whether
    // the managed backend is used. Native requested but unavailable falls back to managed with a warning.
    private static bool UseManaged(FileDialogBackend requested, ManagedDialogExtras? extras)
    {
        var effective = requested != FileDialogBackend.Auto
            ? requested
            : PreferredBackend != FileDialogBackend.Auto ? PreferredBackend : FileDialogBackend.Auto;

        if (effective == FileDialogBackend.Managed)
        {
            return true;
        }

        if (effective == FileDialogBackend.Native)
        {
            if (NativeAvailable())
            {
                return false;
            }

            DiagLog.Write("[filedialog] Native backend requested but unavailable; falling back to managed.");
            return true;
        }

        // Auto: managed-only advanced options bias toward managed; otherwise prefer native when available.
        if (extras != null)
        {
            return true;
        }

        return !NativeAvailable();
    }

    private static bool NativeAvailable()
    {
        try
        {
            return Host.FileDialog.IsNativeDialogAvailable();
        }
        catch
        {
            return false;
        }
    }

    private static Platform.IPlatformHost Host =>
        Application.IsRunning ? Application.Current.PlatformHost : Application.DefaultPlatformHost;

    // Resolve the active window as the dialog owner when the caller didn't set one, so the dialog is parented
    // (transient + modal) to the application window instead of floating free.
    private static Window? ResolveOwnerWindow(Window? current)
    {
        if (current != null || !Application.IsRunning)
        {
            return current;
        }

        var windows = Application.Current.AllWindows;
        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i].IsActive && windows[i].Handle != 0)
            {
                return windows[i];
            }
        }

        for (int i = 0; i < windows.Count; i++)
        {
            if (windows[i].Handle != 0)
            {
                return windows[i];
            }
        }

        return null;
    }
}
