using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Aprillz.MewUI.TaskManager.Sample;

internal static class PrivilegeService
{
    public static bool IsElevated => OperatingSystem.IsWindows()
        ? IsWindowsElevated()
        : geteuid() == 0;

    public static void RestartElevated()
    {
        if (IsElevated || Environment.ProcessPath is not { Length: > 0 } executable) return;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                });
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "pkexec",
                    ArgumentList = { executable },
                    UseShellExecute = false,
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                var shellCommand = $"'{executable.Replace("'", "'\\''", StringComparison.Ordinal)}'";
                var escaped = shellCommand
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/bin/osascript",
                    ArgumentList = { "-e", $"do shell script \"{escaped}\" with administrator privileges" },
                    UseShellExecute = false,
                });
            }
        }
        catch
        {
            // Cancellation or a missing platform elevation provider leaves the current process running.
        }
    }

    private static bool IsWindowsElevated()
    {
#pragma warning disable CA1416
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
#pragma warning restore CA1416
    }

    [DllImport("libc")]
    private static extern uint geteuid();
}
