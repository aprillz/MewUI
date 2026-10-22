namespace Aprillz.MewUI;

/// <summary>
/// A structured file filter: a display name plus one or more glob patterns (e.g. "*.png").
/// </summary>
/// <remarks>
/// Native backends translate this into their platform filter format; the managed dialog uses it directly.
/// </remarks>
/// <param name="Name">The display name.</param>
/// <param name="Patterns">The glob patterns, at least one.</param>
public sealed record FileFilter(string Name, params string[] Patterns)
{
    /// <summary>Returns whether <paramref name="fileName"/> matches any of this filter's patterns.</summary>
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
/// Managed-only advanced options. Native backends ignore these; they are applied when the managed dialog
/// is selected explicitly or used as a fallback.
/// </summary>
public sealed class ManagedDialogExtras
{
    /// <summary>Gets or sets whether hidden files and folders are shown in the managed browser.</summary>
    public bool ShowHiddenFiles { get; set; }
}

/// <summary>
/// Options for opening a single file or multiple files.
/// </summary>
public sealed class OpenFileDialogOptions
{
    /// <summary>Gets or sets the window that owns the dialog; null uses the active window.</summary>
    public Window? Owner { get; set; }
    /// <summary>Gets or sets the dialog title; the default is "Open".</summary>
    public string Title { get; set; } = "Open";
    /// <summary>Gets or sets the directory the dialog opens in, or null for the dialog's own default.</summary>
    public string? InitialDirectory { get; set; }
    /// <summary>Gets or sets the file filters offered, or null for no filtering.</summary>
    public IReadOnlyList<FileFilter>? Filters { get; set; }
    /// <summary>Gets or sets the zero-based index of the filter selected by default.</summary>
    public int FilterIndex { get; set; }
    /// <summary>Gets or sets whether more than one file can be selected.</summary>
    public bool Multiselect { get; set; }
    /// <summary>Gets or sets whether a native dialog is preferred when available, falling back to the managed dialog; the default is true.</summary>
    public bool PreferNative { get; set; } = true;
    /// <summary>Gets or sets the managed-only advanced options; null uses the defaults.</summary>
    public ManagedDialogExtras? Managed { get; set; }
}

/// <summary>
/// Options for saving a file.
/// </summary>
public sealed class SaveFileDialogOptions
{
    /// <summary>Gets or sets the window that owns the dialog; null uses the active window.</summary>
    public Window? Owner { get; set; }
    /// <summary>Gets or sets the dialog title; the default is "Save".</summary>
    public string Title { get; set; } = "Save";
    /// <summary>Gets or sets the directory the dialog opens in, or null for the dialog's own default.</summary>
    public string? InitialDirectory { get; set; }
    /// <summary>Gets or sets the file filters offered, or null for no filtering.</summary>
    public IReadOnlyList<FileFilter>? Filters { get; set; }
    /// <summary>Gets or sets the zero-based index of the filter selected by default.</summary>
    public int FilterIndex { get; set; }
    /// <summary>Gets or sets the file name the dialog starts with, or null for none.</summary>
    public string? FileName { get; set; }
    /// <summary>Gets or sets the extension appended when the user types none, or null for none.</summary>
    public string? DefaultExtension { get; set; }
    /// <summary>Gets or sets whether the dialog asks before overwriting an existing file; the default is true.</summary>
    public bool OverwritePrompt { get; set; } = true;
    /// <summary>Gets or sets whether a native dialog is preferred when available, falling back to the managed dialog; the default is true.</summary>
    public bool PreferNative { get; set; } = true;
    /// <summary>Gets or sets the managed-only advanced options; null uses the defaults.</summary>
    public ManagedDialogExtras? Managed { get; set; }
}

/// <summary>
/// Options for selecting a folder.
/// </summary>
public sealed class FolderDialogOptions
{
    /// <summary>Gets or sets the window that owns the dialog; null uses the active window.</summary>
    public Window? Owner { get; set; }
    /// <summary>Gets or sets the dialog title; the default is "Select folder".</summary>
    public string Title { get; set; } = "Select folder";
    /// <summary>Gets or sets the directory the dialog opens in, or null for the dialog's own default.</summary>
    public string? InitialDirectory { get; set; }
    /// <summary>Gets or sets whether a native dialog is preferred when available, falling back to the managed dialog; the default is true.</summary>
    public bool PreferNative { get; set; } = true;
    /// <summary>Gets or sets the managed-only advanced options; null uses the defaults.</summary>
    public ManagedDialogExtras? Managed { get; set; }
}

/// <summary>
/// Provides file dialogs (open/save/select folder), routed to the native OS dialog or the in-framework
/// managed dialog depending on the request's native preference and native availability.
/// </summary>
public static class FileDialog
{
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
        if (UseManaged(options.PreferNative))
        {
            var managed = RunManaged(FileDialogMode.Save, options.Owner, options.InitialDirectory, options.Filters, options.Managed);
            return managed is { Length: > 0 } ? managed[0] : null;
        }

        options.Owner = ResolveOwnerWindow(options.Owner);
        try
        {
            return Host.FileDialog.SaveFile(options);
        }
        catch (Exception ex)
        {
            if (!ManagedFileDialogGate.IsSupported)
            {
                throw;
            }

            LogNativeFallback(ex);
            var managed = RunManaged(FileDialogMode.Save, options.Owner, options.InitialDirectory, options.Filters, options.Managed);
            return managed is { Length: > 0 } ? managed[0] : null;
        }
    }

    /// <summary>Opens a dialog for selecting a folder.</summary>
    public static string? SelectFolder(FolderDialogOptions? options = null)
    {
        options ??= new FolderDialogOptions();
        if (UseManaged(options.PreferNative))
        {
            var managed = RunManaged(FileDialogMode.SelectFolder, options.Owner, options.InitialDirectory, null, options.Managed);
            return managed is { Length: > 0 } ? managed[0] : null;
        }

        options.Owner = ResolveOwnerWindow(options.Owner);
        try
        {
            return Host.FileDialog.SelectFolder(options);
        }
        catch (Exception ex)
        {
            if (!ManagedFileDialogGate.IsSupported)
            {
                throw;
            }

            LogNativeFallback(ex);
            var managed = RunManaged(FileDialogMode.SelectFolder, options.Owner, options.InitialDirectory, null, options.Managed);
            return managed is { Length: > 0 } ? managed[0] : null;
        }
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
        if (UseManaged(options.PreferNative))
        {
            return await RunManagedAsync(mode, options.Owner, options.InitialDirectory, options.Filters, options.Managed).ConfigureAwait(true);
        }

        options.Owner = ResolveOwnerWindow(options.Owner);
        try
        {
            return Host.FileDialog.OpenFile(options);
        }
        catch (Exception ex)
        {
            if (!ManagedFileDialogGate.IsSupported)
            {
                throw;
            }

            LogNativeFallback(ex);
            return await RunManagedAsync(mode, options.Owner, options.InitialDirectory, options.Filters, options.Managed).ConfigureAwait(true);
        }
    }

    /// <summary>Asynchronously opens a dialog for choosing a save path.</summary>
    public static async Task<string?> SaveFileAsync(SaveFileDialogOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new SaveFileDialogOptions();
        cancellationToken.ThrowIfCancellationRequested();
        if (UseManaged(options.PreferNative))
        {
            var managed = await RunManagedAsync(FileDialogMode.Save, options.Owner, options.InitialDirectory, options.Filters, options.Managed).ConfigureAwait(true);
            return managed is { Length: > 0 } ? managed[0] : null;
        }

        options.Owner = ResolveOwnerWindow(options.Owner);
        try
        {
            return Host.FileDialog.SaveFile(options);
        }
        catch (Exception ex)
        {
            if (!ManagedFileDialogGate.IsSupported)
            {
                throw;
            }

            LogNativeFallback(ex);
            var managed = await RunManagedAsync(FileDialogMode.Save, options.Owner, options.InitialDirectory, options.Filters, options.Managed).ConfigureAwait(true);
            return managed is { Length: > 0 } ? managed[0] : null;
        }
    }

    /// <summary>Asynchronously opens a dialog for selecting a folder.</summary>
    public static async Task<string?> SelectFolderAsync(FolderDialogOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new FolderDialogOptions();
        cancellationToken.ThrowIfCancellationRequested();
        if (UseManaged(options.PreferNative))
        {
            var managed = await RunManagedAsync(FileDialogMode.SelectFolder, options.Owner, options.InitialDirectory, null, options.Managed).ConfigureAwait(true);
            return managed is { Length: > 0 } ? managed[0] : null;
        }

        options.Owner = ResolveOwnerWindow(options.Owner);
        try
        {
            return Host.FileDialog.SelectFolder(options);
        }
        catch (Exception ex)
        {
            if (!ManagedFileDialogGate.IsSupported)
            {
                throw;
            }

            LogNativeFallback(ex);
            var managed = await RunManagedAsync(FileDialogMode.SelectFolder, options.Owner, options.InitialDirectory, null, options.Managed).ConfigureAwait(true);
            return managed is { Length: > 0 } ? managed[0] : null;
        }
    }

    private static string[]? RunOpen(OpenFileDialogOptions options, FileDialogMode mode)
    {
        if (UseManaged(options.PreferNative))
        {
            return RunManaged(mode, options.Owner, options.InitialDirectory, options.Filters, options.Managed);
        }

        options.Owner = ResolveOwnerWindow(options.Owner);
        try
        {
            return Host.FileDialog.OpenFile(options);
        }
        catch (Exception ex)
        {
            if (!ManagedFileDialogGate.IsSupported)
            {
                throw;
            }

            LogNativeFallback(ex);
            return RunManaged(mode, options.Owner, options.InitialDirectory, options.Filters, options.Managed);
        }
    }

    private static string[]? RunManaged(FileDialogMode mode, Window? owner, string? initialDirectory, IReadOnlyList<FileFilter>? filters, ManagedDialogExtras? extras)
    {
        if (!ManagedFileDialogGate.IsSupported)
        {
            throw new NotSupportedException("Managed file dialogs are disabled for this application.");
        }

        return RunManagedCore(mode, owner, initialDirectory, filters, extras);
    }

    private static string[]? RunManagedCore(FileDialogMode mode, Window? owner, string? initialDirectory, IReadOnlyList<FileFilter>? filters, ManagedDialogExtras? extras)
    {
        var dialog = new ManagedFileDialogWindow(mode, initialDirectory, filters, extras);
        dialog.ShowDialog(ResolveOwnerWindow(owner));
        return dialog.Accepted ? dialog.SelectedPaths.ToArray() : null;
    }

    private static Task<string[]?> RunManagedAsync(FileDialogMode mode, Window? owner, string? initialDirectory, IReadOnlyList<FileFilter>? filters, ManagedDialogExtras? extras)
    {
        if (!ManagedFileDialogGate.IsSupported)
        {
            throw new NotSupportedException("Managed file dialogs are disabled for this application.");
        }

        return RunManagedCoreAsync(mode, owner, initialDirectory, filters, extras);
    }

    private static async Task<string[]?> RunManagedCoreAsync(FileDialogMode mode, Window? owner, string? initialDirectory, IReadOnlyList<FileFilter>? filters, ManagedDialogExtras? extras)
    {
        var dialog = new ManagedFileDialogWindow(mode, initialDirectory, filters, extras);
        await dialog.ShowDialogAsync(ResolveOwnerWindow(owner)).ConfigureAwait(true);
        return dialog.Accepted ? dialog.SelectedPaths.ToArray() : null;
    }

    // PreferNative expresses a preference, not a guarantee. An unavailable native path uses managed
    // only when the application includes managed file-dialog support.
    private static bool UseManaged(bool preferNative)
        => ShouldUseManaged(preferNative, NativeAvailable(), ManagedFileDialogGate.IsSupported);

    internal static bool ShouldUseManaged(bool preferNative, bool nativeAvailable, bool managedSupported)
        => !preferNative || (managedSupported && !nativeAvailable);

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

    private static void LogNativeFallback(Exception exception)
        => DiagLog.Write($"[filedialog] Native dialog failed; falling back to managed. {exception.GetType().Name}");

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

internal static class ManagedFileDialogGate
{
    private const string ENABLED_SWITCH = "Aprillz.MewUI.ManagedFileDialogs.Enabled";

    private static readonly bool _isSupported =
        !AppContext.TryGetSwitch(ENABLED_SWITCH, out bool enabled) || enabled;

    internal static bool IsSupported => _isSupported;
}
