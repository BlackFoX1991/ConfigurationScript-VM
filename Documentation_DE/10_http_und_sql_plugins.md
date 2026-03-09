# HTTP und SQL Plugins verwenden

## Grundidee

Die offiziellen Zusatzplugins werden wie die Standardbibliothek per DLL Import geladen.

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/CFGS.Web.Http.dll";
import "dist/Debug/net10.0/CFGS.Microsoft.SQL.dll";
```

Danach stehen ihre Builtins und Intrinsics global zur Verfuegung.

## HTTP Plugin

### Verfuegbare Builtins

- `http_get(url, headers = optional, timeoutMs = optional)`
- `http_post(url, body, headers = optional, timeoutMs = optional)`
- `http_download(url, path, timeoutMs = optional)`
- `urlencode(text)`
- `urldecode(text)`
- `http_server(port)`

### Rueckgabeform von `http_get` und `http_post`

Die Antwort ist ein Dictionary mit diesen Keys.

- `status`
- `reason`
- `headers`
- `body`

Beispiel.

```cfs
async func ping() {
    var resp = await http_get("https://httpbin.org/get", {"User-Agent": "CFGS_HTTP/1.0"});
    print(resp.status);
    print(resp.reason);
    print(resp.body);
}
```

### Downloads

`http_download` schreibt Bytes auf die Platte und liefert die Anzahl geschriebener Bytes. Datei I O muss dabei erlaubt sein.

```cfs
var bytes = await http_download("https://example.com/file.txt", "downloads/file.txt");
print(bytes);
```

### `http_server(port)`

Dieses Builtin liefert einen `ServerHandle`.

```cfs
var srv = http_server(19081);
```

Der Handle unterstuetzt diese Intrinsics.

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

### Rueckgabeform von `poll`

`poll` und `poll_async` liefern entweder `null` oder ein Request Dictionary mit diesen Keys.

- `id`
- `method`
- `path`
- `query`
- `headers`
- `body`
- `remote`

Das ist bewusst genug Information, um einfache lokale HTTP Workflows in CFGS zu fahren.

### Minimales HTTP Server Beispiel

```cfs
import "dist/Debug/net10.0/CFGS.StandardLibrary.dll";
import "dist/Debug/net10.0/CFGS.Web.Http.dll";

async func main() {
    var srv = http_server(19081);
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

### Einstiegspunkt

Das SQL Plugin beginnt mit genau einem Builtin.

- `sql_connect(connectionString)`

Der Aufruf ist async und liefert bei Erfolg einen `SqlHandle`.

```cfs
var conn = await sql_connect("Server=127.0.0.1;Database=master;User Id=sa;Password=Secret;Encrypt=False;TrustServerCertificate=True;");
```

### `SqlHandle` Intrinsics

Der Handle unterstuetzt diese Kernmethoden.

- `close()`
- `is_open()`
- `begin()`
- `commit()`
- `rollback()`
- `execute(query, params = optional)`
- `execute_async(query, params = optional)`
- `execute_scalar(query, params = optional)`
- `execute_scalar_async(query, params = optional)`
- `execute_nonquery(query, params = optional)`
- `execute_nonquery_async(query, params = optional)`
- `query(query, params = optional)`
- `query_async(query, params = optional)`

Die Parameter werden als Dictionary uebergeben und direkt in SQL Parameter umgesetzt.

```cfs
var rows = conn.query(
    "SELECT * FROM Users WHERE Id = @id",
    {"id": 42}
);
```

### Rueckgabeformen

- `execute` und `query` liefern Listen von Dictionary Zeilen.
- `execute_scalar` liefert genau einen Wert.
- `execute_nonquery` liefert die Anzahl betroffener Zeilen.

### Schema und Metadaten

Das Plugin bringt zusaetzlich Schema Helfer mit.

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

Beispiel.

```cfs
var cols = conn.columns("Users");
foreach (var c in cols) {
    print(c["name"]);
    print(c["type"]);
}
```

### Transaktionen

```cfs
conn.begin();
conn.execute_nonquery("UPDATE Users SET Active = 1 WHERE Id = @id", {"id": 42});
conn.commit();
```

Wenn etwas schiefgeht, rollst du wie gewohnt zurueck.

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

## Fehlerverhalten

### HTTP

HTTP Fehler kommen als normale Exceptions aus dem jeweiligen Await oder Aufruf zurueck.

### SQL

`sql_connect` wirft bei fehlgeschlagenem Verbindungsaufbau einen CFGS Fehler mit einer Meldung wie `SQL connect failed: ...`.

Die eigentlichen Query Helfer kapseln Datenbankfehler in Meldungen wie `SQL error (SqlException): ...`.

## Sicherheits und Freigabeschalter

- HTTP Downloads haengen an Datei I O. Wenn Dateioperationen global deaktiviert sind, kann `http_download` nicht schreiben.
- Das SQL Plugin hat einen eigenen Schalter `AllowSql`. Ist dieser deaktiviert, lehnt `sql_connect` den Aufruf ab.

## Wann du welches Plugin nimmst

- HTTP ist gut fuer Web APIs, einfache Downloads und lokale Testserver.
- SQL ist gut fuer direkte Datenbankverwaltung, Migrationsskripte, Reports und Datenextraktion.

Wenn du eigene Builtins und Intrinsics in derselben Form bauen willst, lies als Naechstes [Plugins erstellen](11_plugins_erstellen.md).
