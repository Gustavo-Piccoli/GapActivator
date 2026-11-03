# Plan 2 — Application (CQRS) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `GapActivator.Application`: every use case as a Command or Query with its own handler, the ports the outer layers implement, run supervision, the scheduler cycle, startup reconciliation, shutdown, and notifications, all tested with fakes (no database, no OS processes).

**Architecture:** `GapActivator.Application` references only `GapActivator.Domain` plus two Microsoft abstraction packages. A thin, hand-written dispatcher runs each command in its own DI scope behind a single write gate, so there is one writer at a time. The pipeline is: handler, then domain events become history entries, then the unit of work commits, then after-commit actions and notifications run, each isolated from failures. Long-lived work (a running child, a grace period, a time limit) lives in the singleton `RunSupervisor`, never inside a handler. The supervisor reports back through internal commands, so a long run never blocks anything.

**Tech Stack:** .NET 10 / C# 14; `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.14; `Microsoft.Extensions.Logging.Abstractions` 10.0.14; tests: xUnit v3 on Microsoft.Testing.Platform, Shouldly, NetArchTest, `Microsoft.Extensions.DependencyInjection` 10.0.14, `Microsoft.Extensions.TimeProvider.Testing` 10.11.0.

**Spec:** `docs/superpowers/specs/2026-11-03-gapactivator-design.md` (mainly §6.3–§6.6, §8, §10 "Authorization"). Also read `docs/behavior-inventory.md`: every row whose *Plan* column is 2 lands here.

## Global Constraints

- Everything from Plan 1's Global Constraints still holds: English everywhere; Conventional Commits; **no AI attribution trailers**; synthetic test data only; never write the legacy repository's name or path; warnings as errors; `dotnet format --verify-no-changes` passes.
- `GapActivator.Application` targets `net10.0`, `IsAotCompatible=true`, references `GapActivator.Domain` and **only** these packages: `Microsoft.Extensions.DependencyInjection.Abstractions` (used by `AddApplication` and by the dispatcher's `IServiceScopeFactory`) and `Microsoft.Extensions.Logging.Abstractions` (`ILogger<T>` is the spec's "log writer" port). Both are abstractions of the hosting stack the spec already justifies (§3, Hosting.WindowsServices). There is no mediator library (§3).
- New test-only packages: `Microsoft.Extensions.DependencyInjection` (real container in the test host) and `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider` for timers and delays). Nothing else.
- The application never reads a clock directly. Time comes from the injected `TimeProvider`: `GetUtcNow()`, `LocalTimeZone`, and `Task.Delay(delay, timeProvider, ct)`.
- Handlers never call the dispatcher, because a nested send would deadlock on the write gate. Work that must follow a commit is queued with `AfterCommit.Enqueue`.
- Rule violations: the domain throws `DomainException`. The application throws `CommandRejectedException(code, message)` with a code from `RejectionCodes`. Not found is the rejection `not-found`. Plan 4 maps both to IPC errors.
- Owner decisions taken while writing this plan (2026-11-03), recorded by Task 1 in the spec and the inventory:
  - **Q-12:** a new or changed OneTime schedule whose instant is not in the future is rejected when saving.
  - **Q-13:** a new recurring schedule ignores occurrences before the moment it is saved. Its first run is the first occurrence at or after save time, and COUNT still counts from the anchor.
  - **Q-14:** occurrences that came due while the installation was paused are discarded on Resume, and pending retries stay pending.
  - **Q-15:** Run now on a Disabled process is allowed for RunToCompletion and rejected for Service kind. A Blocked process still needs "Unblock and run".
- Builder decisions in this plan (the spec leaves them open, §16):
  - **Late threshold:** a run is late when `now − scheduledFor > 2 × CycleInterval`.
  - **CancelRun on a Service-kind run** is rejected with `use-disable`. "Stop" for Service kind is DisableProcess (§6.3).
  - **Disabling a Service-kind process** always stops its active run.
  - **Enabling a Service-kind process** starts it at once.
  - **Kind changes:** UpdateProcess cannot change the Kind while a run is active.
  - **One cycle at a time:** processes are evaluated one after another, each in its own command, so a failure in one process never affects another (BI-039). Launches happen outside the gate.

## Review Focus

1. **A command racing a run's exit.** A cancel or time-limit stop can arrive just after the child exited. The run must end exactly once, with the outcome of whichever was recorded first, and the supervisor must not throw or leak an entry. Test in Task 5.
2. **Recording a completion fails.** If the commit throws when the exit is recorded, the supervisor retries 3 times, 1 s apart, then logs the error. It never crashes, and the run stays active for startup reconciliation. Test in Task 5.
3. **Editing a process while its schedules are mid-series.** Changing only the description must keep each unchanged schedule's `NextOccurrenceAt` and `FulfilledAt`. It must not re-create the schedule and cause a catch-up run. Test in Task 4.
4. **Settings changed between cycles.** A new cycle interval applies to the next wait. A failure limit lowered below a process's counter blocks it on the next cycle, unless the run is manual (Q-3). Tests in Tasks 6 and 7.
5. **Shutdown while a launch is in flight.** The run ends **Interrupted**: not Abandoned, not a failure, no retry. A time limit longer than the maximum timer delay (about 49.7 days) still fires. Tests in Tasks 5 and 8.

## Execution notes

- Branch `feat/plan-2-application` from `main`, in the working copy (no worktrees).
- Every task ends with `dotnet build -c Release`, `dotnet test -c Release` and `dotnet format --verify-no-changes` green.
- Test naming: `Method_Condition_ExpectedResult`. Put the inventory ID in a comment above each test that covers a row (`// BI-041`).
- Handler tests go through the real dispatcher in `ApplicationTestHost` (Task 2), never by calling `HandleAsync` directly, so the gate, commit, events and after-commit actions are always exercised.

## File Structure

```
src/GapActivator.Domain/Scheduling/RecurringSchedule.cs      (modify: notBefore overload)
src/GapActivator.Domain/Runs/Run.cs, RunEvents.cs              (modify: RunStopRequested)
src/GapActivator.Application/
  GapActivator.Application.csproj  ApplicationAssembly.cs  DependencyInjection.cs
  Messaging/   ICommand.cs  IQuery.cs  Handlers.cs  Unit.cs  Caller.cs  CallerAccessor.cs
               ICommandDispatcher.cs  CommandDispatcher.cs  IQueryDispatcher.cs  QueryDispatcher.cs
               WriteGate.cs  AfterCommit.cs  EventBuffer.cs  CommandRejectedException.cs  RejectionCodes.cs
  Ports/       IUnitOfWork.cs  IHistoryWriter.cs  IRunHistoryReader.cs  IRunOutputReader.cs
               IProcessLauncher.cs  ISecretProtector.cs  IEmailSender.cs  IAppNotifier.cs
               IServiceControl.cs  IHistoryRetention.cs  ILogRetention.cs
  Events/      ApplicationEvents.cs  HistoryEntry.cs  HistoryEntryFactory.cs
               INotificationHandler.cs  EventPublisher.cs
  Processes/   ProcessDefinition.cs  ProcessDefinitionMapper.cs
               RegisterProcess.cs  UpdateProcess.cs  DeleteProcess.cs
               EnableProcess.cs  DisableProcess.cs  UnblockProcess.cs
  Runs/        RunSupervisor.cs  RunStarter.cs  RunStartTicket.cs
               RecordRunLaunched.cs  RecordRunLaunchFailed.cs  RecordLaunchAborted.cs  RecordRunExited.cs
               StopRun.cs  StartPendingRun.cs  RunNow.cs  RestartRun.cs  CancelRun.cs
  Cycle/       EvaluateProcess.cs  PurgeHistory.cs  RecordCycleFailure.cs  CycleRunner.cs
               SchedulerLoop.cs  ReconcileOnStartup.cs
  Settings/    SettingsInput.cs  UpdateSettings.cs  PauseInstallation.cs  ResumeInstallation.cs
               SendTestEmail.cs  RestartGapActivator.cs
  Notifications/ AlertRules.cs  EmailAlertHandler.cs  AppNotificationHandler.cs
  Queries/     Views.cs  ListProcesses.cs  GetProcess.cs  GetDashboard.cs  ListRuns.cs  GetRun.cs
               GetSettings.cs  GetCycleSettings.cs  ListProcessIds.cs  PreviewSchedule.cs
tests/GapActivator.Testing/Application/
               InMemoryStore.cs  InMemoryUnitOfWork.cs  InMemoryProcessRepository.cs  InMemoryRunRepository.cs
               InMemorySettingsRepository.cs  InMemoryHistory.cs  FakeProcessLauncher.cs  FakeRunningProcess.cs
               FakeSecretProtector.cs  RecordingEmailSender.cs  RecordingAppNotifier.cs  FakeServiceControl.cs
               FakeLogRetention.cs  FakeRunOutputReader.cs  ApplicationTestHost.cs
tests/GapActivator.Application.Tests/   one folder per Application folder
tests/GapActivator.Architecture.Tests/LayeringTests.cs        (modify)
```

Each command or query file holds the message record and its handler. Handlers are `internal sealed`; message records and views are `public`.

---

### Task 1: Owner decisions Q-12..Q-15 and the small domain additions

**Files:**
- Modify: `src/GapActivator.Domain/Scheduling/RecurringSchedule.cs`, `src/GapActivator.Domain/Runs/Run.cs`, `src/GapActivator.Domain/Runs/RunEvents.cs`
- Modify: `docs/superpowers/specs/2026-11-03-gapactivator-design.md` (§6.4 bullets for Q-12, Q-13, Q-14; §8 edge-case table row for Q-15), `docs/behavior-inventory.md` (Owner decisions rows Q-12..Q-15)
- Test: `tests/GapActivator.Domain.Tests/Scheduling/ScheduleTests.cs`, `tests/GapActivator.Domain.Tests/Runs/RunStateMachineTests.cs`

**Interfaces:**
- Produces: `RecurringSchedule.Create(RecurrenceRule rule, DateTimeOffset anchor, DateTimeOffset notBefore, IOccurrenceCalculator calculator)`. The existing 3-argument overload stays and means `notBefore = anchor`.
- Produces: `public sealed record RunStopRequested(RunId RunId, ProcessId ProcessId, StopReason Reason, DateTimeOffset OccurredAt) : IDomainEvent`, raised by `Run.RequestStop` only when it sets the first stop reason.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] // Q-13
public void RecurringSchedule_CreateWithNotBeforeAfterAnchor_SkipsEarlierOccurrences()
{
    var calc = new FakeOccurrenceCalculator(TimeSpan.FromHours(1));
    var anchor = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    var s = RecurringSchedule.Create(RecurrenceRule.Parse("FREQ=HOURLY"), anchor, anchor.AddMinutes(90), calc);
    s.NextOccurrenceAt.ShouldBe(anchor.AddHours(2));
}
```

Also add:
- `RecurringSchedule_CreateWithNotBeforeOnAnOccurrence_KeepsThatOccurrence`: `notBefore = anchor + 2h` gives `anchor + 2h`. This is the equality case.
- `RecurringSchedule_CreateWithNotBeforeBeforeAnchor_StartsAtAnchor`.
- `RecurringSchedule_CreateWithNotBeforeAfterExhaustion_IsExhausted`: `FakeOccurrenceCalculator(1h, count: 2)` with `notBefore = anchor + 5h`.
- `RequestStop_FirstReason_RaisesRunStopRequested` and `RequestStop_SecondReason_RaisesNothingMore`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test -c Release --project tests/GapActivator.Domain.Tests`
Expected: build FAILS (no 4-argument `Create`, no `RunStopRequested`).

- [ ] **Step 3: Implement**

The `notBefore` overload computes:

```csharp
var first = calculator.FirstOccurrence(rule, anchor);
NextOccurrenceAt = first is { } f && f < notBefore
    ? calculator.NextOccurrence(rule, anchor, notBefore.AddTicks(-1))   // first occurrence at or after notBefore
    : first;
```

In `RequestStop`, raise `RunStopRequested` inside the branch where `StopReason` was null before the call.

- [ ] **Step 4: Record the owner decisions**

- Inventory: add rows Q-12..Q-15 to the *Owner decisions* table, with the wording from Global Constraints. *Rows* is `—`; *Covered by* lists the tests from Tasks 1, 4, 6 and 9 named below.
- Spec §6.4: add three bullets (Q-12, Q-13, Q-14).
- Spec §8 "Other edge cases": add the row "Run now on a disabled process → allowed for RunToCompletion; rejected for Service kind (enable it to start)".

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test -c Release`
Expected: PASS (all 512 earlier tests plus the new ones).

- [ ] **Step 6: Commit**

```bash
git commit -am "feat(domain): add not-before schedule creation and stop-requested event"
```

### Task 2: Application project, ports, test fakes, architecture rules

**Files:**
- Create: `src/GapActivator.Application/GapActivator.Application.csproj`, `ApplicationAssembly.cs`, `DependencyInjection.cs`, all of `Ports/`, `Messaging/ICommand.cs`, `IQuery.cs`, `Handlers.cs`, `Unit.cs`, `Caller.cs`, `CommandRejectedException.cs`, `RejectionCodes.cs`, `Events/ApplicationEvents.cs`, `Events/HistoryEntry.cs`
- Create: `tests/GapActivator.Application.Tests/GapActivator.Application.Tests.csproj` (same shape as `GapActivator.Domain.Tests`, plus references to Application, `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.TimeProvider.Testing`), and every file under `tests/GapActivator.Testing/Application/`
- Modify: `GapActivator.slnx`, `Directory.Packages.props`, `tests/GapActivator.Testing/GapActivator.Testing.csproj` (references Application and the two test packages), `tests/GapActivator.Architecture.Tests/*.csproj` and `LayeringTests.cs`

**Interfaces (Produces; every later task uses these exact names):**

```csharp
// Messaging
public interface ICommand<TResult>;                 // public commands: caller must have CanChange
public interface IInternalCommand<TResult> : ICommand<TResult>;   // only Caller.System may send
public interface IUngatedCommand;                   // marker: runs without the write gate and without a commit
public interface IQuery<TResult>;
public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{ Task<TResult> HandleAsync(TCommand command, CancellationToken ct); }
public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{ Task<TResult> HandleAsync(TQuery query, CancellationToken ct); }
public readonly record struct Unit { public static Unit Value => default; }
public sealed record Caller(string Name, bool CanChange, bool IsSystem)
{ public static Caller System { get; } = new("GapActivator", true, true);
  public static Caller Viewer(string name) => new(name, false, false);
  public static Caller Operator(string name) => new(name, true, false); }
public sealed class CommandRejectedException(string code, string message) : Exception(message) { public string Code { get; } = code; }
public static class RejectionCodes  // const strings, kebab-case:
{ NotFound = "not-found", Unauthorized = "unauthorized", NameTaken = "name-taken",
  ScheduleInPast = "schedule-in-past", SecretValueRequired = "secret-value-required",
  HasActiveRun = "has-active-run", RunAlreadyActive = "run-already-active", RunNotActive = "run-not-active",
  ProcessBlocked = "process-blocked", ProcessDisabled = "process-disabled", InstallationPaused = "installation-paused",
  UseDisable = "use-disable", EmailTestFailed = "email-test-failed" }

// Ports (implemented in Plan 3, faked in GapActivator.Testing)
public interface IUnitOfWork
{ IReadOnlyList<IDomainEvent> DequeueDomainEvents();   // events of every aggregate loaded or added in this scope, then cleared
  Task CommitAsync(CancellationToken ct); }
public interface IHistoryWriter { void Add(HistoryEntry entry); }          // staged; saved by CommitAsync
public sealed record RunFilter(ProcessId? ProcessId, IReadOnlyList<RunOutcome>? Outcomes,
    DateTimeOffset? From, DateTimeOffset? To, int Page, int PageSize); // newest CreatedAt first; Page>=1; PageSize 1..200
public sealed record RunPage(IReadOnlyList<Run> Items, int TotalCount);
public interface IRunHistoryReader
{ Task<RunPage> ListRunsAsync(RunFilter filter, CancellationToken ct);
  Task<IReadOnlyList<HistoryEntry>> ListEntriesForRunAsync(RunId runId, CancellationToken ct);   // oldest first
  Task<IReadOnlyDictionary<ProcessId, Run>> LastCompletedRunsAsync(CancellationToken ct); }
public sealed record RunOutput(bool Exists, string Text, bool Truncated);
public interface IRunOutputReader { Task<RunOutput> ReadTailAsync(RunId runId, int maxBytes, CancellationToken ct); }
public sealed record LaunchRequest(RunId RunId, string Executable, string Arguments, string WorkingDirectory,
    IReadOnlyList<KeyValuePair<string, string>> Environment);           // secrets already unprotected
public interface IRunningProcess : IAsyncDisposable
{ int Pid { get; } DateTimeOffset StartTime { get; }
  Task<int> Exited { get; }        // exit code; completes once
  void SignalCancel();             // idempotent
  Task KillTreeAsync(); }
public sealed class LaunchFailedException(string message, Exception? inner = null) : Exception(message, inner);
public interface IProcessLauncher
{ Task<IRunningProcess> LaunchAsync(LaunchRequest request, CancellationToken ct); // LaunchFailedException; OperationCanceledException if ct fires before the OS process exists
  IRunningProcess? TryAttach(RunId runId, int pid, DateTimeOffset processStartTime); }   // null when no live process matches both
public interface ISecretProtector { ProtectedSecret Protect(string plaintext); string Unprotect(ProtectedSecret secret); }
public sealed record EmailMessage(string Subject, string Body);
public sealed class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);
public interface IEmailSender { Task SendAsync(EmailAlertSettings smtp, EmailMessage message, CancellationToken ct); }
public enum NotificationSeverity { Information, Warning, Error }
public sealed record AppNotification(NotificationSeverity Severity, string Title, string Message, ProcessId? ProcessId, RunId? RunId);
public interface IAppNotifier { Task NotifyAsync(AppNotification notification, CancellationToken ct); }
public interface IServiceControl { Task RestartAsync(CancellationToken ct); }
public interface IHistoryRetention { Task<int> PurgeBeforeAsync(DateTimeOffset cutoff, CancellationToken ct); } // completed runs ended before cutoff, their entries, and run-less entries before cutoff
public interface ILogRetention { Task PurgeOlderThanAsync(int days, CancellationToken ct); }               // log files and run output files

// Events
public sealed record CycleFailed(string Error, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record GapActivatorStarted(int ReadoptedRuns, int AbandonedRuns, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record GapActivatorRestarted(DateTimeOffset OccurredAt) : IDomainEvent;   // restart requested from the app
public enum HistoryEntryKind { RunLate, RunSkipped, RunStarted, RunStopRequested, RunCompleted, ServiceProcessCrashed,
    ProcessBlocked, ProcessUnblocked, InstallationPaused, InstallationResumed, CycleFailed, GapActivatorStarted, GapActivatorRestarted }
public sealed record HistoryEntry(Guid Id, DateTimeOffset OccurredAt, HistoryEntryKind Kind, ProcessId? ProcessId,
    RunId? RunId, RunOutcome? Outcome, int? ExitCode, string? Message);
```

`DependencyInjection.AddApplication(this IServiceCollection services)` registers:
- `TimeProvider.System`, with `TryAdd` so the host or tests can override it;
- dispatchers, `WriteGate`, `RunSupervisor`, `CycleRunner` and `SchedulerLoop` as singletons;
- handlers, `CallerAccessor`, `AfterCommit`, `EventBuffer`, `RunStarter`, `EventPublisher` and notification handlers as scoped.

Each later task adds its own handler registrations there.

The test fakes share a singleton `InMemoryStore` that holds processes, runs, settings (seeded with `InstallationSettings.Default()`), history entries and purge calls:
- Scoped repositories track every aggregate they return or receive in the scoped `InMemoryUnitOfWork`. Adds and removes are staged and applied on `CommitAsync`.
- `InMemoryStore.FailNextCommits(int count, Exception error)` makes the next `count` commits of any scope throw.
- `InMemoryStore.FailOnLoad(ProcessId)` makes loading that process throw, which simulates a fault in one process.
- `FakeProcessLauncher` queues launch behaviours per executable path: succeed with a PID, fail, or block until cancelled. It records each `LaunchRequest` and `TryAttach` call.
- `FakeRunningProcess` exposes `Exit(int code)`, `SignalCancelCount` and `Killed`. Killing it completes `Exited` with code `-1`.
- `FakeSecretProtector` turns `x` into `ProtectedSecret("enc:" + x)`.
- `ApplicationTestHost` builds a `ServiceCollection` with `AddApplication()`, every fake and a `FakeTimeProvider` starting at `2030-01-01T12:00:00Z` in UTC. It exposes `Send<TCommand, TResult>(command, caller?)`, `Query<...>`, `Store`, `Launcher`, `Time`, `Email` and `AppNotifier`.

- [ ] **Step 1: Write the failing architecture tests** in `LayeringTests.cs`:
  - `Application_DependsOnlyOnDomain`: no dependency on `GapActivator.Infrastructure`, `GapActivator.Service`, `GapActivator.App`, `Ical.Net`, `Microsoft.EntityFrameworkCore`, `System.Diagnostics.Process`, `System.IO.File`, `System.IO.Directory`, `System.Net.Http`, `Microsoft.Win32` or `MailKit`.
  - `Domain_DependsOnNoOtherLayer` already covers Application.
  - `Application_PackageReferencesAreOnlyTheAllowedAbstractions`: XML check like `Domain_HasNoPackageOrProjectReferences`.
  - `Application_SourceNeverReadsTheSystemClock`: the same source scan as the domain, with the forbidden tokens `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `Thread.Sleep` and `Stopwatch`. `TimeProvider` is allowed here.
  - `EveryMessage_HasExactlyOneHandlerRegistered`: for every non-abstract type in `ApplicationAssembly.Reference` that implements `ICommand<>` or `IQuery<>`, the `ServiceCollection` built by `AddApplication()` has exactly one descriptor for the matching handler interface.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test -c Release --project tests/GapActivator.Architecture.Tests`
Expected: build FAILS (no Application project).

- [ ] **Step 3: Create the project, the ports, the messaging types, the fakes and `ApplicationTestHost`** exactly as listed above.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test -c Release`
Expected: PASS. `EveryMessage_HasExactlyOneHandlerRegistered` passes vacuously until Task 3 adds messages.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(application): add application project, ports and test fakes"
```

### Task 3: Dispatch pipeline

**Files:**
- Create: `Messaging/CallerAccessor.cs`, `WriteGate.cs`, `AfterCommit.cs`, `EventBuffer.cs`, `ICommandDispatcher.cs`, `CommandDispatcher.cs`, `IQueryDispatcher.cs`, `QueryDispatcher.cs`, `Events/HistoryEntryFactory.cs`, `Events/INotificationHandler.cs`, `Events/EventPublisher.cs`
- Test: `tests/GapActivator.Application.Tests/Messaging/CommandDispatcherTests.cs`, `Events/HistoryEntryFactoryTests.cs`, `Events/EventPublisherTests.cs`

**Interfaces:**
- Produces:

```csharp
public interface ICommandDispatcher
{ Task<TResult> SendAsync<TCommand, TResult>(TCommand command, Caller caller, CancellationToken ct) where TCommand : ICommand<TResult>; }
public interface IQueryDispatcher
{ Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, Caller caller, CancellationToken ct) where TQuery : IQuery<TResult>; }
public sealed class CallerAccessor { public Caller Current { get; internal set; } = Caller.System; }   // scoped; queries read it for masking
public sealed class AfterCommit { public void Enqueue(Func<CancellationToken, Task> action); }      // scoped
public sealed class EventBuffer { public void Record(IDomainEvent e); }                             // scoped; for events no aggregate raises
public interface INotificationHandler { Task HandleAsync(IDomainEvent domainEvent, CancellationToken ct); }
public static class HistoryEntryFactory { public static HistoryEntry? From(IDomainEvent e, Func<Guid> newId); }
```

- `CommandDispatcher.SendAsync` order:
  1. Authorize: an `IInternalCommand` requires `caller.IsSystem`, and any other command requires `caller.CanChange`, else `Unauthorized`.
  2. Unless the command is `IUngatedCommand`, wait on `WriteGate` (a `SemaphoreSlim(1,1)`).
  3. Create an async scope, set `CallerAccessor.Current` and resolve `ICommandHandler<TCommand,TResult>`.
  4. `HandleAsync`.
  5. Unless ungated: take the events from `uow.DequeueDomainEvents()` plus the `EventBuffer`, `Add` each `HistoryEntryFactory.From(...)` result to `IHistoryWriter`, then `CommitAsync`.
  6. Release the gate.
  7. Run the `AfterCommit` actions in order, each in a try/catch that logs.
  8. `EventPublisher.PublishAsync(events)`.
  9. Dispose the scope and return.

  If step 4 or 5 throws, nothing is committed or published, the actions are dropped, the gate is released and the exception propagates.
- `QueryDispatcher` creates a scope, sets the caller and uses no gate. Any caller may query.
- `EventPublisher` logs each event at Information, then calls every `INotificationHandler`. A handler exception is logged at Warning and swallowed (BI-096). An `OperationCanceledException` while `ct` is cancelled propagates.
- `HistoryEntryFactory`:
  - Maps every domain and application event to its `HistoryEntryKind`, copying `ProcessId`, `RunId`, `Outcome` and `ExitCode`.
  - `Message` is English detail: the stop reason name, `"{n} consecutive failures"` for ProcessBlocked, the error for CycleFailed, and `"{readopted} re-adopted, {abandoned} abandoned"` for GapActivatorStarted.
  - Returns null for an unknown event type.

- [ ] **Step 1: Write the failing tests.** Use probe messages declared in the test project (`ProbeCommand`, `ProbeInternalCommand`, `ProbeUngatedCommand`) whose handlers mutate a process and enqueue actions:
  - `Send_ViewerCaller_IsRejectedUnauthorized`
  - `Send_InternalCommandFromOperator_IsRejectedUnauthorized`
  - `Send_Success_CommitsThenRunsActionsThenPublishes`: a recording notification handler and an action append to a shared list. The expected order is `["commit", "action", "event"]`.
  - `Send_HandlerThrows_NothingCommittedOrPublished`
  - `Send_CommitThrows_ActionsDroppedAndGateReleased`: a second send succeeds afterwards.
  - `Send_DomainEvents_AreWrittenToHistoryInTheSameCommit`: the entries are in `Store.History` only after commit.
  - `Send_ActionThrows_LaterActionsAndEventsStillRun`
  - `Send_TwoConcurrentCommands_NeverOverlap`: each handler waits on a `TaskCompletionSource` and an in-flight counter must never exceed 1.
  - `Send_UngatedCommand_RunsWhileGateIsHeld`
  - `Publish_HandlerThrows_OtherHandlersStillRun` (BI-096)
  - `HistoryEntryFactory_MapsEveryKnownEvent`: a theory with one row per event type.

- [ ] **Step 2: Run** `dotnet test -c Release --project tests/GapActivator.Application.Tests`. Expected: build FAILS.
- [ ] **Step 3: Implement** the types above.
- [ ] **Step 4: Run** the same command. Expected: PASS.
- [ ] **Step 5: Commit** `feat(application): add command and query dispatch pipeline`

### Task 4: Process catalog commands (register, update, delete)

**Files:**
- Create: `Processes/ProcessDefinition.cs`, `ProcessDefinitionMapper.cs`, `RegisterProcess.cs`, `UpdateProcess.cs`, `DeleteProcess.cs`
- Test: `tests/GapActivator.Application.Tests/Processes/RegisterProcessTests.cs`, `UpdateProcessTests.cs`, `DeleteProcessTests.cs`

**Interfaces:**
- Consumes: Task 2 ports, Task 3 dispatcher, `IOccurrenceCalculator` (registered by the host; the test host registers `FakeOccurrenceCalculator(1h)`).
- Produces:

```csharp
public enum ScheduleInputKind { OneTime, Recurring }
public sealed record ScheduleInput(Guid? Id, ScheduleInputKind Kind, DateTimeOffset At, string? Rule); // At = instant (OneTime) or anchor (Recurring)
public sealed record EnvironmentVariableInput(string Name, string? Value, bool IsSecret);              // secret with null Value = keep the stored secret of that name
public sealed record ProcessDefinition(string Name, string Description, ProcessKind Kind, string Executable,
    string Arguments, string? WorkingDirectory, IReadOnlyList<EnvironmentVariableInput> Environment,
    TimeSpan? TimeLimit, TimeSpan? GracePeriod, IReadOnlyList<ScheduleInput> Schedules);
public sealed record ProcessSaved(ProcessId Id, IReadOnlyList<string> ProcessesSharingExecutable);   // warning list (names), BI-010
public sealed record RegisterProcess(ProcessDefinition Definition) : ICommand<ProcessSaved>;
public sealed record UpdateProcess(ProcessId Id, ProcessDefinition Definition) : ICommand<ProcessSaved>;
public sealed record DeleteProcess(ProcessId Id) : ICommand<Unit>;
```

Rules:
- Name uniqueness uses `NameExistsAsync(name, excluding)` and rejects with `NameTaken` (BI-001, Q-8).
- **Schedules on update.** An input whose `Id` matches an existing schedule with the same definition keeps that instance. For OneTime the same `At`; for Recurring the same `Rule.Value` and `Anchor`. A kept instance keeps `NextOccurrenceAt` and `FulfilledAt`.
- **New schedules.** Any other input creates a new schedule:
  - OneTime: rejected with `ScheduleInPast` when `At <= now` (Q-12).
  - Recurring: `RecurringSchedule.Create(rule, anchor, notBefore: now, calc)` (Q-13).
- **Secrets.** `IsSecret` with a value is protected with `ISecretProtector`. `IsSecret` with a null value reuses the stored secret of the same name (case-insensitive), or is rejected with `SecretValueRequired`.
- **Shared executable.** The warning lists the names from `ListByExecutableAsync(executable, excluding self)`.
- **Kind change.** UpdateProcess rejects a Kind change while `GetActiveForProcessAsync` returns a run (`HasActiveRun`).
- **Delete.** DeleteProcess rejects when a run is active (`HasActiveRun`), otherwise calls `Remove`. History stays, because runs keep a snapshot of the name.
- An unknown id is rejected with `NotFound`.

- [ ] **Step 1: Write the failing tests**
  - `Register_Valid_StoresEnabledProcess`
  - `Register_DuplicateNameDifferentCase_IsRejectedNameTaken` (BI-001)
  - `Register_SameExecutableAsAnother_SucceedsWithWarning` (BI-010)
  - `Register_OneTimeInThePast_IsRejected` and `Register_OneTimeAtNow_IsRejected` (Q-12)
  - `Register_RecurringWithOldAnchor_FirstOccurrenceIsNotBeforeNow` (Q-13)
  - `Register_SecretVariable_IsStoredProtected`
  - `Register_InvalidRule_ThrowsDomainException`
  - `Update_UnchangedSchedule_KeepsNextOccurrenceAndFulfilledAt` (Review Focus 3): register an hourly schedule at T, advance time by 5 h, call `ConsumeDueOccurrences` on the stored aggregate (`host.Store.Processes[id]`), then update the description only. `NextOccurrenceAt` must be unchanged.
  - `Update_ChangedSchedule_IsRecreatedFromNow`
  - `Update_SecretWithoutValue_KeepsStoredSecret`
  - `Update_SecretWithoutValueAndNoStoredSecret_IsRejected`
  - `Update_KindChangeWithActiveRun_IsRejected`
  - `Update_UnknownId_IsRejectedNotFound`
  - `Delete_WithActiveRun_IsRejected`
  - `Delete_Idle_RemovesProcessAndKeepsRuns`
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(application): add process registration, update and delete`

### Task 5: Run supervisor and run-recording commands

**Files:**
- Create: `Runs/RunStartTicket.cs`, `RunStarter.cs`, `RunSupervisor.cs`, `RecordRunLaunched.cs`, `RecordRunLaunchFailed.cs`, `RecordLaunchAborted.cs`, `RecordRunExited.cs`, `StopRun.cs`, `StartPendingRun.cs`
- Test: `tests/GapActivator.Application.Tests/Runs/RunSupervisorTests.cs`, `RunRecordingTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record RunStartTicket(RunId RunId, ProcessId ProcessId, LaunchRequest Request, TimeSpan? TimeLimit, TimeSpan GracePeriod);
public sealed class RunStarter   // scoped
{ public Run Begin(Process process, InstallationSettings settings, RunTrigger trigger, DateTimeOffset? scheduledFor, bool isLate); }
  // Run.Begin(...) + IRunRepository.Add + AfterCommit.Enqueue(supervisor.Start(ticket)); ticket env = plain values + Unprotect(secrets);
  // WorkingDirectory = Launch.EffectiveWorkingDirectory; TimeLimit = process.EffectiveTimeLimit(settings.DefaultTimeLimit);
  // GracePeriod = process.EffectiveGracePeriod(settings.DefaultGracePeriod)
public sealed class RunSupervisor   // singleton
{ public void Start(RunStartTicket ticket);
  public void Adopt(RunId runId, ProcessId processId, IRunningProcess process, TimeSpan? remainingTimeLimit, TimeSpan gracePeriod, bool stopping);
  public void Stop(RunId runId);                 // uses the ticket's grace period
  public void StartAfterEnd(RunId runId, ProcessId processId);
  public bool IsTracking(ProcessId processId);
  public IReadOnlyCollection<RunId> TrackedRuns { get; }
  public Task WaitForEndAsync(RunId runId, CancellationToken ct);   // completes after the end was recorded (or at once if not tracked)
  public Task StopAllAsync(CancellationToken ct); }                 // Task 8
public sealed record RecordRunLaunched(RunId RunId, int Pid, DateTimeOffset StartTime) : IInternalCommand<Unit>;
public sealed record RecordRunLaunchFailed(RunId RunId, string Error) : IInternalCommand<Unit>;
public sealed record RecordLaunchAborted(RunId RunId) : IInternalCommand<Unit>;
public sealed record RecordRunExited(RunId RunId, int ExitCode, bool Killed) : IInternalCommand<Unit>;
public sealed record StopRun(RunId RunId, StopReason Reason) : IInternalCommand<Unit>;
public sealed record StartPendingRun(ProcessId ProcessId) : IInternalCommand<Unit>;
```

Supervisor behaviour. Each tracked run has a background task:
1. **Launch.** `LaunchAsync(request, launchCts.Token)`.
   - `LaunchFailedException` → send `RecordRunLaunchFailed`.
   - `OperationCanceledException` after `Stop` → send `RecordLaunchAborted`.
2. **Launched.** Send `RecordRunLaunched`. If `Stop` was called meanwhile, `SignalCancel` at once (§8 "Cancel during Launching").
3. **Time limit.** Wait for the time limit in chunks of at most 1 day with `Task.Delay(chunk, time, ct)`, then send `StopRun(TimeLimit)`.
4. **Stop.** `SignalCancel`, wait for the grace period, and if the process has not exited, call `KillTreeAsync` and mark it killed.
5. **Exit.** On `Exited`, send `RecordRunExited(code, killed)`. If the send throws, retry 3 times, 1 s apart, then log Error and give up (Review Focus 2).
6. **End.** Remove the entry, complete `WaitForEndAsync`, and if `StartAfterEnd` was set, send `StartPendingRun`.

`Start` for a process that already has an entry is ignored and logged. This is the in-memory half of BI-041.

Handler rules:
- `RecordRunLaunched` → `MarkRunning`.
- `RecordRunLaunchFailed` → `FailToStart` + `ApplyRunOutcome`.
- `RecordLaunchAborted` → `AbortLaunch` + `ApplyRunOutcome`.
- `RecordRunExited` → `Exit` + `process.ApplyRunOutcome(outcome, settings.FailureLimit, now)`. The process may have been deleted only if its run was inactive, which cannot happen, so a missing process is logged and the run is still completed.
- `StopRun`: a no-op when the run is not active (Review Focus 1). Otherwise `RequestStop`, plus `AfterCommit` → `supervisor.Stop`.
- `StartPendingRun`: re-checks paused, Blocked, Service+Disabled and active run, and silently logs and skips when any applies. Otherwise `ConsumeDueOccurrences` + `RunStarter.Begin(trigger: Restart)`.

- [ ] **Step 1: Write the failing tests** (drive time with `FakeTimeProvider.Advance`; await `WaitForEndAsync`):
  - `Start_LaunchSucceedsAndExitsZero_RunSucceededAndCounterReset`
  - `Start_ExitNonZero_RunFailedRetryPending`
  - `Start_LaunchFails_FailedToStartWithError`
  - `Start_TimeLimitExpires_SignalsThenKillsAfterGrace_TimedOut`: advance by the limit, check `SignalCancelCount == 1` and not killed; advance by the grace period, check `Killed`, `Outcome == TimedOut` and `Run.Killed`.
  - `Start_ChildExitsWithinGrace_NotKilled`
  - `Start_TimeLimitLongerThanTimerMaximum_StillFires`: 60 days (Review Focus 5).
  - `Stop_WhileLaunching_LaunchAbortedCancelled`
  - `Stop_AfterOsProcessCreatedButBeforeRecorded_SignalsImmediately`
  - `StopRun_AfterRunCompleted_IsNoOp` (Review Focus 1)
  - `RecordRunExited_CommitFailsOnce_RetriedAndRecorded`
  - `RecordRunExited_CommitAlwaysFails_LoggedAndEntryRemoved` (Review Focus 2)
  - `Start_SecondTicketSameProcess_Ignored` (BI-041)
  - `RunStarter_SecretVariable_IsUnprotectedInRequest`
  - `RunStarter_NoWorkingDirectory_UsesExecutableFolder`
  - `RunStarter_ServiceKind_HasNoTimeLimit`
  - `RunStarter_ProcessTimeLimit_WinsOverDefault`
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS (run the suite 3 times to catch timing flakiness; every wait uses `FakeTimeProvider`, never real time).
- [ ] **Step 5: Commit** `feat(application): add run supervisor and run recording commands`

### Task 6: Operator run and lifecycle commands

**Files:**
- Create: `Runs/RunNow.cs`, `RestartRun.cs`, `CancelRun.cs`, `Processes/EnableProcess.cs`, `DisableProcess.cs`, `UnblockProcess.cs`
- Test: `tests/GapActivator.Application.Tests/Runs/ManualRunTests.cs`, `Processes/LifecycleCommandTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record RunNow(ProcessId Id) : ICommand<RunId>;
public sealed record RestartRun(ProcessId Id) : ICommand<Unit>;
public sealed record CancelRun(RunId Id) : ICommand<Unit>;
public sealed record EnableProcess(ProcessId Id) : ICommand<Unit>;
public sealed record DisableProcess(ProcessId Id, bool CancelActiveRun) : ICommand<Unit>;
public sealed record UnblockProcess(ProcessId Id, bool RunNow) : ICommand<Unit>;
```

Rules (checked in this order; each check is a rejection code):

- **RunNow**
  1. `InstallationPaused` (Q-1).
  2. `ProcessBlocked`; the app offers "Unblock and run".
  3. Service kind and Disabled → `ProcessDisabled` (Q-15).
  4. An active run → `RunAlreadyActive`; the app then asks and sends RestartRun.
  5. Otherwise `ConsumeDueOccurrences(now)` (Q-4) and `RunStarter.Begin(Manual)`.

  It does **not** call `EnforceFailureLimit` (Q-3), and it runs a Disabled RunToCompletion process (Q-15).
- **RestartRun:** the same first three checks. With no active run it behaves like RunNow with trigger `Restart`. With an active run it does `RequestStop(UserCancel)`, then `AfterCommit` → `supervisor.Stop` and `StartAfterEnd`. The new run starts only after the old one was recorded as ended (§8).
- **CancelRun:** a missing or inactive run → `RunNotActive` (BI-058). A Service kind → `UseDisable`. Otherwise `RequestStop(UserCancel)` + `AfterCommit` → `supervisor.Stop` (BI-056).
- **EnableProcess:**
  1. `Enable(now, calc)` discards missed occurrences (Q-11).
  2. `EnforceFailureLimit(settings.FailureLimit, now)`.
  3. For a Service kind that is still Enabled, with the installation not paused and no active run: `RunStarter.Begin(ServiceKeepAlive)`.
- **DisableProcess:** `Disable(now)`. A Service kind always stops its active run: `RequestStop(UserCancel)`, which ends Stopped. A RunToCompletion run is stopped only when `CancelActiveRun`.
- **UnblockProcess:** when `RunNow` is set and the installation is paused, reject `InstallationPaused` before changing anything. Otherwise `Unblock(now, calc)`, and if `RunNow`, `RunStarter.Begin(Manual)`. A discarded OneTime occurrence becomes fulfilled and creates **no** run.

- [ ] **Step 1: Write the failing tests**
  - `RunNow_Idle_StartsManualRunAndConsumesDueOccurrences` (BI-061, Q-4)
  - `RunNow_WhilePaused_IsRejected` (Q-1)
  - `RunNow_Blocked_IsRejected`
  - `RunNow_DisabledRunToCompletion_Runs` (Q-15)
  - `RunNow_DisabledService_IsRejected` (Q-15)
  - `RunNow_WithActiveRun_IsRejectedRunAlreadyActive` (BI-062)
  - `RunNow_CounterAboveLoweredLimit_StillRuns` (Q-3, Review Focus 4)
  - `RestartRun_Active_CancelsThenStartsAfterEnd_NeverOverlapping`: the old run's outcome is Cancelled, the new run's trigger is Restart, and the new launch request is recorded only after the old `Exited` completes (BI-062).
  - `RestartRun_ServiceKind_OldRunStoppedNewRunStarted`
  - `CancelRun_NothingActive_IsRejected` (BI-058)
  - `CancelRun_Running_EndsCancelledCounterUnchanged`
  - `CancelRun_ServiceKind_IsRejectedUseDisable`
  - `Enable_Service_StartsAtOnce`
  - `Enable_CounterAtLimit_BlocksInsteadOfStarting`
  - `Disable_RunToCompletion_LetsCurrentRunFinish`
  - `Disable_WithCancel_EndsCancelled`
  - `Disable_Service_StopsRunAsStopped`
  - `Unblock_WithRunNow_StartsManualRun`
  - `Unblock_WithRunNowWhilePaused_IsRejectedAndStaysBlocked`
  - `Unblock_DiscardedOneTime_IsFulfilledWithoutRun`
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(application): add run-now, restart, cancel and lifecycle commands`

### Task 7: The cycle and the scheduler loop

**Files:**
- Create: `Cycle/EvaluateProcess.cs`, `PurgeHistory.cs`, `RecordCycleFailure.cs`, `CycleRunner.cs`, `SchedulerLoop.cs`, `Queries/GetCycleSettings.cs`, `Queries/ListProcessIds.cs`
- Test: `tests/GapActivator.Application.Tests/Cycle/EvaluateProcessTests.cs`, `CycleRunnerTests.cs`, `SchedulerLoopTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record EvaluateProcess(ProcessId Id) : IInternalCommand<Unit>;
public sealed record PurgeHistory(int HistoryRetentionDays) : IInternalCommand<int>;
public sealed record RecordCycleFailure(string Error) : IInternalCommand<Unit>;
public sealed record CycleSettings(bool IsPaused, TimeSpan CycleInterval, int LogRetentionDays, int HistoryRetentionDays);
public sealed record GetCycleSettings : IQuery<CycleSettings>;
public sealed record ListProcessIds : IQuery<IReadOnlyList<ProcessId>>;
public sealed class CycleRunner { public Task<TimeSpan> RunOnceAsync(CancellationToken ct); }   // returns the interval to wait
public sealed class SchedulerLoop { public Task RunAsync(CancellationToken stoppingToken); }     // Task 8 adds startup and shutdown
```

`EvaluateProcess` rules:
- **Blocked:** nothing (BI-008).
- **Lowered limit:** first `EnforceFailureLimit(settings.FailureLimit, now)` (BI-044, Q-3). If that blocks, stop here.
- **RunToCompletion:** `decision = EvaluateDue(now, 2 × CycleInterval, calc)`.
  - Not due: nothing.
  - Due by `Schedule` while a run is active: `Run.Skip(...)` for `decision.ScheduledFor` (BI-027).
  - Due by `Retry` while a run is active: nothing.
  - Due with no active run: `RunStarter.Begin(decision.Trigger, decision.ScheduledFor, decision.IsLate)`.
- **Service kind, Enabled:**
  - Active run Running: `MarkServiceFoundRunning(now)`.
  - Active run Launching or Stopping: nothing (BI-070).
  - No active run and `!supervisor.IsTracking`: `RunStarter.Begin(ServiceKeepAlive)` (BI-067, BI-068).

`CycleRunner.RunOnceAsync`:
1. Query `GetCycleSettings` as `Caller.System`.
2. **Retention**, once per local calendar day (`TimeZoneInfo.ConvertTime(now, time.LocalTimeZone).Date`). It records the day only after success, so a failure frees the day (BI-086). Its parts:
   - `ILogRetention.PurgeOlderThanAsync(LogRetentionDays)`, called outside the gate;
   - `PurgeHistory(HistoryRetentionDays)` (BI-089).

   Each part is in its own try/catch that logs a Warning, and neither blocks dispatch.
3. When paused, return `CycleInterval` without dispatching (Q-1).
4. Otherwise send `EvaluateProcess` for each id in `ListProcessIds`. Each send is in its own try/catch: an exception is logged at Error with the process id and the loop continues (BI-039). A shutdown `OperationCanceledException` propagates.
5. Return `CycleInterval`.

`SchedulerLoop.RunAsync` loop:
1. `start = now`, then `interval = await cycle.RunOnceAsync(ct)`.
2. On an unexpected exception: log Error and send `RecordCycleFailure(ex.Message)`, which records a `CycleFailed` through the `EventBuffer`. If that send fails, log it and `EventPublisher.PublishAsync([new CycleFailed(...)])` directly. Use the last known interval, 60 s at first (BI-040, BI-093).
3. `await Task.Delay(max(0, interval − (now − start)), time, ct)`. The interval is re-read every cycle (BI-037).
4. Exit quietly on stopping-token cancellation, with no CycleFailed (BI-040).

Runs never block the loop, because launches go to the supervisor (BI-038).

- [ ] **Step 1: Write the failing tests**
  - `Evaluate_DueSchedule_StartsScheduledRun`
  - `Evaluate_LateOccurrence_RunIsLate` (more than 2 × interval)
  - `Evaluate_DueWhileActive_RecordsSkipped` (BI-027)
  - `Evaluate_RetryPending_StartsRetryRun`
  - `Evaluate_RetryWhileActive_DoesNothing`
  - `Evaluate_Blocked_DoesNothing` (BI-008)
  - `Evaluate_CounterAboveLoweredLimit_BlocksWithoutRunning` (BI-044, Review Focus 4)
  - `Evaluate_ServiceNotRunning_StartsKeepAlive` (BI-067)
  - `Evaluate_ServiceRunning_ResetsCounter`
  - `Evaluate_ServiceLaunching_DoesNothing` (BI-070)
  - `Cycle_Paused_DispatchesNothingButRunsRetention` (Q-1)
  - `Cycle_OneProcessThrows_OthersStillEvaluated` (BI-039): a store fault injector throws for one process id.
  - `Cycle_RetentionFails_DispatchStillRunsAndRetriedSameDay` (BI-086)
  - `Cycle_RetentionSucceeded_NotRepeatedSameDay`
  - `Cycle_RetentionNextLocalDay_RunsAgain`, with `FakeTimeProvider.SetLocalTimeZone` to a non-UTC zone so that local midnight differs from UTC midnight.
  - `Loop_IntervalChanged_NextWaitUsesNewInterval` (BI-037, Review Focus 4)
  - `Loop_LongRunActive_NextCycleStillOnTime` (BI-038)
  - `Loop_CycleThrows_RecordsCycleFailedAndContinues` (BI-040)
  - `Loop_StoppingToken_EndsWithoutCycleFailed` (BI-040)
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(application): add scheduler cycle, retention and loop`

### Task 8: Startup reconciliation and shutdown

**Files:**
- Create: `Cycle/ReconcileOnStartup.cs`
- Modify: `Cycle/SchedulerLoop.cs` (reconcile before the first cycle; `StopAllAsync` in `finally`), `Runs/RunSupervisor.cs` (`StopAllAsync`)
- Test: `tests/GapActivator.Application.Tests/Cycle/ReconcileOnStartupTests.cs`, `Runs/ShutdownTests.cs`

**Interfaces:**
- Produces: `public sealed record ReconcileOnStartup : IInternalCommand<Unit>;`

**ReconcileOnStartup**, for each `ListActiveAsync` run:
- **Launching** (no PID): `Abandon` + `ApplyRunOutcome`. This gives Abandoned for RunToCompletion and Interrupted for Service kind.
- **Running or Stopping:** call `launcher.TryAttach(runId, pid, processStartTime)`.
  - Null → `Abandon` + `ApplyRunOutcome` (BI-071).
  - Otherwise `AfterCommit` → `supervisor.Adopt(...)` (BI-072). The remaining time limit is `limit − (now − StartedAt)`, so zero or less makes the supervisor send `StopRun(TimeLimit)` at once. `stopping` is `State == Stopping`, so a re-adopted Stopping run is signalled again and killed after its grace period.
- Finally record `GapActivatorStarted(readopted, abandoned)` in the `EventBuffer`.

Reconciliation runs once per start, not every cycle (BI-073). A Service-kind process found gone is relaunched by the first cycle's `EvaluateProcess` without counting a failure (BI-066). If reconciliation throws, `SchedulerLoop` logs it at Critical and rethrows, so the host stops and the service's recovery actions restart it.

**`RunSupervisor.StopAllAsync(ct)`:**
1. For each tracked run, send `StopRun(runId, StopReason.Shutdown)`.
2. Await every `WaitForEndAsync`, bounded by `ct`. `SchedulerLoop` passes a token that expires after the largest tracked grace period plus 15 s.
3. Runs still active when the token expires stay in the database for the next start's reconciliation.

A launch in flight is aborted and ends **Interrupted** (BI-074, Review Focus 5).

- [ ] **Step 1: Write the failing tests**
  - `Reconcile_RunningAndAlive_ReadoptedAndSupervised` (BI-072): after `Exit(0)` the run is Succeeded.
  - `Reconcile_RunningButGone_AbandonedCountsFailure` (BI-071)
  - `Reconcile_ServiceRunGone_InterruptedNoFailureAndRelaunchedByFirstCycle` (BI-066)
  - `Reconcile_LaunchingRun_Abandoned`
  - `Reconcile_StoppingAndAlive_SignalledAgainThenKilled`
  - `Reconcile_TimeLimitAlreadyExceeded_StopsAtOnceTimedOut`
  - `Reconcile_RecordsGapActivatorStartedWithCounts`
  - `Shutdown_ActiveRuns_EndInterruptedNotFailures` (BI-074)
  - `Shutdown_DuringLaunch_EndsInterrupted` (Review Focus 5)
  - `Shutdown_ChildIgnoresSignal_KilledAfterGrace`
  - `Loop_ReconcileThrows_Rethrows`
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(application): add startup reconciliation and graceful shutdown`

### Task 9: Settings commands

**Files:**
- Create: `Settings/SettingsInput.cs`, `UpdateSettings.cs`, `PauseInstallation.cs`, `ResumeInstallation.cs`, `SendTestEmail.cs`, `RestartGapActivator.cs`
- Test: `tests/GapActivator.Application.Tests/Settings/SettingsCommandTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum SecretChangeKind { Keep, Clear, Set }
public sealed record SecretChange(SecretChangeKind Kind, string? NewValue);     // NewValue required for Set
public sealed record EmailInput(bool Enabled, string Host, int Port, TlsMode Tls, string? Username,
    SecretChange Password, string Sender, IReadOnlyList<string> Recipients, IReadOnlyList<AlertKind> Alerts);
public sealed record SettingsInput(TimeSpan CycleInterval, int FailureLimit, TimeSpan? DefaultTimeLimit,
    TimeSpan DefaultGracePeriod, int LogRetentionDays, int HistoryRetentionDays, LogLevel LogLevel,
    bool CheckForUpdates, EmailInput Email);
public sealed record UpdateSettings(SettingsInput Settings) : ICommand<Unit>;
public sealed record PauseInstallation : ICommand<Unit>;
public sealed record ResumeInstallation : ICommand<Unit>;
public sealed record SendTestEmail(EmailInput Email) : ICommand<Unit>, IUngatedCommand;
public sealed record RestartGapActivator : ICommand<Unit>;
```

Rules:
- **UpdateSettings** calls `Change(...)` and `ChangeEmail(EmailAlertSettings.Create(...))`. The password is kept, cleared, or set through `ISecretProtector.Protect`.
- **ResumeInstallation** calls `Resume(now)`, then `ConsumeDueOccurrences(now, calc)` on every process (Q-14). Retries stay pending.
- **SendTestEmail** builds the settings with `enabled: true`, so the domain validates them. `Keep` uses the stored password. It sends the subject `"GapActivator test e-mail"` and maps `EmailDeliveryException` to `EmailTestFailed` with the delivery message.
- **RestartGapActivator** records `GapActivatorRestarted` in the `EventBuffer` (BI-094), then `AfterCommit` → `IServiceControl.RestartAsync`.

- [ ] **Step 1: Write the failing tests**
  - `UpdateSettings_Valid_Saved`
  - `UpdateSettings_PasswordKeep_KeepsCiphertext`, `UpdateSettings_PasswordSet_Protects`, `UpdateSettings_PasswordClear_Removes`
  - `UpdateSettings_Invalid_ThrowsDomainException`
  - `Pause_ThenCycle_StartsNothing` (Q-1)
  - `Pause_ActiveRun_FinishesNormally` (Q-1)
  - `Resume_MissedOccurrencesDiscarded_NoCatchUpRun` (Q-14)
  - `Resume_RetryPending_StillRetriedNextCycle` (Q-14)
  - `SendTestEmail_UsesStoredPasswordOnKeep`
  - `SendTestEmail_DeliveryFails_IsRejectedEmailTestFailed`
  - `SendTestEmail_DoesNotWaitForTheWriteGate`
  - `Restart_RecordsEventThenCallsServiceControl` (BI-094)
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(application): add settings, pause, test e-mail and restart commands`

### Task 10: Notifications

**Files:**
- Create: `Notifications/AlertRules.cs`, `EmailAlertHandler.cs`, `AppNotificationHandler.cs`
- Test: `tests/GapActivator.Application.Tests/Notifications/NotificationTests.cs`

**Interfaces:**
- Produces: `public static class AlertRules { public static AlertKind? KindOf(IDomainEvent e); }`, with this mapping:

| Event | AlertKind |
|---|---|
| `ProcessBlocked` | `ProcessBlocked` |
| `RunCompleted` with outcome Failed, FailedToStart or Abandoned | `RunFailed` |
| `RunCompleted` with outcome TimedOut | `RunTimedOut` |
| `ServiceProcessCrashed` | `ServiceProcessCrashed` |
| `CycleFailed` | `CycleFailed` |
| `GapActivatorRestarted` | `ServiceRestarted` |
| anything else, including `RunCompleted` with outcome Crashed (`ServiceProcessCrashed` already alerts for it) | null |

- `EmailAlertHandler` sends only when `settings.Email.Enabled` and `KindOf(e)` is in `settings.Email.Alerts`.
  - Subject: `"[GapActivator] {Environment.MachineName}: {title}"`.
  - Body: the process name (from `IProcessRepository`, or `"(deleted process)"`), the run id, the outcome, the exit code and the time in the machine's local zone.
  - It throws on failure; the publisher logs it (BI-096).
- `AppNotificationHandler` sends an `AppNotification` for every event with a non-null `KindOf`, whatever the e-mail settings. ProcessBlocked, CycleFailed and failures are `Error`, TimedOut and Crashed are `Warning`, and Restarted is `Information`.

- [ ] **Step 1: Write the failing tests**
  - `KindOf_MapsEveryRow`: a theory over the table.
  - `Blocked_EmailEnabledForBlocked_SendsOneEmailNamingProcess` (BI-095)
  - `Failure_EmailDisabled_SendsNoEmailButNotifiesApp`
  - `Failure_KindNotSelected_SendsNoEmail`
  - `EmailSenderThrows_BlockStillSaved` (BI-096): run a real failing run up to the limit.
  - `CycleFailed_NotifiesAppAndEmails` (BI-093)
  - `Crashed_SendsOneCrashAlertNotAlsoRunFailed`
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(application): add e-mail and app notification handlers`

### Task 11: Queries

**Files:**
- Create: `Queries/Views.cs`, `ListProcesses.cs`, `GetProcess.cs`, `GetDashboard.cs`, `ListRuns.cs`, `GetRun.cs`, `GetSettings.cs`, `PreviewSchedule.cs`
- Test: `tests/GapActivator.Application.Tests/Queries/QueryTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record ActiveRunView(RunId RunId, RunState State, RunTrigger Trigger, DateTimeOffset? StartedAt, int? Pid);
public sealed record ProcessSummary(ProcessId Id, string Name, ProcessKind Kind, ProcessLifecycle Lifecycle,
    int ConsecutiveFailures, bool RetryPending, DateTimeOffset? NextScheduledAt, ActiveRunView? ActiveRun,
    RunOutcome? LastOutcome, DateTimeOffset? LastEndedAt);
public sealed record EnvironmentVariableView(string Name, string? Value, bool IsSecret);
public sealed record ScheduleView(Guid Id, ScheduleInputKind Kind, DateTimeOffset At, string? Rule,
    DateTimeOffset? NextOccurrenceAt, DateTimeOffset? FulfilledAt);
public sealed record ProcessDetails(ProcessSummary Summary, string Description, string Executable, string? Arguments,
    string? WorkingDirectory, IReadOnlyList<EnvironmentVariableView> Environment, TimeSpan? TimeLimit, TimeSpan? GracePeriod,
    IReadOnlyList<ScheduleView> Schedules, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool SensitiveHidden);
public sealed record RunRow(RunId Id, ProcessId ProcessId, string ProcessName, ProcessKind Kind, RunTrigger Trigger,
    RunState State, RunOutcome? Outcome, bool IsLate, DateTimeOffset? ScheduledFor, DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, int? ExitCode, bool Killed);
public sealed record RunDetails(RunRow Run, string? Error, IReadOnlyList<HistoryEntry> Entries, RunOutput Output);
public sealed record UpcomingRun(ProcessId ProcessId, string ProcessName, DateTimeOffset At);
public sealed record Dashboard(bool IsPaused, IReadOnlyList<RunRow> ActiveRuns, IReadOnlyList<UpcomingRun> Upcoming,
    IReadOnlyList<RunRow> RecentFailures);
public sealed record EmailView(bool Enabled, string Host, int Port, TlsMode Tls, string? Username, bool HasPassword,
    string Sender, IReadOnlyList<string> Recipients, IReadOnlyList<AlertKind> Alerts);
public sealed record SettingsView(TimeSpan CycleInterval, int FailureLimit, TimeSpan? DefaultTimeLimit, TimeSpan DefaultGracePeriod,
    int LogRetentionDays, int HistoryRetentionDays, LogLevel LogLevel, bool CheckForUpdates, bool IsPaused, EmailView Email);
public sealed record ListProcesses : IQuery<IReadOnlyList<ProcessSummary>>;          // ordered by name
public sealed record GetProcess(ProcessId Id) : IQuery<ProcessDetails>;                // NotFound rejection when missing
public sealed record GetDashboard : IQuery<Dashboard>;
public sealed record RunRowPage(IReadOnlyList<RunRow> Items, int TotalCount);
public sealed record ListRuns(RunFilter Filter) : IQuery<RunRowPage>;
public sealed record GetRun(RunId Id) : IQuery<RunDetails>;
public sealed record GetSettings : IQuery<SettingsView>;
public sealed record PreviewSchedule(string Rule, DateTimeOffset Anchor, int Count) : IQuery<IReadOnlyList<DateTimeOffset>>;
```

Views never expose domain types other than ids and enums.

Rules:
- **Masking (§10):** when `CallerAccessor.Current.CanChange` is false, `Arguments` is null, every `EnvironmentVariableView.Value` is null and `SensitiveHidden` is true. A secret's `Value` is always null, for every caller. `GetSettings` never returns the password, only `HasPassword`.
- **Dashboard:**
  - active runs come from `ListActiveAsync`;
  - upcoming runs are the 10 earliest `NextScheduledAt` among Enabled processes;
  - recent failures are the 10 newest completed runs whose outcome has `FailureEffect.Increment` and that ended in the last 24 h.
- **PreviewSchedule:** `Count` must be 1..50. Occurrences start at or after `max(Anchor, now)`, using the Q-13 semantics. An invalid rule throws `DomainException`.
- **GetRun:** output comes from `IRunOutputReader.ReadTailAsync(runId, 1_048_576)`.

- [ ] **Step 1: Write the failing tests**
  - `GetProcess_Viewer_HidesArgumentsAndValues`
  - `GetProcess_Operator_ShowsPlainValuesButNeverSecrets`
  - `GetProcess_FulfilledOneTime_ShowsFulfilledAt`
  - `ListProcesses_IncludesActiveRunAndLastOutcome`
  - `ListRuns_ViewerCanList`
  - `GetRun_IncludesEntriesInOrderAndOutputTail`
  - `GetDashboard_UpcomingOrderedAndLimitedToTen`
  - `GetDashboard_RecentFailuresExcludeCancelledAndOlderThanADay`
  - `GetSettings_NeverReturnsPassword`
  - `PreviewSchedule_OldAnchor_StartsFromNow`
  - `PreviewSchedule_CountOutOfRange_IsRejected`
  - `PreviewSchedule_ExhaustedRule_ReturnsFewer`
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(application): add process, run, dashboard and settings queries`

### Task 12: Inventory coverage and final verification

**Files:**
- Modify: `docs/behavior-inventory.md` (the *Covered by* column of every row whose *Plan* is 2, plus the "(… : Plan 2)" remainders in BI-001, BI-027, BI-066, BI-076 and Q-1/Q-3/Q-8, replaced with the test names from Tasks 3–11)
- Modify: `C:\Users\Gustavo\.claude\projects\…\memory\gapactivator-plan1-carryover.md` (outside the repo, by the controller): drop the items this plan closed; add the notes for Plan 3.

**Notes for Plan 3:**
- `TryAttach` must keep a process handle so that the exit code is readable.
- `IUnitOfWork` must persist history entries in the same transaction.
- Time limits no longer need an upper bound, because the delays are chunked.

- [ ] **Step 1: Fill the *Covered by* cells.** Every Plan-2 row must cite at least one existing test (`ClassName.MethodName`).

- [ ] **Step 2: Verify the citations**

Run: `dotnet test -c Release -- --list-tests > TestResults/tests.txt`. Every name cited in the inventory must appear in that list. Check this with a script in the job's temp folder, not in the repository.
Expected: zero missing names.

- [ ] **Step 3: Full verification**

Run: `dotnet format --verify-no-changes; dotnet build -c Release; dotnet test -c Release`
Expected: all green.

- [ ] **Step 4: Sanitization check**

Run the sanitization pattern the controller gives at execution time with `git grep -n -i -E "<pattern>"` and `git log -p main.. | grep -i -E "<pattern>"`.
Expected: no matches.

- [ ] **Step 5: Commit** `docs: cite application tests in the behavior inventory`
