using GapActivator.App.Localization;
using GapActivator.App.Scheduling;
using GapActivator.App.Services;
using GapActivator.App.Tests.Fakes;
using GapActivator.App.ViewModels;
using GapActivator.Ipc;
using GapActivator.Ipc.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace GapActivator.App.Tests.ViewModels;

public sealed class ProcessEditorViewModelTests : IDisposable
{
    private readonly FakeServiceGateway _gateway = new();
    private readonly Localizer _l = new(AppLanguage.English);
    private readonly ScriptedDialogService _dialogs = new();
    private readonly FakeFilePicker _files = new();
    private readonly FakeTimeProvider _time = new(Dtos.Epoch);
    private readonly TimeZoneInfo _zone = Zones.Fixed(TimeSpan.FromHours(-3));
    private readonly FailureAcknowledgements _seen;
    private readonly TimeDisplay _times;
    private readonly Permissions _permissions;
    private readonly ProcessEditorViewModel _vm;

    public ProcessEditorViewModelTests()
    {
        _seen = new FailureAcknowledgements(_time);
        _times = new TimeDisplay(_l, _zone);
        _permissions = new Permissions(_l, _gateway);
        _vm = new ProcessEditorViewModel(
            _l,
            _gateway,
            _permissions,
            _dialogs,
            _seen,
            _times,
            _zone,
            () => new ScheduleEditorViewModel(_l, _gateway, _time, _zone, _times, new ImmediateUiDispatcher()),
            _files);
    }

    public void Dispose() => _vm.Dispose();

    private static GapActivatorRequestException Failure(string code) => new(code, "service said so", retryable: code == IpcErrorCodes.Disconnected);

    // Everything a test sends that changes something.
    private List<(string Method, object?[] Args)> Sent() =>
        [.. _gateway.Calls.Where(c => c.Method is nameof(IServiceGateway.RegisterProcessAsync) or nameof(IServiceGateway.UpdateProcessAsync))];

    private ProcessDefinitionDto SentDefinition() => (ProcessDefinitionDto)Sent()[^1].Args[^1]!;

    private void FillNew(string name = "Backup", string executable = "C:\\Tools\\backup.exe")
    {
        _vm.LoadNew();
        _vm.Name = name;
        _vm.Executable = executable;
    }

    private Guid AddProcess(string name, string executable)
    {
        var summary = Dtos.Summary(name);
        _gateway.Processes.Add(summary);
        _gateway.Details[summary.Id] = Dtos.Details(summary) with { Executable = executable };
        return summary.Id;
    }

    private Guid LoadExisting(ProcessDetailsDto details)
    {
        _gateway.Details[details.Summary.Id] = details;
        return details.Summary.Id;
    }

    [Theory]
    [InlineData("C:\\%TOOLS%\\x.exe", true)]
    [InlineData("C:\\100% done\\x.exe", false)]
    public void VariableHint_ShownOnlyForAPercentPattern(string executable, bool expected)
    {
        _vm.LoadNew();

        _vm.Executable = executable;

        _vm.ShowVariableHint.ShouldBe(expected);
    }

    [Fact]
    public void VariableHint_FollowsTheWorkingDirectoryToo()
    {
        _vm.LoadNew();

        _vm.WorkingDirectory = "C:\\%TOOLS%";

        _vm.ShowVariableHint.ShouldBeTrue();
    }

    [Fact]
    public async Task New_Save_RegistersTheDefinitionWithEveryField()
    {
        _vm.LoadNew();
        _vm.Name = "Nightly backup";
        _vm.Description = "Copies the data.";
        _vm.Kind = ProcessKind.RunToCompletion;
        _vm.Executable = "  \"C:\\Tools\\backup tool.exe\"  ";
        _vm.Arguments = "--full --quiet";
        _vm.WorkingDirectory = "   ";
        _vm.TimeLimitSeconds = 90;
        _vm.GracePeriodSeconds = 15;
        _vm.AddVariableCommand.Execute(null);
        _vm.Environment[0].Name = " MODE ";
        _vm.Environment[0].Value = "fast";
        bool? closed = null;
        _vm.Closed += (_, saved) => closed = saved;

        await _vm.SaveCommand.ExecuteAsync(null);

        ProcessDefinitionDto sent = SentDefinition();
        sent.Name.ShouldBe("Nightly backup");
        sent.Description.ShouldBe("Copies the data.");
        sent.Kind.ShouldBe(ProcessKind.RunToCompletion);
        sent.Executable.ShouldBe("C:\\Tools\\backup tool.exe");
        sent.Arguments.ShouldBe("--full --quiet");
        sent.WorkingDirectory.ShouldBeNull();
        sent.TimeLimit.ShouldBe(TimeSpan.FromSeconds(90));
        sent.GracePeriod.ShouldBe(TimeSpan.FromSeconds(15));
        sent.Environment.ShouldBe([new EnvironmentVariableInputDto("MODE", "fast", false)]);
        sent.Schedules.ShouldBeEmpty();
        _vm.Executable.ShouldBe("C:\\Tools\\backup tool.exe");
        _vm.ErrorText.ShouldBeNull();
        closed.ShouldBe(true);
        Sent().Single().Method.ShouldBe(nameof(IServiceGateway.RegisterProcessAsync));
    }

    [Fact]
    public async Task New_Save_EmptyLimitsAreSentAsNull()
    {
        FillNew();

        await _vm.SaveCommand.ExecuteAsync(null);

        SentDefinition().TimeLimit.ShouldBeNull();
        SentDefinition().GracePeriod.ShouldBeNull();
        SentDefinition().Arguments.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Save_RequiredFieldsMissing_ShowsTheChecksAndSendsNothing()
    {
        _vm.LoadNew();
        await _vm.SaveCommand.ExecuteAsync(null);
        _vm.ErrorText.ShouldBe("Enter a name.");

        _vm.Name = "X";
        await _vm.SaveCommand.ExecuteAsync(null);
        _vm.ErrorText.ShouldBe("Enter the executable path.");

        _vm.Executable = "   ";
        await _vm.SaveCommand.ExecuteAsync(null);
        _vm.ErrorText.ShouldBe("Enter the executable path.");

        _vm.Executable = "C:\\x.exe";
        _vm.AddVariableCommand.Execute(null);
        await _vm.SaveCommand.ExecuteAsync(null);
        _vm.ErrorText.ShouldBe("Every variable needs a name.");

        _vm.Environment[0].Name = "SECRET";
        _vm.Environment[0].IsSecret = true;
        await _vm.SaveCommand.ExecuteAsync(null);
        _vm.ErrorText.ShouldBe("Enter a value for SECRET.");

        Sent().ShouldBeEmpty();
        _vm.Name.ShouldBe("X");
    }

    [Fact]
    public async Task Save_ServiceInvalid_ShowsTheServiceMessageAndKeepsTheInput()
    {
        FillNew("Keep me", "C:\\keep.exe");
        _vm.Arguments = "-a";
        _gateway.FailNext(nameof(IServiceGateway.RegisterProcessAsync), new GapActivatorRequestException(IpcErrorCodes.Invalid, "bad executable", retryable: false));
        var closed = false;
        _vm.Closed += (_, _) => closed = true;

        await _vm.SaveCommand.ExecuteAsync(null);

        _vm.ErrorText.ShouldBe("The service rejected the input: bad executable");
        _vm.Name.ShouldBe("Keep me");
        _vm.Executable.ShouldBe("C:\\keep.exe");
        _vm.Arguments.ShouldBe("-a");
        closed.ShouldBeFalse();
    }

    [Fact]
    public async Task Save_Disconnected_KeepsTheInputAndShowsTheError()
    {
        FillNew("Keep me", "C:\\keep.exe");
        _vm.Description = "typed";
        _vm.TimeLimitSeconds = 30;
        _gateway.FailNext(nameof(IServiceGateway.RegisterProcessAsync), Failure(IpcErrorCodes.Disconnected));

        await _vm.SaveCommand.ExecuteAsync(null);

        _vm.ErrorText.ShouldBe("Not connected to the service.");
        _vm.Name.ShouldBe("Keep me");
        _vm.Description.ShouldBe("typed");
        _vm.TimeLimitSeconds.ShouldBe(30);

        // The retry after the failure goes through with everything that was typed.
        await _vm.SaveCommand.ExecuteAsync(null);

        _vm.ErrorText.ShouldBeNull();
        SentDefinition().Description.ShouldBe("typed");
    }

    [Fact]
    public async Task Save_ConnectionLostWhileEditing_KeepsTheInputAndWorksAgainAfterReconnecting()
    {
        FillNew("Keep me", "C:\\keep.exe");
        _vm.Arguments = "-a";
        var closed = false;
        _vm.Closed += (_, _) => closed = true;

        _gateway.SetStatus(FakeServiceGateway.Stopped);

        _vm.IsReadOnly.ShouldBeTrue();
        _vm.SaveCommand.CanExecute(null).ShouldBeFalse();
        _vm.SaveDeniedReason.ShouldBe("Not connected to the service.");
        _vm.Name.ShouldBe("Keep me");
        _vm.Arguments.ShouldBe("-a");

        _gateway.SetStatus(FakeServiceGateway.Operator());

        _vm.IsReadOnly.ShouldBeFalse();
        _vm.SaveCommand.CanExecute(null).ShouldBeTrue();
        await _vm.SaveCommand.ExecuteAsync(null);
        closed.ShouldBeTrue();
        SentDefinition().Arguments.ShouldBe("-a");
    }

    [Fact]
    public async Task Save_NotFound_KeepsTheInput()
    {
        var id = LoadExisting(Dtos.Details());
        await _vm.LoadAsync(id);
        _vm.Description = "edited after it was deleted elsewhere";
        _gateway.FailNext(nameof(IServiceGateway.UpdateProcessAsync), Failure(IpcErrorCodes.NotFound));

        await _vm.SaveCommand.ExecuteAsync(null);

        _vm.ErrorText.ShouldBe("The item no longer exists. Refresh and try again.");
        _vm.Description.ShouldBe("edited after it was deleted elsewhere");
        _vm.Name.ShouldBe("Sample");
    }

    [Fact]
    public async Task Save_ErrorTextIsClearedByTheNextAttempt()
    {
        FillNew();
        _gateway.FailNext(nameof(IServiceGateway.RegisterProcessAsync), Failure(IpcErrorCodes.Conflict));
        await _vm.SaveCommand.ExecuteAsync(null);
        _vm.ErrorText.ShouldNotBeNull();

        await _vm.SaveCommand.ExecuteAsync(null);

        _vm.ErrorText.ShouldBeNull();
    }

    [Fact]
    public async Task Save_ScheduleInPast_IsShownNextToTheSchedules()
    {
        var id = LoadExisting(Dtos.Details());
        await _vm.LoadAsync(id);
        _vm.Description = "typed";
        _gateway.FailNext(nameof(IServiceGateway.UpdateProcessAsync), Failure(IpcErrorCodes.ScheduleInPast));

        await _vm.SaveCommand.ExecuteAsync(null);

        _vm.ScheduleErrorText.ShouldBe("A one-time schedule must be in the future.");
        _vm.ErrorText.ShouldBe("The schedule could not be saved. Check the Schedules section.");
        _vm.ShowKeptInput.ShouldBeTrue();
        _vm.Description.ShouldBe("typed");
    }

    [Fact]
    public async Task Load_Failed_LocksTheFormWithAReasonAndAnHonestTitle()
    {
        var id = LoadExisting(Dtos.Details());
        var hold = _gateway.Hold(nameof(IServiceGateway.GetProcessAsync));
        _gateway.FailNext(nameof(IServiceGateway.GetProcessAsync), Failure(IpcErrorCodes.NotFound));

        Task load = _vm.LoadAsync(id);
        _vm.TitleText.ShouldBe("Edit process");
        _vm.IsLocked.ShouldBeTrue();
        hold.SetResult();
        await load;

        _vm.LoadFailed.ShouldBeTrue();
        _vm.IsLocked.ShouldBeTrue();
        _vm.TitleText.ShouldBe("Edit process");
        _vm.SaveDeniedReason.ShouldBe("The process could not be loaded. Try again.");
        _vm.AddVariableCommand.CanExecute(null).ShouldBeFalse();
        _vm.BrowseExecutableCommand.CanExecute(null).ShouldBeFalse();
        _vm.RetryLoadCommand.CanExecute(null).ShouldBeTrue();

        await _vm.RetryLoadCommand.ExecuteAsync(null);

        _vm.IsLocked.ShouldBeFalse();
        _vm.SaveDeniedReason.ShouldBeNull();
        _vm.TitleText.ShouldBe("Edit \"Sample\"");
    }

    [Fact]
    public async Task Save_InFlight_LocksTheFieldsAndTheRowsUntilItEnds()
    {
        FillNew();
        _vm.AddVariableCommand.Execute(null);
        _vm.Environment[0].Name = "A";
        var hold = _gateway.Hold(nameof(IServiceGateway.RegisterProcessAsync));
        _gateway.FailNext(nameof(IServiceGateway.RegisterProcessAsync), Failure(IpcErrorCodes.Conflict));

        Task save = _vm.SaveCommand.ExecuteAsync(null);

        _vm.IsSaving.ShouldBeTrue();
        _vm.IsLocked.ShouldBeTrue();
        _vm.Environment[0].IsLocked.ShouldBeTrue();
        _vm.AddVariableCommand.CanExecute(null).ShouldBeFalse();
        hold.SetResult();
        await save;

        _vm.IsSaving.ShouldBeFalse();
        _vm.IsLocked.ShouldBeFalse();
        _vm.Environment[0].IsLocked.ShouldBeFalse();
        _vm.Name.ShouldBe("Backup");
    }

    [Fact]
    public async Task Save_RenamedStoredSecret_RequiresANewValue()
    {
        var id = LoadExisting(Dtos.Details() with { Environment = [new EnvironmentVariableViewDto("TOKEN", null, true)] });
        await _vm.LoadAsync(id);

        _vm.Environment[0].Name = "OTHER";

        _vm.Environment[0].HasStoredSecret.ShouldBeFalse();
        _vm.Environment[0].Placeholder.ShouldBeNull();
        await _vm.SaveCommand.ExecuteAsync(null);
        _vm.ErrorText.ShouldBe("Enter a value for OTHER.");
        Sent().ShouldBeEmpty();

        // Renaming it back restores the stored state: the stored value is kept again.
        _vm.Environment[0].Name = "TOKEN";
        _vm.Environment[0].HasStoredSecret.ShouldBeTrue();
        await _vm.SaveCommand.ExecuteAsync(null);
        SentDefinition().Environment.ShouldBe([new EnvironmentVariableInputDto("TOKEN", null, true)]);
    }

    [Fact]
    public async Task Save_SwappedStoredSecretNames_AreNotSentAsKeepSoNoValueMovesBetweenThem()
    {
        var id = LoadExisting(Dtos.Details() with
        {
            Environment = [new EnvironmentVariableViewDto("A", null, true), new EnvironmentVariableViewDto("B", null, true)],
        });
        await _vm.LoadAsync(id);

        _vm.Environment[0].Name = "B";
        _vm.Environment[1].Name = "A";
        await _vm.SaveCommand.ExecuteAsync(null);

        _vm.ErrorText.ShouldBe("Enter a value for B.");
        Sent().ShouldBeEmpty();

        _vm.Environment[0].Value = "x";
        _vm.Environment[1].Value = "y";
        await _vm.SaveCommand.ExecuteAsync(null);

        SentDefinition().Environment.ShouldBe([new EnvironmentVariableInputDto("B", "x", true), new EnvironmentVariableInputDto("A", "y", true)]);
    }

    [Fact]
    public async Task Edit_StoredSecret_ShowsThePlaceholderAndNoValue()
    {
        var id = LoadExisting(Dtos.Details() with { Environment = [new EnvironmentVariableViewDto("TOKEN", null, true), new EnvironmentVariableViewDto("MODE", "fast", false)] });

        await _vm.LoadAsync(id);

        EnvironmentVariableRowViewModel token = _vm.Environment[0];
        token.IsSecret.ShouldBeTrue();
        token.HasStoredSecret.ShouldBeTrue();
        token.Value.ShouldBe(string.Empty);
        token.Placeholder.ShouldBe("•••••• (stored; type to replace)");
        token.MaskChar.ShouldBe('\u2022');
        _vm.Environment[1].Value.ShouldBe("fast");
        _vm.Environment[1].Placeholder.ShouldBeNull();
        _vm.Environment[1].MaskChar.ShouldBe('\0');
    }

    [Fact]
    public async Task Save_UntouchedStoredSecret_SendsANullValue()
    {
        var id = LoadExisting(Dtos.Details() with { Environment = [new EnvironmentVariableViewDto("TOKEN", null, true)] });
        await _vm.LoadAsync(id);

        await _vm.SaveCommand.ExecuteAsync(null);

        SentDefinition().Environment.ShouldBe([new EnvironmentVariableInputDto("TOKEN", null, true)]);
        Sent().Single().Method.ShouldBe(nameof(IServiceGateway.UpdateProcessAsync));
        Sent().Single().Args[0].ShouldBe(id);
    }

    [Fact]
    public async Task Save_TypedSecret_SendsTheNewValue()
    {
        var id = LoadExisting(Dtos.Details() with { Environment = [new EnvironmentVariableViewDto("TOKEN", null, true)] });
        await _vm.LoadAsync(id);

        _vm.Environment[0].Value = "s3cret";
        await _vm.SaveCommand.ExecuteAsync(null);

        SentDefinition().Environment.ShouldBe([new EnvironmentVariableInputDto("TOKEN", "s3cret", true)]);
    }

    [Fact]
    public async Task Save_StoredSecretTurnedPlainWithoutAValue_AsksForTheValue()
    {
        var id = LoadExisting(Dtos.Details() with { Environment = [new EnvironmentVariableViewDto("TOKEN", null, true)] });
        await _vm.LoadAsync(id);

        _vm.Environment[0].IsSecret = false;
        await _vm.SaveCommand.ExecuteAsync(null);

        _vm.ErrorText.ShouldBe("Enter a value for TOKEN.");
        Sent().ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_UnchangedSchedule_SendsItsIdAtAndRuleUnchanged()
    {
        var schedule = new ScheduleViewDto(Guid.NewGuid(), ScheduleInputKind.Recurring, Dtos.Epoch, "FREQ=DAILY;BYHOUR=8;BYMINUTE=0", Dtos.Epoch.AddDays(1), null);
        var id = LoadExisting(Dtos.Details(schedules: [schedule]));
        await _vm.LoadAsync(id);

        await _vm.SaveCommand.ExecuteAsync(null);

        SentDefinition().Schedules.ShouldBe([new ScheduleInputDto(schedule.Id, ScheduleInputKind.Recurring, Dtos.Epoch, "FREQ=DAILY;BYHOUR=8;BYMINUTE=0")]);
        _vm.Schedules.Single().IsModified.ShouldBeFalse();
    }

    [Fact]
    public async Task Schedules_AddEditRemove_FollowTheScheduleEditor()
    {
        var schedule = new ScheduleViewDto(Guid.NewGuid(), ScheduleInputKind.Recurring, Dtos.Epoch, "FREQ=DAILY;BYHOUR=8;BYMINUTE=0", Dtos.Epoch.AddDays(1), null);
        var id = LoadExisting(Dtos.Details(schedules: [schedule]));
        await _vm.LoadAsync(id);

        // Edit the stored one into a weekly schedule: it keeps its id and is marked as changed.
        _vm.EditScheduleCommand.Execute(_vm.Schedules[0]);
        _vm.ScheduleEditor.ShouldNotBeNull();
        _vm.ScheduleEditor.Mode.ShouldBe(ScheduleMode.Daily);
        _vm.ScheduleEditor.Mode = ScheduleMode.Weekly;
        _vm.ScheduleEditor.Monday = true;
        _vm.ApplyScheduleCommand.CanExecute(null).ShouldBeTrue();
        _vm.ApplyScheduleCommand.Execute(null);

        _vm.ScheduleEditor.ShouldBeNull();
        _vm.Schedules.Count.ShouldBe(1);
        _vm.Schedules[0].IsModified.ShouldBeTrue();
        _vm.Schedules[0].Input.Id.ShouldBe(schedule.Id);

        // Add a new one.
        _vm.AddScheduleCommand.Execute(null);
        _vm.ScheduleEditor.ShouldNotBeNull();
        _vm.ApplyScheduleCommand.Execute(null);
        _vm.Schedules.Count.ShouldBe(2);
        _vm.Schedules[1].Original.ShouldBeNull();
        _vm.Schedules[1].Input.Id.ShouldBeNull();

        // Remove the first.
        _vm.RemoveScheduleCommand.Execute(_vm.Schedules[0]);
        _vm.Schedules.Count.ShouldBe(1);

        await _vm.SaveCommand.ExecuteAsync(null);
        SentDefinition().Schedules.Count.ShouldBe(1);
        SentDefinition().Schedules[0].Id.ShouldBeNull();
    }

    [Fact]
    public async Task ScheduleEditor_CancelAndClose_DisposeTheEditorsTheyOwn()
    {
        FillNew();
        _vm.AddScheduleCommand.Execute(null);
        ScheduleEditorViewModel first = _vm.ScheduleEditor!;
        _vm.CancelScheduleCommand.Execute(null);

        _vm.ScheduleEditor.ShouldBeNull();
        first.IsDisposed.ShouldBeTrue();

        _vm.AddScheduleCommand.Execute(null);
        ScheduleEditorViewModel second = _vm.ScheduleEditor!;
        _vm.AddScheduleCommand.Execute(null);

        second.IsDisposed.ShouldBeTrue();
        ScheduleEditorViewModel third = _vm.ScheduleEditor!;

        _vm.CloseCommand.Execute(null);
        _vm.Dispose();

        third.IsDisposed.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Save_WhileAScheduleIsBeingEdited_AsksToFinishItFirst()
    {
        FillNew();
        _vm.AddScheduleCommand.Execute(null);

        await _vm.SaveCommand.ExecuteAsync(null);

        _vm.ErrorText.ShouldBe("Apply or discard the schedule you are editing before saving.");
        Sent().ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_ServiceKind_SendsNoSchedulesAndNoTimeLimit()
    {
        var schedule = new ScheduleViewDto(Guid.NewGuid(), ScheduleInputKind.Recurring, Dtos.Epoch, "FREQ=DAILY;BYHOUR=8;BYMINUTE=0", null, null);
        var id = LoadExisting(Dtos.Details(schedules: [schedule]) with { TimeLimit = TimeSpan.FromSeconds(60) });
        await _vm.LoadAsync(id);

        _vm.Kind = ProcessKind.Service;

        _vm.IsServiceKind.ShouldBeTrue();
        _vm.ShowSchedules.ShouldBeFalse();
        await _vm.SaveCommand.ExecuteAsync(null);

        SentDefinition().Schedules.ShouldBeEmpty();
        SentDefinition().TimeLimit.ShouldBeNull();
        SentDefinition().Kind.ShouldBe(ProcessKind.Service);
        _vm.TimeLimitSeconds.ShouldBe(60);
        _vm.Schedules.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Save_ExecutableUsedByAnother_AsksFirst()
    {
        AddProcess("Nightly copy", "C:\\TOOLS\\backup.exe");
        AddProcess("Unrelated", "C:\\other.exe");
        FillNew("Backup", "C:\\Tools\\backup.exe");
        _dialogs.Enqueue(DialogChoice.Primary);

        await _vm.SaveCommand.ExecuteAsync(null);

        _dialogs.Asked.Count.ShouldBe(1);
        _dialogs.Asked[0].Title.ShouldBe("Executable already in use");
        _dialogs.Asked[0].Message.ShouldBe("Also used by: Nightly copy. Save anyway?");
        _dialogs.Asked[0].Primary.ShouldBe("Save");
        Sent().Count.ShouldBe(1);
        _vm.NoticeText.ShouldBeNull();
    }

    [Fact]
    public async Task Save_ExecutableUsedByAnother_No_SendsNothing()
    {
        AddProcess("Nightly copy", "C:\\Tools\\backup.exe");
        FillNew("Backup", "C:\\Tools\\backup.exe");
        var closed = false;
        _vm.Closed += (_, _) => closed = true;

        await _vm.SaveCommand.ExecuteAsync(null);

        Sent().ShouldBeEmpty();
        closed.ShouldBeFalse();
        _vm.Name.ShouldBe("Backup");
    }

    [Fact]
    public async Task Save_ExistingWithUnchangedExecutable_DoesNotLookForOtherProcesses()
    {
        AddProcess("Nightly copy", "C:\\Tools\\backup.exe");
        var id = LoadExisting(Dtos.Details() with { Executable = "C:\\Tools\\backup.exe" });
        await _vm.LoadAsync(id);

        await _vm.SaveCommand.ExecuteAsync(null);

        _gateway.Calls.ShouldNotContain(c => c.Method == nameof(IServiceGateway.ListProcessesAsync));
        _dialogs.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_ExistingWithChangedExecutable_ChecksTheOthersButNotItself()
    {
        var other = AddProcess("Nightly copy", "C:\\Tools\\backup.exe");
        var id = LoadExisting(Dtos.Details());
        _gateway.Processes.Add(Dtos.Details().Summary with { Id = id });
        await _vm.LoadAsync(id);
        _vm.Executable = "C:\\Tools\\backup.exe";
        _dialogs.Enqueue(DialogChoice.Primary);

        await _vm.SaveCommand.ExecuteAsync(null);

        _gateway.Calls.Where(c => c.Method == nameof(IServiceGateway.GetProcessAsync) && (Guid)c.Args[0]! == other).Count().ShouldBe(1);
        _dialogs.Asked.Single().Message.ShouldBe("Also used by: Nightly copy. Save anyway?");
    }

    [Fact]
    public async Task Save_ServiceReportsAnUnseenSharer_ShowsTheNote()
    {
        FillNew();
        _gateway.Saved = new ProcessSavedDto(Guid.NewGuid(), ["Hidden sibling"]);
        bool? closed = null;
        _vm.Closed += (_, saved) => closed = saved;

        await _vm.SaveCommand.ExecuteAsync(null);

        _vm.NoticeText.ShouldBe("Saved. Also used by: Hidden sibling.");
        closed.ShouldBe(true);
    }

    [Fact]
    public async Task Viewer_IsReadOnlyAndShowsHiddenValues()
    {
        _gateway.SetStatus(FakeServiceGateway.Viewer());
        var id = LoadExisting(Dtos.Details() with
        {
            SensitiveHidden = true,
            Environment = [new EnvironmentVariableViewDto("MODE", null, false), new EnvironmentVariableViewDto("TOKEN", null, true)],
        });

        await _vm.LoadAsync(id);

        _vm.IsReadOnly.ShouldBeTrue();
        _vm.ArgumentsHidden.ShouldBeTrue();
        _vm.Arguments.ShouldBe("Hidden");
        _vm.Environment.Select(e => e.Value).ShouldBe(["Hidden", "Hidden"]);
        _vm.Environment.ShouldAllBe(e => e.MaskChar == '\0' && e.Placeholder == null);
        _vm.SaveCommand.CanExecute(null).ShouldBeFalse();
        _vm.SensitiveNote.ShouldBe("Arguments and environment values are hidden from viewers.");
        _vm.AddVariableCommand.CanExecute(null).ShouldBeFalse();
        _vm.AddScheduleCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Viewer_LanguageSwitch_RebuildsTheHiddenWordAndTheNote()
    {
        _gateway.SetStatus(FakeServiceGateway.Viewer());
        var id = LoadExisting(Dtos.Details() with
        {
            SensitiveHidden = true,
            Environment = [new EnvironmentVariableViewDto("MODE", null, false)],
        });
        await _vm.LoadAsync(id);

        _l.SetLanguage(AppLanguage.Portuguese);

        _vm.Arguments.ShouldBe("Oculto");
        _vm.Environment[0].Value.ShouldBe("Oculto");
        _vm.SensitiveNote.ShouldBe("Argumentos e valores de ambiente ficam ocultos para quem só visualiza.");
    }

    [Fact]
    public async Task FulfilledOneTime_IsShownAsNoLongerPendingNotAsARun()
    {
        var fulfilledAt = Dtos.Epoch.AddHours(-5);
        var schedule = new ScheduleViewDto(Guid.NewGuid(), ScheduleInputKind.OneTime, Dtos.Epoch.AddHours(-6), null, null, fulfilledAt);
        var id = LoadExisting(Dtos.Details(schedules: [schedule]));

        await _vm.LoadAsync(id);

        _vm.Schedules.Single().StatusText.ShouldBe(_l.ScheduleNoLongerPending(_times.DateTime(fulfilledAt)));
        _gateway.Calls.Select(c => c.Method).Intersect([nameof(IServiceGateway.ListRunsAsync), nameof(IServiceGateway.GetRunAsync), nameof(IServiceGateway.PreviewScheduleAsync)]).ShouldBeEmpty();
    }

    [Fact]
    public async Task ScheduleItems_ShowTheNextRunOrNone_AndFollowTheLanguage()
    {
        var next = new ScheduleViewDto(Guid.NewGuid(), ScheduleInputKind.Recurring, Dtos.Epoch, "FREQ=DAILY;BYHOUR=8;BYMINUTE=0", Dtos.Epoch.AddDays(1), null);
        var none = new ScheduleViewDto(Guid.NewGuid(), ScheduleInputKind.Recurring, Dtos.Epoch, "FREQ=DAILY;BYHOUR=9;BYMINUTE=0", null, null);
        var id = LoadExisting(Dtos.Details(schedules: [next, none]));
        await _vm.LoadAsync(id);

        _vm.Schedules[0].StatusText.ShouldBe(_l.NextRun(_times.DateTime(next.NextOccurrenceAt!.Value)));
        _vm.Schedules[1].StatusText.ShouldBe("No next run");

        _l.SetLanguage(AppLanguage.Portuguese);

        _vm.Schedules[1].StatusText.ShouldBe("Sem próxima execução");
        _vm.Schedules[0].SummaryText.ShouldStartWith("Diariamente");
    }

    [Fact]
    public async Task Load_MarksTheProcessFailuresSeen()
    {
        var id = LoadExisting(Dtos.Details());
        var failure = Dtos.RunRow(processId: id, outcome: RunOutcome.Failed, endedAt: Dtos.Epoch);
        _seen.IsUnseen(failure).ShouldBeTrue();

        await _vm.LoadAsync(id);

        _seen.IsUnseen(failure).ShouldBeFalse();
    }

    [Fact]
    public async Task Load_ShowsTheStatesAndFillsEveryField()
    {
        var details = Dtos.Details(Dtos.Summary("Report", kind: ProcessKind.Service)) with
        {
            Description = "Desc",
            Arguments = "-x",
            WorkingDirectory = "C:\\Work",
            TimeLimit = TimeSpan.FromSeconds(120),
            GracePeriod = TimeSpan.FromSeconds(5),
        };
        var id = LoadExisting(details);
        var hold = _gateway.Hold(nameof(IServiceGateway.GetProcessAsync));

        Task load = _vm.LoadAsync(id);
        _vm.IsLoading.ShouldBeTrue();
        _vm.SaveCommand.CanExecute(null).ShouldBeFalse();
        hold.SetResult();
        await load;

        _vm.IsLoading.ShouldBeFalse();
        _vm.IsNew.ShouldBeFalse();
        _vm.Name.ShouldBe("Report");
        _vm.Description.ShouldBe("Desc");
        _vm.Kind.ShouldBe(ProcessKind.Service);
        _vm.Executable.ShouldBe("C:\\Tools\\sample.exe");
        _vm.Arguments.ShouldBe("-x");
        _vm.WorkingDirectory.ShouldBe("C:\\Work");
        _vm.TimeLimitSeconds.ShouldBe(120);
        _vm.GracePeriodSeconds.ShouldBe(5);
        _vm.LoadFailed.ShouldBeFalse();
    }

    [Fact]
    public async Task Load_Failure_ShowsTheErrorAndCanBeRetried()
    {
        var id = LoadExisting(Dtos.Details());
        _gateway.FailNext(nameof(IServiceGateway.GetProcessAsync), Failure(IpcErrorCodes.Disconnected));

        await _vm.LoadAsync(id);

        _vm.LoadFailed.ShouldBeTrue();
        _vm.ErrorText.ShouldBe("Not connected to the service.");
        _vm.SaveCommand.CanExecute(null).ShouldBeFalse();

        await _vm.RetryLoadCommand.ExecuteAsync(null);

        _vm.LoadFailed.ShouldBeFalse();
        _vm.ErrorText.ShouldBeNull();
        _vm.Name.ShouldBe("Sample");
    }

    [Fact]
    public async Task Browse_PickedPath_FillsTheExecutable()
    {
        _vm.LoadNew();
        _files.Path = "C:\\Picked\\tool.exe";

        await _vm.BrowseExecutableCommand.ExecuteAsync(null);

        _vm.Executable.ShouldBe("C:\\Picked\\tool.exe");
        _files.Opened.ShouldBe(1);
    }

    [Fact]
    public async Task Browse_Cancelled_KeepsTheExecutable()
    {
        _vm.LoadNew();
        _vm.Executable = "C:\\keep.exe";
        _files.Path = null;

        await _vm.BrowseExecutableCommand.ExecuteAsync(null);

        _vm.Executable.ShouldBe("C:\\keep.exe");
    }

    [Fact]
    public void Variables_AddAndRemove()
    {
        _vm.LoadNew();

        _vm.AddVariableCommand.Execute(null);
        _vm.AddVariableCommand.Execute(null);
        _vm.RemoveVariableCommand.Execute(_vm.Environment[0]);

        _vm.Environment.Count.ShouldBe(1);
    }

    [Fact]
    public void Close_RaisesClosedWithoutSaving()
    {
        FillNew();
        bool? closed = null;
        _vm.Closed += (_, saved) => closed = saved;

        _vm.CloseCommand.Execute(null);

        closed.ShouldBe(false);
        Sent().ShouldBeEmpty();
    }

    [Fact]
    public void Title_FollowsTheLanguage()
    {
        _vm.LoadNew();
        _vm.TitleText.ShouldBe("New process");

        _l.SetLanguage(AppLanguage.Portuguese);

        _vm.TitleText.ShouldBe("Novo processo");
        _vm.KindOptions.Select(k => k.Text).ShouldBe(["Executa até concluir", "Serviço (permanece em execução)"]);
    }

    [Fact]
    public void Dispose_StopsFollowingTheGatewayAndTheLanguage()
    {
        _vm.LoadNew();
        _vm.Dispose();
        var raised = false;
        _vm.PropertyChanged += (_, _) => raised = true;

        _l.SetLanguage(AppLanguage.Portuguese);
        _gateway.SetStatus(FakeServiceGateway.Viewer());

        raised.ShouldBeFalse();
    }
}
