# 7. The installer and the release pipeline

## Status

Accepted.

## Context

GapActivator registers a Windows service, creates a local group and an event source, and sets access rights on a data folder. Doing so needs administrator rights and must be repeatable, upgradable and removable. Releases must reach users as signed-off artifacts for both architectures without a code-signing certificate in the first version.

## Decision

- **Inno Setup 6, per machine.** The installer asks for administrator rights and installs to `{autopf}\GapActivator`. There is no portable build, because a Windows service cannot run loose.
- **The installer calls the service.** Operations that need Windows APIs, namely preparing the data folder, granting the logon right, registering the event source and managing the Operators group, are verbs of the service executable: `GapActivator.Service.exe setup ...`. They are unit-tested in C#, not written in installer script.
- **Passwords stay out of command lines.** For a specific account, the installer validates the password with `LogonUserW` and sets it with `ChangeServiceConfigW`, and passes none to the setup verbs. The silent switch `/PASSWORD` is visible in the process command line and in the setup log, so the environment variable `GAPACTIVATOR_SETUP_PASSWORD` is the recommended route.
- **Upgrades keep the account.** An upgrade stops the service, replaces the files, keeps the configured account, re-applies the start type and starts it again. The uninstaller keeps the data unless the user opts in, or `/DELETEDATA` is passed.
- **Scripts double as tests.** `build/Publish-*.ps1`, `Test-Publish.ps1` and `Test-Installer.ps1` are what CI runs, and they are what a developer runs.
- **release-please plus a reusable workflow.** release-please opens the release PR and creates the tag. A tag created with the default token does not start other workflows, so release-please calls `release.yml` directly. A manual `v*` tag push works too.
- **Dry runs.** Pull requests that touch packaging files, `feat/**` branches and manual runs without a tag build and test everything with the version `0.0.0-ci.<run_number>` and publish nothing.
- **Unsigned in v1.** Code signing and a winget package come later.

## Consequences

- Users get one installer per architecture and `SHA256SUMS.txt`. SmartScreen warns about unsigned builds until signing is added.
- The installer test runs on elevated hosted runners for both architectures, so a change to the packaging is exercised before a release.
- Release pull requests opened with the default token do not run the normal CI, so they are merged with an admin bypass, or closed and reopened. See [RELEASING](../RELEASING.md).
- Merging with rebase keeps commit footers such as `Release-As:` in the history that release-please reads.
