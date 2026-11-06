# 6. A Native AOT app and a self-contained service

## Status

Accepted.

## Context

Users should not have to install the .NET runtime, and the app should start quickly. The two programs have different constraints:

- The app is a thin client with a small dependency set (Avalonia and the `Ipc` contract). Native AOT is feasible for it.
- The service uses EF Core, whose support for Native AOT is not mature, and Ical.Net and MailKit. Trimming it would risk failures at run time that only show up on some code paths.

## Decision

- Publish the app with Native AOT. Every project it references must stay AOT-compatible, and CI publishes it with AOT to enforce that. Avalonia's native libraries cannot be linked into the executable, so the package ships `GapActivator.exe` plus its native `*.dll` files rather than a true single file.
- Publish the service self-contained, not trimmed and not as a single file, into a `Service` subfolder. It carries its own runtime, and it avoids the trimming risks above.
- Build both for x64 and for ARM64 on matching runners. There is no framework-dependent variant.

## Consequences

- Nothing to install first. The cost is size: the service folder carries the runtime.
- Every publish is smoke-tested before it ships: the service starts in console mode and opens its pipe, and the app starts and stays alive. This catches a missing native library, such as the SQLite one, or a trimming failure.
- The app cannot use reflection-based APIs, and architecture tests keep its references to the justified ones.
- One build configuration per architecture to test and support.
