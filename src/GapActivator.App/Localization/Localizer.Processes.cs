namespace GapActivator.App.Localization;

public sealed partial class Localizer
{
    public string RunNow => T("Run now", "Executar agora");

    public string Enable => T("Enable", "Ativar");

    public string Disable => T("Disable", "Desativar");

    public string Unblock => T("Unblock", "Desbloquear");

    public string UnblockAndRun => T("Unblock and run", "Desbloquear e executar");

    public string Delete => T("Delete", "Excluir");

    public string NewProcess => T("New process", "Novo processo");

    public string Edit => T("Edit", "Editar");

    public string RunWhileRunningTitle => T("Already running", "Já em execução");

    // The spec wording (S:255), not the shorter one of the plan (preflight ruling C24).
    public string RunWhileRunningMessage => T(
        "The process is already running. Do you want to cancel the current run and start a new one?",
        "O processo já está em execução. Deseja cancelar a execução atual e iniciar uma nova?");

    public string RestartServiceKindMessage => T("The process is already running. Restart it?", "O processo já está em execução. Reiniciá-lo?");

    public string RestartButton => T("Restart", "Reiniciar");

    public string DisableTitle(string name) => string.Format(Culture, T("Disable \"{0}\"", "Desativar \"{0}\""), name);

    public string DisableActiveMessage(string name) =>
        string.Format(Culture, T("\"{0}\" has an active run.", "\"{0}\" tem uma execução ativa."), name);

    public string DisableAndCancel => T("Disable and cancel the current run", "Desativar e cancelar a execução atual");

    public string DisableAndLetFinish => T("Disable and let it finish", "Desativar e deixar terminar");

    public string DeleteTitle => T("Delete process", "Excluir processo");

    public string DeleteConfirm(string name) =>
        string.Format(Culture, T("Delete \"{0}\"? Its run history is kept.", "Excluir \"{0}\"? O histórico de execuções é mantido."), name);

    public string DeleteActiveMessage(string name) => string.Format(
        Culture,
        T("\"{0}\" has an active run. Cancel it and delete the process?", "\"{0}\" tem uma execução ativa. Cancelá-la e excluir o processo?"),
        name);

    public string CancelAndDelete => T("Cancel and delete", "Cancelar e excluir");

    public string DeleteNotStopped => T(
        "The run has not stopped yet. Try deleting again in a moment.",
        "A execução ainda não parou. Tente excluir novamente em instantes.");

    public string NextRun(string text) => string.Format(Culture, T("Next: {0}", "Próxima: {0}"), text);

    public string NoNextRun => T("No next run", "Sem próxima execução");

    // Not in the Fixed strings: the list's empty state and the accessible names of the row buttons (every row says the same word).
    public string NoProcesses => T("No processes are registered yet.", "Nenhum processo cadastrado ainda.");

    public string LastRunLabel => T("Last run", "Última execução");

    public string RunNowFor(string name) => string.Format(Culture, T("Run {0} now", "Executar {0} agora"), name);

    public string EnableFor(string name) => string.Format(Culture, T("Enable {0}", "Ativar {0}"), name);

    public string DisableFor(string name) => string.Format(Culture, T("Disable {0}", "Desativar {0}"), name);

    public string UnblockFor(string name) => string.Format(Culture, T("Unblock {0}", "Desbloquear {0}"), name);

    public string UnblockAndRunFor(string name) => string.Format(Culture, T("Unblock {0} and run it", "Desbloquear {0} e executá-lo"), name);

    public string DeleteFor(string name) => string.Format(Culture, T("Delete {0}", "Excluir {0}"), name);
}
