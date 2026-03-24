# _cfs_web

Small reusable base for server-side CFS web apps.

Files:

- `common.cfs`: escaping, cookie helpers, query/form parsing.
- `dom.cfs`: `Dom.h(...)`, `Dom.render(...)`, low-level `Ui.*` helpers, `Html.page(...)`.
- `layout.cfs`: semantic page structure such as hero, grid, panel, row, stack.
- `components.cfs`: reusable UI blocks like metrics, log boxes, inline code and session lines.
- `forms.cfs`: higher-level POST form helpers built on top of `Ui`.
- `validation.cfs`: small form-binding and validation helpers for request data.
- `cookies.cfs`: read, serialize, write and expire browser cookies from CFS responses.
- `component_state.cfs`: session-backed state buckets for server-rendered UI components.
- `guards.cfs`: route wrappers for protected handlers such as session-flag checks.
- `middleware.cfs`: reusable before/after middleware builders such as logging, counters and headers.
- `views.cfs`: small view-model and template layer for page and dashboard style rendering.
- `server.cfs`: route registry, session cookie handling, form/json body parsing, response helpers, server loop.

The server wrapper supports:

- route params via patterns like `/hello/:name`
- route groups via `app.group("/prefix")`
- named routes with URL building via `app.get_named(...)`, `app.post_named(...)`, `ctx.request.url(...)`
- `use_before(...)`, `use_after(...)`, `use_error(...)`
- static mounts via `serve_static_with(...)` and `serve_static_map(...)`
- request and response helper objects on `ctx.request` and `ctx.response`
- layout slots through `ViewModels.push_slot(...)` and the page templates
- session state (`ctx.session`) and app-wide state (`ctx.shared`)

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

func main() {
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
    app.run();
}

main();
```

For a larger example, see `../mini_web_framework_cfs_only.cfs`.
