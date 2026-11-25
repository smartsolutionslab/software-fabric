# E-mail: SMTP

**Status:** Accepted (generated)

## Decision

The project sends e-mail over SMTP with MailKit, behind one interface, `IEmailSender`, in `src/BuildingBlocks/Email` (generator ADR-0019, drawn from fabric-pilot's hand-written sending).

- **One plain-text message to one address.** `EmailMessage` holds the recipient, the subject and the body; the sender is the configured address, `Email:SenderAddress`. Modules take `IEmailSender`; only the building block names MailKit or MimeKit, so another SMTP library, or a provider's API once one is chosen, changes one folder and no module.
- **One connection per send.** `MailKitEmailSender` connects, signs in when it has credentials, sends and disconnects. Each network operation times out after 30 seconds and the caller's cancellation token bounds the whole send. A failure throws, so the consumer or reaction that sent is retried; a send is never swallowed.
- **TLS unless the AppHost says otherwise.** The connection string `mail` is `smtp://host:port` (STARTTLS, required) or `smtps://host:port` (TLS from the start); anything else is refused when the sender is first needed. A server that offers no TLS is refused unless `Email:AllowUnencryptedSmtp` is true, which only the AppHost sets, in run mode, for the local Mailpit; a settings file never does.
- **Signing in is optional and all or nothing.** `Email:SmtpUserName` and `Email:SmtpPassword` are given together or not at all; without them nothing signs in, as with Mailpit or a relay. When publishing, the AppHost declares them as the parameter `smtp-user-name` and the secret parameter `smtp-password`; the password never lives in a settings file, and the settings' text names the user but never shows the password.
- **Mailpit catches every e-mail locally.** In run mode the AppHost starts Mailpit (its inbox at http://localhost:8025) and gives the host its SMTP endpoint, the sender `noreply@example.com` and leave to send unencrypted, and the host waits for it. Mailpit is not published: a deployment gives the host its own server as the connection string `mail` and its sender as `Email:SenderAddress`.
- **The settings are read when the sender is first needed.** The host starts without them, and the first send fails naming what is missing; registering the sender connects to nothing.
- **At most once is the module's job.** A consumer may receive the same integration event twice, so it deduplicates on `Metadata.EventIdentifier`; how a module keeps an e-mail from going out twice (a record of what was sent, a claim before sending) is its own code, not a building block.

## Consequences

- The architecture rule `OnlyTheEmailWiringNamesMailKit` fails when a module assembly references MailKit or MimeKit, or a source file under `src` outside `src/BuildingBlocks/Email` names either.
- The architecture rule `NoSmtpPasswordInSettings` fails when an `appsettings*.json` holds the SMTP password or leave to send unencrypted; it names the file and the setting, never the value.
- Tests never send e-mail: `tests/Fakes/FakeEmailSender` keeps what it is given and fails the next send when told to, and each module's container check registers it where the host registers the real sender. `tests/Email.Tests` checks the settings and the sender's refusals against a loopback server{{ if project["backend.test.integration"] != "testcontainers" }}, and the integration tests send through `IEmailSender` to the AppHost's Mailpit and see a server without TLS refused{{ end }}.
- Every e-mail sends its content to the SMTP server the deployment names. A refused sign-in or an unreachable server fails the send, and the consumer's message is retried and then dead-lettered like any other failed delivery.
