namespace GapActivator.Application.Messaging;

public static class RejectionCodes
{
    public const string NotFound = "not-found";
    public const string Unauthorized = "unauthorized";
    public const string NameTaken = "name-taken";
    public const string ScheduleInPast = "schedule-in-past";
    public const string SecretValueRequired = "secret-value-required";
    public const string HasActiveRun = "has-active-run";
    public const string RunAlreadyActive = "run-already-active";
    public const string RunNotActive = "run-not-active";
    public const string ProcessBlocked = "process-blocked";
    public const string ProcessDisabled = "process-disabled";
    public const string InstallationPaused = "installation-paused";
    public const string UseDisable = "use-disable";
    public const string EmailTestFailed = "email-test-failed";
}
