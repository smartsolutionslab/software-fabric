## E-mail: none

- The project sends no e-mail. Do not add MailKit, MimeKit or another e-mail library to a module; an architecture rule fails the build if MailKit or MimeKit appears.
- A feature that needs to send e-mail is a project decision (`fabric change --set backend.email=smtp`): escalate it with `needs:decision` instead of starting it in one module.
