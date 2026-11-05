namespace GapActivator.App.Localization;

public sealed partial class Localizer
{
    public string BannerConnecting => T("Connecting to the service…", "Conectando ao serviço…");

    public string BannerStopped => T("The GapActivator service is stopped.", "O serviço GapActivator está parado.");

    public string BannerNotInstalled => T("The GapActivator service is not installed.", "O serviço GapActivator não está instalado.");

    public string BannerStarting => T("Starting the service…", "Iniciando o serviço…");

    public string BannerDevelopment => T(
        "Not connected to the installed service — development mode",
        "Sem conexão com o serviço instalado — modo de desenvolvimento");

    public string StartService => T("Start service", "Iniciar serviço");

    public string StartServiceFailed(int code) =>
        string.Format(Culture, T("The service could not be started (error {0}).", "Não foi possível iniciar o serviço (erro {0})."), code);

    public string DeniedOperators => T(
        "Requires Administrators or GapActivator Operators",
        "Requer Administradores ou GapActivator Operators");

    public string DeniedAdministratorsOnly => T("Administrators only", "Somente Administradores");

    public string DeniedDisconnected => T("Not connected to the service.", "Sem conexão com o serviço.");

    // Not in the Fixed strings: the main window's own labels.
    public string NavigationLabel => T("Main navigation", "Navegação principal");

    public string FooterConnected => T("Connected", "Conectado");

    public string FooterNotConnected => T("Not connected", "Sem conexão");
}
