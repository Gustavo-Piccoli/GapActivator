using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.App.Settings;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.ViewModels;

/// <summary>An e-mail alert the user can tick; <see cref="Text"/> follows the language, <see cref="IsChecked"/> is kept.</summary>
public sealed partial class AlertOption(AlertKind kind, string text) : ObservableObject
{
    public AlertKind Kind { get; } = kind;

    [ObservableProperty]
    public partial string Text { get; set; } = text;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}

/// <summary>
/// The settings page. The service settings are loaded from and saved to the service (changing them needs the right to change);
/// the app settings (theme, language, notifications, update check) are always editable and are written the moment they change.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private const int MaxDays = 3650;

    private readonly IServiceGateway _gateway;
    private readonly Permissions _permissions;
    private readonly IAppSettingsService _appSettings;
    private readonly ServiceActions _service;

    private bool _applying;
    private bool _serviceCheckForUpdates;
    private Outcome _status;

    public SettingsViewModel(Localizer l, IServiceGateway gateway, Permissions permissions, IAppSettingsService appSettings, ServiceActions service)
        : base(l)
    {
        _gateway = gateway;
        _permissions = permissions;
        _appSettings = appSettings;
        _service = service;

        foreach (AppTheme theme in Enum.GetValues<AppTheme>())
        {
            ThemeOptions.Add(new ChoiceOption(theme, l.ThemeName(theme)));
        }

        LanguageOptions.Add(new ChoiceOption(AppLanguage.English, l.LanguageEnglish));
        LanguageOptions.Add(new ChoiceOption(AppLanguage.Portuguese, l.LanguagePortuguese));
        foreach (LogLevel level in Enum.GetValues<LogLevel>())
        {
            LogLevelOptions.Add(new ChoiceOption(level, l.LogLevelName(level)));
        }

        foreach (TlsMode mode in Enum.GetValues<TlsMode>())
        {
            TlsOptions.Add(new ChoiceOption(mode, l.TlsName(mode)));
        }

        foreach (AlertKind kind in Enum.GetValues<AlertKind>())
        {
            Alerts.Add(new AlertOption(kind, l.AlertName(kind)));
        }

        ReadAppSettings();

        permissions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Permissions.CanChange))
            {
                OnPropertyChanged(nameof(ServiceFieldsReadOnly));
                SaveCommand.NotifyCanExecuteChanged();
                SendTestEmailCommand.NotifyCanExecuteChanged();
                RestartServiceCommand.NotifyCanExecuteChanged();
            }
        };
        gateway.ConnectionChanged += (_, status) =>
        {
            OnPropertyChanged(nameof(AuthorizationText));

            // Nothing is shown before the first read, so the first connection brings the values in.
            if (status.IsConnected && !HasLoaded)
            {
                Loading = LoadCommand.ExecuteAsync(null);
            }
        };
        l.PropertyChanged += (_, _) => RebuildTexts();
    }

    private enum Outcome
    {
        None,
        Saved,
        TestEmailSent,
    }

    public ObservableCollection<ChoiceOption> ThemeOptions { get; } = [];

    public ObservableCollection<ChoiceOption> LanguageOptions { get; } = [];

    public ObservableCollection<ChoiceOption> LogLevelOptions { get; } = [];

    public ObservableCollection<ChoiceOption> TlsOptions { get; } = [];

    public ObservableCollection<AlertOption> Alerts { get; } = [];

    // Service settings. Durations are whole seconds; the numeric boxes work with decimals.
    [ObservableProperty]
    public partial decimal CycleIntervalSeconds { get; set; } = 30;

    [ObservableProperty]
    public partial decimal FailureLimit { get; set; } = 3;

    [ObservableProperty]
    public partial decimal? DefaultTimeLimitSeconds { get; set; }

    [ObservableProperty]
    public partial decimal DefaultGracePeriodSeconds { get; set; } = 10;

    [ObservableProperty]
    public partial decimal LogRetentionDays { get; set; } = 30;

    [ObservableProperty]
    public partial decimal HistoryRetentionDays { get; set; } = 90;

    [ObservableProperty]
    public partial LogLevel LogLevel { get; set; } = LogLevel.Information;

    [ObservableProperty]
    public partial bool EmailEnabled { get; set; }

    [ObservableProperty]
    public partial string SmtpHost { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal SmtpPort { get; set; } = 25;

    [ObservableProperty]
    public partial TlsMode Tls { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    /// <summary>What the user typed; empty keeps the stored password. The stored one is never shown.</summary>
    [ObservableProperty]
    public partial string NewPassword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasStoredPassword { get; private set; }

    [ObservableProperty]
    public partial bool ClearPassword { get; set; }

    [ObservableProperty]
    public partial string Sender { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RecipientsText { get; set; } = string.Empty;

    // App settings, written to the app settings file as soon as they change.
    [ObservableProperty]
    public partial AppTheme Theme { get; set; }

    [ObservableProperty]
    public partial AppLanguage Language { get; set; }

    [ObservableProperty]
    public partial bool Notifications { get; set; }

    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>True after the service settings have been read once; before that the form would show defaults as if they were real.</summary>
    [ObservableProperty]
    public partial bool HasLoaded { get; private set; }

    // The drop-downs select an option object; these map it to and from the enum values the rest of the model uses.
    public ChoiceOption ThemeChoice
    {
        get => ThemeOptions.First(o => o.Value.Equals(Theme));
        set => Choose(value, v => Theme = (AppTheme)v, nameof(ThemeChoice));
    }

    public ChoiceOption LanguageChoice
    {
        get => LanguageOptions.First(o => o.Value.Equals(Language));
        set => Choose(value, v => Language = (AppLanguage)v, nameof(LanguageChoice));
    }

    public ChoiceOption LogLevelChoice
    {
        get => LogLevelOptions.First(o => o.Value.Equals(LogLevel));
        set => Choose(value, v => LogLevel = (LogLevel)v, nameof(LogLevelChoice));
    }

    public ChoiceOption TlsChoice
    {
        get => TlsOptions.First(o => o.Value.Equals(Tls));
        set => Choose(value, v => Tls = (TlsMode)v, nameof(TlsChoice));
    }

    public Permissions Permissions => _permissions;

    public bool ServiceFieldsReadOnly => !_permissions.CanChange;

    public string? AuthorizationText => _gateway.Status is { IsConnected: true, OperatorsMode: { } mode } ? L.OperatorsModeText(mode) : null;

    /// <summary>What the last save or test e-mail achieved; null while there is nothing to report.</summary>
    public string? StatusText => _status switch
    {
        Outcome.Saved => L.SettingsSaved,
        Outcome.TestEmailSent => L.TestEmailSent,
        _ => null,
    };

    /// <summary>The read the last connection started; tests await it, the screen does not need to.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    private bool CanChange => _permissions.CanChange;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        SettingsViewDto? dto = null;
        var ok = await TryAsync(async () => dto = await _gateway.GetSettingsAsync().ConfigureAwait(true)).ConfigureAwait(true);
        IsLoading = false;
        if (ok && dto is not null)
        {
            Apply(dto);
            HasLoaded = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task SaveAsync()
    {
        SetStatus(Outcome.None);
        if (Validate() is { } invalid)
        {
            ErrorText = invalid;
            return;
        }

        var input = new SettingsInputDto(
            TimeSpan.FromSeconds((double)CycleIntervalSeconds),
            (int)FailureLimit,
            DefaultTimeLimitSeconds is { } limit ? TimeSpan.FromSeconds((double)limit) : null,
            TimeSpan.FromSeconds((double)DefaultGracePeriodSeconds),
            (int)LogRetentionDays,
            (int)HistoryRetentionDays,
            LogLevel,
            _serviceCheckForUpdates,
            BuildEmail());
        var saved = await TryAsync(() => _gateway.UpdateSettingsAsync(input)).ConfigureAwait(true);
        if (saved)
        {
            SetStatus(Outcome.Saved);
            await LoadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task SendTestEmailAsync()
    {
        SetStatus(Outcome.None);
        EmailInputDto email = BuildEmail();
        if (await TryAsync(() => _gateway.SendTestEmailAsync(email)).ConfigureAwait(true))
        {
            SetStatus(Outcome.TestEmailSent);
        }
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task RestartServiceAsync()
    {
        SetStatus(Outcome.None);
        await TryAsync(() => _service.RestartServiceAsync()).ConfigureAwait(true);
    }

    /// <summary>Splits on new lines, commas and semicolons, trims, and drops empty entries.</summary>
    internal static List<string> ParseRecipients(string text) =>
        [.. text.Split(['\r', '\n', ',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    partial void OnThemeChanged(AppTheme value)
    {
        OnPropertyChanged(nameof(ThemeChoice));
        WriteAppSetting(s => s with { Theme = value });
    }

    partial void OnLanguageChanged(AppLanguage value)
    {
        OnPropertyChanged(nameof(LanguageChoice));
        WriteAppSetting(s => s with { Language = value });
    }

    partial void OnLogLevelChanged(LogLevel value) => OnPropertyChanged(nameof(LogLevelChoice));

    partial void OnTlsChanged(TlsMode value) => OnPropertyChanged(nameof(TlsChoice));

    partial void OnNotificationsChanged(bool value) => WriteAppSetting(s => s with { Notifications = value });

    partial void OnCheckForUpdatesChanged(bool value) => WriteAppSetting(s => s with { CheckForUpdates = value });

    private void Choose(ChoiceOption? option, Action<object> assign, string property)
    {
        if (option is null)
        {
            // A combo box deselects while its items change; keep the current value and push it back.
            OnPropertyChanged(property);
            return;
        }

        assign(option.Value);
    }

    private void WriteAppSetting(Func<AppSettings, AppSettings> change)
    {
        if (!_applying)
        {
            _appSettings.Update(change);
        }
    }

    private void SetStatus(Outcome status)
    {
        _status = status;
        OnPropertyChanged(nameof(StatusText));
    }

    private void ReadAppSettings()
    {
        AppSettings current = _appSettings.Current;
        _applying = true;
        try
        {
            Theme = current.Theme;
            Language = current.Language;
            Notifications = current.Notifications;
            CheckForUpdates = current.CheckForUpdates;
        }
        finally
        {
            _applying = false;
        }
    }

    private void Apply(SettingsViewDto dto)
    {
        _applying = true;
        try
        {
            CycleIntervalSeconds = (decimal)dto.CycleInterval.TotalSeconds;
            FailureLimit = dto.FailureLimit;
            DefaultTimeLimitSeconds = dto.DefaultTimeLimit is { } limit ? (decimal)limit.TotalSeconds : null;
            DefaultGracePeriodSeconds = (decimal)dto.DefaultGracePeriod.TotalSeconds;
            LogRetentionDays = dto.LogRetentionDays;
            HistoryRetentionDays = dto.HistoryRetentionDays;
            LogLevel = dto.LogLevel;
            _serviceCheckForUpdates = dto.CheckForUpdates;

            EmailViewDto email = dto.Email;
            EmailEnabled = email.Enabled;
            SmtpHost = email.Host;
            SmtpPort = email.Port;
            Tls = email.Tls;
            Username = email.Username ?? string.Empty;
            HasStoredPassword = email.HasPassword;
            NewPassword = string.Empty;
            ClearPassword = false;
            Sender = email.Sender;
            RecipientsText = string.Join(Environment.NewLine, email.Recipients);
            foreach (AlertOption option in Alerts)
            {
                option.IsChecked = email.Alerts.Contains(option.Kind);
            }
        }
        finally
        {
            _applying = false;
        }
    }

    private EmailInputDto BuildEmail()
    {
        SecretChangeDto password = NewPassword.Length > 0
            ? new SecretChangeDto(SecretChangeKind.Set, NewPassword)
            : ClearPassword
                ? new SecretChangeDto(SecretChangeKind.Clear, null)
                : new SecretChangeDto(SecretChangeKind.Keep, null);
        var username = Username.Trim();
        return new EmailInputDto(
            EmailEnabled,
            SmtpHost.Trim(),
            (int)SmtpPort,
            Tls,
            username.Length == 0 ? null : username,
            password,
            Sender.Trim(),
            ParseRecipients(RecipientsText),
            [.. Alerts.Where(a => a.IsChecked).Select(a => a.Kind)]);
    }

    // The domain's bounds; the numeric boxes enforce them too, this covers a value typed past them.
    private string? Validate() =>
        Check(CycleIntervalSeconds, L.CycleIntervalLabel, 5, 3600)
        ?? Check(FailureLimit, L.FailureLimitLabel, 1, 100)
        ?? Check(DefaultGracePeriodSeconds, L.DefaultGraceLabel, 1, 600)
        ?? Check(LogRetentionDays, L.LogRetentionLabel, 1, MaxDays)
        ?? Check(HistoryRetentionDays, L.HistoryRetentionLabel, 1, MaxDays)
        ?? Check(SmtpPort, L.SmtpPortLabel, 1, 65535);

    private string? Check(decimal value, string label, int min, int max) =>
        value >= min && value <= max && value == decimal.Truncate(value) ? null : L.SettingsOutOfRange(label, min, max);

    private void RebuildTexts()
    {
        foreach (ChoiceOption option in ThemeOptions)
        {
            option.Text = L.ThemeName((AppTheme)option.Value);
        }

        LanguageOptions[0].Text = L.LanguageEnglish;
        LanguageOptions[1].Text = L.LanguagePortuguese;
        foreach (ChoiceOption option in LogLevelOptions)
        {
            option.Text = L.LogLevelName((LogLevel)option.Value);
        }

        foreach (ChoiceOption option in TlsOptions)
        {
            option.Text = L.TlsName((TlsMode)option.Value);
        }

        foreach (AlertOption option in Alerts)
        {
            option.Text = L.AlertName(option.Kind);
        }

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(AuthorizationText));
    }
}
