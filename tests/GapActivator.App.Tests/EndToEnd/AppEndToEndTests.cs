using System.Diagnostics;
using System.Reflection;
using GapActivator.App.Services;
using GapActivator.App.Shell;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.Application.Messaging;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.App.Tests.EndToEnd;

/// <summary>
/// The desktop app's view models against the real GapActivator host, in-process, over a real named pipe and the real sample
/// child (done-when ruling 16). Nothing creates the shell window or a tray icon. Real pipes and child processes make these
/// integration tests; they are not part of <c>Category!=Integration</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AppEndToEndTests
{
    private static readonly TimeSpan ConnectWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReconnectWait = TimeSpan.FromSeconds(15);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RegisterRunNowAndHistory_ThroughTheViewModels_ShowsSucceededWithOutput()
    {
        using var localAppData = new TempDirectory();
        await using AppE2EHost host = await AppE2EHost.StartAsync(Ct);
        await using ServiceProvider app = host.ComposeApp(localAppData.Path);
        StartApp(app);
        IServiceGateway gateway = app.GetRequiredService<IServiceGateway>();

        await Eventually(() => gateway.Status.State == ConnectionState.Connected, ConnectWait, "The gateway did not connect.");
        gateway.Status.ServerVerified.ShouldBeFalse();
        gateway.Status.Caller!.CanChange.ShouldBeTrue();

        ProcessesViewModel processes = app.GetRequiredService<ProcessesViewModel>();
        await RegisterThroughTheEditorAsync(processes, "Sample via app", "1");

        await Eventually(() => processes.Items.Any(i => i.Name == "Sample via app"), TimeSpan.FromSeconds(5), "The process did not reach the list.");
        ProcessRowViewModel row = processes.Items.First(i => i.Name == "Sample via app");
        await row.RunNowCommand.ExecuteAsync(null);

        HistoryViewModel history = app.GetRequiredService<HistoryViewModel>();
        history.SelectedProcess = history.ProcessOptions.First(o => o.Name == "Sample via app");
        await Eventually(
            async () =>
            {
                await history.ApplyCommand.ExecuteAsync(null);
                return history.Items.Count > 0 && history.Items[0].StatusText == "Succeeded";
            },
            TimeSpan.FromSeconds(30),
            "The run did not show as Succeeded.",
            TimeSpan.FromMilliseconds(500));

        await history.OpenRunCommand.ExecuteAsync(history.Items[0]);
        RunDetailViewModel detail = history.Detail!;
        detail.Output.ShouldContain("Done.");
        detail.Output.ShouldContain($"Started by GapActivator (PID {Environment.ProcessId}).");
        detail.ExitCodeText.ShouldBe("0");
        history.CloseDetailCommand.Execute(null);
    }

    [Fact]
    public async Task ServiceStopsAndStartsAgain_GatewayReconnectsAndTheListReturns()
    {
        using var localAppData = new TempDirectory();
        await using AppE2EHost first = await AppE2EHost.StartAsync(Ct);
        await using ServiceProvider app = first.ComposeApp(localAppData.Path);
        StartApp(app);
        IServiceGateway gateway = app.GetRequiredService<IServiceGateway>();
        await Eventually(() => gateway.Status.IsConnected, ConnectWait, "The gateway did not connect.");

        ProcessesViewModel processes = app.GetRequiredService<ProcessesViewModel>();
        await RegisterThroughTheEditorAsync(processes, "Sample kept", "1");
        LiveState live = app.GetRequiredService<LiveState>();
        await Eventually(() => live.Processes.Any(p => p.Name == "Sample kept"), TimeSpan.FromSeconds(5), "The process did not reach the live state.");

        await first.StopAsync();
        await Eventually(() => !gateway.Status.IsConnected, TimeSpan.FromSeconds(5), "The gateway stayed connected after the host stopped.");
        TrayIconState tray = TrayCoordinator.Compute(
            gateway.Status, live.Dashboard, live.Processes, app.GetRequiredService<FailureAcknowledgements>());
        tray.ShouldBe(TrayIconState.Unreachable);

        // The second host reuses the data root and the pipe; it neither seeds nor deletes the root, and is disposed first.
        await using AppE2EHost second = await AppE2EHost.StartAsync(Ct, first.PipeName, first.DataRoot);
        await Eventually(() => gateway.Status.IsConnected, ReconnectWait, "The gateway did not reconnect.");
        await Eventually(() => live.Processes.Any(p => p.Name == "Sample kept"), TimeSpan.FromSeconds(5), "The list did not return.");
    }

    [Fact]
    public async Task ServiceRestartedRightAfterAReconnect_GatewayReturnsToConnected()
    {
        using var localAppData = new TempDirectory();
        await using AppE2EHost first = await AppE2EHost.StartAsync(Ct);
        await using ServiceProvider app = first.ComposeApp(localAppData.Path);
        StartApp(app);
        IServiceGateway gateway = app.GetRequiredService<IServiceGateway>();
        await Eventually(() => gateway.Status.IsConnected, ConnectWait, "The gateway did not connect.");

        await first.StopAsync();
        await Eventually(() => !gateway.Status.IsConnected, TimeSpan.FromSeconds(5), "The gateway stayed connected after the first stop.");

        await using AppE2EHost second = await AppE2EHost.StartAsync(Ct, first.PipeName, first.DataRoot);
        await Eventually(() => gateway.Status.IsConnected, ReconnectWait, "The gateway did not reconnect to the second host.");

        // Stop it again at once and bring up a third host on the same root and pipe: the same gateway must find it.
        await second.StopAsync();
        await using AppE2EHost third = await AppE2EHost.StartAsync(Ct, first.PipeName, first.DataRoot);
        await Eventually(() => gateway.Status.IsConnected, ReconnectWait, "The gateway did not return to Connected after the quick restart.");
    }

    [Fact]
    public async Task Viewer_EditorHidesArgumentsAndCannotSave()
    {
        using var operatorData = new TempDirectory();
        using var viewerData = new TempDirectory();
        await using AppE2EHost host = await AppE2EHost.StartAsync(Ct);
        await using ServiceProvider operatorApp = host.ComposeApp(operatorData.Path);
        StartApp(operatorApp);
        IServiceGateway operatorGateway = operatorApp.GetRequiredService<IServiceGateway>();
        await Eventually(() => operatorGateway.Status.CanChange, ConnectWait, "The operator did not connect.");

        using ProcessEditorViewModel editor = operatorApp.GetRequiredService<Func<ProcessEditorViewModel>>()();
        editor.LoadNew();
        editor.Name = "Sample secret";
        editor.Executable = $"  \"{ChildPaths.SampleChild}\"  ";
        editor.Arguments = "--secret-arg";
        editor.AddVariableCommand.Execute(null);
        editor.Environment[0].Name = "SAMPLE_MODE";
        editor.Environment[0].Value = "fast";
        await editor.SaveCommand.ExecuteAsync(null);
        editor.ErrorText.ShouldBeNull();
        LiveState operatorLive = operatorApp.GetRequiredService<LiveState>();
        await Eventually(() => operatorLive.Processes.Any(p => p.Name == "Sample secret"), TimeSpan.FromSeconds(5), "The process did not reach the list.");
        Guid id = operatorLive.Processes.First(p => p.Name == "Sample secret").Id;

        host.Callers.Next.Enqueue(Caller.Viewer(@"TEST\viewer"));
        await using ServiceProvider viewerApp = host.ComposeApp(viewerData.Path);
        StartApp(viewerApp);
        IServiceGateway viewerGateway = viewerApp.GetRequiredService<IServiceGateway>();
        await Eventually(() => viewerGateway.Status.IsConnected, ConnectWait, "The viewer did not connect.");
        viewerGateway.Status.CanChange.ShouldBeFalse();

        using ProcessEditorViewModel viewerEditor = viewerApp.GetRequiredService<Func<ProcessEditorViewModel>>()();
        await viewerEditor.LoadAsync(id);

        viewerEditor.LoadFailed.ShouldBeFalse();
        viewerEditor.ArgumentsHidden.ShouldBeTrue();
        viewerEditor.Environment.Count.ShouldBe(1);
        viewerEditor.Environment[0].Value.ShouldBe("Hidden");
        viewerEditor.IsReadOnly.ShouldBeTrue();
        viewerEditor.SaveCommand.CanExecute(null).ShouldBeFalse();
        StringsOf(viewerEditor).ShouldNotContain(s => s.Contains("--secret-arg", StringComparison.Ordinal));
        StringsOf(viewerEditor.Environment[0]).ShouldNotContain(s => s.Contains("fast", StringComparison.Ordinal));
    }

    private static void StartApp(ServiceProvider app)
    {
        app.GetRequiredService<IServiceGateway>().Start();
        app.GetRequiredService<LiveState>().Start();
    }

    private static async Task RegisterThroughTheEditorAsync(ProcessesViewModel processes, string name, string arguments)
    {
        await Eventually(() => processes.NewProcessCommand.CanExecute(null), TimeSpan.FromSeconds(5), "New process stayed disabled.");
        processes.NewProcessCommand.Execute(null);
        ProcessEditorViewModel editor = processes.Editor!;
        bool? closedWithSave = null;
        editor.Closed += (_, saved) => closedWithSave = saved;
        editor.Name = name;
        editor.Executable = $"  \"{ChildPaths.SampleChild}\"  ";
        editor.Arguments = arguments;
        editor.Kind = ProcessKind.RunToCompletion;
        await editor.SaveCommand.ExecuteAsync(null);
        editor.ErrorText.ShouldBeNull();
        closedWithSave.ShouldBe(true);
    }

    private static IEnumerable<string> StringsOf(object viewModel) =>
        viewModel.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
            .Select(p => (string?)p.GetValue(viewModel))
            .OfType<string>();

    private static Task Eventually(Func<bool> condition, TimeSpan timeout, string failure, TimeSpan? interval = null) =>
        Eventually(() => Task.FromResult(condition()), timeout, failure, interval);

    /// <summary>Polls until the condition holds; a collection that changes under the poll is read again, not failed.</summary>
    private static async Task Eventually(Func<Task<bool>> condition, TimeSpan timeout, string failure, TimeSpan? interval = null)
    {
        TimeSpan delay = interval ?? TimeSpan.FromMilliseconds(100);
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (await condition())
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                // A list was modified by the gateway thread while it was read.
            }

            if (watch.Elapsed > timeout)
            {
                throw new TimeoutException($"{failure} (waited {timeout}).");
            }

            await Task.Delay(delay, Ct);
        }
    }
}
