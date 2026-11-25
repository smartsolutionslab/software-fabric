## E-mail: SMTP

- Send e-mail through `IEmailSender` (`src/BuildingBlocks/Email`) with an `EmailMessage(To, Subject, Body)`: one plain-text message to one address, from the configured sender. Only `src/BuildingBlocks/Email` names MailKit or MimeKit; the architecture rule `OnlyTheEmailWiringNamesMailKit` fails a module or host that does.
- Send from a consumer or a domain-event reaction, never from an endpoint: a slow or refused server must not fail the caller's request, and a failed send must be retried.
- A failed send throws; let it. The consumer's message is retried and then dead-lettered. Never catch and swallow a send failure.
- A consumer may receive the same event twice, so deduplicate on `message.Metadata.EventIdentifier` before sending. Keeping an e-mail from going out twice (a record of what was sent, a claim before the send) is the module's own code; there is no building block for it.
- The server is the connection string `mail` (`smtp://host:port` for STARTTLS, `smtps://host:port` for TLS), the sender `Email:SenderAddress`. In run mode the AppHost sends to Mailpit (inbox at http://localhost:8025); a deployment sets its own.
- The SMTP password is the AppHost's secret parameter `smtp-password` (with `smtp-user-name`), set in the deployment's secret store; never in `appsettings*.json`. Never set `Email:AllowUnencryptedSmtp` anywhere but the AppHost's run mode: `NoSmtpPasswordInSettings` fails a settings file that holds either.
- Tests never send e-mail: give the code under test a `FakeEmailSender` (`tests/Fakes`) and read what it sent from `Sent`; set `FailsNextSend` to see a failed delivery.
