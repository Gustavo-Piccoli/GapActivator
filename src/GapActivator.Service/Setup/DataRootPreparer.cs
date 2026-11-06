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
/// Reparse points are deleted (the link only, never the target).
/// </summary>
internal static class DataRootPreparer
{
    private const uint Rights = DataRootNative.ReadControl | DataRootNative.WriteDac | DataRootNative.WriteOwner
        | DataRootNative.FileReadAttributes | DataRootNative.Delete;

    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);

    /// <param name="allowOwnerFallback">
    /// Test only: keeps the current owner when the process cannot set Administrators as the owner (not elevated). The
    /// command line never sets it.
    /// </param>
    public static void Prepare(string path, SecurityIdentifier account, bool allowOwnerFallback = false)
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

        ResetChildren(fullPath, rootFinal, allowOwnerFallback);
    }

    /// <summary>
    /// Opens the root itself. A reparse point there is deleted (the link only), the folder is created and opened again,
    /// and a second reparse point is refused.
    /// </summary>
    private static SafeFileHandle OpenRoot(string path)
    {
        if (DataRootNative.TryOpenNoFollow(path, Rights, out SafeFileHandle existing))
        {
            if (!IsReparsePoint(existing))
            {
                RequireDirectory(existing, path);
                return existing;
            }

            DataRootNative.MarkForDelete(existing);
            existing.Dispose();
        }

        Directory.CreateDirectory(path);
        SafeFileHandle created = DataRootNative.OpenNoFollow(path, Rights);
        if (IsReparsePoint(created))
        {
            created.Dispose();
            throw new IOException($"'{path}' became a reparse point again; refusing to use it.");
        }

        RequireDirectory(created, path);
        return created;
    }

    private static bool IsReparsePoint(SafeFileHandle handle) =>
        DataRootNative.GetAttributes(handle).HasFlag(FileAttributes.ReparsePoint);

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

    private static byte[] ToBytes(DirectorySecurity security)
    {
        RawAcl acl = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0).DiscretionaryAcl!;
        byte[] bytes = new byte[acl.BinaryLength];
        acl.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private static void ResetChildren(string directory, string directoryFinal, bool allowOwnerFallback)
    {
        foreach (string childPath in Directory.EnumerateFileSystemEntries(directory).ToList())
        {
            string expected = directoryFinal + "\\" + Path.GetFileName(childPath);
            using SafeFileHandle child = DataRootNative.OpenNoFollow(childPath, Rights);
            FileAttributes attributes = DataRootNative.GetAttributes(child);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // Only the link goes, never the target.
                DataRootNative.MarkForDelete(child);
                continue;
            }

            // The handle must be the entry itself, not something an intermediate link redirected to.
            if (!string.Equals(DataRootNative.GetFinalPath(child), expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"'{childPath}' resolved to a different location; refusing to change it.");
            }

            ApplyOwner(child, allowOwnerFallback);
            DataRootNative.ResetToInherited(child);
            if (attributes.HasFlag(FileAttributes.Directory))
            {
                ResetChildren(childPath, expected, allowOwnerFallback);
            }
        }
    }
}
