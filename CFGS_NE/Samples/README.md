# Samples

This folder contains a mix of runnable demos, interactive tools, long-running server samples, and a small number of negative examples.

Build the repository first so the sample imports can resolve `dist/Debug/net10.0`.

```bat
dotnet build -v minimal CFGS_VM.sln
```

Run a sample from the repository root.

```bat
dotnet dist\Debug\net10.0\CFGS_VM.dll CFGS_NE\Samples\general_Tests\feature_08_async_await_yield.cfs
```

Recommended entry points.

- `general_Tests/feature_01_core_control_classes.cfs` through `feature_08_async_await_yield.cfs` are the current feature-oriented reference set.
- `general_Tests/destructuring.cfs`, `foreach.cfs`, and `small_test.cfs` are compact focused examples.
- `Class_Tests/classes.cfs` and `Class_Tests/inheritance.cfs` are the clearest OOP samples from the older set.
- `Http_Tests/mini_web_framework_cfs_only.cfs` is the cleanest starting point for a server-side CFS web app without custom browser JavaScript.
- `Http_Tests/_cfs_web/` contains the reusable base pieces behind that sample: helpers, DOM and HTML builders, semantic layout/components/forms modules, validation, cookie and guard helpers, reusable middleware, a tiny view-model/template layer with slots, static mounts, named routes with URL building, route groups, request/response helper objects, component state helpers, and a small route/session server wrapper.

Important caveats.

- `Class_Tests/cyclic_edge.cfs` is an intentional negative sample and should fail with a compile error.
- `tool_scripts/ExprParser.cfs`, `tool_scripts/qrTest.cfs`, and `tool_scripts/bloomRepl.cfs` are interactive.
- `tool_scripts/bloom.cfs` and `tool_scripts/qrCode.cfs` are helper or library style scripts and are not useful entry points by themselves.
- `Http_Tests/http.cfs`, `Http_Tests/framework_test.cfs`, and `Http_Tests/md_render_test.cfs` start local servers and keep running until you stop them.
- `Http_Tests/mini_web_framework.cfs` uses a tiny JavaScript fetch bridge, while `Http_Tests/mini_web_framework_cfs_only.cfs` stays on plain HTML form posts.
- `general_Tests/await.cfs` and the `/getbin` route in `Http_Tests/http.cfs` require outbound internet access.
