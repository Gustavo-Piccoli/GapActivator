using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Tests.ViewModels;

public class SettingsViewModelTests
{
    private readonly DashboardHarness _h = new();

    private SettingsViewModel Vm => _h.Settings;

    private SettingsInputDto SentSettings() =>
        (SettingsInputDto)_h.Gateway.Calls.Last(c => c.Method == nameof(IServiceGateway.UpdateSettingsAsync)).Args[0]!;

    private EmailInputDto SentTestEmail() =>
        (EmailInputDto)_h.Gateway.Calls.Last(c => c.Method == nameof(IServiceGateway.SendTestEmailAsync)).Args[0]!;

    private int Count(string method) => _h.Gateway.Calls.Count(c => c.Method == method);

    private static SettingsViewDto Full() =>
        new(
            TimeSpan.FromSeconds(45),
            7,
            TimeSpan.FromSeconds(120),
            TimeSpan.FromSeconds(15),
            21,
            400,
            LogLevel.Warning,
            CheckForUpdates: false,
            IsPaused: false,
            new EmailViewDto(
                true,
                "smtp.example.com",
                587,
                TlsMode.StartTls,
                "user@example.com",
                HasPassword: true,
                "from@example.com",
                ["a@example.com", "b@example.com"],
                [AlertKind.RunFailed, AlertKind.CycleFailed]));

    private async Task LoadAsync(SettingsViewDto? dto = null)
    {
        _h.Gateway.Settings = dto ?? Full();
        await Vm.LoadCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Load_FillsEveryServiceField()
    {
        await LoadAsync();

        Vm.CycleIntervalSeconds.ShouldBe(45m);
        Vm.FailureLimit.ShouldBe(7m);
        Vm.DefaultTimeLimitSeconds.ShouldBe(120m);
        Vm.DefaultGracePeriodSeconds.ShouldBe(15m);
        Vm.LogRetentionDays.ShouldBe(21m);
        Vm.HistoryRetentionDays.ShouldBe(400m);
        Vm.LogLevel.ShouldBe(LogLevel.Warning);
        Vm.EmailEnabled.ShouldBeTrue();
        Vm.SmtpHost.ShouldBe("smtp.example.com");
        Vm.SmtpPort.ShouldBe(587m);
        Vm.Tls.ShouldBe(TlsMode.StartTls);
        Vm.Username.ShouldBe("user@example.com");
        Vm.HasStoredPassword.ShouldBeTrue();
        Vm.NewPassword.ShouldBeEmpty();
        Vm.ClearPassword.ShouldBeFalse();
        Vm.Sender.ShouldBe("from@example.com");
        Vm.RecipientsText.Split('\n', StringSplitOptions.TrimEntries).ShouldBe(["a@example.com", "b@example.com"]);
        Vm.Alerts.Where(a => a.IsChecked).Select(a => a.Kind).ShouldBe([AlertKind.RunFailed, AlertKind.CycleFailed]);
        Vm.Alerts.Select(a => a.Kind).ShouldBe(Enum.GetValues<AlertKind>());
    }

    [Fact]
    public async Task Save_SendsEveryFieldAndKeepsTheServiceUpdateFlag()
    {
        await LoadAsync(Full());
        Vm.CheckForUpdates = true; // the app-local flag must not leak into the service value
        Vm.CycleIntervalSeconds = 60;
        Vm.FailureLimit = 4;
        Vm.DefaultTimeLimitSeconds = 300;
        Vm.DefaultGracePeriodSeconds = 20;
        Vm.LogRetentionDays = 10;
        Vm.HistoryRetentionDays = 100;
        Vm.LogLevel = LogLevel.Error;
        Vm.EmailEnabled = false;
        Vm.SmtpHost = "mail.example.com";
        Vm.SmtpPort = 465;
        Vm.Tls = TlsMode.Implicit;
        Vm.Username = "other@example.com";
        Vm.Sender = "sender@example.com";
        Vm.RecipientsText = "x@example.com";
        Vm.Alerts.Single(a => a.Kind == AlertKind.RunFailed).IsChecked = false;
        Vm.Alerts.Single(a => a.Kind == AlertKind.ProcessBlocked).IsChecked = true;

        await Vm.SaveCommand.ExecuteAsync(null);

        SettingsInputDto sent = SentSettings();
        sent.CycleInterval.ShouldBe(TimeSpan.FromSeconds(60));
        sent.FailureLimit.ShouldBe(4);
        sent.DefaultTimeLimit.ShouldBe(TimeSpan.FromSeconds(300));
        sent.DefaultGracePeriod.ShouldBe(TimeSpan.FromSeconds(20));
        sent.LogRetentionDays.ShouldBe(10);
        sent.HistoryRetentionDays.ShouldBe(100);
        sent.LogLevel.ShouldBe(LogLevel.Error);
        sent.CheckForUpdates.ShouldBeFalse();
        sent.Email.Enabled.ShouldBeFalse();
        sent.Email.Host.ShouldBe("mail.example.com");
        sent.Email.Port.ShouldBe(465);
        sent.Email.Tls.ShouldBe(TlsMode.Implicit);
        sent.Email.Username.ShouldBe("other@example.com");
        sent.Email.Sender.ShouldBe("sender@example.com");
        sent.Email.Recipients.ShouldBe(["x@example.com"]);
        sent.Email.Alerts.ShouldBe([AlertKind.ProcessBlocked, AlertKind.CycleFailed]);
        Vm.StatusText.ShouldBe("Settings saved.");
        Count(nameof(IServiceGateway.GetSettingsAsync)).ShouldBe(2);
    }

    [Fact]
    public async Task Save_EmptyTimeLimit_SendsNull()
    {
        await LoadAsync(Full());
        Vm.DefaultTimeLimitSeconds = null;

        await Vm.SaveCommand.ExecuteAsync(null);

        SentSettings().DefaultTimeLimit.ShouldBeNull();
    }

    [Theory]
    [InlineData("", false, SecretChangeKind.Keep, null)]
    [InlineData("s3cret", false, SecretChangeKind.Set, "s3cret")]
    [InlineData("", true, SecretChangeKind.Clear, null)]
    public async Task Password_UntouchedTypedOrCleared_GivesKeepSetOrClear(string typed, bool clear, SecretChangeKind kind, string? value)
    {
        await LoadAsync();
        Vm.NewPassword = typed;
        Vm.ClearPassword = clear;

        await Vm.SaveCommand.ExecuteAsync(null);

        SentSettings().Email.Password.ShouldBe(new SecretChangeDto(kind, value));
    }

    [Fact]
    public async Task Recipients_AreSplitAndTrimmed()
    {
        await LoadAsync();
        Vm.RecipientsText = "a@example.com; b@example.com\n\n c@example.com,";

        await Vm.SaveCommand.ExecuteAsync(null);

        SentSettings().Email.Recipients.ShouldBe(["a@example.com", "b@example.com", "c@example.com"]);
    }

    [Fact]
    public async Task Save_OutOfRange_SendsNothingAndNamesTheField()
    {
        await LoadAsync();
        Vm.CycleIntervalSeconds = 4;

        await Vm.SaveCommand.ExecuteAsync(null);

        Count(nameof(IServiceGateway.UpdateSettingsAsync)).ShouldBe(0);
        Vm.ErrorText.ShouldBe(_h.L.SettingsOutOfRange(_h.L.CycleIntervalLabel, 5, 3600));
    }

    [Fact]
    public async Task SendTestEmail_SendsTheCurrentFieldsAndShowsSent()
    {
        await LoadAsync();
        Vm.SmtpHost = "mail.example.com";
        Vm.NewPassword = "typed";

        await Vm.SendTestEmailCommand.ExecuteAsync(null);

        EmailInputDto sent = SentTestEmail();
        sent.Host.ShouldBe("mail.example.com");
        sent.Password.ShouldBe(new SecretChangeDto(SecretChangeKind.Set, "typed"));
        Vm.StatusText.ShouldBe("Test e-mail sent.");
        Vm.ErrorText.ShouldBeNull();
    }

    [Fact]
    public async Task SendTestEmail_Failure_ShowsTheServiceDetail()
    {
        await LoadAsync();
        _h.Gateway.FailNext(nameof(IServiceGateway.SendTestEmailAsync), new GapActivatorRequestException(IpcErrorCodes.EmailTestFailed, "connection refused", retryable: false));

        await Vm.SendTestEmailCommand.ExecuteAsync(null);

        Vm.ErrorText.ShouldBe("The test e-mail failed: connection refused");
        Vm.StatusText.ShouldBeNull();
    }

    [Fact]
    public async Task Save_ServiceInvalid_ShowsTheMessage()
    {
        await LoadAsync();
        _h.Gateway.FailNext(nameof(IServiceGateway.UpdateSettingsAsync), new GapActivatorRequestException(IpcErrorCodes.Invalid, "bad host", retryable: false));

        await Vm.SaveCommand.ExecuteAsync(null);

        Vm.ErrorText.ShouldBe("The service rejected the input: bad host");
        Vm.StatusText.ShouldBeNull();
        Count(nameof(IServiceGateway.GetSettingsAsync)).ShouldBe(1);
    }

    [Fact]
    public async Task Save_AfterAFailure_ClearsTheErrorOnSuccess()
    {
        await LoadAsync();
        _h.Gateway.FailNext(nameof(IServiceGateway.UpdateSettingsAsync), new GapActivatorRequestException(IpcErrorCodes.Invalid, "bad host", retryable: false));
        await Vm.SaveCommand.ExecuteAsync(null);

        await Vm.SaveCommand.ExecuteAsync(null);

        Vm.ErrorText.ShouldBeNull();
        Vm.StatusText.ShouldBe("Settings saved.");
    }

    [Fact]
    public async Task RestartService_Confirmed_SendsIt()
    {
        _h.Dialogs.Enqueue(DialogChoice.Primary);

        await Vm.RestartServiceCommand.ExecuteAsync(null);

        Count(nameof(IServiceGateway.RestartServiceAsync)).ShouldBe(1);
    }

    [Fact]
    public async Task RestartService_Declined_SendsNothing()
    {
        await Vm.RestartServiceCommand.ExecuteAsync(null);

        Count(nameof(IServiceGateway.RestartServiceAsync)).ShouldBe(0);
    }

    [Fact]
    public async Task RestartService_Failure_ShowsTheError()
    {
        _h.Dialogs.Enqueue(DialogChoice.Primary);
        _h.Gateway.FailNext(nameof(IServiceGateway.RestartServiceAsync), new GapActivatorRequestException(IpcErrorCodes.Unauthorized, "no", retryable: false));

        await Vm.RestartServiceCommand.ExecuteAsync(null);

        Vm.ErrorText.ShouldBe("Changes require Administrators or GapActivator Operators.");
    }

    [Fact]
    public async Task Viewer_ServiceFieldsReadOnlyAndAppFieldsEditable()
    {
        _h.Gateway.SetStatus(FakeServiceGateway.Viewer());
        await LoadAsync();

        Vm.ServiceFieldsReadOnly.ShouldBeTrue();
        Vm.SaveCommand.CanExecute(null).ShouldBeFalse();
        Vm.SendTestEmailCommand.CanExecute(null).ShouldBeFalse();
        Vm.RestartServiceCommand.CanExecute(null).ShouldBeFalse();
        Vm.LoadCommand.CanExecute(null).ShouldBeTrue();

        Vm.Theme = AppTheme.Dark;
        _h.AppSettings.Current.Theme.ShouldBe(AppTheme.Dark);

        _h.Gateway.SetStatus(FakeServiceGateway.Operator());
        Vm.ServiceFieldsReadOnly.ShouldBeFalse();
        Vm.SaveCommand.CanExecute(null).ShouldBeTrue();
    }

    [Theory]
    [InlineData("theme")]
    [InlineData("language")]
    [InlineData("notifications")]
    [InlineData("updates")]
    public async Task AppFields_Change_SaveAtOnce(string field)
    {
        await LoadAsync();
        var before = _h.AppSettings.Updates;

        switch (field)
        {
            case "theme":
                Vm.Theme = AppTheme.Light;
                _h.AppSettings.Current.Theme.ShouldBe(AppTheme.Light);
                break;
            case "language":
                Vm.Language = AppLanguage.Portuguese;
                _h.AppSettings.Current.Language.ShouldBe(AppLanguage.Portuguese);
                break;
            case "notifications":
                Vm.Notifications = false;
                _h.AppSettings.Current.Notifications.ShouldBeFalse();
                break;
            default:
                Vm.CheckForUpdates = false;
                _h.AppSettings.Current.CheckForUpdates.ShouldBeFalse();
                break;
        }

        _h.AppSettings.Updates.ShouldBe(before + 1);
        Count(nameof(IServiceGateway.UpdateSettingsAsync)).ShouldBe(0);
    }

    [Fact]
    public void AppFields_StartFromTheAppSettings_WithoutWritingThem()
    {
        var settings = new FakeAppSettings(new AppSettings(AppTheme.Dark, AppLanguage.Portuguese, false, false, null, null));
        var vm = new SettingsViewModel(_h.L, _h.Gateway, _h.Permissions, settings, _h.Service);

        vm.Theme.ShouldBe(AppTheme.Dark);
        vm.Language.ShouldBe(AppLanguage.Portuguese);
        vm.Notifications.ShouldBeFalse();
        vm.CheckForUpdates.ShouldBeFalse();
        settings.Updates.ShouldBe(0);
    }

    [Fact]
    public void AuthorizationText_FollowsTheOperatorsMode()
    {
        Vm.AuthorizationText.ShouldBe(_h.L.OperatorsModeText(OperatorsGroupMode.Enabled));

        _h.Gateway.SetStatus(FakeServiceGateway.Operator(OperatorsGroupMode.Disabled));
        Vm.AuthorizationText.ShouldBe(_h.L.OperatorsModeText(OperatorsGroupMode.Disabled));

        _h.Gateway.SetStatus(FakeServiceGateway.Stopped);
        Vm.AuthorizationText.ShouldBeNull();
    }

    [Fact]
    public async Task Connecting_LoadsWhenNothingWasLoadedYet()
    {
        _h.Gateway.SetStatus(FakeServiceGateway.Stopped);
        Vm.HasLoaded.ShouldBeFalse();

        _h.Gateway.SetStatus(FakeServiceGateway.Operator());
        await Vm.Loading;

        Vm.HasLoaded.ShouldBeTrue();
    }

    [Fact]
    public async Task Language_Switched_RebuildsTheCachedTextsKeepingTheSelections()
    {
        await LoadAsync();
        await Vm.SaveCommand.ExecuteAsync(null);

        _h.L.SetLanguage(AppLanguage.Portuguese);

        Vm.StatusText.ShouldBe("Configurações salvas.");
        Vm.Alerts.Single(a => a.Kind == AlertKind.RunFailed).IsChecked.ShouldBeTrue();
        Vm.Alerts.Single(a => a.Kind == AlertKind.RunFailed).Text.ShouldBe(_h.L.AlertName(AlertKind.RunFailed));
        Vm.ThemeOptions.Select(o => o.Text).ShouldBe(["Sistema", "Claro", "Escuro"]);
        Vm.LogLevelOptions.Single(o => o.Value is LogLevel.Warning).Text.ShouldBe("Aviso");
        Vm.LogLevel.ShouldBe(LogLevel.Warning);
    }
}
