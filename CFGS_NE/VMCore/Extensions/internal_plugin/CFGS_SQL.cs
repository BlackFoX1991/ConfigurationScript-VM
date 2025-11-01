using CFGS_VM.VMCore.Plugin;
using Microsoft.Data.SqlClient;
using System.Data;

namespace CFGS_VM.VMCore.Extensions.internal_plugin
{
    /// <summary>
    /// Defines the <see cref="SqlHandle" />
    /// </summary>
    public class SqlHandle : IDisposable
    {
        /// <summary>
        /// Gets the Connection
        /// </summary>
        public SqlConnection Connection { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlHandle"/> class.
        /// </summary>
        /// <param name="connection">The connection<see cref="SqlConnection"/></param>
        public SqlHandle(SqlConnection connection)
        {
            Connection = connection;
        }

        /// <summary>
        /// The Dispose
        /// </summary>
        public void Dispose()
        {
            try
            {
                Connection?.Close();
                Connection?.Dispose();
            }
            catch { }
        }

        /// <summary>
        /// The EnsureOpen
        /// </summary>
        public void EnsureOpen()
        {
            if (Connection == null || Connection.State != ConnectionState.Open)
            {
                throw new InvalidOperationException("Die SQL-Verbindung ist nicht geöffnet.");
            }
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
            RegisterSqlHandle(intrinsics);
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
                    throw new VMException("Runtime error: SQL operations are disabled (AllowSql=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                if (args[0] is not string connString)
                    throw new VMException("Runtime error: sql_connect requires a string connection string",
                        instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                try
                {
                    SqlConnection conn = new(connString);
                    conn.Open();
                    return new SqlHandle(conn);
                }
                catch (Exception ex)
                {
                    throw new VMException($"Runtime error: sql_connect failed: {ex.GetType().Name}: {ex.Message}",
                        instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                }
            }));
        }

        /// <summary>
        /// The RegisterSqlHandle
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterSqlHandle(IIntrinsicRegistry intrinsics)
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
                return handle?.Connection != null && handle.Connection.State == ConnectionState.Open;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("execute_nonquery", 1, 2, (recv, a, instr) =>
            {
                SqlHandle handle = (SqlHandle)recv;
                handle.EnsureOpen();

                string query = a[0]?.ToString() ?? "";
                Dictionary<string, object>? vmParams = (a.Count > 1) ? a[1] as Dictionary<string, object> : null;

                try
                {
                    using (SqlCommand cmd = new(query, handle.Connection))
                    {
                        if (vmParams != null)
                            AddParameters(cmd, vmParams);

                        return cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    throw new VMException($"Runtime error: execute_nonquery failed: {ex.GetType().Name}: {ex.Message}",
                        instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                }
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("execute_scalar", 1, 2, (recv, a, instr) =>
            {
                SqlHandle handle = (SqlHandle)recv;
                handle.EnsureOpen();

                string query = a[0]?.ToString() ?? "";
                Dictionary<string, object>? vmParams = (a.Count > 1) ? a[1] as Dictionary<string, object> : null;

                try
                {
                    using (SqlCommand cmd = new(query, handle.Connection))
                    {
                        if (vmParams != null)
                            AddParameters(cmd, vmParams);

                        object? result = cmd.ExecuteScalar();
                        return (result == DBNull.Value) ? null! : result;
                    }
                }
                catch (Exception ex)
                {
                    throw new VMException($"Runtime error: execute_scalar failed: {ex.GetType().Name}: {ex.Message}",
                        instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                }
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("execute", 1, 2, (recv, a, instr) =>
            {
                SqlHandle handle = (SqlHandle)recv;
                handle.EnsureOpen();

                string query = a[0]?.ToString() ?? "";
                Dictionary<string, object>? vmParams = (a.Count > 1) ? a[1] as Dictionary<string, object> : null;

                List<object> results = new();

                try
                {
                    using (SqlCommand cmd = new(query, handle.Connection))
                    {
                        if (vmParams != null)
                            AddParameters(cmd, vmParams);

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Dictionary<string, object> row = new();
                                for (int i = 0; i < reader.FieldCount; i++)
                                {
                                    string colName = reader.GetName(i);
                                    object colValue = reader.GetValue(i);
                                    row[colName] = (colValue == DBNull.Value) ? null! : colValue;
                                }
                                results.Add(row);
                            }
                        }
                    }
                    return results;
                }
                catch (Exception ex)
                {
                    throw new VMException($"Runtime error: execute failed: {ex.GetType().Name}: {ex.Message}",
                        instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                }
            }));
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
                object value = kvp.Value ?? DBNull.Value;

                string paramName = kvp.Key.StartsWith("@") ? kvp.Key : "@" + kvp.Key;

                cmd.Parameters.AddWithValue(paramName, value);
            }
        }
    }
}
