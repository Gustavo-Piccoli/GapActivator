# 1. Clean Architecture and CQRS without a mediator

## Status

Accepted.

## Context

GapActivator is a small product with rules that are easy to get subtly wrong: schedules and their occurrences, failure counting, blocking, and what happens to a run when the service stops. Those rules need to be tested without a database, a clock or a child process, and the design should draw its boundaries from the start.

For dispatching commands and queries, a mediator library was an option. Its licensing does not fit a small MIT project, and the indirection is not needed for a few dozen messages.

## Decision

Build the codebase in Clean Architecture layers: Domain, Application, Infrastructure and a Service host, with the app as a separate client. Separate commands (changes) from queries (reads), give each exactly one handler and invoke the handlers through two small interfaces and a thin dispatcher written in the project. The rules the product must keep are captured in [the behavior inventory](../behavior-inventory.md), and each rule it keeps is covered by a test.

## Consequences

- The domain and the application run in tests with fakes and no I/O. The domain never reads a clock; the time comes in as a value.
- [Architecture tests](../architecture.md#how-the-architecture-tests-enforce-the-rules) fail the test run, and so CI, when a layer reaches where it should not, or when a message has no handler or two.
- A call through the dispatcher is plain dependency injection, so a stack trace shows the handler directly.
- The project owns a small dispatcher instead of using a library; it is a few classes to maintain.
- More types than a single-project design would need. This is accepted for the testability.
