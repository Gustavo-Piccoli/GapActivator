using System.IO.Pipes;
using GapActivator.Application.Cycle;
using GapActivator.Ipc;
using GapActivator.Service.Ipc;
using GapActivator.Service.Tests.Support;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GapActivator.Service.Tests.Ipc;

/// <summary>The pipe server inside the real host: composition, startup order and a failing start.</summary>
[Trait("Category", "Integration")]
public sealed class IpcHostTests
{
    private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Host_IpcHostedServiceIsRegisteredAfterTheWorker()
    {
        await using var root = new TempDataRoot();
        using IHost host = ServiceHostFactory.CreateBuilder(root.Path, []).Build();

        Type[] hosted = [.. host.Services.GetServices<IHostedService>().Select(s => s.GetType())];

        hosted.ShouldContain(typeof(IpcHostedService));
        Array.IndexOf(hosted, typeof(IpcHostedService)).ShouldBeGreaterThan(Array.IndexOf(hosted, typeof(GapActivatorWorker)));
    }

    [Fact]
    public async Task Host_PipeNameFromConfiguration_IsUsed()
    {
        await using var root = new TempDataRoot();
        HostApplicationBuilder builder = ServiceHostFactory.CreateBuilder(root.Path, []);
        string name = builder.Configuration[IpcOptions.PipeNameKey].ShouldNotBeNull();
        using IHost host = builder.Build();

        name.ShouldStartWith("GapActivator.Tests.");
        host.Services.GetRequiredService<IpcOptions>().PipeName.ShouldBe(name);
    }

    [Theory]
    [InlineData("--GapActivator:Ipc:PipeName= ")]
    [InlineData(@"--GapActivator:Ipc:PipeName=a\b")]
    [InlineData("--GapActivator:Authorization:OperatorsGroup=Sometimes")]
    public async Task Host_InvalidPipeNameOrOperatorsSetting_FailsToBuild(string argument)
    {
        await using var root = new TempDataRoot();
        string[] args = ["--GapActivator:DataRoot=" + root.Path, argument];
        var reported = new List<Exception>();

        Should.Throw<InvalidOperationException>(() => GapActivatorHost.CreateBuilder(args));
        int exitCode = await ServiceEntry.RunAsync(args, reported.Add, TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ServiceExitCodes.Fatal);
        reported.ShouldHaveSingleItem().ShouldBeOfType<InvalidOperationException>();
    }

    // Ruling 6: a pipe name that is already taken is a loud, fatal start, not a silent service without a pipe.
    [Fact]
    public async Task Host_PipeNameAlreadyTaken_LogsCriticalSetsExitCode1AndStops()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var root = new TempDataRoot();
        var exitCodes = new List<int>();
        var logs = new LogCapture();
        HostApplicationBuilder builder = ServiceHostFactory.CreateBuilder(root.Path, exitCodes, logs);
        string name = builder.Configuration[IpcOptions.PipeNameKey].ShouldNotBeNull();
        await using var squatter = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        using (IHost host = builder.Build())
        {
            try
            {
                await host.StartAsync(ct);
                await ServiceHostFactory.WaitForStoppingAsync(host, StartWait, ct);
            }
            finally
            {
                await ServiceHostFactory.StopAsync(host);
            }
        }

        exitCodes.ShouldContain(ServiceExitCodes.Fatal);
        logs.Entries.ShouldContain(e => e.Level == LogLevel.Critical && e.Category == typeof(IpcHostedService).FullName);
    }

    [Fact]
    public async Task Host_Started_AnswersHelloOnItsPipeAfterReconcile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using var root = new TempDataRoot();
        await E2EFixture.SeedAsync(root.Path, ct);
        HostApplicationBuilder builder = ServiceHostFactory.CreateBuilder(root.Path, []);
        string name = builder.Configuration[IpcOptions.PipeNameKey].ShouldNotBeNull();

        using IHost host = builder.Build();
        try
        {
            await host.StartAsync(ct);
            await host.Services.GetRequiredService<SchedulerLoop>().Reconciled.WaitAsync(StartWait, ct);

            await using GapActivatorClient client = await GapActivatorClient.ConnectAsync(
                new GapActivatorClientOptions
                {
                    PipeName = name,
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                    ServiceName = "GapActivator.Tests.NotInstalled",
                },
                ct);

            client.ShouldNotBeNull();
        }
        finally
        {
            await ServiceHostFactory.StopAsync(host);
        }
    }
}
