using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;
using CFGS_VM.VMCore.Extensions.Intrinsics.Core;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;
using CFGS_VM.VMCore.Plugin;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        private enum NumKind
        {
            Int32,

            Int64,

            UInt64,

            Double,

            Decimal,

            Maximal,
        }

        public static int DEBUG_BUFFER = 100;

        private static readonly AsyncLocal<VM?> CurrentVmSlot = new();
        private static readonly AsyncLocal<bool?> AllowFileIoOverrideSlot = new();

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

        private sealed class ScriptExceptionSignal(Value payload, Instruction instr) : Exception
        {
            public Value Payload { get; } = payload;

            public Instruction Instruction { get; } = instr;
        }

        private int _minRouteCallDepth;

        /// <summary>
        /// Serialisiert die Bytecode-Ausfuehrung ueber die Wurzel-VM und alle daraus hot-gestarteten
        /// Child-VMs hinweg: zu jedem Zeitpunkt fuehrt hoechstens EIN VM-Kontext Bytecode aus, waehrend
        /// echte IO-Awaits ausserhalb der Sperre liegen. Das entfernt die Data-Race-Klasse auf dem
        /// geteilten, per Referenz genutzten Heap (Arrays, Dictionaries, Instanzfelder, Globals), ohne die
        /// IO-Nebenlaeufigkeit des Hot-Start-Modells aufzugeben. Alle Child-VMs teilen dieselbe Instanz
        /// (zugewiesen im jeweiligen Konstruktor; Child-VMs erhalten die Instanz der Wurzel-VM).
        /// </summary>
        private readonly SemaphoreSlim _executionGate;

        private void PopScopesToBase(int baseDepth)
        {
            while (_scopes.Count > baseDepth)
                _scopes.RemoveAt(_scopes.Count - 1);
        }

        private readonly VmState _state = new();

        /// <summary>
        /// Gets the CurrentThis
        /// </summary>
        private object? CurrentThis => _callStack.Count > 0 ? _callStack.Peek().ThisRef : null;

        /// <summary>
        /// Gets the raw tagged-value operand stack.
        /// </summary>
        private ValueStack _valueStack => _state.Stack;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Value ToRuntimeValue(object? value)
            => value is Value tagged ? tagged : Value.FromObject(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static object? ToHostObject(object? value)
            => value is Value tagged ? tagged.ToObject() : value;

        private void PushRuntimeObject(object? value)
            => _valueStack.Push(ToRuntimeValue(value));

        private object? PopRuntimeObject()
            => _valueStack.Pop().ToObject();

        private object? PeekRuntimeObject()
            => _valueStack.Peek().ToObject();

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

        private IReadOnlyDictionary<int, ScopeDescriptor> _rootDescriptorByAddress
        {
            get => _state.RootScopeDescriptors;
            set => _state.RootScopeDescriptors = value;
        }

        private IReadOnlyDictionary<string, ScopeDescriptor?> _classShapeByName
        {
            get => _state.ClassFieldShapes;
            set => _state.ClassFieldShapes = value;
        }

        // Monomorphic inline cache per member-access (INDEX_GET) site: once a `p.field` read resolves to a
        // plain own-layer field slot, cache (that class's shape identity, slot index). A later read whose
        // receiver has the same shape skips the whole resolution machinery (property/hierarchy/visibility
        // walks) and indexes the slot directly. Shapes are immutable, so the cache never needs invalidation;
        // a shape mismatch simply re-resolves and overwrites the entry. Per-VM, sized to the program.
        private struct MemberInlineCache
        {
            public ScopeDescriptor? Shape;
            public int Slot;
        }

        private MemberInlineCache[] _memberCaches = System.Array.Empty<MemberInlineCache>();

        /// <summary>
        /// Gets the call stack.
        /// </summary>
        private Stack<CallFrame> _callStack => _state.CallStack;

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

        private static bool TryGetLocalVarValue(Env env, string name, out Value value)
            // No lock: all bytecode-path environment access is serialized by VM._executionGate. See Env.
            => env.TryGetLocalValue(name, out value);

        private static void SetLocalVarValue(Env env, string name, Value value)
            => env.DefineValue(name, value);

        /// <summary>
        /// Atomically resolves a variable by walking the current scope chain (then the global scope
        /// as a fallback) and reading its value under the owning environment's lock in a single
        /// step. This replaces the former "find env, then read it" pattern, which acquired the
        /// environment lock twice and could observe a concurrently-removed local between the check
        /// and the read now that async execution is no longer globally serialized.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <param name="owner">The environment that owns the resolved local.</param>
        /// <param name="value">The resolved value.</param>
        /// <returns>True if the variable was found.</returns>
        private bool TryResolveVarOwnerValue(string name, out Env owner, out Value value)
        {
            for (Env? env = _scopes.Count > 0 ? _scopes[^1] : null; env != null; env = env.Parent)
            {
                if (TryGetLocalVarValue(env, name, out value))
                {
                    owner = env;
                    return true;
                }
            }

            if (_scopes.Count > 0 && TryGetLocalVarValue(_scopes[0], name, out value))
            {
                owner = _scopes[0];
                return true;
            }

            owner = null!;
            value = Value.Null;
            return false;
        }

        private static bool IsReceiverName(string s) => s == "this" || s == "type";

        /// <summary>
        /// Gets or sets a value indicating whether AllowFileIO
        /// </summary>
        public static bool AllowFileIO
        {
            get => AllowFileIoOverrideSlot.Value ?? true;
            set => AllowFileIoOverrideSlot.Value = value;
        }

        public static IDisposable PushAllowFileIOScope(bool allowFileIO)
        {
            bool? previous = AllowFileIoOverrideSlot.Value;
            AllowFileIoOverrideSlot.Value = allowFileIO;
            return new AllowFileIOScope(previous);
        }

        private sealed class AllowFileIOScope(bool? previous) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                AllowFileIoOverrideSlot.Value = previous;
            }
        }

        public bool AllowNativeInterop { get; set; } = true;

        public bool AllowPluginLoading { get; set; } = true;

        /// <summary>
        /// When enabled, <see cref="ResolveWorkspacePath"/> rejects any path that resolves outside
        /// <see cref="WorkingDirectory"/> (whether via an absolute path or <c>..</c> traversal),
        /// jailing standard-library file operations to the workspace. Opt-in and disabled by
        /// default so trusted hosts (e.g. the CLI) keep unrestricted absolute-path access.
        /// </summary>
        public bool RestrictFileIoToWorkspace { get; set; } = false;

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

        private enum StepResult
        {
            Next,

            Continue,

            Routed,

            Halt,

            Await
        }

        private StepResult HandleInstruction(ref int _ip, List<Instruction> _insns, Instruction instr)
        {
            switch (instr.Code)
            {
                case OpCode.PUSH_INT:
                    if (instr.Operand is null) _valueStack.Push(Value.FromInt(0));
                    else _valueStack.Push(Value.FromInt((int)instr.Operand));
                    break;

                case OpCode.PUSH_LNG:
                    if (instr.Operand is null) _valueStack.Push(Value.FromLong(0));
                    else _valueStack.Push(Value.FromLong((long)instr.Operand));
                    break;

                case OpCode.PUSH_FLT:
                    if (instr.Operand is null) PushRuntimeObject((float)0);
                    else PushRuntimeObject((float)instr.Operand);
                    break;

                case OpCode.PUSH_DBL:
                    if (instr.Operand is null) _valueStack.Push(Value.FromDouble(0.0));
                    else _valueStack.Push(Value.FromDouble((double)instr.Operand));
                    break;

                case OpCode.PUSH_DEC:
                    if (instr.Operand is null) PushRuntimeObject((decimal)0);
                    else PushRuntimeObject((decimal)instr.Operand);
                    break;

                case OpCode.PUSH_SPC:
                    if (instr.Operand is null) PushRuntimeObject((BigInteger)0);
                    else PushRuntimeObject((BigInteger)instr.Operand);
                    break;

                case OpCode.PUSH_STR:
                    if (instr.Operand is null) PushRuntimeObject(string.Empty);
                    else PushRuntimeObject((string)instr.Operand);
                    break;

                case OpCode.PUSH_CHR:
                    if (instr.Operand is null) _valueStack.Push(Value.FromChar((char)0));
                    else _valueStack.Push(Value.FromChar((char)instr.Operand));
                    break;

                case OpCode.PUSH_BOOL:
                    if (instr.Operand is null) _valueStack.Push(Value.FromBool(false));
                    else _valueStack.Push(Value.FromBool((bool)instr.Operand));
                    break;

                case OpCode.PUSH_NULL:
                    _valueStack.Push(Value.Null);
                    break;

                case OpCode.PUSH_CONST:
                    if (instr.Operand is Interop.NativeAssemblyReference nativeAssemblyReference)
                    {
                        if (!AllowNativeInterop)
                            throw new VMException("Runtime error: native .NET interop is disabled for this VM.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        PushRuntimeObject(Interop.NativeInterop.LoadAssembly(nativeAssemblyReference, NativeAssemblyBaseDirectory));
                    }
                    else
                        PushRuntimeObject(instr.Operand);
                    break;

                case OpCode.PUSH_SCOPE:
                    {
                        _scopes.Add(new Env(_scopes[^1], instr.Operand as ScopeDescriptor));
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
                        // Pre-size the instance's field storage from the class shape (declared fields go into a
                        // Value[] instead of the dictionary). An ambiguous/absent shape falls back to the dict.
                        _classShapeByName.TryGetValue(className, out ScopeDescriptor? shape);
                        ClassInstance obj = new(className, shape);
                        PushRuntimeObject(obj);
                        break;
                    }
                case OpCode.NEW_STATIC:
                    {
                        string className = instr.Operand?.ToString() ?? "<anon>";
                        StaticInstance st = new(className);
                        PushRuntimeObject(st);
                        break;
                    }

                case OpCode.NEW_ARRAY:
                    return HandleNewArrayInstruction(instr);

                case OpCode.SLICE_GET:
                    return HandleSliceGetInstruction(instr);

                case OpCode.SLICE_SET:
                    return HandleSliceSetInstruction(instr);

                case OpCode.INDEX_GET:
                    return HandleIndexGetInstruction(_ip, instr);

                case OpCode.INDEX_SET:
                case OpCode.INDEX_SET_INTERNAL:
                case OpCode.INDEX_INIT:
                    return HandleIndexSetInstruction(_ip, instr);

                case OpCode.NEW_DICT:
                    return HandleNewDictionaryInstruction(instr);

                case OpCode.NEW_NAMESPACE:
                    return HandleNewNamespaceInstruction(instr);

                case OpCode.NEW_DICT_INTERNAL:
                    return HandleNewDictionaryInstruction(instr);

                case OpCode.NEW_ENUM:
                    {
                        if (instr.Operand is null) break;

                        if (_valueStack.Count < 1)
                            throw new VMException("Runtime error: stack underflow (NEW_ENUM needs count)", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        int count = Convert.ToInt32(_valueStack.Pop().ToObject());

                        if (count < 0)
                            throw new VMException($"Runtime error: enum member count cannot be negative ({count})", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        RequireStack(2 * count, instr, "NEW_ENUM");

                        EnumInstance ei = new(instr.Operand.ToString() ?? "null");

                        for (int i = 0; i < count; i++)
                        {
                            object? valueObj = _valueStack.Pop().ToObject();
                            object? keyObj = _valueStack.Pop().ToObject();

                            string key = keyObj?.ToString() ?? "null";
                            int value = Convert.ToInt32(valueObj);

                            ei.Add(key, value);
                        }

                        PushRuntimeObject(ei);
                        break;
                    }

                case OpCode.ROT:
                    {
                        RequireStack(3, instr, "ROT");
                        Value a = _valueStack.Pop();
                        Value b = _valueStack.Pop();
                        Value c = _valueStack.Pop();
                        _valueStack.Push(b);
                        _valueStack.Push(a);
                        _valueStack.Push(c);
                        break;
                    }
                case OpCode.SWAP:
                    {
                        RequireStack(2, instr, "SWAP");

                        Value a = _valueStack.Pop();
                        Value b = _valueStack.Pop();
                        _valueStack.Push(a);
                        _valueStack.Push(b);
                        break;
                    }

                case OpCode.IS_DICT:
                    return HandleIsDictionaryInstruction(instr);

                case OpCode.IS_ARRAY:
                    return HandleIsArrayInstruction(instr);

                case OpCode.DICT_KEYS:
                    return HandleDictionaryKeysInstruction(instr);

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

                        // Hot path first: a normal variable read. The receiver keywords this/type/super/
                        // outer can never be stored as ordinary locals (VAR_DECL/CONST_DECL reject them),
                        // so resolving the environment first is semantically identical to the old order and
                        // takes the four keyword string comparisons off every successful variable load.
                        if (TryResolveVarOwnerValue(name, out Env hotOwner, out Value hotVal))
                        {
                            MarkAsyncHazardForEnvAccess(hotOwner);
                            _valueStack.Push(hotVal);
                            break;
                        }

                        if (name == "this")
                        {
                            object? th = CurrentThis;
                            if (th == null)
                                throw new VMException("Runtime error: 'this' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            PushRuntimeObject(th);
                            break;
                        }

                        if (name == "type")
                        {
                            object? recv = CurrentThis;
                            if (recv is StaticInstance st)
                            {
                                PushRuntimeObject(st);
                                break;
                            }
                            if (recv is ClassInstance inst)
                            {
                                if (TryGetInstanceFieldValue(inst, "__type", out Value typeValue) && typeValue.ToObject() is StaticInstance st2)
                                {
                                    PushRuntimeObject(st2);
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
                                ClassInstance resolutionLayer = inst;
                                if (_callStack.Count > 0 &&
                                    _callStack.Peek().AccessType is StaticInstance accessType &&
                                    TryFindInstanceLayerForType(inst, accessType, out ClassInstance accessLayer))
                                {
                                    resolutionLayer = accessLayer;
                                }

                                if (TryGetInstanceFieldValue(resolutionLayer, "__base", out Value baseValue) && baseValue.ToObject() is ClassInstance baseInst)
                                {
                                    // 'super' keeps the derived instance as 'this'; only method
                                    // resolution starts one layer above the currently executing
                                    // declaring type. The member-access path
                                    // resolves on BaseLayer and rebinds the receiver to Derived.
                                    PushRuntimeObject(new SuperReference(inst, baseInst));
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__base' on instance for 'super'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }

                            if (recv is StaticInstance st)
                            {
                                if (TryGetStaticFieldValue(st, "__base", out Value baseValue) && baseValue.ToObject() is StaticInstance baseType)
                                {
                                    PushRuntimeObject(baseType);
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
                                if (TryGetInstanceFieldValue(inst, "__outer", out Value outerValue) && outerValue.ToObject() is ClassInstance outerInst)
                                {
                                    PushRuntimeObject(outerInst);
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

                        if (TryGetRegisteredNamespaceRoot(name, out object namespaceObject))
                        {
                            _valueStack.Push(Value.FromObject(namespaceObject));
                            break;
                        }

                        if (TryGetBuiltin(name, out BuiltinDescriptor d))
                        {
                            _valueStack.Push(Value.FromObject(new BuiltinCallable(d.Name, d.ArityMin, d.ArityMax)));
                            break;
                        }

                        throw new VMException(UndefinedVariableMessage(name), instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    }

                case OpCode.VAR_DECL:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this" || name == "type" || name == "super" || name == "outer")
                            throw new VMException($"Runtime error: cannot declare '{name}' as a variable", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        RequireStack(1, instr, "VAR_DECL");
                        Value value = _valueStack.Pop();
                        Env scope = _scopes[^1];
                        if (scope.HasLocal(name))
                            throw new VMException($"Runtime error: variable '{name}' already declared in this scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        scope.DefineValue(name, value);
                        break;
                    }

                case OpCode.CONST_DECL:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this" || name == "type" || name == "super" || name == "outer")
                            throw new VMException($"Runtime error: cannot declare '{name}' as a constant", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        RequireStack(1, instr, "CONST_DECL");
                        Value value = _valueStack.Pop();
                        Env scope = _scopes[^1];
                        if (scope.HasLocal(name))
                            throw new VMException($"Runtime error: variable '{name}' already declared in this scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        scope.DefineConstValue(name, value);
                        break;
                    }

                case OpCode.STORE_VAR:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        RequireStack(1, instr, "STORE_VAR");
                        Value value = _valueStack.Pop();

                        // Hot path: assign to an existing binding. The resolver already located the owning
                        // environment, so we set directly (const-checked) instead of re-probing it via
                        // SetLocalIfNotConst, and the receiver-keyword checks move to the cold miss path.
                        if (TryResolveVarOwnerValue(name, out Env env, out _))
                        {
                            MarkAsyncHazardForEnvAccess(env);
                            if (env.IsConstLocal(name))
                                throw new VMException($"Runtime error: cannot assign to constant '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            env.DefineValue(name, value);
                            break;
                        }

                        if (name == "this" || name == "type" || name == "super" || name == "outer")
                            throw new VMException($"Runtime error: cannot assign to '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        throw new VMException(UndefinedAssignmentMessage(name), instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    }

                case OpCode.LOAD_SLOT:
                    {
                        // Operand packs (hops << 16) | slotIndex. Walk `hops` parent scopes from the
                        // innermost, then read the slot directly — no name hash, no per-scope probe. The
                        // compiler only emits this for a reference it proved binds to that slot after its
                        // declaration, so the slot is never the Undefined temporal-dead-zone sentinel.
                        int packed = (int)instr.Operand!;
                        Env env = _scopes[^1];
                        for (int h = packed >> 16; h > 0; h--)
                            env = env.Parent!;
                        _valueStack.Push(env.GetSlotValue(packed & 0xFFFF));
                        break;
                    }

                case OpCode.STORE_SLOT:
                    {
                        RequireStack(1, instr, "STORE_SLOT");
                        Value value = _valueStack.Pop();
                        int packed = (int)instr.Operand!;
                        int idx = packed & 0xFFFF;
                        Env env = _scopes[^1];
                        for (int h = packed >> 16; h > 0; h--)
                            env = env.Parent!;
                        if (env.IsSlotConst(idx))
                            throw new VMException($"Runtime error: cannot assign to constant '{env.SlotName(idx)}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        env.SetSlotValue(idx, value);
                        break;
                    }

                case OpCode.VAR_DECL_SLOT:
                    {
                        RequireStack(1, instr, "VAR_DECL_SLOT");
                        Value value = _valueStack.Pop();
                        int idx = (int)instr.Operand!;
                        Env scope = _scopes[^1];
                        // A non-Undefined slot means the name was already declared in this scope activation.
                        if (!scope.GetSlotValue(idx).IsUndefined)
                            throw new VMException($"Runtime error: variable '{scope.SlotName(idx)}' already declared in this scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        scope.SetSlotValue(idx, value);
                        break;
                    }

                case OpCode.CONST_DECL_SLOT:
                    {
                        RequireStack(1, instr, "CONST_DECL_SLOT");
                        Value value = _valueStack.Pop();
                        int idx = (int)instr.Operand!;
                        Env scope = _scopes[^1];
                        if (!scope.GetSlotValue(idx).IsUndefined)
                            throw new VMException($"Runtime error: variable '{scope.SlotName(idx)}' already declared in this scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        scope.SetSlotValue(idx, value);
                        scope.SetSlotConst(idx, true);
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

                case OpCode.MERGE_LEFT:
                    return HandleMergeInstruction(instr, rightWins: false);

                case OpCode.MERGE_RIGHT:
                    return HandleMergeInstruction(instr, rightWins: true);

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
                        _valueStack.Push(_valueStack.Peek());
                        break;
                    }

                case OpCode.POP:
                    {
                        RequireStack(1, instr, "POP");
                        _valueStack.Pop();
                        break;
                    }

                case OpCode.DESTROY:
                    {
                        RequireStack(1, instr, "DESTROY");
                        object? value = _valueStack.Pop().ToObject();
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
                        Value v = _valueStack.Pop();
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
                        Value v = _valueStack.Pop();
                        if (ToBool(v))
                        {
                            _ip = (int)instr.Operand;
                            return StepResult.Continue;
                        }
                        break;
                    }

                // --- Superinstructions (see SuperInstructionRewriter). At entry _ip already points at the
                // folded tail instruction; the fused handler reuses the existing per-op logic and advances
                // _ip past the tail, so semantics are identical to the unfused run. ---

                case OpCode.LT_JF:
                    HandleLessThanInstruction(instr);
                    return FusedBranchOnFalse(ref _ip, _insns);

                case OpCode.LE_JF:
                    HandleLessThanOrEqualInstruction(instr);
                    return FusedBranchOnFalse(ref _ip, _insns);

                case OpCode.GT_JF:
                    HandleGreaterThanInstruction(instr);
                    return FusedBranchOnFalse(ref _ip, _insns);

                case OpCode.GE_JF:
                    HandleGreaterThanOrEqualInstruction(instr);
                    return FusedBranchOnFalse(ref _ip, _insns);

                case OpCode.EQ_JF:
                    HandleEqualInstruction(instr);
                    return FusedBranchOnFalse(ref _ip, _insns);

                case OpCode.NEQ_JF:
                    HandleNotEqualInstruction(instr);
                    return FusedBranchOnFalse(ref _ip, _insns);

                case OpCode.SLOT_ADD_INT:
                    return FusedSlotIntBinary(ref _ip, _insns, instr, subtract: false);

                case OpCode.SLOT_SUB_INT:
                    return FusedSlotIntBinary(ref _ip, _insns, instr, subtract: true);

                case OpCode.INC_SLOT:
                    return FusedSlotIncrement(ref _ip, _insns, instr, subtract: false);

                case OpCode.DEC_SLOT:
                    return FusedSlotIncrement(ref _ip, _insns, instr, subtract: true);

                case OpCode.LOAD_SLOT2:
                    // Fused LOAD_SLOT, LOAD_SLOT: this instruction carries the first slot, the folded
                    // second LOAD_SLOT (at _ip) carries the second. Push both, then step past the fold.
                    _valueStack.Push(LoadSlot((int)instr.Operand!));
                    _valueStack.Push(LoadSlot((int)_insns[_ip].Operand!));
                    _ip++;
                    return StepResult.Next;

                case OpCode.ADD_STORE_SLOT:
                    return FusedBinaryStoreSlot(ref _ip, _insns, instr, subtract: false);

                case OpCode.SUB_STORE_SLOT:
                    return FusedBinaryStoreSlot(ref _ip, _insns, instr, subtract: true);

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
                        StaticInstance? capturedAccessType = _callStack.Count > 0 ? _callStack.Peek().AccessType : null;
                        PushRuntimeObject(new Closure(
                            funcAddr,
                            funcInfo.Parameters,
                            funcInfo.MinArgs,
                            capturedEnv,
                            funcName ?? throw new VMException("Invalid function-name", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!),
                            funcInfo.RestParameter,
                            funcInfo.isAsync,
                            CurrentThis,
                            capturedAccessType));
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
        /// Completes a fused compare-and-branch (LT_JF/LE_JF/...): the overlaid comparison has already pushed
        /// its bool result and <paramref name="_ip"/> points at the folded JMP_IF_FALSE. Pop the result and
        /// either branch to that JMP_IF_FALSE's target or fall through past it — identical to executing the
        /// comparison and JMP_IF_FALSE separately.
        /// </summary>
        private StepResult FusedBranchOnFalse(ref int _ip, List<Instruction> _insns)
        {
            Value v = _valueStack.Pop();
            if (!ToBool(v))
            {
                _ip = (int)_insns[_ip].Operand!;
                return StepResult.Continue;
            }

            _ip++;
            return StepResult.Next;
        }

        /// <summary>
        /// Executes a fused "local +/- integer constant" run (SLOT_ADD_INT / SLOT_SUB_INT): read the slot
        /// named by the fused LOAD_SLOT operand, materialize the folded PUSH_INT constant, advance
        /// <paramref name="_ip"/> past the PUSH_INT and the arithmetic op, then reuse the full ADD/SUB
        /// coercion path so semantics are unchanged.
        /// </summary>
        private StepResult FusedSlotIntBinary(ref int _ip, List<Instruction> _insns, Instruction instr, bool subtract)
        {
            Value current = LoadSlot((int)instr.Operand!);
            int k = _insns[_ip].Operand is int folded ? folded : 0;
            _ip += 2; // skip the folded PUSH_INT and ADD/SUB

            // Fast path: the common int-counter case computes in registers, skipping the operand round-trip
            // through the stack and the coercion cascade. Anything else (double/long/char/non-numeric) falls
            // back to the full ADD/SUB handler so semantics (incl. int->long overflow promotion) are unchanged.
            if (TryFastIntBinary(current, Value.FromInt(k), subtract, out Value result))
            {
                _valueStack.Push(result);
                return StepResult.Next;
            }

            _valueStack.Push(current);
            _valueStack.Push(Value.FromInt(k));
            return subtract ? HandleSubInstruction(instr) : HandleAddInstruction(instr);
        }

        /// <summary>
        /// Executes a fused in-place counter update (INC_SLOT / DEC_SLOT = `x = x +/- k` on the same slot):
        /// read the slot, combine with the folded PUSH_INT constant, and write the result back (honouring the
        /// const check), then step past PUSH_INT, the arithmetic op and the STORE_SLOT. Uses the int fast path
        /// for the common counter case and falls back to the full ADD/SUB coercion otherwise.
        /// </summary>
        private StepResult FusedSlotIncrement(ref int _ip, List<Instruction> _insns, Instruction instr, bool subtract)
        {
            int packed = (int)instr.Operand!;
            Value current = LoadSlot(packed);
            int k = _insns[_ip].Operand is int folded ? folded : 0;
            _ip += 3; // skip the folded PUSH_INT, ADD/SUB and STORE_SLOT

            if (TryFastIntBinary(current, Value.FromInt(k), subtract, out Value result))
            {
                StoreSlot(packed, result, instr);
                return StepResult.Next;
            }

            _valueStack.Push(current);
            _valueStack.Push(Value.FromInt(k));
            if (subtract)
                HandleSubInstruction(instr);
            else
                HandleAddInstruction(instr);
            StoreSlot(packed, _valueStack.Pop(), instr);
            return StepResult.Next;
        }

        /// <summary>
        /// Executes a fused accumulate-and-store (ADD_STORE_SLOT / SUB_STORE_SLOT): combine the top two stack
        /// operands and store the result into the local named by the folded STORE_SLOT (const check honoured).
        /// Uses the int fast path when both operands are ints, else falls back to the full ADD/SUB handler.
        /// </summary>
        private StepResult FusedBinaryStoreSlot(ref int _ip, List<Instruction> _insns, Instruction instr, bool subtract)
        {
            int slot = (int)_insns[_ip].Operand!; // STORE_SLOT operand
            _ip++; // skip the folded STORE_SLOT

            RequireStack(2, instr, subtract ? "SUB_STORE_SLOT" : "ADD_STORE_SLOT");
            Value right = _valueStack.Pop();
            Value left = _valueStack.Pop();

            if (TryFastIntBinary(left, right, subtract, out Value result))
            {
                StoreSlot(slot, result, instr);
                return StepResult.Next;
            }

            _valueStack.Push(left);
            _valueStack.Push(right);
            if (subtract)
                HandleSubInstruction(instr);
            else
                HandleAddInstruction(instr);
            StoreSlot(slot, _valueStack.Pop(), instr);
            return StepResult.Next;
        }

        /// <summary>
        /// Fast path shared by the fused arithmetic slot ops: when both operands are plain ints, add/subtract
        /// them exactly as <c>AddInt32</c>/<c>SubInt32</c> do (widen to long, then narrow with int-&gt;long
        /// overflow promotion) without materialising them on the operand stack or running the coercion cascade.
        /// Returns false for any non-int operand so the caller can defer to the full handler.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static bool TryFastIntBinary(Value a, Value b, bool subtract, out Value result)
        {
            if (a.Kind == ValueKind.Int && b.Kind == ValueKind.Int)
            {
                long r = subtract ? (long)a.AsInt - b.AsInt : (long)a.AsInt + b.AsInt;
                result = NarrowInt64Value(r);
                return true;
            }

            result = default;
            return false;
        }

        /// <summary>Reads a slot value by its packed (hops &lt;&lt; 16) | index operand — shared by fused slot ops.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private Value LoadSlot(int packed)
        {
            Env env = _scopes[^1];
            for (int h = packed >> 16; h > 0; h--)
                env = env.Parent!;
            return env.GetSlotValue(packed & 0xFFFF);
        }

        /// <summary>Writes a slot value by its packed operand, honouring the const check — shared by fused slot ops.</summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void StoreSlot(int packed, Value value, Instruction instr)
        {
            int idx = packed & 0xFFFF;
            Env env = _scopes[^1];
            for (int h = packed >> 16; h > 0; h--)
                env = env.Parent!;
            if (env.IsSlotConst(idx))
                throw new VMException($"Runtime error: cannot assign to constant '{env.SlotName(idx)}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            env.SetSlotValue(idx, value);
        }

        public void LoadInstructions(List<Instruction> inst)
        {
            // Fuse hot adjacent runs into superinstructions in place (idempotent; keeps instruction indices
            // stable so jump targets / caches below stay valid). Runs here at load time so .cfb is unaffected.
            SuperInstructionRewriter.Rewrite(inst);
            _program = inst;
            _memberCaches = inst.Count > 0 ? new MemberInlineCache[inst.Count] : System.Array.Empty<MemberInlineCache>();
        }

        public void LoadCompiledScript(CompiledScript script)
        {
            LoadFunctions(script.Functions);
            _rootDescriptorByAddress = script.RootScopeDescriptors;
            _classShapeByName = script.ClassFieldShapes;
            LoadInstructions(script.Instructions);
        }

        public void LoadReplCompiledScript(CompiledScript script)
        {
            ResetReplExecutionState();
            RemoveReplFunctionBindings();
            Functions.Clear();
            _functionsByAddress.Clear();
            LoadFunctions(script.Functions);
            _rootDescriptorByAddress = script.RootScopeDescriptors;
            _classShapeByName = script.ClassFieldShapes;
            LoadInstructions(script.Instructions);
        }

        private void ResetReplExecutionState()
        {
            _valueStack.Clear();
            _callStack.Clear();
            _tryHandlers.Clear();
            _awaitTask = null;
            _awaitResumeIp = 0;

            while (_scopes.Count > 1)
                _scopes.RemoveAt(_scopes.Count - 1);
        }

        private void RemoveReplFunctionBindings()
        {
            if (_scopes.Count == 0)
                return;

            Env global = _scopes[0];
            foreach (string functionName in Functions.Keys.ToArray())
                global.RemoveLocal(functionName);
        }

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
        /// Gets or sets the VM-local working directory for standard-library file operations.
        /// </summary>
        public string WorkingDirectory
        {
            get => _workingDirectory;
            set => _workingDirectory = string.IsNullOrWhiteSpace(value)
                ? Environment.CurrentDirectory
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(value));
        }

        private string _workingDirectory = Environment.CurrentDirectory;

        /// <summary>
        /// Resolves a path against the VM-local working directory.
        /// </summary>
        public string ResolveWorkspacePath(string path)
        {
            string expanded = Environment.ExpandEnvironmentVariables((path ?? string.Empty).Trim());
            string resolved = Path.IsPathFullyQualified(expanded)
                ? Path.GetFullPath(expanded)
                : Path.GetFullPath(Path.Combine(WorkingDirectory, expanded));

            if (RestrictFileIoToWorkspace)
                EnsureWithinWorkspace(resolved);

            return resolved;
        }

        /// <summary>
        /// Throws if <paramref name="resolvedFullPath"/> is not contained within
        /// <see cref="WorkingDirectory"/>. Used to enforce <see cref="RestrictFileIoToWorkspace"/>.
        /// </summary>
        private void EnsureWithinWorkspace(string resolvedFullPath)
        {
            string root = Path.GetFullPath(WorkingDirectory);

            // GetRelativePath uses the platform's path comparison (case-insensitive on Windows). A
            // contained path yields a relative subpath; an escape yields a "..\" prefix or, on a
            // different volume, a rooted result.
            string relative = Path.GetRelativePath(root, resolvedFullPath);
            bool escapes = Path.IsPathRooted(relative)
                || relative == ".."
                || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);

            if (escapes)
                throw new VMException(
                    $"Runtime error: path '{resolvedFullPath}' escapes the workspace '{root}' (RestrictFileIoToWorkspace=true)",
                    -1, -1, string.Empty, IsDebugging, DebugStream!);
        }

        /// <summary>
        /// Gets or sets the base directory for relative native .NET assembly references.
        /// </summary>
        public string NativeAssemblyBaseDirectory { get; set; } = string.Empty;

        internal Interop.NativeClosureDelegateCache NativeClosureDelegateCache { get; } = new();

        private void RequireStack(int needed, Instruction instr, string? opName = null)
        {
            if (_valueStack.Count < needed)
                throw new VMException(
                    $"Runtime error: {(opName ?? instr.Code.ToString())} needs {needed} stack values (have {_valueStack.Count})",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VM"/> class.
        /// </summary>
        public VM()
        {
            _debugStream = new MemoryStream();
            _builtins = new BuiltinRegistry();
            _intrinsics = new IntrinsicRegistry();
            _namespaces = new NamespaceRegistry();
            _executionGate = new SemaphoreSlim(1, 1);
            RegisterInterfaceReflectionBuiltins();

            // The non-IO standard library is built into the runtime and always available, so scripts
            // no longer need to import it. File-system APIs remain opt-in via the CFGS.IO plugin.
            CFGS_VM.StandardLibrary.CoreStandardLibrary.Register(_builtins, _intrinsics, _namespaces);
        }

        /// <summary>
        /// Konstruktor fuer hot-gestartete Child-VMs. Er teilt die Binding-Registries UND die Execution-Gate
        /// der Wurzel-VM per Referenz, statt die Standardbibliothek pro async-Aufruf erneut zu registrieren
        /// und die Bindings zu kopieren. Die Registries werden nach dem Setup nicht mehr mutiert und Reads
        /// sind durch die Execution-Gate serialisiert, daher ist das Teilen sicher.
        /// </summary>
        private VM(VM root)
        {
            _debugStream = new MemoryStream();
            _builtins = root._builtins;
            _intrinsics = root._intrinsics;
            _namespaces = root._namespaces;
            _executionGate = root._executionGate;
            // Share the immutable function-address→call-scope descriptor map so that call scopes built on a
            // hot-start async child VM (e.g. a nested async call) are pre-sized identically to the root's —
            // otherwise they would be dynamic and slot opcodes (LOAD_SLOT/STORE_SLOT/VAR_DECL_SLOT) would
            // index the wrong storage.
            _rootDescriptorByAddress = root._rootDescriptorByAddress;
            _classShapeByName = root._classShapeByName;
        }

        private void RegisterInterfaceReflectionBuiltins()
        {
            RegisterCoreBuiltinIfMissing(new BuiltinDescriptor("interfaces", 1, 1, (args, instr) => GetInterfaceList(args[0])));
            RegisterCoreBuiltinIfMissing(new BuiltinDescriptor("implements", 2, 2, (args, instr) => ImplementsRuntimeType(args[0], args[1])));
            RegisterCoreBuiltinIfMissing(new BuiltinDescriptor("interfaceof", 1, 1, (args, instr) => GetInterfaceMetadata(args[0])));
            RegisterCoreBuiltinIfMissing(new BuiltinDescriptor("interface_call", 3, 4, (args, instr) => InterfaceCall(args, instr)!));
        }

        private void RegisterCoreBuiltinIfMissing(BuiltinDescriptor descriptor)
        {
            if (!_builtins.Contains(descriptor.Name))
                _builtins.Register(descriptor);
        }

        private List<object> GetInterfaceList(object? value)
        {
            StaticInstance? type = value switch
            {
                ClassInstance instance when TryGetStaticType(instance, out StaticInstance instanceType) => instanceType,
                StaticInstance staticType => staticType,
                _ => null
            };

            List<object> result = new();
            if (type == null)
                return result;

            HashSet<StaticInstance> seen = new();
            StaticInstance? current = type;
            while (current != null)
            {
                foreach (StaticInstance iface in EnumerateDirectInterfaces(current))
                    AddInterfaceRecursive(iface, seen, result);

                current = TryGetStaticFieldValue(current, "__base", out Value baseValue) && baseValue.ToObject() is StaticInstance baseType
                    ? baseType
                    : null;
            }

            return result;
        }

        private static void AddInterfaceRecursive(StaticInstance iface, HashSet<StaticInstance> seen, List<object> result)
        {
            if (!seen.Add(iface))
                return;

            result.Add(iface);
            foreach (StaticInstance baseIface in EnumerateDirectInterfaces(iface))
                AddInterfaceRecursive(baseIface, seen, result);
        }

        private bool ImplementsRuntimeType(object? value, object? target)
        {
            if (target is not StaticInstance targetType)
                return false;

            if (value is ClassInstance instance && TryGetStaticType(instance, out StaticInstance instanceType))
            {
                return IsInterfaceType(targetType)
                    ? ImplementsInterface(instanceType, targetType) || StructurallyImplementsInterface(instance, instanceType, targetType)
                    : IsSameOrDerivedStaticType(instanceType, targetType);
            }

            if (value is StaticInstance staticType)
            {
                return IsInterfaceType(targetType)
                    ? ImplementsInterface(staticType, targetType)
                    : IsSameOrDerivedStaticType(staticType, targetType);
            }

            return false;
        }

        private Dictionary<string, object> GetInterfaceMetadata(object? value)
        {
            if (value is not StaticInstance iface || !IsInterfaceType(iface))
                return new Dictionary<string, object>();

            Dictionary<string, object> result = new(StringComparer.Ordinal)
            {
                ["name"] = iface.ClassName,
                ["bases"] = EnumerateDirectInterfaces(iface).Cast<object>().ToList(),
                ["methods"] = TryGetStaticFieldValue(iface, "__interface_methods", out Value methods) ? methods.ToObject()! : new List<object>(),
                ["properties"] = TryGetStaticFieldValue(iface, "__interface_properties", out Value properties) ? properties.ToObject()! : new List<object>(),
                ["indexers"] = TryGetStaticFieldValue(iface, "__interface_indexers", out Value indexers) ? indexers.ToObject()! : new List<object>()
            };

            return result;
        }

        private object? InterfaceCall(List<object> args, Instruction instr)
        {
            if (args[0] is not ClassInstance instance)
                throw new VMException("Runtime error: interface_call target must be a class instance.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            if (args[1] is not StaticInstance iface || !IsInterfaceType(iface))
                throw new VMException("Runtime error: interface_call second argument must be an interface type.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            if (!ImplementsRuntimeType(instance, iface))
            {
                throw new VMException(
                    $"Runtime error: interface_call target does not implement interface '{iface.ClassName}'.",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    IsDebugging,
                    DebugStream!);
            }

            string methodName = args[2]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(methodName))
                throw new VMException("Runtime error: interface_call method name cannot be empty.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            List<object> callArgs = [];
            if (args.Count > 3)
            {
                if (args[3] is not List<object> providedArgs)
                    throw new VMException("Runtime error: interface_call fourth argument must be an array of arguments.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                callArgs.AddRange(providedArgs);
            }

            Closure closure;
            if (!TryResolveExplicitInterfaceMethod(instance, iface, methodName, out StaticInstance? ownerType, out closure))
            {
                if (!InterfaceDeclaresInstanceMethod(iface, methodName))
                {
                    throw new VMException($"Runtime error: interface_call could not find method '{methodName}' for interface '{iface.ClassName}'.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                }

                if (!TryResolveInstanceMemberValueInHierarchy(instance, methodName, out _, out ownerType, out Value memberValue) ||
                    memberValue.Reference is not Closure fallbackClosure)
                {
                    throw new VMException($"Runtime error: interface_call could not find method '{methodName}' for interface '{iface.ClassName}'.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                }

                closure = fallbackClosure;
            }

            return InvokeClosureSync(closure, callArgs, instr, instance, ownerType);
        }

        private bool TryResolveExplicitInterfaceMethod(
            ClassInstance instance,
            StaticInstance iface,
            string methodName,
            out StaticInstance? ownerType,
            out Closure closure)
        {
            foreach (StaticInstance contractIface in EnumerateInterfaceSelfAndBases(iface))
            {
                foreach (string interfaceName in EnumerateRuntimeTypeNames(contractIface))
                {
                    string explicitName = $"{interfaceName}.{methodName}";
                    if (TryResolveInstanceMemberValueInHierarchy(instance, explicitName, out _, out ownerType, out Value memberValue) &&
                        memberValue.Reference is Closure resolvedClosure)
                    {
                        closure = resolvedClosure;
                        return true;
                    }
                }
            }

            ownerType = null;
            closure = null!;
            return false;
        }
    }
}
