# 5. Job Objects and the child contract

## Status

Accepted.

## Context

The service has to start any executable, stop it cleanly when asked, and be able to stop everything the executable started. It must also survive its own crash without losing track of the children, and it must not require a program to know about GapActivator.

## Decision

- **Launch.** The service creates each child with `CreateProcessW`, suspended, in a Job Object, and resumes it afterwards. The child runs without a window and with standard input set to `NUL`; its output and error go to one file per run, capped at 50 MiB. `System.Diagnostics.Process` is only a fallback to kill.
- **Cancel.** The service creates a manual-reset named event, `Local\GapActivator.Cancel.{childPid}`, before the child runs, and sets it to ask for a stop. After the grace period (30 seconds by default, per process) it kills the process tree through the job. The job does not kill anything when the service closes it, so children can outlive a crash.
- **Identity.** The child gets `GAPACTIVATOR_PARENT_PID` and `GAPACTIVATOR_PARENT_START_TIME`. The pair identifies the service even when a process ID has been reused.
- **Outcome.** Exit code `0` is success and anything else is a failure. GapActivator decides Cancelled and TimedOut itself, so the exit code after a cancel request does not matter.
- **Recovery.** After a crash the restarted service re-adopts every live child by process ID and start time.
- Any executable works with none of this. The contract only adds graceful cancellation and the parent identity. See [the child contract](../child-contract.md).

## Consequences

- A cancel reaches the process tree, not just the first process, and the service never waits forever on a stuck child.
- A child that outlives the service loses its captured output, because its pipe is broken, and must tolerate failed writes.
- After a crash, a re-adopted child's tree is found by walking parent links, so descendants whose parent has already exited are not reached. Durable job ownership is future work ([future work](../future-work.md)).
- There is a short window between creating a child and committing its process ID. A crash inside it leaves a run that cannot be matched to a process.
- When Windows shuts down it may kill the children without waiting for the grace period. The next start closes those runs.
