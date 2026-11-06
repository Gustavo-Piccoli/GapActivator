using GapActivator.App.Services;

namespace GapActivator.App.Tests.Services;

public class ShellLauncherTests
{
    [Fact]
    public void CreateOpenFolderStartInfo_Folder_UsesTheRootedWindowsExplorer()
    {
        var info = ShellLauncher.CreateOpenFolderStartInfo(@"C:\Example Folder");

        Path.IsPathRooted(info.FileName).ShouldBeTrue();
        info.FileName.ShouldEndWith(@"\explorer.exe");
        info.FileName.ShouldBe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
        info.Arguments.ShouldBe("\"C:\\Example Folder\"");
    }
}
