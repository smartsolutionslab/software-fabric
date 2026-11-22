# E-mail: none

**Status:** Accepted (generated)

## Decision

The project sends no e-mail (ADR-0019 of the generator). No module references an e-mail library, and the host has no SMTP settings.

## Consequences

- An architecture rule fails the build when a module references MailKit or MimeKit.
- Sending e-mail later is a project-wide change: `fabric change --set backend.email=smtp` (ADR-0016 C1) brings `IEmailSender` in `src/BuildingBlocks/Email`, Mailpit in run mode, the SMTP parameters when publishing, and their gates. A library added to one module by hand skips all of them.
