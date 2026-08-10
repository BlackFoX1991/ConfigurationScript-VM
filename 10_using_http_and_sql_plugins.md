# Using the HTTP and SQL Plugins

## Core Idea

The official extra plugins are loaded through DLL imports. Unlike the non-IO standard library, which is built into the runtime, they must be imported explicitly.

```cfs
import "dist/Debug/net10.0/CFGS.Web.Http.dll";
import "dist/Debug/net10.0/CFGS.Microsoft.SQL.dll";
```

After that, their namespace members and handle intrinsics are available through `http`, `sql`, `std.http`, and `std.sql`.

## HTTP Plugin

### Available Namespace Members

- `http.get(url, headers = optional, timeoutMs = optional)`
- `http.post(url, body, headers = optional, timeoutMs = optional, contentType = optional)`
- `http.put(url, body, headers = optional, timeoutMs = optional, contentType = optional)`
- `http.patch(url, body, headers = optional, timeoutMs = optional, contentType = optional)`
- `http.delete(url, headers = optional, timeoutMs = optional)`
- `http.head(url, headers = optional, timeoutMs = optional)`
- `http.download(url, path, timeoutMs = optional)`
- `http.urlencode(text)`
- `http.urldecode(text)`
- `http.server(port, maxRequestBodyBytes = optional, mode = optional)`

`http.post`, `http.put`, and `http.patch` accept an optional `contentType` parameter. The default is `text/plain`. Their `body` argument may be a string or a byte array such as `[0x00, 0xFF, 0x41]`.

### Return Shape of `http.get`, `http.post`, and Others

All HTTP request namespace members are async. The response is a dictionary with these keys.

- `status`
- `reason`
- `headers`
- `body`, decoded as text
- `body_bytes`, the raw response bytes
- `body_length`, the raw response byte count

Example.

```cfs
async func ping() {
    var resp = await http.get("https://httpbin.org/get", {"User-Agent": "CFGS_HTTP/1.0"});
    print(resp.status);
    print(resp.reason);
    print(resp.body);
}
```

### Downloads

`http.download` streams bytes to disk and returns the number of written bytes. File I O must be allowed for this to work.

```cfs
var bytes = await http.download("https://example.com/file.txt", "downloads/file.txt");
print(bytes);
```

### `http.server(port, maxRequestBodyBytes = optional, mode = optional)`

This namespace member returns a `ServerHandle`.

```cfs
var srv = http.server(19081, 10 * 1024 * 1024);
```

`maxRequestBodyBytes` defaults to `10 MiB`. Requests above this limit are marked with `body_too_large` and do not expose partial bytes.

Pass `"stream"` as the second or third argument to defer request body reads to the VM:

```cfs
var srv = http.server(19081, 10 * 1024 * 1024, "stream");
```

In stream mode the request dictionary includes `body_stream`, a handle with:

- `read_bytes(count)`
- `read_all_bytes(maxBytes = optional)`
- `read_text(maxBytes = optional, encoding = optional)`
- `copy_to(path, maxBytes = optional)`
- `bytes_read()`
- `is_consumed()`

The handle supports these intrinsics.

- `start()`
- `stop()`
- `start_async()`
- `stop_async()`
- `is_running()`
- `pending_count()`
- `poll(timeoutMs = optional)`
- `poll_async(timeoutMs = optional)`
- `respond(id, status, body, headers = optional)`
- `respond_async(id, status, body, headers = optional)`
- `close()`
- `close_async()`

The response `body` may be a string or a byte array such as `[0x89, 0x50, 0x4E, 0x47]`. Strings are written as UTF-8. Byte arrays are written as raw response bytes.

### Return Shape of `poll`

`poll` and `poll_async` return either `null` or a request dictionary with these keys.

- `id`
- `method`
- `path`
- `query`
- `headers`
- `body`, decoded as text
- `body_bytes`, the raw request bytes
- `body_length`
- `body_too_large`
- `body_bytes_truncated`
- `body_limit`
- `body_stream`, only in stream mode
- `body_streaming`
- `remote`

Use `body` for normal text, JSON, and form workflows. Use `body_bytes` when the request may contain binary data, for example multipart uploads or custom binary protocols.

### Minimal HTTP Server Example

```cfs
import "dist/Debug/net10.0/CFGS.Web.Http.dll";

async func main() {
    var srv = http.server(19081);
    await srv.start_async();

    var req = await srv.poll_async(5000);
    if (req != null) {
        await srv.respond_async(req["id"], 200, "ok", {"Content-Type": "text/plain; charset=utf-8"});
    }

    await srv.close_async();
}

var _ = await main();
```

## SQL Plugin

### Entry Point

The SQL plugin begins with exactly one namespace member.

- `sql.connect(connectionString)`

The call is async and returns a `SqlHandle` when it succeeds.

```cfs
var conn = await sql.connect("Server=127.0.0.1;Database=master;User Id=sa;Password=Secret;Encrypt=False;TrustServerCertificate=True;");
```

### `SqlHandle` Intrinsics

The handle supports these core methods.

- `close()`
- `close_async()`
- `is_open()`
- `begin()`
- `begin_async()`
- `commit()`
- `commit_async()`
- `rollback()`
- `rollback_async()`
- `execute(query, params = optional)`
- `execute_async(query, params = optional)`
- `execute_scalar(query, params = optional)`
- `execute_scalar_async(query, params = optional)`
- `execute_nonquery(query, params = optional)`
- `execute_nonquery_async(query, params = optional)`
- `query(query, params = optional)`
- `query_async(query, params = optional)`

Parameters are passed as a dictionary and mapped directly to SQL parameters.

```cfs
var rows = conn.query(
    "SELECT * FROM Users WHERE Id = @id",
    {"id": 42}
);
```

### Return Shapes

- `execute` and `query` return lists of dictionary rows.
- `execute_scalar` returns a single value.
- `execute_nonquery` returns the number of affected rows.

### Schema and Metadata

The plugin also provides schema helpers.

- `tables()`
- `tables_async()`
- `columns(table)`
- `columns_async(table)`
- `views()`
- `views_async()`
- `procedures()`
- `procedures_async()`
- `procedure_info(name)`
- `procedure_info_async(name)`
- `view_definition(name)`
- `view_definition_async(name)`
- `constraints(table)`
- `constraints_async(table)`
- `foreign_keys(table)`
- `foreign_keys_async(table)`

Example.

```cfs
var cols = conn.columns("Users");
foreach (var c in cols) {
    print(c["name"]);
    print(c["type"]);
}
```

### Transactions

```cfs
conn.begin();
conn.execute_nonquery("UPDATE Users SET Active = 1 WHERE Id = @id", {"id": 42});
conn.commit();
```

If something fails, you roll back in the usual way.

```cfs
try {
    conn.begin();
    conn.execute_nonquery("UPDATE Users SET Active = 1 WHERE Id = @id", {"id": 42});
    conn.commit();
} catch(e) {
    conn.rollback();
    throw e;
}
```

For non-blocking workflows, use the async transaction and close methods together with the async query helpers.

```cfs
try {
    await conn.begin_async();
    await conn.execute_nonquery_async("UPDATE Users SET Active = 1 WHERE Id = @id", {"id": 42});
    await conn.commit_async();
} catch(e) {
    await conn.rollback_async();
    throw e;
}

await conn.close_async();
```

## Error Behavior

### HTTP

HTTP failures come back as normal exceptions from the specific await or call site. Request bodies are limited to 10 MB.

### SQL

`sql.connect` throws a CFGS error when the connection cannot be established, with a message like `SQL connect failed: ...`.

The actual query helpers wrap database failures into sanitized error messages that do not expose internal SQL details.

## Security and Enablement Switches

- HTTP downloads depend on file I O. If file operations are globally disabled, `http.download` cannot write.
- The HTTP server automatically adds `X-Content-Type-Options: nosniff` and `X-Frame-Options: DENY` to all responses.
- The SQL plugin has its own `AllowSql` switch. If it is disabled, `sql.connect` rejects the call.

## When to Use Which Plugin

- HTTP is a good fit for web APIs, simple downloads, and small local test servers.
- SQL is a good fit for direct database administration, migration scripts, reports, and data extraction.

If you want to build your own namespace members and intrinsics in the same style, read [Creating Plugins](11_creating_plugins.md) next.
