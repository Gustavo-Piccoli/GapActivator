# Plan 1 — Foundation and Domain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the GapActivator repository foundation and a fully tested, framework-free domain (processes, schedules, runs, settings, domain events, repository interfaces), plus the RRULE occurrence calculator adapter that the scheduling rules depend on.

**Architecture:** Clean Architecture. `GapActivator.Domain` has no project or package references. Scheduling depends on an `IOccurrenceCalculator` port declared in the domain and implemented with Ical.Net in `GapActivator.Infrastructure`. Aggregates (`Process`, `Run`, `InstallationSettings`) protect their invariants and raise domain events. Architecture tests enforce the Dependency Rule.

**Tech Stack:** .NET 10 / C# 14, xUnit v3 on Microsoft.Testing.Platform, Shouldly, NetArchTest.Rules, Ical.Net 5.2.2.

**Spec:** `docs/superpowers/specs/2026-11-03-gapactivator-design.md`

## Global Constraints

- English everywhere: code, identifiers, comments, docs, commit messages.
- Conventional Commits; **no AI attribution trailers** (no `Co-Authored-By`).
- No company names, system acronyms, internal URLs, e-mails, connection strings, or realistic personal data anywhere. Test data is synthetic and obviously fake (`C:\Tools\backup.exe`, `ops@example.com`).
- The legacy repository is read-only reference. Never copy code, names, comments, or test data from it, and never write its name or path into this repository. Its location is given by the owner at execution time.
- SDK `10.0.300`, `rollForward: latestFeature`, test runner `Microsoft.Testing.Platform` (`global.json`).
- `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`, `Nullable=enable`, `ImplicitUsings=enable`, `Deterministic=true`.
- Central package versions in `Directory.Packages.props`.
- `dotnet format --verify-no-changes` must pass.
- `GapActivator.Domain` targets `net10.0`, `IsAotCompatible=true`, and references no packages or projects.
- All times in the domain are `DateTimeOffset`; the domain never reads a clock (callers pass `now`). The machine time zone is handled only inside the calculator adapter.
- New libraries in this plan: Ical.Net (spec §3), NetArchTest.Rules and the GapMouse test stack (spec §3, "same as GapMouse"). Nothing else.

## Review Focus

1. **DST transitions:** a daily 02:30 rule on a spring-forward day and on a fall-back day must yield exactly one occurrence per day, never zero and never two → test in Task 9.
2. **Exhausted recurrence (`COUNT`/`UNTIL`):** when no future occurrence exists, the schedule becomes exhausted and the process is simply not due; no exception (the legacy activator threw) → tests in Tasks 4 and 9.
3. **Clock moved backwards:** evaluating due with a `now` earlier than the last evaluation must not run anything twice or throw → test in Task 6.
4. **Failure limit lowered below the current counter:** the process is blocked on the next enforcement, not left running forever → test in Task 6.
5. **Environment variables that differ only by case** (Windows treats them as the same) and names using the reserved `GAPACTIVATOR_` prefix must be rejected → test in Task 3.

## Execution notes

- Repository: the GapActivator working copy (already contains the spec commit on `main`). Confirm the branch with the owner before the first commit.
- Every task ends with `dotnet build -c Release` and `dotnet test -c Release` green, plus `dotnet format --verify-no-changes`.
- Test naming: `Method_Condition_ExpectedResult`.

## File Structure

```
global.json  Directory.Build.props  Directory.Packages.props  .editorconfig
.gitattributes  .gitignore  GapActivator.slnx  .config/dotnet-tools.json
.github/workflows/ci.yml  .github/dependabot.yml
docs/behavior-inventory.md
src/GapActivator.Domain/
  DomainAssembly.cs
  Common/         Entity.cs  AggregateRoot.cs  IDomainEvent.cs  DomainException.cs
  Processes/      Process.cs  ProcessId.cs  ProcessKind.cs  ProcessLifecycle.cs
                  ProcessName.cs  ExecutablePath.cs  LaunchOptions.cs
                  EnvironmentVariable.cs  EnvironmentVariables.cs  ExecutionLimits.cs
                  DueDecision.cs  ProcessEvents.cs  IProcessRepository.cs
  Scheduling/     Schedule.cs  OneTimeSchedule.cs  RecurringSchedule.cs
                  RecurrenceRule.cs  IOccurrenceCalculator.cs
  Runs/           Run.cs  RunId.cs  RunState.cs  RunOutcome.cs  RunOutcomeRules.cs
                  RunTrigger.cs  StopReason.cs  RunEvents.cs  IRunRepository.cs
  Settings/       InstallationSettings.cs  EmailAlertSettings.cs  TlsMode.cs
                  AlertKind.cs  LogLevel.cs  ProtectedSecret.cs  ISettingsRepository.cs
src/GapActivator.Infrastructure/
  InfrastructureAssembly.cs
  Scheduling/IcalOccurrenceCalculator.cs
tests/GapActivator.Testing/          FakeOccurrenceCalculator.cs  ProcessBuilder.cs  RunBuilder.cs
tests/GapActivator.Domain.Tests/     one test file per domain type
tests/GapActivator.Infrastructure.Tests/Scheduling/IcalOccurrenceCalculatorTests.cs
tests/GapActivator.Architecture.Tests/LayeringTests.cs
```

---

### Task 1: Behavior inventory

**Files:**
- Create: `docs/behavior-inventory.md`

**Interfaces:**
- Consumes: the legacy repository (read-only; source and unit tests), spec §6–§8 and §13.
- Produces: rule IDs `BI-nnn` referenced by later tasks' test names in comments (`// BI-012`).

- [ ] **Step 1: Read the legacy code and its unit tests** and list every observable rule (scheduling, dispatch, retries, failure counting, blocking, cancellation, service processes, startup reconciliation, log retention, alerts, restart/pause of the activator).

- [ ] **Step 2: Write `docs/behavior-inventory.md`** with one table per bounded context (spec §6.1). Columns: `ID`, `Rule (neutral English)`, `Verdict` (`keep` / `redesign` / `drop`), `New rule` (for redesign), `Spec §`, `Plan` (1–5). Rules already decided in the spec must agree with it (for example: cancellation no longer resets the failure counter; per-process time limit is applied; equality-instant bug fixed; time zone is the machine's). Add a final section "Rules not covered by the spec" listing anything found that the spec does not decide; **stop and ask the owner** about each before Task 6 if any exist.

- [ ] **Step 3: Sanitization check**

Run the sanitization pattern the controller gives at execution time (legacy identifiers, table prefixes, and non-English domain terms; it is deliberately not written in this repository) with `git grep -n -i -E "<pattern>"`.
Expected: no matches.

- [ ] **Step 4: Commit**

```bash
git add docs/behavior-inventory.md
git commit -m "docs: add behavior inventory of the predecessor activator"
```

### Task 2: Repository foundation, CI, and architecture tests

**Files:**
- Create: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitattributes`, `.gitignore`, `GapActivator.slnx`, `.config/dotnet-tools.json`, `.github/workflows/ci.yml`, `.github/dependabot.yml`
- Create: `src/GapActivator.Domain/GapActivator.Domain.csproj`, `src/GapActivator.Domain/DomainAssembly.cs`
- Create: `src/GapActivator.Infrastructure/GapActivator.Infrastructure.csproj` (references Domain and `Ical.Net`), `src/GapActivator.Infrastructure/InfrastructureAssembly.cs`
- Create: test projects `tests/GapActivator.Testing`, `tests/GapActivator.Domain.Tests`, `tests/GapActivator.Infrastructure.Tests`, `tests/GapActivator.Architecture.Tests`
- Test: `tests/GapActivator.Architecture.Tests/LayeringTests.cs`

**Interfaces:**
- Produces: `GapActivator.Domain.DomainAssembly.Reference` and `GapActivator.Infrastructure.InfrastructureAssembly.Reference` (`System.Reflection.Assembly`), used by architecture tests.

Copy the shape of these files from GapMouse (https://github.com/Gustavo-Piccoli/GapMouse): `global.json`, `Directory.Build.props` (without `<Version>`; start at `0.1.0`), `Directory.Packages.props` (keep only `xunit.v3` 4.0.2, `xunit.runner.visualstudio` 4.0.1, `Microsoft.NET.Test.Sdk` 18.11.0, `Shouldly` 4.3.0, `NetArchTest.Rules` 1.3.2, `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0; add `Ical.Net` 5.2.2), `.editorconfig`, `.gitattributes`, `.gitignore`, `.config/dotnet-tools.json` (reportgenerator 5.5.12), `dependabot.yml`, and the `build-test` job of `ci.yml` (no AOT job yet; no `Category=Win32` filter needed). Test projects target `net10.0`, are `Exe` + `IsTestProject`, with global usings `Xunit` and `Shouldly`, exactly like GapMouse.

- [ ] **Step 1: Write the failing architecture tests** in `LayeringTests.cs`:

```csharp
[Fact] public void Domain_DependsOnNoOtherLayer() =>
    Types.InAssembly(DomainAssembly.Reference).ShouldNot()
        .HaveDependencyOnAny("GapActivator.Application", "GapActivator.Infrastructure",
                             "GapActivator.Service", "GapActivator.App")
        .GetResult().IsSuccessful.ShouldBeTrue();

[Fact] public void Domain_DependsOnNoFrameworkOrIo() =>
    Types.InAssembly(DomainAssembly.Reference).ShouldNot()
        .HaveDependencyOnAny("Ical.Net", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions",
                             "System.IO.File", "System.IO.Directory", "System.IO.FileStream",
                             "System.Net.Http", "System.Text.Json", "Microsoft.Win32",
                             "System.Diagnostics.Process", "System.Runtime.InteropServices.Marshal")
        .GetResult().IsSuccessful.ShouldBeTrue();

[Fact] public void Infrastructure_DoesNotDependOnOuterLayers() =>
    Types.InAssembly(InfrastructureAssembly.Reference).ShouldNot()
        .HaveDependencyOnAny("GapActivator.Service", "GapActivator.App")
        .GetResult().IsSuccessful.ShouldBeTrue();
```

Also add `Domain_HasNoPackageOrProjectReferences`: load `src/GapActivator.Domain/GapActivator.Domain.csproj` as XML (path found by walking up from `AppContext.BaseDirectory` to the folder containing `GapActivator.slnx`) and assert it has zero `PackageReference` and `ProjectReference` elements.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test -c Release`
Expected: build FAILS (projects and marker classes do not exist yet).

- [ ] **Step 3: Create the solution, props, projects, marker classes, CI, and dependabot** as described above.

- [ ] **Step 4: Verify**

Run: `dotnet restore; dotnet format --verify-no-changes --no-restore; dotnet build -c Release --no-restore; dotnet test -c Release --no-build`
Expected: format clean, build with 0 warnings, 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "build: scaffold solution, CI and architecture tests"
```

### Task 3: Domain building blocks and process value objects

**Files:**
- Create: `src/GapActivator.Domain/Common/*.cs`, `Processes/ProcessId.cs`, `ProcessName.cs`, `ExecutablePath.cs`, `EnvironmentVariable.cs`, `EnvironmentVariables.cs`, `LaunchOptions.cs`, `ExecutionLimits.cs`, `Settings/ProtectedSecret.cs`
- Test: `tests/GapActivator.Domain.Tests/Processes/ValueObjectTests.cs`, `Common/AggregateRootTests.cs`

**Interfaces:**
- Produces:
  - `public interface IDomainEvent { DateTimeOffset OccurredAt { get; } }`
  - `public abstract class Entity<TId> where TId : notnull { public TId Id { get; } }` with equality by `Id`.
  - `public abstract class AggregateRoot<TId> : Entity<TId>` with `IReadOnlyList<IDomainEvent> DomainEvents`, `protected void Raise(IDomainEvent e)`, `public void ClearDomainEvents()`.
  - `public sealed class DomainException(string message) : Exception(message)` — the only exception type the domain throws for rule violations.
  - `readonly record struct ProcessId(Guid Value)` with `static ProcessId New()` using `Guid.CreateVersion7()`.
  - `sealed record ProcessName` — `static ProcessName Create(string value)`: trimmed, 1–100 chars, no control characters. Comparison is case-insensitive (`Equals` and `GetHashCode` use `StringComparer.OrdinalIgnoreCase`).
  - `sealed record ExecutablePath` — `Create(string value)`: trimmed, `Path.IsPathFullyQualified`, no `"` characters, no invalid path chars, ≤ 32,767 chars. Equality case-insensitive.
  - `sealed record ProtectedSecret(string Ciphertext)` — opaque; non-empty; `ToString()` returns `"***"`.
  - `sealed record EnvironmentVariable(string Name, string? PlainValue, ProtectedSecret? SecretValue)` — `static Plain(string name, string value)`, `static Secret(string name, ProtectedSecret value)`; `bool IsSecret`. Name: non-empty, no `=`, no `\0`, must not start with `GAPACTIVATOR_` (case-insensitive).
  - `sealed class EnvironmentVariables` — `static EnvironmentVariables Create(IEnumerable<EnvironmentVariable>)`, `static Empty`, `IReadOnlyList<EnvironmentVariable> Items`; duplicate names (case-insensitive) rejected.
  - `sealed record LaunchOptions(ExecutablePath Executable, string Arguments, string? WorkingDirectory, EnvironmentVariables Environment)` — `Arguments` ≤ 8,191 chars (empty allowed); `WorkingDirectory`, when present, must be fully qualified.
  - `sealed record ExecutionLimits(TimeSpan? TimeLimit, TimeSpan? GracePeriod)` — each, when present, must be > 0; `GracePeriod` ≤ 10 minutes. Methods: `TimeSpan? EffectiveTimeLimit(TimeSpan? installationDefault)` and `TimeSpan EffectiveGracePeriod(TimeSpan installationDefault)` (per-process value wins, else the default).

- [ ] **Step 1: Write the failing tests.** One `[Theory]` per value object for valid and invalid inputs, asserting `Should.Throw<DomainException>` for the invalid ones. Required cases:
  - `ProcessName.Create("  Nightly backup  ").Value == "Nightly backup"`; `""`, 101 chars, and `"a\tb"` throw; `Create("Backup") == Create("BACKUP")`.
  - `ExecutablePath.Create(@"C:\Tools\backup.exe")` succeeds; `"backup.exe"`, `@"""C:\Tools\a.exe"""`, and `""` throw.
  - `EnvironmentVariables.Create([Plain("Path_Extra","1"), Plain("PATH_EXTRA","2")])` throws (Review Focus 5); `Plain("GAPACTIVATOR_PARENT_PID","1")` throws; `Plain("A=B","1")` throws.
  - `new ExecutionLimits(TimeSpan.FromHours(2), null).EffectiveTimeLimit(TimeSpan.FromHours(8)) == TimeSpan.FromHours(2)` (fixes the legacy "per-process limit ignored" defect); `new ExecutionLimits(null, null).EffectiveTimeLimit(null) == null`; `EffectiveGracePeriod(TimeSpan.FromSeconds(30)) == 30 s` when unset; `TimeSpan.Zero` throws.
  - `new ProtectedSecret("abc").ToString() == "***"`.
  - `AggregateRootTests`: raising two events keeps their order; `ClearDomainEvents()` empties the list.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test -c Release --project tests/GapActivator.Domain.Tests`
Expected: build FAILS (types missing).

- [ ] **Step 3: Implement the types** with the signatures above.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test -c Release`
Expected: all PASS, including architecture tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(domain): add building blocks and process value objects"
```

### Task 4: Schedules and the occurrence calculator port

**Files:**
- Create: `src/GapActivator.Domain/Scheduling/RecurrenceRule.cs`, `IOccurrenceCalculator.cs`, `Schedule.cs`, `OneTimeSchedule.cs`, `RecurringSchedule.cs`
- Create: `tests/GapActivator.Testing/FakeOccurrenceCalculator.cs`
- Test: `tests/GapActivator.Domain.Tests/Scheduling/RecurrenceRuleTests.cs`, `ScheduleTests.cs`

**Interfaces:**
- Consumes: `Entity<TId>`, `DomainException` (Task 3).
- Produces:
  - `sealed record RecurrenceRule` — `static RecurrenceRule Parse(string value)`, `string Value` (uppercased, trimmed, no trailing `;`). Valid parts: `FREQ` (required; one of `SECONDLY, MINUTELY, HOURLY, DAILY, WEEKLY, MONTHLY, YEARLY`), `UNTIL, COUNT, INTERVAL, BYSECOND, BYMINUTE, BYHOUR, BYDAY, BYMONTHDAY, BYYEARDAY, BYWEEKNO, BYMONTH, BYSETPOS, WKST`. Each part is `KEY=VALUE`, no duplicate keys, `COUNT` and `UNTIL` mutually exclusive, `INTERVAL`/`COUNT` positive integers. Weekday tokens in `BYDAY`/`WKST`: `MO TU WE TH FR SA SU` with an optional signed ordinal prefix in `BYDAY` (`-1FR`, `2MO`).
  - `public interface IOccurrenceCalculator { DateTimeOffset? FirstOccurrence(RecurrenceRule rule, DateTimeOffset anchor); DateTimeOffset? NextOccurrence(RecurrenceRule rule, DateTimeOffset anchor, DateTimeOffset after); }` — `FirstOccurrence` returns the first instance at or after `anchor`; `NextOccurrence` returns the first instance strictly after `after`; `null` = exhausted.
  - `readonly record struct ScheduleId(Guid Value)` with `New()`.
  - `abstract class Schedule : Entity<ScheduleId>` — `DateTimeOffset? NextOccurrenceAt`; `bool IsExhausted => NextOccurrenceAt is null`; `internal abstract void Advance(DateTimeOffset now, IOccurrenceCalculator calculator)`.
  - `sealed class OneTimeSchedule : Schedule` — `static Create(DateTimeOffset at)`; `DateTimeOffset At`; `DateTimeOffset? FulfilledAt`; `Advance` sets `FulfilledAt = now` and `NextOccurrenceAt = null`.
  - `sealed class RecurringSchedule : Schedule` — `static Create(RecurrenceRule rule, DateTimeOffset anchor, IOccurrenceCalculator calculator)` sets `NextOccurrenceAt = calculator.FirstOccurrence(rule, anchor)`; `Advance` sets `NextOccurrenceAt = calculator.NextOccurrence(Rule, Anchor, now)`.
  - `FakeOccurrenceCalculator` (Testing): constructed with a fixed step `TimeSpan`; returns `anchor + k*step` and supports an optional `count` to simulate exhaustion.

- [ ] **Step 1: Write the failing tests.**
  - `RecurrenceRule.Parse("freq=weekly;interval=2;byday=mo,we,fr").Value == "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR"`.
  - These throw `DomainException`: `""`, `"INTERVAL=2"`, `"FREQ=HOURLYY"`, `"FREQ=DAILY;FREQ=DAILY"`, `"FREQ=DAILY;COUNT=2;UNTIL=20270101T000000Z"`, `"FREQ=DAILY;INTERVAL=0"`, `"FREQ=DAILY;BYDAY=XX"`, `"FREQ=DAILY;FOO=1"`.
  - `OneTimeSchedule.Create(t)`: `NextOccurrenceAt == t`; after `Advance(now)`: `FulfilledAt == now`, `IsExhausted`.
  - `RecurringSchedule` with a fake step of 1 h and anchor `2027-01-01T08:00+00:00`: `NextOccurrenceAt == anchor`; `Advance(now: 2027-01-01T10:30)` → `11:00` (missed occurrences collapse into one).
  - **Equality instant (legacy defect):** `Advance(now: exactly 09:00)` → `10:00`, never `09:00`.
  - **Exhaustion (Review Focus 2):** fake with `count: 1` → after one `Advance`, `IsExhausted` is true and no exception.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test -c Release --project tests/GapActivator.Domain.Tests`
Expected: build FAILS.

- [ ] **Step 3: Implement the types** with the signatures above.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test -c Release`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(domain): add schedules and recurrence rules"
```

### Task 5: Run aggregate and its state machine

**Files:**
- Create: `src/GapActivator.Domain/Runs/*.cs`, `src/GapActivator.Domain/Processes/ProcessKind.cs`
- Create: `tests/GapActivator.Testing/RunBuilder.cs`
- Test: `tests/GapActivator.Domain.Tests/Runs/RunStateMachineTests.cs`, `RunOutcomeRulesTests.cs`

**Interfaces:**
- Consumes: `AggregateRoot<TId>`, `ProcessId`, `ProcessName`, `DomainException` (Task 3).
- Produces:
  - `enum ProcessKind { RunToCompletion, Service }`
  - `readonly record struct RunId(Guid Value)` with `New()`.
  - `enum RunState { Launching, Running, Stopping, Completed }`
  - `enum RunOutcome { Succeeded, Failed, FailedToStart, TimedOut, Cancelled, Interrupted, Abandoned, Skipped, Crashed, Stopped }`
  - `enum RunTrigger { Schedule, Retry, Manual, Restart, ServiceKeepAlive }`
  - `enum StopReason { UserCancel, TimeLimit, Shutdown }`
  - `enum FailureEffect { Increment, Reset, Unchanged }` and `static class RunOutcomeRules { public static FailureEffect EffectOf(RunOutcome o); }` — Succeeded → Reset; Failed, FailedToStart, TimedOut, Abandoned, Crashed → Increment; Cancelled, Interrupted, Skipped, Stopped → Unchanged (spec §6.3, owner decision).
  - `sealed class Run : AggregateRoot<RunId>` with read-only properties `ProcessId`, `ProcessName ProcessNameSnapshot`, `ProcessKind Kind`, `RunTrigger Trigger`, `DateTimeOffset? ScheduledFor`, `bool IsLate`, `RunState State`, `StopReason? StopReason`, `int? Pid`, `DateTimeOffset? ProcessStartTime`, `DateTimeOffset CreatedAt`, `DateTimeOffset? StartedAt`, `DateTimeOffset? EndedAt`, `RunOutcome? Outcome`, `int? ExitCode`, `bool Killed`, `string? Error`.
  - Factory and transitions (any transition not listed throws `DomainException`):
    - `static Run Begin(ProcessId, ProcessName, ProcessKind, RunTrigger, DateTimeOffset? scheduledFor, bool isLate, DateTimeOffset now)` → `Launching`; raises `RunLate` when `isLate`.
    - `static Run Skip(ProcessId, ProcessName, ProcessKind, DateTimeOffset scheduledFor, DateTimeOffset now)` → `Completed`/`Skipped`; raises `RunSkipped`.
    - `void MarkRunning(int pid, DateTimeOffset processStartTime, DateTimeOffset now)`: `Launching` → `Running`, or → `Stopping` if a stop was already requested (the caller must then signal immediately); raises `RunStarted`.
    - `void RequestStop(StopReason reason, DateTimeOffset now)`: in `Launching` records the reason and stays `Launching`; `Running` → `Stopping`; in `Stopping` keeps the first reason (idempotent); in `Completed` throws.
    - `void AbortLaunch(DateTimeOffset now)`: only in `Launching` with a stop requested → `Completed` with the outcome mapped from the reason.
    - `void FailToStart(string error, DateTimeOffset now)`: `Launching` → `Completed`/`FailedToStart`.
    - `void Exit(int exitCode, bool killed, DateTimeOffset now)`: `Running`/`Stopping` → `Completed`.
    - `void Abandon(DateTimeOffset now)`: any non-completed state → `Completed`; outcome `Abandoned` for RunToCompletion, `Interrupted` for Service (spec §6.3).
  - Outcome mapping when a stop reason is set (used by `Exit` and `AbortLaunch`): `UserCancel` → `Cancelled` (RunToCompletion) / `Stopped` (Service); `TimeLimit` → `TimedOut`; `Shutdown` → `Interrupted`. With no stop reason: RunToCompletion → exit 0 `Succeeded`, else `Failed`; Service → `Crashed` for any exit code.
  - Events (records implementing `IDomainEvent`, in `RunEvents.cs`): `RunLate(RunId, ProcessId, DateTimeOffset ScheduledFor, DateTimeOffset OccurredAt)`, `RunSkipped(RunId, ProcessId, DateTimeOffset ScheduledFor, DateTimeOffset OccurredAt)`, `RunStarted(RunId, ProcessId, int Pid, DateTimeOffset OccurredAt)`, `RunCompleted(RunId, ProcessId, RunOutcome Outcome, int? ExitCode, bool Killed, DateTimeOffset OccurredAt)` (raised by every transition into `Completed`, including `Skip`), `ServiceProcessCrashed(RunId, ProcessId, int? ExitCode, DateTimeOffset OccurredAt)` (raised in addition to `RunCompleted` for `Crashed`).
  - `public interface IRunRepository { Task<Run?> GetByIdAsync(RunId id, CancellationToken ct); Task<Run?> GetActiveForProcessAsync(ProcessId processId, CancellationToken ct); Task<IReadOnlyList<Run>> ListActiveAsync(CancellationToken ct); void Add(Run run); }`

- [ ] **Step 1: Write the failing tests.** One `[Fact]` per transition and per outcome-mapping row above, plus:
  - `RunOutcomeRulesTests`: a `[Theory]` over all 10 outcomes asserting the exact `FailureEffect`.
  - `Exit_ServiceKindExitCodeZero_IsCrashed` and raises both `RunCompleted` and `ServiceProcessCrashed`.
  - `RequestStop_DuringLaunching_ThenMarkRunning_GoesToStopping` (spec §8, cancel during Launching).
  - `RequestStop_Twice_KeepsFirstReason` (TimeLimit then UserCancel → outcome `TimedOut`).
  - `RequestStop_WhenCompleted_Throws`; `Exit_WhenLaunching_Throws`; `MarkRunning_Twice_Throws`.
  - `Abandon_ServiceKind_IsInterrupted`; `Abandon_RunToCompletion_IsAbandoned`.
  - `Skip_IsCompletedImmediately_WithSkippedOutcome`.
  - `EndedAt` and `StartedAt` equal the `now` passed in.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test -c Release --project tests/GapActivator.Domain.Tests`
Expected: build FAILS.

- [ ] **Step 3: Implement the types.** Keep the transition table in one private method so `Exit` and `AbortLaunch` share the outcome mapping.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test -c Release`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(domain): add run aggregate and state machine"
```

### Task 6: Process aggregate

**Files:**
- Create: `src/GapActivator.Domain/Processes/Process.cs`, `ProcessLifecycle.cs`, `DueDecision.cs`, `ProcessEvents.cs`, `IProcessRepository.cs`
- Create: `tests/GapActivator.Testing/ProcessBuilder.cs`
- Test: `tests/GapActivator.Domain.Tests/Processes/ProcessLifecycleTests.cs`, `ProcessFailureTests.cs`, `ProcessDueTests.cs`

**Interfaces:**
- Consumes: Task 3 value objects; `Schedule`, `OneTimeSchedule`, `RecurringSchedule`, `IOccurrenceCalculator` (Task 4); `ProcessKind`, `RunOutcome`, `RunOutcomeRules`, `FailureEffect` (Task 5).
- Produces:
  - `enum ProcessLifecycle { Enabled, Disabled, Blocked }`
  - `sealed class Process : AggregateRoot<ProcessId>` with properties `ProcessName Name`, `string Description` (≤ 1,000 chars), `ProcessKind Kind`, `LaunchOptions Launch`, `ExecutionLimits Limits`, `IReadOnlyList<Schedule> Schedules`, `ProcessLifecycle Lifecycle`, `int ConsecutiveFailures`, `bool RetryPending`, `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`.
  - `static Process Register(ProcessName name, string description, ProcessKind kind, LaunchOptions launch, ExecutionLimits limits, IEnumerable<Schedule> schedules, DateTimeOffset now)` → `Enabled`; a Service-kind process with any schedule throws.
  - `void Update(ProcessName name, string description, ProcessKind kind, LaunchOptions launch, ExecutionLimits limits, IEnumerable<Schedule> schedules, DateTimeOffset now)` — same validation.
  - `void Enable(DateTimeOffset now)` (Disabled → Enabled), `void Disable(DateTimeOffset now)` (Enabled → Disabled), `void Unblock(DateTimeOffset now)` (Blocked → Enabled, counter 0, raises `ProcessUnblocked`); other source states throw.
  - `void ApplyRunOutcome(RunOutcome outcome, int failureLimit, DateTimeOffset now)` — uses `RunOutcomeRules.EffectOf`: `Reset` → counter 0, `RetryPending = false`; `Increment` → counter + 1, then if counter ≥ `failureLimit` block (counter reset to 0, `RetryPending = false`, `Lifecycle = Blocked`, raise `ProcessBlocked(ProcessId, int FailuresReached, DateTimeOffset OccurredAt)`), else for RunToCompletion set `RetryPending = true`; `Unchanged` → no change.
  - `void MarkServiceFoundRunning(DateTimeOffset now)` — Service kind only; counter 0.
  - `void EnforceFailureLimit(int failureLimit, DateTimeOffset now)` — if `Enabled` and counter ≥ limit, block as above (legacy pre-dispatch check).
  - `DueDecision EvaluateDue(DateTimeOffset now, TimeSpan lateThreshold, IOccurrenceCalculator calculator)` — RunToCompletion and `Enabled` only, otherwise `DueDecision.NotDue`. Advances every schedule whose `NextOccurrenceAt <= now` (inclusive: the equality instant is due). `ScheduledFor` = the earliest consumed occurrence. `IsLate = now - ScheduledFor > lateThreshold`. If no schedule is due but `RetryPending`, returns due with `Trigger = Retry` and no `ScheduledFor`; `RetryPending` is cleared by `ApplyRunOutcome`, not here.
  - `DateTimeOffset? NextScheduledAt` — the minimum `NextOccurrenceAt` over non-exhausted schedules.
  - `sealed record DueDecision(bool IsDue, RunTrigger? Trigger, DateTimeOffset? ScheduledFor, bool IsLate)` with `static DueDecision NotDue`.
  - `ProcessEvents.cs`: `ProcessBlocked`, `ProcessUnblocked(ProcessId, DateTimeOffset OccurredAt)`.
  - `public interface IProcessRepository { Task<Process?> GetByIdAsync(ProcessId id, CancellationToken ct); Task<IReadOnlyList<Process>> ListAsync(CancellationToken ct); Task<bool> NameExistsAsync(ProcessName name, ProcessId? excluding, CancellationToken ct); Task<IReadOnlyList<Process>> ListByExecutableAsync(ExecutablePath executable, ProcessId? excluding, CancellationToken ct); void Add(Process process); void Remove(Process process); }`
  - `ProcessBuilder` (Testing): a fluent builder with sensible defaults (`Name "Sample job"`, `C:\Tools\sample.exe`, RunToCompletion, no schedules).

- [ ] **Step 1: Write the failing tests.**
  - Lifecycle: every allowed transition above, plus `Enable_WhenBlocked_Throws`, `Disable_WhenBlocked_Throws`, `Register_ServiceWithSchedule_Throws`.
  - Failures (`failureLimit = 3`): two `Failed` → counter 2, `RetryPending`; third → `Blocked`, counter 0, one `ProcessBlocked` with `FailuresReached == 3`; `Succeeded` after one failure → counter 0, `RetryPending` false; `Cancelled`, `Interrupted`, `Skipped` after two failures → counter still 2 (owner decision); Service-kind `Crashed` ×2 then `MarkServiceFoundRunning` → counter 0.
  - **Limit lowered (Review Focus 4):** counter 4, `EnforceFailureLimit(3)` → `Blocked`.
  - Due (fake calculator, step 1 h, anchor `2027-01-01T08:00Z`, `lateThreshold` 2 min): `now 07:59` not due; `now 08:00` due with `ScheduledFor 08:00`, not late (equality instant); `now 11:30` → due once, `ScheduledFor 08:00`, `IsLate`, next becomes `12:00`; immediately evaluating again at `11:30` → not due.
  - Two schedules (08:00 hourly and a one-time at 08:30), `now 08:45` → due once with `ScheduledFor 08:00`; both advanced; the one-time is fulfilled.
  - **Clock backwards (Review Focus 3):** due at `11:30`, then `EvaluateDue(now: 09:00)` → not due, no exception.
  - `RetryPending` with nothing scheduled → due with `Trigger Retry`; `Disabled` or `Blocked` → not due; Service kind → not due.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test -c Release --project tests/GapActivator.Domain.Tests`
Expected: build FAILS.

- [ ] **Step 3: Implement `Process`** with the signatures above.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test -c Release`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(domain): add process aggregate with failure and due rules"
```

### Task 7: Installation settings aggregate

**Files:**
- Create: `src/GapActivator.Domain/Settings/InstallationSettings.cs`, `EmailAlertSettings.cs`, `TlsMode.cs`, `AlertKind.cs`, `LogLevel.cs`, `ISettingsRepository.cs`
- Test: `tests/GapActivator.Domain.Tests/Settings/InstallationSettingsTests.cs`, `EmailAlertSettingsTests.cs`

**Interfaces:**
- Consumes: `AggregateRoot<TId>`, `ProtectedSecret`, `DomainException` (Task 3).
- Produces:
  - `enum LogLevel { Debug, Information, Warning, Error }`; `enum TlsMode { None, StartTls, Implicit }`; `enum AlertKind { ProcessBlocked, RunFailed, RunTimedOut, ServiceProcessCrashed, CycleFailed, ServiceRestarted }`.
  - `sealed record EmailAlertSettings` — `static EmailAlertSettings Disabled`; `static EmailAlertSettings Create(bool enabled, string host, int port, TlsMode tls, string? username, ProtectedSecret? password, string sender, IEnumerable<string> recipients, IEnumerable<AlertKind> alerts)`. When `enabled`: host non-empty, port 1–65535, sender and every recipient valid per `System.Net.Mail.MailAddress.TryCreate`, at least one recipient, at least one alert kind; a password requires a username. When disabled, the values are kept but not validated beyond types (so the user can switch alerts off without losing their configuration).
  - `sealed class InstallationSettings : AggregateRoot<int>` (single row, `Id = 1`) with `TimeSpan CycleInterval`, `int FailureLimit`, `TimeSpan? DefaultTimeLimit`, `TimeSpan DefaultGracePeriod`, `int LogRetentionDays`, `int HistoryRetentionDays`, `LogLevel LogLevel`, `EmailAlertSettings Email`, `bool CheckForUpdates`.
  - `static InstallationSettings Default()` — this plan's defaults: `CycleInterval` 60 s, `FailureLimit` 3, `DefaultTimeLimit` null (unlimited), `DefaultGracePeriod` 30 s (spec), `LogRetentionDays` 30, `HistoryRetentionDays` 90 (spec), `LogLevel.Information`, `Email = Disabled`, `CheckForUpdates` true.
  - `void Change(TimeSpan cycleInterval, int failureLimit, TimeSpan? defaultTimeLimit, TimeSpan defaultGracePeriod, int logRetentionDays, int historyRetentionDays, LogLevel logLevel, bool checkForUpdates)` — ranges: cycle 5 s–1 h; failure limit 1–100; default time limit > 0 when set; grace 1 s–10 min; both retentions 1–3,650 days.
  - `void ChangeEmail(EmailAlertSettings email)`.
  - `public interface ISettingsRepository { Task<InstallationSettings> GetAsync(CancellationToken ct); }` (Plan 3 seeds the default row).

- [ ] **Step 1: Write the failing tests:** `Default()` returns exactly the values above; each range boundary (min, max, min − 1, max + 1) for `Change`; `Create(enabled: true, …)` with zero recipients, an invalid sender (`"not-an-email"`), port 0, and a password without a username each throw; `Create(enabled: false, host: "", …)` succeeds.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test -c Release --project tests/GapActivator.Domain.Tests`
Expected: build FAILS.

- [ ] **Step 3: Implement the types.**

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test -c Release`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(domain): add installation settings"
```

### Task 8: Inventory coverage check

**Files:**
- Modify: `docs/behavior-inventory.md` (add a `Covered by` column)

**Interfaces:**
- Consumes: Task 1 inventory; tests from Tasks 3–7.

- [ ] **Step 1: Fill `Covered by`** for every `keep`/`redesign` rule assigned to Plan 1 with the test name that pins it. Any Plan 1 rule without a test gets a test added to the owning test file now, in that file's style.

- [ ] **Step 2: Verify**

Run: `dotnet test -c Release`
Expected: all PASS; no Plan 1 row in the inventory has an empty `Covered by`.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "test(domain): cover remaining inventory rules"
```

### Task 9: Ical.Net occurrence calculator

**Files:**
- Create: `src/GapActivator.Infrastructure/Scheduling/IcalOccurrenceCalculator.cs`
- Test: `tests/GapActivator.Infrastructure.Tests/Scheduling/IcalOccurrenceCalculatorTests.cs`

**Interfaces:**
- Consumes: `IOccurrenceCalculator`, `RecurrenceRule` (Task 4).
- Produces: `public sealed class IcalOccurrenceCalculator(TimeZoneInfo timeZone) : IOccurrenceCalculator` — Plan 3 registers it with `TimeZoneInfo.Local`.

Approach: convert `anchor` to wall-clock time in `timeZone`, build an Ical.Net `CalendarEvent` with `Start = new CalDateTime(wallClock, timeZone.Id)` and `RecurrenceRule = new RecurrencePattern(rule.Value)`, enumerate occurrences from `after` (or `anchor` for `FirstOccurrence`) and return the first that satisfies the at-or-after / strictly-after contract, converted back to `DateTimeOffset` with the zone's offset at that instant. Return `null` when enumeration ends. Never throw for exhaustion.

- [ ] **Step 1: Write the failing tests** with the zone `TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time")` (deterministic DST dates, unrelated to any real deployment):
  - `FREQ=DAILY;BYHOUR=8;BYMINUTE=0;BYSECOND=0`, anchor `2027-03-01T08:00+01:00`: `FirstOccurrence == anchor`; `NextOccurrence(after: anchor) == 2027-03-02T08:00+01:00`.
  - Equality: `NextOccurrence(after: 2027-03-02T08:00+01:00)` returns `2027-03-03T08:00+01:00`.
  - **DST (Review Focus 1):** `FREQ=DAILY;BYHOUR=2;BYMINUTE=30;BYSECOND=0`. Exactly one occurrence on the spring-forward day `2027-03-28` and exactly one on the fall-back day `2027-10-31` (enumerate `NextOccurrence` across each day and count).
  - Offset follows the zone: a daily 08:00 rule returns `+01:00` in March and `+02:00` in April.
  - `FREQ=WEEKLY;BYDAY=MO,WE,FR;BYHOUR=9;BYMINUTE=0;BYSECOND=0` from a Tuesday → Wednesday 09:00.
  - **Exhaustion (Review Focus 2):** `FREQ=DAILY;COUNT=2` → `FirstOccurrence` = anchor, the next = anchor + 1 day, the next after that = `null`.
  - `FREQ=MINUTELY;INTERVAL=5` from `08:02` → `08:05`, given an anchor at `08:00`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test -c Release --project tests/GapActivator.Infrastructure.Tests`
Expected: build FAILS.

- [ ] **Step 3: Implement `IcalOccurrenceCalculator`.**

- [ ] **Step 4: Run all checks**

Run: `dotnet format --verify-no-changes; dotnet build -c Release; dotnet test -c Release`
Expected: format clean, 0 warnings, all PASS (architecture tests confirm Ical.Net stays out of the domain).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(infrastructure): add Ical.Net occurrence calculator"
```

---

## Done when

- `dotnet format --verify-no-changes`, `dotnet build -c Release` (0 warnings), and `dotnet test -c Release` pass locally; the CI workflow file is in place (it runs once the repository has a remote).
- The domain covers spec §6.2–§6.6 and the run/process rules of §8 that are domain rules. Application concerns (startup grace for service processes, the run-now dialog, duplicate-executable warning, dispatch) are Plan 2 and are listed with `Plan 2` in the inventory.
- `docs/behavior-inventory.md` exists, is sanitized, and every Plan 1 rule has a covering test.

## Calls made by this plan (not fixed by the spec)

- IDs are `Guid` v7 generated in the domain.
- Settings defaults: cycle 60 s, failure limit 3, no default time limit, log retention 30 days; ranges as in Task 7.
- Name limits: name 100 chars, description 1,000 chars, arguments 8,191 chars; grace period up to 10 minutes.
- "Late" is decided by a `lateThreshold` passed in by the application (Plan 2 chooses its value).
- The Ical.Net adapter is built in this plan (instead of Plan 3) because the scheduling rules in spec §6.4 cannot be proven without it.
