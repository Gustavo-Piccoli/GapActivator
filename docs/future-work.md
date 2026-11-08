# Future work

Things GapActivator does not do yet, and known limits. Nothing here is promised for a date.

## Product ideas

- **A distributed edition** with a central database that manages several machines.
- **Per-process run-as accounts**, so that each process runs under its own account instead of the service's.
- **Generic webhook notifications** for chat tools, next to the Windows notifications and e-mail.
- **More database providers**, through the existing repository ports.
- **Configurable retry backoff.**
- **Restricted service privileges.** Restrict the service's privileges with `sc.exe privs` to the minimum it needs, so that children do not inherit `SeImpersonatePrivilege`. This needs verifying first which privileges the service and typical children need.
- **Code signing and a winget package.** The first releases are unsigned, so SmartScreen warns about them.

## Known limits

- **Durable job ownership across service crashes.** After a crash, the restarted service re-adopts a live child by process ID and start time, and finds its process tree by walking parent links. Descendants whose parent has already exited are not reached and keep running when the service stops that child.
- **The IPC busy cap after abrupt disconnects.** The service allows at most 16 connections and answers a further one with `busy`. After clients disconnect abruptly, the cap can stay reached for a while, and new clients then get `busy`.
- **Localized service notifications.** Localizing the notifications that come from the service needs a protocol change.
- **Date formats.** English dates in the app use the en-US pattern instead of following the operating system's region.
- **Old schedule anchors.** A recurring rule is expanded from a fixed anchor, so the calculator does more work the older the anchor is. Re-basing old anchors would bound that cost.

## Tests to add

These are small gaps found in review and parked:

- A look-ahead test in the occurrence calculator does not discriminate: it passes with and without the behavior it is meant to guard.
- The occurrence calculator has no test for the cost of a sub-daily rule (such as `MINUTELY`) with a large `INTERVAL`. Large intervals are tested only for a yearly rule.
