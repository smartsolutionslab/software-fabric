# Frontend architecture: a single-page application

**Status:** Accepted (generated)

## Decision

One React application, `web/app`, served as static files and talking to the host's API. Modules of the backend appear as areas of this one app, not as separate apps.
