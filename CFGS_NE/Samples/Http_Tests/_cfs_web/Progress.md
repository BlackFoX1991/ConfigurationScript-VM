# _cfs_web Progress

Last updated: 2026-04-01

## Current State

- `_cfs_web` is already a solid reusable base for server-rendered CFS apps.
- It is not yet a full framework; the remaining work is tracked in `Roadmap.md`.
- Milestone 5 is complete, and Milestone 6 is now mostly complete with the fuller example app, synced README coverage, usage notes, the full framework guide, and improved error/debug pages. The remaining work sits in live-server verification, which is still blocked by the runtime listener issue here.

## Completed

- [x] Tracking files `Roadmap.md` and `Progress.md` created.
- [x] Randomized session ids, CSRF protection, and safer redirect-back behavior are in place.
- [x] Multipart browser form fields now parse correctly in the request body pipeline.
- [x] `HEAD` responses are finalized with an empty body.
- [x] Security smoke coverage includes multipart CSRF handling.
- [x] `405 Method Not Allowed` responses now include ordered `Allow` headers.
- [x] Automatic `OPTIONS` responses are generated for known route and static paths.
- [x] `HEAD -> GET` fallback works when no explicit `HEAD` route exists.
- [x] `204` and `304` responses are normalized to stay bodyless.
- [x] Direct-handle HTTP semantics coverage exists in `phase3_http_semantics_smoke.cfs`.
- [x] Filesystem-backed static serving exists through `serve_static_dir(...)`.
- [x] Static directory traversal attempts are rejected by path normalization.
- [x] Static responses now emit `ETag` and honor `If-None-Match`.
- [x] Static/file responses now honor metadata-driven `Last-Modified` and `If-Modified-Since`.
- [x] Direct filesystem static coverage exists in `phase4_static_fs_smoke.cfs`.
- [x] Text-based file and download helpers exist on `ctx.response`.
- [x] Asset URL helpers exist on `app.asset_url(...)` and `ctx.request.asset(...)`.
- [x] Flash messages exist via `ctx.response.flash(...)`, redirect helpers, and `ctx.request.flash(...)`.
- [x] Multipart uploads are now exposed through `ctx.request.upload(...)` and `ctx.request.uploads(...)`.
- [x] `textarea`, `checkbox`, `select`, `radio`, file-field, and error-summary form helpers exist.
- [x] Sticky old-input flows exist through request/response helpers plus `Forms.*_from_ctx(...)`.
- [x] Validation now covers boolean fields and can surface field/error maps for redirect-after-post flows.
- [x] `ServerApp` can now use a pluggable session store through `app.set_session_store(...)`.
- [x] Flash, old-input, rotation, and expiry behavior were verified against a copying custom session store.
- [x] Session cookies can now be signed and verified server-side.
- [x] Generic signed-cookie helpers exist on the request/response side.
- [x] Auth/session guard helpers now cover authenticated, guest, role, session-key, and multi-flag checks.
- [x] Persistent session-message helpers now exist alongside one-request flash helpers.
- [x] Session lifecycle helpers now cover remember-mode cookies, reverting to browser-session cookies, and logout/reset flows.
- [x] `ServerApp` can now reject oversized request bodies with a configurable byte limit.
- [x] Route-local request-size overrides exist through `{"max_body_bytes": ...}` options.
- [x] Reusable default security-header middleware exists through `Middleware.security_headers(...)`.
- [x] Fixed-window rate limiting primitives now exist through `Middleware.rate_limit(...)`.
- [x] Rate-limit header emission exists through `Middleware.rate_limit_headers(...)`.
- [x] Trusted proxy handling now resolves client IP, host, scheme, and origin from `X-Forwarded-*` headers when the remote peer is explicitly trusted.
- [x] Host/origin validation policy helpers now exist through `Middleware.host_policy(...)` and `Middleware.origin_policy(...)`.
- [x] A fuller in-folder example app now exists through `example_app.cfs` plus `run_example_app.cfs`.
- [x] Practical deployment and limitation notes now exist in `USAGE_NOTES.md`.
- [x] A full framework guide now exists in `DOCUMENTATION.md` with API details and worked examples.
- [x] README coverage was refreshed to include the example app and current helper surface.
- [x] `ServerApp` now exposes browser-friendly default HTML error pages for HTML-accepting requests.
- [x] Explicit HTML helpers now exist through `ctx.response.error_page(...)` and `ctx.response.debug_page(...)`.
- [x] Direct file/helper coverage exists in `phase5_file_helpers_smoke.cfs`.
- [x] Direct flash/forms coverage exists in `phase6_forms_flash_smoke.cfs`.
- [x] Combined metadata/upload/old-input coverage exists in `phase7_metadata_uploads_old_input_smoke.cfs`.
- [x] Session-store contract coverage exists in `phase8_session_store_smoke.cfs`.
- [x] Signed-cookie coverage exists in `phase9_signed_cookie_smoke.cfs`.
- [x] Auth-guard coverage exists in `phase10_auth_guards_smoke.cfs`.
- [x] Session lifecycle/message coverage exists in `phase11_session_lifecycle_messages_smoke.cfs`.
- [x] Deployment/security smoke coverage now exists in `phase12_deployment_security_smoke.cfs`.
- [x] Rate-limit smoke coverage now exists in `phase13_rate_limit_smoke.cfs`.
- [x] Trusted-proxy and host/origin policy coverage now exists in `phase14_trusted_proxy_policies_smoke.cfs`.
- [x] Example-app direct-handle coverage now exists in `phase15_example_app_smoke.cfs`.
- [x] Error/debug-page coverage now exists in `phase16_error_debug_smoke.cfs`.

## In Progress

- [ ] Live async server smoke for `app.run()` is not green yet.
- [ ] Milestone 6 still needs the blocked live-server verification path.

## Known Blockers

- [ ] The installed runtime currently fails to open a live local HTTP listener in this environment.
  Observed behavior:
  `http_server(...).start()` fails with `Cannot start HTTP server ... Das Handle ist ungueltig. (code 6)`.
  This affects live-server smoke verification and appears below the `_cfs_web` layer.
- [ ] The current HTTP plugin response contract is string-only.
  Practical effect:
  `_cfs_web` can currently serve text-like filesystem assets safely, but true binary asset delivery needs plugin/runtime support for byte-array response bodies.
- [ ] The current standard-library surface does not expose per-file timestamps directly.
  Practical effect:
  automatic `Last-Modified` support works for explicit metadata/headers, but `serve_static_dir(...)` and `ctx.response.file(...)` cannot infer file timestamps on their own yet.
- [x] Handle-level async server contract without binding was already validated separately via the existing async edgecase.

## Verification Snapshot

- [x] `phase1_security_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase3_http_semantics_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase4_static_fs_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase5_file_helpers_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase6_forms_flash_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase7_metadata_uploads_old_input_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase8_session_store_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase9_signed_cookie_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase10_auth_guards_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase11_session_lifecycle_messages_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase12_deployment_security_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase13_rate_limit_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase14_trusted_proxy_policies_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase15_example_app_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [x] `phase16_error_debug_smoke.cfs` passes with the installed `CFGS_VM.exe`.
- [ ] `phase2_async_smoke.cfs` cannot currently complete end-to-end because the runtime cannot start the local listener here.

## Next Up

1. Stabilize and expand direct-handle plus live-server verification where the runtime allows it.
2. Re-run live-server smoke once the runtime listener issue is resolved.

## Session Notes

- 2026-04-01: Added roadmap/progress tracking.
- 2026-04-01: Added multipart form field parsing and `HEAD` response finalization.
- 2026-04-01: Extended security smoke coverage for multipart CSRF.
- 2026-04-01: Added `405` / `Allow`, automatic `OPTIONS`, `HEAD -> GET` fallback, and `204` / `304` normalization.
- 2026-04-01: Added `phase3_http_semantics_smoke.cfs` for direct-handle HTTP behavior coverage.
- 2026-04-01: Added `serve_static_dir(...)`, traversal hardening, `ETag` / `If-None-Match`, and `phase4_static_fs_smoke.cfs`.
- 2026-04-01: Added text-based file/download helpers, asset URL helpers, and `phase5_file_helpers_smoke.cfs`.
- 2026-04-01: Added flash helpers plus expanded form helpers and `phase6_forms_flash_smoke.cfs`.
- 2026-04-01: Added metadata-driven `Last-Modified` / `If-Modified-Since`, multipart upload modeling, old-input redirect helpers, validation bool support, and `phase7_metadata_uploads_old_input_smoke.cfs`.
- 2026-04-01: Added `session_store.cfs`, `app.set_session_store(...)`, and `phase8_session_store_smoke.cfs` to verify custom store persistence behavior.
- 2026-04-01: Added signed-cookie helpers, `app.set_session_cookie_signing_secret(...)`, and `phase9_signed_cookie_smoke.cfs`.
- 2026-04-01: Added richer auth/session guards in `guards.cfs` and verified them with `phase10_auth_guards_smoke.cfs`.
- 2026-04-01: Added session-message helpers plus remember/forget/logout lifecycle helpers and verified them with `phase11_session_lifecycle_messages_smoke.cfs`.
- 2026-04-01: Added request-body limits, default security-header middleware, and `phase12_deployment_security_smoke.cfs`.
- 2026-04-01: Added fixed-window rate limiting primitives plus `phase13_rate_limit_smoke.cfs`.
- 2026-04-01: Added trusted-proxy request metadata plus host/origin policy helpers and `phase14_trusted_proxy_policies_smoke.cfs`.
- 2026-04-01: Added `example_app.cfs`, `run_example_app.cfs`, `USAGE_NOTES.md`, and `phase15_example_app_smoke.cfs`.
- 2026-04-01: Added explicit error/debug page helpers plus browser-friendly default HTML error pages and `phase16_error_debug_smoke.cfs`.
- 2026-04-01: Added `DOCUMENTATION.md` as the full framework guide with API explanations, patterns, and examples.
- 2026-04-01: Confirmed live listener startup issue occurs outside the framework layer in the installed runtime on this machine.
