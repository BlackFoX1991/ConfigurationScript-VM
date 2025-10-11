# CFGS_HTTP – HTTP Plugin for the CFGS VM


## Table of Contents
- [Quick Start](#quick-start)
  - [GET](#get)
  - [POST (JSON)](#post-json)
  - [Download](#download)
  - [URL Encode/Decode](#url-encodedeocode)
- [API – Client Built-ins](#api--client-built-ins)
  - [`http_get(url, [headers], [timeoutMs])`](#http_geturl-headers-timeoutms)
  - [`http_post(url, body, [headers], [timeoutMs])`](#http_posturl-body-headers-timeoutms)
  - [`http_download(url, path, [timeoutMs])`](#http_downloadurl-path-timeoutms)
  - [`urlencode(text)` / `urldecode(text)`](#urlencodetext--urldecodetext)
- [API – Local HTTP Server](#api--local-http-server)
  - [`http_server(port)` → ServerHandle](#http_serverport--serverhandle)
  - [ServerHandle Intrinsics](#serverhandle-intrinsics)
  - [Request object from `poll`](#request-object-from-poll)
- [Server Examples](#server-examples)
  - [Hello World](#hello-world)
  - [Echo Endpoint (JSON + Query)](#echo-endpoint-json--query)
  - [Graceful Shutdown](#graceful-shutdown)
- [Errors, Timeouts & Notes](#errors-timeouts--notes)

---

## Quick Start

### GET
```cfgs
var resp = http_get("https://httpbin.org/get");
if (resp["status"] == 200) {
    # Access reason, headers, body
    print(resp["reason"]);
    print(resp["headers"]["Content-Type"]);
    print(resp["body"]);
}
```

### POST (JSON)
```cfgs
var headers = { "Content-Type": "application/json", "X-Token": "abc123" };
var payload = "{ \"hello\": \"world\" }";

var resp = http_post("https://httpbin.org/post", payload, headers, 8000);
if (resp["status"] >= 200 && resp["status"] < 300) {
    print(resp["body"]);
} else {
    print("HTTP Error: " + resp["status"] + " " + resp["reason"]);
}
```

### Download
```cfgs
# Note: requires CFGS_STDLIB.AllowFileIO == true
var bytes = http_download("https://example.com/logo.png", "./out/logo.png");
print("Saved bytes: " + bytes);
```

### URL Encode/Decode
```cfgs
var q = urlencode("name=Max Mustermann & city=Köln");
var url = "https://example.com/search?q=" + q;
var resp = http_get(url);
var original = urldecode(q);
```

---

## API – Client Built-ins

### `http_get(url, [headers], [timeoutMs])`

- **url** `string`
- **headers** `dict<string,string>` *(optional)*  
  If `Content-Type` is specified on a GET, an **empty** content body is attached so the header is actually sent.
- **timeoutMs** `int` *(optional, default **100000**)*

**Returns**: `dict`
```cfgs
{
  "status": int,                   # HTTP status code
  "reason": string,                # reason phrase
  "headers": dict<string,string>,  # merged response + content headers
  "body": string                   # response body as UTF-8 text
}
```

**Example (headers & timeout):**
```cfgs
var resp = http_get(
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

- **url** `string`
- **body** `string` (UTF-8). Default content type is `"text/plain"` unless overridden via `headers`.
- **headers** `dict<string,string>` *(optional)*
- **timeoutMs** `int` *(optional, default **100000**)*

**Returns**: same as `http_get`.

**Example (JSON POST):**
```cfgs
var payload = "{ \"name\": \"Ada\", \"age\": 42 }";
var headers = { "Content-Type": "application/json" };

var resp = http_post("https://httpbin.org/post", payload, headers);
print(resp["status"]);
print(resp["body"]);
```

---

### `http_download(url, path, [timeoutMs])`

- **url** `string`
- **path** `string` (relative/absolute). Missing directories are created.
- **timeoutMs** `int` *(optional, default **100000**)*

**Security**: Honors `CFGS_STDLIB.AllowFileIO`. If `false`, it throws a `VMException`:  
`Runtime error: file I/O is disabled (AllowFileIO=false)`

**Returns**: `int64` – number of bytes written to disk.

**Example:**
```cfgs
var n = http_download("https://example.com/file.bin", "./data/file.bin", 15000);
print("OK: " + n + " bytes");
```

---

### `urlencode(text)` / `urldecode(text)`

Light wrappers around `WebUtility.UrlEncode/Decode`.

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

> **Note:** Binding is restricted to `localhost`. Requests from other machines are **not** accepted.

### ServerHandle Intrinsics

- `srv.start()` → Start listener (idempotent).
- `srv.stop()` → Stop accepting new requests (can be started again).
- `srv.close()` → Fully close & dispose resources.
- `srv.is_running()` → `bool`
- `srv.pending_count()` → `int` (queue length of unpolled requests)
- `srv.poll([timeoutMs])` → `dict | null` (next pending request; optional wait)
- `srv.respond(id, status, body, [headers])` → `int` (`1` on success, `0` if `id` unknown/expired)

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

- **Timeouts (client):** All client ops (`http_get`, `http_post`, `http_download`) accept `timeoutMs`. Each call creates a `CancellationTokenSource` with that value.
- **Header application (client):** Headers are applied to `req.Headers`; if that fails, to `req.Content.Headers` (content is created on demand). If you set `Content-Type` on `http_get`, an **empty** content is attached so the header is sent.
- **Response headers (client):** `resp.Headers` and `resp.Content.Headers` are merged into a case-insensitive dictionary.
- **Server IDs:** `respond(id, ...)` returns `0` if the request id is unknown (already answered/expired).
- **File I/O:** `http_download` is allowed only if `CFGS_STDLIB.AllowFileIO == true`; otherwise a `VMException` is thrown.
- **Port validation:** `http_server(port)` accepts only `1..65535` and always binds `http://localhost:{port}/`.

---

[Back](README.md)
