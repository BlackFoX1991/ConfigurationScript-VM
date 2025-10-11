using CFGS_VM.VMCore.Plugin;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using static CFGS_VM.VMCore.VM;

namespace CFGS_VM.VMCore.CorePlugin
{
    /// <summary>
    /// Defines the <see cref="CFGS_STDLIB" />
    /// </summary>
    public sealed class CFGS_STDLIB : IVmPlugin
    {
        /// <summary>
        /// Gets or sets a value indicating whether AllowFileIO
        /// </summary>
        public static bool AllowFileIO { get; set; } = true;

        /// <summary>
        /// The Register
        /// </summary>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
        {
            RegisterBuiltins(builtins);
            RegisterString(intrinsics);
            RegisterArray(intrinsics);
            RegisterDict(intrinsics);
            RegisterException(intrinsics);
            RegisterFile(intrinsics);
        }

        /// <summary>
        /// The RegisterBuiltins
        /// </summary>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        private static void RegisterBuiltins(IBuiltinRegistry builtins)
        {
            builtins.Register(new BuiltinDescriptor("typeof", 1, 1, (args, instr) =>
            {
                object val = args[0];
                if (val == null) return "Null";
                if (val is bool) return "Bool";
                if (val is int) return "Int";
                if (val is long) return "Long";
                if (val is double) return "Double";
                if (val is float) return "Float";
                if (val is decimal) return "Decimal";
                if (val is string) return "String";
                if (val is char) return "Char";
                if (val is List<object>) return "Array";
                if (val is Dictionary<string, object>) return "Dictionary";
                if (val is CFGS_VM.VMCore.Extensions.ClassInstance ci) return ci.ClassName;
                if (val is CFGS_VM.VMCore.Extensions.StaticInstance si) return si.ClassName;
                if (val is Extensions.EnumInstance ei) return "Enum";
                if (val is CFGS_VM.VMCore.VM.ExceptionObject) return "Exception";
                return val.GetType().Name;
            }));

            builtins.Register(new BuiltinDescriptor("len", 1, 1, (args, instr) =>
            {
                object a0 = args[0];
                if (a0 is string s) return s.Length;
                if (a0 is List<object> list) return list.Count;
                if (a0 is Dictionary<string, object> dct) return dct.Count;
                return -1;
            }));

            builtins.Register(new BuiltinDescriptor("print", 1, 1, (args, instr) =>
            {
                PrintValue(args[0], Console.Out, escapeNewlines: false);
                Console.Out.WriteLine();
                Console.Out.Flush();
                return 1;
            }));

            builtins.Register(new BuiltinDescriptor("put", 1, 1, (args, instr) =>
            {
                PrintValue(args[0], Console.Out, escapeNewlines: false);
                Console.Out.Flush();
                return 1;
            }));

            builtins.Register(new BuiltinDescriptor("clear", 0, 0, (args, instr) =>
            {
                Console.Clear();
                return 1;
            }));

            builtins.Register(new BuiltinDescriptor("getl", 0, 0, (args, instr) =>
            {
                return Console.ReadLine() ?? "";
            }));

            builtins.Register(new BuiltinDescriptor("getc", 0, 0, (args, instr) =>
            {
                return Console.Read();
            }));

            builtins.Register(new BuiltinDescriptor("str", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return null!;
                return args[0].ToString() ?? "";
            }));

            builtins.Register(new BuiltinDescriptor("toi", 1, 1, (args, instr) => ToNumber(args[0])));
            builtins.Register(new BuiltinDescriptor("toi16", 1, 1, (args, instr) => Convert.ToInt16(args[0])));
            builtins.Register(new BuiltinDescriptor("toi32", 1, 1, (args, instr) => Convert.ToInt32(args[0])));
            builtins.Register(new BuiltinDescriptor("toi64", 1, 1, (args, instr) => Convert.ToInt64(args[0])));

            builtins.Register(new BuiltinDescriptor("abs", 1, 1, (args, instr) => Math.Abs((dynamic)args[0])));

            builtins.Register(new BuiltinDescriptor("rand", 3, 3, (args, instr) =>
            {
                return new Random((int)Convert.ToInt32(args[0]))
                    .Next(Convert.ToInt32(args[1]), Convert.ToInt32(args[2]));
            }));

            builtins.Register(new BuiltinDescriptor("isdigit", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return false;
                return char.IsDigit(Convert.ToChar(args[0], CultureInfo.InvariantCulture));
            }));
            builtins.Register(new BuiltinDescriptor("isletter", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return false;
                return char.IsLetter(Convert.ToChar(args[0], CultureInfo.InvariantCulture));
            }));
            builtins.Register(new BuiltinDescriptor("isalnum", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return false;
                return char.IsLetterOrDigit(Convert.ToChar(args[0], CultureInfo.InvariantCulture));
            }));
            builtins.Register(new BuiltinDescriptor("isspace", 1, 1, (args, instr) =>
            {
                if (args[0] is null) return false;
                return char.IsWhiteSpace(Convert.ToChar(args[0], CultureInfo.InvariantCulture));
            }));

            builtins.Register(new BuiltinDescriptor("isarray", 1, 1, (args, instr) => args[0] is List<object>));
            builtins.Register(new BuiltinDescriptor("isdict", 1, 1, (args, instr) => args[0] is Dictionary<string, object>));

            builtins.Register(new BuiltinDescriptor("getfields", 1, 1, (args, instr) =>
            {
                if (args[0] is not Dictionary<string, object> d) return new List<object>();
                return d.Keys.ToList<object>();
            }));

            builtins.Register(new BuiltinDescriptor("json", 1, 1, (args, instr) =>
            {
                return JsonStringify(args[0]);
            }));
            builtins.Register(new BuiltinDescriptor("now", 0, 1, (args, instr) =>
            {
                if (args.Count == 0) return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                return DateTime.Now.ToString(args[0].ToString() ?? "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }));
            builtins.Register(new BuiltinDescriptor("date", 0, 0, (args, instr) =>
            {
                return new DateTime();
            }));

            builtins.Register(new BuiltinDescriptor("fopen", 2, 2, (args, instr) =>
            {
                if (!AllowFileIO)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile);

                object a0 = args[0];
                object a1 = args[1];

                string path;
                int mode;

                if (a0 is string || a0 is char)
                {
                    path = a0.ToString() ?? "";
                    mode = Convert.ToInt32(a1, CultureInfo.InvariantCulture);
                }
                else if (a1 is string || a1 is char)
                {
                    path = a1.ToString() ?? "";
                    mode = Convert.ToInt32(a0, CultureInfo.InvariantCulture);
                }
                else
                {
                    throw new VMException("Runtime error: fopen needs a string path and a numeric mode",
                        instr.Line, instr.Col, instr.OriginFile);
                }

                FileMode fmode; FileAccess facc; bool canRead = false, canWrite = false;
                switch (mode)
                {
                    case 0: fmode = FileMode.Open; facc = FileAccess.Read; canRead = true; break;
                    case 1: fmode = FileMode.OpenOrCreate; facc = FileAccess.ReadWrite; canRead = true; canWrite = true; break;
                    case 2: fmode = FileMode.Create; facc = FileAccess.Write; canWrite = true; break;
                    case 3: fmode = FileMode.Append; facc = FileAccess.Write; canWrite = true; break;
                    case 4: fmode = FileMode.OpenOrCreate; facc = FileAccess.Write; canWrite = true; break;
                    default:
                        throw new VMException($"Runtime error: invalid fopen mode {mode} (0..4 expected)",
                            instr.Line, instr.Col, instr.OriginFile);
                }

                try
                {
                    FileStream fs = new(path, fmode, facc, FileShare.Read);
                    if (mode == 3) fs.Seek(0, SeekOrigin.End);

                    return (object)Activator.CreateInstance(
                        Type.GetType("CFGS_VM.VMCore.VM+FileHandle, CFGS_VM") ??
                        Type.GetType("CFGS_VM.VMCore.VM+FileHandle", throwOnError: true)!,
                        args: new object?[] { path, mode, fs, canRead, canWrite }
                    )!;
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new VMException(
                        $"Runtime error: fopen unauthorized for '{path}' (mode={mode}): {ex.GetType().Name}: {ex.Message}",
                        instr.Line, instr.Col, instr.OriginFile);
                }
                catch (DirectoryNotFoundException ex)
                {
                    throw new VMException(
                        $"Runtime error: fopen path not found '{path}' (mode={mode}): {ex.GetType().Name}: {ex.Message}",
                        instr.Line, instr.Col, instr.OriginFile);
                }
                catch (FileNotFoundException ex)
                {
                    throw new VMException(
                        $"Runtime error: fopen file not found '{path}' (mode={mode}): {ex.GetType().Name}: {ex.Message}",
                        instr.Line, instr.Col, instr.OriginFile);
                }
                catch (IOException ex)
                {
                    throw new VMException(
                        $"Runtime error: fopen I/O error for '{path}' (mode={mode}): {ex.GetType().Name}: {ex.Message}",
                        instr.Line, instr.Col, instr.OriginFile);
                }
                catch (Exception ex)
                {
                    throw new VMException(
                        $"Runtime error: fopen('{path}', {mode}) failed: {ex.GetType().Name}: {ex.Message}",
                        instr.Line, instr.Col, instr.OriginFile);
                }
            }));
        }

        /// <summary>
        /// The RegisterString
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterString(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(string);

            intrinsics.Register(T, new IntrinsicDescriptor("len", 0, 0, (recv, a, i) => ((string)(recv ?? ""))!.Length));
            intrinsics.Register(T, new IntrinsicDescriptor("contains", 1, 1, (recv, a, i) => ((string)(recv ?? ""))!.Contains(a[0].ToString()! ?? "")));
            intrinsics.Register(T, new IntrinsicDescriptor("substr", 2, 2, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                int start = ClampIndex(Convert.ToInt32(a[0]), len);
                int length = Math.Max(0, Convert.ToInt32(a[1]));
                if (start > len) start = len;
                if (start + length > len) length = len - start;
                return s.Substring(start, length);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("slice", 0, 2, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                object? startObj = a.Count > 0 ? a[0] : null;
                object? endObj = a.Count > 1 ? a[1] : null;
                (int st, int ex) = NormalizeSliceBounds(startObj, endObj, len);
                return s.Substring(st, ex - st);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("replace_range", 3, 3, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                (int st, int ex) = NormalizeSliceBounds(a[0], a[1], len);
                string repl = a[2]?.ToString() ?? "";
                return s.Substring(0, st) + repl + s.Substring(ex);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("remove_range", 2, 2, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                (int st, int ex) = NormalizeSliceBounds(a[0], a[1], len);
                return s.Substring(0, st) + s.Substring(ex);
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("replace", 2, 2, (recv, a, i) =>
            {
                string s = recv?.ToString() ?? "";
                if (a[0] is not string || a[1] is not string) return s;
                return s.Replace((string)a[0], (string)a[1]);
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("insert_at", 2, 2, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                int idx = ClampIndex(Convert.ToInt32(a[0]), len);
                string repl = a[1]?.ToString() ?? "";
                return s.Substring(0, idx) + repl + s.Substring(idx);
            }));
        }

        /// <summary>
        /// The RegisterArray
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterArray(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(List<object>);

            intrinsics.Register(T, new IntrinsicDescriptor("len", 0, 0, (recv, a, i) => ((List<object>)recv).Count));
            intrinsics.Register(T, new IntrinsicDescriptor("push", 1, 1, (recv, a, i) =>
            {
                List<object> arr = (List<object>)recv;
                arr.Add(a[0]);
                return arr.Count;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("pop", 0, 0, (recv, a, i) =>
            {
                List<object> arr = (List<object>)recv;
                if (arr.Count == 0) return null!;
                object last = arr[^1];
                arr.RemoveAt(arr.Count - 1);
                return last;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("insert_at", 2, 2, (recv, a, i) =>
            {
                List<object> arr = (List<object>)recv;
                int idx = ClampIndex(Convert.ToInt32(a[0]), arr.Count);
                arr.Insert(idx, a[1]);
                return arr;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("remove_range", 2, 2, (recv, a, i) =>
            {
                List<object> arr = (List<object>)recv;
                (int st, int ex) = NormalizeSliceBounds(a[0], a[1], arr.Count);
                arr.RemoveRange(st, ex - st);
                return arr;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("replace_range", 3, 3, (recv, a, i) =>
            {
                List<object> arr = (List<object>)recv;
                (int st, int ex) = NormalizeSliceBounds(a[0], a[1], arr.Count);
                if (a[2] is List<object> replList)
                {
                    arr.RemoveRange(st, ex - st);
                    arr.InsertRange(st, replList);
                }
                else
                {
                    arr.RemoveRange(st, ex - st);
                    arr.Insert(st, a[2]);
                }
                return arr;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("slice", 0, 2, (recv, a, instr) =>
            {
                List<object> arr = (List<object>)recv;
                object? startObj = a.Count > 0 ? a[0] : null;
                object? endObj = a.Count > 1 ? a[1] : null;
                (int st, int ex) = NormalizeSliceBounds(startObj, endObj, arr.Count);
                return arr.GetRange(st, ex - st);
            }));
        }

        /// <summary>
        /// The RegisterDict
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterDict(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(Dictionary<string, object>);
            intrinsics.Register(T, new IntrinsicDescriptor("len", 0, 0, (r, a, i) => ((Dictionary<string, object>)r).Count));
            intrinsics.Register(T, new IntrinsicDescriptor("contains", 1, 1, (r, a, i) =>
            {
                Dictionary<string, object> d = (Dictionary<string, object>)r;
                string key = a[0]?.ToString() ?? "";
                return d.ContainsKey(key);
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("remove", 1, 1, (r, a, i) =>
            {
                Dictionary<string, object> d = (Dictionary<string, object>)r;
                string key = a[0]?.ToString() ?? "";
                return d.Remove(key);
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("keys", 0, 0, (r, a, i) => ((Dictionary<string, object>)r).Keys.ToList<object>()));
            intrinsics.Register(T, new IntrinsicDescriptor("values", 0, 0, (r, a, i) => ((Dictionary<string, object>)r).Values.ToList()));
            intrinsics.Register(T, new IntrinsicDescriptor("set", 2, 2, (r, a, i) =>
            {
                Dictionary<string, object> d = (Dictionary<string, object>)r;
                string key = a[0]?.ToString() ?? "";
                d[key] = a[1];
                return d;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("get_or", 2, 2, (r, a, i) =>
            {
                Dictionary<string, object> d = (Dictionary<string, object>)r;
                string key = a[0]?.ToString() ?? "";
                return d.TryGetValue(key, out object? v) ? v : a[1];
            }));
        }

        /// <summary>
        /// The RegisterException
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterException(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(CFGS_VM.VMCore.VM.ExceptionObject);
            intrinsics.Register(T, new IntrinsicDescriptor("message", 0, 0, (r, a, i) => ((CFGS_VM.VMCore.VM.ExceptionObject)r).Message));
            intrinsics.Register(T, new IntrinsicDescriptor("type", 0, 0, (r, a, i) => ((CFGS_VM.VMCore.VM.ExceptionObject)r).Type));
            intrinsics.Register(T, new IntrinsicDescriptor("file", 0, 0, (r, a, i) => ((CFGS_VM.VMCore.VM.ExceptionObject)r).File));
            intrinsics.Register(T, new IntrinsicDescriptor("line", 0, 0, (r, a, i) => ((CFGS_VM.VMCore.VM.ExceptionObject)r).Line));
            intrinsics.Register(T, new IntrinsicDescriptor("col", 0, 0, (r, a, i) => ((CFGS_VM.VMCore.VM.ExceptionObject)r).Col));
            intrinsics.Register(T, new IntrinsicDescriptor("stack", 0, 0, (r, a, i) => ((CFGS_VM.VMCore.VM.ExceptionObject)r).Stack));
            intrinsics.Register(T, new IntrinsicDescriptor("toString", 0, 0, (r, a, i) => ((CFGS_VM.VMCore.VM.ExceptionObject)r).ToString()));
        }

        /// <summary>
        /// The RegisterFile
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterFile(IIntrinsicRegistry intrinsics)
        {
            Type T = Type.GetType("CFGS_VM.VMCore.VM+FileHandle, CFGS_VM", throwOnError: false) ??
                     Type.GetType("CFGS_VM.VMCore.VM+FileHandle");

            if (T == null)
            {
                Console.Error.WriteLine("[CFGS_STDLIB] FileHandle type not found. File intrinsics unavailable. Make VM.FileHandle public.");
                return;
            }

            intrinsics.Register(T, new IntrinsicDescriptor("write", 1, 1, (recv, a, i) =>
            {
                dynamic fh = recv; fh.Write(a[0]?.ToString() ?? ""); return recv;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("writeln", 1, 1, (recv, a, i) =>
            {
                dynamic fh = recv; fh.Writeln(a[0]?.ToString() ?? ""); return recv;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("flush", 0, 0, (recv, a, i) =>
            {
                dynamic fh = recv; fh.Flush(); return recv;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("read", 1, 1, (recv, a, i) =>
            {
                dynamic fh = recv; return (string)fh.Read(Convert.ToInt32(a[0]));
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("readline", 0, 0, (recv, a, i) =>
            {
                dynamic fh = recv; return (string)fh.ReadLine();
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("seek", 2, 2, (recv, a, i) =>
            {
                dynamic fh = recv;
                long off = Convert.ToInt64(a[0]); int org = Convert.ToInt32(a[1]);
                SeekOrigin o = org switch { 0 => SeekOrigin.Begin, 1 => SeekOrigin.Current, 2 => SeekOrigin.End, _ => SeekOrigin.Begin };
                return (long)fh.Seek(off, o);
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("tell", 0, 0, (recv, a, i) => { dynamic fh = recv; return (long)fh.Tell(); }));
            intrinsics.Register(T, new IntrinsicDescriptor("eof", 0, 0, (recv, a, i) => { dynamic fh = recv; return (bool)fh.Eof(); }));
            intrinsics.Register(T, new IntrinsicDescriptor("close", 0, 0, (recv, a, i) => { dynamic fh = recv; fh.Close(); return 1; }));
        }

        /// <summary>
        /// The ToNumber
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <returns>The <see cref="object"/></returns>
        private static object ToNumber(object? v)
        {
            if (v is null) return 0;
            if (v is int or long or short or sbyte or byte or ushort or uint or ulong) return Convert.ToInt64(v, CultureInfo.InvariantCulture);
            if (v is float or double or decimal) return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (v is bool b) return b ? 1 : 0;
            if (v is char c) return (int)c;
            if (v is string s)
            {
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long li)) return li;
                if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double dd)) return dd;
                return 0;
            }
            return 0;
        }

        /// <summary>
        /// The JsonStringify
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string JsonStringify(object? v)
        {
            JsonSerializerOptions options = new()
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                WriteIndented = false
            };

            object? Normalize(object? x)
            {
                if (x is null) return null;
                if (x is string or bool or int or long or double or float or decimal or char) return x;
                if (x is List<object> list) return list.Select(Normalize).ToList();
                if (x is Dictionary<string, object> dict)
                    return dict.ToDictionary(k => k.Key, k => Normalize(k.Value)!);
                return x.ToString();
            }

            return JsonSerializer.Serialize(Normalize(v), options);
        }

        /// <summary>
        /// The ClampIndex
        /// </summary>
        /// <param name="idx">The idx<see cref="int"/></param>
        /// <param name="len">The len<see cref="int"/></param>
        /// <returns>The <see cref="int"/></returns>
        private static int ClampIndex(int idx, int len)
        {
            if (idx < 0) idx += len;
            if (idx < 0) idx = 0;
            if (idx > len) idx = len;
            return idx;
        }

        /// <summary>
        /// The NormalizeSliceBounds
        /// </summary>
        /// <param name="startObj">The startObj<see cref="object?"/></param>
        /// <param name="endObj">The endObj<see cref="object?"/></param>
        /// <param name="len">The len<see cref="int"/></param>
        /// <returns>The <see cref="(int start, int endEx)"/></returns>
        private static (int start, int endEx) NormalizeSliceBounds(object? startObj, object? endObj, int len)
        {
            bool startIsNull = startObj == null;
            int start = startIsNull ? 0 : Convert.ToInt32(startObj);
            if (start < 0) start += len;

            int endEx;
            if (endObj == null)
            {
                endEx = len;
            }
            else
            {
                int rawEnd = Convert.ToInt32(endObj);
                if (rawEnd == 0 && startIsNull) endEx = len;
                else
                {
                    endEx = rawEnd;
                    if (endEx < 0) endEx += len;
                }
            }

            start = Math.Clamp(start, 0, len);
            endEx = Math.Clamp(endEx, 0, len);
            if (endEx < start) endEx = start;
            return (start, endEx);
        }
    }
}
