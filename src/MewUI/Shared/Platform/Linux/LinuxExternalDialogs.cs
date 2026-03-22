using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Platform.Linux;

internal static class LinuxExternalDialogs
{
    public static bool? ShowMessageBox(nint owner, string text, string caption, NativeMessageBoxButtons buttons, NativeMessageBoxIcon icon)
    {
        if (TryShowWithZenity(text, caption, buttons, icon, out var zenityResult))
        {
            return zenityResult;
        }

        if (TryShowWithKDialog(text, caption, buttons, icon, out var kdialogResult))
        {
            return kdialogResult;
        }

        throw new PlatformNotSupportedException("No supported Linux dialog tool found (zenity/kdialog).");
    }

    public static string[]? OpenFile(OpenFileDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (TryOpenFileWithZenity(options, out var zenity))
        {
            return zenity;
        }

        if (TryOpenFileWithKDialog(options, out var kdialog))
        {
            return kdialog;
        }

        throw new PlatformNotSupportedException("No supported Linux dialog tool found (zenity/kdialog).");
    }

    public static string? SaveFile(SaveFileDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (TrySaveFileWithZenity(options, out var zenity))
        {
            return zenity;
        }

        if (TrySaveFileWithKDialog(options, out var kdialog))
        {
            return kdialog;
        }

        throw new PlatformNotSupportedException("No supported Linux dialog tool found (zenity/kdialog).");
    }

    public static string? SelectFolder(FolderDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (TrySelectFolderWithZenity(options, out var zenity))
        {
            return zenity;
        }

        if (TrySelectFolderWithKDialog(options, out var kdialog))
        {
            return kdialog;
        }

        throw new PlatformNotSupportedException("No supported Linux dialog tool found (zenity/kdialog).");
    }

    private static bool TryShowWithZenity(string text, string caption, NativeMessageBoxButtons buttons, NativeMessageBoxIcon icon, out bool? result)
    {
        var args = new List<string>();

        if (buttons == NativeMessageBoxButtons.Ok)
        {
            args.Add(GetZenityIconVerb(icon));
            args.Add("--title");
            args.Add(caption);
            args.Add("--text");
            args.Add(text);

            if (!TryRunProcess("zenity", args, out int exitCode, out _))
            {
                result = default;
                return false;
            }

            result = true;
            return exitCode == 0;
        }

        if (buttons == NativeMessageBoxButtons.OkCancel)
        {
            args.Add("--question");
            args.Add("--title");
            args.Add(caption);
            args.Add("--text");
            args.Add(text);
            args.Add("--ok-label");
            args.Add(MewUIStrings.OK);
            args.Add("--cancel-label");
            args.Add(MewUIStrings.Cancel);

            if (!TryRunProcess("zenity", args, out int exitCode, out _))
            {
                result = default;
                return false;
            }

            result = exitCode == 0 ? true : false;
            return true;
        }

        if (buttons == NativeMessageBoxButtons.YesNo)
        {
            args.Add("--question");
            args.Add("--title");
            args.Add(caption);
            args.Add("--text");
            args.Add(text);
            args.Add("--ok-label");
            args.Add(MewUIStrings.Yes);
            args.Add("--cancel-label");
            args.Add(MewUIStrings.No);

            if (!TryRunProcess("zenity", args, out int exitCode, out _))
            {
                result = default;
                return false;
            }

            result = exitCode == 0 ? true : (bool?)null;
            return true;
        }

        // YesNoCancel: zenity has no 3-button dialog, emulate with a radiolist.
        args.Add("--list");
        args.Add("--radiolist");
        args.Add("--title");
        args.Add(caption);
        args.Add("--text");
        args.Add(text);
        args.Add("--column");
        args.Add(string.Empty);
        args.Add("--column");
        args.Add("Choice");
        args.Add("TRUE");
        args.Add(MewUIStrings.Yes);
        args.Add("FALSE");
        args.Add(MewUIStrings.No);
        args.Add("FALSE");
        args.Add(MewUIStrings.Cancel);

        if (!TryRunProcess("zenity", args, out int listExitCode, out var output))
        {
            result = default;
            return false;
        }

        if (listExitCode != 0)
        {
            result = false;
            return true;
        }

        var selection = (output ?? string.Empty).Trim();
        result = selection switch
        {
            _ when selection == MewUIStrings.Yes => true,
            _ when selection == MewUIStrings.No => (bool?)null,
            _ => false
        };
        return true;
    }

    private static string GetZenityIconVerb(NativeMessageBoxIcon icon)
    {
        return icon switch
        {
            NativeMessageBoxIcon.Warning => "--warning",
            NativeMessageBoxIcon.Error => "--error",
            _ => "--info"
        };
    }

    private static bool TryShowWithKDialog(string text, string caption, NativeMessageBoxButtons buttons, NativeMessageBoxIcon icon, out bool? result)
    {
        var args = new List<string>();
        args.Add("--title");
        args.Add(caption);

        string verb = GetKDialogVerb(buttons, icon);
        args.Add(verb);
        args.Add(text);

        if (!TryRunProcess("kdialog", args, out int exitCode, out _))
        {
            result = default;
            return false;
        }

        result = buttons switch
        {
            NativeMessageBoxButtons.Ok => true,
            NativeMessageBoxButtons.OkCancel => exitCode == 0 ? true : false,
            NativeMessageBoxButtons.YesNo => exitCode == 0 ? true : (bool?)null,
            NativeMessageBoxButtons.YesNoCancel => exitCode switch
            {
                0 => true,
                1 => (bool?)null,
                _ => false
            },
            _ => true
        };

        return true;
    }

    private static string GetKDialogVerb(NativeMessageBoxButtons buttons, NativeMessageBoxIcon icon)
    {
        return buttons switch
        {
            NativeMessageBoxButtons.Ok => icon switch
            {
                NativeMessageBoxIcon.Error => "--error",
                NativeMessageBoxIcon.Warning => "--sorry",
                _ => "--msgbox"
            },
            NativeMessageBoxButtons.OkCancel => "--warningcontinuecancel",
            NativeMessageBoxButtons.YesNo => "--yesno",
            NativeMessageBoxButtons.YesNoCancel => "--yesnocancel",
            _ => "--msgbox"
        };
    }

    private static bool TryOpenFileWithZenity(OpenFileDialogOptions options, out string[]? files)
    {
        var args = new List<string>
        {
            "--file-selection",
            "--title",
            options.Title ?? "Open"
        };

        if (!string.IsNullOrWhiteSpace(options.InitialDirectory))
        {
            args.Add("--filename");
            args.Add(EnsureTrailingSeparator(options.InitialDirectory!));
        }

        foreach (var filterArg in BuildZenityFilterArgs(options.Filter))
        {
            args.Add("--file-filter");
            args.Add(filterArg);
        }

        if (options.Multiselect)
        {
            args.Add("--multiple");
            args.Add("--separator");
            args.Add("\n");
        }

        if (!TryRunProcess("zenity", args, out int exitCode, out var output))
        {
            files = null;
            return false;
        }

        if (exitCode != 0)
        {
            files = null;
            return true;
        }

        files = SplitLines(output);
        return true;
    }

    private static bool TrySaveFileWithZenity(SaveFileDialogOptions options, out string? file)
    {
        var args = new List<string>
        {
            "--file-selection",
            "--save",
            "--title",
            options.Title ?? "Save"
        };

        if (options.OverwritePrompt)
        {
            args.Add("--confirm-overwrite");
        }

        var suggested = GetSuggestedPath(options.InitialDirectory, options.FileName);
        if (!string.IsNullOrWhiteSpace(suggested))
        {
            args.Add("--filename");
            args.Add(suggested!);
        }
        else if (!string.IsNullOrWhiteSpace(options.InitialDirectory))
        {
            args.Add("--filename");
            args.Add(EnsureTrailingSeparator(options.InitialDirectory!));
        }

        foreach (var filterArg in BuildZenityFilterArgs(options.Filter))
        {
            args.Add("--file-filter");
            args.Add(filterArg);
        }

        if (!TryRunProcess("zenity", args, out int exitCode, out var output))
        {
            file = null;
            return false;
        }

        if (exitCode != 0)
        {
            file = null;
            return true;
        }

        file = (output ?? string.Empty).Trim();
        file = file.Length == 0 ? null : file;
        return true;
    }

    private static bool TrySelectFolderWithZenity(FolderDialogOptions options, out string? folder)
    {
        var args = new List<string>
        {
            "--file-selection",
            "--directory",
            "--title",
            options.Title ?? "Select folder"
        };

        if (!string.IsNullOrWhiteSpace(options.InitialDirectory))
        {
            args.Add("--filename");
            args.Add(EnsureTrailingSeparator(options.InitialDirectory!));
        }

        if (!TryRunProcess("zenity", args, out int exitCode, out var output))
        {
            folder = null;
            return false;
        }

        if (exitCode != 0)
        {
            folder = null;
            return true;
        }

        folder = (output ?? string.Empty).Trim();
        folder = folder.Length == 0 ? null : folder;
        return true;
    }

    private static bool TryOpenFileWithKDialog(OpenFileDialogOptions options, out string[]? files)
    {
        var args = new List<string>();

        args.Add("--title");
        args.Add(options.Title ?? "Open");

        if (options.Multiselect)
        {
            args.Add("--getopenfilename");
            args.Add("--multiple");
            args.Add("--separate-output");
        }
        else
        {
            args.Add("--getopenfilename");
        }

        var start = options.InitialDirectory;
        if (!string.IsNullOrWhiteSpace(start))
        {
            args.Add(start!);
        }

        var filterString = BuildKDialogFilter(options.Filter);
        if (!string.IsNullOrWhiteSpace(filterString))
        {
            args.Add(filterString!);
        }

        if (!TryRunProcess("kdialog", args, out int exitCode, out var output))
        {
            files = null;
            return false;
        }

        if (exitCode != 0)
        {
            files = null;
            return true;
        }

        files = SplitLines(output);
        return true;
    }

    private static bool TrySaveFileWithKDialog(SaveFileDialogOptions options, out string? file)
    {
        var args = new List<string>();
        args.Add("--title");
        args.Add(options.Title ?? "Save");
        args.Add("--getsavefilename");

        var start = GetSuggestedPath(options.InitialDirectory, options.FileName) ?? options.InitialDirectory;
        if (!string.IsNullOrWhiteSpace(start))
        {
            args.Add(start!);
        }

        var filterString = BuildKDialogFilter(options.Filter);
        if (!string.IsNullOrWhiteSpace(filterString))
        {
            args.Add(filterString!);
        }

        if (!TryRunProcess("kdialog", args, out int exitCode, out var output))
        {
            file = null;
            return false;
        }

        if (exitCode != 0)
        {
            file = null;
            return true;
        }

        file = (output ?? string.Empty).Trim();
        file = file.Length == 0 ? null : file;
        return true;
    }

    private static bool TrySelectFolderWithKDialog(FolderDialogOptions options, out string? folder)
    {
        var args = new List<string>();
        args.Add("--title");
        args.Add(options.Title ?? "Select folder");
        args.Add("--getexistingdirectory");

        if (!string.IsNullOrWhiteSpace(options.InitialDirectory))
        {
            args.Add(options.InitialDirectory!);
        }

        if (!TryRunProcess("kdialog", args, out int exitCode, out var output))
        {
            folder = null;
            return false;
        }

        if (exitCode != 0)
        {
            folder = null;
            return true;
        }

        folder = (output ?? string.Empty).Trim();
        folder = folder.Length == 0 ? null : folder;
        return true;
    }

    private static IEnumerable<string> BuildZenityFilterArgs(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            yield break;
        }

        var parts = filter.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            yield break;
        }

        if (parts.Length % 2 == 1)
        {
            var token = parts[0].Trim();
            if (token.Length != 0)
            {
                yield return $"Files | {NormalizeFilterPattern(token)}";
            }
            yield break;
        }

        for (int i = 0; i < parts.Length; i += 2)
        {
            var desc = parts[i].Trim();
            var spec = parts[i + 1].Trim();
            if (desc.Length == 0 || spec.Length == 0)
            {
                continue;
            }

            yield return $"{desc} | {NormalizeFilterPattern(spec)}";
        }
    }

    private static string NormalizeFilterPattern(string pattern)
    {
        return pattern.Replace(';', ' ').Trim();
    }

    private static string? BuildKDialogFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return null;
        }

        var parts = filter.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        if (parts.Length % 2 == 1)
        {
            var token = parts[0].Trim();
            return token.Length == 0 ? null : token;
        }

        // Format: "*.txt|Text files (*.txt)\n*|All Files"
        var lines = new List<string>();
        for (int i = 0; i < parts.Length; i += 2)
        {
            var desc = parts[i].Trim();
            var spec = parts[i + 1].Trim();
            if (desc.Length == 0 || spec.Length == 0)
            {
                continue;
            }

            lines.Add($"{spec}|{desc}");
        }

        return lines.Count == 0 ? null : string.Join('\n', lines);
    }

    private static string EnsureTrailingSeparator(string directory)
    {
        if (directory.EndsWith("/", StringComparison.Ordinal) || directory.EndsWith("\\", StringComparison.Ordinal))
        {
            return directory;
        }

        return directory + "/";
    }

    private static string? GetSuggestedPath(string? initialDirectory, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (Path.IsPathRooted(fileName))
        {
            return fileName;
        }

        if (string.IsNullOrWhiteSpace(initialDirectory))
        {
            return fileName;
        }

        return Path.Combine(initialDirectory, fileName);
    }

    private static string[]? SplitLines(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool TryRunProcess(string fileName, List<string> args, out int exitCode, out string? stdout)
    {
        exitCode = -1;
        stdout = null;

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            for (int i = 0; i < args.Count; i++)
            {
                process.StartInfo.ArgumentList.Add(args[i]);
            }

            if (!process.Start())
            {
                return false;
            }

            stdout = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(milliseconds: 5 * 60 * 1000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            exitCode = process.ExitCode;
            return true;
        }
        catch (Exception ex) when (IsToolMissing(ex))
        {
            return false;
        }
    }

    private static bool IsToolMissing(Exception ex)
    {
        if (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }

        if (ex is Win32Exception)
        {
            return true;
        }

        if (ex is ExternalException)
        {
            return true;
        }

        return false;
    }
}
