using GapActivator.Ipc.Contracts;

namespace GapActivator.App.Localization;

public sealed partial class Localizer
{
    public string OutcomeText(RunOutcome outcome) => outcome switch
    {
        RunOutcome.Succeeded => T("Succeeded", "Concluída com sucesso"),
        RunOutcome.Failed => T("Failed", "Falhou"),
        RunOutcome.FailedToStart => T("Failed to start", "Falha ao iniciar"),
        RunOutcome.TimedOut => T("Timed out", "Tempo esgotado"),
        RunOutcome.Cancelled => T("Cancelled", "Cancelada"),
        RunOutcome.Interrupted => T("Interrupted", "Interrompida"),
        RunOutcome.Abandoned => T("Abandoned", "Abandonada"),
        RunOutcome.Skipped => T("Skipped", "Ignorada"),
        RunOutcome.Crashed => T("Crashed", "Encerrada inesperadamente"),
        RunOutcome.Stopped => T("Stopped", "Parada"),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };

    public string StateText(RunState state) => state switch
    {
        RunState.Launching => T("Launching", "Iniciando"),
        RunState.Running => T("Running", "Em execução"),
        RunState.Stopping => T("Stopping", "Parando"),
        RunState.Completed => T("Completed", "Concluída"),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    // Not in the Fixed strings.
    public string TriggerText(RunTrigger trigger) => trigger switch
    {
        RunTrigger.Schedule => T("Scheduled", "Agendada"),
        RunTrigger.Retry => T("Retry", "Nova tentativa"),
        RunTrigger.Manual => T("Started manually", "Iniciada manualmente"),
        RunTrigger.Restart => T("Restarted", "Reiniciada"),
        RunTrigger.ServiceKeepAlive => T("Kept running", "Mantida em execução"),
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, null),
    };

    public string LifecycleText(ProcessLifecycle lifecycle) => lifecycle switch
    {
        ProcessLifecycle.Enabled => T("Enabled", "Ativado"),
        ProcessLifecycle.Disabled => T("Disabled", "Desativado"),
        ProcessLifecycle.Blocked => T("Blocked", "Bloqueado"),
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, null),
    };

    /// <summary>The explanation of a lifecycle badge; null for Enabled, which needs none.</summary>
    public string? LifecycleTooltip(ProcessLifecycle lifecycle) => lifecycle switch
    {
        ProcessLifecycle.Enabled => null,
        ProcessLifecycle.Disabled => T("Paused manually", "Pausado manualmente"),
        ProcessLifecycle.Blocked => T("The failure limit was reached", "O limite de falhas foi atingido"),
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, null),
    };

    public string KindText(ProcessKind kind) => kind switch
    {
        ProcessKind.RunToCompletion => T("Runs to completion", "Executa até concluir"),
        ProcessKind.Service => T("Service (stays running)", "Serviço (permanece em execução)"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    // Not in the Fixed strings.
    public string EventText(HistoryEntryKind kind) => kind switch
    {
        HistoryEntryKind.RunLate => T("Run started late", "Execução iniciada com atraso"),
        HistoryEntryKind.RunSkipped => T("Run skipped", "Execução ignorada"),
        HistoryEntryKind.RunStarted => T("Run started", "Execução iniciada"),
        HistoryEntryKind.RunStopRequested => T("Stop requested", "Parada solicitada"),
        HistoryEntryKind.RunCompleted => T("Run completed", "Execução concluída"),
        HistoryEntryKind.ServiceProcessCrashed => T("Service process crashed", "Processo de serviço encerrado inesperadamente"),
        HistoryEntryKind.ProcessBlocked => T("Process blocked", "Processo bloqueado"),
        HistoryEntryKind.ProcessUnblocked => T("Process unblocked", "Processo desbloqueado"),
        HistoryEntryKind.InstallationPaused => T("GapActivator paused", "GapActivator pausado"),
        HistoryEntryKind.InstallationResumed => T("GapActivator resumed", "GapActivator retomado"),
        HistoryEntryKind.CycleFailed => T("Cycle failed", "Ciclo com falha"),
        HistoryEntryKind.GapActivatorStarted => T("GapActivator started", "GapActivator iniciado"),
        HistoryEntryKind.GapActivatorRestarted => T("GapActivator restarted", "GapActivator reiniciado"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public string OperatorsModeText(OperatorsGroupMode mode) => mode switch
    {
        OperatorsGroupMode.Enabled => T(
            "Operators mode is on: Administrators and members of the local group GapActivator Operators can change processes and settings. Everyone else can view.",
            "Modo Operators ativado: Administradores e membros do grupo local GapActivator Operators podem alterar processos e configurações. Os demais usuários podem apenas visualizar."),
        OperatorsGroupMode.Disabled => T(
            "Operators mode is off: only Administrators can change processes and settings. This mode is used when the service runs under a specific account, because whoever changes what it runs gets that account's rights. Everyone else can view.",
            "Modo Operators desativado: somente Administradores podem alterar processos e configurações. Esse modo é usado quando o serviço é executado com uma conta específica, porque quem altera o que ele executa obtém os direitos dessa conta. Os demais usuários podem apenas visualizar."),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
