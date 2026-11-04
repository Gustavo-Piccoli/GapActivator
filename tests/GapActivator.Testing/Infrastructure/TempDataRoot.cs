namespace GapActivator.Testing;

/// <summary>
/// An empty, unique folder under <c>%TEMP%\GapActivator.Tests</c> that integration tests use instead of the real
/// <c>%ProgramData%</c>. Callers that opened SQLite connections must release the connection pools before disposing it.
/// </summary>
public sealed class TempDataRoot : IAsyncDisposable
{
    private const int DeleteRetries = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    public TempDataRoot()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GapActivator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Deletes the folder, retrying while files are still in use; never throws.</summary>
    public async ValueTask DisposeAsync()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }

                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == DeleteRetries)
                {
                    return;
                }
            }

            await Task.Delay(RetryDelay).ConfigureAwait(false);
        }
    }
}
