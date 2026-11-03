# GapActivator — Design

Date: 2026-11-03
Status: reviewed, awaiting owner approval

## 1. Intent

GapActivator is a self-contained Windows application that runs scheduled programs in the background, supervises them, and keeps a full history of what happened. Anyone can download it, install it, and register their first process from a desktop app, without setting up a database server, Docker, or SQL scripts.

It is the open-source successor of a private process activator (referred to here as **the legacy activator**) that originated from proprietary work. GapActivator is a **new, independent codebase**: the legacy activator is used only as a behavioral reference. No code, names, schema, comments, test data, or git history are carried over.

GapActivator is a sibling of GapMouse (https://github.com/Gustavo-Piccoli/GapMouse) and follows its engineering conventions (section 12).

### Why

- The legacy activator only works inside the environment it was built for: a shared SQL Server, processes registered by hand-written SQL, a corporate SMTP relay, a hard-coded time zone, a child exit-code contract that contradicts OS conventions, and Portuguese identifiers.
- The owner wants a robust, polished product that strangers can install and trust, and a codebase that is a model of Clean Architecture, DDD, SOLID, and CQRS.

### Success looks like

- A user downloads the installer, installs, opens the app, registers a program with a friendly schedule editor, and sees its runs (with captured output) in the history — in minutes, with no other software installed.
- The service survives crashes, reboots, misbehaving children, and concurrent commands without losing or corrupting state.
- The repository can be made public with no trace of the original company.

## 2. Goals, non-goals, anti-goals

### Goals

- Clean Architecture with the Dependency Rule enforced by architecture tests.
- A rich domain: aggregates, value objects, explicit state machines, domain events; no framework dependencies in the domain.
- CQRS in the application layer: every use case is a Command or a Query with its own handler.
- Preserve the legacy activator's valuable behavior (scheduling, retries, blocking after repeated failures, graceful cancellation, long-running "service" processes, logs, alerts), redesigned where noted.
- Explicit handling of every edge case listed in section 6.
- English everywhere: code, identifiers, comments, commit messages, documentation. The desktop UI is localized (English and Brazilian Portuguese).
- Maintainability, testability, and clarity over premature performance work.

### Non-goals (for now)

- Managing several machines from one place (see Future work).
- Running individual processes under different Windows accounts (see Future work).
- Cross-platform support. GapActivator is Windows-only (Windows Service, named events, named pipes, DPAPI).
- Backward compatibility with the legacy activator (schema, configuration, child contract). The owner explicitly allowed a free redesign.
- Migration tooling from the legacy database.

### Anti-goals (failure even if it technically works)

- Any company name, system acronym, internal URL, e-mail, connection string, realistic personal data, or corporate rule left in code, docs, tests, or history.
- A default configuration that lets a non-admin user run arbitrary programs with elevated rights.
- Children that must know about GapActivator's database to behave correctly.
- Two instances of the same process registration running at the same time.
- Code ported line by line from the legacy activator when a cleaner design exists.

## 3. Constraints

- .NET 10, C# 14, Windows 10/11 and Windows Server, x64 and ARM64.
- New libraries need a stated reason. Libraries already justified:
  - **Microsoft.EntityFrameworkCore.Sqlite**: embedded relational storage with migrations.
  - **Ical.Net**: RFC 5545 RRULE expansion (used by the legacy activator; proven).
  - **MailKit**: modern SMTP with TLS and authentication (`System.Net.Mail` is not recommended by Microsoft for new code).
  - **Avalonia**: desktop UI, same as GapMouse.
  - **Microsoft.Extensions.Hosting.WindowsServices**: Windows Service host.
  - Test stack: same as GapMouse (xUnit on Microsoft.Testing.Platform).
  - No mediator library. Handlers are resolved and invoked directly through small interfaces (`ICommandHandler<TCommand, TResult>`, `IQueryHandler<TQuery, TResult>`) and a thin dispatcher written in the project. MediatR is avoided because its current licensing does not fit a small MIT project, and the indirection is not needed.
- Native AOT: required for the desktop app if Avalonia + the IPC client allow it; **not** required for the service, because EF Core does not support AOT maturely. The service is published self-contained (no .NET install needed).
- No secrets in the repository. SMTP credentials are stored encrypted with DPAPI (machine scope) in the database; nothing sensitive in config files.

## 4. Approaches considered

### 4.1 How to build it

| Approach | Trade-off |
|---|---|
| A. Refactor the legacy repository in place, step by step | Almost nothing survives the redesign, so most steps would translate code that is about to be replaced. Git history keeps the proprietary import; rewriting history is risky and never fully reassuring. |
| **B. New repository, built from scratch in sequential plans, using the legacy activator as a behavioral spec (chosen)** | Clean history from the first commit; the design is born DDD/CQRS. Costs discipline to not lose subtle rules — solved by a behavior inventory (section 13) where every preserved rule becomes a new test. |
| C. New repository, porting code module by module | Faster start, but copies the old design and its origin markers, which the owner explicitly ruled out. |

The owner chose **B**.

### 4.2 Storage

| Option | Trade-off |
|---|---|
| **SQLite, embedded (chosen)** | Real SQL with transactions in a single file, no server, EF Core provider. The most widely used embedded database. |
| LiteDB | Document store; not SQL; smaller community. |
| SQL Server LocalDB | Needs its own installer; not embedded. |
| JSON/YAML files | Fine for settings, poor for run history and concurrent access. |

The owner chose a **single edition with SQLite** (no separate "with database" edition). A distributed edition with a central database is future work.

### 4.3 How the desktop app changes data

| Option | Trade-off |
|---|---|
| A. App writes SQLite directly, elevated | Simple, but UAC prompts on every change, two writers, commands only take effect on the next cycle. |
| **B. Only the service touches the database; the app is a client over an ACL-protected named pipe (chosen)** | Single writer, immediate commands, live state, one place for authorization, natural fit for CQRS. More work: a versioned message protocol. The app needs the service running. |
| C. Hybrid: app reads the DB, writes through the pipe | Complexity of both; the schema becomes a public contract. |

The owner chose **B**.

### 4.4 Service account

| Option | Trade-off |
|---|---|
| LocalSystem | Full machine control; whoever edits the process list owns the PC. Rejected as a default. |
| LocalService / NetworkService | Minimal rights; most useful processes hit "access denied"; NetworkService is shared with other services. |
| **Virtual account `NT SERVICE\GapActivator` (chosen default)** | Microsoft's recommended service identity: no password, least privilege, unique so folders can be granted to it alone. Users must grant it access to the folders their processes use. |
| **Specific account (chosen as installer option)** | Behaves "like when I run it by hand"; needs the password stored by the SCM; a Windows password change breaks the service until updated. |
| Per-process accounts | Most flexible, much more complex and a larger attack surface. Future work. |

## 5. Product overview

Two executables plus an optional helper package:

1. **GapActivator Service** (`GapActivator.Service.exe`): a Windows Service. It owns the SQLite database, runs the scheduler, launches and supervises child processes, records history, writes logs, sends alerts, and serves the named pipe.
2. **GapActivator app** (`GapActivator.exe`): an Avalonia desktop app with a tray icon. It is a pure client of the service.
3. **`GapActivator.Child`** (optional NuGet package, decided later in Plan 3): implements the child side of the contract (section 7) in a few lines.

One installation manages the processes of the local machine only. The legacy concept of several activators sharing one database (activator table, activator id on processes, activator name in config, configurable table prefix) is removed: "activator" becomes the **installation settings**.

## 6. Domain model

### 6.1 Bounded contexts

The product is small; contexts are modules inside the same projects, not separate services.

| Context | Responsibility |
|---|---|
| **Process Catalog** | Process registrations: what to run, how, and when. |
| **Scheduling** | Schedules and next-occurrence calculation. |
| **Execution** | Runs: launching, supervising, cancelling, outcomes, failure counting, blocking. |
| **History & Logging** | Run history and run events (replaces the legacy "monitoring" tables), local log files. |
| **Notifications** | Reactions to domain events: Windows notifications through the app, optional e-mail. |
| **Settings** | Installation-wide settings. |

The legacy "monitoring" subsystem (monitored systems, per-system recipients, severity rows) is **dropped**. Everything it recorded becomes run events, and alerts become reactions to domain events.

### 6.2 Process (aggregate root)

Fields (indicative; final names are the builder's call):

- Id, Name (unique), Description.
- Kind: **RunToCompletion** or **Service** (long-running, must stay up while GapActivator runs; relaunched when it exits).
- Executable path, arguments, working directory, environment variables.
- Schedules (0..n, owned by the aggregate; see 6.4). A Service-kind process has no schedule; it runs whenever enabled.
- Time limit (optional, per process; falls back to the installation default; none = unlimited). **Owner decision:** time limits never apply to Service-kind processes; a Service-kind registration with a per-process time limit is rejected.
- Working directory default (owner decision): when not set, the executable's folder.
- Cancellation grace period (per process; falls back to the installation default, 30 s).
- Lifecycle state: **Enabled**, **Disabled** (manual pause, new), **Blocked** (failure limit reached).
- Consecutive failure count.
- Audit timestamps (created/updated). The legacy numeric "control user" columns are dropped.

Rules:

- A registration whose executable path is already used by another registration is **allowed with a warning** shown when saving.
- Blocking happens when the consecutive failure count reaches the installation's failure limit; blocking resets the counter (legacy behavior).
- Unblocking is manual and returns the process to Enabled with counter = 0.
- Legacy columns with no behavior (process type number, system acronym, server name) are not carried over.

### 6.3 Run (aggregate root)

One Run per execution attempt. Non-terminal states:

- **Launching** (new): the scheduler decided to start it; the executable is not confirmed running yet.
- **Running**: PID and process start time recorded.
- **Stopping** (new): cancellation was requested or the time limit expired; the cancel event was signalled and GapActivator waits for the grace period, then kills the process tree.

Terminal outcomes (exactly one, recorded with reason, exit code, duration, and whether a kill was needed):

| Outcome | Meaning | Counts as failure |
|---|---|---|
| Succeeded | exit code 0 | no (resets counter) |
| Failed | exit code ≠ 0 | yes |
| FailedToStart (new) | executable missing, access denied, launch error | yes |
| TimedOut | time limit expired | yes |
| Cancelled | requested by the user | no |
| Interrupted (new) | GapActivator stopped or restarted during the run | no |
| Abandoned (new) | GapActivator died during the run and the process is gone when it comes back | yes (legacy: orphan counted as failure) |
| Skipped (new) | the schedule came due while a previous run was still active | no |

Service-kind processes use the same states plus:

- **Crashed**: the process exited on its own. Counts as failure; relaunched on the next cycle unless the failure limit blocks it.
- **Stopped**: stopped on request; not relaunched.

Service-kind rules:

- A Service-kind process that exits on its own is **Crashed** regardless of exit code (it is supposed to stay up).
- Its failure counter resets to 0 whenever a cycle finds it running (legacy behavior), so an occasional crash never accumulates into a block; only consecutive crashes do.
- Right after GapActivator starts, a Service-kind process found not running is relaunched **without** counting a failure until the first full cycle completes (legacy behavior). This rule wins over **Abandoned** for Service-kind runs; Abandoned applies to RunToCompletion runs only.
- "Stop" on a Service-kind process **disables** the registration; it stays stopped across GapActivator and machine restarts until enabled again. Enabling starts it.

Disable and delete (builder's call, confirmed in design review):

- Disabling a RunToCompletion process lets the current run finish and prevents new runs; the app offers "Disable and cancel the current run".
- Deleting a process with an active run is rejected; the app offers "Cancel and delete".
- Deleting a process keeps its history; runs store a snapshot of the process name.

**Failure counter (owner decision):** only **Succeeded** resets the counter (and, for Service-kind processes, being found running; see below). Cancelled, Interrupted, and Skipped leave it **unchanged** — neither incremented nor reset. This deliberately changes the legacy behavior, where a user cancellation reset the counter and could hide a process that keeps failing.

### 6.4 Schedules (value objects)

- **OneTime(at)**: runs once; afterwards it is kept with the date it was fulfilled (replaces the legacy "single executed" *type*, which was really a state).
- **Recurring(rrule, anchor)**: an RFC 5545 RRULE expanded with Ical.Net from the anchor.
- The legacy "simple vs. complex" cyclic distinction is removed: the schedule editor always produces explicit rules (with hour and minute where relevant), so there is no ambiguity about which time fields the rule governs.
- Next run of a process = the earliest next occurrence among its schedules.
- Time zone: the machine's local time zone (legacy hard-coded a specific zone; removed).
- Missed occurrences (machine off, service stopped): run **once**, then jump to the next future occurrence (legacy behavior). The run is flagged **late** in history.
- An exhausted recurrence (COUNT reached, UNTIL passed) simply finishes: no next occurrence, no error.
- A manual run (run now) consumes the occurrences that are due at its start, like a scheduled run (owner decision).
- Occurrences that came due while a process was **Disabled** or **Blocked** are **discarded** when it is enabled or unblocked: no catch-up run; the process waits for its next future occurrence (owner decision). "Unblock and run" still runs at once because the run is manual.
- A new recurring schedule ignores occurrences before the moment it is saved: its first run is the first occurrence at or after save time, and COUNT still counts from the anchor (owner decision).
- A new or changed OneTime schedule whose instant is not in the future is rejected when saving (owner decision).
- Occurrences that came due while the installation was **paused** are discarded on Resume: no catch-up run. Pending retries stay pending (owner decision).
- Time-zone semantics: rules expand on the zone's wall clock; a nonexistent local time (spring forward) uses the pre-gap offset; an ambiguous local time (fall back) resolves to its first pass, so sub-daily rules run the repeated hour once.
- RRULE frequency SECONDLY is rejected; MINUTELY and coarser are accepted; UNTIL has no artificial year range (owner decision).
- **Known legacy bug, not to be reproduced:** an occurrence exactly equal to the reference instant was neither advanced nor considered (`<` in one place, `>` in the other), so the schedule could silently drop out. GapActivator must have a test for the equality case.

### 6.5 Settings (installation)

Cycle interval, failure limit, default time limit, default cancellation grace period, log retention days (log files and per-run output files), history retention days (run records and run events in the database; default 90, builder's call), log level, optional e-mail alerts (enabled flag, SMTP host, port, TLS mode, username, encrypted password, sender, recipients, which events alert), update check on/off.

The legacy "simplified log" flag (it only changed a log header) is replaced by the log level.

**Pause (owner decision):** the installation can be **paused**. While paused, no new run starts — not by schedule, retry, run now, or Service-kind relaunch. Runs already active finish normally (controller's call; the owner may change it). The paused flag is persisted and survives restarts. Log retention is by age in days (files older than N days are deleted), replacing the legacy "keep the N newest folders".

### 6.6 Domain events

At least: RunStarted, RunCompleted (with outcome), RunLate, RunSkipped, ProcessBlocked, ProcessUnblocked, ServiceProcessCrashed, InstallationPaused, InstallationResumed, CycleFailed, GapActivatorStarted, GapActivatorRestarted. Events are persisted as **run events** (history) and dispatched to notification handlers after the transaction commits.

## 7. Child process contract

Any executable works without knowing the contract; the contract only adds graceful cancellation.

- **Exit code**: 0 = success, anything else = failure. Outcomes such as Cancelled or TimedOut are decided by GapActivator (it knows what it requested and when), never by exit code. The legacy codes (1 = success, 2/3 overlapping for cancellation) are dropped; analysis of the legacy children showed the 2-vs-3 distinction carried no reliable meaning.
- **Launch**: configurable arguments, working directory, and environment variables per process; no window (session 0).
- **Parent identity**: env var `GAPACTIVATOR_PARENT_PID` plus `GAPACTIVATOR_PARENT_START_TIME` (so a child can detect PID reuse when checking whether GapActivator is alive). Exact format is the builder's call, documented in the contract doc.
- **Cancellation**: GapActivator **creates** a manual-reset named event `Local\GapActivator.Cancel.{childPid}` (exact name builder's call, documented) before or immediately after launch, so a cancel requested early is never lost. It signals the event and waits for the per-process grace period, then kills the entire process tree.
- **Output**: stdout and stderr are captured to the run's output file and are visible in the app. Children never need to write to GapActivator's database.
- **Children never touch GapActivator's database.** (Legacy children polled the activator's tables to detect cancellation; that coupling is removed.)
- A sample child process and a contract document ship in the repository.

## 8. Application layer (CQRS)

- Commands (examples): RegisterProcess, UpdateProcess, DeleteProcess, EnableProcess, DisableProcess, UnblockProcess, RunNow, CancelRun, RestartRun ("cancel current and start a new one"), UpdateSettings, SendTestEmail, RunCycle (internal), ReconcileOnStartup (internal).
- Queries (examples): GetDashboard, ListProcesses, GetProcess, ListRuns (filters), GetRun (events + output), GetSettings, PreviewSchedule (next N occurrences).
- Ports: process launcher/supervisor, clock (`TimeProvider`), repositories, unit of work, run-output store, log writer, notifier (e-mail, app notification), secret protector (DPAPI), service control.
- The same handlers serve the scheduler loop and the IPC endpoint.

### Run-now while running

The app asks: "The process is already running. Do you want to cancel the current run and start a new one?" On yes, the current run is cancelled gracefully (kill after the grace period) and recorded as **Cancelled**; the new run starts as soon as the old one has actually ended. Two runs of the same registration never overlap. For Service-kind processes the same dialog acts as "restart". On no, nothing happens.

### Other edge cases

| Situation | Behavior |
|---|---|
| Run now on a blocked process | "Unblock and run" action. |
| Run now on a disabled process | Allowed for RunToCompletion; rejected for Service kind (enable it to start) (owner decision). |
| Cancel during Launching | Abort the launch; if the executable already started, signal it immediately. |
| Cancel with nothing running | Command rejected: nothing to cancel. |
| GapActivator stops or restarts during runs | Graceful cancel → **Interrupted**, not a failure. |
| GapActivator crashes or power loss | On start, for each non-terminal run: if PID + start time still match a live process, **re-adopt** and keep supervising; otherwise **Abandoned** (failure). |
| Schedule due while still running | **Skipped**, recorded. |
| Executable missing | **FailedToStart**, failure, clear message. |
| Same executable in two registrations | Allowed with a warning at save time; supervision is per PID, so registrations never confuse each other. |
| Failure | Retry on the **next cycle** (not the next scheduled occurrence) until the failure limit, then block (legacy behavior). The pending retry belongs to the process, not to a schedule: a OneTime schedule is fulfilled when its occurrence is consumed, and a failed run is retried through the process's pending retry. A daily job that fails therefore runs again on the next cycle. Configurable backoff is future work. |

Legacy behavior preserved: a long-running run must never block the scheduling of other processes; a failure in one process never stops the cycle; an unexpected cycle failure is logged and alerted, never crashes the service; log retention runs at most once per calendar day and never prevents the cycle.

## 9. Infrastructure and service

- SQLite via EF Core with migrations, database at `%ProgramData%\GapActivator\gapactivator.db`. ACL: SYSTEM, Administrators, and the service account only.
- Process launcher on `System.Diagnostics.Process` + Win32 where needed (named events, process start time, tree kill).
- Logs at `%ProgramData%\GapActivator\logs\`: one daily service log and one output file per run, both with configurable retention. Windows Event Log only for severe service errors (event source `GapActivator`).
- E-mail via MailKit, password protected with DPAPI.
- Restart of GapActivator (command from the app) goes through the Windows Service Control Manager.

## 10. Desktop app

- Avalonia, GapMouse look and conventions (tokens, light/dark themes, English and Brazilian Portuguese with live switching, single instance, tray icon colored by state, Windows notifications).
- Screens: **Dashboard** (service state, active runs, upcoming runs, recent failures), **Processes** (list and editor with friendly schedule editor + advanced raw RRULE with next-5 preview; actions run now, cancel, enable/disable, unblock), **History** (runs with filters; detail with events, exit code, duration, captured output), **Settings** (cycle interval, failure limit, default time limit, grace period, log retention, log level, optional e-mail with test button, theme, language, update check), **About** (version, update check via GitHub Releases, paths).
- Friendly schedule editor: every N minutes/hours; daily at HH:MM; weekly on selected weekdays at HH:MM; monthly on day X at HH:MM; once at a date/time.
- When the service is stopped, the app shows it and offers to start it.

### Authorization

- Any interactive user of the machine can **view** state and history.
- **Changes** require membership in Administrators or a local group **GapActivator Operators**. The installer adds the installing user to that group.
- **Owner decision:** when the service runs under a **specific account** (installer option), the Operators group is **disabled** and only Administrators can change anything. Reason: whoever can change registrations can run any program as the service account; with a specific (possibly admin) account, Operators membership would be a privilege escalation. With the virtual account the Operators group is enabled. The installer and the app's Settings/About explain which mode is active.
- Enforced by the named pipe ACL and re-checked server side per command.
- Viewers do not see process arguments or environment variable values; only users who can change see them. Environment variables can be marked **secret**: secret values are stored encrypted with DPAPI and are never returned in full by queries. Captured run output is visible to viewers (documented, so users know not to print secrets).

## 11. Installer and distribution

- Inno Setup, **per machine** (admin required to register a service), x64 and ARM64.
- Installs the service and the app; registers the service with automatic (delayed) start and recovery actions "restart after 60 s" on every failure with the failure count reset after 1 day (owner decision); creates the event source, the Operators group, data folders, and ACLs.
- Account page: virtual account (default) or a specific account (pre-filled with the current user; grants "Log on as a service").
- **On the final page**: "Create a desktop shortcut" and "Create a Start Menu shortcut" (both checked), and "Launch GapActivator now".
- Uninstaller removes the service and shortcuts; keeps data unless the user opts to delete it.
- No portable build (a Windows Service cannot run "loose").
- Release pipeline, checksums, release-please as in GapMouse.

## 12. Engineering conventions (from GapMouse)

- Projects: `GapActivator.Domain`, `GapActivator.Application`, `GapActivator.Infrastructure`, `GapActivator.Service`, `GapActivator.App`; tests per layer plus `GapActivator.Architecture.Tests` and a shared testing library.
- `.slnx`, `global.json`, `Directory.Build.props` (warnings as errors), `Directory.Packages.props`, `.editorconfig`, `dotnet format` in CI.
- ADRs in `docs/adr/`; specs and plans in `docs/superpowers/`.
- Conventional Commits, no AI attribution trailers.
- CI: format, build, tests with coverage, AOT publish of the app where applicable, CodeQL, Dependabot.
- MIT license; README, CONTRIBUTING, CODE_OF_CONDUCT, SECURITY.

## 13. Behavior inventory from the legacy activator

As the **first task of Plan 1** (most rules land in the domain), the builder writes an inventory of the legacy activator's rules (from its code and its unit tests (216 unit and 36 integration tests)), each marked **keep**, **redesign** (with the new rule), or **drop**, and turns every kept/redesigned rule into a test in the new codebase. The inventory lives in the new repo in neutral terms (no legacy names). The legacy repository is private and read-only for this work; its location is given to the builder outside this document, and nothing from it is copied. Known items are already captured in sections 6–9.

Known legacy defects, flagged rather than reproduced:

1. The schedule equality-instant bug (6.4).
2. The per-process time limit is stored but **never applied**; only the installation default is used. GapActivator applies the per-process limit with fallback to the default.
3. A hard-coded time zone (6.4).

## 14. Delivery plan

Each plan ends with the solution building, all tests and CI green, and waits for owner approval before the next.

| # | Plan | Done when |
|---|---|---|
| 1 | Foundation and Domain: behavior inventory, repo, solution, build props, CI, architecture tests, full domain | Domain fully tested, CI green |
| 2 | Application (CQRS): commands/queries/handlers, ports, cycle orchestration, tested with fakes | All activator behavior tested without DB or OS |
| 3 | Infrastructure and Service: SQLite + migrations, real launcher, logs, MailKit, DPAPI, Windows Service, sample child (and decision on `GapActivator.Child`) | Service runs end to end with the sample; integration tests |
| 4 | IPC and desktop app: pipe protocol and ACL, all screens, tray, notifications, localization | Everything operable from the UI |
| 5 | Distribution and open source: installer, release pipeline, docs with layer diagram, OSS files, sanitization report, future-improvements list | Installer tested; repo ready to publish |

## 15. Sanitization

- Nothing is copied from the legacy repository; it is only read.
- The final report (Plan 5) lists everything checked and anything needing manual review: git history (clean by construction), config files, CI files, dependencies, sample data, screenshots.
- Every value that could be an endpoint, ID, credential, path, or personal data in samples and tests is synthetic and obviously fake.

## 16. Left to the builder

Exact identifiers and file layout inside each project; EF Core mapping details; IPC message format and versioning scheme (JSON over named pipe); exact env var and event name formats (documented in the contract doc); internal scheduler loop design (must preserve "a long run never blocks other processes"); polling intervals; UI layout details within the GapMouse design language; test naming.

## 17. Future work (not in this refactor)

- **Distributed edition** with a central database managing several machines.
- Per-process run-as accounts.
- Generic webhook notifications (Slack, Teams, Discord).
- Additional database providers through the existing repository ports.
- Configurable retry backoff.
- Code signing and winget, as in GapMouse.
