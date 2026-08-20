using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SciCanvas.Platform.Windows;

public sealed class WindowsFileIdentityProvider
{
    public string? TryGetFileId(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.None);

            return TryGetFileId(handle);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string? TryGetFileId(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (!OperatingSystem.IsWindows() || handle.IsInvalid)
        {
            return null;
        }

        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation information))
        {
            return null;
        }

        return $"{information.VolumeSerialNumber:X8}:" +
               $"{information.FileIndexHigh:X8}{information.FileIndexLow:X8}";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle fileHandle,
        out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }
}

