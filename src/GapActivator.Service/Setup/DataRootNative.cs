using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace GapActivator.Service.Setup;

/// <summary>Win32 calls for <see cref="DataRootPreparer"/>: privileges and handle-based file access.</summary>
internal static unsafe partial class DataRootNative
{
    public const uint Delete = 0x00010000;
    public const uint ReadControl = 0x00020000;
    public const uint WriteDac = 0x00040000;
    public const uint WriteOwner = 0x00080000;
    public const uint FileReadAttributes = 0x00000080;

    private const uint FileShareAll = 7;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const int ErrorNotAllAssigned = 1300;
    private const uint PrivilegeEnabled = 0x2;
    private const int FileDispositionInfo = 4;
    private const int FileAttributeTagInfo = 9;
    public const uint MountPointTag = 0xA0000003;
    public const uint SymlinkTag = 0xA000000C;
    private const int FileObject = 1;
    private const uint OwnerSecurityInformation = 0x1;
    private const uint DaclSecurityInformation = 0x4;
    private const uint ProtectedDacl = 0x80000000;
    private const uint UnprotectedDacl = 0x20000000;

    /// <summary>Enabled privileges; disposing puts each one back to its previous state.</summary>
    internal sealed class PrivilegeScope : IDisposable
    {
        private readonly nint _token;
        private readonly List<TokenPrivileges> _previous = [];

        public PrivilegeScope(params string[] privileges)
        {
            if (OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out _token) == 0)
            {
                // No token access: nothing can be enabled; the later calls report the real failure.
                _token = 0;
                return;
            }

            foreach (string name in privileges)
            {
                if (LookupPrivilegeValueW(null, name, out long luid) == 0)
                {
                    continue;
                }

                var state = new TokenPrivileges { Count = 1, Luid = luid, Attributes = PrivilegeEnabled };
                if (AdjustTokenPrivileges(_token, 0, ref state, (uint)sizeof(TokenPrivileges), out TokenPrivileges previous, out _) != 0
                    && Marshal.GetLastPInvokeError() != ErrorNotAllAssigned
                    && previous.Count == 1)
                {
                    _previous.Add(previous);
                }
            }
        }

        public void Dispose()
        {
            if (_token == 0)
            {
                return;
            }

            foreach (TokenPrivileges previous in _previous)
            {
                TokenPrivileges copy = previous;
                _ = AdjustTokenPrivileges(_token, 0, ref copy, (uint)sizeof(TokenPrivileges), out _, out _);
            }

            _ = CloseHandle(_token);
        }
    }

    /// <summary>
    /// Opens the object itself, never what a reparse point points to (backup semantics so directories open and the
    /// backup and restore privileges apply).
    /// </summary>
    public static SafeFileHandle OpenNoFollow(string path, uint access)
    {
        SafeFileHandle handle = CreateFileW(path, access, FileShareAll, 0, OpenExisting, BackupSemantics | OpenReparsePoint, 0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        return handle;
    }

    public static bool TryOpenNoFollow(string path, uint access, out SafeFileHandle handle)
    {
        handle = CreateFileW(path, access, FileShareAll, 0, OpenExisting, BackupSemantics | OpenReparsePoint, 0);
        if (!handle.IsInvalid)
        {
            return true;
        }

        handle.Dispose();
        return false;
    }

    /// <summary>Volume and file index: the same pair on two handles means the same object.</summary>
    public static (uint Volume, uint High, uint Low) GetIdentity(SafeFileHandle handle)
    {
        if (GetFileInformationByHandle(handle, out ByHandleFileInformation info) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return (info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow);
    }

    /// <summary>The reparse tag of the opened object (0 when it is not a reparse point).</summary>
    public static uint GetReparseTag(SafeFileHandle handle)
    {
        if (GetFileInformationByHandleEx(handle, FileAttributeTagInfo, out AttributeTagInfo info, (uint)sizeof(AttributeTagInfo)) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return info.ReparseTag;
    }

    internal static int ByHandleInfoSize => sizeof(ByHandleFileInformation);

    public static FileAttributes GetAttributes(SafeFileHandle handle)
    {
        if (GetFileInformationByHandle(handle, out ByHandleFileInformation info) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return (FileAttributes)info.FileAttributes;
    }

    /// <summary>The final path of an open handle, without the <c>\\?\</c> prefix.</summary>
    public static string GetFinalPath(SafeFileHandle handle)
    {
        char* buffer = stackalloc char[1024];
        uint length = GetFinalPathNameByHandleW(handle, buffer, 1024, 0);
        if (length == 0 || length >= 1024)
        {
            throw new Win32Exception(length == 0 ? Marshal.GetLastPInvokeError() : 206);
        }

        string path = new(buffer, 0, (int)length);
        return path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal) ? @"\\" + path[8..]
            : path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..]
            : path;
    }

    /// <summary>Marks the opened object for deletion; it goes when the handle closes. Needs DELETE access.</summary>
    public static void MarkForDelete(SafeFileHandle handle)
    {
        byte delete = 1;
        if (SetFileInformationByHandle(handle, FileDispositionInfo, ref delete, 1) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    /// <summary>Sets the owner through the open handle (needs WRITE_OWNER, or the take-ownership or restore privilege).</summary>
    public static void SetOwner(SafeFileHandle handle, SecurityIdentifier owner)
    {
        byte[] sid = new byte[owner.BinaryLength];
        owner.GetBinaryForm(sid, 0);
        fixed (byte* p = sid)
        {
            ThrowIfError(SetSecurityInfo(handle, FileObject, OwnerSecurityInformation, p, null, null, null));
        }
    }

    /// <summary>Replaces the DACL with <paramref name="acl"/> (a binary ACL) and blocks inheritance from the parent.</summary>
    public static void SetProtectedDacl(SafeFileHandle handle, byte[] acl)
    {
        fixed (byte* p = acl)
        {
            ThrowIfError(SetSecurityInfo(handle, FileObject, DaclSecurityInformation | ProtectedDacl, null, null, p, null));
        }
    }

    /// <summary>Drops every explicit entry and turns inheritance from the parent on.</summary>
    public static void ResetToInherited(SafeFileHandle handle)
    {
        byte[] empty = [2, 0, 8, 0, 0, 0, 0, 0];
        fixed (byte* p = empty)
        {
            ThrowIfError(SetSecurityInfo(handle, FileObject, DaclSecurityInformation | UnprotectedDacl, null, null, p, null));
        }
    }

    private static void ThrowIfError(uint error)
    {
        if (error != 0)
        {
            throw new Win32Exception((int)error);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct AttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public ulong CreationTime;
        public ulong LastAccessTime;
        public ulong LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string fileName, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out AttributeTagInfo information, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetFinalPathNameByHandleW(SafeFileHandle file, char* path, uint length, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int SetFileInformationByHandle(SafeFileHandle file, int informationClass, ref byte information, uint size);

    [LibraryImport("advapi32.dll")]
    private static partial uint SetSecurityInfo(
        SafeFileHandle handle, int objectType, uint information, byte* owner, byte* group, byte* dacl, byte* sacl);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int CloseHandle(nint handle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int LookupPrivilegeValueW(string? systemName, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int AdjustTokenPrivileges(
        nint token, int disableAll, ref TokenPrivileges newState, uint bufferLength, out TokenPrivileges previous, out uint returnLength);
}
