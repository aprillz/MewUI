using System.Text;

namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// The data of one XDND drag: every type the source offers, listed by its atom name, plus the standard formats derived
/// from <c>text/uri-list</c> and the text types. Values are converted from the source's selection on request.
/// </summary>
internal sealed class X11DropDataObject : PlatformDataObject
{
    private const string URI_LIST_FORMAT = "text/uri-list";

    // Text types in order of preference; the last one is Latin-1, the others UTF-8.
    private static readonly string[] _textFormats = ["text/plain;charset=utf-8", "UTF8_STRING", "text/plain", "STRING"];

    private readonly Dictionary<string, nint> _offered = new(StringComparer.Ordinal);
    private readonly Func<nint, byte[]?> _convert;

    /// <param name="offered">The offered types by atom name, in the source's order.</param>
    /// <param name="convert">Converts the drag selection to a type and returns the bytes, or null when it fails.</param>
    public X11DropDataObject(IReadOnlyList<(string Name, nint Atom)> offered, Func<nint, byte[]?> convert)
    {
        _convert = convert;
        foreach (var (name, atom) in offered)
        {
            _offered.TryAdd(name, atom);
        }

        if (_offered.ContainsKey(URI_LIST_FORMAT))
        {
            AddFormat(StandardDataFormats.StorageItems);
            AddFormat(StandardDataFormats.Uris);
        }

        if (_textFormats.Any(_offered.ContainsKey))
        {
            AddFormat(StandardDataFormats.Text);
        }

        foreach (var (name, _) in offered)
        {
            AddFormat(name);
        }
    }

    protected override object? Read(string format)
    {
        switch (format)
        {
            case StandardDataFormats.StorageItems:
                return ReadUris() is List<string> uris ? LocalPaths(uris) : null;

            case StandardDataFormats.Uris:
                return ReadUris();

            case StandardDataFormats.Text:
                return ReadText();

            default:
                return _offered.TryGetValue(format, out var atom) ? _convert(atom) : null;
        }
    }

    /// <summary>The list's URIs as sent, without comment lines; CRLF, LF and CR line ends are all accepted.</summary>
    private List<string>? ReadUris()
    {
        if (GetData(URI_LIST_FORMAT) is not byte[] bytes)
        {
            return null;
        }

        var uris = new List<string>();
        foreach (var line in Encoding.UTF8.GetString(bytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Length > 0 && line[0] != '#')
            {
                uris.Add(line);
            }
        }

        return uris;
    }

    private static List<string> LocalPaths(List<string> uris)
    {
        var paths = new List<string>(uris.Count);
        foreach (var text in uris)
        {
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.IsFile && !string.IsNullOrWhiteSpace(uri.LocalPath))
            {
                paths.Add(uri.LocalPath);
            }
        }

        return paths;
    }

    private string? ReadText()
    {
        foreach (var format in _textFormats)
        {
            if (_offered.ContainsKey(format) && GetData(format) is byte[] bytes)
            {
                var encoding = format == "STRING" ? Encoding.Latin1 : Encoding.UTF8;
                return encoding.GetString(bytes).TrimEnd('\0');
            }
        }

        return null;
    }
}
