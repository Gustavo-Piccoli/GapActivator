# Plan 3 — Infrastructure and Service Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement every Plan 2 port against the real machine: SQLite through EF Core with migrations, the Win32 process launcher with cancel event and job-object tree kill, run output files, service logs and retention, DPAPI, MailKit, and a Windows Service host that runs the sample child end to end.

**Architecture:** `GapActivator.Infrastructure` references Application and Domain and holds every OS and storage adapter. Persistence is scoped: one `DbContext` per command or query scope, and singletons never hold one. The launcher, secret protector, retention, e-mail sender, notifier and output reader are singletons, because the singleton `RunSupervisor`, `CycleRunner` and `SchedulerLoop` capture them. The new `GapActivator.Service` executable is the composition root. It runs `DatabaseInitializer`, then `SchedulerLoop.RunAsync`, which reconciles first, inside a `BackgroundService` hosted by `Microsoft.Extensions.Hosting.WindowsServices`. Children are created suspended, so the cancel event and the job object exist before the child runs its first instruction.

**Tech Stack:** .NET 10 / C# 14, `net10.0-windows` for every project that touches Win32. Microsoft.EntityFrameworkCore.Sqlite 10.0.14 (+ .Design 10.0.14, tooling only), Microsoft.Extensions.Hosting.WindowsServices 10.0.14, Microsoft.Extensions.Logging.EventLog 10.0.14, System.Security.Cryptography.ProtectedData 10.0.0, MailKit 4.18.1 (System.Security.Cryptography.Pkcs pinned to 10.0.14 as a transitive security pin). Hand-written `[LibraryImport]` P/Invoke. Tests use xUnit v3 on Microsoft.Testing.Platform and Shouldly.

**Spec:** `docs/superpowers/specs/2026-11-03-gapactivator-design.md` (mainly §3, §6.3, §7, §9, §12, §14 row 3, §16). Also read `docs/behavior-inventory.md`: every row whose *Plan* column is 3 lands here, plus the "(persistence: Plan 3)" remainder in BI-076/Q-1 and the "(launching in that folder: Plan 3)" remainder in Q-5.

## Global Constraints

- Everything from Plans 1 and 2 still holds:
  - English everywhere and CRLF line endings.
  - Conventional Commits, with **no AI attribution trailers** (no `Co-Authored-By` or any other).
  - Synthetic test data only (e-mail addresses under `example.com`, hosts `127.0.0.1` or `smtp.example.com`); no secrets; never write the legacy repository's name or path.
  - Warnings are errors, and `dotnet format --verify-no-changes` passes.
- Dependency rule: Domain ← Application ← Infrastructure ← Service. `GapActivator.Application` gains no package. Only `GapActivator.Service` references `Microsoft.Extensions.Hosting.WindowsServices`.
- New packages are limited to the list below. Each gets its reason as an XML comment next to its `PackageVersion` in `Directory.Packages.props`, and each version is pinned exactly:
  - `Microsoft.EntityFrameworkCore.Sqlite` 10.0.14: embedded relational storage with migrations (§3).
  - `Microsoft.EntityFrameworkCore.Design` 10.0.14: migrations tooling only, referenced with `PrivateAssets="all"`.
  - `Microsoft.Extensions.Hosting.WindowsServices` 10.0.14: the Windows Service host (§3).
  - `Microsoft.Extensions.Logging.EventLog` 10.0.14: Event Log for severe service errors (§9).
  - `System.Security.Cryptography.ProtectedData` 10.0.0: DPAPI on .NET (§3, §9).
  - `MailKit` 4.13.0: SMTP with TLS and authentication (§3).

  The Microsoft.Extensions and EF Core packages match the 10.0.14 already pinned. If one of them is not published at 10.0.14, use the highest published 10.0.x. For ProtectedData and MailKit, the implementer confirms that the version restores with no `NU190x` audit warning, because those warnings are errors. If MailKit 4.13.0 is flagged, use the newest stable 4.x that is at least two weeks old. Every change of version is recorded in the commit message.
- Tool: `dotnet-ef` 10.0.14 is added to `.config/dotnet-tools.json`.
- Target frameworks: `net10.0-windows` for `GapActivator.Infrastructure`, `GapActivator.Service`, `samples/GapActivator.SampleChild`, `tests/GapActivator.TestChild`, `tests/GapActivator.Infrastructure.Tests`, `tests/GapActivator.Service.Tests` and `tests/GapActivator.Architecture.Tests`. Domain, Application and `GapActivator.Testing` stay `net10.0`.
- `GapActivator.Infrastructure` sets `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`, because `[LibraryImport]` generates unsafe code. There is no CsWin32 or other interop package.
- Paths (§9, ruling 9):
  - The data root is `%ProgramData%\GapActivator`. Configuration key `GapActivator:DataRoot` (env var `GapActivator__DataRoot`, command line `--GapActivator:DataRoot=<path>`) overrides it.
  - Database: `{DataRoot}\gapactivator.db`.
  - Service logs: `{DataRoot}\logs\gapactivator-yyyyMMdd.log`, dated by the machine's local date.
  - Run output: `{DataRoot}\logs\runs\{runId}.log`, where `{runId}` is the GUID in `D` format, lower case.
  - The service creates missing folders and never sets ACLs; the installer does that in Plan 5.
- Child contract (§7, ruling 5):
  - `GAPACTIVATOR_PARENT_PID` is the service's PID in decimal.
  - `GAPACTIVATOR_PARENT_START_TIME` is the service's process start time as UTC `DateTime.ToString("O", CultureInfo.InvariantCulture)`, for example `2030-01-01T12:00:00.1234567Z`.
  - Cancel event: `Local\GapActivator.Cancel.{childPid}`. It is a manual-reset event, created while the child is still suspended.
  - Job object: `Local\GapActivator.Job.{childPid}.{startTimeUtcTicks}`, without `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`.
  - Exit code 0 = success; GapActivator decides Cancelled and TimedOut.
- Event Log source and service name: `GapActivator`. Service exit codes: `0` = normal stop, `1` = fatal startup or run failure, `3` = restart requested.
- Shutdown (addition a): `HostOptions.ShutdownTimeout` and the SCM stop wait hint are both `ServiceTimeouts.Shutdown = ExecutionLimits.MaxGracePeriod + 15 s + 30 s = 10 min 45 s`. This covers the largest possible grace period plus the 15 s `SchedulerLoop` waits on shutdown.
- E-mail (addition b): every SMTP connect, send and receive is bounded by `EmailSenderOptions.OperationTimeout = 30 s`. The whole send is also capped by `EmailSenderOptions.TotalTimeout = 90 s`.
- Restart (addition c): `IServiceControl.RestartAsync` schedules the restart and returns at once. The stop begins only after the current command has published its events.
- Tests:
  - Real SQLite files, real child processes, sockets and the real host are **integration tests**: the test class carries `[Trait("Category", "Integration")]`. CI (`windows-latest`) runs them all with `dotnet test -c Release`.
  - Fast local unit run: `dotnet test -c Release -- --filter-not-trait "Category=Integration"`. If the SDK rejects the `--`, pass the option without it.
  - Integration tests never use the real `%ProgramData%`. They use `TempDataRoot` (Task 2).
  - Test naming is `Method_Condition_ExpectedResult`. Put the inventory ID in a comment above each test that covers a row (`// BI-057`).

## Review Focus

1. **A batch script as the executable.** Users will register `.cmd` and `.bat` files. The script must run through `cmd.exe`, its exit code must be reported, its output captured, and the tree kill must reach what it started. Test in Task 6.
2. **Paths and arguments with spaces and non-ASCII characters.** For example, an executable in `C:\…\Tool Ü\app.exe`, a working directory with spaces, and the argument `"a b" c`. The child must receive exactly those arguments and start in exactly that folder. Test in Task 6.
3. **A child whose output never ends.** A child that floods stdout, or a grandchild that keeps the inherited stdout open after the child exits. The disk use must stay capped, the child must never block on a full pipe, and the run must end when the child exits, not when the pipe closes. Tests in Task 6.
4. **An environment variable that differs only in case from the service's own,** such as a registration that sets `path` while the service has `Path`. The registration's value must win, and the child must see a single variable. Test in Task 6.
5. **Process names that differ only in non-ASCII case,** such as `Relatório` and `RELATÓRIO`. The database must reject them as duplicates just as the domain does (OrdinalIgnoreCase), not only for ASCII. Test in Task 3.

## Execution notes

- Branch `feat/plan-3-infrastructure`, created from `feat/plan-2-application`, in the working copy (no worktrees). The Plan 2 fix wave (Run.Abandon honouring StopReason, the EvaluateProcess order for running services, the AlertFormatter snapshot-name fallback) must be on that branch first. The ports are stable.
- Every task ends with `dotnet build -c Release`, `dotnet test -c Release` and `dotnet format --verify-no-changes` green.
- Fake semantics the real implementations must match (from `tests/GapActivator.Testing/Application/`):
  - `InMemoryUnitOfWork.DequeueDomainEvents`: the events of **every** Process, Run and InstallationSettings loaded, added or removed in the scope. The removed ones count too, and their events are cleared after dequeue.
  - `InMemoryHistory.Add` is staged and saved only on commit. A failed commit saves nothing.
  - `InMemoryHistory.ListRunsAsync`:
    - filters on `ProcessId`, `Outcomes` (a run without an outcome never matches a non-null list), and `CreatedAt` (`From` inclusive, `To` exclusive);
    - orders by `CreatedAt` descending;
    - pages with `Skip((Page-1)*PageSize)`;
    - returns `TotalCount` before paging;
    - throws `ArgumentOutOfRangeException` for `Page < 1` or `PageSize` outside 1..200.
  - `ListEntriesForRunAsync` returns entries oldest first.
  - `LastCompletedRunsAsync`: per process, the completed run with the greatest `EndedAt ?? CreatedAt`.
  - `PurgeBeforeAsync`:
    - deletes completed runs with `EndedAt < cutoff`, all entries of those runs, and run-less entries with `OccurredAt < cutoff`;
    - returns the number of runs deleted;
    - applies immediately, outside the unit of work.
  - `FakeRunningProcess`:
    - `SignalCancel` is idempotent;
    - a kill completes `Exited` with `-1`;
    - `DisposeAsync` never kills.
  - `FakeProcessLauncher.TryAttach` returns null when no live process matches.
  - `FakeSecretProtector` round-trips.
  - `FakeLogRetention` purges log files and run output files.
  - `InMemoryStore` seeds `InstallationSettings.Default()`.
  - The fake keeps in-place mutations after a failed commit. The real unit of work must **not**, and Task 3 proves it.

## File Structure

```
.config/dotnet-tools.json                     (modify: dotnet-ef 10.0.14)
.editorconfig                                 (modify: generated_code for migrations)
Directory.Packages.props  GapActivator.slnx   (modify)
src/GapActivator.Domain/Processes/IProcessRepository.cs          (modify: ListIdsAsync)
src/GapActivator.Application/Queries/ListProcessIds.cs           (modify)
src/GapActivator.Application/Cycle/SchedulerLoop.cs              (modify: Reconciled)
src/GapActivator.Infrastructure/
  GapActivator.Infrastructure.csproj  DependencyInjection.cs  InfrastructureOptions.cs  GapActivatorPaths.cs
  Persistence/   GapActivatorDbContext.cs  ValueConversions.cs  ProcessConfiguration.cs  RunConfiguration.cs
                 SettingsConfiguration.cs  HistoryEntryConfiguration.cs  DesignTimeDbContextFactory.cs
                 DatabaseInitializer.cs  GapActivatorDatabase.cs  PersistenceServiceCollectionExtensions.cs
                 EfUnitOfWork.cs  EfProcessRepository.cs  EfRunRepository.cs  EfSettingsRepository.cs
                 EfHistoryWriter.cs  EfRunHistoryReader.cs  EfHistoryRetention.cs  Migrations/ (generated)
  Processes/     NativeMethods.cs  SafeJobHandle.cs  KernelObjectNames.cs  ParentIdentity.cs
                 CommandLineBuilder.cs  EnvironmentBlockBuilder.cs  RunOutputPump.cs
                 WindowsProcessLauncher.cs  WindowsRunningProcess.cs
  Files/         RunOutputReader.cs  FileLogRetention.cs
  Logging/       LogLevelSwitch.cs  DailyFileLoggerProvider.cs  SafeEventLogLoggerProvider.cs  GapActivatorLogging.cs
  Security/      DpapiSecretProtector.cs
  Email/         MailKitEmailSender.cs
  Notifications/ LoggingAppNotifier.cs
src/GapActivator.Service/
  GapActivator.Service.csproj  Program.cs  GapActivatorHost.cs  GapActivatorWorker.cs  GapActivatorServiceLifetime.cs
  ServiceTimeouts.cs  ExitCodeSink.cs  HostRestart.cs  ScopedServiceControl.cs
samples/GapActivator.SampleChild/  GapActivator.SampleChild.csproj  Program.cs
docs/child-contract.md  docs/running-the-service.md
tests/GapActivator.TestChild/      GapActivator.TestChild.csproj  Program.cs
tests/GapActivator.Testing/Infrastructure/TempDataRoot.cs
tests/GapActivator.Infrastructure.Tests/
  Support/  PersistenceTestHost.cs  FailingCommandInterceptor.cs  MiniSmtpServer.cs  ChildPaths.cs
  Persistence/  Processes/  Files/  Logging/  Security/  Email/  Notifications/  Composition/
tests/GapActivator.Service.Tests/  ServiceControlTests.cs  HostConfigurationTests.cs  WorkerTests.cs
                                   EndToEndTests.cs  ServiceExeSmokeTests.cs  Support/E2EFixture.cs
tests/GapActivator.Architecture.Tests/LayeringTests.cs            (modify)
```

Adapters are `internal sealed` unless a later project needs the type. `GapActivatorPaths`, `InfrastructureOptions`, `EmailSenderOptions`, `LogLevelSwitch`, `DatabaseInitializer`, `GapActivatorDatabase`, `DependencyInjection` and the `AddPersistence`/`AddGapActivatorLogging` extensions are `public`. Add `[assembly: InternalsVisibleTo("GapActivator.Infrastructure.Tests")]` to `InfrastructureAssembly.cs`.

---

### Task 1: Application-side hooks (ListIdsAsync, Reconciled)

**Files:**
- Modify: `src/GapActivator.Domain/Processes/IProcessRepository.cs`, `tests/GapActivator.Testing/Application/InMemoryProcessRepository.cs`, `src/GapActivator.Application/Queries/ListProcessIds.cs`, `src/GapActivator.Application/Cycle/SchedulerLoop.cs`
- Test: `tests/GapActivator.Application.Tests/Queries/QueryTests.cs`, `tests/GapActivator.Application.Tests/Cycle/SchedulerLoopTests.cs`

**Interfaces:**
- Produces: `Task<IReadOnlyList<ProcessId>> IProcessRepository.ListIdsAsync(CancellationToken ct);`. It reads ids only, so one unreadable row cannot fail the whole cycle listing (BI-039, carry-over). `ListProcessIdsHandler` uses it instead of `ListAsync`.
- Produces: `public Task Reconciled { get; }` on `SchedulerLoop`:
  - completes when `ReconcileOnStartup` has committed, before the first cycle;
  - faults with the reconcile exception;
  - is cancelled when the stopping token ends the loop first.

  The Plan 4 pipe server awaits it before accepting commands (ruling 13).
- Fake: `InMemoryProcessRepository.ListIdsAsync` returns the store's keys under `store.Sync`. It tracks nothing and ignores `FailOnLoad`.

- [ ] **Step 1: Write the failing tests**
  - `ListProcessIds_TwoProcesses_ReturnsBothIds`
  - `ListProcessIds_ProcessFailingToLoad_IsStillListed`: `Store.FailOnLoad(id)`, then the query still returns `id` (BI-039).
  - `Loop_Reconciled_CompletesBeforeTheFirstCycleStarts`: replace `ILogRetention` with a test retention whose `PurgeOlderThanAsync` records `loop.Reconciled.IsCompletedSuccessfully`. The first cycle runs retention, and the recorded value is `true`.
  - `Loop_ReconcileThrows_ReconciledFaultsWithTheSameException`
  - `Loop_StoppedBeforeReconcile_ReconciledIsCancelled`
- [ ] **Step 2: Run** `dotnet test -c Release --project tests/GapActivator.Application.Tests`. Expected: build FAILS (no `ListIdsAsync`, no `Reconciled`).
- [ ] **Step 3: Implement.** Back `Reconciled` with a `TaskCompletionSource` created with `RunContinuationsAsynchronously`.
- [ ] **Step 4: Run** the same command. Expected: PASS.
- [ ] **Step 5: Commit** `feat(application): list process ids without loading and expose reconciliation completion`

### Task 2: Persistence model, migration and database initializer

**Files:**
- Modify: `Directory.Packages.props` (all six packages, with reasons), `.config/dotnet-tools.json`, `.editorconfig`, `src/GapActivator.Infrastructure/GapActivator.Infrastructure.csproj`, `tests/GapActivator.Infrastructure.Tests/GapActivator.Infrastructure.Tests.csproj` and `tests/GapActivator.Architecture.Tests/*.csproj` (TFM `net10.0-windows`)
  - Infrastructure references Application.
  - The Infrastructure tests reference Application and `Microsoft.Extensions.TimeProvider.Testing`.
- Create: `GapActivatorPaths.cs`, `InfrastructureOptions.cs`, `Persistence/GapActivatorDbContext.cs`, `ValueConversions.cs`, the four `*Configuration.cs`, `DesignTimeDbContextFactory.cs`, `DatabaseInitializer.cs`, `GapActivatorDatabase.cs`, `Persistence/Migrations/*` (generated)
- Create: `tests/GapActivator.Testing/Infrastructure/TempDataRoot.cs`
- Test: `tests/GapActivator.Infrastructure.Tests/Persistence/MappingTests.cs`, `DatabaseInitializerTests.cs`, `GapActivatorPathsTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record GapActivatorPaths              // ctor throws ArgumentException unless DataRoot is fully qualified
{
    public const string DataRootKey = "GapActivator:DataRoot";
    public GapActivatorPaths(string dataRoot);
    public static GapActivatorPaths Default { get; }  // %ProgramData%\GapActivator (SpecialFolder.CommonApplicationData)
    public string DataRoot { get; }
    public string DatabaseFile { get; }              // {DataRoot}\gapactivator.db
    public string LogsDirectory { get; }             // {DataRoot}\logs
    public string RunLogsDirectory { get; }          // {DataRoot}\logs\runs
    public string RunOutputFile(RunId runId);        // {RunLogsDirectory}\{runId:D lower}.log
    public string ServiceLogFile(DateOnly localDay); // {LogsDirectory}\gapactivator-yyyyMMdd.log
    public void EnsureDirectories();                 // creates DataRoot, logs, logs\runs
}
public sealed record InfrastructureOptions(GapActivatorPaths Paths)
{
    public long RunOutputMaxBytes { get; init; } = 52_428_800;          // 50 MiB
    public TimeSpan KillWaitTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public EmailSenderOptions Email { get; init; } = new();
}
public sealed class GapActivatorDbContext(DbContextOptions<GapActivatorDbContext> options) : DbContext(options)
{ DbSet<Process> Processes; DbSet<Run> Runs; DbSet<InstallationSettings> Settings; DbSet<HistoryEntry> HistoryEntries; }
public sealed class DatabaseInitializer(GapActivatorDbContext db, GapActivatorPaths paths, LogLevelSwitch logLevel)
{ public Task InitializeAsync(CancellationToken ct); }
public static class GapActivatorDatabase
{ public static string ConnectionString(GapActivatorPaths paths);   // Data Source=<file>;Mode=ReadWriteCreate;Default Timeout=30;Pooling=True
  public static void ReleasePooledConnections(); }                 // SqliteConnection.ClearAllPools(), for tests
public sealed class TempDataRoot : IAsyncDisposable   // in GapActivator.Testing
{ public string Path { get; }                          // %TEMP%\GapActivator.Tests\{Guid:N}
  public ValueTask DisposeAsync(); }                   // deletes the folder, retrying 5 times 200 ms apart, never throws
```

`LogLevelSwitch` is created here as a minimal class: `Set(Domain.Settings.LogLevel)` and `Minimum` (`Microsoft.Extensions.Logging.LogLevel`). Task 3 uses it and Task 9 adds the logging around it.

**Mapping.** Every converter lives in `ValueConversions.cs` and is applied in `ConfigureConventions`:
- `DateTimeOffset` and `DateTimeOffset?` become INTEGER UTC ticks: `v.UtcTicks` ↔ `new DateTimeOffset(ticks, TimeSpan.Zero)`.
- `TimeSpan` becomes INTEGER ticks.
- Every enum is stored as TEXT (its name).
- `ProcessId`, `RunId` and `ScheduleId` become `Guid`.
- `ProcessName`, `ExecutablePath` and `RecurrenceRule` become TEXT through their `Create`/`Parse` factories.

| Table | Columns and keys |
|---|---|
| `Processes` | `Id` PK (never generated) · `Name` · `NameKey` (shadow, `Name.ToUpperInvariant()`, **unique**) · `Description` · `Kind` · complex `Launch`: `Executable`, `Arguments`, `WorkingDirectory`, `Environment` (TEXT JSON) · `ExecutableKey` (shadow, upper-invariant, indexed) · complex `Limits`: `TimeLimit`, `GracePeriod` · `Lifecycle` · `ConsecutiveFailures` · `RetryPending` · `CreatedAt` · `UpdatedAt` · `Version` (shadow int, concurrency token) |
| `Schedules` | `Id` PK (never generated) · `ProcessId` (shadow FK, cascade) · `Kind` discriminator `OneTime`/`Recurring` · `NextOccurrenceAt` · `At` · `FulfilledAt` · `Rule` · `Anchor` |
| `Runs` | `Id` PK (never generated) · `ProcessId` (no FK: history outlives the process) · `ProcessName` (from `ProcessNameSnapshot`) · `Kind` · `Trigger` · `ScheduledFor` · `IsLate` · `State` · `StopReason` · `Pid` · `ProcessStartTime` · `CreatedAt` · `StartedAt` · `EndedAt` · `Outcome` · `ExitCode` · `Killed` · `Error` · `Version` · **unique** index on `ProcessId` filtered `"State" <> 'Completed'` · indexes on `CreatedAt`, (`ProcessId`, `CreatedAt`) and `EndedAt` |
| `Settings` | `Id` PK (always 1) · one column per `InstallationSettings` scalar · `Email` (TEXT JSON) · `Version` |
| `HistoryEntries` | `Id` PK (never generated) · `OccurredAt` · `Ordinal` (shadow int) · `Kind` · `ProcessId` · `RunId` · `Outcome` · `ExitCode` · `Message` · indexes on `RunId` and `OccurredAt` |

- `Process.Schedules` is mapped through the field `_schedules` with `UsePropertyAccessMode(PropertyAccessMode.Field)` and `AutoInclude()`. The schedule hierarchy is TPH on `Schedules`.
- `LaunchOptions` and `ExecutionLimits` are EF complex properties, materialized through their constructors.
- `EnvironmentVariables` JSON is `[{"name":"…","value":"…"}|{"name":"…","secret":"<ciphertext>"}]`. It is read back through `EnvironmentVariable.Plain` or `Secret`, then `EnvironmentVariables.Create`.
- `EmailAlertSettings` JSON holds every field, with `password` as ciphertext or null. It is read back through `EmailAlertSettings.Create`.
- Each JSON converter has a `ValueComparer` that uses the type's own `Equals` and `GetHashCode`, with a by-reference snapshot (the types are immutable).
- `NameKey`, `ExecutableKey` and `Version` are set in an override of `SaveChangesAsync`:
  - `NameKey` and `ExecutableKey` for Added and Modified processes;
  - `Version` is `original + 1` for Modified `Process`, `Run` and `InstallationSettings` entries.

**DatabaseInitializer.InitializeAsync** runs, in order:
1. `paths.EnsureDirectories()`;
2. `db.Database.MigrateAsync`;
3. `PRAGMA journal_mode=WAL;` (persisted in the file);
4. if the `Settings` row 1 is missing, add `InstallationSettings.Default()` and save;
5. `logLevel.Set(settings.LogLevel)`.

It is idempotent.

- [ ] **Step 1: Add packages, tool, TFMs and the `.editorconfig` section**

```ini
[src/GapActivator.Infrastructure/Persistence/Migrations/**.cs]
generated_code = true
```

- [ ] **Step 2: Write the failing tests.** `MappingTests` and `DatabaseInitializerTests` are integration tests:
  - `Model_HasNoPendingChanges`: a context over an open in-memory `SqliteConnection("Data Source=:memory:")`, then `db.Database.HasPendingModelChanges().ShouldBeFalse()`. This test is not tagged Integration.
  - `Process_RoundTrip_PreservesEveryField`. Register a process with:
    - a OneTime schedule (later fulfilled through `ConsumeDueOccurrences`) and a Recurring `FREQ=HOURLY` schedule;
    - a plain and a secret variable;
    - arguments `"a b" c`, a working directory, `TimeLimit = TimeSpan.FromDays(3650)` (no upper bound, carry-over) and a grace period of 45 s;
    - lifecycle Disabled and a counter of 2.

    Save it, then reload it in a new context. Every property must be equal, including `NextOccurrenceAt`, `FulfilledAt`, `Anchor` and `Rule.Value`.
  - `Run_RoundTrip_PreservesEveryFieldToTheTick`: `ProcessStartTime` with 7 fractional digits comes back with the same `UtcTicks`.
  - `Settings_RoundTrip_PreservesEmailWithPasswordAndPausedFlag`
  - `HistoryEntry_RoundTrip_PreservesNullableIdsAndOutcome`
  - `Dates_DifferentOffsets_SortByInstantInSql`: three runs whose `CreatedAt` values use offsets +03:00, Z and -05:00 come back from `OrderBy(r => r.CreatedAt)` in instant order.
  - `Schema_SecondActiveRunForSameProcess_IsRejected`: two Launching runs for the same process make `SaveChangesAsync` throw `DbUpdateException`. A Completed run plus an active one is fine.
  - `Initialize_EmptyFolder_CreatesFoldersDatabaseAndDefaultSettings` (BI-091)
  - `Initialize_Twice_KeepsChangedSettings`
  - `Initialize_JournalModeIsWal`: `PRAGMA journal_mode` returns `wal`.
  - `Paths_Default_IsProgramDataGapActivator` (BI-091) and `Paths_RelativeDataRoot_Throws`
- [ ] **Step 3: Run** `dotnet test -c Release --project tests/GapActivator.Infrastructure.Tests`. Expected: build FAILS.
- [ ] **Step 4: Implement the model**, then generate the migration:

Run: `dotnet tool restore; dotnet ef migrations add InitialCreate --project src/GapActivator.Infrastructure --startup-project src/GapActivator.Infrastructure --output-dir Persistence/Migrations`
Expected: `Done.`, and the files appear under `Persistence/Migrations/`. `DesignTimeDbContextFactory` uses `Data Source=design-time.db`, and no database file is created.

- [ ] **Step 5: Run** the tests. Expected: PASS. Then run `dotnet format --verify-no-changes`. Expected: no changes, because the migrations are generated code.
- [ ] **Step 6: Commit** `feat(infrastructure): add SQLite model, initial migration and database initializer`

### Task 3: Repositories, unit of work and history writer

**Files:**
- Create: `Persistence/EfUnitOfWork.cs`, `EfProcessRepository.cs`, `EfRunRepository.cs`, `EfSettingsRepository.cs`, `EfHistoryWriter.cs`, `PersistenceServiceCollectionExtensions.cs`
- Create: `tests/GapActivator.Infrastructure.Tests/Support/PersistenceTestHost.cs`, `FailingCommandInterceptor.cs`
- Test: `tests/GapActivator.Infrastructure.Tests/Persistence/RepositoryTests.cs`, `UnitOfWorkTests.cs`

**Interfaces:**
- Consumes: Task 2 context and paths; Task 1 `ListIdsAsync`.
- Produces:

```csharp
public static IServiceCollection AddPersistence(this IServiceCollection services, GapActivatorPaths paths,
    Action<DbContextOptionsBuilder>? configure = null);
// scoped: GapActivatorDbContext, EfUnitOfWork as IUnitOfWork, IProcessRepository, IRunRepository, ISettingsRepository,
//         IHistoryWriter, DatabaseInitializer; TryAddSingleton: GapActivatorPaths, LogLevelSwitch.
// Task 4 adds IRunHistoryReader and IHistoryRetention here.
internal sealed class FailingCommandInterceptor(string commandTextFragment) : DbCommandInterceptor;  // test support: throws on a matching command
public sealed class PersistenceTestHost : IAsyncDisposable
// AddApplication + AddPersistence(temp root) + every Plan 2 fake except persistence, with FakeTimeProvider at
// ApplicationTestHost.StartTime; InitializeAsync runs DatabaseInitializer; exposes Send, Query, Launcher, Time, Paths,
// and NewScopeDb() for raw checks. DisposeAsync: provider, ReleasePooledConnections, TempDataRoot.
```

Rules:
- **EfUnitOfWork.**
  - `DequeueDomainEvents` walks `ChangeTracker.Entries()` in every state, Deleted included. It collects `DomainEvents` from Process, Run and InstallationSettings entries, then calls `ClearDomainEvents()` on each.
  - `CommitAsync` is one `SaveChangesAsync`, so EF's implicit transaction holds aggregates and history entries together (carry-over).
  - After a successful save, if an `InstallationSettings` entry was Added or Modified, it calls `LogLevelSwitch.Set(settings.LogLevel)`.
  - A `DbUpdateConcurrencyException` propagates untranslated (BI-042). The dispatcher drops the scope, and the supervisor retries with a fresh one.
- **EfProcessRepository.**
  - `NameExistsAsync` compares `NameKey == name.Value.ToUpperInvariant()`, excluding `excluding`.
  - `ListByExecutableAsync` compares `ExecutableKey` the same way.
  - `ListIdsAsync` is `Select(p => p.Id)` without materializing entities.
  - Reads are tracked.
- **EfRunRepository.** "Active" means `State != RunState.Completed`.
- **EfSettingsRepository.GetAsync** returns row 1, tracked. A missing row throws `InvalidOperationException("The database is not initialized.")`.
- **EfHistoryWriter.Add** adds the entry and sets the shadow `Ordinal` to a per-scope counter (0, 1, 2…), so entries of the same instant keep their insertion order.

- [ ] **Step 1: Write the failing tests** (integration tests, through the real dispatcher in `PersistenceTestHost`):
  - `Register_ThenGetProcessInNewScope_ReturnsIt`
  - `Update_ReplacingSchedules_InsertsNewKeepsUnchangedDeletesRemoved`: keep one schedule, drop one, add one. Reload the process: there are exactly 2 schedules, and the kept one still has its `NextOccurrenceAt`.
  - `Delete_Idle_RemovesProcessAndSchedulesKeepsRuns`
  - `Register_NameDifferingOnlyInNonAsciiCase_IsRejectedNameTaken` (Review Focus 5, BI-001): register `Relatório`, then register `RELATÓRIO`. The second must throw `CommandRejectedException` with code `name-taken`.
  - `Schema_NameKeyUnique_RejectsDirectInsertOfNonAsciiCaseDuplicate` (Review Focus 5)
  - `ListByExecutable_DifferentCase_Matches` (BI-010)
  - `Commit_HistoryInsertFails_NothingPersistedAndNothingPublished`: a `FailingCommandInterceptor("INSERT INTO \"HistoryEntries\"")`, then send `PauseInstallation`. The send must throw. A new scope sees `IsPaused == false` and no history, and the app notifier recorded nothing. This is the real rollback test (carry-over).
  - `Commit_AfterFailure_LaterScopeSeesOriginalState`: a `FailingCommandInterceptor("INSERT INTO \"Runs\"")`, then `RunNow` on a process with a due hourly schedule. The send must throw. In a new scope the schedule's `NextOccurrenceAt` is unchanged, `UpdatedAt` is unchanged, and there are no runs. The in-memory fake cannot show this.
  - `Commit_RunChangedByAnotherScope_ThrowsAndFirstWriteWins` (BI-042): two scopes load the same Running run. Scope A requests a stop and commits; scope B marks it exited and commits. B must throw `DbUpdateConcurrencyException`, and the reload shows A's state.
  - `Commit_SettingsLogLevelChanged_UpdatesLogLevelSwitch`
  - `DequeueDomainEvents_LoadedAndAddedAggregates_ReturnsTheirEventsThenNone`
  - `Pause_PersistsAcrossRestart` (BI-076/Q-1 persistence): send `PauseInstallation`, dispose the host, build a new host on the same root. `GetSettings` returns `IsPaused == true`.
  - `Settings_ReadWhileAnotherScopeHoldsAWriteTransaction_Succeeds` (carry-over: SendTestEmail is ungated). Scope A runs `BeginTransactionAsync` and updates settings without committing; scope B's `GetAsync` returns the old values within 2 s.
  - `Cycle_ProcessRowWithCorruptRule_OtherProcessesStillEvaluated` (BI-039 at the storage level): two due processes, then raw SQL `UPDATE "Schedules" SET "Rule" = 'NOT A RULE'` on one. `ListProcessIds` returns both, and `CycleRunner.RunOnceAsync` launches the healthy one (`Launcher.LaunchRequests`).
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(infrastructure): add EF Core repositories and transactional unit of work`

### Task 4: History reader and history retention

**Files:**
- Create: `Persistence/EfRunHistoryReader.cs`, `EfHistoryRetention.cs`
- Modify: `PersistenceServiceCollectionExtensions.cs`
- Test: `tests/GapActivator.Infrastructure.Tests/Persistence/HistoryReaderTests.cs`, `HistoryRetentionTests.cs`

**Interfaces:**
- Consumes: `IRunHistoryReader`, `IHistoryRetention`, `RunFilter`, `RunPage` (Plan 2, unchanged).
- Produces: both registered as scoped in `AddPersistence`. Both read `AsNoTracking`, because queries get their own scope from `QueryDispatcher` (ruling 15).

Rules (the fake's semantics, listed in Execution notes):
- `ListRunsAsync`:
  - validates first, throwing `ArgumentOutOfRangeException` for `Page < 1` or `PageSize` outside 1..200;
  - orders by `CreatedAt` descending, then `Id` descending;
  - gets `TotalCount` from `CountAsync` before `Skip`/`Take`.
- `ListEntriesForRunAsync` orders by `OccurredAt`, then `Ordinal`.
- `LastCompletedRunsAsync`: for each process, the completed run for which no other completed run of the same process has a later `EndedAt`, or the same `EndedAt` and a greater `Id`. Express it as a correlated `!Any(...)` subquery, so exactly one run per process comes back even on ties. The per-process read for GetProcess is declined (see Decisions).
- `PurgeBeforeAsync(cutoff)`, inside one explicit transaction:
  1. `ExecuteDeleteAsync` on the entries whose `RunId` is in (completed runs with `EndedAt < cutoff`), or whose `RunId` is null and `OccurredAt < cutoff`;
  2. `ExecuteDeleteAsync` on those runs, returning the count;
  3. commit.

  It bypasses the change tracker, like the fake.

- [ ] **Step 1: Write the failing tests** (integration tests):
  - `ListRuns_NewestCreatedAtFirstWithPagingAndTotal`: 5 runs, page 2 of size 2, gives runs 3 and 2 with `TotalCount == 5`.
  - `ListRuns_FiltersByProcessOutcomesAndRange_FromInclusiveToExclusive`
  - `ListRuns_RunWithoutOutcome_NeverMatchesAnOutcomeFilter`
  - `ListRuns_PageSizeOutOfRange_Throws` (theory: 0 and 201)
  - `ListEntriesForRun_SameInstant_KeepsInsertionOrder`: one commit records RunStarted and then RunStopRequested at the same instant, and they come back in that order.
  - `LastCompletedRuns_OnePerProcessLatestEnded_EvenOnTies`
  - `PurgeBefore_RemovesOldCompletedRunsTheirEntriesAndOldRunlessEntries`: it keeps active runs, recent runs, entries of kept runs and recent run-less entries, and returns the number of purged runs.
  - `PurgeHistory_ThroughDispatcher_UsesNowMinusRetentionDays` (BI-089 at the storage level)
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(infrastructure): add run history reader and history retention`

### Task 5: DPAPI secret protector, MailKit sender and logging app notifier

**Files:**
- Create: `Security/DpapiSecretProtector.cs`, `Email/MailKitEmailSender.cs`, `Notifications/LoggingAppNotifier.cs`
- Create: `tests/GapActivator.Infrastructure.Tests/Support/MiniSmtpServer.cs`
- Test: `tests/GapActivator.Infrastructure.Tests/Security/DpapiSecretProtectorTests.cs`, `Email/MailKitEmailSenderTests.cs`, `Notifications/LoggingAppNotifierTests.cs`

**Interfaces:**
- Produces:

```csharp
internal sealed class DpapiSecretProtector : ISecretProtector
// Protect: UTF-8 bytes -> ProtectedData.Protect(bytes, Entropy, DataProtectionScope.LocalMachine) -> "dpapi:v1:" + Base64.
// Unprotect: requires the "dpapi:v1:" prefix, else throws CryptographicException("Unknown secret format.").
// Entropy = UTF-8 of "GapActivator.Secret.v1".
public sealed record EmailSenderOptions
{ public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromSeconds(30);   // SmtpClient.Timeout: connect, send and receive
  public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(90); }     // linked CancelAfter over the whole send
internal sealed class MailKitEmailSender(ISecretProtector secrets, EmailSenderOptions options) : IEmailSender
internal static SecureSocketOptions SecureSocketOptionsFor(TlsMode mode); // None->None, StartTls->StartTls, Implicit->SslOnConnect
internal sealed class LoggingAppNotifier(ILogger<LoggingAppNotifier> logger) : IAppNotifier
// Information/Warning/Error -> the same MEL level; message "{Title}: {Message}" with ProcessId and RunId as state. Plan 4 replaces it.
internal sealed class MiniSmtpServer : IAsyncDisposable   // test support on 127.0.0.1:0
{ int Port; bool AdvertiseAuthPlain; bool NeverGreet; string? RejectRecipientWith; // e.g. "550 no such user"
  IReadOnlyList<ReceivedMail> Received; string? AuthenticatedUser; string? AuthenticatedPassword; }
```

`MailKitEmailSender.SendAsync`:
1. Build a `MimeMessage`: From `Sender`, To every recipient, `Subject`, and a text/plain `Body`.
2. Connect with `SecureSocketOptionsFor(Tls)`, `Timeout = OperationTimeout`, under the linked token.
3. Authenticate when `Username` is non-empty, with the password `secrets.Unprotect(Password)`, or `""` when there is none.
4. Send, then disconnect with `quit: true`.

Error handling:
- An `OperationCanceledException` while the caller's `ct` is cancelled propagates.
- Any other exception, the total timeout included, becomes `EmailDeliveryException("SMTP delivery failed: {inner.Message}", inner)`.

The server certificate is validated with MailKit's default callback.

- [ ] **Step 1: Write the failing tests.** The SMTP tests are integration tests because they use sockets; the others are not:
  - `Protect_ThenUnprotect_RoundTrips` (theory: `""`, `p@ss`, `senha-çãõ-密码`)
  - `Protect_SamePlaintextTwice_GivesDifferentCiphertextsWithPrefix`
  - `Unprotect_TamperedCiphertext_ThrowsCryptographicException`
  - `Unprotect_ForeignFormat_ThrowsCryptographicException` (`enc:x`)
  - `SecureSocketOptionsFor_MapsEveryTlsMode` (theory)
  - `Send_PlainSmtp_DeliversSenderAllRecipientsSubjectAndBody` (BI-098): two recipients, `alerts@example.com` and `ops@example.com`.
  - `Send_WithCredentials_AuthenticatesWithUnprotectedPassword` (BI-098): `FakeSecretProtector`, then the server saw user `user` and password `secret`.
  - `Send_StartTlsRequiredButNotOffered_ThrowsEmailDeliveryException`
  - `Send_RecipientRejected_ThrowsEmailDeliveryException`
  - `Send_NothingListening_ThrowsEmailDeliveryException`
  - `Send_ServerNeverGreets_FailsWithinTheOperationTimeout` (addition b): `OperationTimeout = 1 s`, so `EmailDeliveryException` comes in under 5 s.
  - `Send_CallerCancels_ThrowsOperationCanceled`
  - `Notify_LogsAtTheNotificationSeverity` (theory over the three severities, with a recording `ILogger`)
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(infrastructure): add DPAPI secrets, MailKit e-mail and logging app notifier`

### Task 6: Windows process launcher (launch, output, environment, cancel event, job)

**Files:**
- Create: `Processes/NativeMethods.cs`, `SafeJobHandle.cs`, `KernelObjectNames.cs`, `ParentIdentity.cs`, `CommandLineBuilder.cs`, `EnvironmentBlockBuilder.cs`, `RunOutputPump.cs`, `WindowsProcessLauncher.cs`, `WindowsRunningProcess.cs`
- Create: `tests/GapActivator.TestChild/GapActivator.TestChild.csproj` (Exe, `net10.0-windows`, `AllowUnsafeBlocks`, in the slnx under `/tests/`) and `Program.cs`; `tests/GapActivator.Infrastructure.Tests/Support/ChildPaths.cs`
- Modify: `tests/GapActivator.Infrastructure.Tests/*.csproj` (a `ProjectReference` to the TestChild, so its apphost, dll, `runtimeconfig.json` and `deps.json` are copied next to the tests). Verify that `GapActivator.TestChild.exe` is in the test bin folder. If the SDK does not copy the apphost, add a `None` item with `CopyToOutputDirectory` for the TestChild output files.
- Test: `tests/GapActivator.Infrastructure.Tests/Processes/LauncherTests.cs`, `CommandLineBuilderTests.cs`, `EnvironmentBlockBuilderTests.cs`

**Interfaces:**
- Consumes: `IProcessLauncher`, `IRunningProcess`, `LaunchRequest`, `LaunchFailedException` (Plan 2); `InfrastructureOptions`.
- Produces:

```csharp
internal static class KernelObjectNames
{ public static string CancelEvent(int pid) => $@"Local\GapActivator.Cancel.{pid}";
  public static string Job(int pid, DateTimeOffset startTime) =>
    string.Create(CultureInfo.InvariantCulture, $@"Local\GapActivator.Job.{pid}.{startTime.UtcTicks}"); }
internal sealed record ParentIdentity(int Pid, DateTime StartTimeUtc)
{ public static ParentIdentity Current { get; }             // Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime()
  public string FormattedStartTime => StartTimeUtc.ToString("O", CultureInfo.InvariantCulture); }
internal static class CommandLineBuilder
{ public static (string ApplicationName, string CommandLine) Build(string executable, string arguments); }
  // .cmd/.bat (OrdinalIgnoreCase): app = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
  //   line = "\"{cmd}\" /d /s /c \"\"{executable}\" {arguments}\"" ; otherwise app = executable, line = "\"{executable}\" {arguments}"
  //   (trailing space trimmed when arguments are empty); arguments are passed verbatim.
internal static class EnvironmentBlockBuilder
{ public static char[] Build(IDictionary serviceEnvironment, IReadOnlyList<KeyValuePair<string,string>> registration, ParentIdentity parent); }
  // Dictionary(StringComparer.OrdinalIgnoreCase): service env, then registration overrides (Review Focus 4), then
  // GAPACTIVATOR_PARENT_PID / GAPACTIVATOR_PARENT_START_TIME; sorted OrdinalIgnoreCase; "k=v\0"... + "\0".
internal sealed class WindowsProcessLauncher(InfrastructureOptions options, ILogger<WindowsProcessLauncher> logger) : IProcessLauncher
internal sealed class WindowsRunningProcess : IRunningProcess
internal static class ChildPaths   // test support
{ public static string TestChild => Path.Combine(AppContext.BaseDirectory, "GapActivator.TestChild.exe"); }
```

Interop declarations. `NativeMethods.cs` holds exactly these; the bodies are written by the implementer:

```csharp
internal static partial class NativeMethods
{
    internal const uint CREATE_SUSPENDED = 0x00000004, CREATE_UNICODE_ENVIRONMENT = 0x00000400, CREATE_NO_WINDOW = 0x08000000;
    internal const int STARTF_USESTDHANDLES = 0x00000100;
    internal const uint SYNCHRONIZE = 0x00100000, PROCESS_TERMINATE = 0x0001, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint JOB_OBJECT_QUERY = 0x0004, JOB_OBJECT_TERMINATE = 0x0008;
    internal const uint STILL_ACTIVE = 259;
    internal const int ERROR_FILE_NOT_FOUND = 2, ERROR_PATH_NOT_FOUND = 3, ERROR_ACCESS_DENIED = 5,
                       ERROR_BAD_EXE_FORMAT = 193, ERROR_DIRECTORY = 267;

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOW
    {
        public int cb; public nint lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public nint lpReserved2;
        public nint hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION { public nint hProcess, hThread; public int dwProcessId, dwThreadId; }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool CreateProcess(string lpApplicationName, char* lpCommandLine, nint lpProcessAttributes,
        nint lpThreadAttributes, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles, uint dwCreationFlags,
        char* lpEnvironment, string lpCurrentDirectory, in STARTUPINFOW lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial uint ResumeThread(nint hThread);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool CloseHandle(nint handle);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetProcessTimes(SafeProcessHandle h, out long creation, out long exit, out long kernel, out long user);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeProcess(SafeProcessHandle h, out uint exitCode);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateProcess(SafeProcessHandle h, uint exitCode);
    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeJobHandle CreateJobObject(nint attributes, string name);
    [LibraryImport("kernel32.dll", EntryPoint = "OpenJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeJobHandle OpenJobObject(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, string name);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateJobObject(SafeJobHandle job, uint exitCode);
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsProcessInJob(SafeProcessHandle process, SafeJobHandle job, [MarshalAs(UnmanagedType.Bool)] out bool result);
}
internal sealed class SafeJobHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
{ protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle); }
```

**LaunchAsync** steps. This is the algorithm, which the tests do not fully determine:
1. `ct.ThrowIfCancellationRequested()`. After step 4, `ct` is ignored, because the OS process exists.
2. Open the run output file `FileMode.Append`, `FileAccess.Write`, `FileShare.Read` (best effort). On failure, log a Warning and discard the output (BI-085).
3. Output pipe:
   - read end: `NamedPipeServerStream($"GapActivator.Output.{Guid:N}", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)`;
   - write end: a `NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.None, TokenImpersonationLevel.None, HandleInheritability.Inheritable)`, connected right away;
   - stdin: `File.OpenHandle("NUL", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Inheritable)`.

   stdout and stderr both get the same write handle, so the file keeps the child's write order.
4. Inside a static `Lock` (so no concurrent launch inherits another child's pipe), call `CreateProcess`:
   - `CREATE_SUSPENDED | CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT`;
   - `bInheritHandles = true`;
   - `STARTF_USESTDHANDLES`;
   - the environment block from `EnvironmentBlockBuilder`;
   - `lpCurrentDirectory = request.WorkingDirectory`.

   Then dispose the client pipe and NUL handles, still inside the lock. On failure, map the Win32 error to `LaunchFailedException`:
   - 2 or 3 → `The executable '{path}' was not found.`
   - 5 → `Access to '{path}' was denied for the service account.`
   - 193 → `'{path}' is not a valid Windows program.`
   - 267 → `The working directory '{dir}' does not exist.`
   - else → `The process could not be started: {Win32Exception.Message} (error {code}).`
5. While the child is suspended:
   1. `CreateJobObject(0, KernelObjectNames.Job(pid))` and `AssignProcessToJobObject`.
   2. `new EventWaitHandle(false, EventResetMode.ManualReset, KernelObjectNames.CancelEvent(pid), out bool created)`. Call `Reset()` when `!created`, because a stale event from a reused PID must not cancel the new child.
   3. `GetProcessTimes` → `StartTime = DateTimeOffset.FromFileTime(creation).ToUniversalTime()`.

   Any failure here calls `TerminateProcess(h, 1)`, releases everything and throws `LaunchFailedException("The process could not be prepared: …")`.
6. `ResumeThread`, then `CloseHandle(hThread)`.
7. Start `RunOutputPump`: `await server.ReadAsync` into the file. The pump enforces `RunOutputMaxBytes`: past the cap it writes `\r\n[GapActivator] Output truncated after {max} bytes.\r\n` once and keeps draining. It flushes after each chunk. A write failure stops writing, and the pump keeps draining.
8. Start the exit wait: `ThreadPool.RegisterWaitForSingleObject` on a `WaitHandle` whose `SafeWaitHandle` wraps the process handle without owning it. The callback runs `GetExitCodeProcess` and `TrySetResult(unchecked((int)code))`.

**WindowsRunningProcess:**
- `SignalCancel()` is `cancelEvent.Set()`, idempotent. After `DisposeAsync` it does nothing.
- `KillTreeAsync()`:
  - returns at once if `Exited` already completed;
  - otherwise `TerminateJobObject(job, uint.MaxValue)`, so `Exited` reports `-1` like the fake. With no job (adopted without one, Task 7), it calls `Process.GetProcessById(Pid).Kill(entireProcessTree: true)`. The PID cannot be reused while this object holds the process handle.
  - then `await Exited.WaitAsync(options.KillWaitTimeout)`. A `TimeoutException` propagates, and the supervisor retries every 30 s. It never hangs (carry-over).
- `DisposeAsync()`:
  - unregisters the wait;
  - waits up to 2 s for the pump to reach EOF, then cancels it and disposes the pipe and the file;
  - disposes the event, the job handle and the process handle;
  - never kills the child, and is idempotent.

**TestChild** commands (first argument). It sets `Console.OutputEncoding = UTF8` and writes each line with an immediate flush:
- `exit <code>`
- `sleep <ms> [code]`
- `print-both`: stdout `out-1`, stderr `err-1`, stdout `out-2`.
- `echo-args …`: `arg:<value>` per argument.
- `cwd`: `cwd:<dir>`.
- `env <names…>`: `env:<name>=<value|<unset>>` and `envcount:<name>=<keys equal ignoring case>`.
- `check-event`: `event:found` or `event:missing` from `EventWaitHandle.TryOpenExisting(@"Local\GapActivator.Cancel." + Environment.ProcessId, out _)`, as the first statement.
- `console-window`: `console-window:none` when `GetConsoleWindow() == 0` (its own `[LibraryImport]`).
- `honor-cancel`: opens the event, prints `ready`, waits up to 60 s, prints `cancelled`, exits 0.
- `ignore-cancel`: prints `ready`, sleeps 10 min.
- `spawn-grandchild [exit]`: starts `GapActivator.TestChild sleep 600000` with `UseShellExecute=false` and no redirection, so the grandchild inherits stdout. It prints `grandchild:<pid>`, then sleeps 10 min, or exits 0 at once when `exit` is given.
- `flood <bytes>`: writes that many `x`, then exits 0.

- [ ] **Step 1: Write the failing tests.** The builders are unit tests. `LauncherTests` is an integration test class; every test kills its leftover children in `DisposeAsync` with `Process.Kill(true)`.
  - `CommandLineBuilder_PlainExecutable_QuotesPathAndAppendsArgumentsVerbatim`
  - `CommandLineBuilder_CmdOrBat_UsesCmdExeWithDSC` (theory: `.cmd`, `.BAT`)
  - `EnvironmentBlockBuilder_RegistrationOverridesServiceVariableIgnoringCase` (Review Focus 4)
  - `EnvironmentBlockBuilder_AddsParentIdentityAndIsSortedAndDoubleNulTerminated`
  - `Launch_ExitCode_IsReported`: `exit 7` → 7.
  - `Launch_StdoutAndStderr_AreCapturedInWriteOrder` (BI-084): the file is `out-1\r\nerr-1\r\nout-2\r\n`.
  - `Launch_PathsAndArgumentsWithSpacesAndNonAscii_PassedVerbatim` (Review Focus 2, BI-045, Q-5): copy the TestChild files to `{temp}\Tool Ü dir\` and use the working directory `{temp}\work dir ç`. `echo-args "a b" c` gives `arg:a b` and `arg:c`; `cwd` gives that folder.
  - `Launch_Environment_ContainsParentIdentityInContractFormat` (BI-060): `GAPACTIVATOR_PARENT_PID == Environment.ProcessId`, and the start time ends in `Z` and parses with `DateTime.ParseExact(…, "O", …, RoundtripKind)` to exactly `ParentIdentity.Current.StartTimeUtc`.
  - `Launch_RegistrationPathInDifferentCase_ChildSeesOneVariableWithRegistrationValue` (Review Focus 4): `envcount:PATH=1` and `env:PATH=C:\custom`.
  - `Launch_CancelEventExistsBeforeChildRuns` (BI-057): `check-event` prints `event:found`.
  - `Launch_Child_HasNoConsoleWindow` (BI-045)
  - `Launch_MissingExecutable_ThrowsLaunchFailedNamingThePath`
  - `Launch_MissingWorkingDirectory_ThrowsLaunchFailed`
  - `Launch_TokenAlreadyCancelled_ThrowsOperationCanceledAndStartsNothing`
  - `Launch_BatchFile_RunsThroughCmdAndReportsItsExitCode` (Review Focus 1): `hello world.cmd` contains `@echo hello from cmd` and `@exit /b 3`. `Exited` is 3, and the output contains `hello from cmd`.
  - `Launch_OutputFolderUnwritable_ChildStillRuns` (BI-085): `logs\runs` is a file, and `exit 0` still gives 0.
  - `Output_OverTheCap_IsTruncatedWithMarkerAndChildNotBlocked` (Review Focus 3): `RunOutputMaxBytes = 1000` with `flood 100000`. The child exits 0 within 10 s, and the file length is at most 1000 plus the marker, ending with the marker.
  - `Exited_GrandchildKeepsStdoutOpen_CompletesAtChildExit` (Review Focus 3): `spawn-grandchild exit`. `Exited` completes within 5 s and `DisposeAsync` within 5 s while the grandchild is alive.
  - `SignalCancel_ChildHonoringEvent_ExitsCleanly` (BI-057): `honor-cancel` exits 0 within 5 s, and its output contains `cancelled`.
  - `SignalCancel_Twice_IsIdempotent`
  - `KillTree_ChildIgnoringCancelWithGrandchild_KillsBoth` (BI-057): after `KillTreeAsync`, `Exited == -1` and the grandchild PID is gone within 5 s.
  - `KillTree_AlreadyExited_ReturnsWithoutError`
  - `Dispose_WhileRunning_DoesNotKillTheChild`
- [ ] **Step 2: Run** `dotnet test -c Release --project tests/GapActivator.Infrastructure.Tests`. Expected: FAIL.
- [ ] **Step 3: Implement** the types above, following the steps listed.
- [ ] **Step 4: Run** the tests 3 times. Expected: PASS every time, with no orphan `GapActivator.TestChild` left (`Get-Process GapActivator.TestChild -ErrorAction SilentlyContinue` is empty).
- [ ] **Step 5: Commit** `feat(infrastructure): add Windows process launcher with cancel event and job object`

### Task 7: Re-adoption after a restart (TryAttach)

**Files:**
- Modify: `Processes/WindowsProcessLauncher.cs`, `WindowsRunningProcess.cs`
- Test: `tests/GapActivator.Infrastructure.Tests/Processes/AttachTests.cs`

**Interfaces:**
- Produces: `IRunningProcess? TryAttach(RunId runId, int pid, DateTimeOffset processStartTime)`. A "service crash" is simulated by disposing the first launcher's handle, which closes the job and event handles exactly as process death would.

Steps:
1. `OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_TERMINATE, false, pid)`. An invalid handle returns null. `ERROR_ACCESS_DENIED` also returns null and logs a Warning: such a process is not one this service account launched, and throwing would stop every start.
2. If `GetExitCodeProcess` is not `STILL_ACTIVE`, return null.
3. `GetProcessTimes`: when `|creation − processStartTime| > 1 ms`, return null. This is the PID reuse guard (ruling 7).
4. `OpenJobObject(JOB_OBJECT_TERMINATE | JOB_OBJECT_QUERY, false, KernelObjectNames.Job(pid))`. Keep the job only if `IsProcessInJob` is true; otherwise use no job, so the kill falls back to `Kill(entireProcessTree: true)`.
5. Create or open the cancel event **without** resetting it, which creates it if it is missing.
6. Output cannot be re-captured (ruling 7). Append `[GapActivator] The service restarted and re-adopted this process at {now:O}; later output is not captured.\r\n` to the run output file, best effort.
7. Return a `WindowsRunningProcess` with the exit wait and no pump.

`TryAttach` returns null only when no process has the PID, when the start time does not match, or when access is denied (a Warning). Any other failure throws. `ReconcileOnStartup` does not catch it: the exception propagates and stops the service, and SCM recovery restarts it, because an unknown state is never treated as "gone", which could start a duplicate of a live child (Plan 2 ruling T8-9).

- [ ] **Step 1: Write the failing tests** (integration tests):
  - `TryAttach_LiveChildMatchingStartTime_ReportsExitCode` (BI-063, BI-072, carry-over: the handle keeps the exit code readable): launch `sleep 1500 5`, dispose that handle, then `TryAttach` with a second launcher instance. The result is not null, and `Exited == 5`.
  - `TryAttach_StartTimeOffByOneSecond_ReturnsNull` (BI-063)
  - `TryAttach_ProcessGone_ReturnsNull` (BI-071)
  - `TryAttach_ThenSignalCancel_ChildSeesTheEventFromItsLaunch`: launch `honor-cancel`, dispose, attach, signal. The child exits 0 within 5 s.
  - `TryAttach_ThenKillTree_KillsChildAndGrandchild`
  - `TryAttach_AppendsTheReadoptionNoteToTheRunOutput`
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** 3 times. Expected: PASS, with no orphans.
- [ ] **Step 5: Commit** `feat(infrastructure): re-adopt live children by pid and start time`

### Task 8: Run output reader and log retention

**Files:**
- Create: `Files/RunOutputReader.cs`, `Files/FileLogRetention.cs`
- Test: `tests/GapActivator.Infrastructure.Tests/Files/RunOutputReaderTests.cs`, `FileLogRetentionTests.cs`

**Interfaces:**
- Produces: `internal sealed class RunOutputReader(GapActivatorPaths paths) : IRunOutputReader` and `internal sealed class FileLogRetention(GapActivatorPaths paths, TimeProvider time, ILogger<FileLogRetention> logger) : ILogRetention`. Both are singletons.

Rules:
- **`ReadTailAsync(runId, maxBytes)`:**
  - a missing file gives `RunOutput(false, "", false)`;
  - otherwise it opens the file with `FileShare.ReadWrite | FileShare.Delete`;
  - when `length > maxBytes`, it reads the last `maxBytes`, skips leading UTF-8 continuation bytes (`(b & 0xC0) == 0x80`, at most 3) and sets `Truncated = true`;
  - it decodes with `UTF8Encoding(false, throwOnInvalidBytes: false)`.
- **`PurgeOlderThanAsync(days)`:**
  - cutoff = `time.GetUtcNow() − days`;
  - deletes `{logs}\gapactivator-*.log` and `{logs}\runs\*.log` whose `LastWriteTimeUtc < cutoff`, and nothing else;
  - a per-file `IOException` or `UnauthorizedAccessException` is logged at Debug and skipped. Files of running children are open without `FileShare.Delete`, so they are skipped;
  - missing folders are fine;
  - only a cancellation throws.

- [ ] **Step 1: Write the failing tests** (integration tests, temp root):
  - `ReadTail_MissingFile_ReturnsNotExists`
  - `ReadTail_SmallFile_ReturnsAllNotTruncated`
  - `ReadTail_LargerThanMax_ReturnsTailWithoutABrokenCharacter`: a multi-byte `ç` straddles the cut.
  - `ReadTail_FileOpenByWriter_StillReads`
  - `Purge_OldServiceLogsAndRunOutputs_AreDeletedRecentAndUnrelatedKept` (BI-087 and the "retention by age" row): set `LastWriteTimeUtc` with `FakeTimeProvider`. `notes.txt` and `gapactivator.db` are never touched.
  - `Purge_FileOpenByARunningChild_IsSkippedOthersDeleted`
  - `Purge_MissingFolders_DoesNothing`
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(infrastructure): add run output reader and log retention by age`

### Task 9: Service logging (daily file, Event Log, log level)

**Files:**
- Create: `Logging/DailyFileLoggerProvider.cs`, `SafeEventLogLoggerProvider.cs`, `GapActivatorLogging.cs`
- Modify: `Logging/LogLevelSwitch.cs`
- Test: `tests/GapActivator.Infrastructure.Tests/Logging/FileLoggerTests.cs`, `LoggingConfigurationTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class LogLevelSwitch
{ public Microsoft.Extensions.Logging.LogLevel Minimum { get; }                  // default Information
  public void Set(GapActivator.Domain.Settings.LogLevel level); }               // Debug/Information/Warning/Error map 1:1
[ProviderAlias("GapActivatorFile")]
internal sealed class DailyFileLoggerProvider(GapActivatorPaths paths, LogLevelSwitch level, TimeProvider time) : ILoggerProvider
[ProviderAlias("EventLog")]
internal sealed class SafeEventLogLoggerProvider(ILoggerProvider inner) : ILoggerProvider   // every inner call in try/catch, swallowed
public static class GapActivatorLogging
{ public const string EventSource = "GapActivator";
  public static ILoggingBuilder AddGapActivatorLogging(this ILoggingBuilder logging, GapActivatorPaths paths,
      LogLevelSwitch level, TimeProvider time, bool addConsole); }
```

- **File line format:** `yyyy-MM-dd HH:mm:ss.fff zzz [LVL] Category: message`, where `LVL` is `DBG`, `INF`, `WRN`, `ERR` or `CRT` (Trace writes `TRC`). Exception lines follow, indented 4 spaces.
- **File writing:** the day is the local date (`time.LocalTimeZone`). The writer is kept open with `FileShare.ReadWrite | FileShare.Delete` under a lock, rolls over when the day changes, and flushes each line. `IsEnabled(level) = level >= switch.Minimum && level != None`. Any exception while logging is swallowed, and the provider never throws (BI-085). MEL rethrows provider exceptions as an `AggregateException` to the caller, so this is mandatory.
- **`AddGapActivatorLogging`:**
  1. `ClearProviders()` and `SetMinimumLevel(Debug)`;
  2. filters `Microsoft` → Warning, `Microsoft.Hosting.Lifetime` → Information, `Microsoft.EntityFrameworkCore` → Warning;
  3. adds the file provider;
  4. adds `SafeEventLogLoggerProvider` around `EventLogLoggerProvider(new EventLogSettings { SourceName = "GapActivator", LogName = "Application" })`, with the filter `EventLog` → Error (BI-090);
  5. adds the filter `EventLog` + category `GapActivator.Infrastructure.Notifications.LoggingAppNotifier` → None, so failed child runs never reach the Event Log (§9);
  6. adds a simple console only when `addConsole`.

- [ ] **Step 1: Write the failing tests** (integration tests, temp root):
  - `FileLogger_WritesTodaysFileNamedByLocalDateInTheLineFormat` (BI-084): `FakeTimeProvider` with a +09:00 zone at 2030-01-01T20:00Z writes `gapactivator-20300102.log`.
  - `FileLogger_DayChanges_RollsToANewFile`
  - `FileLogger_BelowSwitchLevel_NotWrittenAndSwitchChangeAppliesAtOnce`
  - `FileLogger_FolderUnwritable_DoesNotThrow` (BI-085)
  - `FileLogger_Exception_WritesIndentedStackLines`
  - `SafeEventLog_InnerThrows_IsSwallowed` (BI-085, BI-090)
  - `Logging_EventLogRules_OnlyErrorAndAboveAndNeverAppNotifications` (BI-090): build a `ServiceCollection` with `AddLogging(b => b.AddGapActivatorLogging(...))`, read `IOptions<LoggerFilterOptions>.Value.Rules`, and assert the two `EventLog` rules above.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(infrastructure): add daily file log, safe Event Log and runtime log level`

### Task 10: Infrastructure composition and architecture rules

**Files:**
- Create: `src/GapActivator.Infrastructure/DependencyInjection.cs`
- Modify: `tests/GapActivator.Architecture.Tests/LayeringTests.cs`
- Test: `tests/GapActivator.Infrastructure.Tests/Composition/AddInfrastructureTests.cs`

**Interfaces:**
- Produces: `public static IServiceCollection AddInfrastructure(this IServiceCollection services, InfrastructureOptions options)`. It registers:
  - `AddPersistence(options.Paths)`;
  - the singletons `options`, `options.Email`, `IProcessLauncher` → `WindowsProcessLauncher`, `ISecretProtector` → `DpapiSecretProtector`, `ILogRetention` → `FileLogRetention`, `IRunOutputReader` → `RunOutputReader`, `IEmailSender` → `MailKitEmailSender`, `IAppNotifier` → `LoggingAppNotifier`, and `IOccurrenceCalculator` → `new IcalOccurrenceCalculator(TimeZoneInfo.Local)` (carry-over).

  `IServiceControl` is **not** registered here, because it belongs to the host (Task 11).

- [ ] **Step 1: Write the failing tests**
  - `AddInfrastructure_WithApplication_BuildsWithValidateOnBuildAndValidateScopes`: `AddApplication()`, `AddInfrastructure(temp)` and a `FakeServiceControl`, built with `ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }`. This catches a singleton that captures a scoped service.
  - `AddInfrastructure_LongLivedAdaptersAreSingletons`: the descriptor lifetimes of the seven singletons above (carry-over).
  - `AddInfrastructure_PersistencePortsAreScoped`: `IUnitOfWork`, the three repositories, `IHistoryWriter`, `IRunHistoryReader`, `IHistoryRetention` and `GapActivatorDbContext`.
  - Architecture: `Infrastructure_PackageReferencesAreTheJustifiedOnes`. An XML check of the csproj like `Application_PackageReferencesAreOnlyTheAllowedAbstractions`. The allowed set is `Ical.Net`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`, `MailKit`, `System.Security.Cryptography.ProtectedData` and `Microsoft.Extensions.Logging.EventLog`.
  - Architecture: `Infrastructure_DoesNotDependOnOuterLayers` (existing) stays green.
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(infrastructure): add infrastructure composition and package rule`

### Task 11: Windows Service host

**Files:**
- Create: `src/GapActivator.Service/GapActivator.Service.csproj`:
  - `Microsoft.NET.Sdk`, `OutputType` Exe, `AssemblyName` `GapActivator.Service`, `net10.0-windows`;
  - references Application and Infrastructure, plus the package `Microsoft.Extensions.Hosting.WindowsServices`;
  - listed in the slnx under `/src/`.

  Also create `Program.cs`, `GapActivatorHost.cs`, `GapActivatorWorker.cs`, `GapActivatorServiceLifetime.cs`, `ServiceTimeouts.cs`, `ExitCodeSink.cs`, `HostRestart.cs`, `ScopedServiceControl.cs`.
- Create: `tests/GapActivator.Service.Tests/GapActivator.Service.Tests.csproj` (the shape of the Infrastructure tests, referencing Service, Infrastructure, Testing and TestChild; in the slnx), `ServiceControlTests.cs`, `HostConfigurationTests.cs`, `WorkerTests.cs`
- Modify: `tests/GapActivator.Architecture.Tests/LayeringTests.cs` (`OnlyServiceReferencesHostingWindowsServices`)

**Interfaces:**
- Consumes: `AddApplication`, `AddInfrastructure`, `AddGapActivatorLogging`, `DatabaseInitializer`, `SchedulerLoop.RunAsync` / `Reconciled`.
- Produces:

```csharp
public static class ServiceTimeouts
{ public static readonly TimeSpan Shutdown = ExecutionLimits.MaxGracePeriod + TimeSpan.FromSeconds(15) + TimeSpan.FromSeconds(30); }
public static class ServiceExitCodes { public const int Normal = 0, Fatal = 1, Restart = 3; }
public sealed class ExitCodeSink(Action<int> write) { public void Set(int code); public int? Last { get; } }   // host: code => Environment.ExitCode = code
public sealed class HostRestart(IHostApplicationLifetime lifetime, ExitCodeSink exitCode)     // singleton
{ public void Begin(); }   // once only: exitCode.Set(Restart); lifetime.StopApplication()
internal sealed class ScopedServiceControl(HostRestart restart) : IServiceControl, IAsyncDisposable   // scoped
// RestartAsync: marks the request, returns Task.CompletedTask at once. DisposeAsync: when marked, restart.Begin().
// The dispatcher disposes the command scope after it publishes the events, so the stop begins only then (addition c).
public static class GapActivatorHost
{ public static HostApplicationBuilder CreateBuilder(string[] args);
  public static IHost Build(string[] args) => CreateBuilder(args).Build(); }
internal sealed class GapActivatorWorker(IServiceScopeFactory scopes, SchedulerLoop loop, ExitCodeSink exitCode,
    ILogger<GapActivatorWorker> logger) : BackgroundService
internal sealed class GapActivatorServiceLifetime : WindowsServiceLifetime
// OnStop(): ExitCode = Environment.ExitCode; try RequestAdditionalTime((int)ServiceTimeouts.Shutdown.TotalMilliseconds) catch
// InvalidOperationException; base.OnStop().
```

`GapActivatorHost.CreateBuilder` does, in order:
1. `Host.CreateApplicationBuilder(args)`.
2. `paths`: `configuration["GapActivator:DataRoot"]`, or `GapActivatorPaths.Default` when it is absent.
3. `AddWindowsService(o => o.ServiceName = "GapActivator")`. When `WindowsServiceHelpers.IsWindowsService()`, replace `IHostLifetime` with `GapActivatorServiceLifetime`.
4. `Configure<HostOptions>`: `ShutdownTimeout = ServiceTimeouts.Shutdown` and `BackgroundServiceExceptionBehavior = StopHost` (addition a, BI-075).
5. `Logging.AddGapActivatorLogging(paths, switch, TimeProvider.System, addConsole: !IsWindowsService())`.
6. `AddApplication()`, then `AddInfrastructure(new InfrastructureOptions(paths))`.
7. The singletons `ExitCodeSink(code => Environment.ExitCode = code)` and `HostRestart`; the scoped `IServiceControl` → `ScopedServiceControl`; the hosted `GapActivatorWorker`.

`Program.cs` is `await GapActivatorHost.Build(args).RunAsync();`.

**GapActivatorWorker.ExecuteAsync**:
1. `await Task.Yield()`, so SCM start is not held up.
2. In a scope, `DatabaseInitializer.InitializeAsync`.
3. `await loop.RunAsync(stoppingToken)` (ruling 13).

Errors:
- An `OperationCanceledException` from the stopping token ends quietly.
- Any other exception is logged at Critical, sets `ServiceExitCodes.Fatal`, and is rethrown, so the host stops and SCM recovery restarts the service.

Plan 4 awaits `loop.Reconciled` before it opens the pipe.

- [ ] **Step 1: Write the failing tests**
  - `Restart_StopBeginsOnlyAfterEventsArePublished` (BI-077, addition c). Use an `ApplicationTestHost` whose `configure` registers `HostRestart` with a recording `IHostApplicationLifetime`, a recording `ExitCodeSink`, the scoped `ScopedServiceControl`, and an extra `INotificationHandler` that records whether `StopApplication` had already been called when the `GapActivatorRestarted` event arrived. After `Send(new RestartGapActivator())` returns:
    - the handler saw "not stopped";
    - `StopApplication` was called exactly once;
    - the exit code is 3.
  - `Restart_RestartAsync_ReturnsAtOnceWithoutStopping`: a direct call in a scope that is not yet disposed. The task is completed, and there is no stop yet.
  - `Restart_TwoRequests_StopOnce`
  - `HostOptions_ShutdownTimeoutCoversTheLargestGracePeriodPlus15Seconds` (BI-075, addition a): build with a temp root and read `IOptions<HostOptions>`. The timeout is at least `ExecutionLimits.MaxGracePeriod + 15 s`, and also at least the installation's maximum default grace period (10 min). `ServiceTimeouts.Shutdown == TimeSpan.FromSeconds(645)`.
  - `Host_DataRootFromCommandLine_IsUsed`: `--GapActivator:DataRoot=<temp>` makes the resolved `GapActivatorPaths.DataRoot` equal `<temp>`.
  - `Host_BuildsWithValidateOnBuild`
  - `Worker_DataRootIsAFile_LogsCriticalSetsExitCode1AndStopsHost` (integration)
  - `Worker_Started_InitializesDatabaseThenReconcilesBeforeTheFirstCycle` (integration): after start, `gapactivator.db` exists and `loop.Reconciled` completes within 10 s.
  - Architecture: `OnlyServiceReferencesHostingWindowsServices`
- [ ] **Step 2: Run** `dotnet test -c Release --project tests/GapActivator.Service.Tests`. Expected: build FAILS.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(service): add Windows Service host with graceful shutdown and restart`

### Task 12: Sample child and child contract

**Files:**
- Create: `samples/GapActivator.SampleChild/GapActivator.SampleChild.csproj` (Exe, `net10.0-windows`, in the slnx under a new `/samples/` folder), `Program.cs`, `docs/child-contract.md`, `docs/running-the-service.md`
- Modify: the spec, §5 item 3 and §7 last bullet. Record the ruling 4 decision: no `GapActivator.Child` package in Plan 3; `samples/GapActivator.SampleChild` plus `docs/child-contract.md`; a package may come later without breaking anything. Also write the exact env var formats, the event name and the job object.
- Test: `tests/GapActivator.Service.Tests/SampleChildTests.cs`

**Interfaces:**
- Produces: `GapActivator.SampleChild.exe [seconds]`, which works for `seconds` (default 5) and honours the cancel event. Its whole program, the copy the contract doc shows:

```csharp
using System.Globalization;

int seconds = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 5;
string? parent = Environment.GetEnvironmentVariable("GAPACTIVATOR_PARENT_PID");
Console.WriteLine(parent is null ? "Running on its own." : $"Started by GapActivator (PID {parent}).");

// GapActivator creates this event before the child runs; outside GapActivator it does not exist.
EventWaitHandle.TryOpenExisting($@"Local\GapActivator.Cancel.{Environment.ProcessId}", out EventWaitHandle? cancel);
using (cancel)
{
    for (int i = 1; i <= seconds; i++)
    {
        if (cancel?.WaitOne(TimeSpan.FromSeconds(1)) ?? WaitOneSecond())
        {
            Console.WriteLine("Cancellation requested; stopping cleanly.");
            return 0;
        }

        Console.WriteLine($"Working... {i}/{seconds}");
    }
}

Console.WriteLine("Done.");
return 0;

static bool WaitOneSecond()
{
    Thread.Sleep(1000);
    return false;
}
```

`docs/child-contract.md` sections, using exactly the values in Global Constraints:
- **Exit code.**
- **Launch.** Session 0, no window, the service account, stdin = `NUL`, arguments verbatim, and the working directory default (the executable's folder). `.cmd`/`.bat` run through `cmd.exe /d /s /c`.
- **Environment.** The service's environment, then the registration's variables, which override it ignoring case, then the two `GAPACTIVATOR_*` variables. The prefix is reserved.
- **Parent identity.** The format, and how to compare it with `Process.GetProcessById(pid).StartTime.ToUniversalTime()`, which matches exactly. A child may stop when GapActivator is gone, but it does not need to: a restarted GapActivator re-adopts live children.
- **Cancellation.** The event name, manual reset, and that it exists before the first instruction. The grace period (per process, default 30 s). Then the job-object kill of the whole tree. The exit code after a cancel does not matter.
- **Output.** stdout and stderr go to one file in write order. Write UTF-8. The 50 MiB cap and its marker. Output is visible to viewers, so never print secrets.
- **After a GapActivator crash.** Live children are re-adopted by PID and start time, and their later output is not captured; writes to the broken pipe fail, and .NET ignores them. A child that started between its creation and the first commit is not seen, and keeps running unsupervised: the "crash window". The run ends Abandoned.
- **Children never touch GapActivator's database.**

`docs/running-the-service.md`: console mode (`GapActivator.Service.exe --GapActivator:DataRoot=C:\Temp\GapActivatorDev`), the exit codes, and a developer-only `sc.exe create` / `sc.exe failure GapActivator reset= 86400 actions= restart/60000/restart/60000/restart/60000` / `sc.exe failureflag GapActivator 1` sequence. It notes that the installer (Plan 5) does this, and that a requested restart takes effect after the SCM's 60 s recovery delay.

- [ ] **Step 1: Write the failing tests** (integration tests; the Service tests reference the sample project):
  - `SampleChild_OnItsOwn_RunsAndExitsZero`: started with `Process.Start` and `1`, it exits 0 with `Running on its own.`
  - `SampleChild_UnderTheLauncher_StopsCleanlyOnCancel`: `WindowsProcessLauncher` with `60`. After the first `Working...` line, `SignalCancel` makes it exit 0 within 3 s, and the output has `Started by GapActivator (PID {Environment.ProcessId}).` and `Cancellation requested; stopping cleanly.` (BI-057, BI-060)
- [ ] **Step 2: Run.** Expected: FAIL.
- [ ] **Step 3: Implement** the sample and write both documents and the spec edits.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(samples): add sample child and child process contract`

### Task 13: End to end

**Files:**
- Create: `tests/GapActivator.Service.Tests/Support/E2EFixture.cs`, `EndToEndTests.cs`, `ServiceExeSmokeTests.cs`

**Interfaces:**
- Consumes: `GapActivatorHost.CreateBuilder`, `AddApplication`, `AddInfrastructure`, the commands and queries from Plan 2, and the sample and test children.
- Produces: `E2EFixture` (one per test class):
  1. `TempDataRoot`.
  2. **Seed** before the host starts. Use a separate provider (`AddApplication` + `AddInfrastructure` + `FakeServiceControl`, real time) that runs `DatabaseInitializer` and then:
     - `UpdateSettings` with `CycleInterval = 5 s` (the domain minimum), defaults elsewhere, and e-mail disabled;
     - `RegisterProcess "Sample scheduled"`, which runs the sample with `1` on `OneTime(now + 3 s)`;
     - `RegisterProcess "Sample long"`, the sample with `60` and no schedule;
     - `RegisterProcess "Stubborn"`, the TestChild with `ignore-cancel`, `TimeLimit = 2 s` and `GracePeriod = 1 s`.

     Then dispose it.
  3. Start `GapActivatorHost.Build(["--GapActivator:DataRoot=" + root])` in-process.
  4. Helpers: `WaitForRunAsync(processName, predicate, timeout)` (polls `ListRuns`/`GetRun` every 200 ms) and `Send`/`Query` through the host's dispatchers as `Caller.System`.

  Commands come from the dispatcher, not a pipe, because the pipe arrives in Plan 4.

- [ ] **Step 1: Write the failing tests** (integration tests; this is done-when ruling 17):
  - `Scheduled_SampleRun_SucceedsWithCapturedOutput`: within 30 s, the `Sample scheduled` run has `Outcome == Succeeded`, `Trigger == Schedule` and `ExitCode == 0`. `GetRun(...).Output.Text` contains `Done.` and `Started by GapActivator (PID {Environment.ProcessId}).`
  - `RunNow_ThenCancel_EndsCancelledThroughTheEventWithoutKill` (BI-057): `RunNow(Sample long)`, wait for `Running`, then `CancelRun`. Within 10 s, the outcome is `Cancelled`, `Killed == false`, and the output contains `Cancellation requested; stopping cleanly.`
  - `TimeLimit_ChildIgnoringCancel_EndsTimedOutKilledAfterGrace` (BI-057): `RunNow(Stubborn)`. Within 15 s, the outcome is `TimedOut`, `Killed == true`, and `EndedAt − StartedAt >= 3 s`.
  - `StopHost_ActiveRun_EndsInterruptedAndNoChildLeft` (BI-074, BI-075): `RunNow(Sample long)`, wait for `Running`, then `host.StopAsync()`. A new seeding provider shows the run `Interrupted`, and its PID is gone.
  - `ServiceExe_ConsoleMode_RunsTheScheduledSample` (`ServiceExeSmokeTests`): seed as above, then start `GapActivator.Service.exe --GapActivator:DataRoot=<root>` with `Process.Start`. Poll the database through a seeding provider until `Sample scheduled` succeeds (60 s). `gapactivator-yyyyMMdd.log` exists and contains `Application started`. Kill the exe tree in `finally`.
- [ ] **Step 2: Run** `dotnet test -c Release --project tests/GapActivator.Service.Tests`. Expected: FAIL until the fixture exists, then PASS. Any failure here is a real defect in Tasks 2–12: fix it in the owning file with its own test.
- [ ] **Step 3: Run** the whole suite 3 times. Expected: PASS every time; total time for the integration tests under 3 minutes.
- [ ] **Step 4: Commit** `test(service): add end-to-end runs of the sample child through the host`

### Task 14: Inventory coverage, carry-over and final verification

**Files:**
- Modify: `docs/behavior-inventory.md`:
  - the *Covered by* cell of every Plan-3 row: BI-042, 045, 057, 060, 063, 075, 077, 084, 085, 087, 090, 091, 098, and the "Log retention is by age" row;
  - replace the "(persistence: Plan 3)" text in BI-076 and Q-1, and "(launching in that folder: Plan 3)" in Q-5, with test names.
- Modify (by the controller, outside the repo): the owner's carry-over memory note (outside the repo). Drop the Plan 3 items this plan closed and add the notes below.

**Notes for Plan 4:**
- Await `SchedulerLoop.Reconciled` before the pipe server accepts commands.
- Replace the `IAppNotifier` registration (`LoggingAppNotifier`) with the pipe broadcast, and drop its Event Log exclusion filter.
- Map `DbUpdateConcurrencyException` to an IPC error, as a retryable conflict.
- The Windows identity → `Caller` mapping lives here (ruling 3).

**Notes for Plan 5:**
- The installer must set `sc.exe failureflag GapActivator 1`, so a stop with exit code 3 (restart) or 1 (fatal) triggers the "restart after 60 s" recovery.
- The installer creates the event source `GapActivator`.
- ACLs on `%ProgramData%\GapActivator`: SYSTEM, Administrators and the service account.
- Publish self-contained for `win-x64` and `win-arm64`. The SQLite native library comes from the bundle.

- [ ] **Step 1: Fill the *Covered by* cells.** Every Plan-3 row cites at least one existing test (`ClassName.MethodName`). Use the tests named in Tasks 2–13 with the `// BI-…` comments.
- [ ] **Step 2: Verify the citations**

Run: `dotnet test -c Release -- --list-tests > TestResults/tests.txt`. Every name cited in the inventory must appear in that list. Check this with a script in the job's temp folder, not in the repository.
Expected: zero missing names.

- [ ] **Step 3: Full verification**

Run: `dotnet tool restore; dotnet format --verify-no-changes; dotnet build -c Release; dotnet test -c Release; dotnet publish src/GapActivator.Service -c Release -r win-x64 --self-contained -o TestResults/publish`
Expected: all green, and `TestResults/publish/GapActivator.Service.exe` plus `e_sqlite3.dll` exist. Then `Get-Process GapActivator.TestChild,GapActivator.SampleChild -ErrorAction SilentlyContinue` is empty.

- [ ] **Step 4: Sanitization check**

Run the sanitization pattern the controller gives at execution time with `git grep -n -i -E "<pattern>"` and `git log -p feat/plan-2-application.. | grep -i -E "<pattern>"`.
Expected: no matches.

- [ ] **Step 5: Commit** `docs: cite infrastructure and service tests in the behavior inventory`

---

## Decisions the plan made

1. **DateTimeOffset is stored as INTEGER UTC ticks.** Why: SQLite cannot compare or sort EF's TEXT DateTimeOffset, and the start-time match needs exact ticks; the time-zone calculator ignores offsets anyway. Cost if wrong: values come back as UTC, so a future feature that needs the original offset would need another column.
2. **Enums are stored as TEXT names.** Why: readable data, and a reordered enum cannot corrupt rows. Cost if wrong: a little more space; renaming a member needs a data migration.
3. **Complex properties for LaunchOptions and ExecutionLimits; JSON columns for EnvironmentVariables and EmailAlertSettings.** Why: they are immutable value objects whose constructors validate, and the collections do not deserve their own tables. Cost if wrong: if EF cannot bind a constructor, switch to `OwnsOne` with the same columns, which only changes the configuration file.
4. **Shadow `NameKey` and `ExecutableKey` columns, `ToUpperInvariant()`, with a unique index on `NameKey`.** Why: SQLite `NOCASE` only folds ASCII, while the domain compares OrdinalIgnoreCase (Review Focus 5). Cost if wrong: one migration to change the normalization.
5. **A unique filtered index for one active run per process.** Why: the anti-goal "two instances never overlap" is then guaranteed by the database too, not only by memory. Cost if wrong: a future feature that allows overlap must drop the index.
6. **A shadow `Version` concurrency token on Process, Run and Settings; `DbUpdateConcurrencyException` propagates untranslated.** Why: BI-042 needs a guard, and the application already retries or rejects generic failures. Cost if wrong: Plan 4 adds a mapping (noted).
7. **A shadow `Ordinal` on history entries.** Why: GUID v7 ids are not monotonic within one millisecond, and entries of one command share an instant. Cost if wrong: same-instant entries from different commits may tie arbitrarily.
8. **The settings row is seeded by `DatabaseInitializer`, not by the migration or the repository.** Why: a repository that lazily inserts the row could race the ungated `SendTestEmail`. Cost if wrong: none beyond moving five lines.
9. **`IProcessRepository.ListIdsAsync` is added; the per-process last-completed-run read is declined.** Why: the first protects the whole cycle from one corrupt row (BI-039). The second is only an optimization, and the correlated query is already one row per process. Cost if wrong: one more port method later.
10. **`net10.0-windows` for Infrastructure, Service, the children and their tests.** Why: DPAPI, the Event Log and named kernel objects are Windows-only, and CA1416 is an error under warnings-as-errors; the spec is Windows-only (§2). Cost if wrong: none while the product is Windows-only.
11. **Children are created suspended through `CreateProcessW`, not `System.Diagnostics.Process`.** Why: the event name needs the PID, and ruling 5 wants the event and the job to exist before the child runs. Cost if wrong: about 12 P/Invokes to maintain.
12. **The output pipe is an overlapped named pipe (server) plus an inheritable client handle; child handles are inherited inside a static lock.** Why: it gives truly cancellable async reads, so a grandchild holding stdout cannot pin a thread, and the lock is the same approach .NET's `Process.Start` uses. Cost if wrong: switch to `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` if something else in the service ever creates inheritable handles.
13. **stdout and stderr share one pipe; no stderr prefix; bytes are copied verbatim; the reader decodes UTF-8.** Why: this keeps the child's real write order, like a console, and the child's encoding cannot be known. Cost if wrong: stderr cannot be told apart in the app; adding a second pipe later is local to the launcher.
14. **The run output cap is 50 MiB, with a marker, and the pipe keeps draining.** Why: the spec is silent, and a runaway child must not fill the disk or block (Review Focus 3). Cost if wrong: a constant in `InfrastructureOptions`.
15. **The job is named `Local\GapActivator.Job.{pid}.{startTimeUtcTicks}`, so a re-adopted child is still killed as a tree; the fallback is `Process.Kill(entireProcessTree: true)`.** Why: an unnamed job dies with the crashed service's handle table. Cost if wrong: if Windows does not keep the name alive, the fallback misses grandchildren whose parent already exited. Known limitation: after a service crash, the kill falls back to a parent-link tree walk, so descendants whose parent already exited are not reached; the spec lists durable job ownership across service crashes as future work.
16. **The kill exit code is `uint.MaxValue` (reported as -1), and the kill wait is bounded at 10 s, then `TimeoutException`.** Why: it matches the fake, and it keeps `KillTreeAsync` from hanging. Cost if wrong: cosmetic.
17. **`.cmd` and `.bat` run through `cmd.exe /d /s /c`.** Why: `CreateProcess` does not officially support batch files, and users will register scripts (Review Focus 1). Cost if wrong: one branch in `CommandLineBuilder`.
18. **The child environment is the service's environment, then the registration's variables overriding it ignoring case, then `GAPACTIVATOR_*`.** Why: this matches how Windows treats names; a child without `SystemRoot` or `PATH` breaks. Cost if wrong: secret values from the service's own environment are visible to children; documented.
19. **`TryAttach` treats access denied as "no match" and logs a Warning.** Why: throwing would fail every reconciliation, and so every start; such a process cannot be one this account launched. Cost if wrong: after a change of service account, a still-running old child is recorded Abandoned and can be duplicated.
20. **A pre-existing cancel event is reset on launch but not on attach.** Why: a stale event from a reused PID must not cancel a new child, while a re-adopted child keeps its pending signal. Cost if wrong: none known.
21. **Adopted runs get a note line in their output file.** Why: the app would otherwise show output that silently stops. Cost if wrong: one line per re-adoption.
22. **Log retention goes by `LastWriteTimeUtc`; files open by a running child are skipped through share modes.** Why: a quiet, long-running Service-kind child must not lose its file. Cost if wrong: an idle open file is never purged until the child ends.
23. **The service log file is dated by local time and uses the `[LVL]` line format; the Event Log is wrapped so it never throws; `LoggingAppNotifier` is excluded from the Event Log.** Why: ruling 2 logs app notifications at their own severity, while §9 keeps the Event Log for severe service errors, and MEL rethrows provider exceptions to callers (BI-085). Cost if wrong: filter tweaks only.
24. **The runtime log level follows the database: `EfUnitOfWork` updates `LogLevelSwitch` after a commit that changed the settings.** Why: no domain event carries it, and UpdateSettings must apply without a restart. Cost if wrong: a few lines in the unit of work.
25. **DPAPI uses machine scope with a fixed entropy and the prefix `dpapi:v1:`.** Why: §3 requires machine scope, and the version prefix allows a future change of scheme. Cost if wrong: secrets must be re-entered after a scheme change without a migration path.
26. **SMTP authenticates whenever a username is set (empty password if none); 30 s per operation and 90 s in total.** Why: addition b; some relays accept user-only auth. Cost if wrong: one condition.
27. **Restart: a scoped `IServiceControl` starts the stop when the command scope is disposed, with exit code 3; Plan 5 must set `failureflag`.** Why: addition c ("after the current command has published its events") without changing the dispatcher, and a virtual account cannot restart itself through the SCM without an installer-granted right. Cost if wrong: it relies on the dispatcher disposing the scope after publishing (pinned by `Restart_StopBeginsOnlyAfterEventsArePublished`).
28. **`ServiceTimeouts.Shutdown` = 10 min 45 s; `GapActivatorServiceLifetime` requests that wait hint.** Why: addition a, with a 30 s margin over the loop's own bound. Cost if wrong: at machine shutdown Windows may still cut it short, and reconciliation covers that.
29. **`GapActivator:DataRoot` must be fully qualified, and startup fails otherwise.** Why: a relative path in a service resolves under `System32`. Cost if wrong: none.
30. **A separate `tests/GapActivator.TestChild` drives launcher tests; the sample stays minimal (`[seconds]`) and does not exit when its parent dies.** Why: ruling 4 keeps the sample to the ~20 lines a child needs, while tests need many modes; re-adoption makes exiting on parent death unnecessary. Cost if wrong: one more project.
31. **The end-to-end test runs the host in-process, after seeding the database in a separate provider, plus one smoke test of the real `GapActivator.Service.exe`.** Why: there is no pipe yet to send commands, and the 5 s cycle minimum needs settings that exist before the first wait. Cost if wrong: about 1 minute of CI time.
32. **stdin is `NUL`.** Why: there is no console in session 0 (BI-045). Cost if wrong: none.
33. **MailKit 4.18.1 and ProtectedData 10.0.0 are the pins; System.Security.Cryptography.Pkcs is pinned to 10.0.14.** Why: MailKit 4.13.0 was flagged by the package audit, so the newest stable 4.x that passed was used; Pkcs 10.0.0, a transitive dependency of MailKit, was audit-flagged too, so it is pinned as a transitive security pin. Cost if wrong: one version bump.
34. **`RecordRunLaunched` carries one `LaunchedAt` instant, used as both `StartedAt` and the base of the time-limit deadline.** Why: a run must never be recorded shorter than its time limit. Cost if wrong: none known.
35. **The grace period and the kill-retry wait are measured with the monotonic `TimeProvider` timestamp, not the wall clock.** Why: a clock step must not lengthen or cut the grace period, and a timer firing one tick early must not cut it short. Cost if wrong: none known.
36. **A failure while the host is being built (such as an invalid configuration), or a `host.RunAsync` failure before the worker owns the exit code, is reported best-effort to the Event Log, and the service exits with code 1.** Why: an unhandled crash leaves no diagnosable trace. Cost if wrong: none.
