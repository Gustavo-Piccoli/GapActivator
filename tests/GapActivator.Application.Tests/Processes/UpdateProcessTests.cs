using GapActivator.Application.Messaging;
using GapActivator.Application.Processes;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Runs;
using GapActivator.Domain.Scheduling;
using GapActivator.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace GapActivator.Application.Tests.Processes;

public class UpdateProcessTests
{
    // Q-12, Q-13
    [Fact]
    public async Task Update_UnchangedSchedule_KeepsNextOccurrenceAndFulfilledAt()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset start = host.Time.GetUtcNow();
        var calculator = host.Services.GetRequiredService<IOccurrenceCalculator>();
        DateTimeOffset oneTimeAt = start.AddHours(2);

        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(schedules:
            [
                ProcessDefinitions.Recurring(start),
                ProcessDefinitions.OneTime(oneTimeAt),
            ])));
        Process stored = host.Store.Processes[saved.Id];
        RecurringSchedule recurring = stored.Schedules.OfType<RecurringSchedule>().Single();
        OneTimeSchedule oneTime = stored.Schedules.OfType<OneTimeSchedule>().Single();

        host.Time.Advance(TimeSpan.FromHours(5));
        stored.ConsumeDueOccurrences(host.Time.GetUtcNow(), calculator);
        DateTimeOffset? nextBefore = recurring.NextOccurrenceAt;
        DateTimeOffset? fulfilledBefore = oneTime.FulfilledAt;
        nextBefore!.Value.ShouldBeGreaterThan(host.Time.GetUtcNow());
        fulfilledBefore.ShouldNotBeNull();

        await host.Run<UpdateProcess, ProcessSaved>(new UpdateProcess(
            saved.Id,
            ProcessDefinitions.Basic(description: "Changed", schedules:
            [
                ProcessDefinitions.Recurring(start, id: recurring.Id.Value),
                ProcessDefinitions.OneTime(oneTimeAt, id: oneTime.Id.Value),
            ])));

        Process after = host.Store.Processes[saved.Id];
        after.Description.ShouldBe("Changed");
        after.Schedules.ShouldContain(recurring);
        after.Schedules.ShouldContain(oneTime);
        recurring.NextOccurrenceAt.ShouldBe(nextBefore);
        oneTime.FulfilledAt.ShouldBe(fulfilledBefore);
    }

    // Q-13
    [Fact]
    public async Task Update_ChangedSchedule_IsRecreatedFromNow()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset start = host.Time.GetUtcNow();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(schedules: [ProcessDefinitions.Recurring(start)])));
        Schedule original = host.Store.Processes[saved.Id].Schedules.Single();

        host.Time.Advance(TimeSpan.FromHours(5));
        DateTimeOffset now = host.Time.GetUtcNow();
        await host.Run<UpdateProcess, ProcessSaved>(new UpdateProcess(
            saved.Id,
            ProcessDefinitions.Basic(schedules: [ProcessDefinitions.Recurring(start, "FREQ=HOURLY;INTERVAL=2", original.Id.Value)])));

        Schedule recreated = host.Store.Processes[saved.Id].Schedules.ShouldHaveSingleItem();
        recreated.ShouldNotBeSameAs(original);
        recreated.Id.ShouldNotBe(original.Id);
        recreated.NextOccurrenceAt!.Value.ShouldBeGreaterThanOrEqualTo(now);
    }

    // Q-12
    [Fact]
    public async Task Update_NewOneTimeInThePast_IsRejected()
    {
        await using var host = new ApplicationTestHost();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic()));

        var error = await Should.ThrowAsync<CommandRejectedException>(() => host.Run<UpdateProcess, ProcessSaved>(
            new UpdateProcess(saved.Id, ProcessDefinitions.Basic(
                description: "Changed",
                schedules: [ProcessDefinitions.OneTime(host.Time.GetUtcNow().AddMinutes(-5))]))));

        error.Code.ShouldBe(RejectionCodes.ScheduleInPast);
        host.Store.Processes[saved.Id].Description.ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_UnknownScheduleId_IsTreatedAsNewSchedule()
    {
        await using var host = new ApplicationTestHost();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic()));
        Guid foreign = Guid.NewGuid();

        await host.Run<UpdateProcess, ProcessSaved>(new UpdateProcess(
            saved.Id,
            ProcessDefinitions.Basic(schedules: [ProcessDefinitions.OneTime(host.Time.GetUtcNow().AddHours(1), foreign)])));

        Schedule schedule = host.Store.Processes[saved.Id].Schedules.ShouldHaveSingleItem();
        schedule.Id.Value.ShouldNotBe(foreign);
    }

    [Fact]
    public async Task Update_SecretWithoutValue_KeepsStoredSecret()
    {
        await using var host = new ApplicationTestHost();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(environment: [new EnvironmentVariableInput("API_TOKEN", "s3cret", true)])));

        await host.Run<UpdateProcess, ProcessSaved>(new UpdateProcess(
            saved.Id,
            ProcessDefinitions.Basic(environment: [new EnvironmentVariableInput("api_token", null, true)])));

        EnvironmentVariable variable = host.Store.Processes[saved.Id].Launch.Environment.Items.ShouldHaveSingleItem();
        variable.SecretValue!.Ciphertext.ShouldBe("enc:s3cret");
    }

    [Fact]
    public async Task Update_SecretWithoutValueAndNoStoredSecret_IsRejected()
    {
        await using var host = new ApplicationTestHost();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(environment: [new EnvironmentVariableInput("API_TOKEN", "plain", false)])));

        var error = await Should.ThrowAsync<CommandRejectedException>(() => host.Run<UpdateProcess, ProcessSaved>(
            new UpdateProcess(saved.Id, ProcessDefinitions.Basic(environment: [new EnvironmentVariableInput("API_TOKEN", null, true)]))));

        error.Code.ShouldBe(RejectionCodes.SecretValueRequired);
    }

    [Fact]
    public async Task Update_KindChangeWithActiveRun_IsRejected()
    {
        await using var host = new ApplicationTestHost();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic()));
        Process process = host.Store.Processes[saved.Id];
        var run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, host.Time.GetUtcNow());
        host.Store.Runs[run.Id] = run;

        var error = await Should.ThrowAsync<CommandRejectedException>(() => host.Run<UpdateProcess, ProcessSaved>(
            new UpdateProcess(saved.Id, ProcessDefinitions.Basic(kind: ProcessKind.Service, description: "Changed"))));

        error.Code.ShouldBe(RejectionCodes.HasActiveRun);
        process.Kind.ShouldBe(ProcessKind.RunToCompletion);
        process.Description.ShouldBeEmpty();
    }

    [Fact]
    public async Task Update_SameKindWithActiveRun_Succeeds()
    {
        await using var host = new ApplicationTestHost();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic()));
        Process process = host.Store.Processes[saved.Id];
        var run = Run.Begin(process.Id, process.Name, process.Kind, RunTrigger.Manual, null, false, host.Time.GetUtcNow());
        host.Store.Runs[run.Id] = run;

        await host.Run<UpdateProcess, ProcessSaved>(new UpdateProcess(saved.Id, ProcessDefinitions.Basic(description: "Changed")));

        process.Description.ShouldBe("Changed");
    }

    [Fact]
    public async Task Update_UnknownId_IsRejectedNotFound()
    {
        await using var host = new ApplicationTestHost();

        var error = await Should.ThrowAsync<CommandRejectedException>(() => host.Run<UpdateProcess, ProcessSaved>(
            new UpdateProcess(ProcessId.New(), ProcessDefinitions.Basic())));

        error.Code.ShouldBe(RejectionCodes.NotFound);
    }

    // BI-001
    [Fact]
    public async Task Update_NameOfAnotherProcess_IsRejectedNameTaken()
    {
        await using var host = new ApplicationTestHost();
        await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic(name: "First")));
        ProcessSaved second = await host.Run<RegisterProcess, ProcessSaved>(
            new RegisterProcess(ProcessDefinitions.Basic(name: "Second", executable: @"C:\Tools\other.exe")));

        var error = await Should.ThrowAsync<CommandRejectedException>(() => host.Run<UpdateProcess, ProcessSaved>(
            new UpdateProcess(second.Id, ProcessDefinitions.Basic(name: "first", executable: @"C:\Tools\other.exe"))));

        error.Code.ShouldBe(RejectionCodes.NameTaken);
        host.Store.Processes[second.Id].Name.Value.ShouldBe("Second");
    }

    [Fact]
    public async Task Update_KeepingOwnNameInDifferentCase_Succeeds()
    {
        await using var host = new ApplicationTestHost();
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic(name: "First")));

        await host.Run<UpdateProcess, ProcessSaved>(new UpdateProcess(saved.Id, ProcessDefinitions.Basic(name: "FIRST")));

        host.Store.Processes[saved.Id].Name.Value.ShouldBe("FIRST");
    }

    // BI-010
    [Fact]
    public async Task Update_SameExecutableAsAnother_SucceedsWithWarningExcludingSelf()
    {
        await using var host = new ApplicationTestHost();
        await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic(name: "Other")));
        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(
            new RegisterProcess(ProcessDefinitions.Basic(name: "Mine", executable: @"C:\Tools\mine.exe")));

        ProcessSaved updated = await host.Run<UpdateProcess, ProcessSaved>(
            new UpdateProcess(saved.Id, ProcessDefinitions.Basic(name: "Mine")));

        updated.Id.ShouldBe(saved.Id);
        updated.ProcessesSharingExecutable.ShouldBe(["Other"]);
    }
}
