using CFGS_VM.VMCore.Extension;
using System;
using System.Globalization;
using System.Security;
using System.Text;

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
        /// <param name="parent">The parent<see cref="Env"/></param>
        public Env(Env? parent)
        {
            Parent = parent;
        }

        /// <summary>
        /// The TryGetValue
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object"/></param>
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

    private record CallFrame(int ReturnIp, int ScopesAdded);
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
    /// The CallBuiltin
    /// </summary>
    /// <param name="name">The name<see cref="string"/></param>
    /// <param name="args">The args<see cref="List{object}"/></param>
    /// <param name="instr">The instr<see cref="Instruction"/></param>
    /// <returns>The <see cref="object"/></returns>
    private object CallBuiltin(string name, List<object> args, Instruction instr)
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
                    return new List<object>{ "null"};
                var fld = args[0] as Dictionary<string,object>;
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

            case "str":
                return args[0].ToString() ?? "";

            case "toi16":
                return Convert.ToInt16(args[0]);
            case "toi32":
                return Convert.ToInt32(args[0]);
            case "toi64":
                return Convert.ToInt64(args[0]);
            case "abs":
                return Math.Abs((dynamic)args[0]);
            case "rand":
                return new Random().Next();

            case "print":
                PrintValue(args[0], Console.Out, 1, escapeNewlines: false);
                Console.WriteLine();
                return 1;

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
        {"rand",0 },
        {"getfields",1 }
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
    /// The GetNumKind
    /// </summary>
    /// <param name="o">The o<see cref="object"/></param>
    /// <returns>The <see cref="NumKind"/></returns>
    private static NumKind GetNumKind(object o)
    {
        if (o is int) return NumKind.Int;
        if (o is long) return NumKind.Long;
        if (o is float) return NumKind.Float;
        if (o is double) return NumKind.Double;
        if (o is decimal) return NumKind.Decimal;
        return NumKind.NotNumber;
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
        if (a == NumKind.Float || b == NumKind.Float) return NumKind.Double;
        if (a == NumKind.Long || b == NumKind.Long) return NumKind.Long;
        if (a == NumKind.Int || b == NumKind.Int) return NumKind.Int;
        return NumKind.NotNumber;
    }

    /// <summary>
    /// The IsNumber
    /// </summary>
    /// <param name="v">The v<see cref="object"/></param>
    /// <returns>The <see cref="bool"/></returns>
    private static bool IsNumber(object v) => GetNumKind(v) != NumKind.NotNumber;

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
                        _stack.Push((int)0);
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
                        _stack.Push((double)0.0);
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
                        _stack.Push((string)"");
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
                    _stack.Push(0); 
                    break;

                case OpCode.PUSH_SCOPE:
                    _scopes.Add(new Env(_scopes[^1]));
                    break;
                case OpCode.POP_SCOPE:
                    if (_scopes.Count > 1) _scopes.RemoveAt(_scopes.Count - 1);
                    else throw new VMException($"Runtime error: cannot pop global scope", instr.Line, instr.Col, instr.OriginFile);
                    break;

                case OpCode.NEW_OBJECT:
                    {
                        string className = instr.Operand?.ToString() ?? "<anon>";
                        var obj = new ClassInstance(className);
                        _stack.Push(obj);
                        break;
                    }


                case OpCode.NEW_ARRAY:
                    {

                        int count = instr.Operand is null ? throw new VMException("Null Reference", instr.Line, instr.Col, instr.OriginFile) : (int)instr.Operand;
                        var temp = new object[count];
                        for (int i = count - 1; i >= 0; i--) temp[i] = _stack.Pop();
                        var list = new List<object>(temp);
                        _stack.Push(list);
                        break;
                    }

                case OpCode.SLICE_GET:
                    {
                        if (instr.Operand != null)
                        {
                            var endObj = _stack.Pop();
                            var startObj = _stack.Pop();
                            string name = (string)instr.Operand;
                            var owner = FindEnvWithLocal(name);
                            if (owner == null)
                                throw new VMException($"Runtime error: undefined variable '{name}", instr.Line, instr.Col, instr.OriginFile);

                            var target = owner.Vars[name];

                            if (target is List<object> arr)
                            {
                                int start = startObj == null ? 0 : Convert.ToInt32(startObj);
                                int end = endObj == null ? arr.Count - 1 : Convert.ToInt32(endObj);
                                start = Math.Max(0, start);
                                end = Math.Min(arr.Count - 1, end);
                                if (end < start) end = start;
                                _stack.Push(arr.GetRange(start, (end + 1) - start));
                            }
                            else if (target is Dictionary<string, object> dict)
                            {
                                var keys = dict.Keys.ToList();
                                int start = startObj == null ? 0 : Convert.ToInt32(startObj);
                                int end = endObj == null ? keys.Count - 1 : Convert.ToInt32(endObj);
                                start = Math.Max(0, start);
                                end = Math.Min(keys.Count - 1, end);
                                if (end < start) end = start;

                                var sliceDict = new Dictionary<string, object>();
                                for (int i = start; i <= end; i++)
                                    sliceDict[keys[i]] = dict[keys[i]];

                                _stack.Push(sliceDict);
                            }
                            else
                            {
                                throw new VMException($"Runtime error: SLICE_GET target must be array or dictionary", instr.Line, instr.Col, instr.OriginFile);
                            }
                        }
                        else
                        {
                            var endObj = _stack.Pop();
                            var startObj = _stack.Pop();
                            var target = _stack.Pop();

                            if (target is List<object> arr)
                            {
                                int start = startObj == null ? 0 : Convert.ToInt32(startObj);
                                int end = endObj == null ? arr.Count - 1 : Convert.ToInt32(endObj);
                                start = Math.Max(0, start);
                                end = Math.Min(arr.Count - 1, end);
                                if (end < start) end = start;
                                _stack.Push(arr.GetRange(start, (end + 1) - start));
                            }
                            else if (target is Dictionary<string, object> dict)
                            {
                                var keys = dict.Keys.ToList();
                                int start = startObj == null ? 0 : Convert.ToInt32(startObj);
                                int end = endObj == null ? keys.Count - 1 : Convert.ToInt32(endObj);
                                start = Math.Max(0, start);
                                end = Math.Min(keys.Count - 1, end);
                                if (end < start) end = start;

                                var sliceDict = new Dictionary<string, object>();
                                for (int i = start; i <= end; i++)
                                    sliceDict[keys[i]] = dict[keys[i]];

                                _stack.Push(sliceDict);
                            }
                            else
                            {
                                throw new VMException($"Runtime error: SLICE_GET target must be array or dictionary", instr.Line, instr.Col, instr.OriginFile);
                            }
                        }
                        break;
                    }

                case OpCode.SLICE_SET:
                    {
                        if (instr.Operand != null)
                        {
                            var value = _stack.Pop();
                            var endObj = _stack.Pop();
                            var startObj = _stack.Pop();
                            string name = (string)instr.Operand;
                            var env = FindEnvWithLocal(name);
                            if (env == null)
                                throw new VMException($"Runtime error: undefined variable '{name}", instr.Line, instr.Col, instr.OriginFile);

                            var target = env.Vars[name];

                            if (target is List<object> arr)
                            {
                                int start = startObj == null ? 0 : Convert.ToInt32(startObj);
                                int end = endObj == null ? arr.Count - 1 : Convert.ToInt32(endObj);
                                start = Math.Max(0, start);
                                end = Math.Min(arr.Count - 1, end);
                                if (end < start) end = start;

                                if (value is List<object> lst)
                                {
                                    for (int i = 0; i <= end - start && i < lst.Count; i++)
                                        arr[start + i] = lst[i];
                                }
                                else
                                {
                                    throw new VMException($"Runtime error: trying to assign non-list to array slice", instr.Line, instr.Col, instr.OriginFile);
                                }
                            }
                            else if (target is Dictionary<string, object> dict)
                            {
                                var keys = dict.Keys.ToList();
                                int start = startObj == null ? 0 : Convert.ToInt32(startObj);
                                int end = endObj == null ? keys.Count - 1 : Convert.ToInt32(endObj);
                                start = Math.Max(0, start);
                                end = Math.Min(keys.Count - 1, end);
                                if (end < start) end = start;

                                if (value is Dictionary<string, object> valDict)
                                {
                                    int i = 0;
                                    for (int k = start; k <= end && i < valDict.Count; k++, i++)
                                    {
                                        dict[keys[k]] = valDict.ElementAt(i).Value;
                                    }
                                }
                                else
                                {
                                    throw new VMException($"Runtime error: trying to assign non-dictionary to dictionary slice", instr.Line, instr.Col, instr.OriginFile);
                                }
                            }
                            else
                            {
                                throw new VMException($"Runtime error: SLICE_SET target must be array or dictionary", instr.Line, instr.Col, instr.OriginFile);
                            }
                        }
                        else
                        {
                            var value = _stack.Pop();
                            var endObj = _stack.Pop();
                            var startObj = _stack.Pop();
                            var target = _stack.Pop();

                            if (target is List<object> arr)
                            {
                                int start = startObj == null ? 0 : Convert.ToInt32(startObj);
                                int end = endObj == null ? arr.Count - 1 : Convert.ToInt32(endObj);
                                start = Math.Max(0, start);
                                end = Math.Min(arr.Count - 1, end);
                                if (end < start) end = start;

                                if (value is List<object> lst)
                                {
                                    for (int i = 0; i <= end - start && i < lst.Count; i++)
                                        arr[start + i] = lst[i];
                                }
                                else
                                {
                                    throw new VMException($"Runtime error: trying to assign non-list to array slice", instr.Line, instr.Col, instr.OriginFile);
                                }
                            }
                            else if (target is Dictionary<string, object> dict)
                            {
                                var keys = dict.Keys.ToList();
                                int start = startObj == null ? 0 : Convert.ToInt32(startObj);
                                int end = endObj == null ? keys.Count - 1 : Convert.ToInt32(endObj);
                                start = Math.Max(0, start);
                                end = Math.Min(keys.Count - 1, end);
                                if (end < start) end = start;

                                if (value is Dictionary<string, object> valDict)
                                {
                                    int i = 0;
                                    for (int k = start; k <= end && i < valDict.Count; k++, i++)
                                    {
                                        dict[keys[k]] = valDict.ElementAt(i).Value;
                                    }
                                }
                                else
                                {
                                    throw new VMException($"Runtime error: trying to assign non-dictionary to dictionary slice", instr.Line, instr.Col, instr.OriginFile);
                                }
                            }
                            else
                            {
                                throw new VMException($"Runtime error: SLICE_SET target must be array or dictionary", instr.Line, instr.Col, instr.OriginFile);
                            }
                        }
                        break;
                    }

                case OpCode.INDEX_GET:
                    {
                        if (instr.Operand != null)
                        {
                            var idxObj = _stack.Pop();
                            string name = (string)instr.Operand;
                            var owner = FindEnvWithLocal(name);
                            if (owner == null)
                                throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile);

                            var target = owner.Vars[name];

                            if (target is List<object> arr)
                            {
                                int index = Convert.ToInt32(idxObj);
                                if (index >= 0 && index < arr.Count) _stack.Push(arr[index]);
                                else throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                            }
                            else if (target is string strv)
                            {
                                int index = Convert.ToInt32(idxObj);
                                if (index >= 0 && index < strv.Length) _stack.Push(strv[index]);
                                else throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                            }
                            else if (target is Dictionary<string, object> dict)
                            {
                                string key = idxObj?.ToString() ?? "";
                                if (key == null)
                                    throw new VMException($"Runtime error: dictionary key cannot be null", instr.Line, instr.Col, instr.OriginFile);
                                if (dict.TryGetValue(key, out var val))
                                    _stack.Push(val);
                                else
                                    _stack.Push(0);
                            }
                            else if (target is ClassInstance obj)
                            {
                                string key = idxObj?.ToString() ?? "";
                                if (obj.Fields.TryGetValue(key, out var val))
                                    _stack.Push(val);
                                else
                                    _stack.Push(0);
                            }

                            else
                            {
                                string tid = target?.GetType().FullName ?? "null";
                                string tval;
                                try { tval = target?.ToString() ?? "null"; }
                                catch { tval = "<ToString() failed>"; }

                                string iid = idxObj?.GetType().FullName ?? "null";
                                string ival;
                                try { ival = idxObj?.ToString() ?? "null"; }
                                catch { ival = "<ToString() failed>"; }

                                throw new VMException(
        $"Runtime error: INDEX_GET target is not indexable.\n" +
        $"  target type: {tid}\n" +
        $"  target value: {tval}\n" +
        $"  index type: {iid}\n" +
        $"  index value: {ival}",
        instr.Line,
        instr.Col,
        instr.OriginFile
    );
                            }
                        }
                        else
                        {
                            var idxObj = _stack.Pop();
                            var target = _stack.Pop();

                            if (target is List<object> arr)
                            {
                                int index = Convert.ToInt32(idxObj);
                                if (index < 0 || index >= arr.Count)
                                    throw new VMException($"Runtime error: array index out of range", instr.Line, instr.Col, instr.OriginFile);
                                _stack.Push(arr[index]);
                            }
                            else if (target is string strv)
                            {
                                int index = Convert.ToInt32(idxObj);
                                if (index >= 0 && index < strv.Length) _stack.Push(strv[index]);
                                else throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                            }
                            else if (target is Dictionary<string, object> dict)
                            {
                                string key = idxObj?.ToString() ?? "";
                                if (key == null)
                                    throw new VMException($"Runtime error: dictionary key cannot be null", instr.Line, instr.Col, instr.OriginFile);
                                if (dict.TryGetValue(key, out var val))
                                    _stack.Push(val);
                                else
                                    _stack.Push(0);
                            }
                            else if (target is ClassInstance obj)
                            {
                                string key = idxObj?.ToString() ?? "";
                                if (obj.Fields.TryGetValue(key, out var val))
                                    _stack.Push(val);
                                else
                                    _stack.Push(0);
                            }

                            else
                            {
                                string tid = target?.GetType().FullName ?? "null";
                                string tval;
                                try { tval = target?.ToString() ?? "null"; }
                                catch { tval = "<ToString() failed>"; }

                                string iid = idxObj?.GetType().FullName ?? "null";
                                string ival;
                                try { ival = idxObj?.ToString() ?? "null"; }
                                catch { ival = "<ToString() failed>"; }

                                throw new VMException(
        $"Runtime error: INDEX_GET target is not indexable.\n" +
        $"  target type: {tid}\n" +
        $"  target value: {tval}\n" +
        $"  index type: {iid}\n" +
        $"  index value: {ival}",
                instr.Line,
        instr.Col,
        instr.OriginFile
    );
                            }
                        }
                        break;
                    }

                case OpCode.INDEX_SET:
                    {
                        if (instr.Operand != null)
                        {
                            var value = _stack.Pop();
                            var idxObj = _stack.Pop();
                            string name = (string)instr.Operand;
                            var env = FindEnvWithLocal(name);
                            if (env == null)
                                throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile);

                            var target = env.Vars[name];

                            if (target is List<object> arr)
                            {
                                int index = Convert.ToInt32(idxObj);
                                if (index < 0 || index >= arr.Count)
                                    throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                                arr[index] = value;
                            }
                            else if (target is string strv)
                            {
                                int index = Convert.ToInt32(idxObj);
                                if (index < 0 || index >= strv.Length)
                                    throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                                StringBuilder rep = new StringBuilder(strv);
                                rep[index] = (value?.ToString() ?? "")[0];
                                strv = rep.ToString();
                            }
                            else if (target is Dictionary<string, object> dict)
                            {
                                string key = idxObj?.ToString() ?? "";
                                if (key == null)
                                    throw new VMException($"Runtime error: dictionary key cannot be null", instr.Line, instr.Col, instr.OriginFile);
                                dict[key] = value;
                            }
                            else if (target is ClassInstance obj)
                            {
                                string key = idxObj?.ToString() ?? "";
                                obj.Fields[key] = value;
                            }

                            else
                            {
                                throw new VMException($"Runtime error: variable '{name}' is not index-assignable", instr.Line, instr.Col, instr.OriginFile);
                            }
                        }
                        else
                        {
                            var value = _stack.Pop();
                            var idxObj = _stack.Pop();
                            var target = _stack.Pop();

                            if (target is List<object> arr)
                            {
                                int index = Convert.ToInt32(idxObj);
                                if (index < 0 || index >= arr.Count)
                                    throw new VMException($"Runtime error: array index out of range", instr.Line, instr.Col, instr.OriginFile);
                                arr[index] = value;
                            }
                            else if (target is string strv)
                            {
                                int index = Convert.ToInt32(idxObj);
                                if (index < 0 || index >= strv.Length)
                                    throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile);
                                StringBuilder rep = new StringBuilder(strv);
                                rep[index] = (value.ToString() ?? "")[0];
                                strv = rep.ToString();
                            }
                            else if (target is Dictionary<string, object> dict)
                            {
                                string key = idxObj?.ToString() ?? "";
                                if (key == null)
                                    throw new VMException($"Runtime error: dictionary key cannot be null", instr.Line, instr.Col, instr.OriginFile);
                                dict[key] = value;
                            }
                            else if (target is ClassInstance obj)
                            {
                                string key = idxObj?.ToString() ?? "";
                                obj.Fields[key] = value;
                            }

                            else
                            {
                                throw new VMException($"Runtime error: INDEX_SET target is not index-assignable", instr.Line, instr.Col, instr.OriginFile);
                            }
                        }
                        break;
                    }

                case OpCode.NEW_DICT:
                    {
                        int count = instr.Operand is null ? throw new VMException("Null Reference", instr.Line, instr.Col, instr.OriginFile) : (int)instr.Operand;
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
                            if (env == null || !env.Vars.ContainsKey(name))
                                throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile);

                            var obj = env.Vars[name];
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
                        string name = instr.Operand is null ? throw new VMException("Null Reference", instr.Line, instr.Col, instr.OriginFile) : (string)instr.Operand;
                        var env = FindEnvWithLocal(name);
                        if (env == null || !env.Vars.ContainsKey(name))
                            throw new VMException($"Runtime error: undefined variable '{name}", instr.Line, instr.Col, instr.OriginFile);

                        var target = env.Vars[name];
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
                        string name = instr.Operand is null ? throw new VMException("Null Reference", instr.Line, instr.Col, instr.OriginFile) : (string)instr.Operand;
                        var owner = FindEnvWithLocal(name);
                        if (owner == null || !owner.Vars.TryGetValue(name, out var val))
                            throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile);
                        _stack.Push(val);
                        break;
                    }

                case OpCode.STORE_VAR:
                    {
                        string name = instr.Operand is null ? throw new VMException("Null Reference", instr.Line, instr.Col, instr.OriginFile) : (string)instr.Operand;
                        var value = _stack.Pop();
                        var env = FindEnvWithLocal(name);
                        if (env == null) throw new VMException($"Runtime error: assignment to undeclared variable '{name}'", instr.Line, instr.Col, instr.OriginFile);
                        env.Vars[name] = value;
                        break;
                    }

                case OpCode.VAR_DECL:
                    {
                        string name = instr.Operand is null ? throw new VMException("Null Reference", instr.Line, instr.Col, instr.OriginFile) : (string)instr.Operand;
                        var value = _stack.Pop();
                        var scope = _scopes[^1];
                        if (scope.HasLocal(name)) throw new VMException($"Runtime error: variable '{name}' already declared in this scope", instr.Line, instr.Col, instr.OriginFile);
                        scope.Define(name, value);
                        break;
                    }

                case OpCode.ADD:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();

                        if (IsNumber(l) && IsNumber(r))
                        {
                            var res = PerformBinaryNumericOp(l, r,
                                (int a, int b) => a + b,
                                (long a, long b) => a + b,
                                (double a, double b) => a + b,
                                (decimal a, decimal b) => a + b,
                                OpCode.ADD);
                            _stack.Push(res);
                        }
                        else if (l is List<object> || r is List<object> ||
                                 l is Dictionary<string, object> || r is Dictionary<string, object>)
                        {
                            string ls, rs;
                            using (var lw = new System.IO.StringWriter()) { PrintValue(l, lw); ls = lw.ToString(); }
                            using (var rw = new System.IO.StringWriter()) { PrintValue(r, rw); rs = rw.ToString(); }
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


                        var res = PerformBinaryNumericOp(l, r,
                        (int a, int b) => a - b,
                        (long a, long b) => a - b,
                        (double a, double b) => a - b,
                        (decimal a, decimal b) => a - b,
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
                                (int a, int b) => a * b,
                                (long a, long b) => a * b,
                                (double a, double b) => a * b,
                                (decimal a, decimal b) => a * b,
                                OpCode.MUL);
                            _stack.Push(res);
                        }
                        else if (l is string && IsNumber(r))
                        {
                            _stack.Push(string.Concat(Enumerable.Repeat(l.ToString() ?? "", Convert.ToInt32(r))));
                        }
                        else if (r is string && IsNumber(l))
                        { 
                            _stack.Push(string.Concat(Enumerable.Repeat(r.ToString() ?? "", Convert.ToInt32(l))));
                        }
                        break;
                    }

                case OpCode.MOD:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        var res = PerformBinaryNumericOp(l, r,
                            (int a, int b) => a % b,
                            (long a, long b) => a % b,
                            (double a, double b) => a % b,
                            (decimal a, decimal b) => a % b,
                            OpCode.MOD);
                        _stack.Push(res);
                        break;
                    }

                case OpCode.DIV:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();

                        var ak = GetNumKind(l);
                        var bk = GetNumKind(r);
                        var k = PromoteKind(ak, bk);

                        if (k == NumKind.Int)
                        {
                            int li = Convert.ToInt32(l);
                            int ri = Convert.ToInt32(r);
                            _stack.Push(li / ri);
                        }
                        else if (k == NumKind.Long)
                        {
                            long la = Convert.ToInt64(l);
                            long rb = Convert.ToInt64(r);
                            _stack.Push(la / rb);
                        }
                        else if (k == NumKind.Double)
                        {
                            double ld = Convert.ToDouble(l);
                            double rd = Convert.ToDouble(r);
                            _stack.Push(ld / rd);
                        }
                        else if (k == NumKind.Decimal)
                        {
                            decimal ld = Convert.ToDecimal(l);
                            decimal rd = Convert.ToDecimal(r);
                            _stack.Push(ld / rd);
                        }
                        else
                        {
                            throw new VMException($"Runtime error: cannot DIV {l?.GetType()} and {r?.GetType()}", instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }

                case OpCode.EXPO:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();

                        var res = PerformBinaryNumericOp(l, r,
                            (int a, int b) =>
                            {
                                if (b < 0)
                                    return Math.Pow(a, b);
                                return (int)Math.Pow(a, b);
                            },
                            (long a, long b) =>
                            {
                                if (b < 0)
                                    return Math.Pow(a, b);
                                return (long)Math.Pow(a, b);
                            },
                            (double a, double b) => Math.Pow(a, b),
                            (decimal a, decimal b) => (decimal)Math.Pow((double)a, (double)b),
                            OpCode.EXPO);

                        _stack.Push(res);
                        break;
                    }

                case OpCode.BIT_AND:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        var res = PerformBinaryNumericOp(l, r,
                            (int a, int b) => a & b,
                            (long a, long b) => a & b,
                            (double a, double b) => throw new VMException("BIT_AND not supported on double", instr.Line, instr.Col, instr.OriginFile),
                            (decimal a, decimal b) => throw new VMException("BIT_AND not supported on decimal", instr.Line, instr.Col, instr.OriginFile),
                            OpCode.BIT_AND);
                        _stack.Push(res);
                        break;
                    }

                case OpCode.BIT_OR:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        var res = PerformBinaryNumericOp(l, r,
                            (int a, int b) => a | b,
                            (long a, long b) => a | b,
                            (double a, double b) => throw new VMException("BIT_OR not supported on double", instr.Line, instr.Col, instr.OriginFile),
                            (decimal a, decimal b) => throw new VMException("BIT_OR not supported on decimal", instr.Line, instr.Col, instr.OriginFile),
                            OpCode.BIT_OR);
                        _stack.Push(res);
                        break;
                    }

                case OpCode.BIT_XOR:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        var res = PerformBinaryNumericOp(l, r,
                            (int a, int b) => a ^ b,
                            (long a, long b) => a ^ b,
                            (double a, double b) => throw new VMException("BIT_XOR not supported on double", instr.Line, instr.Col, instr.OriginFile),
                            (decimal a, decimal b) => throw new VMException("BIT_XOR not supported on decimal", instr.Line, instr.Col, instr.OriginFile),
                            OpCode.BIT_XOR);
                        _stack.Push(res);
                        break;
                    }

                case OpCode.SHL:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        var res = PerformBinaryNumericOp(l, r,
                            (int a, int b) => a << b,
                            (long a, long b) => a << (int)b,
                            (double a, double b) => throw new VMException("SHL not supported on double", instr.Line, instr.Col, instr.OriginFile),
                            (decimal a, decimal b) => throw new VMException("SHL not supported on decimal", instr.Line, instr.Col, instr.OriginFile),
                            OpCode.SHL);
                        _stack.Push(res);
                        break;
                    }

                case OpCode.SHR:
                    {
                        var r = _stack.Pop();
                        var l = _stack.Pop();
                        var res = PerformBinaryNumericOp(l, r,
                            (int a, int b) => a >> b,
                            (long a, long b) => a >> (int)b,
                            (double a, double b) => throw new VMException("SHR not supported on double", instr.Line, instr.Col, instr.OriginFile),
                            (decimal a, decimal b) => throw new VMException("SHR not supported on decimal", instr.Line, instr.Col, instr.OriginFile),
                            OpCode.SHR);
                        _stack.Push(res);
                        break;
                    }

                case OpCode.EQ:
                    {
                        var r = _stack.Pop(); var l = _stack.Pop(); _stack.Push(Equals(l, r)); break;
                    }
                case OpCode.NEQ:
                    {
                        var r = _stack.Pop(); var l = _stack.Pop(); _stack.Push(!Equals(l, r)); break;
                    }
                case OpCode.LT:
                    {
                        var r = _stack.Pop(); var l = _stack.Pop();
                        if (IsNumber(l) && IsNumber(r))
                        {
                            var v = PerformBinaryNumericOp(l, r,
                                (int a, int b) => a < b,
                                (long a, long b) => a < b,
                                (double a, double b) => a < b,
                                (decimal a, decimal b) => a < b,
                                OpCode.LT);
                            _stack.Push(v);
                        }
                        else
                        {
                            throw new VMException($"Runtime error: LT on non-numeric types", instr.Line, instr.Col, instr.OriginFile);
                        }
                        break;
                    }
                case OpCode.GT:
                    {
                        var r = _stack.Pop(); var l = _stack.Pop();
                        if (IsNumber(l) && IsNumber(r))
                        {
                            var v = PerformBinaryNumericOp(l, r,
                                (int a, int b) => a > b,
                                (long a, long b) => a > b,
                                (double a, double b) => a > b,
                                (decimal a, decimal b) => a > b,
                                OpCode.GT);
                            _stack.Push(v);
                        }
                        else throw new VMException($"Runtime error: GT on non-numeric types", instr.Line, instr.Col, instr.OriginFile);
                        break;
                    }
                case OpCode.LE:
                    {
                        var r = _stack.Pop(); var l = _stack.Pop();
                        if (IsNumber(l) && IsNumber(r))
                        {
                            var v = PerformBinaryNumericOp(l, r,
                                (int a, int b) => a <= b,
                                (long a, long b) => a <= b,
                                (double a, double b) => a <= b,
                                (decimal a, decimal b) => a <= b,
                                OpCode.LE);
                            _stack.Push(v);
                        }
                        else throw new VMException($"Runtime error: LE on non-numeric types", instr.Line, instr.Col, instr.OriginFile);
                        break;
                    }
                case OpCode.GE:
                    {
                        var r = _stack.Pop(); var l = _stack.Pop();
                        if (IsNumber(l) && IsNumber(r))
                        {
                            var v = PerformBinaryNumericOp(l, r,
                                (int a, int b) => a >= b,
                                (long a, long b) => a >= b,
                                (double a, double b) => a >= b,
                                (decimal a, decimal b) => a >= b,
                                OpCode.GE);
                            _stack.Push(v);
                        }
                        else throw new VMException($"Runtime error: GE on non-numeric types", instr.Line, instr.Col, instr.OriginFile);
                        break;
                    }

                case OpCode.NEG:
                    {
                        var v = _stack.Pop();
                        if (v is int i) _stack.Push(-i);
                        else if (v is long l) _stack.Push(-l);
                        else if (v is float f) _stack.Push(-f);
                        else if (v is double d) _stack.Push(-d);
                        else if (v is decimal m) _stack.Push(-m);
                        else throw new VMException($"NEG only works on numeric types", instr.Line, instr.Col, instr.OriginFile);
                        break;
                    }

                case OpCode.NOT:
                    {
                        var v = _stack.Pop();
                        bool result;
                        if (v is bool b) result = !b;
                        else if (v is int i) result = (i == 0);
                        else if (v == null) result = true;
                        else result = false;
                        _stack.Push(result);
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
                    _ip = instr.Operand is null ? throw new VMException("Null Reference", instr.Line, instr.Col, instr.OriginFile) : (int)instr.Operand;
                    break;

                case OpCode.JMP_IF_FALSE:
                    {
                        var cond = _stack.Pop();
                        if (!ToBool(cond)) _ip = instr.Operand is null ? throw new VMException("Null Reference", instr.Line, instr.Col, instr.OriginFile) : (int)instr.Operand;
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

                            var callEnv = new Env(_scopes[^1]);
                            for (int i = func.Parameters.Count - 1; i >= 0; i--)
                            {
                                var argValue = _stack.Pop();
                                callEnv.Define(func.Parameters[i], argValue);
                            }

                            _scopes.Add(callEnv);
                            _callStack.Push(new CallFrame(_ip, 1));
                            _ip = func.Address;
                        }
                        else
                        {
                            var fn = _stack.Pop();
                            if (fn is not Closure clos)
                                throw new VMException($"Runtime error: CALL target is not a closure", instr.Line, instr.Col, instr.OriginFile);

                            var callEnv = new Env(clos.CapturedEnv);
                            for (int i = clos.Parameters.Count - 1; i >= 0; i--)
                            {
                                var argValue = _stack.Pop();
                                callEnv.Define(clos.Parameters[i], argValue);
                            }

                            _scopes.Add(callEnv);
                            _callStack.Push(new CallFrame(_ip, 1));
                            _ip = clos.Address;
                        }
                        break;
                    }

                case OpCode.CALL_INDIRECT:
                    {
                        Closure? clos = null;
                        var argsList = new List<object>();

                        if (instr.Operand is int explicitArgCount)
                        {
                            for (int i = 0; i < explicitArgCount; i++)
                            {
                                if (_stack.Count == 0)
                                    throw new VMException($"Runtime error: not enough arguments for CALL_INDIRECT (expected {explicitArgCount})", instr.Line, instr.Col, instr.OriginFile);
                                argsList.Add(_stack.Pop());
                            }

                            if (_stack.Count == 0)
                                throw new VMException($"Runtime error: missing callee (closure) for CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile);

                            var maybeCallee = _stack.Pop();
                            if (maybeCallee is not Closure c)
                            {
                                string ct = maybeCallee?.GetType().FullName ?? "null";
                                string cv = maybeCallee?.ToString() ?? "null";
                                throw new VMException($"Runtime error: attempt to call non-function value (expected Closure).\n" +
                                                    $" popped callee type: {ct}\n popped callee value: {cv}", instr.Line, instr.Col, instr.OriginFile);
                            }

                            clos = (Closure)maybeCallee;

                            argsList.Reverse();

                            var callEnv = new Env(clos.CapturedEnv);
                            for (int i = 0; i < argsList.Count && i < clos.Parameters.Count; i++)
                            {
                                callEnv.Define(clos.Parameters[i], argsList[i]);
                            }

                            _scopes.Add(callEnv);
                            _callStack.Push(new CallFrame(_ip, 1));
                            _ip = clos.Address;
                            break;
                        }
                        else
                        {
                            if (_stack.Count == 0)
                                throw new VMException($"Runtime error: missing callee for CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile);

                            var maybeCallee = _stack.Pop();
                            if (maybeCallee is not Closure c)
                            {
                                string ct = maybeCallee?.GetType().FullName ?? "null";
                                string cv = maybeCallee?.ToString() ?? "null";
                                var snap = string.Join(", ", _stack.Reverse().Take(8).Select(o => o == null ? "null" : $"{o.GetType().Name}:{o}"));
                                throw new VMException($"Runtime error: attempt to call non-function value (expected Closure).\n" +
                                                    $" popped callee type: {ct}\n popped callee value: {cv}\n stack snapshot (top..): {snap}", instr.Line, instr.Col, instr.OriginFile);
                            }

                            clos = (Closure)maybeCallee;

                            object? thisObj = null;
                            if (clos.Parameters.Count > 0 && clos.Parameters[0] == "this")
                            {
                                if (_stack.Count == 0)
                                    throw new VMException("Runtime error: missing 'this' for method call.", instr.Line, instr.Col, instr.OriginFile);
                                thisObj = _stack.Pop();
                            }

                            for (int i = clos.Parameters.Count - 1; i >= 0; i--)
                            {
                                if (clos.Parameters[i] == "this") continue;
                                if (_stack.Count == 0)
                                {
                                    break;
                                }
                                argsList.Add(_stack.Pop());
                            }
                            argsList.Reverse();

                            var callEnv = new Env(clos.CapturedEnv);
                            if (clos.Parameters.Count > 0 && clos.Parameters[0] == "this")
                            {
                                callEnv.Define("this", thisObj is null ? throw new VMException("Null Reference",instr.Line, instr.Col, instr.OriginFile) : thisObj);
                            }

                            int argIdx = 0;
                            for (int pi = 0; pi < clos.Parameters.Count; pi++)
                            {
                                var pname = clos.Parameters[pi];
                                if (pname == "this") continue;
                                object? val = (argIdx < argsList.Count) ? argsList[argIdx++] : null;
                                callEnv.Define(pname, val is null ? throw new VMException("Null Reference", instr.Line, instr.Col, instr.OriginFile) : val );
                            }

                            _scopes.Add(callEnv);
                            _callStack.Push(new CallFrame(_ip, 1));
                            _ip = clos.Address;
                            break;
                        }
                    }

                case OpCode.RET:
                    {
                        object? retVal = _stack.Count > 0 ? _stack.Pop() : null;

                        if (_callStack.Count == 0)
                        {
                            _stack.Push(retVal is null ? throw new VMException("Null Reference", instr.Line, instr.Col, instr.OriginFile) : retVal);
                            break;
                        }

                        var frame = _callStack.Pop();

                        for (int i = 0; i < frame.ScopesAdded; i++)
                        {
                            if (_scopes.Count == 0) break;
                            _scopes.RemoveAt(_scopes.Count - 1);
                        }

                        _ip = frame.ReturnIp;
                        _stack.Push(retVal is null ? throw new VMException("Null Reference", instr.Line, instr.Col, instr.OriginFile) : retVal);
                        break;
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
    /// <param name="seen">The seen<see cref="HashSet{object}"/></param>
    /// <param name="escapeNewlines">The escapeNewlines<see cref="bool"/></param>
    private void PrintValue(object v, TextWriter w, int mode = 2, HashSet<object>? seen = null, bool escapeNewlines = true)
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
                entries = entries
                    .Where(kv => kv.Value != null
                                 && !(kv.Value is Closure)
                                 && !(kv.Value is FunctionInfo)
                                 && !(kv.Value is Delegate))
                    .ToList();
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
                w.Write("\"" + (escapeNewlines ? EscapeJsonString(clos.Name ?? "<closure>") : (clos.Name ?? "<closure>")) + "\"");
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
    }

    /// <summary>
    /// The ToBool
    /// </summary>
    /// <param name="v">The v<see cref="object"/></param>
    /// <returns>The <see cref="bool"/></returns>
    private static bool ToBool(object v)
    {
        return v switch
        {
            null => false,
            bool b => b,
            int i => i != 0,
            long l => l != 0L,
            double d => d != 0.0,
            decimal m => m != 0m,
            string s => !string.IsNullOrEmpty(s),
            List<object> arr => arr.Count != 0,
            _ => true
        };
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
    private object PerformBinaryNumericOp(object l, object r,
        Func<int, int, object> intOp,
        Func<long, long, object> longOp,
        Func<double, double, object> doubleOp,
        Func<decimal, decimal, object> decimalOp,
        OpCode code)
    {
        var ak = GetNumKind(l);
        var bk = GetNumKind(r);
        var k = PromoteKind(ak, bk);

        switch (k)
        {
            case NumKind.Int:
                {
                    int li = Convert.ToInt32(l);
                    int ri = Convert.ToInt32(r);
                    return intOp(li, ri);
                }
            case NumKind.Long:
                {
                    long la = Convert.ToInt64(l);
                    long rb = Convert.ToInt64(r);
                    return longOp(la, rb);
                }
            case NumKind.Double:
                {
                    double ld = Convert.ToDouble(l);
                    double rd = Convert.ToDouble(r);
                    return doubleOp(ld, rd);
                }
            case NumKind.Decimal:
                {
                    decimal ld = Convert.ToDecimal(l);
                    decimal rd = Convert.ToDecimal(r);
                    return decimalOp(ld, rd);
                }
            default:
                throw new Exception($"Runtime error: cannot perform {code} on non-numeric types {l?.GetType()} and {r?.GetType()}");
        }
    }
}

/// <summary>
/// Defines the <see cref="VMException" />
/// </summary>
public sealed class VMException(string message, int line, int column, string fileSource) : Exception($"{message}. ( Line : {line}, Column : {column} ) [Source : '{fileSource}']");

