using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;
using CFGS_VM.VMCore.Extensions.Intrinsics.Core;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;
using CFGS_VM.VMCore.Plugin;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CFGS_VM.VMCore
{
    /// <summary>
    /// Defines the <see cref="VM" />
    /// </summary>
    public partial class VM
    {
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
            Decimal,

            /// <summary>
            /// Defines the Maximal
            /// </summary>
            Maximal,
        }

        /// <summary>
        /// Defines the DEBUG_BUFFER
        /// </summary>
        public static int DEBUG_BUFFER = 100;

        private static readonly AsyncLocal<VM?> CurrentVmSlot = new();

        public static VM? CurrentVm
        {
            get => CurrentVmSlot.Value;
            private set => CurrentVmSlot.Value = value;
        }

        /// <summary>
        /// Gets the try handler stack.
        /// </summary>
        private Stack<TryHandler> _tryHandlers => _state.TryHandlers;

        private record CallFrame(int ReturnIp, int BaseScopeDepth, object? ThisRef, StaticInstance? AccessType, bool IsAsync);

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
        /// Defines the _state
        /// </summary>
        private readonly VmState _state = new();

        /// <summary>
        /// Gets the CurrentThis
        /// </summary>
        private object? CurrentThis => _callStack.Count > 0 ? _callStack.Peek().ThisRef : null;

        /// <summary>
        /// Gets the runtime stack.
        /// </summary>
        private Stack<object> _stack => _state.Stack;

        /// <summary>
        /// Gets the runtime scopes.
        /// </summary>
        private List<Env> _scopes => _state.Scopes;

        /// <summary>
        /// Gets the Functions
        /// </summary>
        public Dictionary<string, FunctionInfo> Functions => _state.Functions;

        /// <summary>
        /// Gets the function lookup by address.
        /// </summary>
        private Dictionary<int, FunctionInfo> _functionsByAddress => _state.FunctionsByAddress;

        /// <summary>
        /// Gets the call stack.
        /// </summary>
        private Stack<CallFrame> _callStack => _state.CallStack;

        /// <summary>
        /// The LoadFunctions
        /// </summary>
        /// <param name="funcs">The funcs<see cref="Dictionary{string, FunctionInfo}"/></param>
        public void LoadFunctions(Dictionary<string, FunctionInfo> funcs)
        {
            foreach (KeyValuePair<string, FunctionInfo> kv in funcs)
            {
                if (Functions.ContainsKey(kv.Key))
                    throw new VMException(
                        $"Runtime error: multiple declarations for function '{kv.Key}'",
                        -1, -1, string.Empty, IsDebugging, DebugStream!);
                Functions[kv.Key] = kv.Value;
                _functionsByAddress[kv.Value.Address] = kv.Value;
            }
        }

        /// <summary>
        /// The HasLocalVar
        /// </summary>
        /// <param name="env">The env<see cref="Env"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool HasLocalVar(Env env, string name)
        {
            lock (env.SyncRoot)
                return env.Vars.ContainsKey(name);
        }

        /// <summary>
        /// The TryGetLocalVar
        /// </summary>
        /// <param name="env">The env<see cref="Env"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetLocalVar(Env env, string name, out object? value)
        {
            lock (env.SyncRoot)
            {
                if (env.Vars.TryGetValue(name, out object? local))
                {
                    value = local;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// The GetLocalVar
        /// </summary>
        /// <param name="env">The env<see cref="Env"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="object?"/></returns>
        private static object? GetLocalVar(Env env, string name)
        {
            lock (env.SyncRoot)
                return env.Vars[name];
        }

        /// <summary>
        /// The SetLocalVar
        /// </summary>
        /// <param name="env">The env<see cref="Env"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object?"/></param>
        private static void SetLocalVar(Env env, string name, object? value)
        {
            lock (env.SyncRoot)
                env.Vars[name] = value!;
        }

        /// <summary>
        /// The FindEnvWithLocal
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="Env?"/></returns>
        private Env? FindEnvWithLocal(string name)
        {
            for (Env? env = _scopes.Count > 0 ? _scopes[^1] : null; env != null; env = env.Parent)
            {
                if (HasLocalVar(env, name))
                    return env;
            }

            if (_scopes.Count > 0)
            {
                Env root = _scopes[0];
                if (root != null && HasLocalVar(root, name))
                    return root;
            }

            return null;
        }

        /// <summary>
        /// The IsReceiverName
        /// </summary>
        /// <param name="s">The s<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsReceiverName(string s) => s == "this" || s == "type";

        /// <summary>
        /// Gets or sets a value indicating whether AllowFileIO
        /// </summary>
        public static bool AllowFileIO { get; set; } = true;

        /// <summary>
        private bool _isDebugging;

        /// <summary>
        /// Gets or sets the loaded program.
        /// </summary>
        private List<Instruction>? _program
        {
            get => _state.Program;
            set => _state.Program = value;
        }

        /// <summary>
        /// Gets or sets the pending await task.
        /// </summary>
        private Task<object?>? _awaitTask
        {
            get => _state.AwaitTask;
            set => _state.AwaitTask = value;
        }

        /// <summary>
        /// Gets or sets the await resume ip.
        /// </summary>
        private int _awaitResumeIp
        {
            get => _state.AwaitResumeIp;
            set => _state.AwaitResumeIp = value;
        }

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
            Halt,

            /// <summary>
            /// Defines the Await
            /// </summary>
            Await
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

                case OpCode.PUSH_SPC:
                    if (instr.Operand is null) _stack.Push((BigInteger)0);
                    else _stack.Push((BigInteger)instr.Operand);
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
                    _stack.Push(null!);
                    break;

                case OpCode.PUSH_SCOPE:
                    {
                        _scopes.Add(new Env(_scopes[^1]));
                        break;
                    }

                case OpCode.POP_SCOPE:
                    {
                        if (_scopes.Count <= 1)
                            throw new VMException("Runtime error: cannot pop global scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        _scopes.RemoveAt(_scopes.Count - 1);
                        break;
                    }
                case OpCode.LEAVE:
                    {
                        if (instr.Operand is not object[] arr || arr.Length < 2)
                            throw new VMException("Runtime error: LEAVE requires [targetIp, scopesToPop]", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        int targetIp = Convert.ToInt32(arr[0]);
                        int scopesToPop = Convert.ToInt32(arr[1]);

                        TryHandler? nextFinally = null;
                        foreach (TryHandler th in _tryHandlers)
                        {
                            if (th.FinallyAddr >= 0 && !th.InFinally && th.CallDepth == _callStack.Count)
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
                            nextFinally.CatchAddr = -1;

                            int nip = nextFinally.FinallyAddr;
                            nextFinally.FinallyAddr = -1;
                            _ip = nip;
                            return StepResult.Routed;
                        }

                        for (int i = 0; i < scopesToPop; i++)
                        {
                            if (_scopes.Count <= 1)
                                throw new VMException("Runtime error: cannot pop global scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
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
                    return HandleNewArrayInstruction(instr);

                case OpCode.SLICE_GET:
                    return HandleSliceGetInstruction(instr);

                case OpCode.SLICE_SET:
                    return HandleSliceSetInstruction(instr);

                case OpCode.INDEX_GET:
                    return HandleIndexGetInstruction(instr);

                case OpCode.INDEX_SET:
                case OpCode.INDEX_SET_INTERNAL:
                    return HandleIndexSetInstruction(instr);

                case OpCode.NEW_DICT:
                    return HandleNewDictionaryInstruction(instr);

                case OpCode.NEW_ENUM:
                    {
                        if (instr.Operand is null) break;

                        if (_stack.Count < 1)
                            throw new VMException("Runtime error: stack underflow (NEW_ENUM needs count)", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        int count = Convert.ToInt32(_stack.Pop());

                        if (count < 0)
                            throw new VMException($"Runtime error: enum member count cannot be negative ({count})", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

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
                    return HandleIsDictionaryInstruction(instr);

                case OpCode.IS_ARRAY:
                    return HandleIsArrayInstruction(instr);

                case OpCode.LEN:
                    return HandleLengthInstruction(instr);

                case OpCode.HAS_KEY:
                    return HandleHasKeyInstruction(instr);

                case OpCode.ARRAY_PUSH:
                    return HandleArrayPushInstruction(instr);

                case OpCode.ARRAY_DELETE_SLICE:
                    return HandleArrayDeleteSliceInstruction(instr);

                case OpCode.ARRAY_DELETE_SLICE_ALL:
                    return HandleArrayDeleteSliceAllInstruction(instr);

                case OpCode.ARRAY_DELETE_ELEM:
                    return HandleArrayDeleteElementInstruction(instr);

                case OpCode.ARRAY_DELETE_ALL:
                    return HandleArrayDeleteAllInstruction(instr);

                case OpCode.ARRAY_CLEAR:
                    return HandleArrayClearInstruction(instr);

                case OpCode.ARRAY_DELETE_ELEM_ALL:
                    return HandleArrayDeleteElementAllInstruction(instr);

                case OpCode.LOAD_VAR:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this")
                        {
                            object? th = CurrentThis;
                            if (th == null)
                                throw new VMException("Runtime error: 'this' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
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
                                if (TryGetInstanceField(inst, "__type", out object? tObj) && tObj is StaticInstance st2)
                                {
                                    _stack.Push(st2);
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__type' on instance for 'type'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }
                            throw new VMException("Runtime error: 'type' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

                        if (name == "super")
                        {
                            object? recv = CurrentThis;

                            if (recv is ClassInstance inst)
                            {
                                if (TryGetInstanceField(inst, "__base", out object? bObj) && bObj is ClassInstance baseInst)
                                {
                                    _stack.Push(baseInst);
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__base' on instance for 'super'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }

                            if (recv is StaticInstance st)
                            {
                                if (TryGetStaticField(st, "__base", out object? sbObj) && sbObj is StaticInstance baseType)
                                {
                                    _stack.Push(baseType);
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__base' on static type for 'super'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }

                            throw new VMException("Runtime error: 'super' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        if (name == "outer")
                        {
                            object? recv = CurrentThis;

                            if (recv is ClassInstance inst)
                            {
                                if (TryGetInstanceField(inst, "__outer", out object? oObj) && oObj is ClassInstance outerInst)
                                {
                                    _stack.Push(outerInst);
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__outer' on instance for 'outer'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }

                            if (recv is StaticInstance)
                            {
                                throw new VMException("Runtime error: 'outer' not available in static context", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }

                            throw new VMException("Runtime error: 'outer' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

                        Env? owner = FindEnvWithLocal(name);
                        if (owner != null && TryGetLocalVar(owner, name, out object? val))
                        {
                            MarkAsyncHazardForEnvAccess(owner);
                            _stack.Push(val!);
                            break;
                        }

                        if (TryGetBuiltin(name, out BuiltinDescriptor d))
                        {
                            _stack.Push(new BuiltinCallable(d.Name, d.ArityMin, d.ArityMax));
                            break;
                        }

                        throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    }

                case OpCode.VAR_DECL:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this" || name == "type" || name == "super" || name == "outer")
                            throw new VMException($"Runtime error: cannot declare '{name}' as a variable", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        RequireStack(1, instr, "VAR_DECL");
                        object value = _stack.Pop();
                        Env scope = _scopes[^1];
                        if (scope.HasLocal(name))
                            throw new VMException($"Runtime error: variable '{name}' already declared in this scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        scope.Define(name, value);
                        break;
                    }

                case OpCode.CONST_DECL:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this" || name == "type" || name == "super" || name == "outer")
                            throw new VMException($"Runtime error: cannot declare '{name}' as a constant", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        RequireStack(1, instr, "CONST_DECL");
                        object value = _stack.Pop();
                        Env scope = _scopes[^1];
                        if (scope.HasLocal(name))
                            throw new VMException($"Runtime error: variable '{name}' already declared in this scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        scope.DefineConst(name, value);
                        break;
                    }

                case OpCode.STORE_VAR:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this" || name == "type" || name == "super" || name == "outer")
                            throw new VMException($"Runtime error: cannot assign to '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        RequireStack(1, instr, "STORE_VAR");
                        object value = _stack.Pop();
                        Env? env = FindEnvWithLocal(name);
                        if (env == null)
                            throw new VMException($"Runtime error: assignment to undeclared variable '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        MarkAsyncHazardForEnvAccess(env);
                        if (env.IsConstLocal(name))
                            throw new VMException($"Runtime error: cannot assign to constant '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        SetLocalVar(env, name, value);
                        break;
                    }

                case OpCode.ADD:
                    return HandleAddInstruction(instr);

                case OpCode.SUB:
                    return HandleSubInstruction(instr);

                case OpCode.MUL:
                    return HandleMulInstruction(instr);

                case OpCode.MOD:
                    return HandleModInstruction(instr);

                case OpCode.DIV:
                    return HandleDivInstruction(instr);

                case OpCode.EXPO:
                    return HandleExpoInstruction(instr);

                case OpCode.BIT_AND:
                    return HandleBitAndInstruction(instr);

                case OpCode.BIT_OR:
                    return HandleBitOrInstruction(instr);

                case OpCode.BIT_XOR:
                    return HandleBitXorInstruction(instr);

                case OpCode.SHL:
                    return HandleShiftLeftInstruction(instr);

                case OpCode.SHR:
                    return HandleShiftRightInstruction(instr);
                case OpCode.EQ:
                    return HandleEqualInstruction(instr);

                case OpCode.NEQ:
                    return HandleNotEqualInstruction(instr);

                case OpCode.IS_TYPE:
                    return HandleIsTypeInstruction(instr);

                case OpCode.LT:
                    return HandleLessThanInstruction(instr);

                case OpCode.GT:
                    return HandleGreaterThanInstruction(instr);

                case OpCode.LE:
                    return HandleLessThanOrEqualInstruction(instr);

                case OpCode.GE:
                    return HandleGreaterThanOrEqualInstruction(instr);

                case OpCode.NOT:
                    return HandleNotInstruction(instr);

                case OpCode.NEG:
                    return HandleNegateInstruction(instr);

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

                case OpCode.DESTROY:
                    {
                        RequireStack(1, instr, "DESTROY");
                        object? value = _stack.Pop();
                        DestroyValue(value, instr, recursive: false);
                        break;
                    }

                case OpCode.AND:
                    return HandleAndInstruction(instr);

                case OpCode.OR:
                    return HandleOrInstruction(instr);

                case OpCode.LABEL:
                    return StepResult.Continue;

                case OpCode.JMP:
                    {
                        if (instr.Operand is null)
                            throw new VMException("Runtime error: JMP missing target", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        _ip = (int)instr.Operand;
                        return StepResult.Continue;
                    }

                case OpCode.JMP_IF_FALSE:
                    {
                        if (instr.Operand is null)
                            throw new VMException("Runtime error: JMP_IF_FALSE missing target", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
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
                            throw new VMException("Runtime error: JMP_IF_TRUE missing target", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        RequireStack(1, instr, "JMP_IF_TRUE");
                        object v = _stack.Pop();
                        if (ToBool(v))
                        {
                            _ip = (int)instr.Operand;
                            return StepResult.Continue;
                        }
                        break;
                    }

                case OpCode.AWAIT:
                    return HandleAwaitInstruction(ref _ip, _insns, instr);

                case OpCode.YIELD:
                    return HandleYieldInstruction(ref _ip);

                case OpCode.HALT:
                    return StepResult.Halt;

                case OpCode.PUSH_CLOSURE:
                    {
                        if (instr.Operand == null)
                            throw new VMException($"Runtime error: PUSH_CLOSURE without operand", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

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
                                throw new VMException($"Runtime error: Invalid PUSH_CLOSURE operand type {instr.Operand.GetType().Name}", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

                        _functionsByAddress.TryGetValue(funcAddr, out FunctionInfo? funcInfo);
                        if (funcInfo == null)
                            throw new VMException($"Runtime error: PUSH_CLOSURE unknown function address {funcAddr}", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        Env capturedEnv = _scopes[^1];
                        _stack.Push(new Closure(
                            funcAddr,
                            funcInfo.Parameters,
                            funcInfo.MinArgs,
                            capturedEnv,
                            funcName ?? throw new VMException("Invalid function-name", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!),
                            funcInfo.RestParameter,
                            funcInfo.isAsync));
                        break;
                    }

                case OpCode.CALL:
                    return HandleCallInstruction(ref _ip, instr);

                case OpCode.MAKE_NAMED_ARG:
                    return HandleMakeNamedArgInstruction(instr);

                case OpCode.MAKE_SPREAD_ARG:
                    return HandleMakeSpreadArgInstruction(instr);

                case OpCode.CALL_INDIRECT:
                    return HandleCallIndirectInstruction(ref _ip, instr);

                case OpCode.RET:
                    return HandleReturnInstruction(ref _ip, instr);

                case OpCode.TRY_PUSH:
                    return HandleTryPushInstruction(instr);

                case OpCode.TRY_POP:
                    return HandleTryPopInstruction(ref _ip, _insns, instr);

                case OpCode.THROW:
                    return HandleThrowInstruction(ref _ip, _insns, instr);

                default:
                    throw new VMException($"Runtime error: unknown opcode {instr.Code}", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
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
        private MemoryStream _debugStream;

        /// <summary>
        /// Gets the contextual debug flag for the currently running VM.
        /// </summary>
        public static bool IsDebugging => CurrentVm?._isDebugging ?? false;

        /// <summary>
        /// Gets the contextual debug stream for the currently running VM.
        /// </summary>
        public static MemoryStream? DebugStream => CurrentVm?._debugStream;

        /// <summary>
        /// Gets a value indicating whether this VM instance is currently debugging.
        /// </summary>
        public bool DebugEnabled => _isDebugging;

        /// <summary>
        /// Gets the debug stream for this VM instance.
        /// </summary>
        public MemoryStream DebugOutput => _debugStream;

        /// <summary>
        /// Gets or sets the entry script path for the current VM run.
        /// Importing modules does not change this value.
        /// </summary>
        public string EntryScriptPath { get; set; } = string.Empty;

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
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VM"/> class.
        /// </summary>
        public VM()
        {
            _debugStream = new MemoryStream();
        }
    }
}
