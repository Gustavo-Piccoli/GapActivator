# 1. Clean Architecture and CQRS without a mediator

## Status

Accepted.

## Context

GapActivator is a small product with rules that are easy to get subtly wrong: schedules and their occurrences, failure counting, blocking, and what happens to a run when the service stops. Those rules need to be tested without a database, a clock or a child process. Three ways to build the project were considered:

- **Refactor an existing codebase in place.** Almost nothing would survive the redesign, so most steps would translate code that is about to be replaced.
- **Start a new codebase from scratch, using the observable rules of the earlier tool as a specification.** The design is born with the right boundaries. It costs discipline to not lose subtle rules, which is solved by a behavior inventory in which every preserved rule becomes a test.
- **Start a new codebase by porting module by module.** Faster at first, but it copies the old design.

For dispatching commands and queries, a mediator library was also an option. Its licensing does not fit a small MIT project, and the indirection is not needed for a few dozen messages.

## Decision

Build a new codebase in Clean Architecture layers: Domain, Application, Infrastructure and a Service host, with the app as a separate client. Separate commands (changes) from queries (reads), give each exactly one handler and invoke the handlers through two small interfaces and a thin dispatcher written in the project. Record the preserved rules in [the behavior inventory](../behavior-inventory.md).

## Consequences

- The domain and the application run in tests with fakes and no I/O. The domain never reads a clock; the time comes in as a value.
- [Architecture tests](../architecture.md#how-the-architecture-tests-enforce-the-rules) fail the build when a layer reaches where it should not, or when a message has no handler or two.
- A call through the dispatcher is plain dependency injection, so a stack trace shows the handler directly.
- The project owns a small dispatcher instead of using a library; it is a few classes to maintain.
- More types than a single-project design would need. This is accepted for the testability.
