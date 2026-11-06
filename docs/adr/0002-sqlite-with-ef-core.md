# 2. SQLite with EF Core

## Status

Accepted.

## Context

The service needs durable storage for process registrations, schedules, settings and the history of runs. The history is queried with filters and written while the scheduler runs. The options were:

| Option | Trade-off |
|---|---|
| SQLite, embedded | Real SQL with transactions in a single file, no server to install, an EF Core provider and migrations. |
| LiteDB | A document store: not SQL and a smaller community. |
| SQL Server LocalDB | Needs its own installer and is not embedded. |
| JSON or YAML files | Fine for settings, poor for run history and for concurrent access. |

## Decision

Use SQLite through Entity Framework Core, with migrations, in a single edition: the database file is `%ProgramData%\GapActivator\gapactivator.db`. Only the service opens it ([ADR 0003](0003-the-app-is-a-pure-client-over-a-named-pipe.md)). Secrets, such as environment variables marked secret and the SMTP password, are stored encrypted with DPAPI at machine scope.

## Consequences

- The product has no database to install or configure, and a backup is a file copy of the data folder taken with the service stopped (the database uses a write-ahead log).
- The schema is private to the service. Children never open the database.
- DPAPI at machine scope means any local user could decrypt the secrets if they could read the database, so the installer locks the data folder down ([ADR 0004](0004-virtual-service-account-and-the-operators-group.md)).
- SQLite fits a single machine. A shared database for several machines would be a different edition, and other providers would go through the existing repository ports. See [future work](../future-work.md).
- EF Core is not used in the Native AOT app; it lives in the service, which is not AOT ([ADR 0006](0006-native-aot-app-and-self-contained-service.md)).
