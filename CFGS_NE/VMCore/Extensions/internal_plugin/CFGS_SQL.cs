using CFGS_VM.VMCore.Plugin;
using Microsoft.Data.SqlClient;
using System.Data;

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

            RegisterSqlDual(T, intrinsics, "execute", async cmd =>
            {
                List<object> results = new();
                using SqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    Dictionary<string, object> row = new(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        object value = reader.GetValue(i);
                        row[reader.GetName(i)] = value == DBNull.Value ? null! : value;
                    }
                    results.Add(row);
                }
                return results;
            });

            RegisterSqlDual(T, intrinsics, "execute_scalar", async cmd =>
            {
                object? result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return result == DBNull.Value ? null! : result!;
            });

            RegisterSqlDual(T, intrinsics, "execute_nonquery", async cmd =>
            {
                int rows = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                return (object)rows;
            });

            intrinsics.Register(T, new IntrinsicDescriptor("query", 1, 2, (recv, a, instr) =>
            {
                return RunAsyncSql(recv, a, instr, async cmd =>
                {
                    List<object> results = new();
                    using SqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        Dictionary<string, object> row = new(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            object value = reader.GetValue(i);
                            row[reader.GetName(i)] = value == DBNull.Value ? null! : value;
                        }
                        results.Add(row);
                    }
                    return results;
                });
            }, smartAwait: true));

            intrinsics.Register(T, new IntrinsicDescriptor("query_sync", 1, 2, (recv, a, instr) =>
            {
                return RunSqlSync(recv, a, instr, cmd =>
                {
                    List<object> results = new();
                    using SqlDataReader reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        Dictionary<string, object> row = new(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            object value = reader.GetValue(i);
                            row[reader.GetName(i)] = value == DBNull.Value ? null! : value;
                        }
                        results.Add(row);
                    }
                    return results;
                });
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("begin", 0, 0, (recv, a, i) =>
            {
                SqlHandle h = (SqlHandle)recv;
                h.BeginTransaction();
                return 1;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("commit", 0, 0, (recv, a, i) =>
            {
                SqlHandle h = (SqlHandle)recv;
                h.Commit();
                return 1;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("rollback", 0, 0, (recv, a, i) =>
            {
                SqlHandle h = (SqlHandle)recv;
                h.Rollback();
                return 1;
            }));
        }

        /// <summary>
        /// The RegisterSqlDual
        /// </summary>
        /// <param name="T">The T<see cref="Type"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="asyncBody">The asyncBody<see cref="Func{SqlCommand, Task{object}}"/></param>
        private static void RegisterSqlDual(Type T, IIntrinsicRegistry intrinsics, string name, Func<SqlCommand, Task<object>> asyncBody)
        {
            intrinsics.Register(T, new IntrinsicDescriptor(name, 1, 2, (recv, a, instr) =>
            {
                return RunAsyncSql(recv, a, instr, asyncBody);
            }, smartAwait: true));

            intrinsics.Register(T, new IntrinsicDescriptor($"{name}_sync", 1, 2, (recv, a, instr) =>
            {
                return RunSqlSync(recv, a, instr, cmd =>
                {
                    return asyncBody(cmd).GetAwaiter().GetResult();
                });
            }));
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

            string query = a[0]?.ToString() ?? "";
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

            string query = a[0]?.ToString() ?? "";
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
        /// The AddParameters
        /// </summary>
        /// <param name="cmd">The cmd<see cref="SqlCommand"/></param>
        /// <param name="vmParams">The vmParams<see cref="Dictionary{string, object}"/></param>
        private static void AddParameters(SqlCommand cmd, Dictionary<string, object> vmParams)
        {
            foreach (KeyValuePair<string, object> kvp in vmParams)
            {
                string name = kvp.Key.StartsWith("@") ? kvp.Key : "@" + kvp.Key;
                object value = kvp.Value ?? DBNull.Value;
                if (value is bool b)
                    value = b ? 1 : 0;

                cmd.Parameters.AddWithValue(name, value);
            }
        }
    }
}
