using GapActivator.Application.Ports;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Settings;

namespace GapActivator.Testing;

/// <summary>Turns <c>x</c> into <c>enc:x</c>.</summary>
public sealed class FakeSecretProtector : ISecretProtector
{
    private const string Prefix = "enc:";

    public ProtectedSecret Protect(string plaintext) => new(Prefix + plaintext);

    public string Unprotect(ProtectedSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return secret.Ciphertext.StartsWith(Prefix, StringComparison.Ordinal)
            ? secret.Ciphertext[Prefix.Length..]
            : throw new InvalidOperationException("Secret was not produced by FakeSecretProtector.");
    }
}

public sealed class RecordingEmailSender : IEmailSender
{
    public List<(EmailAlertSettings Smtp, EmailMessage Message)> Sent { get; } = [];

    /// <summary>When set, every send is recorded and then fails with this exception.</summary>
    public Exception? Failure { get; set; }

    public Task SendAsync(EmailAlertSettings smtp, EmailMessage message, CancellationToken ct)
    {
        lock (Sent)
        {
            Sent.Add((smtp, message));
        }

        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }
}

public sealed class RecordingAppNotifier : IAppNotifier
{
    public List<AppNotification> Notifications { get; } = [];

    public Task NotifyAsync(AppNotification notification, CancellationToken ct)
    {
        lock (Notifications)
        {
            Notifications.Add(notification);
        }

        return Task.CompletedTask;
    }
}

public sealed class FakeServiceControl : IServiceControl
{
    public int RestartCount { get; private set; }

    public Task RestartAsync(CancellationToken ct)
    {
        RestartCount++;
        return Task.CompletedTask;
    }
}

/// <summary>Records each purge in <see cref="InMemoryStore.LogRetentionDays"/> and can be told to throw.</summary>
public sealed class FakeLogRetention(InMemoryStore store) : ILogRetention
{
    /// <summary>When set, every purge is recorded and then fails with this exception.</summary>
    public Exception? Failure { get; set; }

    public Task PurgeOlderThanAsync(int days, CancellationToken ct)
    {
        store.LogRetentionDays.Add(days);
        return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }
}

public sealed class FakeRunOutputReader : IRunOutputReader
{
    public Dictionary<RunId, RunOutput> Outputs { get; } = [];

    public Task<RunOutput> ReadTailAsync(RunId runId, int maxBytes, CancellationToken ct) =>
        Task.FromResult(Outputs.GetValueOrDefault(runId) ?? new RunOutput(false, string.Empty, false));
}
