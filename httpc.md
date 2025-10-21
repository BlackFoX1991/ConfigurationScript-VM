# HTTP Builtins / Intrinsics

## Table of Contents

* [Quick Start](#quick-start)

  * [GET](#get)
  * [POST (JSON)](#post-json)
  * [Download](#download)
  * [URL Encode/Decode](#url-encodedeocode)
  * [Await semantics](#await-semantics)
* [API – Client Built-ins](#api--client-built-ins)

  * [`http_get(url, [headers], [timeoutMs])`](#http_geturl-headers-timeoutms)
  * [`http_post(url, body, [headers], [timeoutMs])`](#http_posturl-body-headers-timeoutms)
  * [`http_download(url, path, [timeoutMs])`](#http_downloadurl-path-timeoutms)
  * [`urlencode(text)` / `urldecode(text)`](#urlencodetext--urldecodetext)
* [API – Local HTTP Server](#api--local-http-server)

  * [`http_server(port)` → ServerHandle](#http_serverport--serverhandle)
  * [ServerHandle Intrinsics](#serverhandle-intrinsics)
  * [Request object from `poll`](#request-object-from-poll)
* [Server Examples](#server-examples)

  * [Hello World](#hello-world)
  * [Echo Endpoint (JSON + Query)](#echo-endpoint-json--query)
  * [Graceful Shutdown](#graceful-shutdown)
* [Errors, Timeouts & Notes](#errors-timeouts--notes)

---

## Quick Start

### GET

```cfgs
# If your VM requires explicit awaiting:
var resp = await http_get("https://httpbin.org/get");

# If your VM auto-resolves built-ins (SmartAwait):
# var resp = http_get("https://httpbin.org/get");

if (resp["status"] == 200) {
    print(resp["reason"]);
    print(resp["headers"]["Content-Type"]);
    print(resp["body"]);
}
```

### POST (JSON)

```cfgs
var headers = { "Content-Type": "application/json", "X-Token": "abc123" };
var payload = "{ \"hello\": \"world\" }";

var resp = await http_post("https://httpbin.org/post", payload, headers, 8000);
if (resp["status"] >= 200 && resp["status"] < 300) {
    print(resp["body"]);
} else {
    print("HTTP Error: " + resp["status"] + " " + resp["reason"]);
}
```

### Download

```cfgs
# Note: requires CFGS_STDLIB.AllowFileIO == true
var bytes = await http_download("https://example.com/logo.png", "./out/logo.png");
print("Saved bytes: " + bytes);
```

### URL Encode/Decode

```cfgs
var q = urlencode("name=Max Mustermann & city=Köln");
var url = "https://example.com/search?q=" + q;
var resp = await http_get(url);
var original = urldecode(q);
```

### Await semantics

All client built-ins (`http_get`, `http_post`, `http_download`) are **asynchronous** and return a task.

* If your VM is configured to **require explicit `await`**, you **must** write `await http_get(...)`.
* If your VM enables **SmartAwait** for built-ins, you **may** call them without `await`, and the VM will run them to completion before returning a value.

---

## API – Client Built-ins

### `http_get(url, [headers], [timeoutMs])`

* **url** `string`
* **headers** `dict<string,string>` *(optional)*
  If `Content-Type` is set on a GET, an **empty** content body is attached so the header is sent.
* **timeoutMs** `int` *(optional, default **100000**)*

**Returns**: `Task<dict>` → when awaited:

```cfgs
{
  "status": int,                   # HTTP status code
  "reason": string,                # reason phrase
  "headers": dict<string,string>,  # merged response + content headers
  "body": string                   # response body as text (UTF-8)
}
```

**Example (headers & timeout):**

```cfgs
var resp = await http_get(
    "https://api.example.com/v1/items",
    { "Accept": "application/json", "User-Agent": "CFGS-VM/1.0" },
    5000
);
if (resp["status"] == 200) {
    print(resp["body"]);
}
```

---

### `http_post(url, body, [headers], [timeoutMs])`

* **url** `string`
* **body** `string` (UTF-8). Default content type is `"text/plain"` unless overridden via `headers`.
* **headers** `dict<string,string>` *(optional)*
* **timeoutMs** `int` *(optional, default **100000**)*

**Returns**: `Task<dict>` (same shape as `http_get` result).

**Example (JSON POST):**

```cfgs
var payload = "{ \"name\": \"Ada\", \"age\": 42 }";
var headers = { "Content-Type": "application/json" };

var resp = await http_post("https://httpbin.org/post", payload, headers);
print(resp["status"]);
print(resp["body"]);
```

---

### `http_download(url, path, [timeoutMs])`

* **url** `string`
* **path** `string` (relative/absolute). Missing directories are created.
* **timeoutMs** `int` *(optional, default **100000**)*

**Security**: Honors `CFGS_STDLIB.AllowFileIO`. If `false`, throws a `VMException`:
`Runtime error: file I/O is disabled (AllowFileIO=false)`

**Returns**: `Task<int64>` – number of bytes written.

**Example:**

```cfgs
var n = await http_download("https://example.com/file.bin", "./data/file.bin", 15000);
print("OK: " + n + " bytes");
```

---

### `urlencode(text)` / `urldecode(text)`

Wrapper around `WebUtility.UrlEncode/Decode`.

```cfgs
var v = "a+b c";
var enc = urlencode(v);   # "a%2Bb%20c"
var dec = urldecode(enc); # "a+b c"
```

---

## API – Local HTTP Server

### `http_server(port)` → `ServerHandle`

Creates a local listener on `http://localhost:{port}/` (backed by `HttpListener`).

```cfgs
var srv = http_server(8080);
srv.start();
```

> **Note:** Binding is restricted to `localhost`. Requests from other machines are not accepted.

### ServerHandle Intrinsics

* `srv.start()` → Start listener (idempotent).
* `srv.stop()` → Stop accepting new requests (can be started again).
* `srv.close()` → Fully close & dispose resources.
* `srv.is_running()` → `bool`
* `srv.pending_count()` → `int` (queued, unpolled requests)
* `srv.poll([timeoutMs])` → `dict | null` (dequeues next request; optional wait)
* `srv.respond(id, status, body, [headers])` → `int` (`1` if responded, `0` if `id` unknown/expired)

### Request object from `poll`

```cfgs
{
  "id": string,                  # pass to respond()
  "method": string,              # "GET", "POST", ...
  "path": string,                # e.g. "/hello"
  "query": dict<string,string>,  # decoded query params
  "headers": dict<string,string>,
  "body": string,                # request body (UTF-8)
  "remote": string               # e.g. "127.0.0.1:54321"
}
```

---

## Server Examples

### Hello World

```cfgs
var srv = http_server(8080);
srv.start();

while (srv.is_running()) {
    var req = srv.poll(100);   # wait up to 100 ms
    if (req == null) { continue; }

    srv.respond(req["id"], 200, "Hello from CFGS_HTTP!", { "Content-Type": "text/plain" });
}
```

### Echo Endpoint (JSON + Query)

```cfgs
var srv = http_server(8080);
srv.start();

while (true) {
    var req = srv.poll(250);
    if (req == null) { continue; }

    if (req["path"] == "/echo" && req["method"] == "POST") {
        var who = req["query"]["name"];   # /echo?name=Ada
        var echo = req["body"];           # raw text/JSON
        var json = "{ \"you\": \"" + who + "\", \"echo\": " + echo + " }";
        srv.respond(req["id"], 200, json, { "Content-Type": "application/json" });
    } else {
        srv.respond(req["id"], 404, "Not Found", { "Content-Type": "text/plain" });
    }
}
```

### Graceful Shutdown

```cfgs
var srv = http_server(9090);
srv.start();

var running = true;
while (running) {
    var req = srv.poll(200);
    if (req == null) { continue; }

    if (req["path"] == "/stop") {
        srv.respond(req["id"], 200, "Bye!", { "Content-Type": "text/plain" });
        srv.stop();
        running = false;
    } else {
        srv.respond(req["id"], 200, "OK", { "Content-Type": "text/plain" });
    }
}
srv.close();
```

---

## Errors, Timeouts & Notes

* **Async client ops:** `http_get`, `http_post`, and `http_download` run with a per-call `CancellationTokenSource(timeoutMs)`. Await them (or rely on SmartAwait if enabled).
* **Header application (client):** Headers go to `req.Headers`; if not accepted there, they’re applied to `req.Content.Headers` (creating an empty content as needed). Setting `Content-Type` on `http_get` attaches an empty body so the header is sent.
* **Response headers (client):** `resp.Headers` and `resp.Content.Headers` are merged into a case-insensitive dictionary.
* **Server IDs:** `respond(id, ...)` returns `0` if the request id is unknown (already answered/expired).
* **File I/O:** `http_download` is allowed only if `CFGS_STDLIB.AllowFileIO == true`; otherwise a `VMException` is thrown.
* **Port validation:** `http_server(port)` accepts only `1..65535` and binds `http://localhost:{port}/`.

---

[Back](README.md)
