using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extension;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Plugin;
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
        /// Defines the <see cref="BuiltinCallable" />
        /// </summary>
        public sealed class BuiltinCallable
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
            /// Initializes a new instance of the <see cref="BuiltinCallable"/> class.
            /// </summary>
            /// <param name="name">The name<see cref="string"/></param>
            /// <param name="min">The min<see cref="int"/></param>
            /// <param name="max">The max<see cref="int"/></param>
            public BuiltinCallable(string name, int min, int max)
            {
                Name = name; ArityMin = min; ArityMax = max;
            }

            /// <summary>
            /// The ToString
            /// </summary>
            /// <returns>The <see cref="string"/></returns>
            public override string ToString() => $"<builtin {Name}/{ArityMin}..{ArityMax}>";
        }

        /// <summary>
        /// Gets the Builtins
        /// </summary>
        public BuiltinRegistry Builtins { get; } = new();

        /// <summary>
        /// Gets the Intrinsics
        /// </summary>
        public IntrinsicRegistry Intrinsics { get; } = new();

        /// <summary>
        /// Defines the <see cref="BoundType" />
        /// </summary>
        private sealed class BoundType
        {
            /// <summary>
            /// Gets the Type
            /// </summary>
            public StaticInstance Type { get; }

            /// <summary>
            /// Gets the Outer
            /// </summary>
            public object Outer { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="BoundType"/> class.
            /// </summary>
            /// <param name="type">The type<see cref="StaticInstance"/></param>
            /// <param name="outer">The outer<see cref="object"/></param>
            public BoundType(StaticInstance type, object outer)
            {
                Type = type ?? throw new ArgumentNullException(nameof(type));
                Outer = outer ?? throw new ArgumentNullException(nameof(outer));
            }

            /// <summary>
            /// The ToString
            /// </summary>
            /// <returns>The <see cref="string"/></returns>
            public override string ToString() => $"<boundtype {Type.ClassName}>";
        }

        /// <summary>
        /// Defines the _program
        /// </summary>
        private List<Instruction>? _program;

        /// <summary>
        /// Gets or sets a value indicating whether AllowFileIO
        /// </summary>
        public static bool AllowFileIO { get; set; } = true;

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
            /// Defines the Routed
            /// </summary>
            Routed,

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

        public sealed class FileHandle : IDisposable
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
            public override string ToString()
            {
                return Message ?? base.ToString() ?? "";
            }
        }

        /// <summary>
        /// Defines the <see cref="Env" />
        /// </summary>
        private class Env
        {
            /// <summary>
            /// Defines the Vars
            /// </summary>
            public Dictionary<string, object> Vars = new();

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
            /// Defines the CallDepth
            /// </summary>
            public int CallDepth;

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
            /// Defines the ScopeDepthAtTry
            /// </summary>
            public int ScopeDepthAtTry;

            /// <summary>
            /// Defines the InFinally
            /// </summary>
            public bool InFinally;

            /// <summary>
            /// Defines the HasPendingReturn
            /// </summary>
            public bool HasPendingReturn;

            /// <summary>
            /// Defines the PendingReturnValue
            /// </summary>
            public object? PendingReturnValue;

            /// <summary>
            /// Defines the HasPendingLeave
            /// </summary>
            public bool HasPendingLeave;

            /// <summary>
            /// Defines the PendingLeaveTargetIp
            /// </summary>
            public int PendingLeaveTargetIp;

            /// <summary>
            /// Defines the PendingLeaveScopes
            /// </summary>
            public int PendingLeaveScopes;

            /// <summary>
            /// Initializes a new instance of the <see cref="TryHandler"/> class.
            /// </summary>
            /// <param name="catchAddr">The catchAddr<see cref="int"/></param>
            /// <param name="finallyAddr">The finallyAddr<see cref="int"/></param>
            /// <param name="scopeDepthAtTry">The scopeDepthAtTry<see cref="int"/></param>
            /// <param name="callDepth">The callDepth<see cref="int"/></param>
            public TryHandler(int catchAddr, int finallyAddr, int scopeDepthAtTry, int callDepth)
            {
                CatchAddr = catchAddr;
                FinallyAddr = finallyAddr;
                ScopeDepthAtTry = scopeDepthAtTry;
                CallDepth = callDepth;
                Exception = null;

                InFinally = false;
                HasPendingReturn = false;
                PendingReturnValue = null;

                HasPendingLeave = false;
                PendingLeaveTargetIp = -1;
                PendingLeaveScopes = 0;
            }
        }

        /// <summary>
        /// Defines the _tryHandlers
        /// </summary>
        private readonly Stack<TryHandler> _tryHandlers = new();

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
                    IEnumerable<string> pairs = CapturedEnv.Vars
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

        private record CallFrame(int ReturnIp, int BaseScopeDepth, object? ThisRef);
        /// <summary>
        /// The PopScopesToBase
        /// </summary>
        /// <param name="baseDepth">The baseDepth<see cref="int"/></param>
        private void PopScopesToBase(int baseDepth)
        {
            while (_scopes.Count > baseDepth)
                _scopes.RemoveAt(_scopes.Count - 1);
        }

        /// <summary>
        /// Gets the CurrentThis
        /// </summary>
        private object? CurrentThis => _callStack.Count > 0 ? _callStack.Peek().ThisRef : null;

        /// <summary>
        /// Defines the _stack
        /// </summary>
        private readonly Stack<object> _stack = new();

        /// <summary>
        /// The RequireStack
        /// </summary>
        /// <param name="needed">The needed<see cref="int"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <param name="opName">The opName<see cref="string?"/></param>
        private void RequireStack(int needed, Instruction instr, string? opName = null)
        {
            if (_stack.Count < needed)
                throw new VMException(
                    $"Runtime error: {(opName ?? instr.Code.ToString())} needs {needed} stack values (have {_stack.Count})",
                    instr.Line, instr.Col, instr.OriginFile);
        }

        /// <summary>
        /// Defines the _scopes
        /// </summary>
        private readonly List<Env> _scopes = new() { new Env(null) };

        /// <summary>
        /// Defines the _functions
        /// </summary>
        public Dictionary<string, FunctionInfo> _functions = new();

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
        /// The LoadPluginsFrom
        /// </summary>
        /// <param name="directory">The directory<see cref="string"/></param>
        public void LoadPluginsFrom(string directory)
        {
            PluginLoader.LoadDirectory(directory, Builtins, Intrinsics);
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
                        List<string> keys = dict.Keys.ToList();
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
              or float or double or decimal or char;

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
        /// The CharToNumeric
        /// </summary>
        /// <param name="o">The o<see cref="object"/></param>
        /// <returns>The <see cref="object"/></returns>
        internal static object CharToNumeric(object o)
    => o is char ch ? (char.IsDigit(ch) ? (int)(ch - '0') : (int)ch) : o;

        /// <summary>
        /// The CoercePair
        /// </summary>
        /// <param name="a">The a<see cref="object"/></param>
        /// <param name="b">The b<see cref="object"/></param>
        /// <returns>The <see cref="(object A, object B, NumKind K)"/></returns>
        private static (object A, object B, NumKind K) CoercePair(object a, object b)
        {
            a = a is char ? CharToNumeric(a) : a;
            b = b is char ? CharToNumeric(b) : b;
            NumKind k = PromoteKind(a, b);
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
            char v => (decimal)v,
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}"),
        };

        /// <summary>
        /// The LoadFunctions
        /// </summary>
        /// <param name="funcs">The funcs<see cref="Dictionary{string, FunctionInfo}"/></param>
        public void LoadFunctions(Dictionary<string, FunctionInfo> funcs)
        {
            foreach (KeyValuePair<string, FunctionInfo> kv in funcs)
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

            string s = val.ToString() ?? "";
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
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i32))
                    return i32;

                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i64))
                    return i64;

                if (decimal.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out decimal decInt))
                    return decInt;

                if (double.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out double dblInt))
                    return dblInt;

                throw new FormatException($"toi: '{s}' invalid number.");
            }
            else
            {
                if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal dec))
                    return dec;

                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double dbl))
                    return dbl;

                throw new FormatException($"toi: '{s}' invalid floating point number.");
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
        /// Gets the CurrentReceiver
        /// </summary>
        private object? CurrentReceiver => _callStack.Count > 0 ? _callStack.Peek().ThisRef : null;

        /// <summary>
        /// The FindEnvWithLocal
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="Env?"/></returns>
        private Env? FindEnvWithLocal(string name)
        {
            for (Env? env = _scopes.Count > 0 ? _scopes[^1] : null; env != null; env = env.Parent)
            {
                if (env.Vars.ContainsKey(name))
                    return env;
            }

            if (_scopes.Count > 0)
            {
                Env root = _scopes[0];
                if (root != null && root.Vars.ContainsKey(name))
                    return root;
            }

            return null;
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
                        break;
                    }

                case OpCode.POP_SCOPE:
                    {
                        if (_scopes.Count <= 1)
                            throw new VMException("Runtime error: cannot pop global scope", instr.Line, instr.Col, instr.OriginFile);
                        _scopes.RemoveAt(_scopes.Count - 1);
                        break;
                    }
                case OpCode.LEAVE:
                    {
                        if (instr.Operand is not object[] arr || arr.Length < 2)
                            throw new VMException("Runtime error: LEAVE requires [targetIp, scopesToPop]", instr.Line, instr.Col, instr.OriginFile);

                        int targetIp = Convert.ToInt32(arr[0]);
                        int scopesToPop = Convert.ToInt32(arr[1]);

                        TryHandler? nextFinally = null;
                        foreach (TryHandler th in _tryHandlers)
                        {
                            if (th.FinallyAddr >= 0 && !th.InFinally)
                            {
                                nextFinally = th;
                                break;
                            }
                        }

                        if (nextFinally != null)
                        {
                            nextFinally.HasPendingLeave = true;
                            nextFinally.PendingLeaveTargetIp = targetIp;
                            nextFinally.PendingLeaveScopes = scopesToPop;
                            nextFinally.InFinally = true;

                            int nip = nextFinally.FinallyAddr;
                            nextFinally.FinallyAddr = -1;
                            _ip = nip;
                            return StepResult.Routed;
                        }

                        for (int i = 0; i < scopesToPop; i++)
                        {
                            if (_scopes.Count <= 1)
                                throw new VMException("Runtime error: cannot pop global scope", instr.Line, instr.Col, instr.OriginFile);
                            _scopes.RemoveAt(_scopes.Count - 1);
                        }

                        _ip = targetIp;
                        return StepResult.Continue;
                    }

                case OpCode.NEW_OBJECT:
                    {
                        string className = instr.Operand?.ToString() ?? "<anon>";
                        ClassInstance obj = new(className);
                        _stack.Push(obj);
                        break;
                    }
                case OpCode.NEW_STATIC:
                    {
                        string className = instr.Operand?.ToString() ?? "<anon>";
                        StaticInstance st = new(className);
                        _stack.Push(st);
                        break;
                    }

                case OpCode.NEW_ARRAY:
                    {
                        if (instr.Operand is null) break;
                        int ecount = (int)instr.Operand;
                        RequireStack(ecount, instr, "NEW_ARRAY");
                        object[] temp = new object[ecount];
                        for (int i = ecount - 1; i >= 0; i--) temp[i] = _stack.Pop();
                        List<object> list = new(temp);
                        _stack.Push(list);
                        break;
                    }

                case OpCode.SLICE_GET:
                    {
                        if (instr.Operand is string)
                            RequireStack(2, instr, "SLICE_GET");
                        else
                            RequireStack(3, instr, "SLICE_GET");

                        object endObj = _stack.Pop();
                        object startObj = _stack.Pop();

                        object target;
                        if (instr.Operand is string name)
                        {
                            Env owner = FindEnvWithLocal(name)
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
                                    List<string> keys = dict.Keys.ToList();
                                    Normalize(keys.Count, startObj, endObj, out int start, out int end);

                                    Dictionary<string, object> slice = new();
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
                        if (instr.Operand is string)
                            RequireStack(3, instr, "SLICE_SET");
                        else
                            RequireStack(4, instr, "SLICE_SET");

                        object value = _stack.Pop();
                        object endObj = _stack.Pop();
                        object startObj = _stack.Pop();

                        object target;

                        if (instr.Operand is string name)
                        {
                            Env env = FindEnvWithLocal(name)
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
                                        List<string> keys = dict.Keys.ToList();
                                        Normalize(keys.Count, startObj, endObj, out int start, out int end);

                                        if (value is Dictionary<string, object> valDict)
                                        {
                                            int i = 0;
                                            for (int k = start; k < end && i < valDict.Count; k++, i++)
                                            {
                                                KeyValuePair<String, object> kv = valDict.ElementAt(i);
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

                                        StringBuilder sb = new(s);
                                        string replacement = (value?.ToString()) ?? "";

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
                        if (instr.Operand is string)
                            RequireStack(1, instr, "INDEX_GET");
                        else
                            RequireStack(2, instr, "INDEX_GET");

                        object target;
                        object idxObj = _stack.Pop();

                        if (instr.Operand is string nameFromEnv)
                        {
                            Env owner = FindEnvWithLocal(nameFromEnv)
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
                        if (instr.Operand is string)
                            RequireStack(2, instr, "INDEX_SET");
                        else
                            RequireStack(3, instr, "INDEX_SET");

                        object value = _stack.Pop();
                        object idxObj = _stack.Pop();
                        object target;

                        if (instr.Operand is string nameFromEnv)
                        {
                            Env env = FindEnvWithLocal(nameFromEnv)
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
                        int dcount = (int)instr.Operand;
                        RequireStack(dcount * 2, instr, "NEW_DICT");

                        (string key, object val)[] pairs = new (string key, object val)[dcount];
                        for (int i = dcount - 1; i >= 0; i--)
                        {
                            object value = _stack.Pop();
                            object key = _stack.Pop();
                            string sk = key?.ToString() ?? "null";
                            pairs[i] = (sk, value);
                        }

                        Dictionary<string, object> dict = new(dcount);
                        for (int i = 0; i < dcount; i++)
                            dict[pairs[i].key] = pairs[i].val;

                        _stack.Push(dict);
                        break;
                    }

                case OpCode.NEW_ENUM:
                    {
                        if (instr.Operand is null) break;

                        if (_stack.Count < 1)
                            throw new VMException("Runtime error: stack underflow (NEW_ENUM needs count)", instr.Line, instr.Col, instr.OriginFile);

                        int count = Convert.ToInt32(_stack.Pop());

                        RequireStack(2 * count, instr, "NEW_ENUM");

                        EnumInstance ei = new(instr.Operand.ToString() ?? "null");

                        for (int i = 0; i < count; i++)
                        {
                            object valueObj = _stack.Pop();
                            object keyObj = _stack.Pop();

                            string key = keyObj?.ToString() ?? "null";
                            int value = Convert.ToInt32(valueObj);

                            ei.Add(key, value);
                        }

                        _stack.Push(ei);
                        break;
                    }

                case OpCode.ROT:
                    {
                        RequireStack(3, instr, "ROT");
                        object a = _stack.Pop();
                        object b = _stack.Pop();
                        object c = _stack.Pop();
                        _stack.Push(b);
                        _stack.Push(a);
                        _stack.Push(c);
                        break;
                    }
                case OpCode.SWAP:
                    {
                        RequireStack(2, instr, "SWAP");

                        object a = _stack.Pop();
                        object b = _stack.Pop();
                        _stack.Push(a);
                        _stack.Push(b);
                        break;
                    }

                case OpCode.IS_DICT:
                    {
                        if (_stack.Count < 1)
                            throw new VMException("Stack underflow in IS_DICT", instr.Line, instr.Col, instr.OriginFile);

                        object v = _stack.Pop();
                        _stack.Push(v is Dictionary<string, object>);
                        break;
                    }

                case OpCode.ARRAY_PUSH:
                    {
                        if (instr.Operand == null)
                        {
                            RequireStack(2, instr, "ARRAY_PUSH");
                            object arrObj = _stack.Pop();
                            object value = _stack.Pop();

                            if (arrObj is List<object> arr)
                            {
                                arr.Add(value);
                            }
                            else if (arrObj is Dictionary<string, object> dict)
                            {
                                if (value is Dictionary<string, object> literal && literal.Count == 1)
                                {
                                    foreach (KeyValuePair<string, object> kv in literal)
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
                            RequireStack(1, instr, "ARRAY_PUSH");
                            object value = _stack.Pop();
                            string name = (string)instr.Operand;
                            Env? env = FindEnvWithLocal(name);
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
                                    foreach (KeyValuePair<string, object> kv in literal)
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
                        if (instr.Operand is string)
                            RequireStack(2, instr, "ARRAY_DELETE_SLICE");
                        else
                            RequireStack(3, instr, "ARRAY_DELETE_SLICE");

                        object endObj = _stack.Pop();
                        object startObj = _stack.Pop();

                        object target;
                        if (instr.Operand is string name)
                        {
                            Env env = FindEnvWithLocal(name)
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
                        RequireStack(3, instr, "ARRAY_DELETE_SLICE_ALL");
                        object endObj = _stack.Pop();
                        object startObj = _stack.Pop();
                        object target = _stack.Pop();

                        if (target is string)
                            throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile);

                        if (target is List<object> arr)
                        {
                            (int start, int endEx) = NormalizeSliceBounds(startObj, endObj, arr.Count, instr);
                            int sdcount = endEx - start;
                            if (sdcount > 0) arr.RemoveRange(start, sdcount);
                        }
                        else if (target is Dictionary<string, object> dict)
                        {
                            List<string> keys = dict.Keys.ToList();
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
                        if (instr.Operand != null)
                            RequireStack(1, instr, "ARRAY_DELETE_ELEM");
                        else
                            RequireStack(2, instr, "ARRAY_DELETE_ELEM");

                        object idxObj = _stack.Pop();

                        if (instr.Operand != null)
                        {
                            string name = (string)instr.Operand;
                            Env? owner = FindEnvWithLocal(name);
                            if (owner == null)
                                throw new VMException($"Runtime error: undefined variable '{name}", instr.Line, instr.Col, instr.OriginFile);

                            object target = owner.Vars[name];

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
                            object target = _stack.Pop();

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
                        Env? env = FindEnvWithLocal(name);
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
                        RequireStack(2, instr, "ARRAY_DELETE_ELEM_ALL");

                        object idxObj = _stack.Pop();
                        object target = _stack.Pop();
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
                            object? th = CurrentThis;
                            if (th == null)
                                throw new VMException("Runtime error: 'this' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile);
                            _stack.Push(th);
                            break;
                        }

                        if (name == "type")
                        {
                            object? recv = CurrentThis;
                            if (recv is StaticInstance st)
                            {
                                _stack.Push(st);
                                break;
                            }
                            if (recv is ClassInstance inst)
                            {
                                if (inst.Fields.TryGetValue("__type", out object? tObj) && tObj is StaticInstance st2)
                                {
                                    _stack.Push(st2);
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__type' on instance for 'type'", instr.Line, instr.Col, instr.OriginFile);
                            }
                            throw new VMException("Runtime error: 'type' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile);
                        }

                        if (name == "super")
                        {
                            object? recv = CurrentThis;

                            if (recv is ClassInstance inst)
                            {
                                if (inst.Fields.TryGetValue("__base", out object? bObj) && bObj is ClassInstance baseInst)
                                {
                                    _stack.Push(baseInst);
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__base' on instance for 'super'", instr.Line, instr.Col, instr.OriginFile);
                            }

                            if (recv is StaticInstance st)
                            {
                                if (st.Fields.TryGetValue("__base", out object? sbObj) && sbObj is StaticInstance baseType)
                                {
                                    _stack.Push(baseType);
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__base' on static type for 'super'", instr.Line, instr.Col, instr.OriginFile);
                            }

                            throw new VMException("Runtime error: 'super' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile);
                        }
                        if (name == "outer")
                        {
                            object? recv = CurrentThis;

                            if (recv is ClassInstance inst)
                            {
                                if (inst.Fields.TryGetValue("__outer", out object? oObj) && oObj is ClassInstance outerInst)
                                {
                                    _stack.Push(outerInst);
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__outer' on instance for 'outer'", instr.Line, instr.Col, instr.OriginFile);
                            }

                            if (recv is StaticInstance)
                            {
                                throw new VMException("Runtime error: 'outer' not available in static context", instr.Line, instr.Col, instr.OriginFile);
                            }

                            throw new VMException("Runtime error: 'outer' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile);
                        }

                        Env? owner = FindEnvWithLocal(name);
                        if (owner != null && owner.Vars.TryGetValue(name, out object? val))
                        {
                            _stack.Push(val);
                            break;
                        }

                        if (Builtins.TryGet(name, out BuiltinDescriptor? d))
                        {
                            _stack.Push(new BuiltinCallable(d.Name, d.ArityMin, d.ArityMax));
                            break;
                        }

                        throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile);

                    }

                case OpCode.VAR_DECL:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this" || name == "type" || name == "super" || name == "outer")
                            throw new VMException($"Runtime error: cannot declare '{name}' as a variable", instr.Line, instr.Col, instr.OriginFile);

                        RequireStack(1, instr, "VAR_DECL");
                        object value = _stack.Pop();
                        Env scope = _scopes[^1];
                        if (scope.HasLocal(name))
                            throw new VMException($"Runtime error: variable '{name}' already declared in this scope", instr.Line, instr.Col, instr.OriginFile);

                        scope.Define(name, value);
                        break;
                    }

                case OpCode.STORE_VAR:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this" || name == "type" || name == "super" || name == "outer")
                            throw new VMException($"Runtime error: cannot assign to '{name}'", instr.Line, instr.Col, instr.OriginFile);

                        RequireStack(1, instr, "STORE_VAR");
                        object value = _stack.Pop();
                        Env? env = FindEnvWithLocal(name);
                        if (env == null)
                            throw new VMException($"Runtime error: assignment to undeclared variable '{name}'", instr.Line, instr.Col, instr.OriginFile);

                        env.Vars[name] = value;
                        break;
                    }

                case OpCode.ADD:
                    {
                        RequireStack(2, instr, "ADD");
                        object r = _stack.Pop();
                        object l = _stack.Pop();

                        if (IsNumber(l) && IsNumber(r))
                        {
                            (object A, object B, NumKind K) = CoercePair(l, r);
                            object res = K switch
                            {
                                NumKind.Decimal => (object)((decimal)A + (decimal)B),
                                NumKind.Double => (double)A + (double)B,
                                NumKind.UInt64 => (ulong)A + (ulong)B,
                                NumKind.Int64 => (long)A + (long)B,
                                _ => (int)A + (int)B,
                            };
                            _stack.Push(res);
                            break;
                        }

                        if (l is null || r is null)
                        {
                            if (l is string || r is string)
                            {
                                _stack.Push((l as string ?? "") + (r as string ?? ""));
                            }
                            else
                            {
                                _stack.Push(null);
                            }
                            break;
                        }

                        if (l is List<object> || r is List<object> ||
                            l is Dictionary<string, object> || r is Dictionary<string, object>)
                        {
                            string ls, rs;
                            using (StringWriter lw = new()) { PrintValue(l, lw); ls = lw.ToString(); }
                            using (StringWriter rw = new()) { PrintValue(r, rw); rs = rw.ToString(); }
                            _stack.Push(ls + rs);
                            break;
                        }

                        _stack.Push(l.ToString() + r.ToString());
                        break;
                    }

                case OpCode.SUB:
                    {
                        RequireStack(2, instr, "SUB");
                        object r = _stack.Pop();
                        object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null); break; }
                        if (!IsNumber(l) || !IsNumber(r))
                            throw new VMException("SUB on non-numeric types", instr.Line, instr.Col, instr.OriginFile);

                        (object A, object B, NumKind K) = CoercePair(l, r);
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
                        RequireStack(2, instr, "MUL");
                        object r = _stack.Pop();
                        object l = _stack.Pop();

                        if (l is null || r is null)
                        {
                            if (l is string && IsNumber(r))
                            { _stack.Push(string.Concat(Enumerable.Repeat(l as string ?? "", Convert.ToInt32(r)))); break; }
                            if (r is string && IsNumber(l))
                            { _stack.Push(string.Concat(Enumerable.Repeat(r as string ?? "", Convert.ToInt32(l)))); break; }
                            _stack.Push(null);
                            break;
                        }

                        if (IsNumber(l) && IsNumber(r))
                        {
                            (object A, object B, NumKind K) = CoercePair(l, r);
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
                            _stack.Push(string.Concat(Enumerable.Repeat(l as string ?? "", Convert.ToInt32(r))));
                        }
                        else if (r is string && IsNumber(l))
                        {
                            _stack.Push(string.Concat(Enumerable.Repeat(r as string ?? "", Convert.ToInt32(l))));
                        }
                        else
                        {
                            throw new VMException("MUL on non-numeric types", instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }

                case OpCode.MOD:
                    {
                        RequireStack(2, instr, "MOD");
                        object r = _stack.Pop();
                        object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null); break; }
                        if (!IsNumber(l) || !IsNumber(r))
                            throw new VMException("MOD on non-numeric types", instr.Line, instr.Col, instr.OriginFile);

                        (object A, object B, NumKind K) = CoercePair(l, r);
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
                        RequireStack(2, instr, "DIV");
                        object r = _stack.Pop();
                        object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null); break; }
                        if (!IsNumber(l) || !IsNumber(r))
                            throw new VMException($"Runtime error: cannot DIV {l?.GetType()} and {r?.GetType()}",
                                instr.Line, instr.Col, instr.OriginFile);

                        (object A, object B, NumKind K) = CoercePair(l, r);
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
                        RequireStack(2, instr, "EXPO");
                        object r = _stack.Pop();
                        object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null); break; }
                        if (!IsNumber(l) || !IsNumber(r))
                            throw new VMException("EXPO on non-numeric types", instr.Line, instr.Col, instr.OriginFile);

                        (object A, object B, NumKind K) = CoercePair(l, r);
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
                        RequireStack(2, instr, "BIT_AND");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        if (l is null || r is null) { _stack.Push(null); break; }
                        r = r is char ? CharToNumeric(r) : r;
                        l = l is char ? CharToNumeric(l) : l;
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
                        RequireStack(2, instr, "BIT_OR");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        if (l is null || r is null) { _stack.Push(null); break; }
                        r = r is char ? CharToNumeric(r) : r;
                        l = l is char ? CharToNumeric(l) : l;
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
                        RequireStack(2, instr, "BIT_XOR");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        if (l is null || r is null) { _stack.Push(null); break; }
                        r = r is char ? CharToNumeric(r) : r;
                        l = l is char ? CharToNumeric(l) : l;
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
                        RequireStack(2, instr, "SHL");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        if (l is null || r is null) { _stack.Push(null); break; }
                        r = r is char ? CharToNumeric(r) : r;
                        l = l is char ? CharToNumeric(l) : l;
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
                        RequireStack(2, instr, "SHR");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        if (l is null || r is null) { _stack.Push(null); break; }
                        r = r is char ? CharToNumeric(r) : r;
                        l = l is char ? CharToNumeric(l) : l;
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
                        RequireStack(2, instr, "EQ");
                        object r = _stack.Pop();
                        object l = _stack.Pop();
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
                        RequireStack(2, instr, "NEQ");
                        object r = _stack.Pop();
                        object l = _stack.Pop();
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
                        RequireStack(2, instr, "LT");
                        object r = _stack.Pop(); object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null); break; }

                        if (IsNumber(l) && IsNumber(r))
                        {
                            (object A, object B, NumKind K) = CoercePair(l, r);
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
                        RequireStack(2, instr, "GT");
                        object r = _stack.Pop(); object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null); break; }

                        if (IsNumber(l) && IsNumber(r))
                        {
                            (object A, object B, NumKind K) = CoercePair(l, r);
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
                        RequireStack(2, instr, "LE");
                        object r = _stack.Pop(); object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null); break; }

                        if (IsNumber(l) && IsNumber(r))
                        {
                            (object A, object B, NumKind K) = CoercePair(l, r);
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
                        RequireStack(2, instr, "GE");
                        object r = _stack.Pop(); object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null); break; }

                        if (IsNumber(l) && IsNumber(r))
                        {
                            (object A, object B, NumKind K) = CoercePair(l, r);
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

                case OpCode.NOT:
                    {
                        RequireStack(1, instr, "NOT");
                        object v = _stack.Pop();
                        if (v is null) { _stack.Push(null); break; }
                        _stack.Push(!ToBool(v));
                        break;
                    }

                case OpCode.NEG:
                    {
                        RequireStack(1, instr, "NEG");
                        object? v = _stack.Pop();
                        if (v is null) { _stack.Push(null); break; }
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

                case OpCode.DUP:
                    {
                        RequireStack(1, instr, "DUP");
                        object v = _stack.Peek();
                        _stack.Push(v);
                        break;
                    }

                case OpCode.POP:
                    {
                        RequireStack(1, instr, "POP");
                        _stack.Pop();
                        break;
                    }

                case OpCode.AND:
                    {
                        RequireStack(2, instr, "AND");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        bool lb = ToBool(l); bool rb = ToBool(r);
                        _stack.Push(lb && rb);
                        break;
                    }

                case OpCode.OR:
                    {
                        RequireStack(2, instr, "OR");
                        object r = _stack.Pop(); object l = _stack.Pop();
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
                        RequireStack(1, instr, "JMP_IF_FALSE");
                        object v = _stack.Pop();
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
                        RequireStack(1, instr, "JMP_IF_TRUE");
                        object v = _stack.Pop();
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

                        FunctionInfo? funcInfo = _functions.Values.FirstOrDefault(f => f.Address == funcAddr);
                        if (funcInfo == null)
                            throw new VMException($"Runtime error: PUSH_CLOSURE unknown function address {funcAddr}", instr.Line, instr.Col, instr.OriginFile);

                        Env capturedEnv = _scopes[^1];
                        _stack.Push(new Closure(funcAddr, funcInfo.Parameters, capturedEnv, funcName ?? throw new VMException("Invalid function-name", instr.Line, instr.Col, instr.OriginFile)));
                        break;
                    }

                case OpCode.CALL:
                    {
                        if (instr.Operand is string funcName)
                        {
                            if (Builtins.TryGet(funcName, out BuiltinDescriptor? desc))
                            {
                                List<object> args = new();
                                for (int i = desc.ArityMin - 1; i >= 0; i--)
                                {
                                    if (_stack.Count == 0)
                                        throw new VMException($"Runtime error: insufficient args for {funcName}()", instr.Line, instr.Col, instr.OriginFile);
                                    args.Insert(0, _stack.Pop());
                                }
                                object result = desc.Invoke(args, instr);
                                _stack.Push(result);
                                break;
                            }

                            if (!_functions.TryGetValue(funcName, out FunctionInfo? func))
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
                        static bool IsReceiverName(string s) => s == "this" || s == "type";

                        if (instr.Operand is IConvertible)
                        {
                            int explicitArgCount = Convert.ToInt32(instr.Operand);

                            List<object> argsList = new();
                            for (int i = 0; i < explicitArgCount; i++)
                            {
                                if (_stack.Count == 0)
                                    throw new VMException(
                                        $"Runtime error: not enough arguments for CALL_INDIRECT (expected {explicitArgCount})",
                                        instr.Line, instr.Col, instr.OriginFile
                                    );
                                argsList.Add(_stack.Pop());
                            }

                            if (_stack.Count == 0)
                                throw new VMException("Runtime error: missing callee for CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile);

                            object callee = _stack.Pop();
                            Closure f;
                            object? receiver = null;

                            if (callee is IntrinsicBound ib_ex)
                            {
                                if (explicitArgCount < ib_ex.Method.ArityMin || explicitArgCount > ib_ex.Method.ArityMax)
                                    throw new VMException(
                                        $"Runtime error: {ib_ex.Method.Name} expects {ib_ex.Method.ArityMin}..{ib_ex.Method.ArityMax} args, got {explicitArgCount}",
                                        instr.Line, instr.Col, instr.OriginFile
                                    );

                                object result = ib_ex.Method.Invoke(ib_ex.Receiver, argsList, instr);
                                _stack.Push(result);
                                return StepResult.Continue;
                            }
                            else if (callee is BuiltinCallable bc)
                            {
                                if (!Builtins.TryGet(bc.Name, out BuiltinDescriptor? desc))
                                    throw new VMException($"Runtime error: unknown builtin '{bc.Name}'", instr.Line, instr.Col, instr.OriginFile);

                                if (explicitArgCount < desc.ArityMin || explicitArgCount > desc.ArityMax)
                                    throw new VMException(
                                        $"Runtime error: builtin '{bc.Name}' expects {desc.ArityMin}..{desc.ArityMax} args, got {explicitArgCount}",
                                        instr.Line, instr.Col, instr.OriginFile);

                                object result = desc.Invoke(argsList, instr);
                                _stack.Push(result);
                                return StepResult.Continue;
                            }
                            else if (callee is BoundMethod bm)
                            {
                                f = bm.Function;
                                receiver = bm.Receiver;

                                if (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0]))
                                {
                                    if (argsList.Count > 0 && Equals(argsList[0], receiver))
                                        throw new VMException(
                                            "Runtime error: receiver provided twice (BoundMethod already has implicit receiver).",
                                            instr.Line, instr.Col, instr.OriginFile
                                        );
                                }
                            }
                            else if (callee is BoundType bt)
                            {
                                object ctorVal = GetIndexedValue(bt.Type, "new", instr);
                                if (ctorVal is not Closure ctorClos)
                                    throw new VMException("Runtime error: nested type has no constructor 'new'.", instr.Line, instr.Col, instr.OriginFile);

                                f = ctorClos;
                                receiver = null;

                                argsList.Insert(0, bt.Outer);
                            }
                            else if (callee is Closure clos)
                            {
                                f = clos;

                                if (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0]))
                                {
                                    if (argsList.Count == 0)
                                        throw new VMException(
                                            $"Runtime error: missing '{f.Parameters[0]}' for method call.",
                                            instr.Line, instr.Col, instr.OriginFile
                                        );
                                    receiver = argsList[0];
                                    argsList.RemoveAt(0);
                                }
                            }
                            else if (callee is StaticInstance st)
                            {
                                object ctorVal = GetIndexedValue(st, "new", instr);
                                if (ctorVal is not Closure ctorClos)
                                    throw new VMException("Runtime error: type has no constructor 'new'.", instr.Line, instr.Col, instr.OriginFile);

                                f = ctorClos;
                                receiver = null;
                            }
                            else
                            {
                                throw new VMException($"Runtime error: attempt to call non-function value ({instr.Code})", instr.Line, instr.Col, instr.OriginFile);
                            }

                            Env callEnv = new(f.CapturedEnv);
                            int piStart = (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0])) ? 1 : 0;
                            int expected = f.Parameters.Count - piStart;

                            if (argsList.Count < expected)
                                throw new VMException(
                                    $"Runtime error: insufficient args for call (expected {expected}, got {argsList.Count})",
                                    instr.Line, instr.Col, instr.OriginFile
                                );

                            if (argsList.Count > expected)
                                throw new VMException(
                                    $"Runtime error: too many args for call (expected {expected}, got {argsList.Count})",
                                    instr.Line, instr.Col, instr.OriginFile
                                );

                            for (int pi = piStart, ai = 0; pi < f.Parameters.Count; pi++, ai++)
                                callEnv.Define(f.Parameters[pi], argsList[ai]);

                            int callerDepth = _scopes.Count;
                            _scopes.Add(callEnv);
                            _callStack.Push(new CallFrame(_ip, callerDepth, receiver));
                            _ip = f.Address;
                            return StepResult.Continue;
                        }

                        else
                        {
                            if (_stack.Count == 0)
                                throw new VMException("Runtime error: missing callee for CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile);

                            object callee = _stack.Pop();
                            Closure f;
                            object? receiver = null;

                            if (callee is IntrinsicBound ib)
                            {
                                int need = ib.Method.ArityMin;
                                List<object> argsB = new();
                                for (int i = 0; i < need; i++)
                                {
                                    if (_stack.Count == 0)
                                        throw new VMException("Runtime error: insufficient args for intrinsic call", instr.Line, instr.Col, instr.OriginFile);
                                    argsB.Add(_stack.Pop());
                                }
                                object result = ib.Method.Invoke(ib.Receiver, argsB, instr);
                                _stack.Push(result);
                                return StepResult.Continue;
                            }
                            else if (callee is BuiltinCallable bc2)
                            {
                                if (!Builtins.TryGet(bc2.Name, out BuiltinDescriptor? desc))
                                    throw new VMException($"Runtime error: unknown builtin '{bc2.Name}'", instr.Line, instr.Col, instr.OriginFile);

                                int need = desc.ArityMin;
                                List<object> argsB = new();
                                for (int i = 0; i < need; i++)
                                {
                                    if (_stack.Count == 0)
                                        throw new VMException("Runtime error: insufficient args for builtin call", instr.Line, instr.Col, instr.OriginFile);
                                    argsB.Add(_stack.Pop());
                                }
                                argsB.Reverse();

                                object result = desc.Invoke(argsB, instr);
                                _stack.Push(result);
                                return StepResult.Continue;
                            }

                            else if (callee is BoundMethod bm)
                            {
                                f = bm.Function;
                                receiver = bm.Receiver;
                            }
                            else if (callee is BoundType bt)
                            {
                                object ctorVal = GetIndexedValue(bt.Type, "new", instr);
                                if (ctorVal is not Closure ctorClos)
                                    throw new VMException("Runtime error: nested type has no constructor 'new'.", instr.Line, instr.Col, instr.OriginFile);

                                f = ctorClos;
                                receiver = null;

                                int total = f.Parameters.Count;

                                List<object> argsTmp = new();
                                for (int i = 0; i < total - 1; i++)
                                {
                                    if (_stack.Count == 0)
                                        throw new VMException("Runtime error: insufficient args for constructor call", instr.Line, instr.Col, instr.OriginFile);
                                    argsTmp.Add(_stack.Pop());
                                }
                                argsTmp.Reverse();
                                argsTmp.Insert(0, bt.Outer);

                                Env callEnv2 = new(f.CapturedEnv);
                                int piStart2 = (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0])) ? 1 : 0;
                                int expected2 = f.Parameters.Count - piStart2;

                                if (argsTmp.Count != expected2)
                                    throw new VMException(
                                        $"Runtime error: argument count mismatch (expected {expected2}, got {argsTmp.Count})",
                                        instr.Line, instr.Col, instr.OriginFile
                                    );

                                for (int pi = piStart2, ai = 0; pi < f.Parameters.Count; pi++, ai++)
                                    callEnv2.Define(f.Parameters[pi], argsTmp[ai]);

                                int callerDepth2 = _scopes.Count;
                                _scopes.Add(callEnv2);
                                _callStack.Push(new CallFrame(_ip, callerDepth2, receiver));
                                _ip = f.Address;
                                return StepResult.Continue;
                            }
                            else if (callee is Closure clos)
                            {
                                f = clos;

                                if (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0]))
                                {
                                    if (_stack.Count == 0)
                                        throw new VMException(
                                            $"Runtime error: missing '{f.Parameters[0]}' for method call.",
                                            instr.Line, instr.Col, instr.OriginFile
                                        );
                                    receiver = _stack.Pop();
                                }
                            }
                            else if (callee is StaticInstance st)
                            {
                                object ctorVal = GetIndexedValue(st, "new", instr);
                                if (ctorVal is not Closure ctorClos)
                                    throw new VMException("Runtime error: type has no constructor 'new'.", instr.Line, instr.Col, instr.OriginFile);

                                f = ctorClos;
                                receiver = null;
                            }
                            else
                            {
                                throw new VMException($"Runtime error: attempt to call non-function value ({instr.Code})", instr.Line, instr.Col, instr.OriginFile);
                            }

                            int piStart = (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0])) ? 1 : 0;

                            List<object> argsList2 = new();
                            for (int pi = f.Parameters.Count - 1; pi >= piStart; pi--)
                            {
                                if (_stack.Count == 0)
                                    throw new VMException("Runtime error: insufficient args for call", instr.Line, instr.Col, instr.OriginFile);
                                argsList2.Insert(0, _stack.Pop());
                            }

                            int expected = f.Parameters.Count - piStart;
                            if (argsList2.Count != expected)
                                throw new VMException(
                                    $"Runtime error: argument count mismatch (expected {expected}, got {argsList2.Count})",
                                    instr.Line, instr.Col, instr.OriginFile
                                );

                            Env callEnv = new(f.CapturedEnv);
                            for (int pi = piStart, ai = 0; pi < f.Parameters.Count; pi++, ai++)
                                callEnv.Define(f.Parameters[pi], argsList2[ai]);

                            _scopes.Add(callEnv);
                            _callStack.Push(new CallFrame(_ip, 1, receiver));
                            _ip = f.Address;
                            return StepResult.Continue;
                        }
                    }

                case OpCode.RET:
                    {
                        object? retVal = _stack.Count > 0 ? _stack.Pop() : null;

                        TryHandler? nextFinally = null;
                        foreach (TryHandler th in _tryHandlers)
                        {
                            if (th.FinallyAddr >= 0 && !th.InFinally)
                            {
                                nextFinally = th;
                                break;
                            }
                        }

                        if (nextFinally != null)
                        {
                            nextFinally.HasPendingReturn = true;
                            nextFinally.PendingReturnValue = retVal;
                            nextFinally.InFinally = true;

                            int nip = nextFinally.FinallyAddr;
                            nextFinally.FinallyAddr = -1;
                            _ip = nip;
                            return StepResult.Routed;
                        }

                        if (_callStack.Count == 0)
                            throw new VMException("Runtime error: return with empty call stack", instr.Line, instr.Col, instr.OriginFile);

                        CallFrame fr = _callStack.Pop();

                        while (_scopes.Count > fr.BaseScopeDepth)
                            _scopes.RemoveAt(_scopes.Count - 1);

                        while (_tryHandlers.Count > 0 && _tryHandlers.Peek().CallDepth > _callStack.Count)
                            _tryHandlers.Pop();

                        _ip = fr.ReturnIp;
                        _stack.Push(retVal);
                        return StepResult.Continue;
                    }

                case OpCode.TRY_PUSH:
                    {
                        object[] arr = (object[])instr.Operand!;
                        int catchIp = (int)arr[0];
                        int finallyIp = (int)arr[1];

                        TryHandler th = new(
                            catchAddr: catchIp,
                            finallyAddr: finallyIp,
                            scopeDepthAtTry: _scopes.Count,
                            callDepth: _callStack.Count
                        );

                        _tryHandlers.Push(th);
                        break;
                    }

                case OpCode.TRY_POP:
                    {
                        if (_tryHandlers.Count == 0)
                            break;

                        TryHandler h = _tryHandlers.Peek();

                        if (!h.InFinally && h.FinallyAddr >= 0)
                        {
                            if (h.FinallyAddr >= _ip)
                            {
                                h.InFinally = true;
                                int nip = h.FinallyAddr;
                                h.FinallyAddr = -1;
                                _ip = nip;
                                return StepResult.Routed;
                            }
                            else
                            {
                                h.FinallyAddr = -1;
                            }
                        }

                        if (h.HasPendingReturn)
                        {
                            object? retVal = h.PendingReturnValue;

                            _tryHandlers.Pop();

                            TryHandler? outerWithFinally = null;
                            foreach (TryHandler th in _tryHandlers)
                            {
                                if (th.FinallyAddr >= 0 && !th.InFinally)
                                {
                                    outerWithFinally = th;
                                    break;
                                }
                            }

                            if (outerWithFinally != null)
                            {
                                outerWithFinally.HasPendingReturn = true;
                                outerWithFinally.PendingReturnValue = retVal;
                                outerWithFinally.InFinally = true;

                                int nip = outerWithFinally.FinallyAddr;
                                outerWithFinally.FinallyAddr = -1;
                                _ip = nip;
                                return StepResult.Routed;
                            }

                            if (_callStack.Count == 0)
                                throw new VMException("Runtime error: return with empty call stack", instr.Line, instr.Col, instr.OriginFile);

                            CallFrame fr = _callStack.Pop();

                            while (_scopes.Count > fr.BaseScopeDepth)
                                _scopes.RemoveAt(_scopes.Count - 1);

                            while (_tryHandlers.Count > 0 && _tryHandlers.Peek().CallDepth > _callStack.Count)
                                _tryHandlers.Pop();

                            _ip = fr.ReturnIp;
                            _stack.Push(retVal);
                            return StepResult.Continue;
                        }

                        if (h.HasPendingLeave)
                        {
                            int leaveTarget = h.PendingLeaveTargetIp;
                            int leaveScopes = h.PendingLeaveScopes;

                            _tryHandlers.Pop();

                            TryHandler? outerWithFinally = null;
                            foreach (TryHandler th in _tryHandlers)
                            {
                                if (th.FinallyAddr >= 0 && !th.InFinally) { outerWithFinally = th; break; }
                            }

                            if (outerWithFinally != null)
                            {
                                outerWithFinally.HasPendingLeave = true;
                                outerWithFinally.PendingLeaveTargetIp = leaveTarget;
                                outerWithFinally.PendingLeaveScopes = leaveScopes;
                                outerWithFinally.InFinally = true;

                                int nip = outerWithFinally.FinallyAddr;
                                outerWithFinally.FinallyAddr = -1;
                                _ip = nip;
                                return StepResult.Routed;
                            }

                            for (int i = 0; i < leaveScopes; i++)
                            {
                                if (_scopes.Count <= 1)
                                    throw new VMException("Runtime error: cannot pop global scope", instr.Line, instr.Col, instr.OriginFile);
                                _scopes.RemoveAt(_scopes.Count - 1);
                            }

                            _ip = leaveTarget;
                            return StepResult.Continue;
                        }

                        if (h.Exception is object ex)
                        {
                            _tryHandlers.Pop();

                            if (RouteExceptionToTryHandlers(ex, SafeCurrentInstr(_insns, _ip), out int nip))
                            {
                                _ip = nip;
                                return StepResult.Routed;
                            }

                            Instruction instrNow = SafeCurrentInstr(_insns, _ip);
                            throw new VMException(ex.ToString()!, instrNow.Line, instrNow.Col, instrNow.OriginFile);
                        }

                        _tryHandlers.Pop();
                        break;
                    }

                case OpCode.THROW:
                    {
                        object? thrown = _stack.Count > 0 ? _stack.Pop() : null;

                        object exPayload = thrown is null
                            ? new ExceptionObject(
                                  type: "UserError",
                                  message: "throw",
                                  file: instr.OriginFile,
                                  line: instr.Line,
                                  col: instr.Col,
                                  stack: BuildStackString(_insns, instr))
                            : (thrown is ExceptionObject eo ? eo : thrown);

                        if (RouteExceptionToTryHandlers(exPayload, instr, out int nip))
                        {
                            _ip = nip;
                            return StepResult.Routed;
                        }

                        ExceptionObject payload = exPayload as ExceptionObject
                            ?? new ExceptionObject(
                                   type: "UserError",
                                   message: thrown?.ToString() ?? "throw",
                                   file: instr.OriginFile,
                                   line: instr.Line,
                                   col: instr.Col,
                                   stack: BuildStackString(_insns, instr));

                        throw new VMException(payload.ToString()!, instr.Line, instr.Col, instr.OriginFile);
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

            Env? env = FindEnvWithLocal(name);
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
            if (TryGetVar(name, out object? v)) return v;
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
            object? v = GetVar(name);
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

            Env? env = FindEnvWithLocal(name);
            if (env != null)
            {
                env.Vars[name] = value!;
                return;
            }

            if (!defineIfMissing)
                throw new VMException($"Runtime error: assignment to undeclared variable '{name}'", 0, 0, "<host>");

            Env target = toGlobal ? _scopes[0] : _scopes[^1];
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
            foreach (KeyValuePair<string, object?> kv in vars)
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

            if (!_functions.TryGetValue(name, out FunctionInfo? fInfo))
                throw new VMException($"Runtime error: function '{name}' not found.", 0, 0, "<host>");

            if (fInfo.Parameters.Count > 0 && fInfo.Parameters[0] == "this")
                throw new VMException(
                    $"Runtime error: function '{name}' requires 'this'. Use CallFunctionWithThis(...).",
                    0, 0, "<host>");

            Closure clos = new(fInfo.Address, fInfo.Parameters, _scopes[^1], name);

            if (args.Length < clos.Parameters.Count)
                throw new VMException(
                    $"Runtime error: insufficient args for '{name}' (need {clos.Parameters.Count}, have {args.Length})",
                    0, 0, "<host>");

            Env callEnv = new(clos.CapturedEnv);
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

                    Instruction instr = _program[_ip++];
                    StepResult step = HandleInstruction(ref _ip, _program, instr);
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
            if (_program is null || _program.Count == 0)
                return;

            bool routed = false;
            DebugStream = new MemoryStream();
            int _ip = lastPos;

            while (_ip < _program.Count)
            {
                try
                {
                    if (debugging)
                    {
                        int di = Math.Clamp(_ip, 0, _program.Count - 1);
                        Instruction dinstr = _program[di];
                        DebugStream.Write(System.Text.Encoding.Default.GetBytes(
                            $"[DEBUG] {dinstr.Line} ->  IP={_ip}, STACK=[{string.Join(", ", _stack.Reverse())}], SCOPES={_scopes.Count}, CALLSTACK={_callStack.Count}\n"));
                        DebugStream.Write(System.Text.Encoding.Default.GetBytes(
                            $"[DEBUG] {dinstr} (Line {dinstr.Line}, Col {dinstr.Col})\n"));

                    }

                    Instruction instr = _program[_ip++];
                    _ip = Math.Clamp(_ip, 0, _program.Count - 1);
                    StepResult res = HandleInstruction(ref _ip, _program, instr);

                    if (res == StepResult.Halt) return;
                    if (res == StepResult.Continue) continue;
                    if (res == StepResult.Routed) routed = true;
                }
                catch (VMException ex)
                {
                    int safeIp = Math.Min(_ip, _program.Count - 1);

                    ExceptionObject payload = new(
                        type: "RuntimeError",
                        message: ex.Message,
                        file: _program[safeIp].OriginFile,
                        line: _program[safeIp].Line,
                        col: _program[safeIp].Col,
                        stack: BuildStackString(_program, _program[safeIp])
                    );

                    if (RouteExceptionToTryHandlers(payload, _program[safeIp], out int nip))
                    {

                        _ip = nip;
                        routed = true;
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception sysEx)
                {
                    int safeIp = Math.Min(_ip, _program.Count - 1);
                    ExceptionObject payload = new(
                        type: "SystemError",
                        message: sysEx.Message,
                        file: _program[safeIp].OriginFile,
                        line: _program[safeIp].Line,
                        col: _program[safeIp].Col,
                        stack: BuildStackString(_program, _program[safeIp])
                    );

                    if (RouteExceptionToTryHandlers(payload, _program[safeIp], out int nip))
                    {
                        _ip = nip;
                        routed = true;
                    }
                    else
                    {
                        if (debugging)
                        {
                            DebugStream.Position = 0;
                            using FileStream file = File.Create("log_file.log");
                            DebugStream.CopyTo(file);
                        }
                        throw new VMException($"Uncaught system exception : " + sysEx.Message, _program[safeIp].Line, _program[safeIp].Col, _program[safeIp].OriginFile);
                    }
                }

                if (routed)
                {
                    int safeIp = Math.Min(_ip, _program.Count - 1);
                    routed = false;

                    if (_tryHandlers.Count > 0)
                    {
                        TryHandler top = _tryHandlers.Peek();

                        if (top.Exception is object deferredEx && !top.InFinally)
                        {
                            top.Exception = null;

                            if (RouteExceptionToTryHandlers(deferredEx, _program[safeIp], out int nip2))
                            {
                                _ip = nip2;
                                routed = true;
                            }
                            else
                            {
                                if (debugging)
                                {
                                    DebugStream.Position = 0;
                                    using FileStream file = File.Create("log_file.log");
                                    DebugStream.CopyTo(file);
                                }
                                throw new VMException("Uncaught system exception : ", _program[safeIp].Line, _program[safeIp].Col, _program[safeIp].OriginFile);
                            }
                        }
                    }

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
            StringBuilder sb = new();

            sb.Append("  at ")
              .Append(current.OriginFile).Append(':')
              .Append(current.Line).Append(':')
              .Append(current.Col).AppendLine();

            foreach (CallFrame? frame in _callStack.Reverse())
            {
                int ip = Math.Clamp(frame.ReturnIp, 0, insns.Count - 1);
                Instruction i = insns[ip];
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
            while (_tryHandlers.Count > 0)
            {
                TryHandler h = _tryHandlers.Peek();

                PopScopesToBase(h.ScopeDepthAtTry);

                if (h.CatchAddr >= 0)
                {
                    _stack.Push(exPayload);
                    newIp = h.CatchAddr;

                    h.CatchAddr = -1;
                    h.Exception = null;
                    while (_callStack.Count > h.CallDepth)
                    {
                        CallFrame fr = _callStack.Pop();

                        while (_scopes.Count > fr.BaseScopeDepth)
                            _scopes.RemoveAt(_scopes.Count - 1);

                        while (_tryHandlers.Count > 0 && _tryHandlers.Peek().CallDepth > _callStack.Count)
                            _tryHandlers.Pop();
                    }

                    return true;
                }

                if (h.FinallyAddr >= 0)
                {
                    h.Exception = exPayload;
                    newIp = h.FinallyAddr;
                    h.FinallyAddr = -1;
                    h.InFinally = true;
                    while (_callStack.Count > h.CallDepth)
                    {
                        CallFrame fr = _callStack.Pop();

                        while (_scopes.Count > fr.BaseScopeDepth)
                            _scopes.RemoveAt(_scopes.Count - 1);

                        while (_tryHandlers.Count > 0 && _tryHandlers.Peek().CallDepth > _callStack.Count)
                            _tryHandlers.Pop();
                    }

                    return true;
                }

                _tryHandlers.Pop();
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
            StringBuilder sb = new();
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
            object[] arr = _stack.ToArray();
            IEnumerable<string> parts = arr.Select(FormatVal);
            return string.Join(" | ", parts);
        }

        /// <summary>
        /// The DumpCallStack
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        private string DumpCallStack()
        {
            if (_callStack == null || _callStack.Count == 0) return "<empty>";

            CallFrame[] arr = _callStack.ToArray();
            int curDepth = _scopes?.Count ?? 0;

            IEnumerable<string> parts = arr.Select((fr, i) =>
            {
                bool isTop = (i == 0);
                int scopesPlus = Math.Max(0, curDepth - fr.BaseScopeDepth);

                string thisPart = fr.ThisRef != null ? FormatVal(fr.ThisRef) : "null";
                string scopeInfo = isTop
                    ? $"base={fr.BaseScopeDepth}, scopes+={scopesPlus}"
                    : $"base={fr.BaseScopeDepth}";

                return $"#{i}: ret={fr.ReturnIp}, {scopeInfo}, this={thisPart}";
            });

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
            StringBuilder sb = new(s.Length + 8);
            foreach (char ch in s)
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

                        List<KeyValuePair<string, object>> entries = dict.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
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
                            KeyValuePair<string, object> kv = entries[i];
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
            StringBuilder sb = new();
            using StringWriter sw = new(sb, CultureInfo.InvariantCulture);
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
        public static void PrintValue(object v, TextWriter w, int mode = 2, HashSet<object>? seen = null, bool escapeNewlines = false)
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

                List<KeyValuePair<string, object>> entries = dict.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
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
                    KeyValuePair<string, object> kv = entries[i];
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
            if (v is ClassInstance ci)
            {
                w.Write(ci.ClassName);
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
        /// The TryBindIntrinsic
        /// </summary>
        /// <param name="receiver">The receiver<see cref="object"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="bound">The bound<see cref="IntrinsicBound"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryBindIntrinsic(object receiver, string name, out IntrinsicBound bound, Instruction instr)
        {
            Type t = receiver?.GetType() ?? typeof(object);
            if (Intrinsics.TryGet(t, name, out IntrinsicDescriptor? desc))
            {
                IntrinsicMethod adapted = new(desc.Name, desc.ArityMin, desc.ArityMax,
                    (recv, args, ins) => desc.Invoke(recv, args, ins));
                bound = new IntrinsicBound(adapted, receiver);
                return true;
            }
            bound = null!;
            return false;
        }

        /// <summary>
        /// The GetIndexedValue
        /// </summary>
        /// <param name="target">The target<see cref="object"/></param>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="object"/></returns>
        private object GetIndexedValue(object target, object idxObj, Instruction instr)
        {
            switch (target)
            {
                case List<object> arr:
                    {
                        if (idxObj is string mname && TryBindIntrinsic(arr, mname, out IntrinsicBound? bound, instr))
                            return bound;

                        int index = Convert.ToInt32(idxObj);
                        if (index < 0 || index >= arr.Count)
                            throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                        return arr[index];
                    }

                case FileHandle fh:
                    {
                        if (idxObj is string mname && TryBindIntrinsic(fh, mname, out IntrinsicBound? bound, instr))
                            return bound;
                        throw new VMException($"invalid file member '{idxObj}'", instr.Line, instr.Col, instr.OriginFile);
                    }

                case ExceptionObject exo:
                    {
                        string key = idxObj?.ToString() ?? "";
                        if (idxObj is string mname && TryBindIntrinsic(exo, mname, out IntrinsicBound? bound, instr))
                            return bound;
                        if (string.Equals(key, "message$", StringComparison.Ordinal)) return exo.Message;
                        if (string.Equals(key, "type$", StringComparison.Ordinal)) return exo.Type;
                        throw new VMException($"invalid member '{key}' on Exception", instr.Line, instr.Col, instr.OriginFile);
                    }

                case string strv:
                    {
                        if (idxObj is string mname && TryBindIntrinsic(strv, mname, out IntrinsicBound? bound, instr))
                            return bound;

                        int index = Convert.ToInt32(idxObj);
                        if (index < 0 || index >= strv.Length)
                            throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                        return (char)strv[index];
                    }

                case Dictionary<string, object> dict:
                    {
                        if (idxObj is string mname && TryBindIntrinsic(dict, mname, out IntrinsicBound? bound, instr))
                            return bound;
                        string key = idxObj?.ToString() ?? "";
                        if (dict.TryGetValue(key, out object? val))
                            return val;
                        return null;
                    }

                case ClassInstance obj:
                    {
                        string key = idxObj?.ToString() ?? "";

                        if (key == "outer")
                        {
                            if (obj.Fields.TryGetValue("__outer", out object? outerVal))
                                return outerVal;

                            throw new VMException(
                                "Runtime error: missing '__outer' on instance for 'outer'.",
                                instr.Line, instr.Col, instr.OriginFile
                            );
                        }
                        if (obj.Fields.TryGetValue(key, out object? fval))
                        {
                            if (fval is Closure clos &&
                                clos.Parameters.Count > 0 && clos.Parameters[0] == "this")
                            {
                                return new BoundMethod(clos, obj);
                            }
                            return fval;
                        }

                        ClassInstance curInst = obj;
                        while (curInst.Fields.TryGetValue("__base", out object? bObj) && bObj is ClassInstance baseInst)
                        {
                            if (baseInst.Fields.TryGetValue(key, out object? bval))
                            {
                                if (bval is Closure bclos &&
                                    bclos.Parameters.Count > 0 && bclos.Parameters[0] == "this")
                                {
                                    return new BoundMethod(bclos, obj);
                                }
                                return bval;
                            }
                            curInst = baseInst;
                        }

                        if (obj.Fields.TryGetValue("__type", out object? tObj) && tObj is StaticInstance st2)
                        {
                            if (st2.Fields.TryGetValue(key, out object? sval))
                            {
                                if (sval is Closure sClos &&
                                    sClos.Parameters.Count > 0 && sClos.Parameters[0] == "type")
                                {
                                    return new BoundMethod(sClos, st2);
                                }
                                if (sval is StaticInstance nestedType)
                                {
                                    return new BoundType(nestedType, obj);
                                }
                                return sval;
                            }

                            StaticInstance curType = st2;
                            while (curType.Fields.TryGetValue("__base", out object? sbObj) && sbObj is StaticInstance baseType)
                            {
                                if (baseType.Fields.TryGetValue(key, out object? sv2))
                                {
                                    if (sv2 is Closure sClos2 &&
                                        sClos2.Parameters.Count > 0 && sClos2.Parameters[0] == "type")
                                    {
                                        return new BoundMethod(sClos2, st2);
                                    }
                                    if (sv2 is StaticInstance nestedType2)
                                    {
                                        return new BoundType(nestedType2, obj);
                                    }
                                    return sv2;
                                }
                                curType = baseType;
                            }
                        }

                        throw new VMException($"invalid field '{key}' in class '{obj.ClassName}'", instr.Line, instr.Col, instr.OriginFile);
                    }

                case StaticInstance st:
                    {
                        string key = idxObj?.ToString() ?? "";

                        if (key == "outer")
                        {
                            throw new VMException(
                                $"Runtime error: invalid static member 'outer' in class '{st.ClassName}'.",
                                instr.Line, instr.Col, instr.OriginFile
                            );
                        }
                        if (st.Fields.TryGetValue(key, out object? fval))
                        {
                            if (fval is Closure clos &&
                                clos.Parameters.Count > 0 && clos.Parameters[0] == "type")
                            {
                                return new BoundMethod(clos, st);
                            }
                            return fval;
                        }

                        StaticInstance curType = st;
                        while (curType.Fields.TryGetValue("__base", out object? sbObj) && sbObj is StaticInstance baseType)
                        {
                            if (baseType.Fields.TryGetValue(key, out object? bval))
                            {
                                if (bval is Closure bclos &&
                                    bclos.Parameters.Count > 0 && bclos.Parameters[0] == "type")
                                {
                                    return new BoundMethod(bclos, st);
                                }
                                return bval;
                            }
                            curType = baseType;
                        }

                        throw new VMException(
                            $"Runtime error: invalid static member '{key}' in class '{st.ClassName}'.",
                            instr.Line, instr.Col, instr.OriginFile
                        );
                    }
                case EnumInstance en:
                    {
                        string key = idxObj?.ToString() ?? "";

                        if (string.Equals(key, "name", StringComparison.Ordinal))
                        {
                            return new IntrinsicBound(new IntrinsicMethod("name", 0, 1, (recv, args, ins) =>
                            {
                                if (args.Count == 0) return en.EnumName;
                                string m = args[0]?.ToString() ?? "";
                                return (from sk in en.Values where sk.Value == (int)args[0] select sk.Key).FirstOrDefault() ?? "null";
                            }), en);
                        }
                        if (string.Equals(key, "contains", StringComparison.Ordinal))
                        {
                            return new IntrinsicBound(new IntrinsicMethod("contains", 1, 1, (recv, args, ins) =>
                            {
                                string m = args[0]?.ToString() ?? "";
                                return en.Values.ContainsKey(m);
                            }), en);
                        }
                        if (en.TryGet(key, out int enumVal))
                            return enumVal;

                        throw new VMException($"Runtime error: invalid enum member '{key}' in enum '{en.EnumName}'", instr.Line, instr.Col, instr.OriginFile);
                    }

                default:
                    if (idxObj is string defName && TryBindIntrinsic(target, defName, out IntrinsicBound? defbound, instr))
                        return defbound;
                    throw CreateIndexException(target, idxObj, instr);
            }
        }

        /// <summary>
        /// The SetIndexedValue
        /// </summary>
        /// <param name="target">The target<see cref="object"/></param>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <param name="value">The value<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        private void SetIndexedValue(ref object target, object idxObj, object value, Instruction instr)
        {
            switch (target)
            {
                case List<object> arr:
                    {
                        if (IsReservedIntrinsicName(arr, idxObj))
                            throw new VMException($"Runtime error: cannot assign to array intrinsic '{idxObj}'", instr.Line, instr.Col, instr.OriginFile);

                        int index = RequireIntIndex(idxObj, instr);
                        if (index < 0 || index >= arr.Count)
                            throw new VMException($"Runtime error: index {index} out of range (0..{arr.Count - 1})", instr.Line, instr.Col, instr.OriginFile);
                        arr[index] = value;
                        break;
                    }

                case string _:
                    throw new VMException("Runtime error: INDEX_SET on string. Strings are immutable.", instr.Line, instr.Col, instr.OriginFile);

                case Dictionary<string, object> dict:
                    {
                        if (IsReservedIntrinsicName(dict, idxObj))
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

                        if (key == "outer")
                        {
                            throw new VMException(
                                "Runtime error: cannot assign to 'outer' (read-only).",
                                instr.Line, instr.Col, instr.OriginFile
                            );
                        }
                        obj.Fields[key] = value;
                        break;
                    }

                case StaticInstance st:
                    {
                        string key = idxObj?.ToString() ?? "";
                        if (key.Length == 0)
                            throw new VMException("Runtime error: static member name cannot be empty", instr.Line, instr.Col, instr.OriginFile);

                        if (key == "outer")
                        {
                            throw new VMException(
                                "Runtime error: cannot assign to 'outer' on static type.",
                                instr.Line, instr.Col, instr.OriginFile
                            );
                        }
                        st.Fields[key] = value;
                        break;
                    }
                case EnumInstance en:
                    throw new VMException($"Runtime error: cannot assign to enum '{en.EnumName}' members", instr.Line, instr.Col, instr.OriginFile);

                case null:
                    throw new VMException("Runtime error: INDEX_SET on null target", instr.Line, instr.Col, instr.OriginFile);

                default:
                    throw new VMException("Runtime error: target is not index-assignable", instr.Line, instr.Col, instr.OriginFile);
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

            if (idxObj is string sVal && int.TryParse(sVal, out int parsed))
                return parsed;

            throw new VMException($"Runtime error: index must be an integer, got '{idxObj?.GetType().Name ?? "null"}'", instr.Line, instr.Col, instr.OriginFile);
        }

        /// <summary>
        /// The IsReservedIntrinsicName
        /// </summary>
        /// <param name="receiver">The receiver<see cref="object"/></param>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool IsReservedIntrinsicName(object receiver, object idxObj)
        {
            if (idxObj is not string name) return false;
            Type t = receiver?.GetType() ?? typeof(object);
            return Intrinsics.TryGet(t, name, out _);
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
    public sealed class VMException(string message, int line, int column, string? fileSource)
    : Exception(BuildMessage(message, line, column, fileSource))
    {
        /// <summary>
        /// Gets the Line
        /// </summary>
        public int Line { get; } = line;

        /// <summary>
        /// Gets the Column
        /// </summary>
        public int Column { get; } = column;

        /// <summary>
        /// Gets the FileSource
        /// </summary>
        public string? FileSource { get; } = fileSource;

        /// <summary>
        /// The BuildMessage
        /// </summary>
        /// <param name="message">The message<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="column">The column<see cref="int"/></param>
        /// <param name="fileSource">The fileSource<see cref="string?"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string BuildMessage(string message, int line, int column, string? fileSource)
        {
            StringBuilder sb = new();
            if (!string.IsNullOrEmpty(message))
            {
                sb.Append(message.TrimEnd());
                if (!message.TrimEnd().EndsWith(".")) sb.Append('.');
            }

            bool hasLine = line >= 0;
            bool hasCol = column >= 0;

            if (hasLine && hasCol) sb.Append($" ( Line : {line}, Column : {column} )");
            else if (hasLine) sb.Append($" ( Line : {line} )");
            else if (hasCol) sb.Append($" ( Column : {column} )");

            if (!string.IsNullOrWhiteSpace(fileSource))
                sb.Append($" [Source : '{fileSource}']");

            return sb.ToString();
        }
    }
}
