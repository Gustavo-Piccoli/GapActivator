using System.Net;
using GapActivator.App.Settings;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.Updates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.Updates;

public class UpdateCheckerTests
{
    private static readonly Uri Page = new("https://github.com/Gustavo-Piccoli/GapActivator/releases/tag/v0.2.0");

    private readonly FakeTimeProvider _time = new(Dtos.Epoch);
    private readonly FakeReleaseSource _source = new() { Release = new ReleaseInfo("v0.2.0", Page) };
    private readonly FakeAppSettings _settings = new();
    private readonly ListLogger<UpdateChecker> _logger = new();

    private UpdateChecker Create() => new(_source, new Version(0, 1, 0), _settings, _time, _logger);

    /// <summary>Completes when the settings are written next, which the checker does last in a background check.</summary>
    private Task<AppSettings> NextSettingsChange()
    {
        var done = new TaskCompletionSource<AppSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        _settings.Changed += (_, s) => done.TrySetResult(s);
        return done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Intervals_AreADayAnHourAndTenSeconds()
    {
        UpdateChecker.CheckInterval.ShouldBe(TimeSpan.FromHours(24));
        UpdateChecker.TickInterval.ShouldBe(TimeSpan.FromHours(1));
        UpdateChecker.Timeout.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Background_CheckedLessThanADayAgo_DoesNotCheck()
    {
        _settings.Update(s => s with { LastUpdateCheck = _time.GetUtcNow() - TimeSpan.FromHours(23) });
        using var checker = Create();

        checker.StartBackground();
        _time.Advance(TimeSpan.FromMinutes(30));

        _source.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Background_Due_ChecksOnceAndRecordsTheTime()
    {
        var start = _time.GetUtcNow();
        _settings.Update(s => s with { LastUpdateCheck = start - TimeSpan.FromHours(25) });
        using var checker = Create();
        var changed = NextSettingsChange();

        checker.StartBackground();
        checker.StartBackground();
        var settings = await changed;
        _time.Advance(TimeSpan.FromHours(1));

        _source.Calls.ShouldBe(1);
        settings.LastUpdateCheck.ShouldBe(start);
    }

    [Fact]
    public async Task Background_NeverChecked_ChecksAtStart()
    {
        using var checker = Create();
        var changed = NextSettingsChange();

        checker.StartBackground();
        await changed;

        _source.Calls.ShouldBe(1);
        _settings.Current.LastUpdateCheck.ShouldNotBeNull();
    }

    [Fact]
    public void Background_Disabled_NeverChecks()
    {
        _settings.Update(s => s with { CheckForUpdates = false });
        using var checker = Create();

        checker.StartBackground();
        _time.Advance(TimeSpan.FromHours(30));

        _source.Calls.ShouldBe(0);
        _settings.Current.LastUpdateCheck.ShouldBeNull();
    }

    [Fact]
    public async Task Background_Failure_IsSilentAndStillRecordsTheAttempt()
    {
        _source.Failure = new HttpRequestException("offline");
        using var checker = Create();
        var raised = false;
        checker.UpdateFound += (_, _) => raised = true;
        var changed = NextSettingsChange();

        checker.StartBackground();
        await changed;

        raised.ShouldBeFalse();
        _settings.Current.LastUpdateCheck.ShouldNotBeNull();
        _logger.Levels.ShouldBe([LogLevel.Information]);
        checker.Last.ShouldNotBeNull().Outcome.ShouldBe(UpdateCheckOutcome.Failed);
    }

    [Fact]
    public async Task CheckNow_AlwaysChecks()
    {
        _settings.Update(s => s with { CheckForUpdates = false, LastUpdateCheck = _time.GetUtcNow() });
        using var checker = Create();
        _time.Advance(TimeSpan.FromMinutes(5));

        var result = await checker.CheckNowAsync(TestContext.Current.CancellationToken);

        _source.Calls.ShouldBe(1);
        result.Outcome.ShouldBe(UpdateCheckOutcome.UpdateAvailable);
        _settings.Current.LastUpdateCheck.ShouldBe(_time.GetUtcNow());
        checker.Last.ShouldBe(result);
    }

    [Fact]
    public async Task CheckNow_NewerTag_RaisesUpdateFound()
    {
        using var checker = Create();
        UpdateCheckResult? found = null;
        checker.UpdateFound += (_, r) => found = r;

        await checker.CheckNowAsync(TestContext.Current.CancellationToken);

        var result = found.ShouldNotBeNull();
        result.LatestVersion.ShouldBe(new Version(0, 2, 0));
        result.ReleaseUrl.ShouldBe(Page);
    }

    [Theory]
    [InlineData("v0.1.0", UpdateCheckOutcome.UpToDate)]
    [InlineData("v0.0.9", UpdateCheckOutcome.UpToDate)]
    [InlineData("nonsense", UpdateCheckOutcome.Failed)]
    public async Task CheckNow_OtherTags_DoNotRaiseUpdateFound(string tag, UpdateCheckOutcome outcome)
    {
        _source.Release = new ReleaseInfo(tag, Page);
        using var checker = Create();
        var raised = false;
        checker.UpdateFound += (_, _) => raised = true;

        var result = await checker.CheckNowAsync(TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(outcome);
        raised.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckNow_NoRelease_ReportsNoReleases()
    {
        _source.Release = null;
        using var checker = Create();

        var result = await checker.CheckNowAsync(TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(UpdateCheckOutcome.NoReleases);
    }

    [Fact]
    public async Task CheckNow_Failure_IsFailedAndLoggedAtInformation()
    {
        _source.Failure = new HttpRequestException("offline");
        using var checker = Create();

        var result = await checker.CheckNowAsync(TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(UpdateCheckOutcome.Failed);
        _logger.Levels.ShouldBe([LogLevel.Information]);
    }

    [Fact]
    public async Task CheckNow_CallerCancels_Throws()
    {
        using var checker = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _source.Failure = new OperationCanceledException(cts.Token);

        await Should.ThrowAsync<OperationCanceledException>(() => checker.CheckNowAsync(cts.Token));
    }

    [Fact]
    public async Task Dispose_InFlightScheduledCheck_CancelsItAndStaysSilent()
    {
        _source.Hang = true;
        var checker = Create();
        var raised = 0;
        checker.UpdateFound += (_, _) => raised++;
        checker.Checked += (_, _) => raised++;

        checker.StartBackground();
        await _source.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        checker.Dispose();
        await _source.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        // The scheduled check has finished once it is no longer running; only then can "nothing happened" be asserted.
        while (checker.ScheduledCheckRunning)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        _settings.Updates.ShouldBe(0);
        raised.ShouldBe(0);
        _logger.Levels.ShouldBeEmpty();
    }

    [Fact]
    public async Task Checked_IsRaisedAfterEveryCheckBeforeUpdateFound()
    {
        using var checker = Create();
        var order = new List<string>();
        checker.Checked += (_, r) => order.Add("checked:" + r.Outcome);
        checker.UpdateFound += (_, _) => order.Add("found");

        await checker.CheckNowAsync(TestContext.Current.CancellationToken);
        _source.Release = new ReleaseInfo("v0.1.0", Page);
        await checker.CheckNowAsync(TestContext.Current.CancellationToken);

        order.ShouldBe(["checked:UpdateAvailable", "found", "checked:UpToDate"]);
    }

    [Fact]
    public async Task Checked_FailureIsRaisedToo()
    {
        _source.Failure = new HttpRequestException("offline");
        using var checker = Create();
        UpdateCheckResult? seen = null;
        checker.Checked += (_, r) => seen = r;

        await checker.CheckNowAsync(TestContext.Current.CancellationToken);

        seen.ShouldNotBeNull().Outcome.ShouldBe(UpdateCheckOutcome.Failed);
    }

    [Fact]
    public async Task CheckNow_PageOfAnotherRepository_IsFailed()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """{"tag_name":"v9.0.0","html_url":"https://github.com/someone-else/repo/releases/tag/v9.0.0"}""");
        using var checker = new UpdateChecker(new GitHubReleaseSource(new HttpClient(handler), "0.1.0"), new Version(0, 1, 0), _settings, _time, _logger);

        var result = await checker.CheckNowAsync(TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(UpdateCheckOutcome.Failed);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "")]
    [InlineData(HttpStatusCode.OK, "{ not json")]
    public async Task CheckNow_RateLimitedOrMalformed_IsFailedAndLoggedAtInformation(HttpStatusCode status, string body)
    {
        using var checker = new UpdateChecker(new GitHubReleaseSource(new HttpClient(new StubHttpHandler(status, body)), "0.1.0"), new Version(0, 1, 0), _settings, _time, _logger);

        var result = await checker.CheckNowAsync(TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(UpdateCheckOutcome.Failed);
        _logger.Levels.ShouldBe([LogLevel.Information]);
    }

    [Fact]
    public async Task CheckNow_SourceNeverAnswers_FailsAfterTheTimeout()
    {
        _source.Hang = true;
        using var checker = Create();

        var running = checker.CheckNowAsync(TestContext.Current.CancellationToken);
        await _source.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        running.IsCompleted.ShouldBeFalse();
        _time.Advance(UpdateChecker.Timeout);
        var result = await running.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(UpdateCheckOutcome.Failed);
        _logger.Levels.ShouldBe([LogLevel.Information]);
        _settings.Current.LastUpdateCheck.ShouldNotBeNull();
    }

    [Fact]
    public async Task CheckNow_PrereleaseIsNotOffered()
    {
        var body = """{"tag_name":"v9.0.0","html_url":"https://github.com/Gustavo-Piccoli/GapActivator/releases/tag/v9.0.0","prerelease":true}""";
        using var checker = new UpdateChecker(new GitHubReleaseSource(new HttpClient(new StubHttpHandler(HttpStatusCode.OK, body)), "0.1.0"), new Version(0, 1, 0), _settings, _time, _logger);
        var raised = false;
        checker.UpdateFound += (_, _) => raised = true;

        var result = await checker.CheckNowAsync(TestContext.Current.CancellationToken);

        result.Outcome.ShouldBe(UpdateCheckOutcome.NoReleases);
        raised.ShouldBeFalse();
    }
}
