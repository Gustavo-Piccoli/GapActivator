namespace GapActivator.App.Localization;

public sealed partial class Localizer
{
    public string ExecutableHintNoExpansion => T(
        "Environment variables such as %VAR% are not expanded. Enter the full path.",
        "Variáveis de ambiente como %VAR% não são expandidas. Informe o caminho completo.");

    public string WorkingDirectoryHint => T("Empty: the executable's folder.", "Vazio: a pasta do executável.");

    public string ServiceKindHint => T(
        "A service-kind process runs whenever it is enabled. It has no schedule and no time limit.",
        "Um processo do tipo serviço é executado sempre que está ativado. Ele não tem agendamento nem limite de tempo.");

    public string StoredSecretPlaceholder => T("•••••• (stored; type to replace)", "•••••• (salvo; digite para substituir)");

    public string SensitiveHiddenNote => T(
        "Arguments and environment values are hidden from viewers.",
        "Argumentos e valores de ambiente ficam ocultos para quem só visualiza.");

    public string RequiredName => T("Enter a name.", "Informe um nome.");

    public string RequiredExecutable => T("Enter the executable path.", "Informe o caminho do executável.");

    public string RequiredVariableName => T("Every variable needs a name.", "Toda variável precisa de um nome.");

    public string RequiredVariableValue(string name) =>
        string.Format(Culture, T("Enter a value for {0}.", "Informe um valor para {0}."), name);

    public string SharedExecutableTitle => T("Executable already in use", "Executável já em uso");

    public string SharedExecutableQuestion(string names) =>
        string.Format(Culture, T("Also used by: {0}. Save anyway?", "Também usado por: {0}. Salvar mesmo assim?"), names);

    public string SharedExecutableNote(string names) =>
        string.Format(Culture, T("Saved. Also used by: {0}.", "Salvo. Também usado por: {0}."), names);

    public string ScheduleNoLongerPending(string at) =>
        string.Format(Culture, T("No longer pending since {0}", "Não está mais pendente desde {0}"), at);

    // Not in the Fixed strings: titles, form labels, states and the texts of the buttons of the editor.
    public string EditProcessTitleBare => T("Edit process", "Editar processo");

    public string ScheduleProblemFooter => T(
        "The schedule could not be saved. Check the Schedules section.",
        "Não foi possível salvar o agendamento. Veja a seção Agendamentos.");

    public string EditorLoadFailed => T(
        "The process could not be loaded. Try again.",
        "Não foi possível carregar o processo. Tente novamente.");

    public string EditProcessTitle(string name) =>
        string.Format(Culture, T("Edit \"{0}\"", "Editar \"{0}\""), name);

    public string SectionGeneral => T("General", "Geral");

    public string SectionEnvironment => T("Environment variables", "Variáveis de ambiente");

    public string SectionLimits => T("Limits", "Limites");

    public string SectionSchedules => T("Schedules", "Agendamentos");

    public string NameLabel => T("Name", "Nome");

    public string DescriptionLabel => T("Description", "Descrição");

    public string KindLabel => T("Type", "Tipo");

    public string ExecutableLabel => T("Executable", "Executável");

    public string ArgumentsLabel => T("Arguments", "Argumentos");

    public string WorkingDirectoryLabel => T("Working directory", "Diretório de trabalho");

    public string Browse => T("Browse…", "Procurar…");

    public string BrowseTitle => T("Choose the executable", "Escolha o executável");

    public string BrowseExecutableFilter => T("Executables", "Executáveis");

    public string VariableNameLabel => T("Variable name", "Nome da variável");

    public string VariableValueLabel => T("Value", "Valor");

    public string SecretLabel => T("Secret (write-only)", "Segredo (somente gravação)");

    public string AddVariable => T("Add variable", "Adicionar variável");

    public string RemoveVariable => T("Remove", "Remover");

    public string RemoveVariableFor(string name) =>
        string.Format(Culture, T("Remove variable {0}", "Remover a variável {0}"), name);

    public string VariableValueFor(string name) =>
        string.Format(Culture, T("Value of {0}", "Valor de {0}"), name);

    public string SecretFor(string name) =>
        string.Format(Culture, T("{0} is a secret", "{0} é um segredo"), name);

    public string NewVariable => T("new variable", "nova variável");

    public string NoVariables => T("No variables.", "Nenhuma variável.");

    public string TimeLimitLabel => T("Time limit (seconds)", "Limite de tempo (segundos)");

    public string GracePeriodLabel => T("Grace period (seconds)", "Período de tolerância (segundos)");

    public string LimitsHint => T("Empty: the default from Settings.", "Vazio: o padrão das Configurações.");

    public string AddSchedule => T("Add schedule", "Adicionar agendamento");

    public string ApplySchedule => T("Apply schedule", "Aplicar agendamento");

    public string CancelSchedule => T("Discard schedule", "Descartar agendamento");

    public string EditScheduleFor(string summary) =>
        string.Format(Culture, T("Edit schedule: {0}", "Editar agendamento: {0}"), summary);

    public string RemoveScheduleFor(string summary) =>
        string.Format(Culture, T("Remove schedule: {0}", "Remover agendamento: {0}"), summary);

    public string NoSchedules => T(
        "No schedules. The process runs only when you start it.",
        "Nenhum agendamento. O processo só é executado quando você o inicia.");

    public string ScheduleChanged => T("Changed; saved with the process", "Alterado; gravado junto com o processo");

    public string FinishScheduleFirst => T(
        "Apply or discard the schedule you are editing before saving.",
        "Aplique ou descarte o agendamento em edição antes de salvar.");

    public string EditorLoading => T("Loading the process…", "Carregando o processo…");

    public string EditorSaving => T("Saving…", "Salvando…");

    public string TryAgain => T("Try again", "Tentar novamente");

    public string EditorKeptInput => T(
        "Nothing was lost: your entries are still here.",
        "Nada foi perdido: suas alterações continuam aqui.");

    public string EditLabelFor(string name) =>
        string.Format(Culture, T("Edit {0}", "Editar {0}"), name);
}
