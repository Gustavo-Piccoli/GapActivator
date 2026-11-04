namespace GapActivator.App.Localization;

public sealed partial class Localizer
{
    public string ErrorUnexpected(string message) =>
        string.Format(Culture, T("Unexpected error: {0}", "Erro inesperado: {0}"), message);

    /// <summary>The text of an IPC error code, with <c>{0}</c> where the service message goes; null for a code the app does not know.</summary>
    internal string? ErrorTemplate(string code) => code switch
    {
        "not-found" => T("The item no longer exists. Refresh and try again.", "O item não existe mais. Atualize e tente novamente."),
        "unauthorized" => T("Changes require Administrators or GapActivator Operators.", "Alterações exigem Administradores ou GapActivator Operators."),
        "name-taken" => T("Another process already uses this name.", "Outro processo já usa este nome."),
        "schedule-in-past" => T("A one-time schedule must be in the future.", "Um agendamento único precisa estar no futuro."),
        "secret-value-required" => T("A new secret needs a value.", "Um novo segredo precisa de um valor."),
        "has-active-run" => T("The process has an active run. Cancel it first.", "O processo tem uma execução ativa. Cancele-a primeiro."),
        "run-already-active" => T("The process is already running.", "O processo já está em execução."),
        "run-not-active" => T("There is no active run to cancel.", "Não há execução ativa para cancelar."),
        "process-blocked" => T("The process is blocked. Unblock it first.", "O processo está bloqueado. Desbloqueie-o primeiro."),
        "process-disabled" => T("The service is disabled. Enable it to start it.", "O serviço está desativado. Ative-o para iniciá-lo."),
        "installation-paused" => T("GapActivator is paused. Resume it to start runs.", "O GapActivator está pausado. Retome-o para iniciar execuções."),
        "use-disable" => T("A service-kind process is stopped by disabling it.", "Um processo do tipo serviço é parado ao desativá-lo."),
        "email-test-failed" => T("The test e-mail failed: {0}", "O e-mail de teste falhou: {0}"),
        "invalid" => T("The service rejected the input: {0}", "O serviço recusou os dados: {0}"),
        "conflict" => T("The data changed while saving. Try again.", "Os dados mudaram durante a gravação. Tente novamente."),
        "unknown-message" => T("This version of the app does not match the service.", "Esta versão do app não corresponde à do serviço."),
        "internal" => T("The service hit an unexpected error. See the service log.", "O serviço encontrou um erro inesperado. Veja o log do serviço."),
        "service-stopping" => T("GapActivator is stopping. Try again when it is running.", "O GapActivator está parando. Tente novamente quando ele estiver em execução."),
        "protocol-mismatch" => T("The app and the service use different protocol versions.", "O app e o serviço usam versões diferentes do protocolo."),
        "busy" => T("The service is serving too many connections. Retrying…", "O serviço está atendendo conexões demais. Tentando novamente…"),
        "disconnected" => T("Not connected to the service.", "Sem conexão com o serviço."),
        "server-not-verified" => T(
            "The pipe is not served by the installed GapActivator service. The connection was refused.",
            "O pipe não é atendido pelo serviço GapActivator instalado. A conexão foi recusada."),
        _ => null,
    };
}
