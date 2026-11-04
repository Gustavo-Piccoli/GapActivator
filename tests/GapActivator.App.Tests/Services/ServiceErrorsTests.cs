using System.Reflection;
using GapActivator.App.Localization;
using GapActivator.App.Services;
using GapActivator.Ipc;

namespace GapActivator.App.Tests.Services;

public class ServiceErrorsTests
{
    public static TheoryData<string, string, string> Rows => new()
    {
        { "not-found", "The item no longer exists. Refresh and try again.", "O item não existe mais. Atualize e tente novamente." },
        { "unauthorized", "Changes require Administrators or GapActivator Operators.", "Alterações exigem Administradores ou GapActivator Operators." },
        { "name-taken", "Another process already uses this name.", "Outro processo já usa este nome." },
        { "schedule-in-past", "A one-time schedule must be in the future.", "Um agendamento único precisa estar no futuro." },
        { "secret-value-required", "A new secret needs a value.", "Um novo segredo precisa de um valor." },
        { "has-active-run", "The process has an active run. Cancel it first.", "O processo tem uma execução ativa. Cancele-a primeiro." },
        { "run-already-active", "The process is already running.", "O processo já está em execução." },
        { "run-not-active", "There is no active run to cancel.", "Não há execução ativa para cancelar." },
        { "process-blocked", "The process is blocked. Unblock it first.", "O processo está bloqueado. Desbloqueie-o primeiro." },
        { "process-disabled", "The service is disabled. Enable it to start it.", "O serviço está desativado. Ative-o para iniciá-lo." },
        { "installation-paused", "GapActivator is paused. Resume it to start runs.", "O GapActivator está pausado. Retome-o para iniciar execuções." },
        { "use-disable", "A service-kind process is stopped by disabling it.", "Um processo do tipo serviço é parado ao desativá-lo." },
        { "email-test-failed", "The test e-mail failed: {0}", "O e-mail de teste falhou: {0}" },
        { "invalid", "The service rejected the input: {0}", "O serviço recusou os dados: {0}" },
        { "conflict", "The data changed while saving. Try again.", "Os dados mudaram durante a gravação. Tente novamente." },
        { "unknown-message", "This version of the app does not match the service.", "Esta versão do app não corresponde à do serviço." },
        { "internal", "The service hit an unexpected error. See the service log.", "O serviço encontrou um erro inesperado. Veja o log do serviço." },
        { "service-stopping", "GapActivator is stopping. Try again when it is running.", "O GapActivator está parando. Tente novamente quando ele estiver em execução." },
        { "protocol-mismatch", "The app and the service use different protocol versions.", "O app e o serviço usam versões diferentes do protocolo." },
        { "busy", "The service is serving too many connections. Retrying…", "O serviço está atendendo conexões demais. Tentando novamente…" },
        { "disconnected", "Not connected to the service.", "Sem conexão com o serviço." },
        { "server-not-verified", "The pipe is not served by the installed GapActivator service. The connection was refused.", "O pipe não é atendido pelo serviço GapActivator instalado. A conexão foi recusada." },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Describe_EachKnownCode_GivesTheFixedText(string code, string english, string portuguese)
    {
        var error = new GapActivatorRequestException(code, "m", retryable: false);

        ServiceErrors.Describe(error, new Localizer(AppLanguage.English)).ShouldBe(english.Replace("{0}", "m", StringComparison.Ordinal));
        ServiceErrors.Describe(error, new Localizer(AppLanguage.Portuguese)).ShouldBe(portuguese.Replace("{0}", "m", StringComparison.Ordinal));
        ServiceErrors.Describe(code, "m", new Localizer(AppLanguage.English)).ShouldBe(english.Replace("{0}", "m", StringComparison.Ordinal));
    }

    [Fact]
    public void Describe_InvalidAndEmailTestFailed_IncludeTheServiceMessage()
    {
        var l = new Localizer(AppLanguage.English);

        ServiceErrors.Describe(new GapActivatorRequestException("invalid", "Name must not be empty.", false), l)
            .ShouldBe("The service rejected the input: Name must not be empty.");
        ServiceErrors.Describe(new GapActivatorRequestException("email-test-failed", "Connection refused.", false), l)
            .ShouldBe("The test e-mail failed: Connection refused.");
    }

    [Fact]
    public void Describe_UnknownCode_FallsBackToTheServiceMessage()
    {
        var l = new Localizer(AppLanguage.English);

        ServiceErrors.Describe(new GapActivatorRequestException("brand-new-code", "Something specific.", false), l)
            .ShouldBe("Something specific.");
        ServiceErrors.Describe("brand-new-code", null, l).ShouldBe("brand-new-code");
    }

    [Fact]
    public void Describe_Timeout_IsDisconnected()
    {
        ServiceErrors.Describe(new TimeoutException("late"), new Localizer(AppLanguage.English)).ShouldBe("Not connected to the service.");
        ServiceErrors.Describe(new TimeoutException("late"), new Localizer(AppLanguage.Portuguese)).ShouldBe("Sem conexão com o serviço.");
    }

    [Fact]
    public void Describe_AnythingElse_IsUnexpectedError()
    {
        ServiceErrors.Describe(new InvalidOperationException("boom"), new Localizer(AppLanguage.English)).ShouldBe("Unexpected error: boom");
        ServiceErrors.Describe(new InvalidOperationException("boom"), new Localizer(AppLanguage.Portuguese)).ShouldBe("Erro inesperado: boom");
    }

    [Fact]
    public void Is_MatchesOnlyARequestExceptionWithThatCode()
    {
        ServiceErrors.Is(new GapActivatorRequestException("busy", "m", true), "busy").ShouldBeTrue();
        ServiceErrors.Is(new GapActivatorRequestException("busy", "m", true), "invalid").ShouldBeFalse();
        ServiceErrors.Is(new TimeoutException("busy"), "busy").ShouldBeFalse();
    }

    [Fact]
    public void KnownCodes_CoverEveryIpcErrorCode()
    {
        var codes = typeof(IpcErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        codes.ShouldNotBeEmpty();
        ServiceErrors.KnownCodes.ShouldBe(codes, ignoreOrder: true);
    }
}
