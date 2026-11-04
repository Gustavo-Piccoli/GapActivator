using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GapActivator.Infrastructure.Tests.Support;

/// <summary>
/// Throws after a command whose text contains the fragment has executed, so that statement really ran inside the
/// transaction and the failure proves the rollback. Records the text of every command executed, in order.
/// </summary>
internal sealed class FailingCommandInterceptor(string commandTextFragment) : DbCommandInterceptor
{
    private readonly List<string> _executed = [];
    private readonly Lock _sync = new();

    public IReadOnlyList<string> Executed
    {
        get
        {
            lock (_sync)
            {
                return [.. _executed];
            }
        }
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Record(command))
        {
            result.Dispose();
            throw Failure();
        }

        return result;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Record(command))
        {
            await result.DisposeAsync();
            throw Failure();
        }

        return result;
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result) =>
        Record(command) ? throw Failure() : result;

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default) =>
        Record(command) ? throw Failure() : ValueTask.FromResult(result);

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result) =>
        Record(command) ? throw Failure() : result;

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default) =>
        Record(command) ? throw Failure() : ValueTask.FromResult(result);

    private bool Record(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_sync)
        {
            _executed.Add(command.CommandText);
        }

        return command.CommandText.Contains(commandTextFragment, StringComparison.Ordinal);
    }

    private InvalidOperationException Failure() =>
        new($"Simulated failure of a command containing '{commandTextFragment}'.");
}
