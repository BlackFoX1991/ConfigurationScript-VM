# _cfs_web

Small reusable base for server-side CFS web apps.

Start here for the full guide:

- `DOCUMENTATION.md`

Files:

- `DOCUMENTATION.md`: detailed framework guide with API explanations, patterns, and end-to-end examples.
- `Roadmap.md`: ordered backlog for turning `_cfs_web` into a fuller framework.
- `Progress.md`: current execution status, completed items, and blockers.
- `USAGE_NOTES.md`: practical deployment notes, patterns, and current limitations.
- `common.cfs`: escaping, cookie helpers, query/form parsing, multipart upload parsing, HTTP metadata helpers.
- `dom.cfs`: `Dom.h(...)`, `Dom.render(...)`, low-level `Ui.*` helpers, `Html.page(...)`.
- `layout.cfs`: semantic page structure such as hero, grid, panel, row, stack.
- `components.cfs`: reusable UI blocks like metrics, log boxes, inline code and session lines.
- `forms.cfs`: higher-level POST form helpers, old-input binding helpers, and file/radio controls built on top of `Ui`.
- `validation.cfs`: small form-binding and validation helpers for request data, including boolean fields.
- `cookies.cfs`: read, serialize, write and expire browser cookies from CFS responses.
- `session_store.cfs`: default in-memory session store plus the pluggable store contract used by `ServerApp`.
- `component_state.cfs`: session-backed state buckets for server-rendered UI components.
- `guards.cfs`: route wrappers for protected handlers such as session-flag checks.
- `middleware.cfs`: reusable before/after middleware builders such as logging, counters and headers.
- `views.cfs`: small view-model and template layer for page and dashboard style rendering.
- `server.cfs`: route registry, session cookie handling, pluggable session-store integration, form/json/multipart body parsing, response helpers, old-input/flash handling, and server loop.
- `example_app.cfs`: fuller local example app that combines sessions, guards, uploads, proxy metadata, rate limiting, downloads, and signed cookies.
- `run_example_app.cfs`: tiny runner for the example app when the runtime can open a local listener.

The server wrapper supports:

- route params via patterns like `/hello/:name`
- route groups via `app.group("/prefix")`
- named routes with URL building via `app.get_named(...)`, `app.post_named(...)`, `ctx.request.url(...)`
- sync and async handlers, middleware, and static loaders
- first-class `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `HEAD`, and `OPTIONS` helpers
- `use_before(...)`, `use_after(...)`, `use_error(...)`
- static mounts via `serve_static_with(...)` and `serve_static_map(...)`
- filesystem-backed static mounts via `serve_static_dir(...)` for text-based assets
- request and response helper objects on `ctx.request` and `ctx.response`
- asset URL helpers via `app.asset_url(...)` and `ctx.request.asset(...)`
- text-based file helpers via `await ctx.response.file(...)` and `await ctx.response.download(...)`
- upload access via `ctx.request.upload(...)` and `ctx.request.uploads(...)`
- layout slots through `ViewModels.push_slot(...)` and the page templates
- session state (`ctx.session`) and app-wide state (`ctx.shared`)
- pluggable session stores via `app.set_session_store(...)`
- trusted proxy handling via `app.set_trusted_proxies(...)` for `X-Forwarded-*` request metadata
- signed cookies via `ctx.response.with_signed_cookie(...)`, `ctx.request.signed_cookie(...)`, and signed session-cookie support
- flash messages via `ctx.response.flash(...)`, `ctx.response.redirect_with_flash(...)`, and `ctx.request.flash(...)`
- persistent session messages via `ctx.response.session_message(...)`, `ctx.response.clear_session_message(...)`, and `ctx.request.session_message(...)`
- sticky old-input flows via `ctx.response.remember_old_input(...)`, `ctx.response.redirect_with_input(...)`, `ctx.request.old(...)`, and `ctx.request.old_error(...)`
- richer auth/session guards via `Guards.require_authenticated(...)`, `Guards.require_guest(...)`, `Guards.require_role(...)`, `Guards.require_session_flags_all(...)`, and `Guards.require_session_flags_any(...)`
- concurrent request processing via `poll_async(...)` and `respond_async(...)`
- request body limits via `app.set_max_body_bytes(...)` and per-route `{"max_body_bytes": ...}` overrides
- rate limiting via `Middleware.rate_limit(...)` and `Middleware.rate_limit_headers(...)`
- host/origin validation via `Middleware.host_policy(...)` and `Middleware.origin_policy(...)`
- automatic `405 Method Not Allowed` responses with `Allow` headers
- automatic `OPTIONS` responses for known route/static paths
- `HEAD -> GET` fallback when no explicit `HEAD` route exists
- bodyless normalization for `HEAD`, `204`, and `304` responses
- browser-friendly default HTML error pages for requests that explicitly accept HTML
- automatic `ETag` generation plus `If-None-Match` handling for static responses
- metadata-driven `Last-Modified` plus `If-Modified-Since` handling for static/file responses

Current limitation:

- the installed HTTP plugin currently transports response bodies as strings, so `serve_static_dir(...)` is currently suitable for text-like assets (`txt`, `html`, `css`, `js`, `json`, `svg`, `xml`) but not binary files such as real `png` or `jpg` content
- the current standard-library surface does not expose per-file timestamps directly, so automatic `Last-Modified` values currently come from explicit metadata or headers rather than filesystem inspection

Security defaults:

- session ids are generated from cryptographic random bytes
- the default session cookie uses `HttpOnly`, `SameSite=Lax`, and is browser-session scoped unless remember mode or persistent cookies are enabled
- idle and absolute session timeouts are enforced server-side
- mutating browser form requests are CSRF-protected
- `ctx.response.back(...)` only accepts same-host or relative targets
- default 500 responses are generic unless `app.set_debug_errors(true)` is enabled

Security helpers:

- `ctx.request.csrf_field_name()` and `ctx.request.csrf_token()`
- `Forms.event_button(..., ctx)` and `Forms.text_post(..., ctx)` inject the CSRF field automatically
- `ctx.response.rotate_session()` rotates the session id and refreshes the CSRF token
- `ctx.response.flash(...)` and `Forms.flash_message(ctx, ...)` support post-redirect-get feedback
- `Forms.text_field_from_ctx(...)`, `Forms.select_field_from_ctx(...)`, and `Forms.radio_group_from_ctx(...)` bind old input and field errors back into the UI
- `app.set_max_body_bytes(bytes)` rejects oversized requests with `413 Payload Too Large`
- `ctx.response.error_page(...)` and `ctx.response.debug_page(...)` render framework-styled HTML problem pages
- custom session stores can be attached with `app.set_session_store(store)` when `store` implements `load(...)`, `save(...)`, `forget(...)`, `prune(...)`, and `snapshot()`
- signed session cookies can be enabled with `app.set_session_cookie_signing_secret("...")`
- arbitrary cookies can be signed via `ctx.response.with_signed_cookie(...)` and verified via `ctx.request.signed_cookie(...)`
- auth/session routes can be wrapped with `Guards.require_authenticated(...)`, `Guards.require_guest(...)`, `Guards.require_role(...)`, `Guards.require_session_key(...)`, and the multi-flag guards
- persistent session messages can be stored with `ctx.response.session_message(...)` and rendered with `Forms.session_message(ctx, ...)`
- remember-me style cookies can be enabled per session with `ctx.response.remember_session(seconds = optional)` and reverted with `ctx.response.forget_session()`
- logout/session reset flows can be handled with `ctx.response.logout_session(fresh = true)`
- `Middleware.security_headers(overrides = optional)` applies sane response-header defaults without overwriting explicit handler headers
- `Middleware.rate_limit(limit, windowSeconds, keyFunc = optional, options = optional)` provides a fixed-window limiter
- `Middleware.rate_limit_headers(localKey = optional)` emits `X-RateLimit-*` and `Retry-After` headers from limiter state
- `app.set_trusted_proxies([...])` allows `ctx.request.client_ip()`, `ctx.request.host()`, `ctx.request.scheme()`, and `ctx.request.origin()` to honor trusted `X-Forwarded-*` headers
- `Middleware.host_policy(allowedHosts, options = optional)` rejects requests with invalid resolved hosts
- `Middleware.origin_policy(allowedOrigins = optional, options = optional)` validates `Origin` or `Referer` against the resolved request origin by default
- `app.set_session_timeouts(idleSeconds, absoluteSeconds)`
- `app.set_session_cookie_persistent(true)` opts into persistent cookies by default instead of browser-session cookies
- `app.set_session_cookie_secure(true)`
- `app.set_csrf_enabled(false)` for explicit opt-out cases

Full example:

- Buildable app module: `example_app.cfs`
- Direct runner: `run_example_app.cfs`
- Focused direct-handle verification: `phase15_example_app_smoke.cfs`
- Practical deployment notes: `USAGE_NOTES.md`
- Full reference guide: `DOCUMENTATION.md`

Minimal shape:

```cfs
import { Dom } from "_cfs_web/dom.cfs";
import { ComponentState } from "_cfs_web/component_state.cfs";
import { Guards } from "_cfs_web/guards.cfs";
import { Middleware } from "_cfs_web/middleware.cfs";
import { ServerApp } from "_cfs_web/server.cfs";
import { Validation } from "_cfs_web/validation.cfs";
import { Layout } from "_cfs_web/layout.cfs";
import { ViewModels, Templates } from "_cfs_web/views.cfs";

func home(ctx) {
    var vm = ViewModels.page("Demo", "Example", "Minimal page built on the reusable base.");
    ViewModels.push_slot(vm, "footer", Dom.h("div", null, "footer slot"));
    return ServerApp.html(Templates.page(vm, Layout.panel([
        Dom.h("div", null, "Content")
    ])));
}

async func main() {
    var app = new ServerApp(8092);
    var api = app.group("/api", "api");
    app.use_before(Middleware.request_logger());
    app.get_named("home", "/", home);
    app.get_named("hello.show", "/hello/:name", func(ctx) { return ctx.response.text(ctx.request.param("name")); });
    app.get_named("admin.home", "/admin", Guards.require_session_flag("is_admin", func(ctx) { return ServerApp.text("ok"); }));
    api.get_named("state", "/state/:id", func(ctx) {
        var form = Validation.bind({"name":ctx.request.param("id", "")});
        form.text("name");
        return ctx.response.json(ComponentState.scope(ctx, form.value("name"), {}));
    });
    return await app.run();
}

var _ = await main();
```

For a fuller in-folder example that stays aligned with the newer APIs, see `example_app.cfs`.
