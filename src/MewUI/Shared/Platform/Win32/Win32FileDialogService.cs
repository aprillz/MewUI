using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Aprillz.MewUI.Platform.Win32;

[SupportedOSPlatform("windows")]
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

        // Run on an STA thread to avoid MTA/UI-thread hangs with COM-based dialogs.
        Action? pump = Application.IsRunning ? Application.DoEvents : null;
        return StaHelper.Run(() => SelectFolderCore(options), pump);
    }

    private static string? SelectFolderCore(FolderDialogOptions options)
    {
        string title = options.Title ?? "Select folder";
        string? initialDirectory = options.InitialDirectory;

        int hr = Ole32.CoInitializeEx(0, Ole32.COINIT_APARTMENTTHREADED);
        bool uninitialize = hr is >= 0 and not Ole32.RPC_E_CHANGED_MODE;

        IFileOpenDialog? dialog = null;
        IShellItem? folderItem = null;
        IShellItem? resultItem = null;
        try
        {
            var iidFileOpenDialog = typeof(IFileOpenDialog).GUID;
            var clsidFileOpenDialog = CLSID.FileOpenDialog;
            int hrCreate = Ole32.CoCreateInstance(
                ref clsidFileOpenDialog,
                0,
                Ole32.CLSCTX_INPROC_SERVER,
                ref iidFileOpenDialog,
                out var dialogPtr);
            if (hrCreate < 0 || dialogPtr == 0)
            {
                Marshal.ThrowExceptionForHR(hrCreate);
            }

            dialog = (IFileOpenDialog)Marshal.GetTypedObjectForIUnknown(dialogPtr, typeof(IFileOpenDialog));
            Marshal.Release(dialogPtr);
            dialog.GetOptions(out uint currentOptions);
            uint optionsFlags = currentOptions
                | (uint)FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS
                | (uint)FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM
                | (uint)FILEOPENDIALOGOPTIONS.FOS_PATHMUSTEXIST
                | (uint)FILEOPENDIALOGOPTIONS.FOS_NOCHANGEDIR;
            dialog.SetOptions(optionsFlags);
            dialog.SetTitle(title);

            if (!string.IsNullOrWhiteSpace(initialDirectory))
            {
                Guid shellItemGuid = typeof(IShellItem).GUID;
                if (Shell32.SHCreateItemFromParsingName(initialDirectory!, 0, ref shellItemGuid, out var itemPtr) == 0 && itemPtr != 0)
                {
                    try
                    {
                        folderItem = (IShellItem)Marshal.GetTypedObjectForIUnknown(itemPtr, typeof(IShellItem));
                        dialog.SetFolder(folderItem);
                    }
                    finally
                    {
                        Marshal.Release(itemPtr);
                    }
                }
            }

            hr = dialog.Show(options.Owner);
            if (hr == unchecked((int)0x800704C7)) // ERROR_CANCELLED
            {
                return null;
            }

            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            dialog.GetResult(out resultItem);
            resultItem.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out nint pszPath);
            try
            {
                var path = Marshal.PtrToStringUni(pszPath);
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            finally
            {
                if (pszPath != 0)
                {
                    Ole32.CoTaskMemFree(pszPath);
                }
            }
        }
        finally
        {
            if (resultItem != null) Marshal.ReleaseComObject(resultItem);
            if (folderItem != null) Marshal.ReleaseComObject(folderItem);
            if (dialog != null) Marshal.ReleaseComObject(dialog);
            if (uninitialize)
            {
                Ole32.CoUninitialize();
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

    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(nint parent);
        void SetFileTypes(uint cFileTypes, nint rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(nint pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(nint pFilter);
        void GetResults(out nint ppenum);
        void GetSelectedItems(out nint ppsai);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint pbc, ref Guid bhid, ref Guid riid, out nint ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, out nint ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [Flags]
    private enum FILEOPENDIALOGOPTIONS : uint
    {
        FOS_NOCHANGEDIR = 0x00000008,
        FOS_PICKFOLDERS = 0x00000020,
        FOS_FORCEFILESYSTEM = 0x00000040,
        FOS_PATHMUSTEXIST = 0x00000800,
        FOS_FILEMUSTEXIST = 0x00001000
    }

    private enum SIGDN : uint
    {
        SIGDN_FILESYSPATH = 0x80058000
    }

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

        [LibraryImport(LibraryName, EntryPoint = "SHCreateItemFromParsingName", StringMarshalling = StringMarshalling.Utf16)]
        public static partial int SHCreateItemFromParsingName(
            string pszPath,
            nint pbc,
            ref Guid riid,
            out nint ppv);
    }

    private static partial class Ole32
    {
        private const string LibraryName = "ole32.dll";

        public const uint COINIT_APARTMENTTHREADED = 0x2;
        public const uint CLSCTX_INPROC_SERVER = 0x1;
        public const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

        [LibraryImport(LibraryName)]
        public static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

        [LibraryImport(LibraryName)]
        public static partial void CoUninitialize();

        [LibraryImport(LibraryName)]
        public static partial int CoCreateInstance(
            ref Guid rclsid,
            nint pUnkOuter,
            uint dwClsContext,
            ref Guid riid,
            out nint ppv);

        [LibraryImport(LibraryName)]
        public static partial void CoTaskMemFree(nint pv);
    }

    private static class CLSID
    {
        public static readonly Guid FileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    }
}
