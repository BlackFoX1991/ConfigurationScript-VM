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
        /// Initializes a new instance of the <see cref="VM"/> class.
        /// </summary>
        public VM()
        {
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
            int start = startObj == null ? 0 : Convert.ToInt32(startObj);
            if (start < 0) start += len;

            int endEx = endObj == null ? len : Convert.ToInt32(endObj);
            if (endEx < 0) endEx += len;

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
   x is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

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
                    PrintValue(args[0], Console.Out, 1, escapeNewlines: false);
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
    };

        /// <summary>
        /// The FindEnvWithLocal
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="Env?"/></returns>
        private Env? FindEnvWithLocal(string name)
        {
            for (int i = _scopes.Count - 1; i >= 0; i--)
                if (_scopes[i].Vars.ContainsKey(name))
                    return _scopes[i];

            var env = _scopes[^1];
            while (env.Parent != null)
            {
                env = env.Parent;
                if (env.Vars.ContainsKey(name)) return env;
            }
            return null;
        }

        /// <summary>
        /// Defines the NumKind
        /// </summary>
        private enum NumKind
        {
            /// <summary>
            /// Defines the None
            /// </summary>
            None,

            /// <summary>
            /// Defines the Int
            /// </summary>
            Int,

            /// <summary>
            /// Defines the Long
            /// </summary>
            Long,

            /// <summary>
            /// Defines the Float
            /// </summary>
            Float,

            /// <summary>
            /// Defines the Double
            /// </summary>
            Double,

            /// <summary>
            /// Defines the Decimal
            /// </summary>
            Decimal,

            /// <summary>
            /// Defines the NotNumber
            /// </summary>
            NotNumber
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
        /// The GetNumKind
        /// </summary>
        /// <param name="v">The v<see cref="object"/></param>
        /// <returns>The <see cref="NumKind"/></returns>
        private static NumKind GetNumKind(object v)
        {
            if (v is int) return NumKind.Int;
            if (v is long) return NumKind.Long;
            if (v is double) return NumKind.Double;
            if (v is decimal) return NumKind.Decimal;
            return NumKind.None;
        }

        /// <summary>
        /// The PromoteKind
        /// </summary>
        /// <param name="a">The a<see cref="NumKind"/></param>
        /// <param name="b">The b<see cref="NumKind"/></param>
        /// <returns>The <see cref="NumKind"/></returns>
        private static NumKind PromoteKind(NumKind a, NumKind b)
        {
            if (a == NumKind.Decimal || b == NumKind.Decimal) return NumKind.Decimal;
            if (a == NumKind.Double || b == NumKind.Double) return NumKind.Double;
            if (a == NumKind.Long || b == NumKind.Long) return NumKind.Long;
            if (a == NumKind.Int && b == NumKind.Int) return NumKind.Int;
            return NumKind.None;
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
        /// The Run
        /// </summary>
        /// <param name="scriptname">The scriptname<see cref="string"/></param>
        /// <param name="_insns">The _insns<see cref="List{Instruction}"/></param>
        public void Run(string scriptname, List<Instruction> _insns)
        {
            int _ip = 0;
            while (_ip < _insns.Count)
            {
                var instr = _insns[_ip++];

                switch (instr.Code)
                {
                    case OpCode.PUSH_INT:
                        if (instr.Operand is null)
                            _stack.Push(0);
                        else
                            _stack.Push((int)instr.Operand);
                        break;
                    case OpCode.PUSH_LNG:
                        if (instr.Operand is null)
                            _stack.Push((long)0);
                        else
                            _stack.Push((long)instr.Operand);
                        break;
                    case OpCode.PUSH_FLT:
                        if (instr.Operand is null)
                            _stack.Push((float)0);
                        else
                            _stack.Push((float)instr.Operand);
                        break;
                    case OpCode.PUSH_DBL:
                        if (instr.Operand is null)
                            _stack.Push(0.0);
                        else
                            _stack.Push((double)instr.Operand);
                        break;
                    case OpCode.PUSH_DEC:
                        if (instr.Operand is null)
                            _stack.Push((decimal)0);
                        else
                            _stack.Push((decimal)instr.Operand);
                        break;
                    case OpCode.PUSH_STR:
                        if (instr.Operand is null)
                            _stack.Push("");
                        else
                            _stack.Push((string)instr.Operand);
                        break;
                    case OpCode.PUSH_CHR:
                        if (instr.Operand is null)
                            _stack.Push((char)0);
                        else
                            _stack.Push((char)instr.Operand);
                        break;
                    case OpCode.PUSH_BOOL:
                        if (instr.Operand is null)
                            _stack.Push(false);
                        else
                            _stack.Push((bool)instr.Operand);
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
                        }

                        void DoSliceSet(ref object target, object startObj, object endObj, object value, Instruction instr)
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
                                var res = PerformBinaryNumericOp(l, r,
                                    (a, b) => a + b,
                                    (a, b) => a + b,
                                    (a, b) => a + b,
                                    (a, b) => a + b,
                                    OpCode.ADD);
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

                            var res = PerformBinaryNumericOp(l, r,
                                (a, b) => a - b,
                                (a, b) => a - b,
                                (a, b) => a - b,
                                (a, b) => a - b,
                                OpCode.SUB);
                            _stack.Push(res);
                            break;
                        }

                    case OpCode.MUL:
                        {
                            var r = _stack.Pop();
                            var l = _stack.Pop();

                            if (IsNumber(l) && IsNumber(r))
                            {
                                var res = PerformBinaryNumericOp(l, r,
                                    (a, b) => a * b,
                                    (a, b) => a * b,
                                    (a, b) => a * b,
                                    (a, b) => a * b,
                                    OpCode.MUL);
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

                            var kind = PromoteKind(GetNumKind(l), GetNumKind(r));
                            if ((kind == NumKind.Int && Convert.ToInt32(r) == 0) ||
                                (kind == NumKind.Long && Convert.ToInt64(r) == 0))
                                throw new VMException("division by zero in MOD", instr.Line, instr.Col, instr.OriginFile);

                            var res = PerformBinaryNumericOp(l, r,
                                (a, b) => a % b,
                                (a, b) => a % b,
                                (a, b) => a % b,
                                (a, b) => a % b,
                                OpCode.MOD);
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

                            var ak = GetNumKind(l);
                            var bk = GetNumKind(r);
                            var k = PromoteKind(ak, bk);

                            switch (k)
                            {
                                case NumKind.Int:
                                    {
                                        int li = Convert.ToInt32(l);
                                        int ri = Convert.ToInt32(r);
                                        if (ri == 0) throw new VMException("division by zero", instr.Line, instr.Col, instr.OriginFile);
                                        _stack.Push(li / ri);
                                        break;
                                    }
                                case NumKind.Long:
                                    {
                                        long la = Convert.ToInt64(l);
                                        long rb = Convert.ToInt64(r);
                                        if (rb == 0) throw new VMException("division by zero", instr.Line, instr.Col, instr.OriginFile);
                                        _stack.Push(la / rb);
                                        break;
                                    }
                                case NumKind.Double:
                                    {
                                        double ld = Convert.ToDouble(l);
                                        double rd = Convert.ToDouble(r);
                                        _stack.Push(ld / rd);
                                        break;
                                    }
                                case NumKind.Decimal:
                                    {
                                        decimal ld = Convert.ToDecimal(l);
                                        decimal rd = Convert.ToDecimal(r);
                                        if (rd == 0m) throw new VMException("division by zero", instr.Line, instr.Col, instr.OriginFile);
                                        _stack.Push(ld / rd);
                                        break;
                                    }
                                default:
                                    throw new VMException($"Runtime error: cannot DIV {l?.GetType()} and {r?.GetType()}",
                                        instr.Line, instr.Col, instr.OriginFile);
                            }
                            break;
                        }

                    case OpCode.EXPO:
                        {
                            var r = _stack.Pop();
                            var l = _stack.Pop();

                            if (!IsNumber(l) || !IsNumber(r))
                                throw new VMException("EXPO on non-numeric types", instr.Line, instr.Col, instr.OriginFile);

                            var res = PerformBinaryNumericOp(l, r,
                                (a, b) =>
                                {
                                    if (b < 0) return Math.Pow(a, b);
                                    return (int)Math.Pow(a, b);
                                },
                                (a, b) =>
                                {
                                    if (b < 0) return Math.Pow(a, b);
                                    return (long)Math.Pow(a, b);
                                },
                                (a, b) => Math.Pow(a, b),
                                (a, b) => (decimal)Math.Pow((double)a, (double)b),
                                OpCode.EXPO);

                            _stack.Push(res);
                            break;
                        }

                    case OpCode.BIT_AND:
                        {
                            var r = _stack.Pop();
                            var l = _stack.Pop();

                            if (!(l is int || l is long) || !(r is int || r is long))
                                throw new VMException("BIT_AND requires integral types (int/long)", instr.Line, instr.Col, instr.OriginFile);

                            var res = PerformBinaryNumericOp(l, r,
                                (a, b) => a & b,
                                (a, b) => a & b,
                                (a, b) => throw new VMException("BIT_AND not supported on double", instr.Line, instr.Col, instr.OriginFile),
                                (a, b) => throw new VMException("BIT_AND not supported on decimal", instr.Line, instr.Col, instr.OriginFile),
                                OpCode.BIT_AND);
                            _stack.Push(res);
                            break;
                        }

                    case OpCode.BIT_OR:
                        {
                            var r = _stack.Pop();
                            var l = _stack.Pop();

                            if (!(l is int || l is long) || !(r is int || r is long))
                                throw new VMException("BIT_OR requires integral types (int/long)", instr.Line, instr.Col, instr.OriginFile);

                            var res = PerformBinaryNumericOp(l, r,
                                (a, b) => a | b,
                                (a, b) => a | b,
                                (a, b) => throw new VMException("BIT_OR not supported on double", instr.Line, instr.Col, instr.OriginFile),
                                (a, b) => throw new VMException("BIT_OR not supported on decimal", instr.Line, instr.Col, instr.OriginFile),
                                OpCode.BIT_OR);
                            _stack.Push(res);
                            break;
                        }

                    case OpCode.BIT_XOR:
                        {
                            var r = _stack.Pop();
                            var l = _stack.Pop();

                            if (!(l is int || l is long) || !(r is int || r is long))
                                throw new VMException("BIT_XOR requires integral types (int/long)", instr.Line, instr.Col, instr.OriginFile);

                            var res = PerformBinaryNumericOp(l, r,
                                (a, b) => a ^ b,
                                (a, b) => a ^ b,
                                (a, b) => throw new VMException("BIT_XOR not supported on double", instr.Line, instr.Col, instr.OriginFile),
                                (a, b) => throw new VMException("BIT_XOR not supported on decimal", instr.Line, instr.Col, instr.OriginFile),
                                OpCode.BIT_XOR);
                            _stack.Push(res);
                            break;
                        }

                    case OpCode.SHL:
                        {
                            var r = _stack.Pop();
                            var l = _stack.Pop();

                            if (!(l is int || l is long) || !IsNumber(r))
                                throw new VMException("SHL requires (int|long) << int", instr.Line, instr.Col, instr.OriginFile);

                            var res = PerformBinaryNumericOp(l, r,
                                (a, b) => a << (b & 0x1F),
                                (a, b) => a << (int)(b & 0x3F),
                                (a, b) => throw new VMException("SHL not supported on double", instr.Line, instr.Col, instr.OriginFile),
                                (a, b) => throw new VMException("SHL not supported on decimal", instr.Line, instr.Col, instr.OriginFile),
                                OpCode.SHL);
                            _stack.Push(res);
                            break;
                        }

                    case OpCode.SHR:
                        {
                            var r = _stack.Pop();
                            var l = _stack.Pop();

                            if (!(l is int || l is long) || !IsNumber(r))
                                throw new VMException("SHR requires (int|long) >> int", instr.Line, instr.Col, instr.OriginFile);

                            var res = PerformBinaryNumericOp(l, r,
                                (a, b) => a >> (b & 0x1F),
                                (a, b) => a >> (int)(b & 0x3F),
                                (a, b) => throw new VMException("SHR not supported on double", instr.Line, instr.Col, instr.OriginFile),
                                (a, b) => throw new VMException("SHR not supported on decimal", instr.Line, instr.Col, instr.OriginFile),
                                OpCode.SHR);
                            _stack.Push(res);
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
                                res = (ls == rs);
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
                                res = (ls != rs);
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
                                var v = PerformBinaryNumericOp(l, r,
                                    (a, b) => a < b, (a, b) => a < b, (a, b) => a < b, (a, b) => a < b, OpCode.LT);
                                _stack.Push(v);
                            }
                            else if (l is string ls && r is string rs)
                            {
                                _stack.Push(string.CompareOrdinal(ls, rs));
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
                                var v = PerformBinaryNumericOp(l, r,
                                    (a, b) => a > b, (a, b) => a > b, (a, b) => a > b, (a, b) => a > b, OpCode.GT);
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
                                var v = PerformBinaryNumericOp(l, r,
                                    (a, b) => a <= b, (a, b) => a <= b, (a, b) => a <= b, (a, b) => a <= b, OpCode.LE);
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
                                var v = PerformBinaryNumericOp(l, r,
                                    (a, b) => a >= b, (a, b) => a >= b, (a, b) => a >= b, (a, b) => a >= b, OpCode.GE);
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
                            if (v is int i) { _stack.Push(-i); }
                            else if (v is long l) { _stack.Push(-l); }
                            else if (v is double d) { _stack.Push(-d); }
                            else if (v is float f) { _stack.Push(-f); }
                            else if (v is decimal m) { _stack.Push(-m); }
                            else
                            {
                                throw new VMException(
                                    $"NEG only works on numeric types (got {v ?? "null"} of type {v?.GetType().Name ?? "null"})",
                                    instr.Line, instr.Col, instr.OriginFile
                                );
                            }
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

                    case OpCode.JMP:
                        {
                            if (instr.Operand is null)
                                throw new VMException("Runtime error: JMP missing target", instr.Line, instr.Col, instr.OriginFile);

                            _ip = (int)instr.Operand;
                            continue;
                        }

                    case OpCode.JMP_IF_FALSE:
                        {
                            if (instr.Operand is null)
                                throw new VMException("Runtime error: JMP_IF_FALSE missing target", instr.Line, instr.Col, instr.OriginFile);

                            var v = _stack.Pop();
                            if (!ToBool(v))
                            {
                                _ip = (int)instr.Operand;
                                continue;
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
                                continue;
                            }
                            break;
                        }

                    case OpCode.HALT:
                        return;

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
                                if (bInFunc.TryGetValue(funcName, out int argCount))
                                {
                                    if (argCount < bInFunc[funcName])
                                        throw new VMException($"Runtime error: {funcName}() expects {bInFunc[funcName]} argument(s), got {argCount}", instr.Line, instr.Col, instr.OriginFile);

                                    var args = new List<object>();
                                    for (int i = argCount - 1; i >= 0; i--)
                                        args.Insert(0, _stack.Pop());

                                    var result = CallBuiltin(funcName, args, instr);
                                    _stack.Push(result);
                                    break;
                                }

                                if (!_functions.TryGetValue(funcName, out var func))
                                    throw new VMException($"Runtime error: unknown function {funcName}", instr.Line, instr.Col, instr.OriginFile);

                                if (func.Parameters.Count > 0 && func.Parameters[0] == "this")
                                    throw new VMException($"Runtime error: cannot CALL method '{funcName}' without receiver. Use CALL_INDIRECT with a bound receiver.", instr.Line, instr.Col, instr.OriginFile);

                                var callEnv = new Env(_scopes[^1]);
                                for (int i = func.Parameters.Count - 1; i >= 0; i--)
                                {
                                    var argValue = _stack.Pop();
                                    callEnv.Define(func.Parameters[i], argValue);
                                }

                                _scopes.Add(callEnv);
                                _callStack.Push(new CallFrame(_ip, 1, null));
                                _ip = func.Address;
                                continue;
                            }
                            else
                            {
                                var fn = _stack.Pop();
                                if (fn is not Closure clos)
                                    throw new VMException($"Runtime error: CALL target is not a closure", instr.Line, instr.Col, instr.OriginFile);

                                if (clos.Parameters.Count > 0 && clos.Parameters[0] == "this")
                                    throw new VMException("Runtime error: cannot CALL a method-closure without receiver. Use CALL_INDIRECT after INDEX_GET or provide the receiver explicitly.", instr.Line, instr.Col, instr.OriginFile);

                                var callEnv = new Env(clos.CapturedEnv);
                                for (int i = clos.Parameters.Count - 1; i >= 0; i--)
                                {
                                    var argValue = _stack.Pop();
                                    callEnv.Define(clos.Parameters[i], argValue);
                                }

                                _scopes.Add(callEnv);
                                _callStack.Push(new CallFrame(_ip, 1, null));
                                _ip = clos.Address;
                                continue;
                            }
                        }

                    case OpCode.CALL_INDIRECT:
                        {
                            if (instr.Operand is int explicitArgCount)
                            {
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

                                if (callee is BoundMethod bm)
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
                                    throw new VMException("Runtime error: attempt to call non-function value.", instr.Line, instr.Col, instr.OriginFile);
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
                                continue;
                            }
                            else
                            {
                                if (_stack.Count == 0)
                                    throw new VMException("Runtime error: missing callee for CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile);

                                var callee = _stack.Pop();
                                Closure f;
                                object? receiver = null;

                                if (callee is BoundMethod bm)
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
                                    throw new VMException("Runtime error: attempt to call non-function value.", instr.Line, instr.Col, instr.OriginFile);
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
                                continue;
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
                            continue;
                        }

                    case OpCode.TRY_PUSH:
                        {
                            var arr = instr.Operand as int[] ?? new int[] { -1, -1 };
                            int catchAddr = arr.Length > 0 ? arr[0] : -1;
                            int finallyAddr = arr.Length > 1 ? arr[1] : -1;
                            _tryHandlers.Add(new TryHandler(catchAddr, finallyAddr));
                            break;
                        }

                    case OpCode.TRY_POP:
                        {
                            if (_tryHandlers.Count == 0) throw new VMException($"Runtime error: TRY_POP with empty try stack", instr.Line, instr.Col, instr.OriginFile);
                            _tryHandlers.RemoveAt(_tryHandlers.Count - 1);
                            break;
                        }

                    case OpCode.THROW:
                        {
                            var ex = _stack.Pop();
                            bool handled = false;

                            for (int i = _tryHandlers.Count - 1; i >= 0; i--)
                            {
                                var h = _tryHandlers[i];
                                if (h.CatchAddr >= 0)
                                {
                                    _stack.Push(ex);
                                    _ip = h.CatchAddr;
                                    h.CatchAddr = -1;
                                    handled = true;
                                    break;
                                }
                                else if (h.FinallyAddr >= 0)
                                {
                                    h.Exception = ex;
                                    _ip = h.FinallyAddr;
                                    handled = true;
                                    break;
                                }
                                else
                                {
                                    _tryHandlers.RemoveAt(i);
                                }
                            }

                            if (!handled)
                            {
                                throw new VMException($"Uncaught exception: {ex}", instr.Line, instr.Col, instr.OriginFile);
                            }
                            break;
                        }

                    case OpCode.END_FINALLY:
                        {
                            if (_tryHandlers.Count == 0) break;
                            var h = _tryHandlers[^1];
                            _tryHandlers.RemoveAt(_tryHandlers.Count - 1);

                            if (h.Exception != null)
                            {
                                var toRethrow = h.Exception;
                                h.Exception = null;
                                bool handled2 = false;
                                for (int i = _tryHandlers.Count - 1; i >= 0; i--)
                                {
                                    var nh = _tryHandlers[i];
                                    if (nh.CatchAddr >= 0)
                                    {
                                        _stack.Push(toRethrow);
                                        _ip = nh.CatchAddr;
                                        nh.CatchAddr = -1;
                                        handled2 = true;
                                        break;
                                    }
                                    else if (nh.FinallyAddr >= 0)
                                    {
                                        nh.Exception = toRethrow;
                                        _ip = nh.FinallyAddr;
                                        handled2 = true;
                                        break;
                                    }
                                    else
                                    {
                                        _tryHandlers.RemoveAt(i);
                                    }
                                }

                                if (!handled2)
                                {
                                    throw new VMException($"Uncaught exception: {toRethrow}", instr.Line, instr.Col, instr.OriginFile);
                                }
                            }
                            break;
                        }

                    default:
                        throw new VMException($"Runtime error: unknown opcode {instr.Code}", instr.Line, instr.Col, instr.OriginFile);
                }
            }
        }

        /// <summary>
        /// The EscapeJsonString
        /// </summary>
        /// <param name="s">The s<see cref="string"/></param>
        /// <param name="escapeNewlines">The escapeNewlines<see cref="bool"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string EscapeJsonString(string s, bool escapeNewlines = true)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\n': sb.Append(escapeNewlines ? "\\n" : "\n"); break;
                    case '\r': sb.Append(escapeNewlines ? "\\r" : "\r"); break;
                    case '\t': sb.Append(escapeNewlines ? "\\t" : "\t"); break;
                    default:
                        if (char.IsControl(c))
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
                        }
                        else sb.Append(c);
                        break;
                }
            }
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
                                     && !(kv.Value is Closure)
                                     && !(kv.Value is FunctionInfo)
                                     && !(kv.Value is Delegate))];
                }

                w.Write("{");
                for (int i = 0; i < entries.Count; i++)
                {
                    var kv = entries[i];
                    w.Write("\"");
                    w.Write(escapeNewlines ? EscapeJsonString(kv.Key) : kv.Key);
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
                    w.Write("\"" + (escapeNewlines ? EscapeJsonString(clos.Name ?? "<closure>") : clos.Name ?? "<closure>") + "\"");
                else
                    w.Write(clos.ToString());
                return;
            }

            if (v is FunctionInfo fi) { w.Write($"<fn {fi.Address}>"); return; }
            if (v is Delegate d) { w.Write("<delegate>"); return; }

            switch (v)
            {
                case string s:
                    if (mode == 2 || mode == 1)
                    {
                        w.Write(escapeNewlines ? EscapeJsonString(s) : s);
                    }
                    else
                    {
                        w.Write(s);
                    }
                    break;
                case double xd: w.Write(xd.ToString(CultureInfo.InvariantCulture)); break;
                case float f: w.Write(f.ToString(CultureInfo.InvariantCulture)); break;
                case decimal m: w.Write(m.ToString(CultureInfo.InvariantCulture)); break;
                case long l: w.Write(l.ToString(CultureInfo.InvariantCulture)); break;
                case int i: w.Write(i.ToString(CultureInfo.InvariantCulture)); break;
                case bool b: w.Write(b ? "true" : "false"); break;
                default: w.Write(Convert.ToString(v, CultureInfo.InvariantCulture)); break;
            }
            w.Flush();
        }

        /// <summary>
        /// The PerformBinaryNumericOp
        /// </summary>
        /// <param name="l">The l<see cref="object"/></param>
        /// <param name="r">The r<see cref="object"/></param>
        /// <param name="intOp">The intOp<see cref="Func{int, int, object}"/></param>
        /// <param name="longOp">The longOp<see cref="Func{long, long, object}"/></param>
        /// <param name="doubleOp">The doubleOp<see cref="Func{double, double, object}"/></param>
        /// <param name="decimalOp">The decimalOp<see cref="Func{decimal, decimal, object}"/></param>
        /// <param name="code">The code<see cref="OpCode"/></param>
        /// <returns>The <see cref="object"/></returns>
        private static object PerformBinaryNumericOp(
            object l, object r,
            Func<int, int, object> intOp,
            Func<long, long, object> longOp,
            Func<double, double, object> doubleOp,
            Func<decimal, decimal, object> decimalOp,
            OpCode code)
        {
            var ak = GetNumKind(l);
            var bk = GetNumKind(r);
            var k = PromoteKind(ak, bk);

            if (k == NumKind.None)
                throw new Exception($"Runtime error: cannot perform {code} on non-numeric types '{l?.GetType()}' and '{r?.GetType()}'");

            switch (k)
            {
                case NumKind.Int:
                    return intOp(Convert.ToInt32(l is bool bl ? (bl ? 1 : 0) : l),
                                 Convert.ToInt32(r is bool br ? (br ? 1 : 0) : r));
                case NumKind.Long:
                    return longOp(Convert.ToInt64(l), Convert.ToInt64(r));
                case NumKind.Double:
                    return doubleOp(Convert.ToDouble(l), Convert.ToDouble(r));
                case NumKind.Decimal:
                    return decimalOp(Convert.ToDecimal(l), Convert.ToDecimal(r));
                default:
                    throw new Exception($"Runtime error: unsupported numeric promotion for {code}");
            }
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
                        int index = Convert.ToInt32(idxObj);
                        if (index < 0 || index >= arr.Count)
                            throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                        return arr[index];
                    }

                case string strv:
                    {
                        int index = Convert.ToInt32(idxObj);
                        if (index < 0 || index >= strv.Length)
                            throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                        return strv[index];
                    }

                case Dictionary<string, object> dict:
                    {
                        string key = idxObj?.ToString() ?? "";
                        if (dict.TryGetValue(key, out var val))
                            return val;
                        return 0;
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
                        int index = Convert.ToInt32(idxObj);
                        if (index < 0 || index >= arr.Count)
                            throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                        arr[index] = value;
                        break;
                    }

                case string strv:
                    {
                        throw new VMException("Runtime error: INDEX_SET with string. Strings are immutable", instr.Line, instr.Col, instr.OriginFile);
                    }

                case Dictionary<string, object> dict:
                    {
                        string key = idxObj?.ToString() ?? "";
                        dict[key] = value;
                        break;
                    }

                case ClassInstance obj:
                    {
                        string key = idxObj?.ToString() ?? "";
                        obj.Fields[key] = value;
                        break;
                    }

                default:
                    throw new VMException($"Runtime error: target is not index-assignable", instr.Line, instr.Col, instr.OriginFile);
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
