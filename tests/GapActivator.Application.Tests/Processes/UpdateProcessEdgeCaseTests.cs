using GapActivator.Application.Processes;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Scheduling;
using GapActivator.Testing;

namespace GapActivator.Application.Tests.Processes;

public class UpdateProcessEdgeCaseTests
{
    [Fact]
    public async Task Update_DuplicateInputIdsForOneSchedule_KeepsItOnceAndCreatesANewOne()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset at = host.Time.GetUtcNow().AddHours(3);
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(schedules: [ProcessDefinitions.OneTime(at)])));
        Schedule original = host.Store.Processes[saved.Id].Schedules.Single();

        await host.Run<UpdateProcess, ProcessSaved>(new UpdateProcess(
            saved.Id,
            ProcessDefinitions.Basic(schedules:
            [
                ProcessDefinitions.OneTime(at, original.Id.Value),
                ProcessDefinitions.OneTime(at, original.Id.Value),
            ])));

        IReadOnlyList<Schedule> after = host.Store.Processes[saved.Id].Schedules;
        after.Count.ShouldBe(2);
        after[0].ShouldBeSameAs(original);
        after[1].ShouldNotBeSameAs(original);
        after.Select(s => s.Id).Distinct().Count().ShouldBe(2);
    }

    // Controller ruling 3
    [Fact]
    public async Task Update_RuleDifferingOnlyInCaseAndWhitespace_KeepsSchedule()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset start = host.Time.GetUtcNow();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(schedules: [ProcessDefinitions.Recurring(start, "FREQ=DAILY;INTERVAL=2")])));
        Schedule original = host.Store.Processes[saved.Id].Schedules.Single();

        await host.Run<UpdateProcess, ProcessSaved>(new UpdateProcess(
            saved.Id,
            ProcessDefinitions.Basic(schedules: [ProcessDefinitions.Recurring(start, "  freq=daily;interval=2 ", original.Id.Value)])));

        host.Store.Processes[saved.Id].Schedules.ShouldHaveSingleItem().ShouldBeSameAs(original);
    }

    // Controller ruling 3
    [Fact]
    public async Task Update_ChangedAnchor_CreatesNewSchedule()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset start = host.Time.GetUtcNow();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(schedules: [ProcessDefinitions.Recurring(start)])));
        Schedule original = host.Store.Processes[saved.Id].Schedules.Single();

        await host.Run<UpdateProcess, ProcessSaved>(new UpdateProcess(
            saved.Id,
            ProcessDefinitions.Basic(schedules: [ProcessDefinitions.Recurring(start.AddMinutes(30), "FREQ=HOURLY", original.Id.Value)])));

        Schedule after = host.Store.Processes[saved.Id].Schedules.ShouldHaveSingleItem();
        after.ShouldNotBeSameAs(original);
    }

    [Fact]
    public async Task Update_ToServiceKindWithSchedules_ThrowsDomainExceptionAndKeepsProcess()
    {
        await using var host = new ApplicationTestHost();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(description: "Before")));

        await Should.ThrowAsync<DomainException>(() => host.Run<UpdateProcess, ProcessSaved>(new UpdateProcess(
            saved.Id,
            ProcessDefinitions.Basic(
                kind: ProcessKind.Service,
                description: "After",
                schedules: [ProcessDefinitions.OneTime(host.Time.GetUtcNow().AddHours(1))]))));

        Process process = host.Store.Processes[saved.Id];
        process.Kind.ShouldBe(ProcessKind.RunToCompletion);
        process.Description.ShouldBe("Before");
        process.Schedules.ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_ToServiceKindWithTimeLimit_ThrowsDomainExceptionAndKeepsProcess()
    {
        await using var host = new ApplicationTestHost();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(description: "Before")));
        ProcessDefinition definition = ProcessDefinitions.Basic(kind: ProcessKind.Service, description: "After")
            with { TimeLimit = TimeSpan.FromMinutes(5) };

        await Should.ThrowAsync<DomainException>(
            () => host.Run<UpdateProcess, ProcessSaved>(new UpdateProcess(saved.Id, definition)));

        Process process = host.Store.Processes[saved.Id];
        process.Kind.ShouldBe(ProcessKind.RunToCompletion);
        process.Description.ShouldBe("Before");
        process.Limits.TimeLimit.ShouldBeNull();
    }
}
