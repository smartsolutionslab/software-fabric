# ADR-0019: E-mail, `backend.email` (`none`, `smtp`)

**Status:** Proposed, waiting for the owner's review (#861, M5-01). It records decisions the owner has already made:
E1 to E3 and X1 (2026-11-21, on #602) and the amendment of ADR-0016 C2 (2026-11-22, on #602, from the review of
PR #869). It makes no decision of its own; where the decisions leave something to the pack's task (M5-02), it says so.
**Date:** 2026-11-22

## Context

ADR-0001 and the M4 decisions E3 and E7 defer a catalog dimension until a pilot has built it by hand, so that its pack
is designed from working code. M5's plan (`docs/roadmap.md`, M5 tasks, "E-mail", on branch `agent/602`, PR #869) makes
e-mail the first of them. The proposal on #602 (section 1) described fabric-pilot's sending and the shape a pack would
take; the owner accepted it on 2026-11-21 as E1 to E3.

What follows is read from fabric-pilot at `c137199` (origin/main). fabric-pilot (`profile: saas-ai-react`,
`backend.auth: keycloak`, `backend.tenancy: column`, modular monolith, generator 0.8.0) e-mails the customer who raised
a ticket when its status changes. fabric-pilot has no ADR of its own for it yet; X2 (2026-11-21) adds one in a
fabric-pilot issue.

### fabric-pilot's hand-written e-mail

- **The interface**, in the module's Application project:
  `src/Modules/Notifications/Notifications.Application/IEmailSender.cs` (`SendAsync(EmailMessage, CancellationToken)`;
  "A failure throws, so the caller's message is retried") and `EmailMessage.cs` (`To`, `Subject`, `Body`: one
  plain-text message to one address, from the configured sender).
- **The sender**, `src/Modules/Notifications/Notifications.Infrastructure/Email/MailKitEmailSender.cs`: MailKit
  (`Directory.Packages.props`: `MailKit` 5.0.0, since fabric-pilot#74), one SMTP connection per send, a 30-second
  timeout per network operation, the caller's token bounding the whole send, and a sign-in only when credentials are
  given.
- **The settings**, `.../Email/SmtpSettings.cs`: the connection string `mail` is `smtp://host:port` (STARTTLS) or
  `smtps://host:port` (TLS from the start), and anything else is refused; the sender is `Notifications:SenderAddress`.
  A server without TLS is refused unless `Notifications:AllowUnencryptedSmtp` is true, which only the AppHost sets, for
  Mailpit. `Notifications:SmtpUserName` and `Notifications:SmtpPassword` are given together or not at all
  (fabric-pilot#66). `.../Email/SmtpCredentials.cs` names the user and never shows the password in its text.
- **The wiring**, `.../Wiring/NotificationsServices.cs`: `SmtpSettings.From(configuration)` and
  `MailKitEmailSender` as singletons.
- **The AppHost**, `src/AppHost/AppHost.cs`, lines 42 to 114. In run mode the host sends to the Mailpit container that
  the keycloak pack added (`IResourceBuilder<ContainerResource>? mailpit`, its `smtp` endpoint as
  `ConnectionStrings__mail`, the sender `noreply@example.com`, `Notifications__AllowUnencryptedSmtp=true`,
  `.WaitFor(mailpit)`). When publishing, it declares the parameter `smtp-user-name` and the secret parameter
  `smtp-password`.
- **The deployment**, `infra/README.md` ("What you need" and step 4): `SMTP_CONNECTION`,
  `NOTIFICATIONS_SENDER_ADDRESS`, and an optional `SMTP_USER_NAME` and `SMTP_PASSWORD`, the password held in Key Vault
  as `smtp-password`. Mailpit is not deployed; a refused sign-in ends in `wolverine.wolverine_dead_letters`.
- **Using it**, `src/Modules/Notifications/Notifications.Application/Consumers/TicketStatusChangedV1Consumer.cs`: an
  e-mail at most once per event. The consumer finds or creates the notice by `Metadata.EventIdentifier`, claims the
  e-mail on it with a lease and a send deadline (`Notifications.Domain/Notices/Notice.cs`, migration
  `20261113090336_ClaimNoticeEmails`), saves the claim, sends, gives the claim back and throws if the send fails, and
  marks the notice e-mailed.
- **The tests**: `tests/Modules/Notifications.Tests/Email/MailKitEmailSenderTests.cs` (a cancelled send, a server
  without STARTTLS, a server that does not speak TLS, through `StallingSmtpServer.cs`), `Email/SmtpSettingsTests.cs`,
  `RecordingEmailSender.cs`; against the AppHost `tests/Integration.Tests/NotificationsTests.cs` (an event delivered
  twice e-mails once, read from Mailpit through `MailpitInbox.cs`) and `NotificationsSmtpAuthenticationTests.cs` (an
  authenticating Mailpit takes the configured user's e-mail; a wrong password fails the send).

### Mailpit in the generator

Until #861, `packs/backend/auth/keycloak/templates/apphost-resources.sbn` added Mailpit in run mode, for Keycloak's own
e-mail: the realm (`packs/backend/auth/keycloak/templates/realm.json.sbn`) points `smtpServer` at `mailpit:1025`, and
the keycloak pack's gate reads a reset-password e-mail from it (`tests/Fabric.Packs.Tests/MailpitTests.cs`). Under
`generic-oidc` and `entra-id` there is no Mailpit, so an e-mail pack could not rely on it.

## Decision

### E1: `backend.email` with `none | smtp` (owner, 2026-11-21)

A project-scoped dimension `backend.email` with two options:

- **`none`**: no e-mail. The default, so no existing profile changes when the dimension is added.
- **`smtp`**: e-mail over SMTP with MailKit, shaped after fabric-pilot's sending above, moved from one module into
  `src/BuildingBlocks/Email` (`IEmailSender`, `EmailMessage`, `SmtpSettings`, `Wiring/AddEmail`, the shape of
  `BuildingBlocks/Llm`), with `tests/Fakes/FakeEmailSender`. It fills slots that exist today: `apphost.resources` (the
  SMTP parameters when publishing), `apphost.host-wiring` (the connection string `mail`, the sender and the permission
  for an unencrypted server, in run mode only; with the services topology on every service host, as ADR-0017 has it),
  `host.services` and the package versions.

No provider-API option (Azure Communication Services' or SendGrid's own API) until a pilot proves one. The pilot's
deployment names Azure Communication Services among the SMTP servers it can send through, signed in (`infra/README.md`),
so `smtp` covers it.

The accepted proposal names the pack's gates and rules, and M5-02 builds them, each seen failing first:

- the architecture rule `OnlyTheEmailWiringNamesMailKit`: MailKit and MimeKit are referenced only from
  `BuildingBlocks/Email`, so modules take `IEmailSender`;
- the architecture rule `NoSmtpPasswordInSettings`, like `NoApiKeyInSettings`: no SMTP password, and no permission for
  an unencrypted server, in an `appsettings*.json`;
- an integration test against the AppHost: an e-mail sent through `IEmailSender` reaches Mailpit, and a server without
  TLS is refused;
- AGENTS.md rules: send from a consumer or a reaction, never from an endpoint; a failed send throws; deduplicate on
  `Metadata.EventIdentifier`; tests use the fake.

Left to M5-02, in its own review: the configuration keys' names (the pilot's are its module's, `Notifications:*`),
and how the pilot's TLS, timeout and sign-in behaviour is split between the building block and its settings.

### E2: Mailpit in the base, behind a condition (owner, 2026-11-21)

Mailpit is the base's, not the keycloak pack's, so e-mail has it under every auth option. The owner chose this over a
new `apphost.mail-catcher` slot: it is one container, and the keycloak realm already points at the host `mailpit`.

As built in #861, in its own change before the pack (ADR-0002):

- The base (`packs/base/core/pack.yaml`) contributes `templates/AppHost/mailpit-resources.sbn` to `apphost.resources`
  at order 21, straight after the identity provider's resources (order 20), the same statements keycloak contributed:
  the container `axllent/mailpit` `v1.33` in run mode only, its inbox on port 8025, its `smtp` endpoint, and
  `keycloak.WaitFor(mailpit)`.
- The condition is in the template, as ADR-0006 lets a pack read another dimension: `project["backend.auth"] ==
  "keycloak"`. ADR-0006's `when` is a key of a file entry, and Mailpit is a contribution to a slot of a file the base
  already writes, so the condition sits at the top of that contribution, and a contribution that renders empty leaves
  no trace (`src/Fabric.Core/Rendering/Renderer.cs`).
- It names keycloak alone for now: `backend.email` is not in the catalog yet, and a template that reads a dimension
  the project does not have reads nothing, so naming it now would add Mailpit to every project. M5-02 widens the
  condition to `backend.auth` keycloak or `backend.email` other than `none`, the proposal's
  `auth == keycloak or email != none`.
- Every profile and every combination of `build/matrix.json` generates byte-identical output: the move changes which
  pack writes the lines, not the lines.

### E3: no building block for the at-most-once claim (owner, 2026-11-21)

The pilot's claim with a lease and a send deadline stays module code. The pack ships an AGENTS.md rule instead
(deduplicate on `Metadata.EventIdentifier`, as integration-event consumers already must), and leaves how a module
keeps a send from happening twice to that module. It is the pilot's domain choice and has one user.

### X1: M5's dimensions are `changeable` (owner, 2026-11-21)

A pilot moves from its hand-written version to the pack through `fabric change` once the pack lands: the only way the
pack is proven on the code it was designed from. So `backend.email`, and after it `backend.blob-storage` and
`deploy.target`, are marked `changeable: true` by the task that adds each (M5-02, M5-05, M5-08). M5-03 is the first
use: fabric-pilot runs `fabric change --set backend.email=smtp` and replaces its hand-written sending with the
generated `IEmailSender`.

### Amendment of ADR-0016 C2 (owner, 2026-11-22)

ADR-0016 C2 allowed `changeable` only on a dimension whose options differ in what the generated files show, not in
what the project's own code builds on: presentation. M5's dimensions are not presentation; e-mail is a library the
project's code calls. The owner decided on 2026-11-22 that this ADR amends C2, so they may be `changeable`.

**The amendment.** Besides a presentation dimension, `changeable` may mark a dimension that M5 adds after a pilot built
it by hand: `backend.email`, `backend.blob-storage` and `deploy.target`. C2's catalog lint is not amended: a marked
dimension is still project-scoped and still named by no other dimension's `active-when`, `requires` or
`default-from`, nor by any option's `requires` or `conflicts`. Should one of them fail that lint, it is a question for
that dimension's ADR, not settled here. Any other non-presentation dimension stays unchangeable until the owner marks it.

**Why.** C2's line kept `fabric change` to choices whose merge rewrites files a project rarely edits. A dimension that
starts from `none` is close to that case: its pack adds files the project has not got and fills slots in files the
generator writes. ADR-0016's merge already handles the rest. The pack's own files are new to the project. Where the
project edited a file the pack fills, such as `src/AppHost/AppHost.cs`, the overlap is a git conflict (D8), not a loss
(D1). Files only the project has, here the pilot's hand-written sender, are in neither generated tree and are left
alone (E4). Removing them is the pilot's own change, reviewed in its CI. Without the amendment, the pilot could reach
the pack only by generating itself again or by copying the pack's files by hand. Then nothing would show that the
pack, applied to the code it was designed from, gives that code.

**What it does not change.** C3 holds as written: a project this fabric and catalog wrote, a clean git tree, D8's
merge, nothing committed, and D1 (b) read as fresh generation with the changed choice. D1's "not guaranteed" list
applies with more weight than for a colour: an edited project may not build until its own code is moved onto the
generated types, a lockfile may need reinstalling, and the summary does not search the project's code for the
hand-written version it replaces.

ADR-0016 carries a note at C2 that points here.

## Consequences

- Mailpit is in the base from #861. Keycloak profiles generate it as before; generic-oidc and entra-id profiles still
  have none until `backend.email` is `smtp`.
- The base names keycloak's resource (`keycloak.WaitFor(mailpit)`) under the keycloak condition. The base has read
  another dimension's choice before (ADR-0006), and here the reason is the realm, which keycloak owns.
- `mailpit` is declared inside the run-mode block, so nothing after it can name it. M5-02's host wiring needs it, as
  the pilot's does (`src/AppHost/AppHost.cs:42`, a nullable variable declared before the block). That change is
  M5-02's, and its profile diff shows it.
- `fabric change` can now move a project onto a pack whose options change code, for M5's three dimensions only. The
  first such change, M5-03, is the test of the amendment: if the merge leaves the pilot in a state its CI cannot reach
  green with ordinary work, that is a finding for this ADR.
- No provider-API option, no at-most-once building block: each waits for a pilot that needs it (E1, E3).
