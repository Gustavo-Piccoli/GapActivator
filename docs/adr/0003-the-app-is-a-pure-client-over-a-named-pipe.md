# 3. The app is a pure client over a named pipe

## Status

Accepted.

## Context

The desktop app must show live state and change processes, schedules and settings, while the service runs in session 0 under another identity. Three ways to connect them were considered:

| Option | Trade-off |
|---|---|
| The app writes the database directly, elevated | Simple, but it needs a UAC prompt for every change, there are two writers, and a change only takes effect on the next scheduler cycle. |
| Only the service touches the database; the app is a client over a named pipe protected by an ACL | One writer, immediate commands, live state and a single place for authorization. It needs a versioned message protocol, and the app needs the service to be running. |
| The app reads the database and writes through the pipe | The complexity of both, and the schema becomes a public contract. |

## Decision

The service is the only process that opens the database. The app is a client of the named pipe `GapActivator`, speaking length-prefixed JSON with a protocol version ([architecture](../architecture.md#the-pipe-protocol)). The app project references only the `Ipc` contract project, never the domain, the application or the infrastructure. The pipe ACL admits interactive users to view, the service re-checks every command against the caller's rights, and the pipe opens only after the startup reconciliation.

## Consequences

- One writer and one place that decides who may change what. The pipe ACL is a first gate, and the server-side check is the real one.
- Commands take effect at once and the app receives pushes of state changes.
- The app has no data of its own. When the service is stopped the app says so and offers to start it.
- The wire contract is a second set of types that mirror the application's views. An architecture test keeps the two in step, property for property.
- Because the app depends only on a small contract with no packages, it can be published with Native AOT ([ADR 0006](0006-native-aot-app-and-self-contained-service.md)).
- The protocol is versioned: a client and a service on different versions are told so at Hello.
