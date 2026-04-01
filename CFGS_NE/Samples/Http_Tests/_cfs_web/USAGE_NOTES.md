# _cfs_web Usage Notes

This file collects the practical patterns and current limits that matter when you turn `_cfs_web` into a real app.

## Good Defaults

- Put `Middleware.rate_limit(...)` in `use_before(...)` and `Middleware.rate_limit_headers(...)` in `use_after(...)` so blocked and successful responses both emit consistent headers.
- Put `Middleware.security_headers(...)` late in `use_after(...)` so handlers can still override specific headers intentionally.
- Use `app.set_session_cookie_signing_secret("...")` in every non-trivial app. Unsigned session cookies are only reasonable for throwaway local demos.
- Keep `app.set_session_cookie_secure(true)` for real HTTPS deployments. The examples leave it off only so local `http://localhost` works.

## Forms And Redirects

- Use `Validation.from_request(ctx)` plus `ctx.response.redirect_to_with_input(...)` when you want classic POST -> redirect -> GET flows with sticky values and field errors.
- Checkbox old-input flows should store the raw posted checkbox value, not just the parsed boolean, if you want `Forms.checkbox_field_from_ctx(...)` to restore the checked state correctly.
- Multipart uploads are available through `ctx.request.upload(...)` and `ctx.request.uploads(...)`, but the framework currently models upload metadata and text content only.

## Sessions And Cookies

- The default session store is in-memory. For multi-process, restart-safe, or production scenarios, replace it with `app.set_session_store(...)`.
- `ctx.response.remember_session(...)` only changes cookie persistence. It does not replace server-side idle and absolute session timeouts.
- Signed helper cookies created through `ctx.response.with_signed_cookie(...)` can still be expired with the normal `ctx.response.expire_cookie(...)` helper because the browser only keys by cookie name/path/domain.

## Reverse Proxies

- Only call `app.set_trusted_proxies(...)` with addresses you actually control. Once a remote is trusted, `X-Forwarded-For`, `X-Forwarded-Host`, and `X-Forwarded-Proto` affect request metadata.
- `Middleware.host_policy(...)` and `Middleware.origin_policy(...)` are intended for deployment edges. Keep the allow-lists explicit and environment-specific.
- If you terminate TLS at a reverse proxy, configure trusted proxies first. Otherwise `ctx.request.scheme()` and `ctx.request.origin()` will stay on `http`.

## Current Limits

- The installed HTTP plugin still transports response bodies as strings, so true binary file or image delivery needs runtime/plugin support first.
- Automatic filesystem `Last-Modified` values are still unavailable because the current standard-library surface does not expose per-file timestamps directly.
- Live `app.run()` verification is still blocked in this environment because `http_server(...).start()` fails below the framework layer with the known listener error.
