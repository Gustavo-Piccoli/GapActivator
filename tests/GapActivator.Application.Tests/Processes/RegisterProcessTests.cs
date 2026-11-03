using GapActivator.Application.Messaging;
using GapActivator.Application.Processes;
using GapActivator.Application.Tests.Messaging;
using GapActivator.Domain.Common;
using GapActivator.Domain.Processes;
using GapActivator.Domain.Scheduling;
using GapActivator.Testing;

namespace GapActivator.Application.Tests.Processes;

public class RegisterProcessTests
{
    [Fact]
    public async Task Register_Valid_StoresEnabledProcess()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();

        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(
            new RegisterProcess(ProcessDefinitions.Basic(description: "  Exports data  ")));

        Process stored = host.Store.Processes[saved.Id];
        stored.Name.Value.ShouldBe("Nightly export");
        stored.Description.ShouldBe("Exports data");
        stored.Lifecycle.ShouldBe(ProcessLifecycle.Enabled);
        stored.Launch.Executable.Value.ShouldBe(ProcessDefinitions.SampleExecutable);
        stored.CreatedAt.ShouldBe(now);
        saved.ProcessesSharingExecutable.ShouldBeEmpty();
    }

    // BI-001
    [Fact]
    public async Task Register_DuplicateNameDifferentCase_IsRejectedNameTaken()
    {
        await using var host = new ApplicationTestHost();
        await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic(name: "Nightly export")));

        var error = await Should.ThrowAsync<CommandRejectedException>(() => host.Run<RegisterProcess, ProcessSaved>(
            new RegisterProcess(ProcessDefinitions.Basic(name: "NIGHTLY EXPORT", executable: @"C:\Tools\other.exe"))));

        error.Code.ShouldBe(RejectionCodes.NameTaken);
        host.Store.Processes.Count.ShouldBe(1);
    }

    // BI-010
    [Fact]
    public async Task Register_SameExecutableAsAnother_SucceedsWithWarning()
    {
        await using var host = new ApplicationTestHost();
        await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic(name: "Zeta")));
        await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(ProcessDefinitions.Basic(name: "Alpha")));

        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(
            new RegisterProcess(ProcessDefinitions.Basic(name: "Middle")));

        host.Store.Processes.Count.ShouldBe(3);
        saved.ProcessesSharingExecutable.ShouldBe(["Alpha", "Zeta"]);
    }

    // Q-12
    [Fact]
    public async Task Register_OneTimeInThePast_IsRejected()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();

        var error = await Should.ThrowAsync<CommandRejectedException>(() => host.Run<RegisterProcess, ProcessSaved>(
            new RegisterProcess(ProcessDefinitions.Basic(schedules: [ProcessDefinitions.OneTime(now.AddMinutes(-1))]))));

        error.Code.ShouldBe(RejectionCodes.ScheduleInPast);
        host.Store.Processes.ShouldBeEmpty();
    }

    // Q-12
    [Fact]
    public async Task Register_OneTimeAtNow_IsRejected()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();

        var error = await Should.ThrowAsync<CommandRejectedException>(() => host.Run<RegisterProcess, ProcessSaved>(
            new RegisterProcess(ProcessDefinitions.Basic(schedules: [ProcessDefinitions.OneTime(now)]))));

        error.Code.ShouldBe(RejectionCodes.ScheduleInPast);
    }

    // Q-13
    [Fact]
    public async Task Register_RecurringWithOldAnchor_FirstOccurrenceIsNotBeforeNow()
    {
        await using var host = new ApplicationTestHost();
        DateTimeOffset now = host.Time.GetUtcNow();

        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(schedules: [ProcessDefinitions.Recurring(now.AddDays(-10))])));

        Schedule schedule = host.Store.Processes[saved.Id].Schedules.ShouldHaveSingleItem();
        schedule.NextOccurrenceAt.ShouldNotBeNull();
        schedule.NextOccurrenceAt.Value.ShouldBeGreaterThanOrEqualTo(now);
    }

    [Fact]
    public async Task Register_SecretVariable_IsStoredProtected()
    {
        await using var host = new ApplicationTestHost();

        ProcessSaved saved = await host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(environment:
            [
                new EnvironmentVariableInput("API_TOKEN", "s3cret", true),
                new EnvironmentVariableInput("MODE", "fast", false),
            ])));

        IReadOnlyList<EnvironmentVariable> items = host.Store.Processes[saved.Id].Launch.Environment.Items;
        EnvironmentVariable secret = items.Single(v => v.Name == "API_TOKEN");
        secret.IsSecret.ShouldBeTrue();
        secret.PlainValue.ShouldBeNull();
        secret.SecretValue!.Ciphertext.ShouldBe("enc:s3cret");
        items.Single(v => v.Name == "MODE").PlainValue.ShouldBe("fast");
    }

    [Fact]
    public async Task Register_SecretWithoutValue_IsRejectedSecretValueRequired()
    {
        await using var host = new ApplicationTestHost();

        var error = await Should.ThrowAsync<CommandRejectedException>(() => host.Run<RegisterProcess, ProcessSaved>(
            new RegisterProcess(ProcessDefinitions.Basic(environment: [new EnvironmentVariableInput("API_TOKEN", null, true)]))));

        error.Code.ShouldBe(RejectionCodes.SecretValueRequired);
    }

    [Fact]
    public async Task Register_InvalidRule_ThrowsDomainException()
    {
        await using var host = new ApplicationTestHost();

        await Should.ThrowAsync<DomainException>(() => host.Run<RegisterProcess, ProcessSaved>(new RegisterProcess(
            ProcessDefinitions.Basic(schedules: [ProcessDefinitions.Recurring(host.Time.GetUtcNow(), "NOT A RULE")]))));

        host.Store.Processes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Register_RecurringWithoutRule_ThrowsDomainException()
    {
        await using var host = new ApplicationTestHost();
        var input = new ScheduleInput(null, ScheduleInputKind.Recurring, host.Time.GetUtcNow(), null);

        await Should.ThrowAsync<DomainException>(() => host.Run<RegisterProcess, ProcessSaved>(
            new RegisterProcess(ProcessDefinitions.Basic(schedules: [input]))));
    }

    [Fact]
    public async Task Register_OneTimeWithRule_ThrowsDomainException()
    {
        await using var host = new ApplicationTestHost();
        var input = new ScheduleInput(null, ScheduleInputKind.OneTime, host.Time.GetUtcNow().AddHours(1), "FREQ=DAILY");

        await Should.ThrowAsync<DomainException>(() => host.Run<RegisterProcess, ProcessSaved>(
            new RegisterProcess(ProcessDefinitions.Basic(schedules: [input]))));
    }
}
