# 4. The virtual service account by default, and the Operators group

## Status

Accepted.

## Context

Whoever can edit the list of registered processes can make the service run any program with the service's rights. The account the service runs under is therefore the main security decision. The options were:

| Option | Trade-off |
|---|---|
| LocalSystem | Full control of the machine. Whoever edits the process list owns the PC. Rejected as a default. |
| LocalService or NetworkService | Minimal rights, so most useful processes get "access denied". NetworkService is shared with other services. |
| Virtual account `NT SERVICE\GapActivator` | The identity Microsoft recommends for services: no password, least privilege, and unique, so a folder can be granted to it alone. Users must grant it access to the folders their processes use. |
| A specific account | Behaves like running the program by hand. The Service Control Manager stores the password, and a Windows password change breaks the service until it is updated. |
| An account per process | The most flexible, with much more complexity and a larger attack surface. |

## Decision

- The default is the virtual account. A specific account is an option on the installer's account page. For it the installer validates the credentials and grants the "Log on as a service" right.
- Any user with an interactive logon may view. Changing anything needs Administrators or, in virtual-account mode, membership of the local group `GapActivator Operators`. The installer creates the group and adds the installing user.
- With a specific account the Operators group is off and only Administrators may change anything. Operators membership would otherwise be a privilege escalation to that account, which may be an administrator.
- The mode comes from the configuration key `GapActivator:Authorization:OperatorsGroup`: `Auto` (the default) enables the group only under a virtual account, and `Enabled` and `Disabled` force it.
- The installer sets the data folder up so that only SYSTEM, Administrators and the service account have rights on it.

## Consequences

- Out of the box the service has few rights of its own. A process gets "access denied" on folders that only specific users can reach, until the user grants the virtual account access to them. The account's token still carries the groups every service has (such as Users, Authenticated Users and SERVICE), so it can reach what those groups can. The installer's documentation says so.
- Membership of the Operators group is effectively equal to local administrator. An Operator can register any program, and it runs as `NT SERVICE\GapActivator`. That token has `SeImpersonatePrivilege`, which can be escalated to SYSTEM. The group is a way to delegate day-to-day management, not a security boundary, so add only users you would trust as administrators. Restricting the service's privileges is listed in [future work](../future-work.md).
- Viewers can read run output, so [the child contract](../child-contract.md) tells authors never to print secrets.
- A specific account's password is never put on a command line by the installer, but a silent install can still receive it ([installing](../installing.md#silent-install)).
- Per-process run-as accounts are future work.
