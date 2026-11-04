using GapActivator.Application.Ports;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;

namespace GapActivator.Infrastructure.Tests.Notifications;

public sealed class LoggingAppNotifierTests
{
    [Theory]
    [InlineData(NotificationSeverity.Information, LogLevel.Information)]
    [InlineData(NotificationSeverity.Warning, LogLevel.Warning)]
    [InlineData(NotificationSeverity.Error, LogLevel.Error)]
    public async Task Notify_LogsAtTheNotificationSeverity(NotificationSeverity severity, LogLevel expected)
    {
        var logger = new RecordingLogger();
        var processId = ProcessId.New();
        var runId = new RunId(Guid.CreateVersion7());
        var notifier = new LoggingAppNotifier(logger);

        await notifier.NotifyAsync(
            new AppNotification(severity, "Run failed", "Exit code 2", processId, runId),
            TestContext.Current.CancellationToken);

        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Level.ShouldBe(expected);
        logger.Entries[0].Message.ShouldBe("Run failed: Exit code 2");
        logger.Entries[0].State.ShouldContain(new KeyValuePair<string, object?>("ProcessId", processId));
        logger.Entries[0].State.ShouldContain(new KeyValuePair<string, object?>("RunId", runId));
    }

    [Fact]
    public async Task Notify_WithoutProcessAndRun_StillLogs()
    {
        var logger = new RecordingLogger();

        await new LoggingAppNotifier(logger).NotifyAsync(
            new AppNotification(NotificationSeverity.Information, "Started", "Service started", null, null),
            TestContext.Current.CancellationToken);

        logger.Entries.Single().Message.ShouldBe("Started: Service started");
    }

    private sealed record Entry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> State);

    private sealed class RecordingLogger : ILogger<LoggingAppNotifier>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var pairs = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            Entries.Add(new Entry(logLevel, formatter(state, exception), pairs));
        }
    }
}
