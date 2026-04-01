# _cfs_web Roadmap

This file is the ordered backlog for turning `_cfs_web` from a good reusable base into a fuller server-side CFS web framework.

Rules:

- Work top to bottom unless a blocker forces a detour.
- Mark an item done only when code and at least one focused verification exist.
- Keep imports on the `plugins/...` path.
- Track actual runtime blockers in `Progress.md` instead of silently downgrading goals.

## Milestone 0: Baseline Hardening

- [x] Session ids generated from cryptographic randomness.
- [x] CSRF protection for browser form requests.
- [x] Safer back redirects via same-host or relative target validation.
- [x] Multipart form field parsing for normal browser form posts.
- [x] `HEAD` responses finalized with an empty body.
- [ ] Live async server roundtrip smoke green against the installed runtime.

## Milestone 1: HTTP Semantics

- [x] Return `405 Method Not Allowed` when path matches but method does not.
- [x] Emit `Allow` header for `405` and `OPTIONS`.
- [x] Add automatic `OPTIONS` handling.
- [x] Add `HEAD -> GET` fallback when no explicit `HEAD` route exists.
- [x] Normalize bodyless status handling for `204` and `304`.
- [x] Review response normalization for headers and status/body combinations.

## Milestone 2: Static Files And Asset Delivery

- [x] Add filesystem-backed static loader helper.
- [x] Harden path traversal handling for static file serving.
- [x] Add ETag generation and `If-None-Match` handling.
- [x] Add `Last-Modified` and `If-Modified-Since` handling.
- [x] Add file/download response helper.
- [x] Add simple cache-busting asset URL helper.
- [ ] Revisit binary asset delivery once the HTTP plugin supports non-string response bodies.

## Milestone 3: Forms, Validation, And Uploads

- [x] Add file upload model on top of multipart parsing.
- [x] Add `textarea`, `select`, and `checkbox` helpers.
- [x] Add `radio` helpers.
- [x] Add sticky form values and old-input helpers.
- [x] Add form-level error summary helpers.
- [x] Add flash message helpers for redirect-after-post flows.
- [x] Expand validation helpers beyond plain text fields.

## Milestone 4: Sessions And Auth

- [x] Add pluggable session store contract.
- [x] Add signed or encrypted cookie option.
- [x] Add flash/session message storage helpers.
- [x] Add richer auth guard helpers beyond single session flags.
- [x] Review session rotation and remember-me style behavior.

## Milestone 5: Security And Deployment

- [x] Add request body size limits.
- [x] Add default security header middleware set.
- [x] Add rate limiting primitives.
- [x] Add trusted proxy and forwarded-header handling.
- [x] Add host/origin validation policy helpers.

## Milestone 6: DX, Testing, And Docs

- [ ] Stabilize smoke coverage for direct `handle(...)` and live-server paths.
- [x] Add a fuller example app that exercises all framework pieces.
- [x] Keep `README.md` in sync with exported APIs.
- [x] Improve error page and debug page support.
- [x] Add usage notes for common patterns and limitations.
- [x] Add a full framework guide with API explanations and examples.

## Current Execution Order

1. Stabilize smoke coverage for direct `handle(...)` and live-server paths where the runtime allows it.
2. Revisit live-server smoke once the runtime listener issue is resolved.
