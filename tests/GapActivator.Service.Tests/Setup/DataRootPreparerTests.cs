using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using GapActivator.Service.Setup;
using GapActivator.Service.Tests.Support;
using Microsoft.Win32.SafeHandles;

namespace GapActivator.Service.Tests.Setup;

public sealed class DataRootPreparerTests : IDisposable
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);
    private static readonly SecurityIdentifier LocalService = new(WellKnownSidType.LocalServiceSid, null);

    private readonly string _temp = Path.Combine(Path.GetTempPath(), "ga-dr-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = [];

    public DataRootPreparerTests() => Directory.CreateDirectory(_temp);

    public void Dispose()
    {
        foreach (string junction in _junctions)
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction);
            }
        }

        using var privileges = new DataRootNative.PrivilegeScope("SeTakeOwnershipPrivilege", "SeRestorePrivilege", "SeBackupPrivilege");
        Directory.Delete(_temp, recursive: true);
    }

    private static SecurityIdentifier CurrentUser()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User!;
    }

    private static List<FileSystemAccessRule> Explicit(FileSystemSecurity security) =>
        [.. security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()];

    private static FileSystemRights Rights(List<FileSystemAccessRule> rules, SecurityIdentifier sid) =>
        rules.Single(r => (SecurityIdentifier)r.IdentityReference == sid).FileSystemRights;

    [Fact]
    public void Prepare_FinalDacl_HasExactlyTheThreeExplicitEntriesAndIsProtected()
    {
        string path = Path.Combine(_temp, "root");
        SecurityIdentifier account = CurrentUser();

        DataRootPreparer.Prepare(path, account, allowOwnerFallback: true);

        DirectorySecurity security = new DirectoryInfo(path).GetAccessControl();
        security.AreAccessRulesProtected.ShouldBeTrue();
        List<FileSystemAccessRule> rules = Explicit(security);
        rules.Count.ShouldBe(3);
        security.GetAccessRules(false, true, typeof(SecurityIdentifier)).Count.ShouldBe(0);
        Rights(rules, LocalSystem).ShouldBe(FileSystemRights.FullControl);
        Rights(rules, Administrators).ShouldBe(FileSystemRights.FullControl);
        Rights(rules, account).ShouldBe(FileSystemRights.Modify | FileSystemRights.Synchronize);
        foreach (FileSystemAccessRule rule in rules)
        {
            rule.AccessControlType.ShouldBe(AccessControlType.Allow);
            rule.InheritanceFlags.ShouldBe(InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
            rule.PropagationFlags.ShouldBe(PropagationFlags.None);
        }
    }

    [Fact]
    public void Prepare_ChildWithForeignExplicitAce_EndsWithNoExplicitAceAndInheritanceOn()
    {
        string path = Path.Combine(_temp, "root");
        string nested = Path.Combine(path, "logs", "runs");
        Directory.CreateDirectory(nested);
        string file = Path.Combine(path, "gapactivator.db");
        string deepFile = Path.Combine(nested, "x.log");
        File.WriteAllText(file, "x");
        File.WriteAllText(deepFile, "x");
        foreach (string target in new[] { file, deepFile })
        {
            var info = new FileInfo(target);
            FileSecurity foreign = info.GetAccessControl();
            foreign.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.FullControl, AccessControlType.Allow));
            info.SetAccessControl(foreign);
            Explicit(info.GetAccessControl()).ShouldContain(r => (SecurityIdentifier)r.IdentityReference == Users);
        }

        DataRootPreparer.Prepare(path, CurrentUser(), allowOwnerFallback: true);

        foreach (string target in new[] { file, deepFile })
        {
            FileSecurity security = new FileInfo(target).GetAccessControl();
            security.AreAccessRulesProtected.ShouldBeFalse();
            Explicit(security).ShouldBeEmpty();
        }

        DirectorySecurity nestedSecurity = new DirectoryInfo(nested).GetAccessControl();
        nestedSecurity.AreAccessRulesProtected.ShouldBeFalse();
        Explicit(nestedSecurity).ShouldBeEmpty();
    }

    [Fact]
    public void Prepare_JunctionAtPath_RemovesOnlyTheLinkAndCreatesARealFolder()
    {
        string target = Path.Combine(_temp, "target");
        Directory.CreateDirectory(target);
        string marker = Path.Combine(target, "keep.txt");
        File.WriteAllText(marker, "keep");
        string path = Path.Combine(_temp, "root");
        Junction(path, target);

        DataRootPreparer.Prepare(path, CurrentUser(), allowOwnerFallback: true);

        File.Exists(marker).ShouldBeTrue();
        File.ReadAllText(marker).ShouldBe("keep");
        new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint).ShouldBeFalse();
        Directory.Exists(path).ShouldBeTrue();
        Directory.GetFileSystemEntries(path).ShouldBeEmpty();
        new DirectoryInfo(target).GetAccessControl().AreAccessRulesProtected.ShouldBeFalse();
    }

    [Fact]
    public void Prepare_JunctionChild_IsDeletedWithoutTouchingItsTarget()
    {
        string target = Path.Combine(_temp, "target");
        Directory.CreateDirectory(target);
        string marker = Path.Combine(target, "keep.txt");
        File.WriteAllText(marker, "keep");
        FileSecurity markerBefore = new FileInfo(marker).GetAccessControl();
        markerBefore.AreAccessRulesProtected.ShouldBeFalse();
        int explicitBefore = Explicit(markerBefore).Count;
        string path = Path.Combine(_temp, "root");
        Directory.CreateDirectory(path);
        string link = Path.Combine(path, "link");
        Junction(link, target);

        DataRootPreparer.Prepare(path, CurrentUser(), allowOwnerFallback: true);

        Directory.Exists(link).ShouldBeFalse();
        File.Exists(marker).ShouldBeTrue();
        Explicit(new FileInfo(marker).GetAccessControl()).Count.ShouldBe(explicitBefore);
    }

    [Fact]
    public void Prepare_PreExistingRootWithForeignAceAndProtectedChild_IsReset()
    {
        string path = Path.Combine(_temp, "root");
        string folder = Path.Combine(path, "sub");
        Directory.CreateDirectory(folder);
        var rootInfo = new DirectoryInfo(path);
        DirectorySecurity foreign = rootInfo.GetAccessControl();
        foreign.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        rootInfo.SetAccessControl(foreign);
        var folderInfo = new DirectoryInfo(folder);
        DirectorySecurity protectedAcl = folderInfo.GetAccessControl();
        protectedAcl.SetAccessRuleProtection(true, false);
        protectedAcl.AddAccessRule(new FileSystemAccessRule(CurrentUser(), FileSystemRights.FullControl, AccessControlType.Allow));
        folderInfo.SetAccessControl(protectedAcl);
        SecurityIdentifier account = CurrentUser();

        DataRootPreparer.Prepare(path, account, allowOwnerFallback: true);

        DirectorySecurity root = new DirectoryInfo(path).GetAccessControl();
        Explicit(root).Count.ShouldBe(3);
        Explicit(root).ShouldNotContain(r => (SecurityIdentifier)r.IdentityReference == Users);
        DirectorySecurity sub = new DirectoryInfo(folder).GetAccessControl();
        sub.AreAccessRulesProtected.ShouldBeFalse();
        Explicit(sub).ShouldBeEmpty();
        List<FileSystemAccessRule> inherited = [.. sub.GetAccessRules(false, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()];
        Rights(inherited, account).ShouldBe(FileSystemRights.Modify | FileSystemRights.Synchronize);
    }

    [Fact]
    public void Prepare_RegularFileAtThePath_IsReplacedByAFolder()
    {
        string path = Path.Combine(_temp, "root");
        File.WriteAllText(path, "not a folder");

        DataRootPreparer.Prepare(path, CurrentUser(), allowOwnerFallback: true);

        Directory.Exists(path).ShouldBeTrue();
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public void Prepare_LinkChildrenAreDeleted()
    {
        string target = Path.Combine(_temp, "target");
        Directory.CreateDirectory(target);
        string path = Path.Combine(_temp, "root");
        Directory.CreateDirectory(path);
        Junction(Path.Combine(path, "a"), target);
        Junction(Path.Combine(path, "b"), target);

        DataRootPreparer.Prepare(path, CurrentUser(), allowOwnerFallback: true);

        Directory.GetFileSystemEntries(path).ShouldBeEmpty();
        Directory.Exists(target).ShouldBeTrue();
    }

    [Fact]
    public void Prepare_NonLinkReparseChild_IsResetLikeAnyOtherChild()
    {
        string path = Path.Combine(_temp, "root");
        Directory.CreateDirectory(path);
        string file = Path.Combine(path, "tagged.txt");
        File.WriteAllText(file, new string('a', 200_000));
        var info = new FileInfo(file);
        FileSecurity foreign = info.GetAccessControl();
        foreign.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.FullControl, AccessControlType.Allow));
        info.SetAccessControl(foreign);
        SetCustomReparseTag(file);
        info.Refresh();
        info.Attributes.HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();

        DataRootPreparer.Prepare(path, CurrentUser(), allowOwnerFallback: true);

        File.Exists(file).ShouldBeTrue();
        FileSecurity security = new FileInfo(file).GetAccessControl();
        security.AreAccessRulesProtected.ShouldBeFalse();
        Explicit(security).ShouldBeEmpty();
    }

    private static void SetCustomReparseTag(string file)
    {
        // A Microsoft-range tag that is neither a symbolic link nor a mount point, with 4 bytes of data.
        byte[] buffer = [0x23, 0x01, 0x00, 0x80, 4, 0, 0, 0, 1, 2, 3, 4];
        using SafeFileHandle handle = File.OpenHandle(file, FileMode.Open, FileAccess.Write);
#pragma warning disable SYSLIB1054 // Test-only call.
        bool ok = DeviceIoControl(handle, 0x900A4, buffer, buffer.Length, null, 0, out _, 0);
#pragma warning restore SYSLIB1054
        ok.ShouldBeTrue("FSCTL_SET_REPARSE_POINT failed with " + Marshal.GetLastWin32Error());
    }

#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device, uint code, byte[] input, int inputSize, byte[]? output, int outputSize, out int returned, nint overlapped);
#pragma warning restore SYSLIB1054


    [Fact]
    public void ByHandleFileInformation_MatchesTheNative52ByteLayout() =>
        DataRootNative.ByHandleInfoSize.ShouldBe(52);

    [Fact]
    [Trait("Category", "Integration")]
    public void Prepare_Elevated_HostileFolderOwnedByAUserWithoutAdministrators_IsTakenOver()
    {
        Elevation.SkipUnlessElevated();
        string path = Path.Combine(_temp, "root");
        string folder = Path.Combine(path, "sub");
        string file = Path.Combine(folder, "x.txt");
        Directory.CreateDirectory(folder);
        File.WriteAllText(file, "x");
        string denied = Path.Combine(path, "denied");
        string deniedFile = Path.Combine(denied, "y.txt");
        Directory.CreateDirectory(denied);
        File.WriteAllText(deniedFile, "y");
        using (new DataRootNative.PrivilegeScope("SeTakeOwnershipPrivilege", "SeRestorePrivilege", "SeBackupPrivilege"))
        {
            Hostile(file);
            Hostile(folder);

            // Not protected, owned by LOCAL SERVICE, with an explicit Deny for Administrators (propagation must still reach it).
            var deniedInfo = new DirectoryInfo(denied);
            DirectorySecurity deny = deniedInfo.GetAccessControl();
            deny.AddAccessRule(new FileSystemAccessRule(Administrators, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
            deny.SetOwner(LocalService);
            deniedInfo.SetAccessControl(deny);
            Hostile(path);
        }

        // The privileges are back to their previous state: Prepare has to enable them itself.
        DataRootPreparer.Prepare(path, CurrentUser());

        using var cleanup = new DataRootNative.PrivilegeScope("SeTakeOwnershipPrivilege", "SeRestorePrivilege", "SeBackupPrivilege");
        DirectorySecurity deniedSecurity = new DirectoryInfo(denied).GetAccessControl();
        deniedSecurity.AreAccessRulesProtected.ShouldBeFalse();
        Explicit(deniedSecurity).ShouldBeEmpty();
        Explicit(new FileInfo(deniedFile).GetAccessControl()).ShouldBeEmpty();
        foreach (string target in new[] { path, folder })
        {
            new DirectoryInfo(target).GetAccessControl().GetOwner(typeof(SecurityIdentifier)).ShouldBe(Administrators);
        }

        new FileInfo(file).GetAccessControl().GetOwner(typeof(SecurityIdentifier)).ShouldBe(Administrators);
        Explicit(new DirectoryInfo(path).GetAccessControl()).Count.ShouldBe(3);
        FileSecurity fileSecurity = new FileInfo(file).GetAccessControl();
        fileSecurity.AreAccessRulesProtected.ShouldBeFalse();
        Explicit(fileSecurity).ShouldBeEmpty();
    }

    // Owned by LOCAL SERVICE, protected DACL granting only LOCAL SERVICE: an elevated administrator is not a member of it,
    // so neither the DACL nor group ownership gives access; only the take-ownership, restore and backup privileges do.
    private static void Hostile(string target)
    {
        if (Directory.Exists(target))
        {
            var info = new DirectoryInfo(target);
            var security = new DirectorySecurity();
            security.SetOwner(LocalService);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(LocalService, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            info.SetAccessControl(security);
        }
        else
        {
            var info = new FileInfo(target);
            var security = new FileSecurity();
            security.SetOwner(LocalService);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(LocalService, FileSystemRights.FullControl, AccessControlType.Allow));
            info.SetAccessControl(security);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Prepare_Elevated_OwnerIsAdministrators()
    {
        Elevation.SkipUnlessElevated();
        string path = Path.Combine(_temp, "root");
        string child = Path.Combine(path, "child.txt");
        Directory.CreateDirectory(path);
        File.WriteAllText(child, "x");

        DataRootPreparer.Prepare(path, CurrentUser());

        new DirectoryInfo(path).GetAccessControl().GetOwner(typeof(SecurityIdentifier)).ShouldBe(Administrators);
        new FileInfo(child).GetAccessControl().GetOwner(typeof(SecurityIdentifier)).ShouldBe(Administrators);
    }

    [Fact]
    public void Prepare_NotElevatedWithoutFallback_FailsInsteadOfKeepingTheOwner()
    {
        Assert.SkipWhen(Elevation.IsElevated, "Only meaningful when not elevated.");

        Should.Throw<System.ComponentModel.Win32Exception>(() => DataRootPreparer.Prepare(Path.Combine(_temp, "root"), CurrentUser()));
    }

    private void Junction(string link, string target)
    {
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("/c");
        info.ArgumentList.Add("mklink");
        info.ArgumentList.Add("/J");
        info.ArgumentList.Add(link);
        info.ArgumentList.Add(target);
        using Process p = Process.Start(info)!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        p.ExitCode.ShouldBe(0);
        _junctions.Add(link);
    }
}
