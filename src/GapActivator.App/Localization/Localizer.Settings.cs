using GapActivator.App.Settings;
using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Localization;

public sealed partial class Localizer
{
    // Fixed strings of the plan.
    public string RestartServiceTitle => T("Restart the service", "Reiniciar o serviço");

    public string RestartServiceConfirm => T(
        "Restart the GapActivator service? Active runs are stopped and recorded as Interrupted. The service comes back after about 60 seconds.",
        "Reiniciar o serviço GapActivator? As execuções ativas são paradas e registradas como Interrompidas. O serviço volta em cerca de 60 segundos.");

    public string TestEmailSent => T("Test e-mail sent.", "E-mail de teste enviado.");

    public string ClearPassword => T("Remove the stored password", "Remover a senha salva");

    public string SettingsSaved => T("Settings saved.", "Configurações salvas.");

    public string LanguageEnglish => T("English", "English");

    public string LanguagePortuguese => T("Português (Brasil)", "Português (Brasil)");

    public string ShowNotifications => T("Show notifications", "Mostrar notificações");

    public string ThemeName(AppTheme theme) => theme switch
    {
        AppTheme.System => T("System", "Sistema"),
        AppTheme.Light => T("Light", "Claro"),
        AppTheme.Dark => T("Dark", "Escuro"),
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null),
    };

    // The rest of the page.
    public string SettingsLoading => T("Loading settings…", "Carregando configurações…");

    public string SectionServiceRuns => T("Runs and cycle", "Execuções e ciclo");

    public string SectionServiceRetention => T("Retention and logs", "Retenção e logs");

    public string SectionEmail => T("E-mail alerts", "Alertas por e-mail");

    public string SectionThisApp => T("This app", "Este aplicativo");

    public string AuthorizationLabel => T("Who can change settings", "Quem pode alterar as configurações");

    public string CycleIntervalLabel => T("Check interval (seconds)", "Intervalo de verificação (segundos)");

    public string FailureLimitLabel => T("Consecutive failures before blocking", "Falhas consecutivas antes de bloquear");

    public string DefaultTimeLimitLabel => T("Default time limit (seconds)", "Limite de tempo padrão (segundos)");

    public string DefaultTimeLimitHint => T("Empty means no time limit.", "Vazio significa sem limite de tempo.");

    public string DefaultGraceLabel => T("Default grace period (seconds)", "Tempo de tolerância padrão (segundos)");

    public string LogRetentionLabel => T("Keep logs (days)", "Manter logs (dias)");

    public string HistoryRetentionLabel => T("Keep run history (days)", "Manter histórico de execuções (dias)");

    public string LogLevelLabel => T("Log level", "Nível de log");

    public string EmailEnabledLabel => T("Send e-mail alerts", "Enviar alertas por e-mail");

    public string SmtpHostLabel => T("SMTP server", "Servidor SMTP");

    public string SmtpPortLabel => T("Port", "Porta");

    public string TlsLabel => T("Encryption", "Criptografia");

    public string SmtpUsernameLabel => T("User name", "Nome de usuário");

    public string SmtpPasswordLabel => T("Password", "Senha");

    public string PasswordStoredHint => T("A password is stored. Leave this empty to keep it.", "Há uma senha salva. Deixe vazio para mantê-la.");

    public string PasswordNoneHint => T("No password is stored.", "Nenhuma senha salva.");

    public string SenderLabel => T("Sender address", "Endereço do remetente");

    public string RecipientsLabel => T("Recipients (one per line)", "Destinatários (um por linha)");

    public string AlertsLabel => T("Send an e-mail when", "Enviar e-mail quando");

    public string SendTestEmail => T("Send test e-mail", "Enviar e-mail de teste");

    public string RestartServiceAction => T("Restart service", "Reiniciar serviço");

    public string ThemeLabel => T("Theme", "Tema");

    public string LanguageLabel => T("Language", "Idioma");

    public string CheckForUpdatesLabel => T("Check for updates", "Verificar atualizações");

    public string AppSettingsNote => T("These options apply to this app only and take effect at once.", "Estas opções valem só para este aplicativo e têm efeito imediato.");

    public string SettingsOutOfRange(string field, int min, int max) =>
        string.Format(Culture, T("{0} must be between {1} and {2}.", "{0} deve estar entre {1} e {2}."), field, min, max);

    public string TlsName(TlsMode mode) => mode switch
    {
        TlsMode.None => T("None (not encrypted)", "Nenhuma (sem criptografia)"),
        TlsMode.StartTls => T("STARTTLS (upgrades the connection)", "STARTTLS (atualiza a conexão)"),
        TlsMode.Implicit => T("Implicit TLS", "TLS implícito"),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public string LogLevelName(LogLevel level) => level switch
    {
        LogLevel.Debug => T("Debug", "Depuração"),
        LogLevel.Information => T("Information", "Informação"),
        LogLevel.Warning => T("Warning", "Aviso"),
        LogLevel.Error => T("Error", "Erro"),
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    public string AlertName(AlertKind kind) => kind switch
    {
        AlertKind.ProcessBlocked => T("A process is blocked", "Um processo é bloqueado"),
        AlertKind.RunFailed => T("A run fails", "Uma execução falha"),
        AlertKind.RunTimedOut => T("A run exceeds its time limit", "Uma execução excede o limite de tempo"),
        AlertKind.ServiceProcessCrashed => T("A service process ends unexpectedly", "Um processo de serviço encerra inesperadamente"),
        AlertKind.CycleFailed => T("A cycle fails", "Um ciclo falha"),
        AlertKind.ServiceRestarted => T("The service restarts", "O serviço é reiniciado"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
