# Security policy

## Supported versions

Only the latest release of GapActivator receives security fixes. Please update to it before you report a problem, and say which version you ran.

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Report it privately through GitHub: open the **Security** tab of this repository, choose **Report a vulnerability**, and describe what you found. A report that helps us most has:

- the version and the Windows version you used;
- the account the service ran under (the virtual account or a specific one);
- the steps to reproduce it, and what you expected to happen instead;
- what an attacker could gain.

We will acknowledge the report, work on a fix and tell you when it is released. We credit reporters who want to be credited.

## What is in scope

GapActivator runs programs with the rights of the service account, so the questions that matter most are:

- who can change what the service runs (the named pipe, its authorization and the `GapActivator Operators` group);
- who can read the database, the logs and the captured run output (the access rights on `%ProgramData%\GapActivator`);
- how secrets are stored (environment variables marked secret and the SMTP password, encrypted with DPAPI at machine scope);
- the installer and its handling of the service account password.

Known limits are described in [docs/future-work.md](docs/future-work.md), and the trust model in [docs/running-the-service.md](docs/running-the-service.md).
