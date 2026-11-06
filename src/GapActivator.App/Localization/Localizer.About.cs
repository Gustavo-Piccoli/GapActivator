namespace GapActivator.App.Localization;

public sealed partial class Localizer
{
    // Fixed strings of the plan.
    public string AboutChecking => T("Checking…", "Verificando…");

    public string AboutUpToDate => T("You have the latest version.", "Você tem a versão mais recente.");

    public string AboutNoReleases => T("No release has been published yet.", "Nenhuma versão foi publicada ainda.");

    public string AboutCheckFailed => T("Could not check for updates.", "Não foi possível verificar atualizações.");

    public string AboutUpdateAvailable(string version) =>
        string.Format(Culture, T("Version {0} is available.", "A versão {0} está disponível."), version);

    public string UpdateAvailableNotice(string version) =>
        string.Format(Culture, T("Update {0} available", "Atualização {0} disponível"), version);

    public string AboutDevelopmentNote => T(
        "The connected service is not the installed GapActivator service (development mode).",
        "O serviço conectado não é o serviço GapActivator instalado (modo de desenvolvimento).");

    // The rest of the page.
    public string AboutVersion(string version) => string.Format(Culture, T("Version {0}", "Versão {0}"), version);

    public string AboutServiceVersion(string version) =>
        string.Format(Culture, T("Service version {0}", "Versão do serviço {0}"), version);

    public string AboutUpdatesLabel => T("Updates", "Atualizações");

    public string AboutCheckNow => T("Check now", "Verificar agora");

    public string AboutOpenRelease => T("Open the release page", "Abrir a página da versão");

    public string AboutOpenReleaseName(string version) =>
        string.Format(Culture, T("Open the release page for version {0} on GitHub", "Abrir no GitHub a página da versão {0}"), version);

    public string AboutRepositoryLabel => T("Source code", "Código-fonte");

    public string AboutOpenRepository => T("Open on GitHub", "Abrir no GitHub");

    public string AboutOpenRepositoryName => T("Open the GapActivator repository on GitHub", "Abrir o repositório do GapActivator no GitHub");

    public string AboutFilesLabel => T("Where things are kept", "Onde ficam os arquivos");

    public string AboutServiceDataLabel => T("Service data", "Dados do serviço");

    public string AboutServiceLogsLabel => T("Service logs", "Logs do serviço");

    public string AboutAppSettingsLabel => T("App settings", "Configurações do aplicativo");

    public string AboutAppLogsLabel => T("App logs", "Logs do aplicativo");

    public string AboutOpenFolder => T("Open folder", "Abrir pasta");

    public string AboutOpenFolderName(string label) =>
        string.Format(Culture, T("Open the folder: {0}", "Abrir a pasta: {0}"), label);

    public string AboutFolderMissing => T("The folder does not exist yet.", "A pasta ainda não existe.");

    public string AboutCouldNotOpen => T("Could not open it.", "Não foi possível abrir.");

    public string AboutNotChecked => T("Not checked yet.", "Ainda não verificado.");

    public string AboutAutomaticChecksOff => T("Automatic checks are off.", "As verificações automáticas estão desligadas.");

    public string AboutCouldNotOpenPage => T("Could not open the page.", "Não foi possível abrir a página.");

    public string UpdateAvailableNoticeName(string version) =>
        string.Format(Culture, T("Update {0} available. Open About.", "Atualização {0} disponível. Abrir Sobre."), version);

    public string AboutServiceNote => T(
        "These are the service's default folders. A service started for development may keep its files elsewhere.",
        "Estas são as pastas padrão do serviço. Um serviço iniciado para desenvolvimento pode guardar os arquivos em outro lugar.");
}
