using GapActivator.App.Updates;

namespace GapActivator.App.Tests.Fakes;

/// <summary>An update checker that returns a set result (or throws) and lets a test raise <c>Checked</c> and <c>UpdateFound</c>.</summary>
internal sealed class FakeUpdateChecker : IUpdateChecker
{
    public UpdateCheckResult? Last { get; set; }

    public UpdateCheckResult Result { get; set; } = new(UpdateCheckOutcome.UpToDate, new Version(0, 1, 0), null);

    public Exception? Failure { get; set; }

    public int Checks { get; private set; }

    public event EventHandler<UpdateCheckResult>? UpdateFound;

    public event EventHandler<UpdateCheckResult>? Checked;

    public Task<UpdateCheckResult> CheckNowAsync(CancellationToken ct)
    {
        Checks++;
        if (Failure is { } failure)
        {
            return Task.FromException<UpdateCheckResult>(failure);
        }

        Last = Result;
        Checked?.Invoke(this, Result);
        if (Result.Outcome == UpdateCheckOutcome.UpdateAvailable)
        {
            UpdateFound?.Invoke(this, Result);
        }

        return Task.FromResult(Result);
    }

    public void RaiseChecked(UpdateCheckResult result)
    {
        Last = result;
        Checked?.Invoke(this, result);
    }

    public void RaiseUpdateFound(UpdateCheckResult result)
    {
        Last = result;
        UpdateFound?.Invoke(this, result);
    }
}
