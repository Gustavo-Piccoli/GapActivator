using System.Text;
using GapActivator.Application.Ports;
using GapActivator.Domain.Runs;

namespace GapActivator.Infrastructure.Files;

/// <summary>
/// Reads the tail of a run's output file. The file may still be open for writing by the run output pump, so it is opened
/// with full sharing.
/// </summary>
internal sealed class RunOutputReader(GapActivatorPaths paths) : IRunOutputReader
{
    private const int MaxContinuationBytes = 3;

    private static readonly UTF8Encoding Utf8 = new(false, throwOnInvalidBytes: false);

    public async Task<RunOutput> ReadTailAsync(RunId runId, int maxBytes, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        FileStream file;
        try
        {
            file = new FileStream(
                paths.RunOutputFile(runId),
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                useAsync: true);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new RunOutput(false, string.Empty, false);
        }

        await using (file.ConfigureAwait(false))
        {
            long length = file.Length;
            bool truncated = length > maxBytes;
            int count = (int)Math.Min(length, maxBytes);
            file.Seek(length - count, SeekOrigin.Begin);

            var buffer = new byte[count];
            int read = await file.ReadAtLeastAsync(buffer, count, throwOnEndOfStream: false, ct).ConfigureAwait(false);

            int start = 0;
            if (truncated)
            {
                while (start < read && start < MaxContinuationBytes && (buffer[start] & 0xC0) == 0x80)
                {
                    start++;
                }
            }

            return new RunOutput(true, Utf8.GetString(buffer, start, read - start), truncated);
        }
    }
}
