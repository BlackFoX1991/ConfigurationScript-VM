# SQL Builtins / Intrinsics
---

## Quick Start

```c
func main()
{
    // Asynchronous connection
    var sql = await sql_connect("Server=localhost;Database=CLSY_BASE;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;");

    if (sql.is_open())
    {
        print("Connection established");

        // Blocking query
        var rows = sql.execute("SELECT TOP 5 * FROM dbo.Kunde");
        for (var row in rows)
            print(row["Vorname"] + " " + row["Nachname"]);

        // Asynchronous query
        var count = await sql.execute_async("SELECT COUNT(*) AS Anzahl FROM dbo.Kunde");
        print("Total customers: " + count[0]["Anzahl"]);

        sql.close();
    }
}
main();
```

---

## Connection Built-ins

| Builtin                          | Arguments | Description                                                         | Notes                                   |
| -------------------------------- | --------- | ------------------------------------------------------------------- | --------------------------------------- |
| `sql_connect(connection_string)` | 1         | Opens a connection to a SQL Server database. Returns a `SqlHandle`. | Supports `await` for async connections. |

### SqlHandle Intrinsics

| Intrinsic    | Arguments | Description                                    |
| ------------ | --------- | ---------------------------------------------- |
| `is_open()`  | 0         | Returns `true` if the connection is open.      |
| `close()`    | 0         | Closes the connection and disposes the handle. |
| `begin()`    | 0         | Starts a transaction.                          |
| `commit()`   | 0         | Commits the current transaction.               |
| `rollback()` | 0         | Rolls back the current transaction.            |

---

## Query Execution

All query intrinsics have **blocking** and **async** variants. Async variants must be called with `await`.

| Intrinsic                                    | Arguments                     | Description                                                                                          | Return Type                                 |
| -------------------------------------------- | ----------------------------- | ---------------------------------------------------------------------------------------------------- | ------------------------------------------- |
| `execute(query, parameters?)`                | 1–2                           | Executes a query and returns the result set as a list of dictionaries (`List<Dict<string,object>>`). | List<Dictionary<string,object>>             |
| `execute_async(query, parameters?)`          | 1–2                           | Same as `execute`, but non-blocking.                                                                 | `await`able List<Dictionary<string,object>> |
| `execute_scalar(query, parameters?)`         | 1–2                           | Executes a query and returns the first column of the first row.                                      | object                                      |
| `execute_scalar_async(query, parameters?)`   | 1–2                           | Async variant of `execute_scalar`.                                                                   | object                                      |
| `execute_nonquery(query, parameters?)`       | 1–2                           | Executes an insert/update/delete. Returns affected rows count.                                       | int                                         |
| `execute_nonquery_async(query, parameters?)` | 1–2                           | Async variant of `execute_nonquery`.                                                                 | int                                         |
| `query(...)`                                 | alias of `execute(...)`       |                                                                                                      |                                             |
| `query_async(...)`                           | alias of `execute_async(...)` |                                                                                                      |                                             |

### Parameters

```c
var param = {
    "id": { "value": 1, "type": "int" },
    "name": { "value": "Max", "type": "nvarchar(50)" }
};

sql.execute("SELECT * FROM Kunde WHERE Id=@id AND Vorname=@name", param);
```

Supported SQL types:

```
bigint, binary, bit, char, date, datetime, datetime2, datetimeoffset, decimal,
float, image, int, money, nchar, ntext, numeric, nvarchar, real,
smalldatetime, smallint, smallmoney, structured, text, time, timestamp,
tinyint, udt, uniqueidentifier, varbinary, varchar, variant, xml
```

---

## Schema Introspection

### Tables

| Intrinsic        | Arguments | Description                                                                |           |
| ---------------- | --------- | -------------------------------------------------------------------------- | --------- |
| `tables()`       | 0         | Returns all tables: `[{"name": "...", "schema": "...", "type": "BASE TABLE | VIEW"}]`. |
| `tables_async()` | 0         | Async variant of `tables()`.                                               |           |

### Columns

| Intrinsic                   | Arguments | Description                                                                    |                            |
| --------------------------- | --------- | ------------------------------------------------------------------------------ | -------------------------- |
| `columns(table_name)`       | 1         | Returns columns for a table: `[{"name": "...", "type": "...", "nullable": "YES | NO", "max_length": int}]`. |
| `columns_async(table_name)` | 1         | Async variant of `columns()`.                                                  |                            |

### Views

| Intrinsic                          | Arguments | Description                                              |
| ---------------------------------- | --------- | -------------------------------------------------------- |
| `views()`                          | 0         | Returns all views: `[{"name": "...", "schema": "..."}]`. |
| `views_async()`                    | 0         | Async variant of `views()`.                              |
| `view_definition(view_name)`       | 1         | Returns the SQL text of a view.                          |
| `view_definition_async(view_name)` | 1         | Async variant.                                           |

### Procedures

| Intrinsic                         | Arguments | Description                                                                                     |          |
| --------------------------------- | --------- | ----------------------------------------------------------------------------------------------- | -------- |
| `procedures()`                    | 0         | Lists all stored procedures: `[{"name": "...", "schema": "...", "type": "..."}]`.               |          |
| `procedures_async()`              | 0         | Async variant.                                                                                  |          |
| `procedure_info(proc_name)`       | 1         | Returns parameters of a procedure: `[{"name": "@...", "type": "...", "length": ..., "mode": "IN | OUT"}]`. |
| `procedure_info_async(proc_name)` | 1         | Async variant.                                                                                  |          |

### Constraints & Foreign Keys

| Intrinsic                        | Arguments | Description                                                                                                          |             |             |
| -------------------------------- | --------- | -------------------------------------------------------------------------------------------------------------------- | ----------- | ----------- |
| `constraints(table_name)`        | 1         | Lists table constraints: `[{"name": "...", "type": "PRIMARY KEY                                                      | FOREIGN KEY | UNIQUE"}]`. |
| `constraints_async(table_name)`  | 1         | Async variant.                                                                                                       |             |             |
| `foreign_keys(table_name)`       | 1         | Lists foreign keys: `[{"fk_name":"...", "fk_table":"...", "fk_column":"...", "pk_table":"...", "pk_column":"..."}]`. |             |             |
| `foreign_keys_async(table_name)` | 1         | Async variant.                                                                                                       |             |             |

---

## Transactions

```c
sql.begin();
sql.execute("INSERT INTO LogEintrag (Nachricht) VALUES ('test')");
sql.commit(); // or sql.rollback();
```

* `begin()` — start transaction
* `commit()` — commit transaction
* `rollback()` — rollback transaction

---

## Async / Await Semantics

* Async intrinsics: `*_async`
* Must be called with `await` in your VM
* Returns a Task-like object compatible with your `smartAwait` system

```c
var count = await sql.execute_async("SELECT COUNT(*) AS Anzahl FROM Kunde");
print(count[0]["Anzahl"]);
```

---

## Examples

### Blocking Query

```c
var rows = sql.execute("SELECT TOP 10 * FROM Kunde");
for (var row in rows)
    print(row["Vorname"] + " " + row["Nachname"]);
```

### Async Query with Parameters

```c
var param = { "id": { "value": 1, "type": "int" } };
var result = await sql.execute_async("SELECT * FROM Kunde WHERE Id=@id", param);
print(result[0]["Vorname"]);
```

### Schema Inspection

```c
for (var t in sql.tables()) print(t["name"]);
for (var c in sql.columns("Kunde")) print(c["name"] + " " + c["type"]);
```

### Transaction

```c
sql.begin();
sql.execute("INSERT INTO LogEintrag(Nachricht) VALUES('Test')");
sql.rollback();
```

---

## Return Types Appendix

| Intrinsic                                                                 | Return Type                                 | Notes                                                      |
| ------------------------------------------------------------------------- | ------------------------------------------- | ---------------------------------------------------------- |
| `execute`, `query`                                                        | List<Dictionary<string,object>>             | Each row is a dictionary keyed by column names.            |
| `execute_async`, `query_async`                                            | `await`able List<Dictionary<string,object>> | Async variant.                                             |
| `execute_scalar`, `execute_scalar_async`                                  | object                                      | First column of first row, null if empty.                  |
| `execute_nonquery`, `execute_nonquery_async`                              | int                                         | Number of affected rows.                                   |
| `tables`, `columns`, `views`, `procedures`, `constraints`, `foreign_keys` | List<Dictionary<string,object>>             | Dictionaries with field-specific keys as documented above. |


[Back](README.md)
