namespace GapActivator.App.Localization;

public sealed partial class Localizer
{
    public string ActiveRunsHeading => T("Active runs", "Execuções ativas");

    public string UpcomingHeading => T("Upcoming runs", "Próximas execuções");

    public string RecentFailuresHeading => T("Recent failures", "Falhas recentes");

    public string Connected => T("Connected to the service", "Conectado ao serviço");

    public string CallerCanChange(string name) =>
        string.Format(Culture, T("{0}: you can change processes and settings.", "{0}: você pode alterar processos e configurações."), name);

    public string CallerViewOnly(string name) =>
        string.Format(Culture, T("{0}: you can view only.", "{0}: você pode apenas visualizar."), name);

    public string CancelRun => T("Cancel", "Cancelar");

    public string StopService => T("Stop", "Parar");

    public string StopServiceTitle => T("Stop the service-kind process", "Parar o processo do tipo serviço");

    public string StopServiceConfirm(string name) => string.Format(
        Culture,
        T("Stopping \"{0}\" disables it. It stays stopped until you enable it again.", "Parar \"{0}\" o desativa. Ele permanece parado até que você o ative novamente."),
        name);

    public string Pause => T("Pause GapActivator", "Pausar o GapActivator");

    public string Resume => T("Resume GapActivator", "Retomar o GapActivator");

    public string PausedText => T("GapActivator is paused: no new run starts.", "O GapActivator está pausado: nenhuma nova execução é iniciada.");

    // Not in the Fixed strings: the dashboard's own labels and its empty states.
    public string StartedLabel => T("Started", "Início");

    public string UnseenLabel => T("New", "Nova");

    public string NoActiveRuns => T("No runs are active.", "Nenhuma execução ativa.");

    public string NoUpcomingRuns => T("No runs are scheduled.", "Nenhuma execução agendada.");

    public string NoRecentFailures => T("No recent failures.", "Nenhuma falha recente.");

    public string StaleNote => T("Not connected: showing the last known state.", "Sem conexão: exibindo o último estado conhecido.");

    public string RefreshFailedText => T(
        "Could not read the service state. The data shown may be out of date.",
        "Não foi possível ler o estado do serviço. Os dados exibidos podem estar desatualizados.");

    public string WaitingForService => T("Waiting for the service to report its state…", "Aguardando o serviço informar o estado…");

    public string CancelRunFor(string name) => string.Format(Culture, T("Cancel {0}", "Cancelar {0}"), name);

    public string StopServiceFor(string name) => string.Format(Culture, T("Stop {0}", "Parar {0}"), name);

    public string OpenRunFor(string name, string outcome, string when) =>
        string.Format(Culture, T("{0}, {1}, {2}. Open the run.", "{0}, {1}, {2}. Abrir a execução."), name, outcome, when);
}
