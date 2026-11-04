# Running the service

## Console mode

For development, run the service in a console. The data root is where the database and logs go; pass a folder of your own so that nothing touches `%ProgramData%`:

```powershell
GapActivator.Service.exe --GapActivator:DataRoot=C:\Temp\GapActivatorDev
```

Stop it with Ctrl+C. The same setting can come from the environment variable `GapActivator__DataRoot`.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Normal stop. |
| `1` | Fatal failure at startup or while running. |
| `3` | A restart was requested. A service manager with a recovery policy starts the service again. |

## Installing as a Windows Service (developers only)

The installer (Plan 5) does this for users. For development, run an elevated PowerShell with the path of your build:

```powershell
sc.exe create GapActivator binPath= "C:\path\to\GapActivator.Service.exe" start= demand
sc.exe failure GapActivator reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc.exe failureflag GapActivator 1
```

`failureflag 1` makes the Service Control Manager apply the recovery actions also when the service stops with a non-zero exit code, which is how a requested restart (exit code `3`) and a fatal failure (exit code `1`) come back. A requested restart takes effect only after the Service Control Manager's recovery delay of 60 seconds.

Remove it with `sc.exe delete GapActivator`.

> **Warning: this is for a developer install only.** Without the installer, nothing sets access rights on the data root, so it inherits `Users: Read` from ProgramData and any local user can read the database and the run output. ProgramData also lets any local user create files in the folder. That user can therefore plant a `gapactivator.db-wal` file before the service creates its own, and change the process definitions that the service then runs with its own rights. On a developer install this is privilege escalation, not only secret exposure. Secrets are protected with machine-scope DPAPI, which lets any local user decrypt them. `sc.exe create` without `obj=` runs the service, and every child it starts, as LocalSystem. Never store real SMTP passwords or secret variables in a developer install. The installer (Plan 5) sets the access rights.

When the system shuts down or reboots, Windows does not wait the 10 min 45 s the service asks for. The operating system may kill the children. At the next start, a RunToCompletion run with no recorded stop ends Abandoned, a Service-kind run ends Interrupted, and a run with a recorded stop ends with that stop's outcome; an earlier stop reason, such as a user cancel or a time limit, wins over the shutdown.
