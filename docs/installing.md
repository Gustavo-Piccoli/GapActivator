# Installing GapActivator

This is the guide for the installer. For a quick start see the [README](../README.md).

## Before you start

- Windows 10 or later (or Windows Server), on x64 or ARM64. Pick the installer that matches: `GapActivator-Setup-<version>-x64.exe` or `GapActivator-Setup-<version>-arm64.exe`.
- Administrator rights. The installer registers a Windows service, so it asks for elevation.
- The installers are not code-signed yet. SmartScreen may warn you; choose "More info" and then "Run anyway". You can first check the download against `SHA256SUMS.txt`, published with each release (see [RELEASING](RELEASING.md#verifying-downloads-with-sha256sumstxt)).

## The installer pages

The installer is available in English and Brazilian Portuguese.

1. **Destination.** The default is `C:\Program Files\GapActivator` (the 64-bit Program Files folder of your architecture).
2. **Service account.** Choose the account the service runs under.
   - **Virtual service account `NT SERVICE\GapActivator` (recommended).** No password. The page notes that Operators mode is on: Administrators and members of the local group `GapActivator Operators` can change processes and settings, and everyone else can view. The user who runs the installer is added to that group.
   - **A specific account.** Enter `DOMAIN\user` (the box is pre-filled with the installing user) and the password. The installer checks them with Windows before it goes on, and grants the account the "Log on as a service" right. The page notes that Operators mode is off: only Administrators can change anything, because whoever changes what the service runs gets that account's rights.
3. **Tasks.** A Start Menu shortcut and a desktop shortcut.
4. **Finish.** Optionally launch GapActivator.

The account page is skipped on an upgrade, which keeps the account the service already has.

## Silent install

Run the installer elevated. Inno Setup's standard switches work, for example `/VERYSILENT`, `/SUPPRESSMSGBOXES`, `/NORESTART`, `/DIR=<folder>` and `/LOG=<file>`.

```powershell
.\GapActivator-Setup-<version>-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

The GapActivator switches are:

| Switch | Effect |
|---|---|
| `/ACCOUNT=<DOMAIN\user>` | Run the service under this account instead of the virtual account. It needs a password. |
| `/PASSWORD=<password>` | The password for `/ACCOUNT`. |
| `/DELETEDATA` | For the uninstaller: also delete `%ProgramData%\GapActivator`. |

Without `/ACCOUNT`, a silent install uses the virtual account. On an upgrade, `/ACCOUNT` and `/PASSWORD` are ignored: the service keeps the account it already has.

> **Warning: `/PASSWORD` is visible.** A switch on the command line can be seen in the process list while the installer runs, and Setup's own `/LOG` file records its command line, so `/PASSWORD` ends up in the log. Prefer the environment variable `GAPACTIVATOR_SETUP_PASSWORD`; the installer reads it once and then clears it. If both are given, `/PASSWORD` wins. The installer itself never passes the password to any other program: it validates it with `LogonUserW` and sets it on the service with `ChangeServiceConfigW`.

Run this from an elevated shell: a variable set in a non-elevated shell does not reach the elevated installer.

```powershell
$env:GAPACTIVATOR_SETUP_PASSWORD = Read-Host -AsSecureString | ConvertFrom-SecureString -AsPlainText   # PowerShell 7
.\GapActivator-Setup-<version>-x64.exe /VERYSILENT /ACCOUNT="DOMAIN\svc-gapactivator"
Remove-Item Env:GAPACTIVATOR_SETUP_PASSWORD
```

If the account and password are not accepted, the install stops with an error and creates no service.

## What gets created

| What | Details |
|---|---|
| Files | `GapActivator.exe` (the app) and its native `*.dll` files under the install folder, `LICENSE.txt` next to the app, and the service in the `Service` subfolder. No `.pdb` files. |
| The service | Name and display name `GapActivator`, with a description. Start type automatic (delayed). Default account `NT SERVICE\GapActivator`. The installer starts it at the end. |
| Recovery settings | Restart after 60 seconds on the first, second and later failures, with the failure count reset after one day. The service is also restarted when it stops with a non-zero exit code, which is how a requested restart comes back. |
| Event source | `GapActivator` in the `Application` log. |
| Group | The local group `GapActivator Operators`, only in virtual-account mode, with the installing user in it. |
| The data folder | `%ProgramData%\GapActivator`, owned by Administrators, with a protected ACL (no inherited entries): SYSTEM Full, Administrators Full and the service account Modify, each inherited by everything below. A folder that already exists is taken over: a junction in its place is removed (never its target), and the previous ACL is replaced. |
| Shortcuts | The ones you chose. |

## Upgrades

Run the new installer over the old one. It stops the running service, waiting up to eleven minutes for children to finish, replaces the files, keeps the configured account, re-applies the start type and the recovery settings, and starts the service again. The service files are replaced as a whole, so a file dropped by a newer build never lingers. Your data stays.

If the service does not stop in time, the installer says so. Wait for the processes to finish or stop the service yourself, then run the installer again.

## Uninstall

Use "Installed apps" in Windows Settings, or run the uninstaller from the install folder. It stops the service (with the same wait), deletes it, removes the event source and the `GapActivator Operators` group, and removes the files and the shortcuts. The "Log on as a service" right granted to a specific account is not removed.

The data folder stays by default. Interactive uninstall asks whether to delete it, with No as the default answer. A silent uninstall deletes it only with `/DELETEDATA`:

```powershell
& "C:\Program Files\GapActivator\unins000.exe" /VERYSILENT /DELETEDATA
```

## Troubleshooting

- **The app says the service is stopped.** Start the service from the app, or with `sc.exe start GapActivator`.
- **A process fails with "access denied".** The virtual account has few rights of its own, so folders that only specific users can reach are closed to it. Give `NT SERVICE\GapActivator` access to the folders the process uses.
- **Where to look.** The logs are under `%ProgramData%\GapActivator\logs`. Severe service errors also go to the Windows Event Log, source `GapActivator`. To run the service by hand in a console, see [running the service](running-the-service.md).
