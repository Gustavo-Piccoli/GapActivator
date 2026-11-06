using System.Security.AccessControl;
using System.Security.Principal;

namespace GapActivator.Service.Setup;

/// <summary>
/// Makes the data root safe against a folder pre-created by a local user: removes a reparse point at the path (never its
/// target), creates the folder, makes Administrators the owner, replaces the DACL with a protected one and resets every
/// existing child to inherit only. Reparse points among the children are deleted, never followed.
/// </summary>
internal static class DataRootPreparer
{
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

        RemoveLink(path);
        Directory.CreateDirectory(path);

        var root = new DirectoryInfo(path);
        TrySetOwner(root, allowOwnerFallback);
        root.SetAccessControl(BuildRootSecurity(account));

        ResetChildren(root, allowOwnerFallback);
    }

    private static void RemoveLink(string path)
    {
        if (Directory.Exists(path) && IsReparsePoint(new DirectoryInfo(path)))
        {
            // Not recursive: on a link this removes the link only, never the target.
            Directory.Delete(path);
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static bool IsReparsePoint(FileSystemInfo info) => info.Attributes.HasFlag(FileAttributes.ReparsePoint);

    private static DirectorySecurity BuildRootSecurity(SecurityIdentifier account)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags both = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        security.AddAccessRule(new FileSystemAccessRule(LocalSystem, FileSystemRights.FullControl, both, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, both, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(account, FileSystemRights.Modify, both, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    private static void TrySetOwner(FileSystemInfo info, bool allowFallback)
    {
        try
        {
            if (info is DirectoryInfo directory)
            {
                var security = new DirectorySecurity();
                security.SetOwner(Administrators);
                directory.SetAccessControl(security);
            }
            else
            {
                var file = (FileInfo)info;
                var security = new FileSecurity();
                security.SetOwner(Administrators);
                file.SetAccessControl(security);
            }
        }
        catch (Exception ex) when (allowFallback && ex is InvalidOperationException or UnauthorizedAccessException or PrivilegeNotHeldException)
        {
            // Test-only fallback: the current owner stays.
        }
    }

    private static void ResetChildren(DirectoryInfo directory, bool allowOwnerFallback)
    {
        foreach (FileSystemInfo child in directory.EnumerateFileSystemInfos().ToList())
        {
            if (IsReparsePoint(child))
            {
                DeleteLink(child);
                continue;
            }

            TrySetOwner(child, allowOwnerFallback);
            if (child is DirectoryInfo subdirectory)
            {
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                subdirectory.SetAccessControl(security);
                ResetChildren(subdirectory, allowOwnerFallback);
            }
            else
            {
                var security = new FileSecurity();
                security.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                ((FileInfo)child).SetAccessControl(security);
            }
        }
    }

    private static void DeleteLink(FileSystemInfo link)
    {
        if (link is DirectoryInfo directory)
        {
            directory.Delete(recursive: false);
        }
        else
        {
            link.Delete();
        }
    }
}
