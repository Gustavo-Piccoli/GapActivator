using GapActivator.Application.Messaging;
using GapActivator.Application.Processes;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Testing;

namespace GapActivator.Application.Tests.Processes;

public class DeleteProcessTests
{
    [Fact]
    public async Task Delete_WithActiveRun_IsRejected()
    {
        await using var host = new ApplicationTestHost();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic()));
        Process process = host.Store.Processes[saved.Id];
        var run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, host.Time.GetUtcNow());
        host.Store.Runs[run.Id] = run;

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<DeleteProcess, Unit>(new DeleteProcess(saved.Id)));

        error.Code.ShouldBe(RejectionCodes.HasActiveRun);
        host.Store.Processes.ShouldContainKey(saved.Id);
    }

    [Fact]
    public async Task Delete_Idle_RemovesProcessAndKeepsRuns()
    {
        await using var host = new ApplicationTestHost();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic()));
        Process process = host.Store.Processes[saved.Id];
        var run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, host.Time.GetUtcNow());
        run.FailToStart("boom", host.Time.GetUtcNow());
        host.Store.Runs[run.Id] = run;

        await host.Run<DeleteProcess, Unit>(new DeleteProcess(saved.Id));

        host.Store.Processes.ShouldNotContainKey(saved.Id);
        host.Store.Runs.ShouldContainKey(run.Id);
        run.ProcessNameSnapshot.Value.ShouldBe("Nightly export");
    }

    [Fact]
    public async Task Delete_UnknownId_IsRejectedNotFound()
    {
        await using var host = new ApplicationTestHost();

        var error = await Should.ThrowAsync<CommandRejectedException>(
            () => host.Run<DeleteProcess, Unit>(new DeleteProcess(ProcessId.New())));

        error.Code.ShouldBe(RejectionCodes.NotFound);
    }
}
