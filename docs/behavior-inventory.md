# Behavior inventory of the legacy activator

This document lists the observable rules of the private predecessor (the **legacy activator**, see the design, section 13), read from its source code and its unit tests. Each rule is written in neutral English domain language and gets a verdict:

- **keep**: GapActivator preserves the rule as stated.
- **redesign**: GapActivator preserves the intent with a different rule (column *New rule*).
- **drop**: GapActivator does not carry the rule over.

Every **keep** and **redesign** row becomes at least one test in the new codebase. Tests reference the row ID in a comment (`// BI-012`).

Column *Spec §* points to the section of the design (`docs/superpowers/specs/2026-11-03-gapactivator-design.md`) that decides the row. Rules the design did not originally decide were put to the owner; the column then names the owner decision (Q-n), listed in the final section. Column *Plan* says where the rule lands: 1 = domain rule, 2 = application/orchestration, 3 = infrastructure/service, 4 = IPC/app, 5 = installer/distribution.

Vocabulary used for the legacy behavior: the legacy activator kept one *status* per process (Waiting, Running, Run-now requested, Cancel requested, Blocked), one *installation state* (Paused, Normal, Restarting), and a *run record* per execution attempt. Operators changed statuses and states by writing directly to the database.

## Process Catalog

| ID | Rule (neutral English) | Verdict | New rule | Spec § | Plan |
|---|---|---|---|---|---|
| BI-001 | A process requires a non-empty name; surrounding whitespace is trimmed. | redesign | Name is required, trimmed, and unique across registrations, compared case-insensitively. | §6.2; owner decision Q-8 | 1 |
| BI-002 | A process requires a non-empty description; surrounding whitespace is trimmed. | redesign | Description is optional (trimmed when present). | owner decision Q-7 | 1 |
| BI-003 | The executable path is required and normalized on load: environment variables expanded, one pair of surrounding double quotes removed, resolved to an absolute path. | keep | | §6.2 | 1 |
| BI-004 | A process is either run-to-completion or a long-running service-kind process (a boolean flag). | redesign | Kind is an explicit value: RunToCompletion or Service. | §6.2 | 1 |
| BI-005 | A process has a per-process time limit in whole hours (0 = no own limit, negative rejected), but the value is stored and never used. | redesign | Time limit is optional per process; see BI-047 for how it is applied. | §6.2, §13 (defect 2) | 1 |
| BI-006 | The consecutive failure counter is never negative. | keep | | §6.2 | 1 |
| BI-007 | A process carries a single status (Waiting, Running, Run-now requested, Cancel requested, Blocked) that mixes lifecycle, run state and operator requests; any value outside this set is rejected. | redesign | Process lifecycle is Enabled / Disabled / Blocked; run states and outcomes live in the Run aggregate; run-now and cancel are commands, not stored statuses. | §6.2, §6.3, §8 | 1 |
| BI-008 | Blocked processes are excluded from every cycle (never read for dispatch). | keep | | §6.2 | 2 |
| BI-009 | Unblocking is done outside the product (editing the stored status). | redesign | UnblockProcess command returns the process to Enabled with counter = 0; "Unblock and run" when running a blocked process. | §6.2, §8 | 1 |
| BI-010 | Two registrations pointing to the same executable are detected only at run time (more than one live OS process matching name and path); an alert is raised once per anomaly (same set of process IDs, order ignored, separate variant when the instances were being stopped), forgotten when the duplication disappears; the duplicates are never killed by an automatic cycle. | redesign | Same executable in two registrations is allowed with a warning at save time; supervision is per process ID, so registrations never confuse each other; no runtime duplicate alert. | §6.2, §8 | 2 |
| BI-011 | Each process belongs to one of several activators sharing a database; a process type number, a system acronym, a server name and numeric audit-user columns are stored without behavior. | drop | | §5, §6.2 | — |
| BI-012 | Processes are launched without arguments and without per-process environment variables. | redesign | Arguments, working directory and environment variables are configurable per process; environment variables can be marked secret. | §6.2, §7, §10 | 1 |

## Scheduling

| ID | Rule (neutral English) | Verdict | New rule | Spec § | Plan |
|---|---|---|---|---|---|
| BI-013 | Schedule kinds: one-time not yet executed, one-time executed, cyclic "simple", cyclic "complex". | redesign | OneTime(at), kept with the date it was fulfilled; Recurring(rrule, anchor). | §6.4 | 1 |
| BI-014 | A cyclic schedule requires a recurrence rule; a one-time schedule does not. | keep | | §6.4 | 1 |
| BI-015 | A "complex" cyclic schedule zeroes the hour, minute and second of its start that the rule does not set (BYHOUR, BYMINUTE, BYSECOND); a "simple" one keeps the start time of day. | drop | The schedule editor always produces explicit rules; no simple/complex distinction. | §6.4 | — |
| BI-016 | A recurring rule is expanded from the schedule's latest stored next occurrence (a moving start), so COUNT is counted from that moving start and a COUNT of 2 or more never exhausts. | redesign | The rule is expanded from a fixed anchor; COUNT and UNTIL are evaluated against that anchor. | §6.4 | 1 |
| BI-017 | The next occurrence after an instant is the first occurrence strictly after that instant. | keep | | §6.4 | 1 |
| BI-018 | An occurrence exactly equal to the reference instant is neither advanced (strictly-before test) nor considered as upcoming (strictly-after test), so the schedule can silently drop out. | redesign | An occurrence equal to the reference instant is due; there is a test for the equality case. | §6.4, §13 (defect 1) | 1 |
| BI-019 | Occurrences are computed in a hard-coded time zone. | redesign | Occurrences are computed in the machine's local time zone. | §6.4, §13 (defect 3) | 1 |
| BI-020 | The next run of a process is the earliest upcoming occurrence among all its active schedules; executed one-time schedules are ignored. | keep | | §6.4 | 1 |
| BI-021 | A process with no schedule (or no upcoming occurrence) never runs automatically; it runs only when a run is requested manually. | keep | | §6.2 (0..n schedules) | 1 |
| BI-022 | A process is due when its next run is at or before the cycle instant. | keep | | §6.4 | 1 |
| BI-023 | Missed occurrences (machine off, service stopped) cause a single run, after which the schedule jumps to the first occurrence after the cycle instant. | redesign | Same rule, and the run is flagged **late** in history (RunLate event). | §6.4, §6.6 | 1 |
| BI-024 | A one-time schedule whose date is before the reference instant is marked executed and never considered again. | redesign | A OneTime schedule is fulfilled when its occurrence is consumed (its run starts), and keeps the fulfillment date. | §6.4, §8 | 1 |
| BI-025 | A recurring schedule's stored next occurrence only moves forward; setting an equal or earlier value is rejected. | keep | | §6.4 | 1 |
| BI-026 | When a run starts, the occurrences due at the cycle instant are consumed (schedules advanced past that instant) before the child is launched. | keep | | §6.4, §8 | 1 |
| BI-027 | Occurrences that come due while a run is still active are silently lost: on completion the schedules are advanced past the run's end. | redesign | An occurrence that comes due while a previous run of the same process is active produces a **Skipped** run, recorded in history. | §6.3, §8 | 1 |
| BI-028 | After a successful (or cancelled) run, the stored next run is replaced by the recomputed one only if it is missing, already in the past, or later than the recomputed one (an externally set earlier date wins). | redesign | The next run is always derived: earliest upcoming schedule occurrence, or the pending retry when one exists (BI-052); there is no separately edited next date. | §6.4, §8 | 1 |
| BI-029 | A recurring rule with no further occurrence (COUNT reached, UNTIL passed) raises an error every time the next occurrence is computed; the error ends up counted as run failures and can block the process. | redesign | An exhausted recurring schedule is finished: it yields no upcoming occurrence and raises no error. | §6.4 (owner decision Q-2) | 1 |
| BI-030 | A manual run request also consumes the occurrences due at its start, like a scheduled run. | keep | | §6.4 (owner decision Q-4) | 1 |
| BI-031 | A service-kind process ignores schedules; it is supervised every cycle. | keep | | §6.2 | 1 |
| BI-032 | Recurrence rule text is trimmed and upper-cased; two rules are equal ignoring case; an empty rule means "no rule". | keep | | §6.4 | 1 |
| BI-033 | Rule structure: parts separated by `;` (empty parts ignored), each part `KEY=VALUE` with a non-empty key and value; FREQ is mandatory; only FREQ, INTERVAL, WKST, BYSECOND, BYMINUTE, BYHOUR, BYDAY, BYMONTHDAY, BYYEARDAY, BYWEEKNO, BYMONTH, BYSETPOS, COUNT, UNTIL are accepted; a key may appear once. | keep | | §6.4 | 1 |
| BI-034 | Rule values: FREQ is one of SECONDLY, MINUTELY, HOURLY, DAILY, WEEKLY, MONTHLY, YEARLY; INTERVAL and COUNT are positive integers; BYDAY is a weekday code optionally preceded by an ordinal in ±1..53; BYMONTH 1..12; BYMONTHDAY ±1..31; BYYEARDAY ±1..366; BYWEEKNO ±1..53; BYSETPOS ±1..366; BYSECOND 0..60; BYMINUTE 0..59; BYHOUR 0..23; WKST is a weekday code. | redesign | Same value rules, except that FREQ=SECONDLY is rejected (MINUTELY and coarser are accepted). | §6.4 (owner decision Q-6) | 1 |
| BI-035 | UNTIL is a DATE (`YYYYMMDD`) or a UTC DATE-TIME (`YYYYMMDDTHHMMSSZ`); a date-time without `Z` is rejected; UNTIL must lie between 1900-01-01 and 2100-12-31. | redesign | Same format rule; UNTIL has no year range. | §6.4 (owner decision Q-6) | 1 |
| BI-036 | Cross-key rules: UNTIL and COUNT are mutually exclusive; BYWEEKNO only with FREQ=YEARLY; BYYEARDAY not with DAILY, WEEKLY or MONTHLY; BYMONTHDAY not with WEEKLY; BYDAY with an ordinal only with MONTHLY or YEARLY. | keep | | §6.4 | 1 |

## Execution

| ID | Rule (neutral English) | Verdict | New rule | Spec § | Plan |
|---|---|---|---|---|---|
| BI-037 | A cycle starts every configured interval; the interval is re-read before each wait, so a change applies without a restart. | keep | | §6.5 | 2 |
| BI-038 | A new cycle starts on time even when runs from previous cycles are still active; a long run never delays the scheduling of other processes. | keep | Loop design is the builder's call; only the guarantee is kept. | §8, §16 | 2 |
| BI-039 | All processes of a cycle are handled concurrently and independently. Legacy defect: an unexpected error in one process's handling cancels the whole dispatch, cancelling the other processes' active runs (later counted as failures) and skipping the ones not started. | redesign | A failure in one process never stops, cancels or skips the handling of the others. | §8 | 2 |
| BI-040 | An unexpected cycle failure is logged and alerted, never crashes the service; a shutdown cancellation ends the cycle silently, without an alert. | keep | Alert goes through the CycleFailed event (BI-093). | §6.6, §8 | 2 |
| BI-041 | Two runs of the same process never overlap: a run is first reserved in memory, then claimed with an atomic compare-and-set on the stored status; a process already owned by another cycle is skipped and an informational line is logged. | keep | | §2 (anti-goals), §8 | 2 |
| BI-042 | Stored state changes are guarded: a writer that finds the state changed since it read it gives up without overwriting (blocking, completing a run, reverting a cancel request). | keep | Optimistic concurrency in persistence. | §16 (builder's call) | 3 |
| BI-043 | If saving a run's results fails after the status change, the process is put back to Running so the next cycle's reconciliation resolves it. | redesign | Each outcome is saved in a single transaction (unit of work); no compensation step. | §8 (ports: unit of work) | 2 |
| BI-044 | Before handling, an enabled process whose counter already reached the failure limit is blocked without running, unless a manual run was requested for it, which runs anyway. | keep | Only reachable when the failure limit is lowered below a process's current counter. | owner decision Q-3 | 2 |
| BI-045 | Children are launched with no window, no shell, standard input not redirected, the executable's folder as working directory, and no arguments. | redesign | No window (session 0); arguments, working directory and environment variables from the registration; default working directory = the executable's folder. | §6.2 (owner decision Q-5), §7 | 3 |
| BI-046 | A missing executable or a launch error is logged and written to the run record, but the failure is counted only on the next cycle, through orphan reconciliation. | redesign | The run ends as **FailedToStart** (a failure) immediately, with a clear message. | §6.3, §8 | 1 |
| BI-047 | Only the installation-wide time limit (whole hours, 0 = none) is applied; the per-process limit is ignored. | redesign | Effective limit = the process's limit if set, otherwise the installation default; if neither is set, unlimited. | §6.2, §13 (defect 2) | 1 |
| BI-048 | When the time limit expires, the child is cancelled gracefully; the run is not completed in that cycle and is counted as a failure on the next cycle, through orphan reconciliation. | redesign | The run ends as **TimedOut** (a failure) as soon as the child is gone. | §6.3 | 1 |
| BI-049 | Exit codes: 1 = success, 2 = error, 3 = the child cancelled itself after noticing (by reading the database) that its status changed, 0 and 4–6 = failure variants; any other code raises an error and the run is resolved later as an orphan. | redesign | Exit code 0 = Succeeded, any other code = Failed; Cancelled and TimedOut are decided by GapActivator, never by exit code. | §6.3, §7 | 1 |
| BI-050 | A failure increments the consecutive failure counter; when the counter reaches the failure limit (counter ≥ limit) the process is blocked. | keep | | §6.2, §6.3 | 1 |
| BI-051 | Blocking resets the failure counter to 0. | keep | | §6.2 | 1 |
| BI-052 | A failure that does not block schedules a retry on the next cycle (the next run is set to the current cycle instant), not on the next scheduled occurrence. | keep | The pending retry belongs to the process, not to a schedule; a OneTime schedule is fulfilled anyway. | §8 | 1 |
| BI-053 | A success resets the failure counter to 0. | keep | | §6.3 | 1 |
| BI-054 | A cancellation (operator stop, or a child exiting with the "cancelled" code) resets the failure counter to 0, never counts as a failure, never blocks. | redesign | Cancelled, Interrupted and Skipped leave the counter unchanged: neither incremented nor reset. | §6.3 (owner decision) | 1 |
| BI-055 | After a cancellation the next run follows the schedules (no immediate retry). | keep | | §6.3, §8 | 1 |
| BI-056 | An operator cancels a run by writing "Cancel requested" in the database; the cycle that owns the run reads the status every 15 seconds and then cancels the child. Shutdown cancellation and operator cancellation are kept apart, so an operator cancel never aborts other processes. | redesign | CancelRun command over the named pipe, applied immediately; the run enters **Stopping**; cancelling one run never affects other runs. | §4.3, §6.3, §8 | 2 |
| BI-057 | Graceful cancel: the parent signals a named event that the child is expected to create, retrying every 250 ms while the child has not created it yet; if the child exits within the grace period no kill is needed, otherwise the whole process tree is killed; the grace period is a fixed 30 seconds. | redesign | GapActivator creates the manual-reset event before or right after launch (an early cancel is never lost), signals it, waits the per-process grace period (installation default 30 s), then kills the whole process tree; the run records whether a kill was needed. | §6.2, §6.3, §7 | 3 |
| BI-058 | A "Cancel requested" process with nothing running is reverted to Waiting without touching any date (only when no run is owned in memory and no matching executable is live). | redesign | Cancel with nothing running is rejected: nothing to cancel. | §8 | 2 |
| BI-059 | Children read the activator's database to detect cancellation and exit with the "cancelled" code. | drop | Children never touch GapActivator's database. | §7 | — |
| BI-060 | Every child receives the parent's process ID in an environment variable, so it can stop if the parent dies. | redesign | Children receive the parent's process ID and process start time (to detect PID reuse); exact format documented in the contract doc. | §7 | 3 |
| BI-061 | A run requested manually ignores the schedule. | keep | | §8 | 2 |
| BI-062 | A manual run request on a process whose run is still active waits: the request stays pending until the active run ends, and that run's results (counter, dates) are discarded because its guarded completion finds the status changed. | redesign | The app asks to cancel the current run and start a new one; on yes, the current run ends Cancelled and the new one starts once it has actually ended; on no, nothing happens. For a service-kind process the same dialog acts as restart. | §8 | 2 |
| BI-063 | Service-kind health check: each cycle looks for a live OS process whose executable name and full image path match the registration (not the database). | redesign | Supervision is per process ID plus process start time, recorded at launch. | §6.3, §8 | 3 |
| BI-064 | A service-kind process found running is marked Running (never from Cancel requested or Blocked) and its failure counter is reset to 0. | keep | | §6.3 | 1 |
| BI-065 | A service-kind process that was Running and is now gone counts one failure, whatever its exit code, and is relaunched; at the failure limit it is blocked without relaunch. | redesign | The run ends **Crashed** (a failure, whatever the exit code); the process is relaunched on the next cycle unless the failure limit blocks it. | §6.3 | 1 |
| BI-066 | In the first cycle after GapActivator starts, a service-kind process found not running is relaunched without counting a failure; the first cycle counts as complete only when it ends without an unexpected error. | keep | This rule wins over Abandoned for service-kind runs. | §6.3 | 1 |
| BI-067 | A service-kind process that was never started (or not marked Running) is launched without counting a failure. | keep | | §6.3 | 1 |
| BI-068 | Service-kind processes are launched fire-and-forget; their exit code is never read, and a crash is noticed only by absence on the next cycle. | redesign | The supervisor observes the exit (Crashed) and the relaunch happens on the next cycle. | §6.3 | 2 |
| BI-069 | Stopping a service-kind process cancels every matching live instance gracefully; it is never relaunched and stays stopped across cycles. | redesign | "Stop" disables the registration; it stays stopped across GapActivator and machine restarts until enabled; enabling starts it. | §6.3 | 1 |
| BI-070 | While another cycle is relaunching a service-kind process, the process is not checked against the OS, no crash is counted and it is not stopped yet. | keep | | §6.3, §8 | 2 |
| BI-071 | A process marked Running that no cycle of this instance owns, with no matching live executable, is an orphan: its open run record is closed as cancelled and the run is completed as a failure (counter +1, block at the limit, otherwise retry next cycle); the run's start time is kept. | redesign | On startup, a non-terminal run whose process ID and start time no longer match a live process ends **Abandoned** (a failure) for RunToCompletion; a service-kind process is relaunched without counting (BI-066). | §6.3, §8 | 2 |
| BI-072 | A process marked Running that no cycle owns, but whose executable is found live by name and path, is left alone (logged, next run re-evaluated). | redesign | On startup, a non-terminal run whose process ID and start time match a live process is **re-adopted** and supervised again. | §8 | 2 |
| BI-073 | Orphan reconciliation runs on every cycle, not only at startup. | redesign | ReconcileOnStartup runs once when GapActivator starts; during normal operation the supervisor knows its own runs. | §8 | 2 |
| BI-074 | When the service stops or restarts, active children are cancelled gracefully but their runs stay open and are counted as failures (orphans) after the restart. | redesign | Runs cancelled because GapActivator stops or restarts end **Interrupted**, not a failure. | §6.3, §8 | 2 |
| BI-075 | On restart, the host waits for active cycles to finish (grace period + 15 s at most) before the process exits. | redesign | On stop or restart, GapActivator waits for active runs to finish cancelling (grace period plus a margin) before exiting. | §8, §9 | 3 |
| BI-076 | Installation state read every cycle: Paused = the cycle does nothing (no dispatch, no log, no retention); Normal = run the cycle; any unknown state is an error. | redesign | The installation can be paused; the paused flag is persisted and survives restarts. While paused, no new run starts: not by schedule, retry, run now, or Service-kind relaunch. Runs already active finish normally. | §6.5 (owner decision Q-1) | 1 |
| BI-077 | Restart is requested by writing "Restarting" in the database; the next cycle logs a warning, runs log retention, writes Normal back before sending a notice e-mail (an e-mail failure is ignored), then exits with code 1 so the service recovery actions start it again. | redesign | Restart is a command from the app that goes through the Service Control Manager; GapActivatorRestarted is raised and notified; a notification failure never prevents the restart. | §6.6, §9 | 3 |
| BI-078 | Log retention runs as part of a restart, and a retention failure aborts the restart. | drop | Retention belongs to the cycle only (BI-086); restart is a plain service restart. | §8, §9 | — |

## History & Logging

| ID | Rule (neutral English) | Verdict | New rule | Spec § | Plan |
|---|---|---|---|---|---|
| BI-079 | A run record is opened when a run starts (result "running", start time set) and closed with result, message and end time; a record in the "running" result needs a start time. | redesign | Run aggregate with states Launching / Running / Stopping and one terminal outcome recorded with reason, exit code, duration and whether a kill was needed; the run stores a snapshot of the process name. | §6.3 | 1 |
| BI-080 | A run record left open by a run that never finished is closed as "cancelled" when the next run starts or when reconciliation finds it, while the process counts a failure for it. | redesign | Such a run ends **Abandoned** (a failure), consistently in history and in the counter. | §6.3 | 1 |
| BI-081 | Blocking writes a dedicated "blocked" history record (start = end = now) and a warning. | redesign | ProcessBlocked is a domain event persisted as a run event. | §6.6 | 1 |
| BI-082 | Outcome messages: success has a fixed text; a cancellation keeps the given reason (or a neutral text) and is never worded as a failure; a failure includes the error detail. | keep | | §6.3 | 1 |
| BI-083 | The run end time is the instant GapActivator observed the child's end. | keep | | §6.3 | 1 |
| BI-084 | One log file per cycle (in a folder per day) with start and end lines, and one log file per run with only the parent's start and outcome lines; the child's output is not captured. | redesign | One daily service log; one output file per run capturing the child's stdout and stderr, visible in the app. | §7, §9 | 3 |
| BI-085 | Writing a log line is best effort: a failure to write never fails the run or the cycle. | keep | | §8, §9 | 3 |
| BI-086 | Log retention runs at most once per calendar day, at the start of a cycle and before dispatch; concurrent attempts for the same day run it once; a failed or cancelled attempt frees the day for a later retry; a retention failure never prevents the dispatch. | keep | | §8 | 2 |
| BI-087 | Retention keeps the N most recent date-named folders of the cycle logs (N = retention days, counted as folders, not as age); per-run log files are never purged. | redesign | Log files and per-run output files older than the retention days are deleted (by age). | §6.5 | 3 |
| BI-088 | Retention refuses to delete anything when the cycle-log folder and the per-run log folder are the same or contain each other. | drop | Fixed folder layout under `%ProgramData%\GapActivator\logs\`. | §9 | — |
| BI-089 | Run records in the database are kept forever. | redesign | Run records and run events older than the history retention days (default 90) are deleted. | §6.5 | 2 |
| BI-090 | Warnings and above (blocks, failed runs, restarts) go to the Windows Event Log. | redesign | The Event Log receives only severe service errors (event source `GapActivator`). | §9 | 3 |
| BI-091 | Log folders are configurable per installation, normalized like executable paths, and created when the settings are read. | redesign | Fixed layout under `%ProgramData%\GapActivator\`, created by the installer and the service. | §9 | 3 |
| BI-092 | A "simplified log" flag changes only a log header. | drop | Replaced by the log level. | §6.5 | — |

## Notifications

| ID | Rule (neutral English) | Verdict | New rule | Spec § | Plan |
|---|---|---|---|---|---|
| BI-093 | An unexpected cycle failure sends an e-mail with the error message, with the cycle log attached. | redesign | CycleFailed event → notification (Windows notification through the app; e-mail when enabled for this event). | §6.6 | 2 |
| BI-094 | A restart sends a notice e-mail. | redesign | GapActivatorRestarted event → notification. | §6.6 | 2 |
| BI-095 | Blocking raises a "critical" monitoring event and e-mails the recipients registered for the process. | redesign | ProcessBlocked event → notification to the recipients in the settings. | §6.1, §6.6 | 2 |
| BI-096 | A notification failure never undoes or prevents the action that caused it (the block is saved first; the restart goes on); only a shutdown cancellation propagates. | redesign | Notifications are dispatched after the transaction commits; a notification failure is logged and never undoes or prevents the action. | §6.6 | 2 |
| BI-097 | Monitoring subsystem: a registry of monitored systems with their own recipient, events with severities (normal, no information, moderate, critical), e-mail only for moderate and critical, an e-mail record that goes from pending to sent or failed, and an error when the system is not registered. | drop | Everything it recorded becomes run events; alerts are reactions to domain events. | §6.1 | — |
| BI-098 | E-mail is sent over plain SMTP (no TLS, no authentication) to a single recipient string with a fixed subject; a send failure is reported as an e-mail error. | redesign | MailKit with TLS mode and authentication, password encrypted with DPAPI, several recipients, per-event selection, test e-mail command. | §6.5, §8, §9 | 3 |

## Settings

| ID | Rule (neutral English) | Verdict | New rule | Spec § | Plan |
|---|---|---|---|---|---|
| BI-099 | The cycle interval is a positive integer (seconds). | keep | | §6.5 | 1 |
| BI-100 | The failure limit is a positive integer. | keep | | §6.5 | 1 |
| BI-101 | The default time limit is ≥ 0 whole hours; 0 = no limit. | redesign | The default time limit is optional (none = unlimited, a negative value is rejected); the unit is the builder's call. | §6.2, §6.5 | 1 |
| BI-102 | The log retention days is a positive integer. | keep | | §6.5 | 1 |
| BI-103 | The cancellation grace period is a fixed 30-second constant. | redesign | Installation default (30 s) with a per-process override. | §6.2, §6.5 | 1 |
| BI-104 | SMTP host, port, sender, recipient and subject are always mandatory (port > 0), even though e-mail is the only alert channel. | redesign | E-mail alerts are optional (enabled flag); SMTP fields are validated only when e-mail is enabled. | §6.5 | 1 |
| BI-105 | The installation has a name, a description and a server name (all mandatory), and is looked up by a name from the configuration file. | drop | One installation per machine; "activator" becomes the installation settings. | §5 | — |
| BI-106 | Configuration-file keys select the database connection by environment name, a table prefix, the cancellation token name, the per-run log file prefix, and switches to turn log files on or off. | drop | | §5, §9 | — |
| BI-107 | The installer registers the service with recovery actions (restart after 60 s on the first failure, after 120 s on the second, nothing afterwards, failure count never reset) and offers a delayed automatic start. | redesign | The installer registers the service with automatic (delayed) start and the recovery action "restart after 60 s" on every failure, with the failure count reset after 1 day. | §11 (owner decision Q-9) | 5 |
| BI-108 | Service-kind processes are launched without a time limit. | redesign | Time limits (per-process or installation default) never apply to Service-kind processes; a Service-kind registration with a per-process time limit is rejected. | §6.2 (owner decision Q-10) | 1 |

## Owner decisions

These rules were not decided by the first version of the design; the owner decided them and the design now records most of them. The affected rows above already reflect each decision.

| Q | Decision | Rows |
|---|---|---|
| Q-1 | Keep an installation-wide pause: while paused no new run starts (schedule, retry, run now, Service-kind relaunch); active runs finish normally; the paused flag is persisted (§6.5). | BI-076 |
| Q-2 | An exhausted recurrence (COUNT reached, UNTIL passed) simply finishes: no next occurrence, no error (§6.4). | BI-029 |
| Q-3 | An enabled process whose counter already reached a lowered failure limit is blocked at the next cycle before running; a manual run still runs. | BI-044 |
| Q-4 | A manual run consumes the occurrences due at its start, like a scheduled run (§6.4). | BI-030 |
| Q-5 | When no working directory is set, the executable's folder is used (§6.2). | BI-045 |
| Q-6 | RRULE frequency SECONDLY is rejected; MINUTELY and coarser are accepted; UNTIL has no year range (§6.4). | BI-034, BI-035 |
| Q-7 | The process description is optional. | BI-002 |
| Q-8 | Process names are unique case-insensitively, ignoring surrounding whitespace. | BI-001 |
| Q-9 | The installer sets automatic (delayed) start and "restart after 60 s" on every failure, with the failure count reset after 1 day (§11). | BI-107 |
| Q-10 | Time limits never apply to Service-kind processes; a Service-kind registration with a per-process time limit is rejected (§6.2). | BI-108 |
| — | Log retention is by age in days: files older than N days are deleted (§6.5). | BI-087 |
