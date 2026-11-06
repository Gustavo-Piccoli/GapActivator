namespace GapActivator.App.Localization;

public sealed partial class Localizer
{
    public string TrayTooltip(string state) => string.Format(Culture, T("GapActivator — {0}", "GapActivator — {0}"), state);

    public string TrayUnreachable => T("service unreachable", "serviço inacessível");

    public string TrayAttention => T("needs attention", "requer atenção");

    public string TrayBusy(int running) => string.Format(Culture, T("{0} running", "{0} em execução"), running);

    public string TrayPaused => T("paused", "pausado");

    public string TrayIdle => T("idle", "ocioso");

    public string MenuOpen => T("Open GapActivator", "Abrir o GapActivator");

    public string MenuRestartService => T("Restart service", "Reiniciar serviço");

    public string MenuQuit => T("Quit", "Sair");

    public string MoreNotifications(int more) => string.Format(Culture, T("(+{0} more)", "(+{0} outras)"), more);
}
