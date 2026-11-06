using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using GapActivator.Service.Setup;
using GapActivator.Service.Tests.Support;

namespace GapActivator.Service.Tests.Setup;

public sealed class DataRootPreparerTests : IDisposable
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

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

        Should.Throw<Exception>(() => DataRootPreparer.Prepare(Path.Combine(_temp, "root"), CurrentUser()));
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
