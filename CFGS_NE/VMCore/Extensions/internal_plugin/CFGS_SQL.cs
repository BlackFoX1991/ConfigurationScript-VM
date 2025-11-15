using CFGS_VM.VMCore.Plugin;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;

namespace CFGS_VM.VMCore.Extensions.internal_plugin
{
    /// <summary>
    /// Defines the <see cref="SqlHandle" />
    /// </summary>
    public sealed class SqlHandle : IDisposable
    {
        /// <summary>
        /// Gets the Connection
        /// </summary>
        public SqlConnection Connection { get; }

        /// <summary>
        /// Gets the Transaction
        /// </summary>
        public SqlTransaction? Transaction { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlHandle"/> class.
        /// </summary>
        /// <param name="connection">The connection<see cref="SqlConnection"/></param>
        public SqlHandle(SqlConnection connection)
        {
            Connection = connection;
        }

        /// <summary>
        /// The EnsureOpen
        /// </summary>
        public void EnsureOpen()
        {
            if (Connection == null || Connection.State != ConnectionState.Open)
                throw new InvalidOperationException("Die SQL-Verbindung ist nicht geöffnet.");
        }

        /// <summary>
        /// The BeginTransaction
        /// </summary>
        public void BeginTransaction()
        {
            EnsureOpen();
            Transaction = Connection.BeginTransaction();
        }

        /// <summary>
        /// The Commit
        /// </summary>
        public void Commit()
        {
            Transaction?.Commit();
            Transaction?.Dispose();
            Transaction = null;
        }

        /// <summary>
        /// The Rollback
        /// </summary>
        public void Rollback()
        {
            Transaction?.Rollback();
            Transaction?.Dispose();
            Transaction = null;
        }

        /// <summary>
        /// The Dispose
        /// </summary>
        public void Dispose()
        {
            try
            {
                Transaction?.Dispose();
                Connection?.Close();
                Connection?.Dispose();
            }
            catch { }
        }
    }

    /// <summary>
    /// Defines the <see cref="CFGS_STDLIB_MSSQL" />
    /// </summary>
    public sealed class CFGS_STDLIB_MSSQL : IVmPlugin
    {
        /// <summary>
        /// Gets or sets a value indicating whether AllowSql
        /// </summary>
        public static bool AllowSql { get; set; } = true;

        /// <summary>
        /// The Register
        /// </summary>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
        {
            RegisterBuiltins(builtins);
            RegisterIntrinsics(intrinsics);
        }

        /// <summary>
        /// The RegisterBuiltins
        /// </summary>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        private static void RegisterBuiltins(IBuiltinRegistry builtins)
        {
            builtins.Register(new BuiltinDescriptor("sql_connect", 1, 1, (args, instr) =>
            {
                if (!AllowSql)
                    throw new VMException("Runtime error: SQL operations are disabled (AllowSql=false)",
                        instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                if (args[0] is not string connString)
                    throw new VMException("Runtime error: sql_connect requires a string connection string",
                        instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                return Task.Run<object?>(() =>
                {
                    try
                    {
                        SqlConnection conn = new(connString);
                        conn.Open();
                        return new SqlHandle(conn);
                    }
                    catch (Exception ex)
                    {
                        throw new VMException($"SQL connect failed: {ex.Message}",
                            instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                    }
                });
            }, smartAwait: true));
        }

        /// <summary>
        /// The RegisterIntrinsics
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterIntrinsics(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(SqlHandle);

            intrinsics.Register(T, new IntrinsicDescriptor("close", 0, 0, (recv, a, i) =>
            {
                ((SqlHandle)recv)?.Dispose();
                return null!;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("is_open", 0, 0, (recv, a, i) =>
            {
                SqlHandle handle = (SqlHandle)recv;
                return handle.Connection?.State == ConnectionState.Open;
            }));

            RegisterSqlDual(intrinsics, T, "execute", async cmd =>
            {
                List<object> results = new();
                using SqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    Dictionary<string, object> row = new(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.GetValue(i) == DBNull.Value ? null! : reader.GetValue(i);
                    results.Add(row);
                }
                return results;
            });

            RegisterSqlDual(intrinsics, T, "execute_scalar", async cmd =>
            {
                object? result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return result == DBNull.Value ? null! : result!;
            });

            RegisterSqlDual(intrinsics, T, "execute_nonquery", async cmd =>
            {
                int rows = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                return (object)rows;
            });

            RegisterSqlDual(intrinsics, T, "query", async cmd =>
            {
                List<object> results = new();
                using SqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    Dictionary<string, object> row = new(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                        row[reader.GetName(i)] = reader.GetValue(i) == DBNull.Value ? null! : reader.GetValue(i);
                    results.Add(row);
                }
                return results;
            });

            intrinsics.Register(T, new IntrinsicDescriptor("begin", 0, 0, (recv, a, i) =>
            {
                ((SqlHandle)recv).BeginTransaction();
                return 1;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("commit", 0, 0, (recv, a, i) =>
            {
                ((SqlHandle)recv).Commit();
                return 1;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("rollback", 0, 0, (recv, a, i) =>
            {
                ((SqlHandle)recv).Rollback();
                return 1;
            }));

            RegisterSchemaDual(intrinsics, T);
            RegisterProcedureAndViewInfo(intrinsics, T);

            RegisterConstraintsAndFks(intrinsics, T);
        }

        /// <summary>
        /// The RegisterSqlDual
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        /// <param name="T">The T<see cref="Type"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="asyncBody">The asyncBody<see cref="Func{SqlCommand, Task{object}}"/></param>
        private static void RegisterSqlDual(IIntrinsicRegistry intrinsics, Type T, string name, Func<SqlCommand, Task<object>> asyncBody)
        {
            intrinsics.Register(T, new IntrinsicDescriptor(name + "_async", 1, 2, (recv, a, instr) =>
            {
                return RunAsyncSql(recv, a!, instr, asyncBody);
            }, smartAwait: true));

            intrinsics.Register(T, new IntrinsicDescriptor(name, 1, 2, (recv, a, instr) =>
            {
                return RunSqlSync(recv, a!, instr, cmd =>
                {
                    return asyncBody(cmd).GetAwaiter().GetResult();
                });
            }));
        }

        /// <summary>
        /// The RegisterSqlDual
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        /// <param name="T">The T<see cref="Type"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="asyncBody">The asyncBody<see cref="Func{SqlCommand, Task{object}}"/></param>
        /// <param name="allowZeroArgsForQuery">The allowZeroArgsForQuery<see cref="bool"/></param>
        private static void RegisterSqlDual(IIntrinsicRegistry intrinsics, Type T, string name, Func<SqlCommand, Task<object>> asyncBody, bool allowZeroArgsForQuery = false)
        {
            RegisterSqlDual(intrinsics, T, name, asyncBody);
        }

        /// <summary>
        /// The RegisterSchemaDual
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        /// <param name="T">The T<see cref="Type"/></param>
        private static void RegisterSchemaDual(IIntrinsicRegistry intrinsics, Type T)
        {
            intrinsics.Register(T, new IntrinsicDescriptor("tables", 0, 0, (recv, a, instr) =>
            {
                return RunSqlSync(recv, a!, instr, cmd =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();
                    List<object> list = new();
                    DataTable tables = h.Connection.GetSchema("Tables");
                    foreach (DataRow row in tables.Rows)
                        list.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = row["TABLE_NAME"],
                            ["schema"] = row["TABLE_SCHEMA"],
                            ["type"] = row["TABLE_TYPE"]
                        });
                    return list;
                });
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("tables_async", 0, 0, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();
                    List<object> list = new();
                    DataTable tables = h.Connection.GetSchema("Tables");
                    foreach (DataRow row in tables.Rows)
                        list.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = row["TABLE_NAME"],
                            ["schema"] = row["TABLE_SCHEMA"],
                            ["type"] = row["TABLE_TYPE"]
                        });
                    return (object)list;
                });
            }, smartAwait: true));

            intrinsics.Register(T, new IntrinsicDescriptor("columns", 1, 1, (recv, a, instr) =>
            {
                return RunSqlSync(recv, a!, instr, cmd =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    string table = a[0]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(table))
                        throw new VMException("columns(tableName): tableName darf nicht leer sein",
                            instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                    List<object> cols = new();
                    DataTable columns = h.Connection.GetSchema("Columns", new string[] { null!, null!, table });
                    foreach (DataRow row in columns.Rows)
                        cols.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = row["COLUMN_NAME"],
                            ["type"] = row["DATA_TYPE"],
                            ["nullable"] = row["IS_NULLABLE"],
                            ["max_length"] = row["CHARACTER_MAXIMUM_LENGTH"]
                        });
                    return cols;
                });
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("columns_async", 1, 1, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    string table = a[0]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(table))
                        throw new VMException("columns(tableName): tableName darf nicht leer sein",
                            instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                    List<object> cols = new();
                    DataTable columns = h.Connection.GetSchema("Columns", new string[] { null!, null!, table });
                    foreach (DataRow row in columns.Rows)
                        cols.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = row["COLUMN_NAME"],
                            ["type"] = row["DATA_TYPE"],
                            ["nullable"] = row["IS_NULLABLE"],
                            ["max_length"] = row["CHARACTER_MAXIMUM_LENGTH"]
                        });
                    return (object)cols;
                });
            }, smartAwait: true));

            intrinsics.Register(T, new IntrinsicDescriptor("views", 0, 0, (recv, a, instr) =>
            {
                return RunSqlSync(recv, a!, instr, cmd =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();
                    List<object> list = new();
                    DataTable dt = h.Connection.GetSchema("Views");
                    foreach (DataRow row in dt.Rows)
                        list.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = row["TABLE_NAME"],
                            ["schema"] = row["TABLE_SCHEMA"]
                        });
                    return list;
                });
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("views_async", 0, 0, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();
                    List<object> list = new();
                    DataTable dt = h.Connection.GetSchema("Views");
                    foreach (DataRow row in dt.Rows)
                        list.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = row["TABLE_NAME"],
                            ["schema"] = row["TABLE_SCHEMA"]
                        });
                    return (object)list;
                });
            }, smartAwait: true));

            intrinsics.Register(T, new IntrinsicDescriptor("procedures", 0, 0, (recv, a, instr) =>
            {
                return RunSqlSync(recv, a!, instr, cmd =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();
                    List<object> list = new();
                    DataTable procs = h.Connection.GetSchema("Procedures");
                    foreach (DataRow row in procs.Rows)
                        list.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = row["SPECIFIC_NAME"],
                            ["schema"] = row["SPECIFIC_SCHEMA"],
                            ["type"] = row["ROUTINE_TYPE"]
                        });
                    return list;
                });
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("procedures_async", 0, 0, (recv, a, instr) =>
            {
                return Task.Run<object?>(() =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();
                    List<object> list = new();
                    DataTable procs = h.Connection.GetSchema("Procedures");
                    foreach (DataRow row in procs.Rows)
                        list.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = row["SPECIFIC_NAME"],
                            ["schema"] = row["SPECIFIC_SCHEMA"],
                            ["type"] = row["ROUTINE_TYPE"]
                        });
                    return (object)list;
                });
            }, smartAwait: true));
        }

        /// <summary>
        /// The RegisterProcedureAndViewInfo
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        /// <param name="T">The T<see cref="Type"/></param>
        private static void RegisterProcedureAndViewInfo(IIntrinsicRegistry intrinsics, Type T)
        {
            intrinsics.Register(T, new IntrinsicDescriptor("procedure_info", 1, 1, (recv, a, instr) =>
            {
                return RunSqlSync(recv, a!, instr, cmd =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();

                    string proc = a[0]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(proc))
                        throw new VMException("procedure_info(procName): procName darf nicht leer sein",
                            instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                    List<object> parameters = new();
                    using SqlCommand pcmd = new(@"
                        SELECT PARAMETER_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, PARAMETER_MODE
                        FROM INFORMATION_SCHEMA.PARAMETERS
                        WHERE SPECIFIC_NAME = @proc
                        ORDER BY ORDINAL_POSITION", h.Connection);
                    pcmd.Parameters.AddWithValue("@proc", proc);
                    using SqlDataReader reader = pcmd.ExecuteReader();
                    while (reader.Read())
                    {
                        parameters.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = reader["PARAMETER_NAME"],
                            ["type"] = reader["DATA_TYPE"],
                            ["length"] = reader["CHARACTER_MAXIMUM_LENGTH"],
                            ["mode"] = reader["PARAMETER_MODE"]
                        });
                    }
                    return parameters;
                });
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("procedure_info_async", 1, 1, (recv, a, instr) =>
            {
                return Task.Run<object?>(async () =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();

                    string proc = a[0]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(proc))
                        throw new VMException("procedure_info(procName): procName darf nicht leer sein",
                            instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                    List<object> parameters = new();
                    using SqlCommand pcmd = new(@"
                        SELECT PARAMETER_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, PARAMETER_MODE
                        FROM INFORMATION_SCHEMA.PARAMETERS
                        WHERE SPECIFIC_NAME = @proc
                        ORDER BY ORDINAL_POSITION", h.Connection);
                    pcmd.Parameters.AddWithValue("@proc", proc);
                    using SqlDataReader reader = await pcmd.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        parameters.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = reader["PARAMETER_NAME"],
                            ["type"] = reader["DATA_TYPE"],
                            ["length"] = reader["CHARACTER_MAXIMUM_LENGTH"],
                            ["mode"] = reader["PARAMETER_MODE"]
                        });
                    }
                    return (object)parameters;
                });
            }, smartAwait: true));

            intrinsics.Register(T, new IntrinsicDescriptor("view_definition", 1, 1, (recv, a, instr) =>
            {
                return RunSqlSync(recv, a!, instr, cmd =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();

                    string view = a[0]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(view))
                        throw new VMException("view_definition(viewName): viewName darf nicht leer sein",
                            instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                    using SqlCommand vcmd = new(@"
                        SELECT VIEW_DEFINITION
                        FROM INFORMATION_SCHEMA.VIEWS
                        WHERE TABLE_NAME = @view", h.Connection);
                    vcmd.Parameters.AddWithValue("@view", view);
                    object def = vcmd.ExecuteScalar();
                    return def ?? "";
                });
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("view_definition_async", 1, 1, (recv, a, instr) =>
            {
                return Task.Run<object?>(async () =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();

                    string view = a[0]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(view))
                        throw new VMException("view_definition(viewName): viewName darf nicht leer sein",
                            instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                    using SqlCommand vcmd = new(@"
                        SELECT VIEW_DEFINITION
                        FROM INFORMATION_SCHEMA.VIEWS
                        WHERE TABLE_NAME = @view", h.Connection);
                    vcmd.Parameters.AddWithValue("@view", view);
                    object? def = await vcmd.ExecuteScalarAsync().ConfigureAwait(false);
                    return def ?? "";
                });
            }, smartAwait: true));
        }

        /// <summary>
        /// The RegisterConstraintsAndFks
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        /// <param name="T">The T<see cref="Type"/></param>
        private static void RegisterConstraintsAndFks(IIntrinsicRegistry intrinsics, Type T)
        {
            intrinsics.Register(T, new IntrinsicDescriptor("constraints", 1, 1, (recv, a, instr) =>
            {
                return RunSqlSync(recv, a!, instr, cmd =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();
                    string table = a[0]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(table))
                        throw new VMException("constraints(tableName): tableName darf nicht leer sein",
                            instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                    List<object> list = new();
                    using SqlCommand ccmd = new(@"
                        SELECT CONSTRAINT_NAME, CONSTRAINT_TYPE
                        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                        WHERE TABLE_NAME = @table", h.Connection);
                    ccmd.Parameters.AddWithValue("@table", table);
                    using SqlDataReader reader = ccmd.ExecuteReader();
                    while (reader.Read())
                    {
                        list.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = reader["CONSTRAINT_NAME"],
                            ["type"] = reader["CONSTRAINT_TYPE"]
                        });
                    }
                    return list;
                });
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("constraints_async", 1, 1, (recv, a, instr) =>
            {
                return Task.Run<object?>(async () =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();
                    string table = a[0]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(table))
                        throw new VMException("constraints(tableName): tableName darf nicht leer sein",
                            instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                    List<object> list = new();
                    using SqlCommand ccmd = new(@"
                        SELECT CONSTRAINT_NAME, CONSTRAINT_TYPE
                        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                        WHERE TABLE_NAME = @table", h.Connection);
                    ccmd.Parameters.AddWithValue("@table", table);
                    using SqlDataReader reader = await ccmd.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        list.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["name"] = reader["CONSTRAINT_NAME"],
                            ["type"] = reader["CONSTRAINT_TYPE"]
                        });
                    }
                    return (object)list;
                });
            }, smartAwait: true));

            intrinsics.Register(T, new IntrinsicDescriptor("foreign_keys", 1, 1, (recv, a, instr) =>
            {
                return RunSqlSync(recv, a!, instr, cmd =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();
                    string table = a[0]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(table))
                        throw new VMException("foreign_keys(tableName): tableName darf nicht leer sein",
                            instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                    List<object> list = new();
                    using SqlCommand fcmd = new(@"
                        SELECT fk.name AS fk_name,
                               tp.name AS fk_table,
                               cp.name AS fk_column,
                               tr.name AS pk_table,
                               cr.name AS pk_column
                        FROM sys.foreign_keys fk
                        JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                        JOIN sys.tables tp ON fkc.parent_object_id = tp.object_id
                        JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
                        JOIN sys.tables tr ON fkc.referenced_object_id = tr.object_id
                        JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id
                        WHERE tp.name = @table", h.Connection);
                    fcmd.Parameters.AddWithValue("@table", table);
                    using SqlDataReader reader = fcmd.ExecuteReader();
                    while (reader.Read())
                    {
                        list.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["fk_name"] = reader["fk_name"],
                            ["fk_table"] = reader["fk_table"],
                            ["fk_column"] = reader["fk_column"],
                            ["pk_table"] = reader["pk_table"],
                            ["pk_column"] = reader["pk_column"]
                        });
                    }
                    return list;
                });
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("foreign_keys_async", 1, 1, (recv, a, instr) =>
            {
                return Task.Run<object?>(async () =>
                {
                    SqlHandle h = (SqlHandle)recv;
                    h.EnsureOpen();
                    string table = a[0]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(table))
                        throw new VMException("foreign_keys(tableName): tableName darf nicht leer sein",
                            instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                    List<object> list = new();
                    using SqlCommand fcmd = new(@"
                        SELECT fk.name AS fk_name,
                               tp.name AS fk_table,
                               cp.name AS fk_column,
                               tr.name AS pk_table,
                               cr.name AS pk_column
                        FROM sys.foreign_keys fk
                        JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id
                        JOIN sys.tables tp ON fkc.parent_object_id = tp.object_id
                        JOIN sys.columns cp ON fkc.parent_object_id = cp.object_id AND fkc.parent_column_id = cp.column_id
                        JOIN sys.tables tr ON fkc.referenced_object_id = tr.object_id
                        JOIN sys.columns cr ON fkc.referenced_object_id = cr.object_id AND fkc.referenced_column_id = cr.column_id
                        WHERE tp.name = @table", h.Connection);
                    fcmd.Parameters.AddWithValue("@table", table);
                    using SqlDataReader reader = await fcmd.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        list.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["fk_name"] = reader["fk_name"],
                            ["fk_table"] = reader["fk_table"],
                            ["fk_column"] = reader["fk_column"],
                            ["pk_table"] = reader["pk_table"],
                            ["pk_column"] = reader["pk_column"]
                        });
                    }
                    return (object)list;
                });
            }, smartAwait: true));
        }

        /// <summary>
        /// The RunAsyncSql
        /// </summary>
        /// <param name="recv">The recv<see cref="object"/></param>
        /// <param name="a">The a<see cref="List{object?}"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <param name="body">The body<see cref="Func{SqlCommand, Task{object}}"/></param>
        /// <returns>The <see cref="Task{object}"/></returns>
        private static async Task<object> RunAsyncSql(object recv, List<object?> a, Instruction instr, Func<SqlCommand, Task<object>> body)
        {
            SqlHandle handle = (SqlHandle)recv;
            handle.EnsureOpen();

            string query = a.Count > 0 ? (a[0]?.ToString() ?? "") : "";
            Dictionary<string, object>? vmParams = a.Count > 1 ? a[1] as Dictionary<string, object> : null;

            try
            {
                using SqlCommand cmd = new(query, handle.Connection);
                if (handle.Transaction != null)
                    cmd.Transaction = handle.Transaction;
                if (vmParams != null)
                    AddParameters(cmd, vmParams);
                return await body(cmd).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new VMException($"SQL error ({ex.GetType().Name}): {ex.Message}",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
        }

        /// <summary>
        /// The RunSqlSync
        /// </summary>
        /// <param name="recv">The recv<see cref="object"/></param>
        /// <param name="a">The a<see cref="List{object?}"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <param name="body">The body<see cref="Func{SqlCommand, object}"/></param>
        /// <returns>The <see cref="object"/></returns>
        private static object RunSqlSync(object recv, List<object?> a, Instruction instr, Func<SqlCommand, object> body)
        {
            SqlHandle handle = (SqlHandle)recv;
            handle.EnsureOpen();

            string query = a.Count > 0 ? (a[0]?.ToString() ?? "") : "";
            Dictionary<string, object>? vmParams = a.Count > 1 ? a[1] as Dictionary<string, object> : null;

            try
            {
                using SqlCommand cmd = new(query, handle.Connection);
                if (handle.Transaction != null)
                    cmd.Transaction = handle.Transaction;
                if (vmParams != null)
                    AddParameters(cmd, vmParams);
                return body(cmd);
            }
            catch (Exception ex)
            {
                throw new VMException($"SQL error ({ex.GetType().Name}): {ex.Message}",
                    instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
            }
        }

        /// <summary>
        /// Defines the TypeWithSizeRegex
        /// </summary>
        private static readonly Regex TypeWithSizeRegex = new(@"^([a-z0-9_]+)\s*(\(\s*(\d+)\s*(,\s*\d+\s*)?\))?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// The AddParameters
        /// </summary>
        /// <param name="cmd">The cmd<see cref="SqlCommand"/></param>
        /// <param name="vmParams">The vmParams<see cref="Dictionary{string, object}"/></param>
        private static void AddParameters(SqlCommand cmd, Dictionary<string, object> vmParams)
        {
            foreach (KeyValuePair<string, object> kvp in vmParams)
            {
                string name = kvp.Key.StartsWith("@") ? kvp.Key : "@" + kvp.Key;
                object paramValue = kvp.Value ?? DBNull.Value;

                SqlParameter p = cmd.CreateParameter();
                p.ParameterName = name;

                if (paramValue is Dictionary<string, object> pdesc)
                {
                    if (pdesc.TryGetValue("value", out object? v))
                        p.Value = v ?? DBNull.Value;
                    else
                        p.Value = DBNull.Value;

                    if (pdesc.TryGetValue("type", out object? t) && t != null)
                    {
                        string typeStr = t.ToString() ?? "";
                        (SqlDbType sqlType, int? size) = ParseSqlTypeWithSize(typeStr);
                        p.SqlDbType = sqlType;
                        if (size.HasValue)
                        {
                            try { p.Size = size.Value; } catch { }
                        }
                    }
                }
                else
                {
                    if (paramValue is bool b)
                    {
                        p.Value = b ? 1 : 0;
                        p.SqlDbType = SqlDbType.Bit;
                    }
                    else
                    {
                        p.Value = paramValue;
                    }
                }

                cmd.Parameters.Add(p);
            }
        }

        /// <summary>
        /// The ParseSqlTypeWithSize
        /// </summary>
        /// <param name="type">The type<see cref="string"/></param>
        /// <returns>The <see cref="(SqlDbType, int?)"/></returns>
        private static (SqlDbType, int?) ParseSqlTypeWithSize(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return (SqlDbType.Variant, null);

            type = type.Trim().ToLowerInvariant();
            Match m = TypeWithSizeRegex.Match(type);
            string baseType = type;
            int? size = null;
            if (m.Success)
            {
                baseType = m.Groups[1].Value;
                if (m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out int s))
                    size = s;
            }

            SqlDbType sqlType = baseType switch
            {
                "bigint" => SqlDbType.BigInt,
                "binary" => SqlDbType.Binary,
                "bit" => SqlDbType.Bit,
                "char" => SqlDbType.Char,
                "date" => SqlDbType.Date,
                "datetime" => SqlDbType.DateTime,
                "datetime2" => SqlDbType.DateTime2,
                "datetimeoffset" => SqlDbType.DateTimeOffset,
                "decimal" => SqlDbType.Decimal,
                "float" => SqlDbType.Float,
                "image" => SqlDbType.Image,
                "int" => SqlDbType.Int,
                "money" => SqlDbType.Money,
                "nchar" => SqlDbType.NChar,
                "ntext" => SqlDbType.NText,
                "numeric" => SqlDbType.Decimal,
                "nvarchar" => SqlDbType.NVarChar,
                "real" => SqlDbType.Real,
                "smalldatetime" => SqlDbType.SmallDateTime,
                "smallint" => SqlDbType.SmallInt,
                "smallmoney" => SqlDbType.SmallMoney,
                "structured" => SqlDbType.Structured,
                "text" => SqlDbType.Text,
                "time" => SqlDbType.Time,
                "timestamp" => SqlDbType.Timestamp,
                "tinyint" => SqlDbType.TinyInt,
                "udt" => SqlDbType.Udt,
                "uniqueidentifier" => SqlDbType.UniqueIdentifier,
                "varbinary" => SqlDbType.VarBinary,
                "varchar" => SqlDbType.VarChar,
                "variant" => SqlDbType.Variant,
                "xml" => SqlDbType.Xml,
                "nvarchar(max)" => SqlDbType.NVarChar,
                "varchar(max)" => SqlDbType.VarChar,
                _ => SqlDbType.Variant
            };

            return (sqlType, size);
        }
    }
}
