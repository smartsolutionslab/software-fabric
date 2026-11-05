## Frontend architecture: micro-frontends

- The shell is `web/app`; each backend module's screens are its remote, `web/modules/<module>` (`@{{ name }}/module-<module>`). Put a module's screens in its remote, never in the shell.
- `fabric add-module` creates the remote; the workspace, the AppHost and the shell find it by its directory. Never add a remote to a list.
- The shell never imports a remote, by package name or by path (lint and the build fail); it mounts one with `<Remote module="<module>" />`. Remotes never import each other; modules talk through the API and events.
- Each remote has its own `pnpm build`, `lint` and `test`; run them in `web/modules/<module>` or for all packages in `web/`.
- A remote's Tailwind classes are compiled by the shell's build: when a remote starts using a utility nothing used before, the shell is built and deployed with it.
