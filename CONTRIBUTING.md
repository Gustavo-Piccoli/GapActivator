# Contributing

Thank you for helping. Please read the [Code of Conduct](CODE_OF_CONDUCT.md) first. For a security problem, see [SECURITY.md](SECURITY.md) instead of opening an issue.

## Prerequisites

- Windows. The service and the app use Windows APIs, and some tests need them.
- The [.NET SDK](https://dotnet.microsoft.com/download) 10.0.403 or newer (pinned in `global.json`).
- For installer builds only: [Inno Setup 6](https://jrsoftware.org/isinfo.php).

## Build and test

```powershell
dotnet build -c Release
dotnet test -c Release
dotnet format --verify-no-changes
```

Tests run on Microsoft.Testing.Platform. To run one project, pass it with `--project`:

```powershell
dotnet test -c Release --project tests/GapActivator.Domain.Tests
```

Warnings are errors, and `dotnet format --verify-no-changes` must pass. Some tests in `tests/GapActivator.Service.Tests/Setup/` change the machine (a local group, the Event Log, a data folder), so they run only elevated and skip otherwise.

To run the service from a console with a data folder of your own, see [docs/running-the-service.md](docs/running-the-service.md). The sample child in `samples/GapActivator.SampleChild` is a handy process to register.

## Architecture rules

The layers and what each may reference are in [docs/architecture.md](docs/architecture.md), and the reasons are in [docs/adr/](docs/adr/0001-clean-architecture-and-cqrs-without-a-mediator.md). In short:

- The domain has no packages and no I/O, and never reads a clock.
- The application depends only on the domain. Commands and queries have exactly one handler each, and there is no mediator library.
- The app references only `GapActivator.Ipc`, and `src/GapActivator.App` must stay Native AOT compatible.
- A new package needs a stated reason, and versions are pinned exactly in `Directory.Packages.props`.

The architecture tests in `tests/GapActivator.Architecture.Tests` enforce these rules, so a violation fails the test run and CI.

## Workflow

- Branch off `main` and open a pull request. A pull request needs green CI before it is merged: format, build, tests and the AOT publish check.
- Write a test first when you can. Behavior changes need a test that fails without the change.
- Use [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, ...). release-please builds the changelog and the version from them.
- Code, names, comments, docs and commits are in English.
- Use synthetic data in tests and docs: `example.com`, `127.0.0.1` and invented process names. Never commit a secret, a real e-mail address or a machine-specific path.
- Merge with rebase, not squash, so that commit footers such as `Release-As:` survive.

## Screenshots

The images in `docs/images` are rendered by a headless tool. After a visual change, regenerate them:

```powershell
dotnet run --project tools/GapActivator.Screenshots
dotnet run --project tools/GapActivator.Screenshots -- --out <folder>   # write somewhere else
```

## Packaging and releases

The scripts in `build/` publish, test and package the service and the app. How to run them and how a release happens is in [docs/RELEASING.md](docs/RELEASING.md). The installer is described in [docs/installing.md](docs/installing.md).
