using GapActivator.App.Composition;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.Updates;
using GapActivator.App.ViewModels;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.ViewModels;

public class AboutViewModelTests : IAsyncDisposable
{
    private static readonly Uri Page = new("https://github.com/Gustavo-Piccoli/GapActivator/releases/tag/v0.2.0");

    private readonly Localizer _l = new(AppLanguage.English);
    private readonly FakeServiceGateway _gateway = new();
    private readonly FakeUpdateChecker _updates = new();
    private readonly FakeShellLauncher _launcher = new();
    private readonly TempDirectory _dir = new();
    private readonly FakeAppSettings _settings = new();

    private AboutViewModel Create(string version = "0.1.0") =>
        new(_l, _gateway, _updates, _settings, _launcher, new AppPaths(Path.Combine(_dir.Path, "app")), new ServicePaths(Path.Combine(_dir.Path, "service")), new ImmediateUiDispatcher(), version);

    private static string Make(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    public ValueTask DisposeAsync()
    {
        _dir.Dispose();
        GC.SuppressFinalize(this);
        return _gateway.DisposeAsync();
    }

    [Fact]
    public void About_VersionHasNoBuildMetadata()
    {
        AppVersion.ToDisplay("0.1.0+abc").ShouldBe("0.1.0");

        Create("0.1.0").VersionText.ShouldBe("0.1.0");
    }

    [Fact]
    public void About_RepositoryUrl_IsTheProjectPage() =>
        Create().RepositoryUrl.ShouldBe("https://github.com/Gustavo-Piccoli/GapActivator");

    [Theory]
    [InlineData(UpdateCheckOutcome.UpToDate, "You have the latest version.")]
    [InlineData(UpdateCheckOutcome.NoReleases, "No release has been published yet.")]
    [InlineData(UpdateCheckOutcome.Failed, "Could not check for updates.")]
    [InlineData(UpdateCheckOutcome.UpdateAvailable, "Version 0.2.0 is available.")]
    public async Task About_CheckNow_ShowsEachStatusText(UpdateCheckOutcome outcome, string expected)
    {
        _updates.Result = new UpdateCheckResult(outcome, outcome is UpdateCheckOutcome.UpdateAvailable ? new Version(0, 2, 0) : null, outcome is UpdateCheckOutcome.UpdateAvailable ? Page : null);
        var vm = Create();

        await vm.CheckNowCommand.ExecuteAsync(null);

        vm.UpdateStatus.ShouldBe(expected);
        vm.AvailableReleaseUrl.ShouldBe(outcome == UpdateCheckOutcome.UpdateAvailable ? Page : null);
    }

    [Fact]
    public async Task About_CheckNow_ShowsCheckingWhileTheCallRuns()
    {
        var tcs = new TaskCompletionSource<UpdateCheckResult>();
        var slow = new SlowChecker(tcs.Task);
        var vm = new AboutViewModel(_l, _gateway, slow, _settings, _launcher, new AppPaths("a"), new ServicePaths("s"), new ImmediateUiDispatcher(), "0.1.0");

        var running = vm.CheckNowCommand.ExecuteAsync(null);
        var during = vm.UpdateStatus;
        tcs.SetResult(new UpdateCheckResult(UpdateCheckOutcome.UpToDate, null, null));
        await running;

        during.ShouldBe("Checking…");
        vm.UpdateStatus.ShouldBe("You have the latest version.");
    }

    [Fact]
    public async Task About_CheckNow_CheckerThrows_ShowsTheFailureText()
    {
        _updates.Failure = new InvalidOperationException("boom");
        var vm = Create();

        await vm.CheckNowCommand.ExecuteAsync(null);

        vm.UpdateStatus.ShouldBe("Could not check for updates.");
    }

    [Fact]
    public void About_StartsFromTheLastBackgroundResult()
    {
        _updates.Last = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(0, 2, 0), Page);

        var vm = Create();

        vm.UpdateStatus.ShouldBe("Version 0.2.0 is available.");
        vm.AvailableReleaseUrl.ShouldBe(Page);
    }

    [Fact]
    public void About_UpdateFound_ShowsTheAvailableVersionAndTheLanguageSwitchRebuildsIt()
    {
        var vm = Create();

        _updates.RaiseUpdateFound(new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(0, 2, 0), Page));
        _l.SetLanguage(AppLanguage.Portuguese);

        vm.UpdateStatus.ShouldBe("A versão 0.2.0 está disponível.");
    }

    [Fact]
    public async Task About_LanguageSwitch_KeepsTheLastOutcome()
    {
        var vm = Create();
        await vm.CheckNowCommand.ExecuteAsync(null);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _l.SetLanguage(AppLanguage.Portuguese);

        vm.UpdateStatus.ShouldBe("Você tem a versão mais recente.");
        changed.ShouldContain(string.Empty);
    }

    [Fact]
    public void About_PathsAreTheServiceAndAppPaths()
    {
        var vm = Create();

        vm.ServiceDataRoot.ShouldBe(Path.Combine(_dir.Path, "service"));
        vm.ServiceLogs.ShouldBe(Path.Combine(_dir.Path, "service", "logs"));
        vm.AppSettingsFile.ShouldBe(Path.Combine(_dir.Path, "app", "app-settings.json"));
        vm.AppLogs.ShouldBe(Path.Combine(_dir.Path, "app", "logs"));
    }

    [Fact]
    public void ServicePaths_Default_IsProgramDataGapActivator()
    {
        ServicePaths.Default.DataRoot.ShouldBe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GapActivator"));
        ServicePaths.Default.LogsDirectory.ShouldEndWith(Path.Combine("GapActivator", "logs"));
    }

    [Fact]
    public void About_ServiceVersionAndAuthorizationFollowHello()
    {
        _gateway.SetStatus(FakeServiceGateway.Stopped);
        var vm = Create();
        vm.ServiceVersionText.ShouldBeNull();
        vm.AuthorizationText.ShouldBeNull();

        _gateway.SetStatus(FakeServiceGateway.Operator(OperatorsGroupMode.Enabled) with { ServiceVersion = "1.2.3" });

        vm.ServiceVersionText.ShouldBe("Service version 1.2.3");
        vm.AuthorizationText.ShouldBe(_l.OperatorsModeText(OperatorsGroupMode.Enabled));

        _l.SetLanguage(AppLanguage.Portuguese);
        vm.ServiceVersionText.ShouldBe("Versão do serviço 1.2.3");
        vm.AuthorizationText.ShouldBe(_l.OperatorsModeText(OperatorsGroupMode.Enabled));

        _gateway.SetStatus(FakeServiceGateway.Stopped);
        vm.ServiceVersionText.ShouldBeNull();
        vm.AuthorizationText.ShouldBeNull();
    }

    [Fact]
    public void About_NotVerified_ShowsTheDevelopmentNote()
    {
        var vm = Create();
        vm.IsDevelopmentMode.ShouldBeFalse();

        _gateway.SetStatus(FakeServiceGateway.DevOperator);

        vm.IsDevelopmentMode.ShouldBeTrue();
        vm.DevelopmentNote.ShouldBe("The connected service is not the installed GapActivator service (development mode).");

        _gateway.SetStatus(FakeServiceGateway.Operator());
        vm.IsDevelopmentMode.ShouldBeFalse();
    }

    [Fact]
    public void About_OpenFolder_OpensAnExistingFolder()
    {
        var vm = Create();
        var logs = Make(vm.ServiceLogs);

        vm.OpenFolderCommand.Execute(logs);

        _launcher.Folders.ShouldBe([logs]);
        vm.FolderErrorText.ShouldBeNull();
    }

    [Fact]
    public void About_OpenFolder_MissingFolder_ShowsAShortErrorAndOpensNothing()
    {
        var vm = Create();

        vm.OpenFolderCommand.Execute(vm.ServiceLogs);

        _launcher.Folders.ShouldBeEmpty();
        vm.FolderErrorText.ShouldBe("The folder does not exist yet.");

        _l.SetLanguage(AppLanguage.Portuguese);
        vm.FolderErrorText.ShouldBe("A pasta ainda não existe.");
    }

    [Fact]
    public void About_OpenFolder_ErrorStaysUntilAnOpenWorks()
    {
        var vm = Create();
        vm.OpenFolderCommand.Execute(vm.ServiceLogs);
        vm.OpenFolderCommand.Execute(vm.ServiceDataRoot);
        vm.FolderErrorText.ShouldNotBeNull();

        vm.OpenFolderCommand.Execute(Make(vm.AppLogs));

        vm.FolderErrorText.ShouldBeNull();
    }

    [Fact]
    public void About_OpenFolder_ShellFailure_ShowsAShortError()
    {
        var vm = Create();
        _launcher.Failure = new InvalidOperationException("no shell");

        vm.OpenFolderCommand.Execute(Make(vm.AppLogs));

        vm.FolderErrorText.ShouldBe("Could not open it.");
    }

    [Fact]
    public void About_OpenRelease_OpensTheReleasePage()
    {
        _updates.Last = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(0, 2, 0), Page);
        var vm = Create();

        vm.OpenReleaseCommand.CanExecute(null).ShouldBeTrue();
        vm.OpenReleaseCommand.Execute(null);

        _launcher.Urls.ShouldBe([Page]);
    }

    [Fact]
    public void About_OpenRelease_WithoutAnUpdate_CannotExecute() =>
        Create().OpenReleaseCommand.CanExecute(null).ShouldBeFalse();

    [Fact]
    public void About_OpenRepository_OpensTheRepository()
    {
        var vm = Create();

        vm.OpenRepositoryCommand.Execute(null);

        _launcher.Urls.ShouldBe([new Uri("https://github.com/Gustavo-Piccoli/GapActivator")]);
    }

    [Fact]
    public void About_Dispose_StopsListening()
    {
        var vm = Create();
        vm.Dispose();

        _updates.RaiseUpdateFound(new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(0, 2, 0), Page));

        vm.UpdateStatus.ShouldBe("Not checked yet.");
    }

    [Fact]
    public void About_BackgroundCheck_UpdatesTheStatus()
    {
        var vm = Create();

        _updates.RaiseChecked(new UpdateCheckResult(UpdateCheckOutcome.UpToDate, new Version(0, 1, 0), null));

        vm.UpdateStatus.ShouldBe("You have the latest version.");
    }

    [Fact]
    public void About_BeforeAnyCheck_SaysNotCheckedYetAndFollowsTheLanguage()
    {
        var vm = Create();

        vm.UpdateStatus.ShouldBe("Not checked yet.");
        _l.SetLanguage(AppLanguage.Portuguese);
        vm.UpdateStatus.ShouldBe("Ainda não verificado.");
    }

    [Fact]
    public void About_AutomaticChecksOff_SaysSoAndFollowsTheSettingAndTheLanguage()
    {
        var vm = Create();
        vm.AutomaticChecksNote.ShouldBeNull();

        _settings.Update(s => s with { CheckForUpdates = false });

        vm.AutomaticChecksNote.ShouldBe("Automatic checks are off.");
        _l.SetLanguage(AppLanguage.Portuguese);
        vm.AutomaticChecksNote.ShouldBe("As verificações automáticas estão desligadas.");

        _settings.Update(s => s with { CheckForUpdates = true });
        vm.AutomaticChecksNote.ShouldBeNull();
    }

    [Fact]
    public void About_OpenRepository_Failure_ShowsTheErrorOnTheRepositoryOnlyUntilItWorks()
    {
        var vm = Create();
        _launcher.Failure = new InvalidOperationException("no browser");

        vm.OpenRepositoryCommand.Execute(null);

        vm.RepositoryErrorText.ShouldBe("Could not open the page.");
        vm.ReleaseErrorText.ShouldBeNull();
        vm.FolderErrorText.ShouldBeNull();
        _l.SetLanguage(AppLanguage.Portuguese);
        vm.RepositoryErrorText.ShouldBe("Não foi possível abrir a página.");

        vm.OpenRepositoryCommand.Execute(null);
        vm.RepositoryErrorText.ShouldNotBeNull();

        _launcher.Failure = null;
        vm.OpenRepositoryCommand.Execute(null);
        vm.RepositoryErrorText.ShouldBeNull();
    }

    [Fact]
    public void About_OpenRelease_Failure_ShowsTheErrorOnTheReleaseOnly()
    {
        _updates.Last = new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, new Version(0, 2, 0), Page);
        var vm = Create();
        _launcher.Failure = new InvalidOperationException("no browser");

        vm.OpenReleaseCommand.Execute(null);

        vm.ReleaseErrorText.ShouldBe("Could not open the page.");
        vm.RepositoryErrorText.ShouldBeNull();
        vm.FolderErrorText.ShouldBeNull();
    }

    [Fact]
    public void About_FolderErrorSurvivesALinkThatOpens()
    {
        var vm = Create();
        vm.OpenFolderCommand.Execute(vm.ServiceLogs);

        vm.OpenRepositoryCommand.Execute(null);

        vm.FolderErrorText.ShouldBe("The folder does not exist yet.");
    }

    private sealed class SlowChecker(Task<UpdateCheckResult> pending) : IUpdateChecker
    {
        public UpdateCheckResult? Last => null;

        public event EventHandler<UpdateCheckResult>? UpdateFound
        {
            add { }
            remove { }
        }

        public event EventHandler<UpdateCheckResult>? Checked
        {
            add { }
            remove { }
        }

        public Task<UpdateCheckResult> CheckNowAsync(CancellationToken ct) => pending;
    }
}
