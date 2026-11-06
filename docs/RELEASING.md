# Releasing GapActivator

## How a release happens

1. Merge changes to `main` using Conventional Commits (`feat:`, `fix:`, ...).
2. release-please keeps a release PR open with the next version and the changelog.
3. Merging that PR creates the tag `vX.Y.Z` and the GitHub Release.
4. `release.yml` publishes the service and the app for x64 and arm64, smoke-tests each publish, builds the installers, runs the installer test on both architectures, and attaches the 3 artifacts to the release:
   - `GapActivator-Setup-<version>-x64.exe` and `GapActivator-Setup-<version>-arm64.exe`
   - `SHA256SUMS.txt`

   Note: release-please publishes the GitHub release immediately; `release.yml` attaches the assets a few minutes later (typically under 15).

### The first release

`release-please-config.json` sets `"release-as": "1.0.0"`, so the first release PR proposes v1.0.0 whatever the commit history says. **After v1.0.0 is released, remove the `release-as` line** (in a `chore:` commit on `main`); otherwise release-please keeps proposing 1.0.0.

### Manual tag fallback

Push a tag `vX.Y.Z` (SemVer, `v` prefix) and `release.yml` runs the same pipeline. If the release already exists, the artifacts are uploaded to it (`--clobber`).

### Recovering from a failed release

If `release.yml` fails after release-please merged, the tag and an asset-less GitHub Release already exist, so the manual tag fallback does not apply. Open the failed run and use "Re-run failed jobs", or run `gh workflow run release.yml -f tag=vX.Y.Z` (assets are uploaded with `--clobber`).

### Pre-releases

Tags like `vX.Y.Z-rc.N` are published as GitHub pre-releases.

### Merging PRs

Merge PRs with rebase, not squash, so commit footers such as `Release-As:` survive into the history release-please reads.

### Dry runs

A dry run builds and tests everything but publishes nothing (version `0.0.0-ci.<run_number>`). It happens on:

- pull requests touching packaging files (`build/**`, `installer/**`, `release.yml`, `Directory.Packages.props`, `Directory.Build.props`, `global.json`, `GapActivator.App.csproj`, `src/GapActivator.Service/**`);
- pushes to `feat/**` branches touching the same files, so a plan branch gets the installer test without a pull request;
- `workflow_dispatch` with an empty tag.

The `publish` job is skipped in a dry run.

## Local packaging

From the repo root, in PowerShell (replace `<v>` with a SemVer version, for example `0.0.0-local`):

```powershell
build/Publish-Service.ps1 -Runtime win-x64 -Version <v>
build/Publish-App.ps1 -Runtime win-x64 -Version <v>
build/Test-Publish.ps1 -PublishDir artifacts/publish/x64
build/New-Installer.ps1 -PublishDir artifacts/publish/x64 -Version <v> -Arch x64
build/New-Checksums.ps1 -Directory artifacts/dist
```

Notes:

- Inno Setup 6 is needed locally only for `New-Installer.ps1`. Install it, or point the `ISCC` environment variable at `ISCC.exe`. CI pins Chocolatey `innosetup` 6.7.1.
- `Test-Installer.ps1 -InstallerPath <exe>` really installs and uninstalls GapActivator (a Windows service, a local group, an event source and the data folder) and must run elevated. Run it only on a machine where that is acceptable; CI runs it on the elevated GitHub-hosted runners.
- Output goes under `artifacts/`, which is git-ignored.

## One-time repository setup (owner)

- **Allow Actions to create pull requests:** repo Settings, Actions, General, "Allow GitHub Actions to create and approve pull requests". release-please needs it to open the release PR.
- **Private vulnerability reporting:** repo Settings, Advanced Security (Code security), enable "Private vulnerability reporting". SECURITY.md points reporters there.
- **CI on release PRs:** release PRs are opened with `GITHUB_TOKEN`, so CI does not run on them. Merge them with an admin bypass, or close and reopen the PR to trigger the checks.

v1 is unsigned.

## Verifying downloads with SHA256SUMS.txt

`SHA256SUMS.txt` lists the SHA-256 hash of every installer in the release. Download it next to the files, then:

```powershell
Get-FileHash .\GapActivator-Setup-<version>-x64.exe -Algorithm SHA256
# compare with the matching line in SHA256SUMS.txt
```

or, with a Unix-style tool (Git Bash, WSL):

```bash
sha256sum -c SHA256SUMS.txt --ignore-missing
```
