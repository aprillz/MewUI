using System.Diagnostics;
using System.Text;

using Aprillz.MewUI.Platform.Linux.X11.DBus;

using Tmds.DBus.Protocol;

namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// File dialogs via the XDG Desktop Portal (<c>org.freedesktop.portal.FileChooser</c>) over DBus.
/// Preferred Linux native path (sandbox-friendly, honors the desktop's native chooser); falls back to
/// external tools when the portal is unavailable. Async portal calls are bridged to the synchronous
/// <see cref="IFileDialogService"/> via a nested event loop.
/// </summary>
internal sealed class XdgPortalFileDialogService : IFileDialogService
{
    private const string Destination = "org.freedesktop.portal.Desktop";
    private const string ObjectPath = "/org/freedesktop/portal/desktop";

    private const uint GlobStyle = 0u;

    private static bool? _available;
    private static uint _version;
    private static DBusConnection? _connection;

    /// <summary>
    /// Whether xdg-desktop-portal FileChooser is reachable. Cached after first probe.
    /// </summary>
    internal static bool IsAvailable()
    {
        if (_available.HasValue)
        {
            return _available.Value;
        }

        try
        {
            _version = NativeDialogHelper.PumpUntil(CheckVersionAsync());
            _available = _version >= 1;
        }
        catch
        {
            _available = false;
        }

        return _available.Value;
    }

    public string[]? OpenFile(OpenFileDialogOptions options)
    {
        using var modal = NativeDialogHelper.BeginOwnerModal(options.Owner);
        return NativeDialogHelper.PumpUntil(OpenFileAsync(options));
    }

    public string? SaveFile(SaveFileDialogOptions options)
    {
        using var modal = NativeDialogHelper.BeginOwnerModal(options.Owner);
        var result = NativeDialogHelper.PumpUntil(SaveFileAsync(options));
        return result is { Length: > 0 } ? result[0] : null;
    }

    public string? SelectFolder(FolderDialogOptions options)
    {
        using var modal = NativeDialogHelper.BeginOwnerModal(options.Owner);
        var result = NativeDialogHelper.PumpUntil(SelectFolderAsync(options));
        return result is { Length: > 0 } ? result[0] : null;
    }


    // A single explicit (non-autoconnect) session connection: the portal needs a stable UniqueName for the
    // request token and the Response signal subscription must share the same connection as the method call.
    // DBusConnection.Session is autoconnect and forbids UniqueName, so it cannot be used here.
    private static async Task<DBusConnection> GetConnectionAsync()
    {
        if (_connection is { } existing)
        {
            return existing;
        }

        var address = DBusAddress.Session
            ?? throw new InvalidOperationException("No DBus session bus address is available.");
        var connection = new DBusConnection(address);
        await connection.ConnectAsync();
        _connection = connection;
        return connection;
    }

    private static async Task<uint> CheckVersionAsync()
    {
        var connection = await GetConnectionAsync();
        var chooser = new FileChooser(connection, Destination, ObjectPath);
        return await chooser.GetVersionAsync();
    }

    private static async Task<string[]?> OpenFileAsync(OpenFileDialogOptions options)
    {
        var connection = await GetConnectionAsync();
        var chooser = new FileChooser(connection, Destination, ObjectPath);

        var chooserOptions = new Dictionary<string, VariantValue>();
        AddFilters(chooserOptions, options.Filter);
        AddCurrentFolder(chooserOptions, options.InitialDirectory);
        chooserOptions.Add("multiple", VariantValue.Bool(options.Multiselect));

        var uris = await CallAsync(connection, chooserOptions,
            (parent, title, opts) => chooser.OpenFileAsync(parent, title, opts),
            options.Owner, options.Title);

        return UrisToPaths(uris);
    }

    private static async Task<string[]?> SaveFileAsync(SaveFileDialogOptions options)
    {
        var connection = await GetConnectionAsync();
        var chooser = new FileChooser(connection, Destination, ObjectPath);

        var chooserOptions = new Dictionary<string, VariantValue>();
        AddFilters(chooserOptions, options.Filter);
        if (!string.IsNullOrEmpty(options.FileName))
        {
            chooserOptions.Add("current_name", VariantValue.String(options.FileName!));
        }
        AddCurrentFolder(chooserOptions, options.InitialDirectory);

        var uris = await CallAsync(connection, chooserOptions,
            (parent, title, opts) => chooser.SaveFileAsync(parent, title, opts),
            options.Owner, options.Title);

        var paths = UrisToPaths(uris);
        if (paths is not { Length: > 0 })
        {
            return paths;
        }

        // Portal does not apply a default extension; do it manually when the chosen name lacks one.
        if (!string.IsNullOrEmpty(options.DefaultExtension) && !Path.HasExtension(paths[0]))
        {
            paths[0] = paths[0] + "." + options.DefaultExtension!.TrimStart('.');
        }
        return paths;
    }

    private static async Task<string[]?> SelectFolderAsync(FolderDialogOptions options)
    {
        var connection = await GetConnectionAsync();
        var chooser = new FileChooser(connection, Destination, ObjectPath);

        var chooserOptions = new Dictionary<string, VariantValue>
        {
            { "directory", VariantValue.Bool(true) },
            { "multiple", VariantValue.Bool(false) },
        };
        AddCurrentFolder(chooserOptions, options.InitialDirectory);

        var uris = await CallAsync(connection, chooserOptions,
            (parent, title, opts) => chooser.OpenFileAsync(parent, title, opts),
            options.Owner, options.Title);

        var paths = UrisToPaths(uris);
        return paths?.Where(Directory.Exists).ToArray();
    }

    // Subscribe to the Request Response signal before invoking the portal (avoids a race), then await it.
    private static async Task<string[]?> CallAsync(
        DBusConnection connection,
        Dictionary<string, VariantValue> chooserOptions,
        Func<string, string, Dictionary<string, VariantValue>, Task<ObjectPath>> call,
        nint owner,
        string title)
    {
        var (expectedPath, token) = CreateRequestToken(connection);
        chooserOptions.Add("handle_token", VariantValue.String(token));
        chooserOptions.Add("modal", VariantValue.Bool(true));

        var parentWindow = owner != 0 ? "x11:" + owner.ToString("x") : string.Empty;

        var request = new Request(connection, Destination, expectedPath);
        var tcs = new TaskCompletionSource<string[]?>();
        using var subscription = await request.WatchResponseAsync(notification =>
        {
            if (notification.IsCompletion)
            {
                tcs.TrySetException(notification.Exception!);
            }
            else if (notification.Value.Results.TryGetValue("uris", out var uris))
            {
                tcs.TrySetResult(uris.GetArray<string>());
            }
            else
            {
                tcs.TrySetResult(null); // user cancelled
            }
        }, ObserverFlags.EmitAll);

        var actualPath = await call(parentWindow, title ?? string.Empty, chooserOptions);
        if (actualPath != expectedPath)
        {
            tcs.TrySetException(new InvalidOperationException(
                $"Portal returned unexpected request path '{actualPath}', expected '{expectedPath}'."));
        }

        return await tcs.Task;
    }

    private static void AddFilters(Dictionary<string, VariantValue> chooserOptions, string? win32Filter)
    {
        // Win32 filter string: "Name|*.a;*.b|Name2|*.c" -> portal a(sa(us)) glob filters.
        if (string.IsNullOrEmpty(win32Filter))
        {
            return;
        }

        var parts = win32Filter!.Split('|');
        var filters = new Array<Struct<string, Array<Struct<uint, string>>>>();
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            var name = parts[i];
            var patterns = parts[i + 1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patterns.Length == 0)
            {
                continue;
            }

            var globs = new Array<Struct<uint, string>>();
            foreach (var pattern in patterns)
            {
                globs.Add(Struct.Create(GlobStyle, pattern));
            }
            filters.Add(Struct.Create(name, globs));
        }

        chooserOptions.Add("filters", filters.AsVariantValue());
    }

    private static void AddCurrentFolder(Dictionary<string, VariantValue> chooserOptions, string? directory)
    {
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            chooserOptions.Add("current_folder", VariantValue.Array(Encoding.UTF8.GetBytes(directory + "\0")));
        }
    }

    private static string[]? UrisToPaths(string[]? uris)
    {
        if (uris is null)
        {
            return null;
        }
        return uris.Select(static uri => new Uri(uri).LocalPath).ToArray();
    }

    private static (ObjectPath ExpectedPath, string Token) CreateRequestToken(DBusConnection connection)
    {
        var sender = (connection.UniqueName ?? string.Empty).TrimStart(':').Replace('.', '_');
        var token = "mewui_" + Stopwatch.GetTimestamp().ToString();
        ObjectPath expectedPath = $"/org/freedesktop/portal/desktop/request/{sender}/{token}";
        return (expectedPath, token);
    }
}
