using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extension;
using CFGS_VM.VMCore.Extention;
using System.Globalization;
using System.Text;

namespace CFGS_VM.VMCore
{
    /// <summary>
    /// Defines the <see cref="VM" />
    /// </summary>
    public class VM
    {
        /// <summary>
        /// Defines the _program
        /// </summary>
        private List<Instruction>? _program;

        /// <summary>
        /// Defines the StepResult
        /// </summary>
        private enum StepResult
        {
            /// <summary>
            /// Defines the Next
            /// </summary>
            Next,

            /// <summary>
            /// Defines the Continue
            /// </summary>
            Continue,

            /// <summary>
            /// Defines the Halt
            /// </summary>
            Halt
        }

        /// <summary>
        /// The IntrinsicInvoker
        /// </summary>
        /// <param name="receiver">The receiver<see cref="object"/></param>
        /// <param name="args">The args<see cref="List{object}"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="object"/></returns>
        private delegate object IntrinsicInvoker(object receiver, List<object> args, Instruction instr);

        /// <summary>
        /// Defines the <see cref="IntrinsicMethod" />
        /// </summary>
        private sealed class IntrinsicMethod
        {
            /// <summary>
            /// Gets the Name
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Gets the ArityMin
            /// </summary>
            public int ArityMin { get; }

            /// <summary>
            /// Gets the ArityMax
            /// </summary>
            public int ArityMax { get; }

            /// <summary>
            /// Gets the Invoke
            /// </summary>
            public IntrinsicInvoker Invoke { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="IntrinsicMethod"/> class.
            /// </summary>
            /// <param name="name">The name<see cref="string"/></param>
            /// <param name="arityMin">The arityMin<see cref="int"/></param>
            /// <param name="arityMax">The arityMax<see cref="int"/></param>
            /// <param name="invoke">The invoke<see cref="IntrinsicInvoker"/></param>
            public IntrinsicMethod(string name, int arityMin, int arityMax, IntrinsicInvoker invoke)
            {
                Name = name; ArityMin = arityMin; ArityMax = arityMax; Invoke = invoke;
            }

            /// <summary>
            /// The ToString
            /// </summary>
            /// <returns>The <see cref="string"/></returns>
            public override string ToString() => $"<intrinsic {Name}>";
        }

        /// <summary>
        /// Defines the <see cref="IntrinsicBound" />
        /// </summary>
        private sealed class IntrinsicBound
        {
            /// <summary>
            /// Gets the Method
            /// </summary>
            public IntrinsicMethod Method { get; }

            /// <summary>
            /// Gets the Receiver
            /// </summary>
            public object Receiver { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="IntrinsicBound"/> class.
            /// </summary>
            /// <param name="m">The m<see cref="IntrinsicMethod"/></param>
            /// <param name="recv">The recv<see cref="object"/></param>
            public IntrinsicBound(IntrinsicMethod m, object recv)
            {
                Method = m; Receiver = recv;
            }

            /// <summary>
            /// The ToString
            /// </summary>
            /// <returns>The <see cref="string"/></returns>
            public override string ToString() => $"<bound {Method.Name}>";
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
        /// Defines the StringProto
        /// </summary>
        private static readonly Dictionary<string, IntrinsicMethod> StringProto = new(StringComparer.Ordinal)
        {
            ["substr"] = new IntrinsicMethod("substr", 2, 2, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                int start = ClampIndex(Convert.ToInt32(a[0]), len);
                int length = Math.Max(0, Convert.ToInt32(a[1]));
                if (start > len) start = len;
                if (start + length > len) length = len - start;
                return s.Substring(start, length);
            }),

            ["slice"] = new IntrinsicMethod("slice", 0, 2, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                object? startObj = a.Count > 0 ? a[0] : null;
                object? endObj = a.Count > 1 ? a[1] : null;
                var (st, ex) = NormalizeSliceBounds(startObj, endObj, len, instr);
                return s.Substring(st, ex - st);
            }),

            ["replace_range"] = new IntrinsicMethod("replace_range", 3, 3, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                var (st, ex) = NormalizeSliceBounds(a[0], a[1], len, instr);
                string repl = a[2]?.ToString() ?? "";
                return s.Substring(0, st) + repl + s.Substring(ex);
            }),

            ["remove_range"] = new IntrinsicMethod("remove_range", 2, 2, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                var (st, ex) = NormalizeSliceBounds(a[0], a[1], len, instr);
                return s.Substring(0, st) + s.Substring(ex);
            }),

            ["insert_at"] = new IntrinsicMethod("insert_at", 2, 2, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                int len = s.Length;
                int idx = ClampIndex(Convert.ToInt32(a[0]), len);
                string repl = a[1]?.ToString() ?? "";
                return s.Substring(0, idx) + repl + s.Substring(idx);
            }),

            ["len"] = new IntrinsicMethod("len", 0, 0, (recv, a, instr) =>
            {
                string s = recv?.ToString() ?? "";
                return s.Length;
            }),
        };

        private sealed class FileHandle : IDisposable
        {
            public string Path { get; }
            public int Mode { get; }
            private FileStream? _fs;
            private StreamReader? _reader;
            private StreamWriter? _writer;

            public bool IsOpen => _fs != null;

            public FileHandle(string path, int mode, FileStream fs, bool canRead, bool canWrite)
            {
                Path = path;
                Mode = mode;
                _fs = fs;
                if (canRead)
                    _reader = new StreamReader(_fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
                if (canWrite)
                    _writer = new StreamWriter(_fs, Encoding.UTF8, bufferSize: 1024, leaveOpen: true) { AutoFlush = false };
            }

            public override string ToString() => _fs == null ? $"<file closed '{Path}'>" : $"<file '{Path}'>";

            public void Dispose()
            {
                try { _writer?.Flush(); } catch { }
                _writer?.Dispose(); _reader?.Dispose(); _fs?.Dispose();
                _writer = null; _reader = null; _fs = null;
                GC.SuppressFinalize(this);
            }
            ~FileHandle() { Dispose(); }

            private FileStream FS => _fs ?? throw new ObjectDisposedException(nameof(FileHandle));
            private StreamWriter Writer => _writer ?? throw new InvalidOperationException("file not opened for writing");
            private StreamReader Reader => _reader ?? throw new InvalidOperationException("file not opened for reading");

            public void Write(string s) { Writer.Write(s); }
            public void Writeln(string s) { Writer.WriteLine(s); }
            public string Read(int count)
            {
                if (count <= 0) return string.Empty;
                char[] buf = new char[count];
                int read = Reader.Read(buf, 0, count);
                return new string(buf, 0, read);
            }
            public string ReadLine() => Reader.ReadLine() ?? "";
            public void Flush() => Writer.Flush();
            public long Tell() => FS.Position;
            public bool Eof() => Reader.EndOfStream && FS.Position >= FS.Length;
            public long Seek(long offset, SeekOrigin origin) => FS.Seek(offset, origin);
            public void Close() => Dispose();
        }
        /// <summary>
        /// Defines the FileProto
        /// </summary>
        private static readonly Dictionary<string, IntrinsicMethod> FileProto = new(StringComparer.Ordinal)
        {
            ["write"] = new IntrinsicMethod("write", 1, 1, (recv, a, instr) => { var fh = (FileHandle)recv; fh.Write(a[0]?.ToString() ?? ""); return fh; }),
            ["writeln"] = new IntrinsicMethod("writeln", 1, 1, (recv, a, instr) => { var fh = (FileHandle)recv; fh.Writeln(a[0]?.ToString() ?? ""); return fh; }),
            ["flush"] = new IntrinsicMethod("flush", 0, 0, (recv, a, instr) => { var fh = (FileHandle)recv; fh.Flush(); return fh; }),
            ["read"] = new IntrinsicMethod("read", 1, 1, (recv, a, instr) => { var fh = (FileHandle)recv; return fh.Read(Convert.ToInt32(a[0])); }),
            ["readline"] = new IntrinsicMethod("readline", 0, 0, (recv, a, instr) => { var fh = (FileHandle)recv; return fh.ReadLine(); }),
            ["seek"] = new IntrinsicMethod("seek", 2, 2, (recv, a, instr) =>
            {
                var fh = (FileHandle)recv; long off = Convert.ToInt64(a[0]); int org = Convert.ToInt32(a[1]);
                var o = org switch { 0 => SeekOrigin.Begin, 1 => SeekOrigin.Current, 2 => SeekOrigin.End, _ => SeekOrigin.Begin };
                return fh.Seek(off, o);
            }),
            ["tell"] = new IntrinsicMethod("tell", 0, 0, (recv, a, instr) => { var fh = (FileHandle)recv; return fh.Tell(); }),
            ["eof"] = new IntrinsicMethod("eof", 0, 0, (recv, a, instr) => { var fh = (FileHandle)recv; return fh.Eof(); }),
            ["close"] = new IntrinsicMethod("close", 0, 0, (recv, a, instr) => { var fh = (FileHandle)recv; fh.Close(); return 1; }),
        };

        /// <summary>
        /// Defines the ArrayProto
        /// </summary>
        private static readonly Dictionary<string, IntrinsicMethod> ArrayProto = new(StringComparer.Ordinal)
        {
            ["len"] = new IntrinsicMethod("len", 0, 0, (recv, a, instr) =>
            {
                var arr = recv as List<object> ?? new List<object>();
                return arr.Count;
            }),

            ["push"] = new IntrinsicMethod("push", 1, 1, (recv, a, instr) =>
            {
                var arr = recv as List<object> ?? throw new VMException("push on non-array", instr.Line, instr.Col, instr.OriginFile);
                arr.Add(a[0]);
                return arr.Count;
            }),

            ["pop"] = new IntrinsicMethod("pop", 0, 0, (recv, a, instr) =>
            {
                var arr = recv as List<object> ?? throw new VMException("pop on non-array", instr.Line, instr.Col, instr.OriginFile);
                if (arr.Count == 0) return null;
                var last = arr[^1];
                arr.RemoveAt(arr.Count - 1);
                return last;
            }),

            ["insert_at"] = new IntrinsicMethod("insert_at", 2, 2, (recv, a, instr) =>
            {
                var arr = recv as List<object> ?? throw new VMException("insert_at on non-array", instr.Line, instr.Col, instr.OriginFile);
                int idx = ClampIndex(Convert.ToInt32(a[0]), arr.Count);
                arr.Insert(idx, a[1]);
                return arr;
            }),

            ["remove_range"] = new IntrinsicMethod("remove_range", 2, 2, (recv, a, instr) =>
            {
                var arr = recv as List<object> ?? throw new VMException("remove_range on non-array", instr.Line, instr.Col, instr.OriginFile);
                var (st, ex) = NormalizeSliceBounds(a[0], a[1], arr.Count, instr);
                arr.RemoveRange(st, ex - st);
                return arr;
            }),

            ["replace_range"] = new IntrinsicMethod("replace_range", 3, 3, (recv, a, instr) =>
            {
                var arr = recv as List<object> ?? throw new VMException("replace_range on non-array", instr.Line, instr.Col, instr.OriginFile);
                var (st, ex) = NormalizeSliceBounds(a[0], a[1], arr.Count, instr);
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
            }),

            ["slice"] = new IntrinsicMethod("slice", 0, 2, (recv, a, instr) =>
            {
                var arr = recv as List<object> ?? new List<object>();
                object? startObj = a.Count > 0 ? a[0] : null;
                object? endObj = a.Count > 1 ? a[1] : null;
                var (st, ex) = NormalizeSliceBounds(startObj, endObj, arr.Count, instr);
                return arr.GetRange(st, ex - st);
            }),
        };

        /// <summary>
        /// Defines the DictProto
        /// </summary>
        private static readonly Dictionary<string, IntrinsicMethod> DictProto = new(StringComparer.Ordinal)
        {
            ["len"] = new IntrinsicMethod("len", 0, 0, (recv, a, instr) =>
            {
                var dict = recv as Dictionary<string, object> ?? new();
                return dict.Count;
            }),

            ["has"] = new IntrinsicMethod("has", 1, 1, (recv, a, instr) =>
            {
                var dict = recv as Dictionary<string, object> ?? new();
                string key = a[0]?.ToString() ?? "";
                return dict.ContainsKey(key);
            }),

            ["remove"] = new IntrinsicMethod("remove", 1, 1, (recv, a, instr) =>
            {
                var dict = recv as Dictionary<string, object> ?? new();
                string key = a[0]?.ToString() ?? "";
                return dict.Remove(key);
            }),

            ["keys"] = new IntrinsicMethod("keys", 0, 0, (recv, a, instr) =>
            {
                var dict = recv as Dictionary<string, object> ?? new();
                return dict.Keys.ToList<object>();
            }),

            ["values"] = new IntrinsicMethod("values", 0, 0, (recv, a, instr) =>
            {
                var dict = recv as Dictionary<string, object> ?? new();
                return dict.Values.ToList();
            }),

            ["set"] = new IntrinsicMethod("set", 2, 2, (recv, a, instr) =>
            {
                var dict = recv as Dictionary<string, object> ?? throw new VMException("set on non-dict", instr.Line, instr.Col, instr.OriginFile);
                string key = a[0]?.ToString() ?? "";
                dict[key] = a[1];
                return dict;
            }),

            ["get_or"] = new IntrinsicMethod("get_or", 2, 2, (recv, a, instr) =>
            {
                var dict = recv as Dictionary<string, object> ?? new();
                string key = a[0]?.ToString() ?? "";
                return dict.TryGetValue(key, out var v) ? v : a[1];
            }),
        };

        /// <summary>
        /// Defines the <see cref="ExceptionObject" />
        /// </summary>
        public sealed class ExceptionObject
        {
            /// <summary>
            /// Gets the Type
            /// </summary>
            public string Type { get; }

            /// <summary>
            /// Gets the Message
            /// </summary>
            public string Message { get; }

            /// <summary>
            /// Gets the File
            /// </summary>
            public string File { get; }

            /// <summary>
            /// Gets the Line
            /// </summary>
            public int Line { get; }

            /// <summary>
            /// Gets the Col
            /// </summary>
            public int Col { get; }

            /// <summary>
            /// Gets the Stack
            /// </summary>
            public string Stack { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="ExceptionObject"/> class.
            /// </summary>
            /// <param name="type">The type<see cref="string"/></param>
            /// <param name="message">The message<see cref="string"/></param>
            /// <param name="file">The file<see cref="string"/></param>
            /// <param name="line">The line<see cref="int"/></param>
            /// <param name="col">The col<see cref="int"/></param>
            /// <param name="stack">The stack<see cref="string"/></param>
            public ExceptionObject(string type, string message, string file, int line, int col, string stack = "")
            {
                Type = type;
                Message = message;
                File = file;
                Line = line;
                Col = col;
                Stack = stack;
            }

            /// <summary>
            /// The ToString
            /// </summary>
            /// <returns>The <see cref="string"/></returns>
            public override string ToString() => $"{Type}: {Message}";
        }

        /// <summary>
        /// Defines the ExceptionProto
        /// </summary>
        private static readonly Dictionary<string, IntrinsicMethod> ExceptionProto =
    new(StringComparer.Ordinal)
    {
        ["message"] = new IntrinsicMethod("message", 0, 0, (recv, a, instr) => ((ExceptionObject)recv).Message),
        ["type"] = new IntrinsicMethod("type", 0, 0, (recv, a, instr) => ((ExceptionObject)recv).Type),
        ["file"] = new IntrinsicMethod("file", 0, 0, (recv, a, instr) => ((ExceptionObject)recv).File),
        ["line"] = new IntrinsicMethod("line", 0, 0, (recv, a, instr) => ((ExceptionObject)recv).Line),
        ["col"] = new IntrinsicMethod("col", 0, 0, (recv, a, instr) => ((ExceptionObject)recv).Col),
        ["stack"] = new IntrinsicMethod("stack", 0, 0, (recv, a, instr) => ((ExceptionObject)recv).Stack),
        ["toString"] = new IntrinsicMethod("toString", 0, 0, (recv, a, instr) => ((ExceptionObject)recv).ToString()),
    };

        /// <summary>
        /// Defines the <see cref="Env" />
        /// </summary>
        private class Env
        {
            /// <summary>
            /// Defines the Vars
            /// </summary>
            public Dictionary<string, object> Vars = new Dictionary<string, object>();

            /// <summary>
            /// Defines the Parent
            /// </summary>
            public Env? Parent;

            /// <summary>
            /// Initializes a new instance of the <see cref="Env"/> class.
            /// </summary>
            /// <param name="parent">The parent<see cref="Env?"/></param>
            public Env(Env? parent)
            {
                Parent = parent;
            }

            /// <summary>
            /// The TryGetValue
            /// </summary>
            /// <param name="name">The name<see cref="string"/></param>
            /// <param name="value">The value<see cref="object?"/></param>
            /// <returns>The <see cref="bool"/></returns>
            public bool TryGetValue(string name, out object? value)
            {
                if (Vars.TryGetValue(name, out value)) return true;
                if (Parent != null) return Parent.TryGetValue(name, out value);
                value = null;
                return false;
            }

            /// <summary>
            /// The HasLocal
            /// </summary>
            /// <param name="name">The name<see cref="string"/></param>
            /// <returns>The <see cref="bool"/></returns>
            public bool HasLocal(string name) => Vars.ContainsKey(name);

            /// <summary>
            /// The Set
            /// </summary>
            /// <param name="name">The name<see cref="string"/></param>
            /// <param name="value">The value<see cref="object"/></param>
            /// <returns>The <see cref="bool"/></returns>
            public bool Set(string name, object value)
            {
                if (Vars.ContainsKey(name))
                {
                    Vars[name] = value;
                    return true;
                }
                if (Parent != null) return Parent.Set(name, value);
                return false;
            }

            /// <summary>
            /// The Define
            /// </summary>
            /// <param name="name">The name<see cref="string"/></param>
            /// <param name="value">The value<see cref="object"/></param>
            public void Define(string name, object value)
            {
                Vars[name] = value;
            }

            /// <summary>
            /// The RemoveLocal
            /// </summary>
            /// <param name="name">The name<see cref="string"/></param>
            /// <returns>The <see cref="bool"/></returns>
            public bool RemoveLocal(string name) => Vars.Remove(name);
        }

        /// <summary>
        /// Defines the <see cref="TryHandler" />
        /// </summary>
        private class TryHandler
        {
            /// <summary>
            /// Defines the CatchAddr
            /// </summary>
            public int CatchAddr;

            /// <summary>
            /// Defines the FinallyAddr
            /// </summary>
            public int FinallyAddr;

            /// <summary>
            /// Defines the Exception
            /// </summary>
            public object? Exception;

            /// <summary>
            /// Initializes a new instance of the <see cref="TryHandler"/> class.
            /// </summary>
            /// <param name="c">The c<see cref="int"/></param>
            /// <param name="f">The f<see cref="int"/></param>
            public TryHandler(int c, int f)
            {
                CatchAddr = c; FinallyAddr = f; Exception = null;
            }
        }

        /// <summary>
        /// Defines the _tryHandlers
        /// </summary>
        private readonly List<TryHandler> _tryHandlers = new();

        /// <summary>
        /// Defines the <see cref="BoundMethod" />
        /// </summary>
        private sealed class BoundMethod
        {
            /// <summary>
            /// Gets the Function
            /// </summary>
            public Closure Function { get; }

            /// <summary>
            /// Gets the Receiver
            /// </summary>
            public object Receiver { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="BoundMethod"/> class.
            /// </summary>
            /// <param name="function">The function<see cref="Closure"/></param>
            /// <param name="receiver">The receiver<see cref="object"/></param>
            public BoundMethod(Closure function, object receiver)
            {
                Function = function ?? throw new ArgumentNullException(nameof(function));
                Receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
            }

            /// <summary>
            /// The ToString
            /// </summary>
            /// <returns>The <see cref="string"/></returns>
            public override string ToString() => $"<bound {Function}>";
        }

        /// <summary>
        /// Defines the <see cref="Closure" />
        /// </summary>
        private class Closure
        {
            /// <summary>
            /// Gets the Address
            /// </summary>
            public int Address { get; }

            /// <summary>
            /// Gets the Parameters
            /// </summary>
            public List<string> Parameters { get; }

            /// <summary>
            /// Gets the CapturedEnv
            /// </summary>
            public Env CapturedEnv { get; }

            /// <summary>
            /// Gets the Name
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="Closure"/> class.
            /// </summary>
            /// <param name="address">The address<see cref="int"/></param>
            /// <param name="parameters">The parameters<see cref="List{string}"/></param>
            /// <param name="env">The env<see cref="Env"/></param>
            /// <param name="name">The name<see cref="string"/></param>
            public Closure(int address, List<string> parameters, Env env, string name)
            {
                Address = address;
                Parameters = parameters;
                CapturedEnv = env;
                Name = name ?? "<anon>";
            }

            /// <summary>
            /// The ToString
            /// </summary>
            /// <returns>The <see cref="string"/></returns>
            public override string ToString()
            {
                string paramList = string.Join(", ", Parameters);
                string captured = "";
                if (CapturedEnv != null && CapturedEnv.Vars.Count > 0)
                {
                    var pairs = CapturedEnv.Vars
                        .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .Select(kvp =>
                        {
                            if (kvp.Value == null) return $"{kvp.Key}=null";
                            if (kvp.Value is Closure) return $"{kvp.Key}=<closure>";
                            if (kvp.Value is FunctionInfo fi) return $"{kvp.Key}=<fn:{fi.Address.ToString()}>";
                            return $"{kvp.Key}={kvp.Value.GetType().Name}";
                        });
                    captured = $" captured: {{{string.Join(", ", pairs)}}}";
                }
                return $"<closure {Name} at {Address} ({paramList}){captured}>";
            }
        }

        private record CallFrame(int ReturnIp, int ScopesAdded, object? ThisRef);
        /// <summary>
        /// Gets the CurrentThis
        /// </summary>
        private object? CurrentThis => _callStack.Count > 0 ? _callStack.Peek().ThisRef : null;

        /// <summary>
        /// Defines the _stack
        /// </summary>
        private readonly Stack<object> _stack = new();

        /// <summary>
        /// Defines the _scopes
        /// </summary>
        private readonly List<Env> _scopes = new() { new Env(null) };

        /// <summary>
        /// Defines the _functions
        /// </summary>
        private readonly Dictionary<string, FunctionInfo> _functions = new();

        /// <summary>
        /// Defines the _callStack
        /// </summary>
        private readonly Stack<CallFrame> _callStack = new();

        /// <summary>
        /// Gets the DebugStream
        /// </summary>
        public MemoryStream DebugStream { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="VM"/> class.
        /// </summary>
        public VM()
        {
            DebugStream = new MemoryStream();
        }

        /// <summary>
        /// The DeleteSliceOnTarget
        /// </summary>
        /// <param name="target">The target<see cref="object"/></param>
        /// <param name="startObj">The startObj<see cref="object"/></param>
        /// <param name="endObj">The endObj<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        private static void DeleteSliceOnTarget(ref object target, object startObj, object endObj, Instruction instr)
        {
            if (target is string)
                throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile);
            switch (target)
            {
                case List<object> arr:
                    {
                        int len = arr.Count;
                        int start = startObj == null ? 0 : Convert.ToInt32(startObj);
                        int end = endObj == null ? len : Convert.ToInt32(endObj);

                        if (start < 0) start += len;
                        if (end < 0) end += len;

                        start = Math.Clamp(start, 0, len);
                        end = Math.Clamp(end, 0, len);
                        if (end < start) end = start;

                        if (start < end)
                            arr.RemoveRange(start, end - start);
                        return;
                    }

                case Dictionary<string, object> dict:
                    {
                        var keys = dict.Keys.ToList();
                        int len = keys.Count;

                        int start = startObj == null ? 0 : Convert.ToInt32(startObj);
                        int end = endObj == null ? len : Convert.ToInt32(endObj);

                        if (start < 0) start += len;
                        if (end < 0) end += len;

                        start = Math.Clamp(start, 0, len);
                        end = Math.Clamp(end, 0, len);
                        if (end < start) end = start;

                        for (int i = end - 1; i >= start; i--)
                            dict.Remove(keys[i]);

                        return;
                    }

                case string s:
                    {
                        int len = s.Length;
                        int start = startObj == null ? 0 : Convert.ToInt32(startObj);
                        int end = endObj == null ? len : Convert.ToInt32(endObj);

                        if (start < 0) start += len;
                        if (end < 0) end += len;

                        start = Math.Clamp(start, 0, len);
                        end = Math.Clamp(end, 0, len);
                        if (end < start) end = start;

                        if (start < end)
                        {
                            target = s.Substring(0, start) + s.Substring(end);
                        }
                        return;
                    }

                default:
                    throw new VMException($"Runtime error: delete slice target must be array, dictionary, or string", instr.Line, instr.Col, instr.OriginFile);
            }
        }

        /// <summary>
        /// The NormalizeSliceBounds
        /// </summary>
        /// <param name="startObj">The startObj<see cref="object?"/></param>
        /// <param name="endObj">The endObj<see cref="object?"/></param>
        /// <param name="len">The len<see cref="int"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="(int start, int endEx)"/></returns>
        private static (int start, int endEx) NormalizeSliceBounds(object? startObj, object? endObj, int len, Instruction instr)
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

                if (rawEnd == 0 && startIsNull)
                {
                    endEx = len;
                }
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

        /// <summary>
        /// The IsNumber
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public static bool IsNumber(object x) =>
            x is sbyte or byte or short or ushort or int or uint or long or ulong
              or float or double or decimal;

        /// <summary>
        /// Defines the NumKind
        /// </summary>
        private enum NumKind
        {
            /// <summary>
            /// Defines the Int32
            /// </summary>
            Int32,

            /// <summary>
            /// Defines the Int64
            /// </summary>
            Int64,

            /// <summary>
            /// Defines the UInt64
            /// </summary>
            UInt64,

            /// <summary>
            /// Defines the Double
            /// </summary>
            Double,

            /// <summary>
            /// Defines the Decimal
            /// </summary>
            Decimal
        }

        /// <summary>
        /// The PromoteKind
        /// </summary>
        /// <param name="a">The a<see cref="object"/></param>
        /// <param name="b">The b<see cref="object"/></param>
        /// <returns>The <see cref="NumKind"/></returns>
        private static NumKind PromoteKind(object a, object b)
        {
            if (a is decimal || b is decimal) return NumKind.Decimal;
            if (a is double || b is double || a is float || b is float) return NumKind.Double;
            if (a is ulong || b is ulong) return NumKind.UInt64;
            if (a is long || b is long) return NumKind.Int64;
            return NumKind.Int32;
        }

        /// <summary>
        /// The CoercePair
        /// </summary>
        /// <param name="a">The a<see cref="object"/></param>
        /// <param name="b">The b<see cref="object"/></param>
        /// <returns>The <see cref="(object A, object B, NumKind K)"/></returns>
        private static (object A, object B, NumKind K) CoercePair(object a, object b)
        {
            var k = PromoteKind(a, b);
            switch (k)
            {
                case NumKind.Decimal:
                    return (Convert.ToDecimal(a), Convert.ToDecimal(b), k);
                case NumKind.Double:
                    return (Convert.ToDouble(a), Convert.ToDouble(b), k);
                case NumKind.UInt64:
                    return (Convert.ToUInt64(a), Convert.ToUInt64(b), k);
                case NumKind.Int64:
                    return (Convert.ToInt64(a), Convert.ToInt64(b), k);
                default:
                    return (Convert.ToInt32(a), Convert.ToInt32(b), k);
            }
        }

        /// <summary>
        /// The CompareAsDecimal
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="decimal"/></returns>
        public static decimal CompareAsDecimal(object x) => x switch
        {
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => (decimal)v,
            float v => (decimal)v,
            double v => (decimal)v,
            decimal v => v,
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}"),
        };

        /// <summary>
        /// The LoadFunctions
        /// </summary>
        /// <param name="funcs">The funcs<see cref="Dictionary{string, FunctionInfo}"/></param>
        public void LoadFunctions(Dictionary<string, FunctionInfo> funcs)
        {
            foreach (var kv in funcs)
            {
                if (_functions.ContainsKey(kv.Key))
                    throw new Exception($"Runtime error : Multiple declarations for function '{kv.Key}'.");
                _functions[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// The ToNumber
        /// </summary>
        /// <param name="val">The val<see cref="object?"/></param>
        /// <returns>The <see cref="object"/></returns>
        private static object ToNumber(object? val)
        {
            if (val is null) return 0;

            switch (val)
            {
                case int or long or float or double or decimal:
                    return val;
                case bool b:
                    return b ? 1 : 0;
                case char ch:
                    if (char.IsDigit(ch)) return (int)(ch - '0');
                    return (int)ch;
            }

            var s = val.ToString() ?? "";
            s = s.Trim();

            if (s.Length == 0) return 0;

            s = s.Replace("_", "");

            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ParseIntegerRadix(s[2..], 16);
            if (s.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                return ParseIntegerRadix(s[2..], 2);
            if (s.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
                return ParseIntegerRadix(s[2..], 8);

            if (s.Contains(',')) s = s.Replace(',', '.');

            bool looksFloat = s.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0;

            if (!looksFloat)
            {
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i32))
                    return i32;

                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i64))
                    return i64;

                if (decimal.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var decInt))
                    return decInt;

                if (double.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dblInt))
                    return dblInt;

                throw new FormatException($"toi: '{s}' ist keine gültige Ganzzahl.");
            }
            else
            {
                if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dec))
                    return dec;

                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl))
                    return dbl;

                throw new FormatException($"toi: '{s}' ist keine gültige Fließkommazahl.");
            }
        }

        /// <summary>
        /// The ParseIntegerRadix
        /// </summary>
        /// <param name="digits">The digits<see cref="string"/></param>
        /// <param name="radix">The radix<see cref="int"/></param>
        /// <returns>The <see cref="object"/></returns>
        private static object ParseIntegerRadix(string digits, int radix)
        {
            if (digits.Length == 0)
                throw new FormatException("toi: leere Ziffernfolge.");

            bool neg = false;
            int idx = 0;
            if (digits[0] == '+' || digits[0] == '-')
            {
                neg = digits[0] == '-';
                idx = 1;
                if (idx >= digits.Length)
                    throw new FormatException("toi: nur Vorzeichen ohne Ziffern.");
            }

            long acc = 0;
            checked
            {
                for (; idx < digits.Length; idx++)
                {
                    char c = digits[idx];
                    int v =
                        c is >= '0' and <= '9' ? (c - '0') :
                        c is >= 'a' and <= 'f' ? (c - 'a' + 10) :
                        c is >= 'A' and <= 'F' ? (c - 'A' + 10) :
                        -1;

                    if (v < 0 || v >= radix)
                        throw new FormatException($"toi: ungültige Ziffer '{c}' für Basis {radix}.");

                    acc = acc * radix + v;
                }
            }

            if (neg) acc = -acc;

            if (acc <= int.MaxValue && acc >= int.MinValue) return (int)acc;
            return acc;
        }

        /// <summary>
        /// The CallBuiltin
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="args">The args<see cref="List{object}"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="object"/></returns>
        private static object CallBuiltin(string name, List<object> args, Instruction instr)
        {
            switch (name)
            {

                case "json":
                    {
                        return JsonStringify(args[0]);
                    }

                case "fopen":
                    {
                        if (args.Count < 2)
                            throw new VMException("Runtime error: fopen(path, mode) expects 2 arguments",
                                instr.Line, instr.Col, instr.OriginFile);

                        object a0 = args[0];
                        object a1 = args[1];

                        string path;
                        int mode;

                        if (a0 is string || a0 is char)
                        {
                            path = a0.ToString() ?? "";
                            mode = Convert.ToInt32(a1);
                        }
                        else if (a1 is string || a1 is char)
                        {
                            path = a1.ToString() ?? "";
                            mode = Convert.ToInt32(a0);
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
                                throw new VMException($"Runtime error: invalid fopen mode {mode}",
                                    instr.Line, instr.Col, instr.OriginFile);
                        }

                        try
                        {
                            var fs = new FileStream(path, fmode, facc, FileShare.Read);
                            if (mode == 3) fs.Seek(0, SeekOrigin.End);
                            return new FileHandle(path, mode, fs, canRead, canWrite);
                        }
                        catch (Exception ex)
                        {
                            throw new VMException($"Runtime error: fopen failed for '{path}': {ex.Message}",
                                instr.Line, instr.Col, instr.OriginFile);
                        }
                    }

                case "typeof":
                    {
                        var val = args[0];
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
                        if (val is FunctionInfo) return "Function";
                        if (val is Closure) return "Closure";
                        if (val is Dictionary<string, object>) return "Dictionary";
                        if (val is ClassInstance) return (val as ClassInstance ?? new ClassInstance("null")).ClassName;
                        return val.GetType().Name;
                    }

                case "getfields":
                    if (args[0] is not Dictionary<string, object>)
                        return new List<object>();
                    var fld = args[0] as Dictionary<string, object>;
                    return fld?.Keys.ToList<object>() ?? new List<object>();
                case "isarray":
                    return args[0] is List<object>;

                case "isdict":
                    return args[0] is Dictionary<string, object>;

                case "len":
                    if (args[0] is string s) return s.Length;
                    if (args[0] is List<object> list) return list.Count;
                    if (args[0] is Dictionary<string, object> dct) return dct.Count;
                    return -1;

                case "isdigit":
                    if (args[0] is null) return false;
                    return char.IsDigit(Convert.ToChar(args[0]));
                case "isletter":
                    if (args[0] is null) return false;
                    return char.IsLetter(Convert.ToChar(args[0]));
                case "isspace":
                    if (args[0] is null) return false;
                    return char.IsWhiteSpace(Convert.ToChar(args[0]));
                case "isalnum":
                    if (args[0] is null) return false;
                    return char.IsLetterOrDigit(Convert.ToChar(args[0]));

                case "str":
                    return args[0].ToString() ?? "";
                case "toi":
                    return ToNumber(args[0]);

                case "toi16":
                    return Convert.ToInt16(args[0]);
                case "toi32":
                    return Convert.ToInt32(args[0]);
                case "toi64":
                    return Convert.ToInt64(args[0]);
                case "abs":
                    return Math.Abs((dynamic)args[0]);
                case "rand":
                    return new Random((int)args[0]).Next((int)args[1], (int)args[2]);

                case "print":
                    PrintValue(args[0], Console.Out, 1, escapeNewlines: true);
                    Console.Out.WriteLine();
                    Console.Out.Flush();
                    return 1;

                case "put":
                    PrintValue(args[0], Console.Out, 1, escapeNewlines: false);
                    return 1;

                case "clear":
                    Console.Clear();
                    return 1;

                case "getl":
                    return Console.ReadLine() ?? "";
                case "getc":
                    return Console.Read();

            }

            throw new VMException($"Runtime error: unknown builtin function '{name}'", instr.Line, instr.Col, instr.OriginFile);
        }

        /// <summary>
        /// Defines the bInFunc
        /// </summary>
        public static Dictionary<string, int> bInFunc = new Dictionary<string, int>()
    {
        {"print",1 },
        {"len",1 },
        {"isarray",1 },
        {"isdict",1 },
        {"typeof",1 },
        {"str",1 },
        {"toi16",1 },
        {"toi32",1 },
        {"toi64",1 },
        {"abs",1 },
        {"rand",3 },
        {"getfields",1 },
            {"getl",0 },
            {"getc",0 },
            {"put",1 },
            {"clear",0 },
            {"isdigit",1 },
            {"isletter",1 },
            {"isspace",1 },
            {"isalnum",1 },
            {"toi",1 },
            { "fopen", 2 },
            {"json",1 }
    };

        /// <summary>
        /// The FindEnvWithLocal
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="Env?"/></returns>
        private Env? FindEnvWithLocal(string name)
        {
            for (var env = _scopes.Count > 0 ? _scopes[^1] : null; env != null; env = env.Parent)
            {
                if (env.Vars.ContainsKey(name))
                    return env;
            }

            if (_scopes.Count > 0)
            {
                var root = _scopes[0];
                if (root != null && root.Vars.ContainsKey(name))
                    return root;
            }

            return null;
        }

        /// <summary>
        /// The IsNumericType
        /// </summary>
        /// <param name="v">The v<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsNumericType(object v)
        {
            return v is int || v is long || v is double || v is decimal;
        }

        /// <summary>
        /// The ToBool
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool ToBool(object? v)
        {
            if (v is null) return false;
            if (v is bool b) return b;
            if (v is int i) return i != 0;
            if (v is long l) return l != 0L;
            if (v is double d) return d != 0.0;
            if (v is float f) return f != 0f;
            if (v is string s) return s.Length != 0;
            if (v is List<object> list) return list.Count != 0;
            if (v is Dictionary<string, object> dict) return dict.Count != 0;
            return true;
        }

        /// <summary>
        /// The HandleInstruction
        /// </summary>
        /// <param name="_ip">The _ip<see cref="int"/></param>
        /// <param name="_insns">The _insns<see cref="List{Instruction}"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="StepResult"/></returns>
        private StepResult HandleInstruction(ref int _ip, List<Instruction> _insns, Instruction instr)
        {
            switch (instr.Code)
            {
                case OpCode.PUSH_INT:
                    if (instr.Operand is null) _stack.Push(0);
                    else _stack.Push((int)instr.Operand);
                    break;

                case OpCode.PUSH_LNG:
                    if (instr.Operand is null) _stack.Push((long)0);
                    else _stack.Push((long)instr.Operand);
                    break;

                case OpCode.PUSH_FLT:
                    if (instr.Operand is null) _stack.Push((float)0);
                    else _stack.Push((float)instr.Operand);
                    break;

                case OpCode.PUSH_DBL:
                    if (instr.Operand is null) _stack.Push(0.0);
                    else _stack.Push((double)instr.Operand);
                    break;

                case OpCode.PUSH_DEC:
                    if (instr.Operand is null) _stack.Push((decimal)0);
                    else _stack.Push((decimal)instr.Operand);
                    break;

                case OpCode.PUSH_STR:
                    if (instr.Operand is null) _stack.Push("");
                    else _stack.Push((string)instr.Operand);
                    break;

                case OpCode.PUSH_CHR:
                    if (instr.Operand is null) _stack.Push((char)0);
                    else _stack.Push((char)instr.Operand);
                    break;

                case OpCode.PUSH_BOOL:
                    if (instr.Operand is null) _stack.Push(false);
                    else _stack.Push((bool)instr.Operand);
                    break;

                case OpCode.PUSH_NULL:
                    _stack.Push(null);
                    break;

                case OpCode.PUSH_SCOPE:
                    {
                        _scopes.Add(new Env(_scopes[^1]));
                        if (_callStack.Count > 0)
                        {
                            var fr = _callStack.Pop();
                            _callStack.Push(new CallFrame(fr.ReturnIp, fr.ScopesAdded + 1, fr.ThisRef));
                        }
                        break;
                    }

                case OpCode.POP_SCOPE:
                    {
                        if (_scopes.Count <= 1)
                            throw new VMException("Runtime error: cannot pop global scope", instr.Line, instr.Col, instr.OriginFile);

                        _scopes.RemoveAt(_scopes.Count - 1);

                        if (_callStack.Count > 0)
                        {
                            var fr = _callStack.Pop();
                            var newCount = Math.Max(0, fr.ScopesAdded - 1);
                            _callStack.Push(new CallFrame(fr.ReturnIp, newCount, fr.ThisRef));
                        }
                        break;
                    }

                case OpCode.NEW_OBJECT:
                    {
                        string className = instr.Operand?.ToString() ?? "<anon>";
                        var obj = new ClassInstance(className);
                        _stack.Push(obj);
                        break;
                    }

                case OpCode.NEW_ARRAY:
                    {
                        if (instr.Operand is null) break;
                        int count = (int)instr.Operand;
                        var temp = new object[count];
                        for (int i = count - 1; i >= 0; i--) temp[i] = _stack.Pop();
                        var list = new List<object>(temp);
                        _stack.Push(list);
                        break;
                    }

                case OpCode.SLICE_GET:
                    {
                        object endObj = _stack.Pop();
                        object startObj = _stack.Pop();

                        object target;
                        if (instr.Operand is string name)
                        {
                            var owner = FindEnvWithLocal(name)
                                ?? throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile);
                            target = owner.Vars[name];
                        }
                        else
                        {
                            target = _stack.Pop();
                        }

                        static void Normalize(int len, object startRaw, object endRaw, out int start, out int end)
                        {
                            start = startRaw == null ? 0 : Convert.ToInt32(startRaw);
                            end = endRaw == null ? len : Convert.ToInt32(endRaw);

                            if (start < 0) start += len;
                            if (end < 0) end += len;

                            start = Math.Clamp(start, 0, len);
                            end = Math.Clamp(end, 0, len);

                            if (end < start) end = start;
                        }

                        switch (target)
                        {
                            case List<object> arr:
                                {
                                    Normalize(arr.Count, startObj, endObj, out int start, out int end);
                                    _stack.Push(arr.GetRange(start, end - start));
                                    break;
                                }

                            case Dictionary<string, object> dict:
                                {
                                    var keys = dict.Keys.ToList();
                                    Normalize(keys.Count, startObj, endObj, out int start, out int end);

                                    var slice = new Dictionary<string, object>();
                                    for (int i = start; i < end; i++)
                                        slice[keys[i]] = dict[keys[i]];

                                    _stack.Push(slice);
                                    break;
                                }

                            case string s:
                                {
                                    Normalize(s.Length, startObj, endObj, out int start, out int end);
                                    _stack.Push(s.Substring(start, end - start));
                                    break;
                                }

                            default:
                                throw new VMException($"Runtime error: SLICE_GET target must be array, dictionary, or string",
                                    instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }

                case OpCode.SLICE_SET:
                    {
                        object value = _stack.Pop();
                        object endObj = _stack.Pop();
                        object startObj = _stack.Pop();

                        object target;

                        if (instr.Operand is string name)
                        {
                            var env = FindEnvWithLocal(name)
                                ?? throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile);
                            target = env.Vars[name];

                            DoSliceSet(ref target, startObj, endObj, value, instr);
                            env.Vars[name] = target;
                        }
                        else
                        {
                            target = _stack.Pop();
                            DoSliceSet(ref target, startObj, endObj, value, instr);
                        }
                        break;

                        static void DoSliceSet(ref object target, object startObj, object endObj, object value, Instruction instr)
                        {
                            if (target is string)
                                throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile);
                            static void Normalize(int len, object startRaw, object endRaw, out int start, out int end)
                            {
                                start = startRaw == null ? 0 : Convert.ToInt32(startRaw);
                                end = endRaw == null ? len : Convert.ToInt32(endRaw);

                                if (start < 0) start += len;
                                if (end < 0) end += len;

                                start = Math.Clamp(start, 0, len);
                                end = Math.Clamp(end, 0, len);
                                if (end < start) end = start;
                            }

                            switch (target)
                            {
                                case List<object> arr:
                                    {
                                        Normalize(arr.Count, startObj, endObj, out int start, out int end);

                                        if (value is List<object> lst)
                                        {
                                            int count = Math.Min(end - start, lst.Count);
                                            for (int i = 0; i < count; i++)
                                                arr[start + i] = lst[i];

                                        }
                                        else
                                        {
                                            throw new VMException($"Runtime error: trying to assign non-list to array slice",
                                                instr.Line, instr.Col, instr.OriginFile);
                                        }
                                        break;
                                    }

                                case Dictionary<string, object> dict:
                                    {
                                        var keys = dict.Keys.ToList();
                                        Normalize(keys.Count, startObj, endObj, out int start, out int end);

                                        if (value is Dictionary<string, object> valDict)
                                        {
                                            int i = 0;
                                            for (int k = start; k < end && i < valDict.Count; k++, i++)
                                            {
                                                var kv = valDict.ElementAt(i);
                                                dict[keys[k]] = kv.Value;
                                            }
                                        }
                                        else
                                        {
                                            throw new VMException($"Runtime error: trying to assign non-dictionary to dictionary slice",
                                                instr.Line, instr.Col, instr.OriginFile);
                                        }
                                        break;
                                    }

                                case string s:
                                    {
                                        Normalize(s.Length, startObj, endObj, out int start, out int end);

                                        var sb = new StringBuilder(s);
                                        var replacement = (value?.ToString()) ?? "";

                                        int count = Math.Min(end - start, replacement.Length);
                                        for (int i = 0; i < count; i++)
                                            sb[start + i] = replacement[i];

                                        target = sb.ToString();
                                        break;
                                    }

                                default:
                                    throw new VMException($"Runtime error: SLICE_SET target must be array, dictionary, or string",
                                        instr.Line, instr.Col, instr.OriginFile);
                            }
                        }
                    }

                case OpCode.INDEX_GET:
                    {
                        object target;
                        object idxObj = _stack.Pop();

                        if (instr.Operand is string nameFromEnv)
                        {
                            var owner = FindEnvWithLocal(nameFromEnv)
                                ?? throw new VMException($"Runtime error: undefined variable '{nameFromEnv}'", instr.Line, instr.Col, instr.OriginFile);
                            target = owner.Vars[nameFromEnv];
                        }
                        else
                        {
                            target = _stack.Pop();
                        }

                        _stack.Push(GetIndexedValue(target, idxObj, instr));
                        break;
                    }

                case OpCode.INDEX_SET:
                    {
                        object value = _stack.Pop();
                        object idxObj = _stack.Pop();
                        object target;

                        if (instr.Operand is string nameFromEnv)
                        {
                            var env = FindEnvWithLocal(nameFromEnv)
                                ?? throw new VMException($"Runtime error: undefined variable '{nameFromEnv}'", instr.Line, instr.Col, instr.OriginFile);

                            target = env.Vars[nameFromEnv];
                            SetIndexedValue(ref target, idxObj, value, instr);
                            env.Vars[nameFromEnv] = target;
                        }
                        else
                        {
                            target = _stack.Pop();
                            SetIndexedValue(ref target, idxObj, value, instr);
                        }

                        break;
                    }

                case OpCode.NEW_DICT:
                    {
                        if (instr.Operand is null) break;
                        int count = (int)instr.Operand;
                        var dict = new Dictionary<string, object>();
                        for (int i = 0; i < count; i++)
                        {
                            var value = _stack.Pop();
                            var key = _stack.Pop();
                            dict[key?.ToString() ?? "null"] = value;
                        }
                        _stack.Push(dict);
                        break;
                    }

                case OpCode.ROT:
                    {
                        var a = _stack.Pop();
                        var b = _stack.Pop();
                        var c = _stack.Pop();
                        _stack.Push(b);
                        _stack.Push(a);
                        _stack.Push(c);
                        break;
                    }

                case OpCode.ARRAY_PUSH:
                    {
                        if (instr.Operand == null)
                        {
                            var arrObj = _stack.Pop();
                            var value = _stack.Pop();

                            if (arrObj is List<object> arr)
                            {
                                arr.Add(value);
                            }
                            else if (arrObj is Dictionary<string, object> dict)
                            {
                                if (value is Dictionary<string, object> literal && literal.Count == 1)
                                {
                                    foreach (var kv in literal)
                                    {
                                        dict[kv.Key] = kv.Value;
                                    }
                                }
                                else
                                {
                                    int k = 0;
                                    while (dict.ContainsKey(k.ToString(CultureInfo.InvariantCulture)))
                                    {
                                        k++;
                                    }
                                    dict[k.ToString(CultureInfo.InvariantCulture)] = value;
                                }
                            }
                            else
                            {
                                throw new VMException($"Runtime error: ARRAY_PUSH target is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile);
                            }
                        }
                        else
                        {
                            var value = _stack.Pop();
                            string name = (string)instr.Operand;
                            var env = FindEnvWithLocal(name);
                            if (env == null || !env.Vars.TryGetValue(name, out object? obj))
                                throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile);
                            if (obj is List<object> arr)
                            {
                                arr.Add(value);
                            }
                            else if (obj is Dictionary<string, object> dict)
                            {
                                if (value is Dictionary<string, object> literal && literal.Count == 1)
                                {
                                    foreach (var kv in literal)
                                    {
                                        dict[kv.Key] = kv.Value;
                                    }
                                }
                                else
                                {
                                    int k = 0;
                                    while (dict.ContainsKey(k.ToString(CultureInfo.InvariantCulture)))
                                    {
                                        k++;
                                    }
                                    dict[k.ToString(CultureInfo.InvariantCulture)] = value;
                                }
                            }
                            else
                            {
                                throw new VMException($"Runtime error: variable '{name}' is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile);
                            }
                        }
                        break;
                    }

                case OpCode.ARRAY_DELETE_SLICE:
                    {
                        var endObj = _stack.Pop();
                        var startObj = _stack.Pop();

                        object target;
                        if (instr.Operand is string name)
                        {
                            var env = FindEnvWithLocal(name)
                                ?? throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile);
                            target = env.Vars[name];

                            DeleteSliceOnTarget(ref target, startObj, endObj, instr);

                            env.Vars[name] = target;
                        }
                        else
                        {
                            target = _stack.Pop();
                            DeleteSliceOnTarget(ref target, startObj, endObj, instr);
                        }
                        break;
                    }

                case OpCode.ARRAY_DELETE_SLICE_ALL:
                    {
                        var endObj = _stack.Pop();
                        var startObj = _stack.Pop();
                        var target = _stack.Pop();

                        if (target is string)
                            throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile);

                        if (target is List<object> arr)
                        {
                            (int start, int endEx) = NormalizeSliceBounds(startObj, endObj, arr.Count, instr);
                            int count = endEx - start;
                            if (count > 0) arr.RemoveRange(start, count);
                        }
                        else if (target is Dictionary<string, object> dict)
                        {
                            var keys = dict.Keys.ToList();
                            (int start, int endEx) = NormalizeSliceBounds(startObj, endObj, keys.Count, instr);
                            for (int i = start; i < endEx; i++)
                                dict.Remove(keys[i]);
                        }
                        else
                        {
                            throw new VMException($"Runtime error: delete target is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }

                case OpCode.ARRAY_DELETE_ELEM:
                    {
                        var idxObj = _stack.Pop();

                        if (instr.Operand != null)
                        {
                            string name = (string)instr.Operand;
                            var owner = FindEnvWithLocal(name);
                            if (owner == null)
                                throw new VMException($"Runtime error: undefined variable '{name}", instr.Line, instr.Col, instr.OriginFile);

                            var target = owner.Vars[name];

                            if (target is string)
                                throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile);

                            if (target is List<object> arr)
                            {
                                int index = Convert.ToInt32(idxObj);
                                if (index >= 0 && index < arr.Count)
                                    arr.RemoveAt(index);
                                else
                                    throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                            }
                            else if (target is Dictionary<string, object> dict)
                            {
                                string key = Convert.ToString(idxObj, CultureInfo.InvariantCulture) ?? "";
                                if (!dict.Remove(key))
                                    throw new VMException($"Runtime error: key '{key}' not found in dictionary", instr.Line, instr.Col, instr.OriginFile);
                            }
                            else
                            {
                                throw new VMException($"Runtime error: variable '{name}' is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile);
                            }
                        }
                        else
                        {
                            var target = _stack.Pop();

                            if (target is string)
                                throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile);

                            if (target is List<object> arr)
                            {
                                int index = Convert.ToInt32(idxObj);
                                if (index >= 0 && index < arr.Count)
                                    arr.RemoveAt(index);
                                else
                                    throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                            }
                            else if (target is Dictionary<string, object> dict)
                            {
                                string key = Convert.ToString(idxObj, CultureInfo.InvariantCulture) ?? "";
                                if (!dict.Remove(key))
                                    throw new VMException($"Runtime error: key '{key}' not found in dictionary", instr.Line, instr.Col, instr.OriginFile);
                            }
                            else
                            {
                                throw new VMException($"Runtime error: delete target is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile);
                            }
                        }
                        break;
                    }

                case OpCode.ARRAY_DELETE_ALL:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;
                        var env = FindEnvWithLocal(name);
                        if (env == null || !env.Vars.TryGetValue(name, out object? target))
                            throw new VMException($"Runtime error: undefined variable '{name}", instr.Line, instr.Col, instr.OriginFile);
                        if (target is List<object>)
                        {
                            env.Vars[name] = new List<object>();
                        }
                        else if (target is Dictionary<string, object>)
                        {
                            env.Vars[name] = new Dictionary<string, object>();
                        }
                        else
                        {
                            throw new VMException($"Runtime error: variable '{name}' is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }

                case OpCode.ARRAY_DELETE_ELEM_ALL:
                    {
                        var idxObj = _stack.Pop();
                        var target = _stack.Pop();
                        if (target is string)
                            throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile);

                        if (target is List<object> arr)
                        {
                            int index = Convert.ToInt32(idxObj);
                            if (index < 0 || index >= arr.Count)
                                throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);

                            arr.RemoveAt(index);
                        }
                        else if (target is Dictionary<string, object> dict)
                        {
                            string key = idxObj?.ToString() ?? throw new VMException(
                                $"Runtime error: dictionary key cannot be null", instr.Line, instr.Col, instr.OriginFile);

                            if (!dict.Remove(key))
                                throw new VMException($"Runtime error: key '{key}' not found in dictionary", instr.Line, instr.Col, instr.OriginFile);
                        }
                        else
                        {
                            throw new VMException($"Runtime error: delete target is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }

                case OpCode.LOAD_VAR:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;
                        if (name == "this")
                        {
                            var th = CurrentThis;
                            if (th == null) throw new VMException("Runtime error: 'this' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile);
                            _stack.Push(th);
                            break;
                        }

                        var owner = FindEnvWithLocal(name);
                        if (owner == null || !owner.Vars.TryGetValue(name, out var val))
                            throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile);

                        _stack.Push(val);
                        break;
                    }

                case OpCode.VAR_DECL:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;
                        if (name == "this") throw new VMException("Runtime error: cannot declare 'this' as a variable", instr.Line, instr.Col, instr.OriginFile);
                        var value = _stack.Pop();
                        var scope = _scopes[^1];
                        if (scope.HasLocal(name)) throw new VMException($"Runtime error: variable '{name}' already declared in this scope", instr.Line, instr.Col, instr.OriginFile);
                        scope.Define(name, value);
                        break;
                    }

                case OpCode.STORE_VAR:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this") throw new VMException("Runtime error: cannot assign to 'this'", instr.Line, instr.Col, instr.OriginFile);
                        var value = _stack.Pop();
                        var env = FindEnvWithLocal(name);
                        if (env == null) throw new VMException($"Runtime error: assignment to undeclared variable '{name}'", instr.Line, instr.Col, instr.OriginFile);
                        env.Vars[name] = value;
                        break;
                    }

                case OpCode.ADD:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();

                        if (IsNumber(l) && IsNumber(r))
                        {
                            var (A, B, K) = CoercePair(l, r);
                            object res = K switch
                            {
                                NumKind.Decimal => (object)((decimal)A + (decimal)B),
                                NumKind.Double => (double)A + (double)B,
                                NumKind.UInt64 => (ulong)A + (ulong)B,
                                NumKind.Int64 => (long)A + (long)B,
                                _ => (int)A + (int)B,
                            };
                            _stack.Push(res);
                        }
                        else if (l is List<object> || r is List<object> ||
                                 l is Dictionary<string, object> || r is Dictionary<string, object>)
                        {
                            string ls, rs;
                            using (var lw = new StringWriter()) { PrintValue(l, lw); ls = lw.ToString(); }
                            using (var rw = new StringWriter()) { PrintValue(r, rw); rs = rw.ToString(); }
                            _stack.Push(ls + rs);
                        }
                        else
                        {
                            _stack.Push((l?.ToString() ?? "null") + (r?.ToString() ?? "null"));
                        }
                        break;
                    }

                case OpCode.SUB:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        if (!IsNumber(l) || !IsNumber(r))
                            throw new VMException("SUB on non-numeric types", instr.Line, instr.Col, instr.OriginFile);

                        var (A, B, K) = CoercePair(l, r);
                        object res = K switch
                        {
                            NumKind.Decimal => (object)((decimal)A - (decimal)B),
                            NumKind.Double => (double)A - (double)B,
                            NumKind.UInt64 => (ulong)A - (ulong)B,
                            NumKind.Int64 => (long)A - (long)B,
                            _ => (int)A - (int)B,
                        };
                        _stack.Push(res);
                        break;
                    }

                case OpCode.MUL:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();

                        if (IsNumber(l) && IsNumber(r))
                        {
                            var (A, B, K) = CoercePair(l, r);
                            object res = K switch
                            {
                                NumKind.Decimal => (object)((decimal)A * (decimal)B),
                                NumKind.Double => (double)A * (double)B,
                                NumKind.UInt64 => (ulong)A * (ulong)B,
                                NumKind.Int64 => (long)A * (long)B,
                                _ => (int)A * (int)B,
                            };
                            _stack.Push(res);
                        }
                        else if (l is string && IsNumber(r))
                        {
                            _stack.Push(string.Concat(Enumerable.Repeat(l?.ToString() ?? "", Convert.ToInt32(r))));
                        }
                        else if (r is string && IsNumber(l))
                        {
                            _stack.Push(string.Concat(Enumerable.Repeat(r?.ToString() ?? "", Convert.ToInt32(l))));
                        }
                        else
                        {
                            throw new VMException("MUL on non-numeric types", instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }

                case OpCode.MOD:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        if (!IsNumber(l) || !IsNumber(r))
                            throw new VMException("MOD on non-numeric types", instr.Line, instr.Col, instr.OriginFile);

                        var (A, B, K) = CoercePair(l, r);
                        if (K == NumKind.Int32 && Convert.ToInt32(r) == 0
                         || K == NumKind.Int64 && Convert.ToInt64(r) == 0
                         || K == NumKind.UInt64 && Convert.ToUInt64(r) == 0UL
                         || K == NumKind.Decimal && Convert.ToDecimal(r) == 0m)
                            throw new VMException("division by zero in MOD", instr.Line, instr.Col, instr.OriginFile);

                        object res = K switch
                        {
                            NumKind.Decimal => (object)((decimal)A % (decimal)B),
                            NumKind.Double => (double)A % (double)B,
                            NumKind.UInt64 => (ulong)A % (ulong)B,
                            NumKind.Int64 => (long)A % (long)B,
                            _ => (int)A % (int)B,
                        };
                        _stack.Push(res);
                        break;
                    }

                case OpCode.DIV:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        if (!IsNumber(l) || !IsNumber(r))
                            throw new VMException($"Runtime error: cannot DIV {l?.GetType()} and {r?.GetType()}",
                                instr.Line, instr.Col, instr.OriginFile);

                        var (A, B, K) = CoercePair(l, r);
                        if (K == NumKind.Int32 && Convert.ToInt32(r) == 0
                         || K == NumKind.Int64 && Convert.ToInt64(r) == 0
                         || K == NumKind.UInt64 && Convert.ToUInt64(r) == 0UL
                         || K == NumKind.Decimal && Convert.ToDecimal(r) == 0m)
                            throw new VMException("division by zero", instr.Line, instr.Col, instr.OriginFile);

                        object res = K switch
                        {
                            NumKind.Decimal => (object)((decimal)A / (decimal)B),
                            NumKind.Double => (double)A / (double)B,
                            NumKind.UInt64 => (ulong)A / (ulong)B,
                            NumKind.Int64 => (long)A / (long)B,
                            _ => (int)A / (int)B,
                        };
                        _stack.Push(res);
                        break;
                    }

                case OpCode.EXPO:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        if (!IsNumber(l) || !IsNumber(r))
                            throw new VMException("EXPO on non-numeric types", instr.Line, instr.Col, instr.OriginFile);

                        var (A, B, K) = CoercePair(l, r);
                        object res = K switch
                        {
                            NumKind.Decimal => (object)((decimal)Math.Pow((double)(decimal)A, (double)(decimal)B)),
                            NumKind.Double => Math.Pow((double)A, (double)B),
                            NumKind.UInt64 => (ulong)Math.Pow((double)(ulong)A, (double)(ulong)B),
                            NumKind.Int64 => (long)Math.Pow((double)(long)A, (double)(long)B),
                            _ => (int)Math.Pow((double)(int)A, (double)(int)B),
                        };
                        _stack.Push(res);
                        break;
                    }

                case OpCode.BIT_AND:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        if (!(l is int || l is long || l is uint || l is ulong) || !(r is int || r is long || r is uint || r is ulong))
                            throw new VMException("BIT_AND requires integral types (int/long/uint/ulong)", instr.Line, instr.Col, instr.OriginFile);

                        if (l is ulong || r is ulong || l is long || r is long)
                            _stack.Push(Convert.ToInt64(l) & Convert.ToInt64(r));
                        else
                            _stack.Push(Convert.ToInt32(l) & Convert.ToInt32(r));
                        break;
                    }

                case OpCode.BIT_OR:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        if (!(l is int || l is long || l is uint || l is ulong) || !(r is int || r is long || r is uint || r is ulong))
                            throw new VMException("BIT_OR requires integral types (int/long/uint/ulong)", instr.Line, instr.Col, instr.OriginFile);

                        if (l is ulong || r is ulong || l is long || r is long)
                            _stack.Push(Convert.ToInt64(l) | Convert.ToInt64(r));
                        else
                            _stack.Push(Convert.ToInt32(l) | Convert.ToInt32(r));
                        break;
                    }

                case OpCode.BIT_XOR:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        if (!(l is int || l is long || l is uint || l is ulong) || !(r is int || r is long || r is uint || r is ulong))
                            throw new VMException("BIT_XOR requires integral types (int/long/uint/ulong)", instr.Line, instr.Col, instr.OriginFile);

                        if (l is ulong || r is ulong || l is long || r is long)
                            _stack.Push(Convert.ToInt64(l) ^ Convert.ToInt64(r));
                        else
                            _stack.Push(Convert.ToInt32(l) ^ Convert.ToInt32(r));
                        break;
                    }

                case OpCode.SHL:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        if (!(l is int || l is long || l is uint || l is ulong) || !IsNumber(r))
                            throw new VMException("SHL requires (int|long|uint|ulong) << int", instr.Line, instr.Col, instr.OriginFile);

                        int sh = Convert.ToInt32(r) & 0x3F;
                        if (l is long or ulong)
                            _stack.Push(Convert.ToInt64(l) << sh);
                        else
                            _stack.Push(Convert.ToInt32(l) << (sh & 0x1F));
                        break;
                    }

                case OpCode.SHR:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        if (!(l is int || l is long || l is uint || l is ulong) || !IsNumber(r))
                            throw new VMException("SHR requires (int|long|uint|ulong) >> int", instr.Line, instr.Col, instr.OriginFile);

                        int sh = Convert.ToInt32(r) & 0x3F;
                        if (l is long or ulong)
                            _stack.Push(Convert.ToInt64(l) >> sh);
                        else
                            _stack.Push(Convert.ToInt32(l) >> (sh & 0x1F));
                        break;
                    }

                case OpCode.EQ:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        bool res;
                        if (IsNumber(l) && IsNumber(r))
                            res = CompareAsDecimal(l) == CompareAsDecimal(r);
                        else if (l is string ls && r is string rs)
                            res = string.Equals(ls, rs, StringComparison.Ordinal);
                        else
                            res = Equals(l, r);
                        _stack.Push(res);
                        break;
                    }

                case OpCode.NEQ:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        bool res;
                        if (IsNumber(l) && IsNumber(r))
                            res = CompareAsDecimal(l) != CompareAsDecimal(r);
                        else if (l is string ls && r is string rs)
                            res = !string.Equals(ls, rs, StringComparison.Ordinal);
                        else
                            res = !Equals(l, r);
                        _stack.Push(res);
                        break;
                    }

                case OpCode.LT:
                    {
                        var r = _stack.Pop(); var l = _stack.Pop();

                        if (IsNumber(l) && IsNumber(r))
                        {
                            var (A, B, K) = CoercePair(l, r);
                            bool v = K switch
                            {
                                NumKind.Decimal => (decimal)A < (decimal)B,
                                NumKind.Double => (double)A < (double)B,
                                NumKind.UInt64 => (ulong)A < (ulong)B,
                                NumKind.Int64 => (long)A < (long)B,
                                _ => (int)A < (int)B,
                            };
                            _stack.Push(v);
                        }
                        else if (l is string ls && r is string rs)
                        {
                            _stack.Push(string.CompareOrdinal(ls, rs) < 0);
                        }
                        else
                        {
                            throw new VMException("Runtime error: LT on non-comparable types", instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }

                case OpCode.GT:
                    {
                        var r = _stack.Pop(); var l = _stack.Pop();

                        if (IsNumber(l) && IsNumber(r))
                        {
                            var (A, B, K) = CoercePair(l, r);
                            bool v = K switch
                            {
                                NumKind.Decimal => (decimal)A > (decimal)B,
                                NumKind.Double => (double)A > (double)B,
                                NumKind.UInt64 => (ulong)A > (ulong)B,
                                NumKind.Int64 => (long)A > (long)B,
                                _ => (int)A > (int)B,
                            };
                            _stack.Push(v);
                        }
                        else if (l is string ls && r is string rs)
                        {
                            _stack.Push(string.CompareOrdinal(ls, rs) > 0);
                        }
                        else
                        {
                            throw new VMException("Runtime error: GT on non-comparable types", instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }

                case OpCode.LE:
                    {
                        var r = _stack.Pop(); var l = _stack.Pop();

                        if (IsNumber(l) && IsNumber(r))
                        {
                            var (A, B, K) = CoercePair(l, r);
                            bool v = K switch
                            {
                                NumKind.Decimal => (decimal)A <= (decimal)B,
                                NumKind.Double => (double)A <= (double)B,
                                NumKind.UInt64 => (ulong)A <= (ulong)B,
                                NumKind.Int64 => (long)A <= (long)B,
                                _ => (int)A <= (int)B,
                            };
                            _stack.Push(v);
                        }
                        else if (l is string ls && r is string rs)
                        {
                            _stack.Push(string.CompareOrdinal(ls, rs) <= 0);
                        }
                        else
                        {
                            throw new VMException("Runtime error: LE on non-comparable types", instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }

                case OpCode.GE:
                    {
                        var r = _stack.Pop(); var l = _stack.Pop();

                        if (IsNumber(l) && IsNumber(r))
                        {
                            var (A, B, K) = CoercePair(l, r);
                            bool v = K switch
                            {
                                NumKind.Decimal => (decimal)A >= (decimal)B,
                                NumKind.Double => (double)A >= (double)B,
                                NumKind.UInt64 => (ulong)A >= (ulong)B,
                                NumKind.Int64 => (long)A >= (long)B,
                                _ => (int)A >= (int)B,
                            };
                            _stack.Push(v);
                        }
                        else if (l is string ls && r is string rs)
                        {
                            _stack.Push(string.CompareOrdinal(ls, rs) >= 0);
                        }
                        else
                        {
                            throw new VMException("Runtime error: GE on non-comparable types", instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }

                case OpCode.NEG:
                    {
                        var v = _stack.Pop();
                        if (!IsNumber(v))
                            throw new VMException(
                                $"NEG only works on numeric types (got {v ?? "null"} of type {v?.GetType().Name ?? "null"})",
                                instr.Line, instr.Col, instr.OriginFile
                            );

                        object res =
                            v is decimal md ? (object)(-md) :
                            v is double dd ? (object)(-dd) :
                            v is float ff ? (object)(-ff) :
                            v is long ll ? (object)(-ll) :
                            v is ulong uu ? (object)unchecked((long)-(long)uu) :
                            (object)(-Convert.ToInt32(v));

                        _stack.Push(res);
                        break;
                    }

                case OpCode.NOT:
                    {
                        var v = _stack.Pop();
                        _stack.Push(!ToBool(v));
                        break;
                    }

                case OpCode.DUP:
                    {
                        var v = _stack.Peek();
                        _stack.Push(v);
                        break;
                    }

                case OpCode.POP:
                    {
                        _stack.Pop();
                        break;
                    }

                case OpCode.AND:
                    {
                        var r = _stack.Pop(); var l = _stack.Pop();
                        bool lb = ToBool(l); bool rb = ToBool(r);
                        _stack.Push(lb && rb);
                        break;
                    }

                case OpCode.OR:
                    {
                        var r = _stack.Pop(); var l = _stack.Pop();
                        bool lb = ToBool(l); bool rb = ToBool(r);
                        _stack.Push(lb || rb);
                        break;
                    }

                case OpCode.LABEL:
                    return StepResult.Continue;

                case OpCode.JMP:
                    {
                        if (instr.Operand is null)
                            throw new VMException("Runtime error: JMP missing target", instr.Line, instr.Col, instr.OriginFile);
                        _ip = (int)instr.Operand;
                        return StepResult.Continue;
                    }

                case OpCode.JMP_IF_FALSE:
                    {
                        if (instr.Operand is null)
                            throw new VMException("Runtime error: JMP_IF_FALSE missing target", instr.Line, instr.Col, instr.OriginFile);
                        var v = _stack.Pop();
                        if (!ToBool(v))
                        {
                            _ip = (int)instr.Operand;
                            return StepResult.Continue;
                        }
                        break;
                    }

                case OpCode.JMP_IF_TRUE:
                    {
                        if (instr.Operand is null)
                            throw new VMException("Runtime error: JMP_IF_TRUE missing target", instr.Line, instr.Col, instr.OriginFile);
                        var v = _stack.Pop();
                        if (ToBool(v))
                        {
                            _ip = (int)instr.Operand;
                            return StepResult.Continue;
                        }
                        break;
                    }

                case OpCode.HALT:
                    return StepResult.Halt;

                case OpCode.PUSH_CLOSURE:
                    {
                        if (instr.Operand == null)
                            throw new VMException($"Runtime error: PUSH_CLOSURE without operand", instr.Line, instr.Col, instr.OriginFile);

                        int funcAddr;
                        string? funcName = null;

                        switch (instr.Operand)
                        {
                            case int i:
                                funcAddr = i;
                                break;
                            case object[] arr when arr.Length >= 2:
                                funcAddr = (int)arr[0];
                                funcName = arr[1]?.ToString() ?? "";
                                break;
                            default:
                                throw new VMException($"Runtime error: Invalid PUSH_CLOSURE operand type {instr.Operand.GetType().Name}", instr.Line, instr.Col, instr.OriginFile);
                        }

                        var funcInfo = _functions.Values.FirstOrDefault(f => f.Address == funcAddr);
                        if (funcInfo == null)
                            throw new VMException($"Runtime error: PUSH_CLOSURE unknown function address {funcAddr}", instr.Line, instr.Col, instr.OriginFile);

                        var capturedEnv = _scopes[^1];
                        _stack.Push(new Closure(funcAddr, funcInfo.Parameters, capturedEnv, funcName ?? throw new VMException("Invalid function-name", instr.Line, instr.Col, instr.OriginFile)));
                        break;
                    }

                case OpCode.CALL:
                    {
                        if (instr.Operand is string funcName)
                        {
                            if (bInFunc.TryGetValue(funcName, out int expectedArgs))
                            {
                                var args = new List<object>();
                                for (int i = expectedArgs - 1; i >= 0; i--) args.Insert(0, _stack.Pop());
                                var result = CallBuiltin(funcName, args, instr);
                                _stack.Push(result);
                                break;
                            }

                            if (!_functions.TryGetValue(funcName, out var func))
                                throw new VMException($"Runtime error: unknown function {funcName}", instr.Line, instr.Col, instr.OriginFile);

                            if (func.Parameters.Count > 0 && func.Parameters[0] == "this")
                                throw new VMException(
                                    $"Runtime error: cannot CALL method '{funcName}' without receiver. Use CALL_INDIRECT with a bound receiver.",
                                    instr.Line, instr.Col, instr.OriginFile);

                            _stack.Push(new Closure(func.Address, func.Parameters, _scopes[^1], funcName));
                            goto case OpCode.CALL_INDIRECT;
                        }
                        else
                        {
                            goto case OpCode.CALL_INDIRECT;
                        }
                    }

                case OpCode.CALL_INDIRECT:
                    {
                        if (instr.Operand is IConvertible)
                        {
                            int explicitArgCount = Convert.ToInt32(instr.Operand);
                            var argsList = new List<object>();
                            for (int i = 0; i < explicitArgCount; i++)
                            {
                                if (_stack.Count == 0)
                                    throw new VMException($"Runtime error: not enough arguments for CALL_INDIRECT (expected {explicitArgCount})", instr.Line, instr.Col, instr.OriginFile);
                                argsList.Add(_stack.Pop());
                            }

                            if (_stack.Count == 0)
                                throw new VMException("Runtime error: missing callee for CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile);

                            var callee = _stack.Pop();
                            Closure f;
                            object? receiver = null;

                            if (callee is IntrinsicBound ib_ex)
                            {
                                if (explicitArgCount < ib_ex.Method.ArityMin || explicitArgCount > ib_ex.Method.ArityMax)
                                    throw new VMException(
                                        $"Runtime error: {ib_ex.Method.Name} expects {ib_ex.Method.ArityMin}..{ib_ex.Method.ArityMax} args, got {explicitArgCount}",
                                        instr.Line, instr.Col, instr.OriginFile
                                    );

                                var result = ib_ex.Method.Invoke(ib_ex.Receiver, argsList, instr);
                                _stack.Push(result);
                                return StepResult.Continue;
                            }
                            else if (callee is BoundMethod bm)
                            {
                                f = bm.Function;
                                receiver = bm.Receiver;

                                if (f.Parameters.Count > 0 && f.Parameters[0] == "this")
                                {
                                    if (argsList.Count > 0 && Equals(argsList[0], receiver))
                                        throw new VMException("Runtime error: receiver provided twice (BoundMethod already has 'this').", instr.Line, instr.Col, instr.OriginFile);
                                }
                            }
                            else if (callee is Closure clos)
                            {
                                f = clos;

                                if (f.Parameters.Count > 0 && f.Parameters[0] == "this")
                                {
                                    if (argsList.Count == 0)
                                        throw new VMException("Runtime error: missing 'this' for method call.", instr.Line, instr.Col, instr.OriginFile);
                                    receiver = argsList[0];
                                    argsList.RemoveAt(0);
                                }
                            }
                            else
                            {
                                throw new VMException($"Runtime error: attempt to call non-function value ({instr.Code} )", instr.Line, instr.Col, instr.OriginFile);
                            }

                            var callEnv = new Env(f.CapturedEnv);
                            int piStart = (f.Parameters.Count > 0 && f.Parameters[0] == "this") ? 1 : 0;
                            if (argsList.Count < f.Parameters.Count - piStart)
                                throw new VMException("Runtime error: insufficient args for call", instr.Line, instr.Col, instr.OriginFile);

                            for (int pi = piStart, ai = 0; pi < f.Parameters.Count; pi++, ai++)
                                callEnv.Define(f.Parameters[pi], argsList[ai]);

                            _scopes.Add(callEnv);
                            _callStack.Push(new CallFrame(_ip, 1, receiver));

                            _ip = f.Address;
                            return StepResult.Continue;
                        }
                        else
                        {
                            if (_stack.Count == 0)
                                throw new VMException("Runtime error: missing callee for CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile);

                            var callee = _stack.Pop();
                            Closure f;
                            object? receiver = null;

                            if (callee is IntrinsicBound ib)
                            {
                                int need = ib.Method.ArityMin;
                                var argsB = new List<object>();
                                for (int i = 0; i < need; i++)
                                {
                                    if (_stack.Count == 0)
                                        throw new VMException("Runtime error: insufficient args for intrinsic call", instr.Line, instr.Col, instr.OriginFile);
                                    argsB.Add(_stack.Pop());
                                }
                                var result = ib.Method.Invoke(ib.Receiver, argsB, instr);
                                _stack.Push(result);
                                return StepResult.Continue;
                            }
                            else if (callee is BoundMethod bm)
                            {
                                f = bm.Function;
                                receiver = bm.Receiver;
                            }
                            else if (callee is Closure clos)
                            {
                                f = clos;

                                if (f.Parameters.Count > 0 && f.Parameters[0] == "this")
                                {
                                    if (_stack.Count == 0)
                                        throw new VMException("Runtime error: missing 'this' for method call.", instr.Line, instr.Col, instr.OriginFile);
                                    receiver = _stack.Pop();
                                }
                            }
                            else
                            {
                                throw new VMException($"Runtime error: attempt to call non-function value ( {instr.Code} )", instr.Line, instr.Col, instr.OriginFile);
                            }

                            int piStart = (f.Parameters.Count > 0 && f.Parameters[0] == "this") ? 1 : 0;
                            var argsList = new List<object>();
                            for (int pi = f.Parameters.Count - 1; pi >= piStart; pi--)
                            {
                                if (_stack.Count == 0)
                                    throw new VMException("Runtime error: insufficient args for call", instr.Line, instr.Col, instr.OriginFile);
                                argsList.Insert(0, _stack.Pop());
                            }

                            var callEnv = new Env(f.CapturedEnv);
                            for (int pi = piStart, ai = 0; pi < f.Parameters.Count; pi++, ai++)
                                callEnv.Define(f.Parameters[pi], argsList[ai]);

                            _scopes.Add(callEnv);
                            _callStack.Push(new CallFrame(_ip, 1, receiver));
                            _ip = f.Address;
                            return StepResult.Continue;
                        }
                    }

                case OpCode.RET:
                    {
                        var retVal = _stack.Pop();

                        var fr = _callStack.Pop();

                        for (int i = 0; i < fr.ScopesAdded; i++)
                            _scopes.RemoveAt(_scopes.Count - 1);

                        _ip = fr.ReturnIp;

                        _stack.Push(retVal);
                        return StepResult.Continue;
                    }

                case OpCode.TRY_PUSH:
                    {
                        var arr = instr.Operand as int[] ?? new[] { -1, -1 };
                        int catchAddr = arr.Length > 0 ? arr[0] : -1;
                        int finallyAddr = arr.Length > 1 ? arr[1] : -1;
                        _tryHandlers.Add(new TryHandler(catchAddr, finallyAddr));
                        break;
                    }

                case OpCode.TRY_POP:
                    {
                        if (_tryHandlers.Count == 0)
                            throw new VMException("Runtime error: TRY_POP with empty try stack", instr.Line, instr.Col, instr.OriginFile);
                        _tryHandlers.RemoveAt(_tryHandlers.Count - 1);
                        break;
                    }

                case OpCode.THROW:
                    {
                        var any = _stack.Pop();

                        var payload = any as ExceptionObject
                            ?? new ExceptionObject(
                                   type: "UserError",
                                    message: any?.ToString() ?? "error",
                                    file: instr.OriginFile,
                                    line: instr.Line,
                                    col: instr.Col,
                                    stack: BuildStackString(_insns, instr)
                               );

                        if (RouteExceptionToTryHandlers(payload, instr, out var nip))
                        { _ip = nip; return StepResult.Continue; }

                        throw new VMException($"Uncaught exception: {payload}", instr.Line, instr.Col, instr.OriginFile);
                    }

                case OpCode.END_FINALLY:
                    {
                        if (_tryHandlers.Count == 0) break;

                        var h = _tryHandlers[^1];
                        _tryHandlers.RemoveAt(_tryHandlers.Count - 1);

                        if (h.Exception != null)
                        {
                            var any = h.Exception;
                            h.Exception = null;

                            var payload = any as ExceptionObject
                                ?? new ExceptionObject(
                                       type: "UserError",
                                       message: any?.ToString() ?? "error",
                                       file: instr.OriginFile,
                                       line: instr.Line,
                                       col: instr.Col,
                                       stack: BuildStackString(_insns, instr)
                                   );

                            if (RouteExceptionToTryHandlers(payload, instr, out var nip))
                            { _ip = nip; return StepResult.Continue; }

                            throw new VMException($"Uncaught exception: {payload}", instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }

                default:
                    throw new VMException($"Runtime error: unknown opcode {instr.Code}", instr.Line, instr.Col, instr.OriginFile);
            }

            return StepResult.Next;
        }

        /// <summary>
        /// The LoadInstructions
        /// </summary>
        /// <param name="inst">The inst<see cref="List{Instruction}"/></param>
        public void LoadInstructions(List<Instruction> inst)
        {
            _program = inst;
        }

        /// <summary>
        /// The TryGetVar
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public bool TryGetVar(string name, out object? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(name)) return false;

            var env = FindEnvWithLocal(name);
            if (env == null) return false;

            return env.Vars.TryGetValue(name, out value);
        }

        /// <summary>
        /// The GetVar
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="object?"/></returns>
        public object? GetVar(string name)
        {
            if (TryGetVar(name, out var v)) return v;
            throw new VMException($"Runtime error: undefined variable '{name}'", 0, 0, "<host>");
        }

        /// <summary>
        /// The GetVar
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="T"/></returns>
        public T GetVar<T>(string name)
        {
            var v = GetVar(name);
            if (v is T t) return t;
            throw new VMException($"Runtime error: variable '{name}' is not of type {typeof(T).Name}", 0, 0, "<host>");
        }

        /// <summary>
        /// The SetVar
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object?"/></param>
        /// <param name="defineIfMissing">The defineIfMissing<see cref="bool"/></param>
        /// <param name="toGlobal">The toGlobal<see cref="bool"/></param>
        public void SetVar(string name, object? value, bool defineIfMissing = true, bool toGlobal = false)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Variable name must not be empty.", nameof(name));

            if (string.Equals(name, "this", StringComparison.Ordinal))
                throw new VMException("Runtime error: cannot assign/declare 'this'", 0, 0, "<host>");

            var env = FindEnvWithLocal(name);
            if (env != null)
            {
                env.Vars[name] = value!;
                return;
            }

            if (!defineIfMissing)
                throw new VMException($"Runtime error: assignment to undeclared variable '{name}'", 0, 0, "<host>");

            var target = toGlobal ? _scopes[0] : _scopes[^1];
            if (target.HasLocal(name))
                target.Vars[name] = value!;
            else
                target.Define(name, value!);
        }

        /// <summary>
        /// The SetVars
        /// </summary>
        /// <param name="vars">The vars<see cref="IDictionary{string, object?}"/></param>
        /// <param name="defineIfMissing">The defineIfMissing<see cref="bool"/></param>
        /// <param name="toGlobal">The toGlobal<see cref="bool"/></param>
        public void SetVars(IDictionary<string, object?> vars, bool defineIfMissing = true, bool toGlobal = false)
        {
            foreach (var kv in vars)
                SetVar(kv.Key, kv.Value, defineIfMissing, toGlobal);
        }

        /// <summary>
        /// The CallFunction
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="args">The args<see cref="object?[]"/></param>
        /// <returns>The <see cref="object?"/></returns>
        public object? CallFunction(string name, params object?[] args)
        {
            int _ip = 0;
            if (_program == null || _program.Count == 0)
                throw new InvalidOperationException("No program loaded. Call LoadProgram(...) first.");

            if (!_functions.TryGetValue(name, out var fInfo))
                throw new VMException($"Runtime error: function '{name}' not found.", 0, 0, "<host>");

            if (fInfo.Parameters.Count > 0 && fInfo.Parameters[0] == "this")
                throw new VMException(
                    $"Runtime error: function '{name}' requires 'this'. Use CallFunctionWithThis(...).",
                    0, 0, "<host>");

            var clos = new Closure(fInfo.Address, fInfo.Parameters, _scopes[^1], name);

            if (args.Length < clos.Parameters.Count)
                throw new VMException(
                    $"Runtime error: insufficient args for '{name}' (need {clos.Parameters.Count}, have {args.Length})",
                    0, 0, "<host>");

            var callEnv = new Env(clos.CapturedEnv);
            for (int i = 0; i < clos.Parameters.Count; i++)
                callEnv.Define(clos.Parameters[i], args[i]);

            _scopes.Add(callEnv);
            _callStack.Push(new CallFrame(-1, 1, null));

            int savedIp = _ip;
            _ip = clos.Address;

            object? result = null;
            bool finished = false;

            try
            {
                while (true)
                {
                    if (_ip < 0)
                    {
                        result = _stack.Count > 0 ? _stack.Pop() : null;
                        finished = true;
                        break;
                    }
                    if (_ip >= _program.Count)
                        throw new VMException("Runtime error: IP out of bounds during CallFunction", 0, 0, "<host>");

                    var instr = _program[_ip++];
                    var step = HandleInstruction(ref _ip, _program, instr);
                    if (step == StepResult.Halt) { finished = true; break; }
                }
            }
            finally
            {
                if (!finished)
                {
                    if (_scopes.Count > 0) _scopes.RemoveAt(_scopes.Count - 1);
                    if (_callStack.Count > 0) _callStack.Pop();
                }
                _ip = savedIp;
            }

            return result;
        }

        /// <summary>
        /// The Run
        /// </summary>
        /// <param name="debugging">The debugging<see cref="bool"/></param>
        /// <param name="lastPos">The lastPos<see cref="int"/></param>
        public void Run(bool debugging = false, int lastPos = 0)
        {
            if (_program is null || _program.Count == 0) return;

            bool routed = false;
            DebugStream = new MemoryStream();
            int _ip = lastPos;

            while (_ip < _program.Count)
            {
                try
                {
                    if (debugging)
                    {
                        DebugStream.Write(System.Text.Encoding.Default.GetBytes(
                            $"[DEBUG] {_program[_ip].Line} ->  IP={_ip}, STACK=[{string.Join(", ", _stack.Reverse())}], SCOPES={_scopes.Count}, CALLSTACK={_callStack.Count}\n"));
                        DebugStream.Write(System.Text.Encoding.Default.GetBytes(
                            $"[DEBUG] {_program[_ip]} (Line {_program[_ip].Line}, Col {_program[_ip].Col})\n"));
                    }

                    var instr = _program[_ip++];
                    var res = HandleInstruction(ref _ip, _program, instr);

                    if (res == StepResult.Halt) return;
                    if (res == StepResult.Continue) continue;
                }
                catch (VMException ex)
                {
                    var payload = new ExceptionObject(
                        type: "RuntimeError",
                        message: ex.Message,
                        file: _program[_ip].OriginFile,
                        line: _program[_ip].Line,
                        col: _program[_ip].Col,
                        stack: BuildStackString(_program, _program[_ip])
                    );

                    if (RouteExceptionToTryHandlers(payload, _program[_ip], out var nip))
                    {
                        _ip = nip;
                        routed = true;
                    }
                    else
                    {
                        throw;
                    }
                }

                if (routed)
                {
                    routed = false;
                    continue;
                }
            }
        }

        /// <summary>
        /// The BuildStackString
        /// </summary>
        /// <param name="insns">The insns<see cref="List{Instruction}"/></param>
        /// <param name="current">The current<see cref="Instruction"/></param>
        /// <returns>The <see cref="string"/></returns>
        private string BuildStackString(List<Instruction> insns, Instruction current)
        {
            var sb = new StringBuilder();

            sb.Append("  at ")
              .Append(current.OriginFile).Append(':')
              .Append(current.Line).Append(':')
              .Append(current.Col).AppendLine();

            foreach (var frame in _callStack.Reverse())
            {
                int ip = Math.Clamp(frame.ReturnIp, 0, insns.Count - 1);
                var i = insns[ip];
                sb.Append("  at ")
                  .Append(i.OriginFile).Append(':')
                  .Append(i.Line).Append(':')
                  .Append(i.Col).AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// The SafeCurrentInstr
        /// </summary>
        /// <param name="insns">The insns<see cref="List{Instruction}"/></param>
        /// <param name="ip">The ip<see cref="int"/></param>
        /// <returns>The <see cref="Instruction"/></returns>
        private static Instruction SafeCurrentInstr(List<Instruction> insns, int ip)
        {
            if (insns == null || insns.Count == 0)
                return new Instruction(OpCode.HALT, null, -1, -1, "");
            int i = ip;
            if (i < 0) i = 0;
            if (i >= insns.Count) i = insns.Count - 1;
            return insns[i];
        }

        /// <summary>
        /// The RouteExceptionToTryHandlers
        /// </summary>
        /// <param name="exPayload">The exPayload<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <param name="newIp">The newIp<see cref="int"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool RouteExceptionToTryHandlers(object exPayload, Instruction instr, out int newIp)
        {
            for (int i = _tryHandlers.Count - 1; i >= 0; i--)
            {
                var h = _tryHandlers[i];
                if (h.CatchAddr >= 0)
                {
                    _stack.Push(exPayload);
                    newIp = h.CatchAddr;
                    h.CatchAddr = -1;
                    return true;
                }
                else if (h.FinallyAddr >= 0)
                {
                    h.Exception = exPayload;
                    newIp = h.FinallyAddr;
                    return true;
                }
                else
                {
                    _tryHandlers.RemoveAt(i);
                }
            }
            newIp = -1;
            return false;
        }

        /// <summary>
        /// The BuildCrashReport
        /// </summary>
        /// <param name="scriptname">The scriptname<see cref="string"/></param>
        /// <param name="instr">The instr<see cref="Instruction?"/></param>
        /// <param name="ipAfterFetch">The ipAfterFetch<see cref="int"/></param>
        /// <param name="ex">The ex<see cref="Exception"/></param>
        /// <returns>The <see cref="string"/></returns>
        private string BuildCrashReport(string scriptname, Instruction? instr, int ipAfterFetch, Exception ex)
        {
            var sb = new StringBuilder();
            int ipAtFault = Math.Max(0, ipAfterFetch - 1);

            sb.AppendLine($"  at IP={ipAtFault} {(instr != null ? instr.Code.ToString() : "<no-op>")}");
            if (instr != null)
            {
                sb.AppendLine($"  Operand: {instr.Operand ?? "null"}");
                sb.AppendLine($"  Source : {instr.OriginFile ?? scriptname} [{instr.Line},{instr.Col}]");
            }

            sb.AppendLine("  Stack  : " + DumpStack());

            sb.AppendLine("  Frames : " + DumpCallStack());

            sb.AppendLine($"  Cause  : {ex.GetType().Name}");
            return sb.ToString();
        }

        /// <summary>
        /// The BuildCrashReport
        /// </summary>
        /// <param name="scriptname">The scriptname<see cref="string"/></param>
        /// <param name="instr">The instr<see cref="Instruction?"/></param>
        /// <param name="ipAfterFetch">The ipAfterFetch<see cref="int"/></param>
        /// <param name="ex">The ex<see cref="VMException"/></param>
        /// <returns>The <see cref="string"/></returns>
        private string BuildCrashReport(string scriptname, Instruction? instr, int ipAfterFetch, VMException ex)
        {
            return BuildCrashReport(scriptname, instr, ipAfterFetch, (Exception)ex);
        }

        /// <summary>
        /// The DumpStack
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        private string DumpStack()
        {
            if (_stack == null || _stack.Count == 0) return "<empty>";
            var arr = _stack.ToArray();
            var parts = arr.Select(FormatVal);
            return string.Join(" | ", parts);
        }

        /// <summary>
        /// The DumpCallStack
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        private string DumpCallStack()
        {
            if (_callStack == null || _callStack.Count == 0) return "<empty>";
            var arr = _callStack.ToArray();
            var parts = arr.Select((fr, i) =>
                $"#{i}: ret={fr.ReturnIp}, scopes+={fr.ScopesAdded}, this={(fr.ThisRef != null ? FormatVal(fr.ThisRef) : "null")}");
            return string.Join(" ; ", parts);
        }

        /// <summary>
        /// The FormatVal
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <returns>The <see cref="string"/></returns>
        private string FormatVal(object? v)
        {
            if (v == null) return "null";
            switch (v)
            {
                case string s:
                    return $"\"{s}\"";
                case List<object> list:
                    return $"[{list.Count} elems]";
                case Dictionary<string, object> dict:
                    return $"{{{dict.Count} pairs}}";
                case ClassInstance ci:
                    return $"Object({ci.ClassName})";
                case Closure clos:
                    return $"Closure({clos.Name ?? clos.Address.ToString()})";
                case BoundMethod bm:
                    return $"BoundMethod({bm.Function.Name ?? bm.Function.Address.ToString()})";
                default:
                    return $"{v} : {v.GetType().Name}";
            }
        }

        /// <summary>
        /// The JsonEscapeString
        /// </summary>
        /// <param name="s">The s<see cref="string"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string JsonEscapeString(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ')
                            sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:X4}", (int)ch);
                        else
                            sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// The WriteJsonValue
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <param name="w">The w<see cref="TextWriter"/></param>
        /// <param name="seen">The seen<see cref="HashSet{object}?"/></param>
        /// <param name="mode">The mode<see cref="int"/></param>
        private static void WriteJsonValue(object? v, TextWriter w, HashSet<object>? seen = null, int mode = 2)
        {
            seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

            if (v is null) { w.Write("null"); return; }

            switch (v)
            {
                case bool b:
                    w.Write(b ? "true" : "false"); return;

                case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    w.Write(Convert.ToString(v, CultureInfo.InvariantCulture)); return;

                case string s:
                    w.Write('"'); w.Write(JsonEscapeString(s)); w.Write('"'); return;

                case ExceptionObject exo:
                    {
                        w.Write('{');
                        w.Write("\"type\":\""); w.Write(JsonEscapeString(exo.Type)); w.Write("\",");
                        w.Write("\"message\":\""); w.Write(JsonEscapeString(exo.Message)); w.Write("\",");
                        w.Write("\"file\":\""); w.Write(JsonEscapeString(exo.File)); w.Write("\",");
                        w.Write("\"line\":"); w.Write(exo.Line.ToString(CultureInfo.InvariantCulture)); w.Write(",");
                        w.Write("\"col\":"); w.Write(exo.Col.ToString(CultureInfo.InvariantCulture));
                        if (!string.IsNullOrEmpty(exo.Stack))
                        {
                            w.Write(",\"stack\":\""); w.Write(JsonEscapeString(exo.Stack)); w.Write('"');
                        }
                        w.Write('}');
                        return;
                    }

                case List<object> list:
                    {
                        if (seen.Contains(v)) { w.Write("[]"); return; }
                        seen.Add(v);
                        w.Write('[');
                        for (int i = 0; i < list.Count; i++)
                        {
                            WriteJsonValue(list[i], w, seen, mode);
                            if (i + 1 < list.Count) w.Write(',');
                        }
                        w.Write(']');
                        seen.Remove(v);
                        return;
                    }

                case Dictionary<string, object> dict:
                    {
                        if (seen.Contains(v)) { w.Write("{}"); return; }
                        seen.Add(v);

                        var entries = dict.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
                        if (mode == 2)
                        {
                            entries = [.. entries
                    .Where(kv => kv.Value != null
                                 && kv.Value is not Closure
                                 && kv.Value is not FunctionInfo
                                 && kv.Value is not Delegate)];
                        }

                        w.Write('{');
                        for (int i = 0; i < entries.Count; i++)
                        {
                            var kv = entries[i];
                            w.Write('"'); w.Write(JsonEscapeString(kv.Key)); w.Write("\":");
                            WriteJsonValue(kv.Value, w, seen, mode);
                            if (i + 1 < entries.Count) w.Write(',');
                        }
                        w.Write('}');
                        seen.Remove(v);
                        return;
                    }

                case Closure clos:
                    w.Write('"'); w.Write(JsonEscapeString(clos.Name ?? "<closure>")); w.Write('"'); return;

                default:
                    w.Write('"'); w.Write(JsonEscapeString(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "")); w.Write('"');
                    return;
            }
        }

        /// <summary>
        /// The JsonStringify
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <param name="mode">The mode<see cref="int"/></param>
        /// <returns>The <see cref="string"/></returns>
        public static string JsonStringify(object? v, int mode = 2)
        {
            var sb = new StringBuilder();
            using var sw = new StringWriter(sb, CultureInfo.InvariantCulture);
            WriteJsonValue(v, sw, null, mode);
            return sb.ToString();
        }

        /// <summary>
        /// The PrintValue
        /// </summary>
        /// <param name="v">The v<see cref="object"/></param>
        /// <param name="w">The w<see cref="TextWriter"/></param>
        /// <param name="mode">The mode<see cref="int"/></param>
        /// <param name="seen">The seen<see cref="HashSet{object}?"/></param>
        /// <param name="escapeNewlines">The escapeNewlines<see cref="bool"/></param>
        private static void PrintValue(object v, TextWriter w, int mode = 2, HashSet<object>? seen = null, bool escapeNewlines = true)
        {
            static string UnescapeForPrinting(string s)
            {
                return s
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t")
                    .Replace("\\b", "\b")
                    .Replace("\\f", "\f");
            }

            seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

            if (v == null) { w.Write("null"); return; }

            if (v is List<object> list)
            {
                if (seen.Contains(v)) { w.Write("[...]"); return; }
                seen.Add(v);
                w.Write("[");
                for (int i = 0; i < list.Count; i++)
                {
                    PrintValue(list[i], w, mode, seen, escapeNewlines);
                    if (i + 1 < list.Count) w.Write(", ");
                }
                w.Write("]");
                seen.Remove(v);
                return;
            }

            if (v is Dictionary<string, object> dict)
            {
                if (seen.Contains(v)) { w.Write("{...}"); return; }
                seen.Add(v);

                var entries = dict.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
                if (mode == 2)
                {
                    entries = [.. entries
                .Where(kv => kv.Value != null
                             && kv.Value is not Closure
                             && kv.Value is not FunctionInfo
                             && kv.Value is not Delegate)];
                }

                w.Write("{");
                for (int i = 0; i < entries.Count; i++)
                {
                    var kv = entries[i];
                    w.Write("\"");
                    w.Write(escapeNewlines ? UnescapeForPrinting(kv.Key) : kv.Key);
                    w.Write("\": ");
                    PrintValue(kv.Value, w, mode, seen, escapeNewlines);
                    if (i + 1 < entries.Count) w.Write(", ");
                }
                w.Write("}");
                seen.Remove(v);
                return;
            }

            if (v is Closure clos)
            {
                if (mode == 2)
                    w.Write("\"" + (escapeNewlines ? UnescapeForPrinting(clos.Name ?? "<closure>") : (clos.Name ?? "<closure>")) + "\"");
                else
                    w.Write(clos.ToString());
                return;
            }

            if (v is ExceptionObject exo)
            {
                if (mode == 2)
                    w.Write($"\"{exo.Type}: {exo.Message}\"");
                else
                    w.Write(exo.ToString());
                return;
            }

            if (v is FunctionInfo fi) { w.Write($"<fn {fi.Address}>"); return; }
            if (v is Delegate) { w.Write("<delegate>"); return; }

            switch (v)
            {
                case string s:
                    w.Write(escapeNewlines ? UnescapeForPrinting(s) : s);
                    break;
                case double xd: w.Write(xd.ToString(CultureInfo.InvariantCulture)); break;
                case float f: w.Write(f.ToString(CultureInfo.InvariantCulture)); break;
                case decimal m: w.Write(m.ToString(CultureInfo.InvariantCulture)); break;
                case long l: w.Write(l.ToString(CultureInfo.InvariantCulture)); break;
                case int i: w.Write(i.ToString(CultureInfo.InvariantCulture)); break;
                case bool b: w.Write(b ? "true" : "false"); break;
                default:
                    w.Write(Convert.ToString(v, CultureInfo.InvariantCulture));
                    break;
            }
            w.Flush();
        }

        /// <summary>
        /// The GetIndexedValue
        /// </summary>
        /// <param name="target">The target<see cref="object"/></param>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="object"/></returns>
        private static object GetIndexedValue(object target, object idxObj, Instruction instr)
        {
            switch (target)
            {
                case List<object> arr:
                    {

                        if (idxObj is string mname && ArrayProto.TryGetValue(mname, out var imArr))
                            return new IntrinsicBound(imArr, arr);

                        int index = Convert.ToInt32(idxObj);
                        if (index < 0 || index >= arr.Count)
                            throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                        return arr[index];
                    }

                case FileHandle fh:
                    {
                        if (idxObj is string mname && FileProto.TryGetValue(mname, out var im))
                            return new IntrinsicBound(im, fh);
                        throw new VMException($"invalid file member '{idxObj}'", instr.Line, instr.Col, instr.OriginFile);
                    }

                case ExceptionObject exo:
                    {
                        string key = idxObj?.ToString() ?? "";
                        if (ExceptionProto.TryGetValue(key, out var im))
                            return new IntrinsicBound(im, exo);
                        if (string.Equals(key, "message$", StringComparison.Ordinal)) return exo.Message;
                        if (string.Equals(key, "type$", StringComparison.Ordinal)) return exo.Type;
                        throw new VMException($"invalid member '{key}' on Exception", instr.Line, instr.Col, instr.OriginFile);
                    }

                case string strv:
                    {
                        if (idxObj is string mname && StringProto.TryGetValue(mname, out var im))
                            return new IntrinsicBound(im, strv);

                        int index = Convert.ToInt32(idxObj);
                        if (index < 0 || index >= strv.Length)
                            throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                        return strv[index];
                    }

                case Dictionary<string, object> dict:
                    {
                        if (idxObj is string mname && DictProto.TryGetValue(mname, out var imDict))
                            return new IntrinsicBound(imDict, dict);
                        string key = idxObj?.ToString() ?? "";
                        if (dict.TryGetValue(key, out var val))
                            return val;
                        return null;
                    }

                case ClassInstance obj:
                    {
                        string key = idxObj?.ToString() ?? "";
                        if (obj.Fields.TryGetValue(key, out var fval))
                        {
                            if (fval is Closure clos &&
                                clos.Parameters.Count > 0 && clos.Parameters[0] == "this")
                            {
                                return new BoundMethod(clos, obj);
                            }
                            return fval;
                        }
                        throw new VMException($"invalid field '{key}' in class '{obj.ClassName}'", instr.Line, instr.Col, instr.OriginFile);
                    }

                default:
                    throw CreateIndexException(target, idxObj, instr);
            }
        }

        /// <summary>
        /// The RequireIntIndex
        /// </summary>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="int"/></returns>
        private static int RequireIntIndex(object idxObj, Instruction instr)
        {
            if (idxObj is int i) return i;
            if (idxObj is long l)
            {
                if (l < int.MinValue || l > int.MaxValue)
                    throw new VMException($"Runtime error: index {l} outside Int32 range", instr.Line, instr.Col, instr.OriginFile);
                return (int)l;
            }
            if (idxObj is short s) return (int)s;
            if (idxObj is byte b) return (int)b;

            if (idxObj is string sVal && int.TryParse(sVal, out var parsed))
                return parsed;

            throw new VMException($"Runtime error: index must be an integer, got '{idxObj?.GetType().Name ?? "null"}'", instr.Line, instr.Col, instr.OriginFile);
        }

        /// <summary>
        /// The IsReservedArrayMemberName
        /// </summary>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsReservedArrayMemberName(object idxObj)
            => idxObj is string name && ArrayProto.ContainsKey(name);

        /// <summary>
        /// The IsReservedDictMemberName
        /// </summary>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsReservedDictMemberName(object idxObj)
            => idxObj is string name && DictProto.ContainsKey(name);

        /// <summary>
        /// The SetIndexedValue
        /// </summary>
        /// <param name="target">The target<see cref="object"/></param>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <param name="value">The value<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        private static void SetIndexedValue(ref object target, object idxObj, object value, Instruction instr)
        {
            switch (target)
            {
                case List<object> arr:
                    {
                        if (IsReservedArrayMemberName(idxObj))
                            throw new VMException($"Runtime error: cannot assign to array intrinsic '{idxObj}'", instr.Line, instr.Col, instr.OriginFile);

                        int index = RequireIntIndex(idxObj, instr);

                        if (index < 0 || index >= arr.Count)
                            throw new VMException($"Runtime error: index {index} out of range (0..{arr.Count - 1})", instr.Line, instr.Col, instr.OriginFile);

                        arr[index] = value;
                        break;
                    }

                case string _:
                    {
                        throw new VMException("Runtime error: INDEX_SET on string. Strings are immutable.", instr.Line, instr.Col, instr.OriginFile);
                    }

                case Dictionary<string, object> dict:
                    {
                        if (IsReservedDictMemberName(idxObj))
                            throw new VMException($"Runtime error: key '{idxObj}' is reserved for dictionary intrinsics", instr.Line, instr.Col, instr.OriginFile);

                        string key = idxObj?.ToString() ?? "";
                        if (key.Length == 0)
                            throw new VMException("Runtime error: dictionary key cannot be empty", instr.Line, instr.Col, instr.OriginFile);

                        dict[key] = value;
                        break;
                    }

                case ClassInstance obj:
                    {
                        string key = idxObj?.ToString() ?? "";
                        if (key.Length == 0)
                            throw new VMException("Runtime error: field name cannot be empty", instr.Line, instr.Col, instr.OriginFile);

                        obj.Fields[key] = value;
                        break;
                    }

                case null:
                    throw new VMException("Runtime error: INDEX_SET on null target", instr.Line, instr.Col, instr.OriginFile);

                default:
                    throw new VMException("Runtime error: target is not index-assignable", instr.Line, instr.Col, instr.OriginFile);
            }
        }

        /// <summary>
        /// The CreateIndexException
        /// </summary>
        /// <param name="target">The target<see cref="object"/></param>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="VMException"/></returns>
        private static VMException CreateIndexException(object target, object idxObj, Instruction instr)
        {
            string tid = target?.GetType().FullName ?? "null";
            string tval; try { tval = target?.ToString() ?? "null"; } catch { tval = "<ToString() failed>"; }

            string iid = idxObj?.GetType().FullName ?? "null";
            string ival; try { ival = idxObj?.ToString() ?? "null"; } catch { ival = "<ToString() failed>"; }

            return new VMException(
                "Runtime error: INDEX_GET target is not indexable.\n" +
                $"  target type: {tid}\n  target value: {tval}\n" +
                $"  index type: {iid}\n  index value: {ival}",
                instr.Line, instr.Col, instr.OriginFile
            );
        }
    }

    /// <summary>
    /// Defines the <see cref="VMException" />
    /// </summary>
    public sealed class VMException(string message, int line, int column, string fileSource) : Exception($"{message}. ( Line : {line}, Column : {column} ) [Source : '{fileSource}']");

}
