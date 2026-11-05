namespace GapActivator.App.Localization;

public sealed partial class Localizer
{
    public string FilterAllProcesses => T("All processes", "Todos os processos");

    public string FilterAllOutcomes => T("All outcomes", "Todos os resultados");

    public string FilterFailures => T("Failures", "Falhas");

    public string FilterDatesInverted => T("The start date is after the end date.", "A data inicial é posterior à data final.");

    public string PageOf(int page, int count) => string.Format(Culture, T("Page {0} of {1}", "Página {0} de {1}"), page, count);

    public string RunError => T("Error", "Erro");

    public string RunReason => T("Reason", "Motivo");

    public string ExitCode => T("Exit code", "Código de saída");

    public string Duration => T("Duration", "Duração");

    public string OutputTruncated => T("Only the last 1 MiB of output is shown.", "Somente o último 1 MiB da saída é exibido.");

    public string NoOutput => T("No output was captured.", "Nenhuma saída foi capturada.");

    public string Late => T("Late", "Atrasada");

    // Not in the Fixed strings: the history's own labels, states and button names.
    public string FilterProcessLabel => T("Process", "Processo");

    public string FilterOutcomeLabel => T("Outcome", "Resultado");

    public string FilterFromLabel => T("From", "De");

    public string FilterToLabel => T("To", "Até");

    public string ApplyFilters => T("Apply filters", "Aplicar filtros");

    public string PreviousPage => T("Previous page", "Página anterior");

    public string NextPage => T("Next page", "Próxima página");

    public string NoRuns => T("No runs match the filters.", "Nenhuma execução corresponde aos filtros.");

    public string LoadingRuns => T("Loading runs…", "Carregando execuções…");

    public string BackToHistory => T("Back to history", "Voltar ao histórico");

    public string RunEvents => T("Events", "Eventos");

    public string RunOutput => T("Output", "Saída");

    public string RunLoading => T("Loading the run…", "Carregando a execução…");

    public string NoEvents => T("No events were recorded.", "Nenhum evento registrado.");

    public string TriggerLabel => T("Trigger", "Origem");

    public string EndedLabel => T("Ended", "Fim");

    public string CreatedLabel => T("Created", "Criada");

    public string RunStatusLabel => T("Status", "Situação");

    public string RunsList => T("Runs", "Execuções");
}
