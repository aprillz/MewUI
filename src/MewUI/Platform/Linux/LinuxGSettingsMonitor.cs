using System.Diagnostics;

namespace Aprillz.MewUI.Platform.Linux;

internal sealed class LinuxGSettingsMonitor : IDisposable
{
    private readonly string _schema;
    private Process? _process;
    private CancellationTokenSource? _cts;
    private Action? _onChange;

    public LinuxGSettingsMonitor(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        _schema = schema;
    }

    public void Start(Action onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);
        if (_process != null)
        {
            return;
        }

        _onChange = onChange;
        _cts = new CancellationTokenSource();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gsettings",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Monitor all keys in schema (includes color-scheme and gtk-theme).
            psi.ArgumentList.Add("monitor");
            psi.ArgumentList.Add(_schema);

            var p = Process.Start(psi);
            if (p == null)
            {
                return;
            }

            _process = p;

            // Read loop on a background thread (avoid blocking UI thread).
            _ = Task.Run(() => ReadLoop(p, _cts.Token));
        }
        catch
        {
            Dispose();
        }
    }

    private async Task ReadLoop(Process p, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && !p.HasExited)
            {
                var line = await p.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                // Example: "color-scheme: 'prefer-dark'" or "gtk-theme: 'Adwaita-dark'".
                if (line.StartsWith("color-scheme:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("gtk-theme:", StringComparison.OrdinalIgnoreCase))
                {
                    _onChange?.Invoke();
                }
            }
        }
        catch
        {
            // ignore; best-effort monitor
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;

        var p = _process;
        _process = null;
        if (p != null)
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                }
            }
            catch { }

            try { p.Dispose(); } catch { }
        }
    }
}

