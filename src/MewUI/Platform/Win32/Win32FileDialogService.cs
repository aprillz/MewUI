using System.Runtime.InteropServices;

using Aprillz.MewUI.Core;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Platform.Win32;

internal sealed partial class Win32FileDialogService : IFileDialogService
{
    public string[]? OpenFile(OpenFileDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        const uint flagsBase = OFN_EXPLORER | OFN_NOCHANGEDIR | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY | OFN_FILEMUSTEXIST;
        uint flags = flagsBase;
        if (options.Multiselect)
        {
            flags |= OFN_ALLOWMULTISELECT;
        }

        return ShowOpenOrSave(
            isSave: false,
            owner: options.Owner,
            title: options.Title,
            initialDirectory: options.InitialDirectory,
            filter: options.Filter,
            defaultExtension: null,
            fileName: null,
            flags: flags,
            out var selected)
            ? selected
            : null;
    }

    public string? SaveFile(SaveFileDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        uint flags = OFN_EXPLORER | OFN_NOCHANGEDIR | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY;
        if (options.OverwritePrompt)
        {
            flags |= OFN_OVERWRITEPROMPT;
        }

        if (!ShowOpenOrSave(
                isSave: true,
                owner: options.Owner,
                title: options.Title,
                initialDirectory: options.InitialDirectory,
                filter: options.Filter,
                defaultExtension: options.DefaultExtension,
                fileName: options.FileName,
                flags: flags,
                out var selected))
        {
            return null;
        }

        return selected is { Length: > 0 } ? selected[0] : null;
    }

    public string? SelectFolder(FolderDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // SHBrowseForFolder can hang when invoked from WndProc-driven callbacks (which can happen in this UI stack).
        // Run it on an STA thread, and do not pass a cross-thread owner HWND (it can also hang).
        var staOptions = new FolderDialogOptions
        {
            Owner = 0,
            Title = options.Title,
            InitialDirectory = options.InitialDirectory
        };

        Action? pump = Application.IsRunning ? Application.DoEvents : null;
        return StaHelper.Run(() => SelectFolderCore(staOptions), pump);
    }

    private static string? SelectFolderCore(FolderDialogOptions options)
    {
        const uint BIF_RETURNONLYFSDIRS = 0x0001;
        const uint BIF_NEWDIALOGSTYLE = 0x0040;
        const uint BIF_EDITBOX = 0x0010;
        const uint BIF_VALIDATE = 0x0020;

        const uint BFFM_INITIALIZED = 1;
        const uint BFFM_SETSELECTIONW = 0x467;

        string title = options.Title ?? string.Empty;
        string? initialDirectory = options.InitialDirectory;

        char[] displayName = new char[260];
        char[] selectedPath = new char[260];

        BrowseCallbackProc? callback = null;
        callback = (hwnd, uMsg, lParam, lpData) =>
        {
            if (uMsg == BFFM_INITIALIZED && lpData != 0)
            {
                User32.SendMessage(hwnd, BFFM_SETSELECTIONW, new nint(1), lpData);
            }
            return 0;
        };

        var callbackPtr = Marshal.GetFunctionPointerForDelegate(callback);

        int hr = Ole32.CoInitializeEx(0, Ole32.COINIT_APARTMENTTHREADED);
        bool uninitialize = hr is >= 0 and not Ole32.RPC_E_CHANGED_MODE;

        unsafe
        {
            fixed (char* pTitle = title)
            fixed (char* pDisplayName = displayName)
            fixed (char* pSelectedPath = selectedPath)
            fixed (char* pInitialDirectory = initialDirectory)
            {
                var bi = new BROWSEINFOW
                {
                    hwndOwner = options.Owner,
                    pidlRoot = 0,
                    pszDisplayName = (nint)pDisplayName,
                    lpszTitle = (nint)pTitle,
                    ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE | BIF_EDITBOX | BIF_VALIDATE,
                    lpfn = callbackPtr,
                    lParam = initialDirectory != null ? (nint)pInitialDirectory : 0,
                    iImage = 0
                };

                nint pidl = Shell32.SHBrowseForFolder(ref bi);
                if (pidl == 0)
                {
                    if (uninitialize)
                    {
                        Ole32.CoUninitialize();
                    }
                    return null;
                }

                try
                {
                    if (!Shell32.SHGetPathFromIDList(pidl, (nint)pSelectedPath))
                    {
                        return null;
                    }

                    var path = new string(pSelectedPath);
                    path = path.TrimEnd('\0');
                    return string.IsNullOrWhiteSpace(path) ? null : path;
                }
                finally
                {
                    Ole32.CoTaskMemFree(pidl);
                    if (uninitialize)
                    {
                        Ole32.CoUninitialize();
                    }
                }
            }
        }
    }

    private static bool ShowOpenOrSave(
        bool isSave,
        nint owner,
        string title,
        string? initialDirectory,
        string? filter,
        string? defaultExtension,
        string? fileName,
        uint flags,
        out string[] selected)
    {
        selected = [];

        title ??= string.Empty;
        initialDirectory ??= null;
        defaultExtension ??= null;
        fileName ??= null;

        string? winFilter = BuildWin32Filter(filter);
        char[] fileBuffer = new char[65536];
        if (!string.IsNullOrEmpty(fileName))
        {
            WriteStringToBuffer(fileName, fileBuffer);
        }

        unsafe
        {
            fixed (char* pFile = fileBuffer)
            fixed (char* pTitle = title)
            fixed (char* pInitialDirectory = initialDirectory)
            fixed (char* pDefExt = defaultExtension)
            fixed (char* pFilter = winFilter)
            {
                var ofn = new OPENFILENAMEW
                {
                    lStructSize = (uint)Marshal.SizeOf<OPENFILENAMEW>(),
                    hwndOwner = owner,
                    hInstance = 0,
                    lpstrFilter = winFilter != null ? (nint)pFilter : 0,
                    lpstrCustomFilter = 0,
                    nMaxCustFilter = 0,
                    nFilterIndex = 1,
                    lpstrFile = (nint)pFile,
                    nMaxFile = (uint)fileBuffer.Length,
                    lpstrFileTitle = 0,
                    nMaxFileTitle = 0,
                    lpstrInitialDir = initialDirectory != null ? (nint)pInitialDirectory : 0,
                    lpstrTitle = (nint)pTitle,
                    Flags = flags,
                    nFileOffset = 0,
                    nFileExtension = 0,
                    lpstrDefExt = defaultExtension != null ? (nint)pDefExt : 0,
                    lCustData = 0,
                    lpfnHook = 0,
                    lpTemplateName = 0,
                    pvReserved = 0,
                    dwReserved = 0,
                    FlagsEx = 0
                };

                bool ok = isSave ? Comdlg32.GetSaveFileName(ref ofn) : Comdlg32.GetOpenFileName(ref ofn);
                if (!ok)
                {
                    uint error = Comdlg32.CommDlgExtendedError();
                    if (error == 0)
                    {
                        return false;
                    }

                    throw new InvalidOperationException($"{(isSave ? "GetSaveFileNameW" : "GetOpenFileNameW")} failed. Error: 0x{error:X}");
                }
            }
        }

        selected = ParseSelectedFiles(fileBuffer);
        return selected.Length != 0;
    }

    private static string? BuildWin32Filter(string? filter)
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
            // Treat a single token as both description and pattern.
            string token = parts[0].Trim();
            if (token.Length == 0)
            {
                return null;
            }

            return token + '\0' + token + "\0\0";
        }

        var result = new System.Text.StringBuilder(filter.Length + 16);
        for (int i = 0; i < parts.Length; i += 2)
        {
            string desc = parts[i].Trim();
            string spec = parts[i + 1].Trim();
            if (desc.Length == 0 || spec.Length == 0)
            {
                continue;
            }

            result.Append(desc);
            result.Append('\0');
            result.Append(spec);
            result.Append('\0');
        }

        result.Append('\0');
        return result.ToString();
    }

    private static void WriteStringToBuffer(string value, char[] buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        int length = Math.Min(value.Length, buffer.Length - 1);
        value.AsSpan(0, length).CopyTo(buffer);
        buffer[length] = '\0';
    }

    private static string[] ParseSelectedFiles(char[] buffer)
    {
        int offset = 0;
        string first = ReadNullTerminated(buffer, ref offset);
        if (first.Length == 0)
        {
            return [];
        }

        string second = ReadNullTerminated(buffer, ref offset);
        if (second.Length == 0)
        {
            return [first];
        }

        var dir = first;
        var files = new List<string> { Path.Combine(dir, second) };
        while (true)
        {
            string next = ReadNullTerminated(buffer, ref offset);
            if (next.Length == 0)
            {
                break;
            }

            files.Add(Path.Combine(dir, next));
        }

        return files.ToArray();
    }

    private static string ReadNullTerminated(char[] buffer, ref int offset)
    {
        int start = offset;
        while (offset < buffer.Length && buffer[offset] != '\0')
        {
            offset++;
        }

        int length = offset - start;
        if (offset < buffer.Length && buffer[offset] == '\0')
        {
            offset++;
        }

        return length == 0 ? string.Empty : new string(buffer, start, length);
    }

    // OPENFILENAME flags
    private const uint OFN_READONLY = 0x00000001;
    private const uint OFN_OVERWRITEPROMPT = 0x00000002;
    private const uint OFN_HIDEREADONLY = 0x00000004;
    private const uint OFN_NOCHANGEDIR = 0x00000008;
    private const uint OFN_PATHMUSTEXIST = 0x00000800;
    private const uint OFN_FILEMUSTEXIST = 0x00001000;
    private const uint OFN_ALLOWMULTISELECT = 0x00000200;
    private const uint OFN_EXPLORER = 0x00080000;

    [StructLayout(LayoutKind.Sequential)]
    private struct OPENFILENAMEW
    {
        public uint lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public nint lpstrFilter;
        public nint lpstrCustomFilter;
        public uint nMaxCustFilter;
        public uint nFilterIndex;
        public nint lpstrFile;
        public uint nMaxFile;
        public nint lpstrFileTitle;
        public uint nMaxFileTitle;
        public nint lpstrInitialDir;
        public nint lpstrTitle;
        public uint Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public nint lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public nint lpTemplateName;
        public nint pvReserved;
        public uint dwReserved;
        public uint FlagsEx;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BROWSEINFOW
    {
        public nint hwndOwner;
        public nint pidlRoot;
        public nint pszDisplayName;
        public nint lpszTitle;
        public uint ulFlags;
        public nint lpfn;
        public nint lParam;
        public int iImage;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BrowseCallbackProc(nint hwnd, uint uMsg, nint lParam, nint lpData);

    private static partial class Comdlg32
    {
        private const string LibraryName = "comdlg32.dll";

        [LibraryImport(LibraryName, EntryPoint = "GetOpenFileNameW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetOpenFileName(ref OPENFILENAMEW ofn);

        [LibraryImport(LibraryName, EntryPoint = "GetSaveFileNameW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetSaveFileName(ref OPENFILENAMEW ofn);

        [LibraryImport(LibraryName)]
        public static partial uint CommDlgExtendedError();
    }

    private static partial class Shell32
    {
        private const string LibraryName = "shell32.dll";

        [LibraryImport(LibraryName, EntryPoint = "SHBrowseForFolderW", SetLastError = true)]
        public static partial nint SHBrowseForFolder(ref BROWSEINFOW bi);

        [LibraryImport(LibraryName, EntryPoint = "SHGetPathFromIDListW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SHGetPathFromIDList(nint pidl, nint pszPath);
    }

    private static partial class Ole32
    {
        private const string LibraryName = "ole32.dll";

        public const uint COINIT_APARTMENTTHREADED = 0x2;
        public const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

        [LibraryImport(LibraryName)]
        public static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

        [LibraryImport(LibraryName)]
        public static partial void CoUninitialize();

        [LibraryImport(LibraryName)]
        public static partial void CoTaskMemFree(nint pv);
    }
}
