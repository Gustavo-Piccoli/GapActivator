using GapActivator.Domain.Runs;

namespace GapActivator.Application.Ports;

public sealed record RunOutput(bool Exists, string Text, bool Truncated);

public interface IRunOutputReader
{
    Task<RunOutput> ReadTailAsync(RunId runId, int maxBytes, CancellationToken ct);
}
