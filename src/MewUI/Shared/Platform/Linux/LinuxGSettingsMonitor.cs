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

    /// <summary>Starts watching the schema; returns false when the monitor process could not be started.</summary>
    public bool Start(Action onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);
        if (_process != null)
        {
            return true;
        }

        _onChange = onChange;
        _cts = new CancellationTokenSource();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sh",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // gsettings monitor exits only when a write fails, so it outlived every app that was killed
            // before a setting changed. The shell ends it once its stdin (our pipe) closes, which the
            // kernel also does when this process dies. Monitors all keys (color-scheme and gtk-theme).
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("gsettings monitor \"$1\" & child=$!; read -r _; kill \"$child\" 2>/dev/null");
            psi.ArgumentList.Add("gsettings-monitor");
            psi.ArgumentList.Add(_schema);

            var p = Process.Start(psi);
            if (p == null)
            {
                return false;
            }

            _process = p;

            // Read loop on a background thread (avoid blocking UI thread).
            _ = Task.Run(() => ReadLoop(p, _cts.Token));
            return true;
        }
        catch
        {
            Dispose();
            return false;
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
            try { p.StandardInput.Close(); } catch { }
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

