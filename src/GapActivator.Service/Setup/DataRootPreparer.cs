using System.ComponentModel;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace GapActivator.Service.Setup;

/// <summary>
/// Makes the data root safe against a folder pre-created by a local user. Every object is opened once, without following
/// reparse points, and the reparse check, the owner and the DACL all go through that one handle, so a link swapped in
/// after the check cannot redirect the writes. The root is locked down first (owner Administrators, protected DACL), so
/// a non-administrator cannot create or swap entries while the children are walked. The take-ownership, restore and
/// backup privileges are enabled for the duration, so a DACL that excludes Administrators does not stop the takeover.
/// Objects are opened without DELETE (it is checked against other processes' share modes). A symbolic link or mount
/// point is deleted (the link only, never the target) through a second no-follow open that must be the same object; any
/// other reparse tag is left alone and reported.
/// </summary>
internal static class DataRootPreparer
{
    private const uint Rights = DataRootNative.ReadControl | DataRootNative.WriteDac | DataRootNative.WriteOwner
        | DataRootNative.FileReadAttributes;

    private const uint DeleteRights = DataRootNative.Delete | DataRootNative.FileReadAttributes;

    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);

    /// <param name="allowOwnerFallback">
    /// Test only: keeps the current owner when the process cannot set Administrators as the owner (not elevated). The
    /// command line never sets it.
    /// </param>
    /// <param name="reportSkipped">Receives one line for each reparse point that is neither a symbolic link nor a mount point.</param>
    public static void Prepare(
        string path, SecurityIdentifier account, bool allowOwnerFallback = false, Action<string>? reportSkipped = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(account);

        using var privileges = new DataRootNative.PrivilegeScope("SeTakeOwnershipPrivilege", "SeRestorePrivilege", "SeBackupPrivilege");

        string fullPath = Path.GetFullPath(path).TrimEnd('\\');
        using SafeFileHandle root = OpenRoot(fullPath);
        string rootFinal = DataRootNative.GetFinalPath(root);

        ApplyOwner(root, allowOwnerFallback);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags both = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, both, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, both, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(account, FileSystemRights.Modify, both, PropagationFlags.None, AccessControlType.Allow));
        DataRootNative.SetProtectedDacl(root, ToBytes(security));

        ResetChildren(fullPath, rootFinal, allowOwnerFallback, reportSkipped ?? (_ => { }));
    }

    /// <summary>
    /// Opens the root itself. A link or a regular file there is deleted, the folder is created and opened again, and a
    /// second reparse point is refused.
    /// </summary>
    private static SafeFileHandle OpenRoot(string path)
    {
        if (DataRootNative.TryOpenNoFollow(path, Rights, out SafeFileHandle existing))
        {
            bool keep = false;
            try
            {
                FileAttributes attributes = DataRootNative.GetAttributes(existing);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    uint tag = DataRootNative.GetReparseTag(existing);
                    if (tag is not (DataRootNative.MountPointTag or DataRootNative.SymlinkTag))
                    {
                        throw new IOException($"'{path}' is a reparse point of an unsupported kind; refusing to use it.");
                    }

                    DeleteSame(path, existing);
                }
                else if (!attributes.HasFlag(FileAttributes.Directory))
                {
                    DeleteSame(path, existing);
                }
                else
                {
                    keep = true;
                    return existing;
                }
            }
            finally
            {
                if (!keep)
                {
                    existing.Dispose();
                }
            }
        }

        Directory.CreateDirectory(path);
        SafeFileHandle created = DataRootNative.OpenNoFollow(path, Rights);
        try
        {
            if (DataRootNative.GetAttributes(created).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException($"'{path}' became a reparse point again; refusing to use it.");
            }

            RequireDirectory(created, path);
            return created;
        }
        catch
        {
            created.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Re-opens <paramref name="path"/> with DELETE and no-follow, checks it is the same object as
    /// <paramref name="original"/>, and marks it for deletion; it goes when the handle closes.
    /// </summary>
    private static void DeleteSame(string path, SafeFileHandle original)
    {
        using SafeFileHandle doomed = DataRootNative.OpenNoFollow(path, DeleteRights);
        if (DataRootNative.GetIdentity(doomed) != DataRootNative.GetIdentity(original))
        {
            throw new IOException($"'{path}' changed while it was being replaced; refusing to delete it.");
        }

        DataRootNative.MarkForDelete(doomed);
    }

    private static void RequireDirectory(SafeFileHandle handle, string path)
    {
        if (!DataRootNative.GetAttributes(handle).HasFlag(FileAttributes.Directory))
        {
            throw new IOException($"'{path}' is not a folder.");
        }
    }

    private static void ApplyOwner(SafeFileHandle handle, bool allowFallback)
    {
        try
        {
            DataRootNative.SetOwner(handle, Administrators);
        }
        catch (Win32Exception ex) when (allowFallback && ex.NativeErrorCode is 5 or 1307 or 1314)
        {
            // Test-only fallback: the current owner stays.
        }
    }

    // Test-only fallback: without the privileges an inherited DACL may not grant WRITE_OWNER; open without it then.
    private static SafeFileHandle OpenChild(string path, bool allowFallback)
    {
        if (!allowFallback)
        {
            return DataRootNative.OpenNoFollow(path, Rights);
        }

        return DataRootNative.TryOpenNoFollow(path, Rights, out SafeFileHandle handle)
            ? handle
            : DataRootNative.OpenNoFollow(path, Rights & ~DataRootNative.WriteOwner);
    }

    private static byte[] ToBytes(DirectorySecurity security)
    {
        RawAcl acl = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0).DiscretionaryAcl!;
        byte[] bytes = new byte[acl.BinaryLength];
        acl.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private static void ResetChildren(string directory, string directoryFinal, bool allowOwnerFallback, Action<string> reportSkipped)
    {
        foreach (string childPath in Directory.EnumerateFileSystemEntries(directory).ToList())
        {
            string expected = directoryFinal + "\\" + Path.GetFileName(childPath);
            using SafeFileHandle child = OpenChild(childPath, allowOwnerFallback);

            // First of all the handle must be the entry itself (for a link, the link's own path), not something an
            // intermediate link redirected to: nothing is changed or deleted before this holds.
            if (!string.Equals(DataRootNative.GetFinalPath(child), expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"'{childPath}' resolved to a different location; refusing to change it.");
            }

            FileAttributes attributes = DataRootNative.GetAttributes(child);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                if (DataRootNative.GetReparseTag(child) is DataRootNative.MountPointTag or DataRootNative.SymlinkTag)
                {
                    // Only the link goes, never the target.
                    DeleteSame(childPath, child);
                }
                else
                {
                    reportSkipped($"skipped '{childPath}': a reparse point that is not a symbolic link or a mount point.");
                }

                continue;
            }

            ApplyOwner(child, allowOwnerFallback);
            DataRootNative.ResetToInherited(child);
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                ResetChildren(childPath, expected, allowOwnerFallback, reportSkipped);
            }
        }
    }
}
