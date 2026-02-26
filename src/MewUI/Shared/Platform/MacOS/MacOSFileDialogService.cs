namespace Aprillz.MewUI.Platform.MacOS;

using System.Runtime.InteropServices;

internal sealed class MacOSFileDialogService : IFileDialogService
{
    public string[]? OpenFile(OpenFileDialogOptions options)
        => RunOpenPanel(options, selectFolder: false);

    public string? SaveFile(SaveFileDialogOptions options)
        => RunSavePanel(options);

    public string? SelectFolder(FolderDialogOptions options)
        => RunSelectFolder(options);

    private static bool _initialized;

    private static nint ClsNSOpenPanel;
    private static nint ClsNSSavePanel;
    private static nint ClsNSURL;
    private static nint ClsNSMutableArray;

    private static nint SelOpenPanel;
    private static nint SelSavePanel;
    private static nint SelRunModal;
    private static nint SelURL;
    private static nint SelURLs;
    private static nint SelPath;
    private static nint SelCount;
    private static nint SelObjectAtIndex;

    private static nint SelSetTitle;
    private static nint SelSetMessage;
    private static nint SelSetPrompt;
    private static nint SelSetDirectoryURL;
    private static nint SelSetNameFieldStringValue;
    private static nint SelSetAllowedFileTypes;
    private static nint SelSetAllowsMultipleSelection;
    private static nint SelSetCanChooseFiles;
    private static nint SelSetCanChooseDirectories;
    private static nint SelSetCanCreateDirectories;
    private static nint SelSetAllowsOtherFileTypes;

    private static nint SelFileURLWithPath;
    private static nint SelArray;
    private static nint SelAddObject;

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        MacOSInterop.EnsureApplicationInitialized();

        ClsNSOpenPanel = ObjC.GetClass("NSOpenPanel");
        ClsNSSavePanel = ObjC.GetClass("NSSavePanel");
        ClsNSURL = ObjC.GetClass("NSURL");
        ClsNSMutableArray = ObjC.GetClass("NSMutableArray");

        SelOpenPanel = ObjC.Sel("openPanel");
        SelSavePanel = ObjC.Sel("savePanel");
        SelRunModal = ObjC.Sel("runModal");
        SelURL = ObjC.Sel("URL");
        SelURLs = ObjC.Sel("URLs");
        SelPath = ObjC.Sel("path");
        SelCount = ObjC.Sel("count");
        SelObjectAtIndex = ObjC.Sel("objectAtIndex:");

        SelSetTitle = ObjC.Sel("setTitle:");
        SelSetMessage = ObjC.Sel("setMessage:");
        SelSetPrompt = ObjC.Sel("setPrompt:");
        SelSetDirectoryURL = ObjC.Sel("setDirectoryURL:");
        SelSetNameFieldStringValue = ObjC.Sel("setNameFieldStringValue:");
        SelSetAllowedFileTypes = ObjC.Sel("setAllowedFileTypes:");
        SelSetAllowsMultipleSelection = ObjC.Sel("setAllowsMultipleSelection:");
        SelSetCanChooseFiles = ObjC.Sel("setCanChooseFiles:");
        SelSetCanChooseDirectories = ObjC.Sel("setCanChooseDirectories:");
        SelSetCanCreateDirectories = ObjC.Sel("setCanCreateDirectories:");
        SelSetAllowsOtherFileTypes = ObjC.Sel("setAllowsOtherFileTypes:");

        SelFileURLWithPath = ObjC.Sel("fileURLWithPath:");
        SelArray = ObjC.Sel("array");
        SelAddObject = ObjC.Sel("addObject:");

        _initialized = true;
    }

    private static string[]? RunOpenPanel(OpenFileDialogOptions options, bool selectFolder)
    {
        ArgumentNullException.ThrowIfNull(options);
        MacOSInterop.EnsureApplicationInitialized();
        EnsureInitialized();

        using var pool = new MacOSInterop.AutoReleasePool();

        nint panel = ObjC.MsgSend_nint(ClsNSOpenPanel, SelOpenPanel);
        if (panel == 0)
        {
            return null;
        }

        ApplyCommonPanelOptions(panel, options.Title, options.InitialDirectory);

        // OpenPanel configuration.
        ObjC.MsgSend_void_nint_bool(panel, SelSetCanChooseFiles, !selectFolder);
        ObjC.MsgSend_void_nint_bool(panel, SelSetCanChooseDirectories, selectFolder);
        ObjC.MsgSend_void_nint_bool(panel, SelSetCanCreateDirectories, true);
        ObjC.MsgSend_void_nint_bool(panel, SelSetAllowsMultipleSelection, options.Multiselect && !selectFolder);

        var allowed = BuildAllowedFileTypes(options.Filter);
        if (allowed != 0 && !selectFolder)
        {
            ObjC.MsgSend_void_nint_nint(panel, SelSetAllowedFileTypes, allowed);
        }

        long response = ObjC.MsgSend_long(panel, SelRunModal);
        if (response != 1)
        {
            return null;
        }

        if (options.Multiselect && !selectFolder)
        {
            var urls = ObjC.MsgSend_nint(panel, SelURLs);
            return urls != 0 ? ExtractPathsFromUrlArray(urls) : null;
        }

        var url = ObjC.MsgSend_nint(panel, SelURL);
        if (url == 0)
        {
            return null;
        }

        var path = GetUrlPath(url);
        return path != null ? new[] { path } : null;
    }

    private static string? RunSelectFolder(FolderDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var files = RunOpenPanel(
            new OpenFileDialogOptions
            {
                Owner = options.Owner,
                Title = options.Title,
                InitialDirectory = options.InitialDirectory,
                Multiselect = false
            },
            selectFolder: true);

        return files is { Length: > 0 } ? files[0] : null;
    }

    private static string? RunSavePanel(SaveFileDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        MacOSInterop.EnsureApplicationInitialized();
        EnsureInitialized();

        using var pool = new MacOSInterop.AutoReleasePool();

        nint panel = ObjC.MsgSend_nint(ClsNSSavePanel, SelSavePanel);
        if (panel == 0)
        {
            return null;
        }

        ApplyCommonPanelOptions(panel, options.Title, options.InitialDirectory);
        ObjC.MsgSend_void_nint_bool(panel, SelSetCanCreateDirectories, true);

        if (!string.IsNullOrEmpty(options.FileName))
        {
            ObjC.MsgSend_void_nint_nint(panel, SelSetNameFieldStringValue, ObjC.CreateNSString(options.FileName));
        }

        // If a filter or default extension is provided, restrict file types.
        var allowed = BuildAllowedFileTypes(options.Filter, options.DefaultExtension);
        if (allowed != 0)
        {
            ObjC.MsgSend_void_nint_nint(panel, SelSetAllowedFileTypes, allowed);
            ObjC.MsgSend_void_nint_bool(panel, SelSetAllowsOtherFileTypes, false);
        }

        long response = ObjC.MsgSend_long(panel, SelRunModal);
        if (response != 1)
        {
            return null;
        }

        var url = ObjC.MsgSend_nint(panel, SelURL);
        return url != 0 ? GetUrlPath(url) : null;
    }

    private static void ApplyCommonPanelOptions(nint panel, string? title, string? initialDirectory)
    {
        if (!string.IsNullOrEmpty(title))
        {
            // Some panels display Title in the window chrome, Message within the panel.
            var ns = ObjC.CreateNSString(title);
            ObjC.MsgSend_void_nint_nint(panel, SelSetTitle, ns);
            ObjC.MsgSend_void_nint_nint(panel, SelSetMessage, ns);
        }

        if (!string.IsNullOrEmpty(initialDirectory) && ClsNSURL != 0)
        {
            var dirUrl = CreateFileUrl(initialDirectory);
            if (dirUrl != 0)
            {
                ObjC.MsgSend_void_nint_nint(panel, SelSetDirectoryURL, dirUrl);
            }
        }
    }

    private static nint CreateFileUrl(string path)
    {
        if (ClsNSURL == 0)
        {
            return 0;
        }

        var nsPath = ObjC.CreateNSString(path);
        return ObjC.MsgSend_nint_nint(ClsNSURL, SelFileURLWithPath, nsPath);
    }

    private static string? GetUrlPath(nint url)
    {
        if (url == 0)
        {
            return null;
        }

        var nsString = ObjC.MsgSend_nint(url, SelPath);
        if (nsString == 0)
        {
            return null;
        }

        // NSString UTF8String
        var utf8Sel = ObjC.Sel("UTF8String");
        var utf8 = ObjC.MsgSend_nint(nsString, utf8Sel);
        return utf8 != 0 ? Marshal.PtrToStringUTF8(utf8) : null;
    }

    private static string[] ExtractPathsFromUrlArray(nint nsArray)
    {
        var result = new List<string>();
        ulong count = ObjC.MsgSend_ulong(nsArray, SelCount);
        for (ulong i = 0; i < count; i++)
        {
            var url = ObjC.MsgSend_nint_ulong(nsArray, SelObjectAtIndex, i);
            var path = GetUrlPath(url);
            if (!string.IsNullOrEmpty(path))
            {
                result.Add(path);
            }
        }

        return result.ToArray();
    }

    private static nint BuildAllowedFileTypes(string? filter, string? defaultExtension = null)
    {
        // MewUI filter currently follows the Win32 "Name|pattern;pattern|Name|pattern" style.
        // We only use it to build a list of extensions (without the dot) for allowedFileTypes.
        // If parsing fails, return 0 to not restrict.
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var parts = filter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 1; i < parts.Length; i += 2)
            {
                foreach (var pat in parts[i].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var e = ExtractExtensionFromPattern(pat);
                    if (e != null)
                    {
                        exts.Add(e);
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(defaultExtension))
        {
            var e = defaultExtension.Trim();
            if (e.StartsWith('.'))
            {
                e = e[1..];
            }

            if (e.Length > 0 && e != "*" && e != "*.*")
            {
                exts.Add(e);
            }
        }

        // If filter only contains wildcard, don't restrict.
        exts.Remove("*");
        exts.Remove("*.*");

        if (exts.Count == 0 || ClsNSMutableArray == 0)
        {
            return 0;
        }

        // NSMutableArray *arr = [NSMutableArray array]; [arr addObject:@"png"]; ...
        var arr = ObjC.MsgSend_nint(ClsNSMutableArray, SelArray);
        if (arr == 0)
        {
            return 0;
        }

        foreach (var ext in exts)
        {
            ObjC.MsgSend_void_nint_nint(arr, SelAddObject, ObjC.CreateNSString(ext));
        }

        return arr;
    }

    private static string? ExtractExtensionFromPattern(string pattern)
    {
        // Accept patterns like "*.png", ".png", "png". Ignore "*.*" or "*".
        var p = pattern.Trim();
        if (p.Length == 0)
        {
            return null;
        }

        if (p == "*" || p == "*.*")
        {
            return null;
        }

        if (p.StartsWith("*."))
        {
            p = p[2..];
        }
        else if (p.StartsWith("."))
        {
            p = p[1..];
        }

        // Strip any remaining wildcards.
        p = p.Replace("*", string.Empty, StringComparison.Ordinal);
        if (p.Length == 0)
        {
            return null;
        }

        // If pattern contains '.', keep only last segment.
        int dot = p.LastIndexOf('.');
        if (dot >= 0 && dot + 1 < p.Length)
        {
            p = p[(dot + 1)..];
        }

        // Basic sanity.
        for (int i = 0; i < p.Length; i++)
        {
            char c = p[i];
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
            {
                return null;
            }
        }

        return p;
    }
}
