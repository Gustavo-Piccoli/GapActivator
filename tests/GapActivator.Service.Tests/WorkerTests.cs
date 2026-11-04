using GapActivator.Application.Cycle;
using GapActivator.Application.Messaging;
using GapActivator.Infrastructure;
using GapActivator.Service.Tests.Support;
using GapActivator.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GapActivator.Service.Tests;

[Trait("Category", "Integration")]
public sealed class WorkerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly string WorkerCategory = typeof(GapActivatorWorker).FullName!;
    private static readonly string LoopCategory = typeof(SchedulerLoop).FullName!;

    [Fact]
    public async Task Worker_DataRootIsAFile_LogsCriticalSetsExitCode1AndStopsHost()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var root = new TempDataRoot();
        string file = Path.Combine(root.Path, "not-a-folder");
        await File.WriteAllTextAsync(file, "x", ct);
        var exitCodes = new List<int>();
        var logs = new LogCapture();
        try
        {
            using IHost host = ServiceHostFactory.CreateBuilder(file, exitCodes, logs).Build();

            await host.StartAsync(ct);
            await ServiceHostFactory.WaitForStoppingAsync(host, Timeout, ct);
            await host.StopAsync(ct);

            exitCodes.ShouldBe([ServiceExitCodes.Fatal]);
            CapturedLog critical = logs.Entries.Where(e => e.Category == WorkerCategory && e.Level == LogLevel.Critical).ShouldHaveSingleItem();
            critical.Exception.ShouldBeOfType<IOException>();
        }
        finally
        {
            Environment.ExitCode = 0;
        }
    }

    // C18: SchedulerLoop already logged the failed reconciliation at Critical; the worker does not log it again.
    [Fact]
    public async Task Worker_ReconcileFails_SetsExitCode1WithoutASecondCriticalAndStopsHost()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var root = new TempDataRoot();
        var exitCodes = new List<int>();
        var logs = new LogCapture();
        try
        {
            HostApplicationBuilder builder = ServiceHostFactory.CreateBuilder(root.Path, exitCodes, logs);
            builder.Services.AddScoped<ICommandHandler<ReconcileOnStartup, Unit>, FailingReconcile>();
            using IHost host = builder.Build();
            SchedulerLoop loop = host.Services.GetRequiredService<SchedulerLoop>();

            await host.StartAsync(ct);
            await ServiceHostFactory.WaitForStoppingAsync(host, Timeout, ct);
            await host.StopAsync(ct);

            exitCodes.ShouldBe([ServiceExitCodes.Fatal]);
            loop.Reconciled.IsFaulted.ShouldBeTrue();
            logs.Entries.Where(e => e.Category == LoopCategory && e.Level == LogLevel.Critical).ShouldHaveSingleItem();
            logs.Entries.ShouldNotContain(e => e.Category == WorkerCategory && e.Level == LogLevel.Critical);
        }
        finally
        {
            Environment.ExitCode = 0;
            SqliteConnection.ClearAllPools();
        }
    }

    // BI-073, BI-091
    [Fact]
    public async Task Worker_Started_InitializesDatabaseThenReconcilesBeforeTheFirstCycle()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var root = new TempDataRoot();
        var exitCodes = new List<int>();
        var logs = new LogCapture();
        try
        {
            using IHost host = ServiceHostFactory.CreateBuilder(root.Path, exitCodes, logs).Build();
            SchedulerLoop loop = host.Services.GetRequiredService<SchedulerLoop>();

            await host.StartAsync(ct);
            await loop.Reconciled.WaitAsync(Timeout, ct);

            File.Exists(new GapActivatorPaths(root.Path).DatabaseFile).ShouldBeTrue();

            await host.StopAsync(ct);

            exitCodes.ShouldBeEmpty();
            logs.Entries.ShouldNotContain(e => e.Level == LogLevel.Critical);
        }
        finally
        {
            Environment.ExitCode = 0;
            SqliteConnection.ClearAllPools();
        }
    }

    private sealed class FailingReconcile : ICommandHandler<ReconcileOnStartup, Unit>
    {
        public Task<Unit> HandleAsync(ReconcileOnStartup command, CancellationToken ct) =>
            throw new InvalidOperationException("Reconciliation failed for the test.");
    }
}
