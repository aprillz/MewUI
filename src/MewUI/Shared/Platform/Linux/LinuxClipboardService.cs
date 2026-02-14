using System.Diagnostics;

namespace Aprillz.MewUI.Platform.Linux;

internal sealed class LinuxClipboardService : IClipboardService
{
    private static readonly object _lock = new();
    private static ClipboardBackend? _backend;
    private static Process? _ownerProcess;
    private static string _localText = string.Empty;

    public bool TrySetText(string text)
    {
        var backend = GetBackend();
        text ??= string.Empty;

        bool ok = backend.Kind != ClipboardBackendKind.None && backend.TrySetText(text);
        lock (_lock)
        {
            // Always keep an in-process fallback so copy/paste works even when no external
            // clipboard helpers are installed (or when no clipboard manager exists).
            _localText = text;
        }

        return ok || text.Length > 0;
    }

    public bool TryGetText(out string text)
    {
        var backend = GetBackend();
        if (backend.Kind != ClipboardBackendKind.None && backend.TryGetText(out text))
        {
            lock (_lock)
            {
                _localText = text ?? string.Empty;
            }

            return true;
        }

        lock (_lock)
        {
            text = _localText;
        }

        return !string.IsNullOrEmpty(text);
    }

    private static ClipboardBackend GetBackend()
    {
        lock (_lock)
        {
            if (_backend != null)
            {
                return _backend;
            }

            // Prefer Wayland tooling when available; fall back to X11.
            // This keeps the runtime dependency surface small (external tools only when present).
            _backend =
                TryCreateWaylandBackend()
                ?? TryCreateX11Backend()
                ?? ClipboardBackend.None();
            return _backend;
        }
    }

    private static ClipboardBackend? TryCreateWaylandBackend()
    {
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        if (string.IsNullOrWhiteSpace(waylandDisplay))
        {
            return null;
        }

        if (ProcessHelpers.CanStart("wl-copy") && ProcessHelpers.CanStart("wl-paste"))
        {
            return ClipboardBackend.External(
                ClipboardBackendKind.WaylandWlClipboard,
                setCommand: "wl-copy",
                setArguments: string.Empty,
                getCommand: "wl-paste",
                getArguments: "--no-newline");
        }

        return null;
    }

    private static ClipboardBackend? TryCreateX11Backend()
    {
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrWhiteSpace(display))
        {
            return null;
        }

        if (ProcessHelpers.CanStart("xclip"))
        {
            return ClipboardBackend.External(
                ClipboardBackendKind.X11Xclip,
                setCommand: "xclip",
                // Keep the selection owner alive for minimal X11 environments (no clipboard manager).
                // xclip will remain until killed or until it has served one paste loop.
                setArguments: "-selection clipboard -in -loops 1",
                getCommand: "xclip",
                getArguments: "-selection clipboard -out");
        }

        if (ProcessHelpers.CanStart("xsel"))
        {
            return ClipboardBackend.External(
                ClipboardBackendKind.X11Xsel,
                setCommand: "xsel",
                setArguments: "--clipboard --input",
                getCommand: "xsel",
                getArguments: "--clipboard --output");
        }

        return null;
    }

    private enum ClipboardBackendKind
    {
        None = 0,
        WaylandWlClipboard = 1,
        X11Xclip = 2,
        X11Xsel = 3,
    }

    private sealed class ClipboardBackend
    {
        private const int TimeoutMs = 1500;

        private readonly string _setCommand;
        private readonly string _setArguments;
        private readonly string _getCommand;
        private readonly string _getArguments;

        public ClipboardBackendKind Kind { get; }

        private ClipboardBackend(
            ClipboardBackendKind kind,
            string setCommand,
            string setArguments,
            string getCommand,
            string getArguments)
        {
            Kind = kind;
            _setCommand = setCommand;
            _setArguments = setArguments;
            _getCommand = getCommand;
            _getArguments = getArguments;
        }

        public static ClipboardBackend None()
            => new(ClipboardBackendKind.None, string.Empty, string.Empty, string.Empty, string.Empty);

        public static ClipboardBackend External(
            ClipboardBackendKind kind,
            string setCommand,
            string setArguments,
            string getCommand,
            string getArguments)
            => new(kind, setCommand, setArguments, getCommand, getArguments);

        public bool TrySetText(string text)
        {
            // NOTE:
            // On X11 the clipboard is selection-based; if the "owner" exits immediately,
            // clipboard content often disappears unless a clipboard manager is running.
            // Similarly, wl-copy stays alive until a paste happens.
            // To make copy/paste reliable in minimal environments, we keep the owner process alive.
            // We replace the previous owner on every Set.
            if (Kind is ClipboardBackendKind.X11Xclip or ClipboardBackendKind.WaylandWlClipboard)
            {
                return TrySetTextWithOwnerProcess(text);
            }

            // For other helpers, do a best-effort synchronous write.
            return TrySetTextOneShot(text);
        }

        private bool TrySetTextWithOwnerProcess(string text)
        {
            try
            {
                lock (_lock)
                {
                    if (_ownerProcess is { HasExited: false })
                    {
                        TryKill(_ownerProcess);
                        _ownerProcess.Dispose();
                    }

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = _setCommand,
                            Arguments = _setArguments,
                            RedirectStandardInput = true,
                            RedirectStandardOutput = false,
                            RedirectStandardError = false,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        },
                        EnableRaisingEvents = true,
                    };

                    if (!process.Start())
                    {
                        process.Dispose();
                        return false;
                    }

                    // Write the content and close stdin. The process keeps owning the clipboard.
                    process.StandardInput.Write(text);
                    process.StandardInput.Close();

                    // If it exits immediately, treat it as failure (no owner kept alive).
                    if (process.WaitForExit(50))
                    {
                        bool ok = process.ExitCode == 0;
                        process.Dispose();
                        return ok;
                    }

                    _ownerProcess = process;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool TrySetTextOneShot(string text)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _setCommand,
                        Arguments = _setArguments,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };

                if (!process.Start())
                {
                    return false;
                }

                process.StandardInput.Write(text);
                process.StandardInput.Close();

                if (!process.WaitForExit(TimeoutMs))
                {
                    TryKill(process);
                    return false;
                }

                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public bool TryGetText(out string text)
        {
            text = string.Empty;

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _getCommand,
                        Arguments = _getArguments,
                        RedirectStandardInput = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = false,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };

                if (!process.Start())
                {
                    return false;
                }

                string output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(TimeoutMs))
                {
                    TryKill(process);
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    return false;
                }

                text = output ?? string.Empty;
                return true;
            }
            catch
            {
                text = string.Empty;
                return false;
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }
    }

    private static class ProcessHelpers
    {
        public static bool CanStart(string fileName)
        {
            try
            {
                // On Linux, PATH lookup happens in Process.Start itself; this probes availability cheaply.
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };

                if (!process.Start())
                {
                    return false;
                }

                process.WaitForExit(400);
                return true;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return false;
            }
            catch
            {
                // If the process exists but errors (no display etc.), we still treat it as "available".
                return true;
            }
        }
    }
}
