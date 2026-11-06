namespace GapActivator.App.Updates;

public enum UpdateCheckOutcome
{
    UpdateAvailable,
    UpToDate,
    NoReleases,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, Version? LatestVersion, Uri? ReleaseUrl);
