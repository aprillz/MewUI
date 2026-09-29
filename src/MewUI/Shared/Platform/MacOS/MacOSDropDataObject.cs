namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>
/// The data of one drag onto a view: every type on the dragging pasteboard, listed by its type identifier, plus the
/// standard formats derived from file URLs, URLs and plain text. Values are read from the pasteboard on request.
/// </summary>
internal sealed class MacOSDropDataObject : PlatformDataObject
{
    private const string FILE_URL_TYPE = "public.file-url";
    private const string FILE_NAMES_TYPE = "NSFilenamesPboardType";
    private const string URL_TYPE = "public.url";
    private const string TEXT_TYPE = "public.utf8-plain-text";
    private const string LEGACY_TEXT_TYPE = "NSStringPboardType";

    private readonly HashSet<string> _types;
    private nint _pasteboard;

    public MacOSDropDataObject(nint pasteboard)
    {
        _pasteboard = ObjC.Retain(pasteboard);
        var types = MacOSWindowInterop.PasteboardTypes(pasteboard);
        _types = new HashSet<string>(types, StringComparer.Ordinal);

        bool hasFiles = _types.Contains(FILE_URL_TYPE) || _types.Contains(FILE_NAMES_TYPE);
        if (hasFiles)
        {
            AddFormat(StandardDataFormats.StorageItems);
        }

        if (hasFiles || _types.Contains(URL_TYPE))
        {
            AddFormat(StandardDataFormats.Uris);
        }

        if (_types.Contains(TEXT_TYPE) || _types.Contains(LEGACY_TEXT_TYPE))
        {
            AddFormat(StandardDataFormats.Text);
        }

        foreach (var type in types)
        {
            AddFormat(type);
        }
    }

    protected override object? Read(string format)
    {
        if (_pasteboard == 0)
        {
            return null;
        }

        switch (format)
        {
            case StandardDataFormats.StorageItems:
                return ReadFiles();

            case StandardDataFormats.Uris:
                return ReadUris();

            case StandardDataFormats.Text:
                return MacOSWindowInterop.PasteboardString(_pasteboard, TEXT_TYPE)
                    ?? MacOSWindowInterop.PasteboardString(_pasteboard, LEGACY_TEXT_TYPE);

            default:
                return _types.Contains(format) ? MacOSWindowInterop.PasteboardData(_pasteboard, format) : null;
        }
    }

    protected override void OnClosed()
    {
        // Balances the retain in the constructor through the pump's autorelease pool.
        ObjC.Autorelease(_pasteboard);
        _pasteboard = 0;
    }

    private List<string> ReadFiles()
    {
        var paths = new List<string>();
        foreach (var url in MacOSWindowInterop.PasteboardItemStrings(_pasteboard, FILE_URL_TYPE))
        {
            if (MacOSWindowInterop.ResolveFileUrl(url) is (string, string path))
            {
                paths.Add(path);
            }
        }

        return paths.Count > 0 ? paths : MacOSWindowInterop.PasteboardFileNames(_pasteboard);
    }

    private List<string> ReadUris()
    {
        // Each item gives its file URL in path form, or else its URL as sent.
        var uris = new List<string>();
        foreach (var url in MacOSWindowInterop.PasteboardItemStrings(_pasteboard, FILE_URL_TYPE))
        {
            uris.Add(MacOSWindowInterop.ResolveFileUrl(url) is (string pathUrl, string) ? pathUrl : url);
        }

        if (uris.Count == 0)
        {
            foreach (var path in MacOSWindowInterop.PasteboardFileNames(_pasteboard))
            {
                uris.Add(new Uri(path).AbsoluteUri);
            }
        }

        foreach (var url in MacOSWindowInterop.PasteboardItemStrings(_pasteboard, URL_TYPE))
        {
            // A file item also offers its file URL here, already listed above in path form.
            bool isFile = url.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
            if (!isFile && !uris.Contains(url, StringComparer.Ordinal))
            {
                uris.Add(url);
            }
        }

        return uris;
    }
}
