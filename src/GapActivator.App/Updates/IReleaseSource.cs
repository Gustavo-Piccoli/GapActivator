namespace GapActivator.App.Updates;

/// <summary>Port that looks up the latest published release.</summary>
public interface IReleaseSource
{
    /// <summary>
    /// Returns the latest release, or <see langword="null"/> when the repository has none.
    /// Throws for transport failures and unusable payloads.
    /// </summary>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken);
}
