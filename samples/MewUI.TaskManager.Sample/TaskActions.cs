using System.Diagnostics;
using System.Text;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.TaskManager.Sample;

/// <summary>What the page buttons and menus do to processes and the system.</summary>
internal static class TaskActions
{
    /// <summary>Ends a process the way Task Manager's End task does: at once, without asking.</summary>
    public static async Task EndProcessAsync(ProcessNode node, Window? owner)
    {
        try
        {
            using var process = Process.GetProcessById(node.ProcessId);
            process.Kill();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // The process already exited.
        }
        catch (Exception exception)
        {
            await MessageBox.NotifyAsync(
                $"Unable to end {node.Name} (PID {node.ProcessId}).",
                PromptIconKind.Error,
                exception.Message + (PrivilegeService.IsElevated ? string.Empty : "\nIt may belong to another user; restart with elevated access from Settings."),
                owner);
        }
    }

    /// <summary>Shows an executable in the platform's file manager, selected where the platform allows it.</summary>
    public static void OpenFileLocation(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + path } });
            else if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", path } });
            else
                Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { Path.GetDirectoryName(path) ?? "/" } });
        }
        catch { }
    }

    /// <summary>The platform's own monitor: Resource Monitor, Activity Monitor, or the desktop's system monitor.</summary>
    public static void OpenSystemMonitor()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("perfmon.exe") { ArgumentList = { "/res" }, UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-a", "Activity Monitor" } });
            else
            {
                foreach (var monitor in new[] { "gnome-system-monitor", "plasma-systemmonitor", "ksysguard", "xfce4-taskmanager" })
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(monitor) { UseShellExecute = false });
                        return;
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public static string Describe(ProcessNode node)
    {
        var text = new StringBuilder();
        text.AppendLine($"Name\t{node.Name}");
        text.AppendLine($"PID\t{node.ProcessId}");
        text.AppendLine($"CPU\t{node.CpuPercent:0.0}%");
        text.AppendLine($"Memory\t{Format.Bytes(node.MemoryBytes)}");
        text.AppendLine($"Disk\t{Format.ByteRate(node.DiskBytesPerSecond)}");
        if (node.ExecutablePath != null) text.AppendLine($"Path\t{node.ExecutablePath}");
        return text.ToString();
    }

    public static void Copy(string text) => Application.Current.PlatformServices.Clipboard?.TrySetText(text);

    /// <summary>
    /// Starts what the user typed, as the Run box of each platform does: the shell's open on Windows (a
    /// program, a document or a URL), and the user's shell elsewhere.
    /// </summary>
    public static string? Run(string command, bool elevated)
    {
        command = command.Trim();
        if (command.Length == 0) return null;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var (file, arguments) = SplitWindowsCommand(command);
                Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true, Verb = elevated ? "runas" : string.Empty });
            }
            else
            {
                string shell = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } userShell ? userShell : "/bin/sh";
                var start = elevated && OperatingSystem.IsLinux()
                    ? new ProcessStartInfo("pkexec") { ArgumentList = { shell, "-c", command } }
                    : new ProcessStartInfo(shell) { ArgumentList = { "-c", command } };
                start.UseShellExecute = false;
                Process.Start(start);
            }
            return null;
        }
        catch (Exception exception)
        {
            return exception.Message;
        }
    }

    private static (string File, string Arguments) SplitWindowsCommand(string command)
    {
        if (command.StartsWith('"'))
        {
            int close = command.IndexOf('"', 1);
            if (close > 0) return (command[1..close], command[(close + 1)..].Trim());
        }
        // A path with spaces but no quotes runs as typed when it names a file.
        if (File.Exists(command) || Directory.Exists(command)) return (command, string.Empty);
        int space = command.IndexOf(' ');
        return space < 0 ? (command, string.Empty) : (command[..space], command[(space + 1)..].Trim());
    }
}

/// <summary>The Run new task box: a command, optionally started with administrative rights.</summary>
internal sealed class RunTaskWindow : Window
{
    private readonly TextBox _command = new TextBox().Placeholder(OperatingSystem.IsWindows() ? "Program, folder, document or website" : "Command");
    private readonly CheckBox _elevated = new CheckBox().Content("Create this task with administrative privileges");
    private readonly ObservableValue<string> _error = new(string.Empty);

    public RunTaskWindow()
    {
        Title = "Create new task";
        Padding = new Thickness(20);
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.FitContentSize(460, 220);
        PreviewKeyDown += e =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Start();
            }
        };
        _elevated.IsVisible = OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

        Content = new StackPanel()
            .Vertical()
            .Spacing(12)
            .Children(
                new TextBlock()
                    .TextWrapping(TextWrapping.Wrap)
                    .Text(OperatingSystem.IsWindows()
                        ? "Type the name of a program, folder, document, or Internet resource, and it will be opened for you."
                        : "Type a command; it runs in your login shell."),
                _command,
                _elevated,
                new TextBlock()
                    .BindText(_error)
                    .TextWrapping(TextWrapping.Wrap)
                    .WithTheme((theme, text) => text.Foreground(Color.FromRgb(196, 43, 28))),
                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Right()
                    .Children(
                        new Button().MinWidth(80).Content("OK").OnClick(Start),
                        new Button().MinWidth(80).Content("Cancel").OnClick(Close)));
        Loaded += () => FocusManager.SetFocus(_command);
    }

    private void Start()
    {
        string? error = TaskActions.Run(_command.Text, _elevated.IsChecked == true);
        if (error == null)
        {
            Close();
            return;
        }
        _error.Value = error;
    }
}
