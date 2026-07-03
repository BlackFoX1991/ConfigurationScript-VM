using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;
using CFGS_VM.VMCore.Extensions.Intrinsics.Core;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;
using CFGS_VM.VMCore.Interop;
using CFGS_VM.VMCore.Plugin;
using System.Collections;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        /// <summary>
        /// Upper bound on nested call frames. CFGS function calls are iterative (the call stack
        /// lives on the heap), so runaway recursion would otherwise grow until OutOfMemory; this
        /// guard turns it into a clean runtime error well before that. Set high enough that
        /// legitimate deep recursion is unaffected.
        /// </summary>
        private const int MaxCallDepth = 100_000;

        /// <summary>
        /// Throws a runtime error when the call stack has reached <see cref="MaxCallDepth"/>.
        /// </summary>
        private void EnsureCallDepthWithinLimit(Instruction instr)
        {
            if (_callStack.Count >= MaxCallDepth)
                throw new VMException(
                    $"Runtime error: maximum call depth ({MaxCallDepth}) exceeded (possible infinite recursion)",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private static List<Value> ToRuntimeArgs(IEnumerable<object?> args)
            => args.Select(static arg => ToRuntimeValue(arg)).ToList();

        private static List<object> ToObjectArgs(IEnumerable<Value> args)
            => args.Select(static arg => arg.ToObject()!).ToList();

        /// <summary>
        /// Invokes a named top-level CFGS function after the module body has finished loading.
        /// </summary>
        public async Task<object?> InvokeGlobalFunctionAsync(string name, List<object>? args = null, string? originFile = null, CancellationToken ct = default)
            => await InvokeGlobalFunctionAsyncValue(name, args is null ? null : ToRuntimeArgs(args), originFile, ct).ConfigureAwait(false);

        /// <summary>
        /// Invokes a named top-level CFGS function after the module body has finished loading using tagged runtime arguments.
        /// </summary>
        internal async Task<object?> InvokeGlobalFunctionAsyncValue(string name, List<Value>? args = null, string? originFile = null, CancellationToken ct = default)
        {
            if (!Functions.TryGetValue(name, out FunctionInfo? func))
            {
                throw new VMException(
                    $"Runtime error: undefined function '{name}'",
                    -1, -1, originFile ?? string.Empty, IsDebugging, DebugStream!);
            }

            if (_scopes.Count == 0)
                throw new InvalidOperationException("Cannot invoke global function: no global scope available.");

            args ??= [];

            Closure closure = new(
                func.Address,
                func.Parameters,
                func.MinArgs,
                _scopes[0],
                name,
                func.RestParameter,
                func.isAsync);

            Instruction syntheticCall = new(OpCode.CALL, name, -1, -1, originFile ?? string.Empty);
            if (TryStartHotAsyncCall(closure, args, receiver: null, accessType: null, syntheticCall, out Task<object?> startedTask))
                return await startedTask.ConfigureAwait(false);

            return InvokeClosureSync(closure, args, syntheticCall, ct: ct);
        }

        /// <summary>
        /// Invokes a CFGS closure synchronously from within an intrinsic/builtin.
        /// </summary>
        public object? InvokeClosureSync(Closure closure, List<object> args, Instruction instr, CancellationToken ct = default)
            => InvokeClosureSync(closure, ToRuntimeArgs(args), instr, receiver: null, accessType: null, ct);

        /// <summary>
        /// Invokes a CFGS closure synchronously with an optional implicit receiver.
        /// </summary>
        public object? InvokeClosureSync(Closure closure, List<object> args, Instruction instr, object? receiver, StaticInstance? accessType, CancellationToken ct = default)
            => InvokeClosureSync(closure, ToRuntimeArgs(args), instr, receiver, accessType, ct);

        /// <summary>
        /// Invokes a CFGS closure synchronously from within the runtime using tagged values directly.
        /// </summary>
        public object? InvokeClosureSync(Closure closure, List<Value> args, Instruction instr, CancellationToken ct = default)
            => InvokeClosureSyncValue(closure, args, instr, receiver: null, accessType: null, ct).ToObject();

        /// <summary>
        /// Invokes a CFGS closure synchronously with tagged arguments and an optional implicit receiver.
        /// </summary>
        public object? InvokeClosureSync(Closure closure, List<Value> args, Instruction instr, object? receiver, StaticInstance? accessType, CancellationToken ct = default)
            => InvokeClosureSyncValue(closure, args, instr, receiver, accessType, ct).ToObject();

        /// <summary>
        /// Invokes a CFGS closure synchronously and returns the tagged runtime result.
        /// </summary>
        public Value InvokeClosureSyncValue(Closure closure, List<Value> args, Instruction instr, CancellationToken ct = default)
            => InvokeClosureSyncValue(closure, args, instr, receiver: null, accessType: null, ct);

        /// <summary>
        /// Invokes a CFGS closure synchronously with tagged arguments and returns the tagged runtime result.
        /// </summary>
        public Value InvokeClosureSyncValue(Closure closure, List<Value> args, Instruction instr, object? receiver, StaticInstance? accessType, CancellationToken ct = default)
        {
            if (_program is null || _program.Count == 0)
                throw new VMException("Cannot invoke closure: no program loaded.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            // C#-style lexical capture: when no explicit receiver is supplied (e.g. a lambda
            // passed as a map/filter/sort callback), fall back to the receiver captured at the
            // closure's definition site so this/type/super/outer still resolve.
            if (receiver == null && closure.CapturedThis != null)
            {
                receiver = closure.CapturedThis;
                accessType ??= closure.CapturedAccessType;
            }

            EnsureCallDepthWithinLimit(instr);
            Env callEnv = BuildCallEnv(closure, args, instr);
            int callerDepth = _scopes.Count;
            _scopes.Add(callEnv);
            _callStack.Push(new CallFrame(_program.Count, callerDepth, receiver, accessType, false));

            RunStopReason reason = RunUntilAwaitOrHalt(false, closure.Address, _callStack.Count, ct);

            if (reason == RunStopReason.AwaitPending)
                throw new VMException("Runtime error: callback function must not use await.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            return _valueStack.Count > 0 ? _valueStack.Pop() : Value.Null;
        }

        private static object? WrapReturnForFrame(CallFrame frame, object? retVal)
        {
            if (!frame.IsAsync)
                return retVal;

            return FinalizeAsyncResult(retVal);
        }

        private VM CreateHotStartChildVm()
        {
            if (_program is null || _program.Count == 0)
                throw new InvalidOperationException("Hot-start async call requires loaded program instructions.");

            // Der Child-Konstruktor teilt Binding-Registries und Execution-Gate der Wurzel-VM per Referenz,
            // sodass pro async-Aufruf weder die Standardbibliothek neu registriert noch die Bindings kopiert
            // werden. Die Gate serialisiert die Bytecode-Segmente ueber den gesamten hot-gestarteten Baum.
            VM child = new(this)
            {
                AllowNativeInterop = AllowNativeInterop,
                AllowPluginLoading = AllowPluginLoading,
                EntryScriptPath = EntryScriptPath,
                WorkingDirectory = WorkingDirectory,
                NativeAssemblyBaseDirectory = NativeAssemblyBaseDirectory,
                RestrictFileIoToWorkspace = RestrictFileIoToWorkspace
            };
            child.LoadInstructions(_program);
            child.LoadFunctions(Functions);

            return child;
        }

        private void PrepareHotStartEntry(Env callEnv, object? receiver, StaticInstance? accessType)
        {
            if (_program is null || _program.Count == 0)
                throw new InvalidOperationException("Hot-start async entry requires loaded program instructions.");

            _valueStack.Clear();
            _tryHandlers.Clear();
            _callStack.Clear();
            _scopes.Clear();
            _scopes.Add(callEnv);
            _callStack.Push(new CallFrame(_program.Count, _scopes.Count, receiver, accessType, false));
        }

        private object? ConsumeHotStartResult()
        {
            if (_valueStack.Count == 0)
                return null;
            return _valueStack.Pop().ToObject();
        }

        private bool TryStartHotAsyncCall(
            Closure f,
            List<Value> args,
            object? receiver,
            StaticInstance? accessType,
            Instruction instr,
            out Task<object?> startedTask)
        {
            if (!f.IsAsync)
            {
                startedTask = null!;
                return false;
            }

            Env callEnv = BuildCallEnv(f, args, instr);
            VM child = CreateHotStartChildVm();
            child.PrepareHotStartEntry(callEnv, receiver, accessType);
            startedTask = child.RunHotStartEntryAsync(f.Address);
            return true;
        }

        private List<Value> ExpandSpreadArguments(List<Value> rawArgs, Instruction instr)
        {
            List<Value> expanded = new();
            foreach (Value arg in rawArgs)
            {
                if (arg.Kind != ValueKind.Ref || arg.Reference is not SpreadArgument spread)
                {
                    expanded.Add(arg);
                    continue;
                }

                if (spread.Value.IsNull)
                    throw new VMException("Runtime error: cannot spread null argument", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                if (spread.Value.Kind != ValueKind.Ref || spread.Value.Reference is not IList list)
                    throw new VMException(
                        $"Runtime error: spread argument must be an array/list (got {GetValueTypeName(spread.Value)})",
                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                foreach (object? item in list)
                    expanded.Add(ToRuntimeValue(item));
            }

            return expanded;
        }

        private static string GetValueTypeName(Value value)
            => value.Kind switch
            {
                ValueKind.Null => "null",
                ValueKind.Bool => nameof(Boolean),
                ValueKind.Int => nameof(Int32),
                ValueKind.Long => nameof(Int64),
                ValueKind.Double => nameof(Double),
                ValueKind.Char => nameof(Char),
                _ => value.Reference?.GetType().Name ?? "null"
            };

        private object GetCallableTarget(Value value, Instruction instr)
        {
            if (value.IsNull)
                throw new VMException("Runtime error: missing callee for CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            if (value.Kind != ValueKind.Ref || value.Reference is null)
                throw new VMException($"Runtime error: attempt to call non-function value ({instr.Code})", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            return value.Reference;
        }

        private static Task<object?> RunOffloadedInvocation(Func<object?> invoke)
        {
            return Task.Run<object?>(async () =>
            {
                object? raw = invoke();
                if (AwaitableAdapter.TryGetTask(raw, out Task<object?>? awaited))
                    return await awaited.ConfigureAwait(false);
                return raw;
            });
        }

        private static object? InvokeBuiltinForCall(BuiltinDescriptor desc, List<object> args, Instruction instr)
        {
            if (!desc.Options.OffloadBlockingWork)
                return desc.Invoke(args, instr);

            List<object> argsCopy = new(args);
            return RunOffloadedInvocation(() => desc.Invoke(argsCopy, instr));
        }

        private static object? InvokeBuiltinForCall(BuiltinDescriptor desc, List<Value> args, Instruction instr)
            => InvokeBuiltinForCall(desc, ToObjectArgs(args), instr);

        private static object? InvokeIntrinsicForCall(IntrinsicMethod method, object receiver, List<object> args, Instruction instr)
        {
            MarkAsyncHazardForMutableReceiver(receiver);

            if (!method.Options.OffloadBlockingWork)
                return method.Invoke(receiver, args, instr);

            List<object> argsCopy = new(args);
            return RunOffloadedInvocation(() => method.Invoke(receiver, argsCopy, instr));
        }

        private static object? InvokeIntrinsicForCall(IntrinsicMethod method, object receiver, List<Value> args, Instruction instr)
            => InvokeIntrinsicForCall(method, receiver, ToObjectArgs(args), instr);

        private Env BuildCallEnv(Closure f, List<Value> rawArgs, Instruction instr)
        {
            int piStart = (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0])) ? 1 : 0;
            int total = f.Parameters.Count - piStart;
            int min = Math.Max(0, f.MinArgs - piStart);

            int restIndex = -1;
            if (!string.IsNullOrWhiteSpace(f.RestParameter))
            {
                for (int i = 0; i < total; i++)
                {
                    if (string.Equals(f.Parameters[piStart + i], f.RestParameter, StringComparison.Ordinal))
                    {
                        restIndex = i;
                        break;
                    }
                }

                if (restIndex < 0)
                {
                    throw new VMException(
                        $"Runtime error: invalid function metadata for rest parameter '{f.RestParameter}'",
                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                }

                if (restIndex != total - 1)
                {
                    throw new VMException(
                        $"Runtime error: rest parameter '{f.RestParameter}' must be last",
                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                }
            }

            int fixedCount = restIndex >= 0 ? restIndex : total;

            List<Value> positional = new();
            Dictionary<string, Value> named = new(StringComparer.Ordinal);
            bool sawNamed = false;

            foreach (Value arg in rawArgs)
            {
                if (arg.Kind == ValueKind.Ref && arg.Reference is NamedArgument na)
                {
                    sawNamed = true;
                    if (named.ContainsKey(na.Name))
                        throw new VMException($"Runtime error: duplicate named argument '{na.Name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    named[na.Name] = na.Value;
                }
                else
                {
                    if (sawNamed)
                        throw new VMException("Runtime error: positional argument cannot follow named arguments", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    positional.Add(arg);
                }
            }

            if (restIndex < 0 && positional.Count > total)
                throw new VMException(
                    $"Runtime error: too many args for call (expected {total}, got {positional.Count})",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            Dictionary<string, int> paramIndex = new(StringComparer.Ordinal);
            for (int i = 0; i < fixedCount; i++)
                paramIndex[f.Parameters[piStart + i]] = i;

            Value[] finalArgs = new Value[fixedCount];
            bool[] assigned = new bool[fixedCount];

            int positionalToAssign = Math.Min(positional.Count, fixedCount);
            for (int i = 0; i < positionalToAssign; i++)
            {
                finalArgs[i] = positional[i];
                assigned[i] = true;
            }

            List<object?> restValues = new();
            if (restIndex >= 0)
            {
                for (int i = fixedCount; i < positional.Count; i++)
                    restValues.Add(positional[i].ToObject());
            }

            foreach (KeyValuePair<string, Value> kv in named)
            {
                if (restIndex >= 0 && string.Equals(kv.Key, f.RestParameter, StringComparison.Ordinal))
                    throw new VMException(
                        $"Runtime error: rest parameter '{kv.Key}' cannot be passed as named argument",
                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                if (!paramIndex.TryGetValue(kv.Key, out int idx))
                    throw new VMException($"Runtime error: unknown named argument '{kv.Key}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                if (assigned[idx])
                    throw new VMException($"Runtime error: argument '{kv.Key}' provided multiple times", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                finalArgs[idx] = kv.Value;
                assigned[idx] = true;
            }

            for (int i = 0; i < fixedCount; i++)
            {
                if (!assigned[i])
                {
                    if (i < min)
                    {
                        string missingParam = f.Parameters[piStart + i];
                        if (string.Equals(missingParam, "__outer", StringComparison.Ordinal))
                        {
                            throw new VMException(
                                "Runtime error: nested constructor requires an outer instance argument '__outer'",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

                        throw new VMException(
                            $"Runtime error: insufficient args for call (expected at least {min})",
                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    }

                    finalArgs[i] = Value.Null;
                }
            }

            Env callEnv = new(f.CapturedEnv);
            for (int i = 0; i < fixedCount; i++)
                callEnv.DefineValue(f.Parameters[piStart + i], finalArgs[i]);

            if (restIndex >= 0)
                callEnv.DefineValue(f.Parameters[piStart + restIndex], ToRuntimeValue(CaptureMutableCollectionOwnership(restValues)));

            return callEnv;
        }

        /// <summary>
        /// Handles the CALL opcode.
        /// </summary>
        private StepResult HandleCallInstruction(ref int _ip, Instruction instr)
        {
            if (instr.Operand is string funcName)
            {
                if (TryGetBuiltin(funcName, out BuiltinDescriptor desc))
                {
                    List<Value> args = new();
                    for (int i = desc.ArityMin - 1; i >= 0; i--)
                    {
                        if (_valueStack.Count == 0)
                            throw new VMException($"Runtime error: insufficient args for {funcName}()", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        args.Insert(0, _valueStack.Pop());
                    }

                    object? ret = InvokeBuiltinForCall(desc, args, instr);
                    PushRuntimeObject(ret);
                    return StepResult.Next;
                }

                if (!Functions.TryGetValue(funcName, out FunctionInfo? func))
                    throw new VMException($"Runtime error: unknown function {funcName}", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                if (func.Parameters.Count > 0 && func.Parameters[0] == "this")
                    throw new VMException(
                        $"Runtime error: cannot CALL method '{funcName}' without receiver. Use CALL_INDIRECT with a bound receiver.",
                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                PushRuntimeObject(new Closure(func.Address, func.Parameters, func.MinArgs, _scopes[^1], funcName, func.RestParameter, func.isAsync));
            }

            return HandleCallIndirectInstruction(ref _ip, instr);
        }

        /// <summary>
        /// Handles the MAKE_NAMED_ARG opcode.
        /// </summary>
        private StepResult HandleMakeNamedArgInstruction(Instruction instr)
        {
            if (instr.Operand is not string argName || string.IsNullOrWhiteSpace(argName))
                throw new VMException("Runtime error: MAKE_NAMED_ARG requires a non-empty argument name", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            RequireStack(1, instr, "MAKE_NAMED_ARG");
            Value value = _valueStack.Pop();
            PushRuntimeObject(new NamedArgument(argName, value));
            return StepResult.Next;
        }

        /// <summary>
        /// Handles the MAKE_SPREAD_ARG opcode.
        /// </summary>
        private StepResult HandleMakeSpreadArgInstruction(Instruction instr)
        {
            RequireStack(1, instr, "MAKE_SPREAD_ARG");
            Value value = _valueStack.Pop();
            PushRuntimeObject(new SpreadArgument(value));
            return StepResult.Next;
        }

        private static Instruction AsInternalMemberAccess(Instruction instr)
            => new(OpCode.INDEX_GET, true, instr.Line, instr.Col, instr.OriginFile);

        /// <summary>
        /// Handles the CALL_INDIRECT opcode.
        /// </summary>
        private StepResult HandleCallIndirectInstruction(ref int _ip, Instruction instr)
        {
            if (instr.Operand is int explicitArgCount)
            {
                if (explicitArgCount < 0)
                    throw new VMException("Runtime error: negative argument count in CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                List<Value> argsList = new();
                for (int i = 0; i < explicitArgCount; i++)
                {
                    if (_valueStack.Count == 0)
                        throw new VMException(
                            $"Runtime error: not enough arguments for CALL_INDIRECT (expected {explicitArgCount})",
                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    argsList.Add(_valueStack.Pop());
                }

                if (_valueStack.Count == 0)
                    throw new VMException("Runtime error: missing callee for CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                object callee = GetCallableTarget(_valueStack.Pop(), instr);
                argsList = ExpandSpreadArguments(argsList, instr);
                int actualArgCount = argsList.Count;
                Closure f;
                object? receiver = null;
                StaticInstance? accessType = null;

                if (callee is IntrinsicBound ibEx)
                {
                    if (actualArgCount < ibEx.Method.ArityMin || actualArgCount > ibEx.Method.ArityMax)
                        throw new VMException(
                            $"Runtime error: {ibEx.Method.Name} expects {ibEx.Method.ArityMin}..{ibEx.Method.ArityMax} args, got {actualArgCount}",
                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    object? result = InvokeIntrinsicForCall(ibEx.Method, ibEx.Receiver, argsList, instr);
                    PushRuntimeObject(result);
                    return StepResult.Continue;
                }
                else if (callee is BuiltinCallable bc)
                {
                    if (!TryGetBuiltin(bc.Name, out BuiltinDescriptor desc))
                        throw new VMException($"Runtime error: unknown builtin '{bc.Name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    if (actualArgCount < desc.ArityMin || actualArgCount > desc.ArityMax)
                        throw new VMException(
                            $"Runtime error: builtin '{bc.Name}' expects {desc.ArityMin}..{desc.ArityMax} args, got {actualArgCount}",
                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    object? result = InvokeBuiltinForCall(desc, argsList, instr);
                    PushRuntimeObject(result);
                    return StepResult.Continue;
                }
                else if (callee is NamespaceBuiltinCallable nbc)
                {
                    BuiltinDescriptor desc = nbc.Descriptor;
                    if (actualArgCount < desc.ArityMin || actualArgCount > desc.ArityMax)
                        throw new VMException(
                            $"Runtime error: builtin '{nbc.MemberName}' expects {desc.ArityMin}..{desc.ArityMax} args, got {actualArgCount}",
                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    object? result = InvokeBuiltinForCall(desc, argsList, instr);
                    PushRuntimeObject(result);
                    return StepResult.Continue;
                }
                else if (callee is ClrCallable clrCallable)
                {
                    try
                    {
                        object? result = NativeInterop.Invoke(clrCallable, argsList, this, instr);
                        PushRuntimeObject(result);
                        return StepResult.Continue;
                    }
                    catch (Exception ex)
                    {
                        throw CreateNativeInteropException(ex, "native interop call failed", instr);
                    }
                }
                else if (callee is ClrDelegateHandle clrDelegate)
                {
                    try
                    {
                        object? result = NativeInterop.Invoke(clrDelegate, argsList, this, instr);
                        PushRuntimeObject(result);
                        return StepResult.Continue;
                    }
                    catch (Exception ex)
                    {
                        throw CreateNativeInteropException(ex, "native interop delegate call failed", instr);
                    }
                }
                else if (callee is ClrEventAccessor clrEventAccessor)
                {
                    try
                    {
                        object? result = NativeInterop.Invoke(clrEventAccessor, argsList, this, instr);
                        PushRuntimeObject(result);
                        return StepResult.Continue;
                    }
                    catch (Exception ex)
                    {
                        throw CreateNativeInteropException(ex, "native interop call failed", instr);
                    }
                }
                else if (callee is BoundMethod bm)
                {
                    f = bm.Function;
                    receiver = bm.Receiver;
                    accessType = bm.DeclaringType;
                    if (accessType == null)
                    {
                        if (receiver is StaticInstance rst)
                            accessType = rst;
                        else if (receiver is ClassInstance rinst && TryGetStaticType(rinst, out StaticInstance rst2))
                            accessType = rst2;
                    }

                    if (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0]))
                    {
                        if (argsList.Count > 0 && argsList[0].Equals(ToRuntimeValue(receiver)))
                            throw new VMException(
                                "Runtime error: receiver provided twice (BoundMethod already has implicit receiver).",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    }
                }
                else if (callee is BoundType bt)
                {
                    object ctorVal = GetIndexedValue(bt.Type, "new", AsInternalMemberAccess(instr));
                    if (ctorVal is BoundMethod ctorBound)
                    {
                        f = ctorBound.Function;
                        receiver = ctorBound.Receiver;
                        accessType = ctorBound.DeclaringType ?? bt.Type;
                    }
                    else if (ctorVal is Closure ctorClos)
                    {
                        f = ctorClos;
                        receiver = bt.Type;
                        accessType = bt.Type;
                    }
                    else
                    {
                        throw new VMException("Runtime error: nested type has no constructor 'new'.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    }

                    argsList.Insert(0, ToRuntimeValue(bt.Outer));
                }
                else if (callee is Closure clos)
                {
                    f = clos;

                    if (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0]))
                    {
                        if (argsList.Count == 0)
                            throw new VMException(
                                $"Runtime error: missing '{f.Parameters[0]}' for method call.",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        if (argsList[0].Kind == ValueKind.Ref && argsList[0].Reference is NamedArgument)
                            throw new VMException(
                                $"Runtime error: '{f.Parameters[0]}' must be provided as the first positional argument.",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        receiver = argsList[0].ToObject();
                        argsList.RemoveAt(0);

                        if (receiver is StaticInstance rst)
                            accessType = rst;
                        else if (receiver is ClassInstance rinst && TryGetStaticType(rinst, out StaticInstance rst2))
                            accessType = rst2;
                    }
                    else if (clos.CapturedThis != null)
                    {
                        // C#-style lexical capture: a lambda defined inside a method keeps the
                        // enclosing receiver so this/type/super/outer resolve when it is later
                        // invoked without an explicit receiver of its own.
                        receiver = clos.CapturedThis;
                        accessType = clos.CapturedAccessType;
                    }
                }
                else if (callee is StaticInstance st)
                {
                    object ctorVal = GetIndexedValue(st, "new", AsInternalMemberAccess(instr));
                    if (ctorVal is BoundMethod ctorBound)
                    {
                        f = ctorBound.Function;
                        receiver = ctorBound.Receiver;
                        accessType = ctorBound.DeclaringType ?? st;
                    }
                    else if (ctorVal is Closure ctorClos)
                    {
                        f = ctorClos;
                        receiver = st;
                        accessType = st;
                    }
                    else
                    {
                        throw new VMException("Runtime error: type has no constructor 'new'.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    }
                }
                else
                {
                    throw new VMException($"Runtime error: attempt to call non-function value ({instr.Code})", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                }

                if (TryStartHotAsyncCall(f, argsList, receiver, accessType, instr, out Task<object?> hotTask))
                {
                    PushRuntimeObject(hotTask);
                    return StepResult.Continue;
                }

                EnsureCallDepthWithinLimit(instr);
                Env callEnv = BuildCallEnv(f, argsList, instr);

                int callerDepth = _scopes.Count;
                _scopes.Add(callEnv);
                _callStack.Push(new CallFrame(_ip, callerDepth, receiver, accessType, f.IsAsync));
                _ip = f.Address;
                return StepResult.Continue;
            }

            if (_valueStack.Count == 0)
                throw new VMException("Runtime error: missing callee for CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            object calleeNoOperand = GetCallableTarget(_valueStack.Pop(), instr);
            Closure fNoOperand;
            object? receiverNoOperand = null;
            StaticInstance? accessTypeNoOperand = null;

            if (calleeNoOperand is IntrinsicBound ib)
            {
                int need = ib.Method.ArityMin;
                List<Value> argsB = new();
                for (int i = 0; i < need; i++)
                {
                    if (_valueStack.Count == 0)
                        throw new VMException("Runtime error: insufficient args for intrinsic call",
                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    argsB.Add(_valueStack.Pop());
                }

                object? result = InvokeIntrinsicForCall(ib.Method, ib.Receiver, argsB, instr);
                PushRuntimeObject(result);
                return StepResult.Continue;
            }
            else if (calleeNoOperand is BuiltinCallable bc2)
            {
                if (!TryGetBuiltin(bc2.Name, out BuiltinDescriptor desc))
                    throw new VMException($"Runtime error: unknown builtin '{bc2.Name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                int need = desc.ArityMin;
                List<Value> argsB = new();
                for (int i = 0; i < need; i++)
                {
                    if (_valueStack.Count == 0)
                        throw new VMException("Runtime error: insufficient args for builtin call", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    argsB.Add(_valueStack.Pop());
                }
                argsB.Reverse();

                object? result = InvokeBuiltinForCall(desc, argsB, instr);
                PushRuntimeObject(result);
                return StepResult.Continue;
            }
            else if (calleeNoOperand is NamespaceBuiltinCallable nbc2)
            {
                BuiltinDescriptor desc = nbc2.Descriptor;
                int need = desc.ArityMin;
                List<Value> argsB = new();
                for (int i = 0; i < need; i++)
                {
                    if (_valueStack.Count == 0)
                        throw new VMException("Runtime error: insufficient args for builtin call", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    argsB.Add(_valueStack.Pop());
                }
                argsB.Reverse();

                object? result = InvokeBuiltinForCall(desc, argsB, instr);
                PushRuntimeObject(result);
                return StepResult.Continue;
            }
            else if (calleeNoOperand is BoundMethod bm2)
            {
                fNoOperand = bm2.Function;
                receiverNoOperand = bm2.Receiver;
                accessTypeNoOperand = bm2.DeclaringType;
                if (accessTypeNoOperand == null)
                {
                    if (receiverNoOperand is StaticInstance rst)
                        accessTypeNoOperand = rst;
                    else if (receiverNoOperand is ClassInstance rinst && TryGetStaticType(rinst, out StaticInstance rst2))
                        accessTypeNoOperand = rst2;
                }
            }
            else if (calleeNoOperand is BoundType bt2)
            {
                object ctorVal = GetIndexedValue(bt2.Type, "new", AsInternalMemberAccess(instr));
                if (ctorVal is BoundMethod ctorBound)
                {
                    fNoOperand = ctorBound.Function;
                    receiverNoOperand = ctorBound.Receiver;
                    accessTypeNoOperand = ctorBound.DeclaringType ?? bt2.Type;
                }
                else if (ctorVal is Closure ctorClos)
                {
                    fNoOperand = ctorClos;
                    receiverNoOperand = bt2.Type;
                    accessTypeNoOperand = bt2.Type;
                }
                else
                {
                    throw new VMException("Runtime error: nested type has no constructor 'new'.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                }

                int total = fNoOperand.Parameters.Count;

                List<Value> argsTmp = new();
                for (int i = 0; i < total - 1; i++)
                {
                    if (_valueStack.Count == 0)
                        throw new VMException("Runtime error: insufficient args for constructor call", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    argsTmp.Add(_valueStack.Pop());
                }
                argsTmp.Reverse();
                argsTmp.Insert(0, ToRuntimeValue(bt2.Outer));

                int piStart2 = (fNoOperand.Parameters.Count > 0 && IsReceiverName(fNoOperand.Parameters[0])) ? 1 : 0;
                int expected2 = fNoOperand.Parameters.Count - piStart2;

                if (argsTmp.Count != expected2)
                    throw new VMException(
                        $"Runtime error: argument count mismatch (expected {expected2}, got {argsTmp.Count})",
                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                if (TryStartHotAsyncCall(fNoOperand, argsTmp, receiverNoOperand, accessTypeNoOperand, instr, out Task<object?> hotTask2))
                {
                    PushRuntimeObject(hotTask2);
                    return StepResult.Continue;
                }

                Env callEnv2 = BuildCallEnv(fNoOperand, argsTmp, instr);
                int callerDepth2 = _scopes.Count;
                _scopes.Add(callEnv2);
                _callStack.Push(new CallFrame(_ip, callerDepth2, receiverNoOperand, accessTypeNoOperand, fNoOperand.IsAsync));
                _ip = fNoOperand.Address;
                return StepResult.Continue;
            }
            else if (calleeNoOperand is Closure clos2)
            {
                fNoOperand = clos2;

                if (fNoOperand.Parameters.Count > 0 && IsReceiverName(fNoOperand.Parameters[0]))
                {
                    if (_valueStack.Count == 0)
                        throw new VMException(
                            $"Runtime error: missing '{fNoOperand.Parameters[0]}' for method call.",
                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    receiverNoOperand = _valueStack.Pop().ToObject();

                    if (receiverNoOperand is StaticInstance rst)
                        accessTypeNoOperand = rst;
                    else if (receiverNoOperand is ClassInstance rinst && TryGetStaticType(rinst, out StaticInstance rst2))
                        accessTypeNoOperand = rst2;
                }
            }
            else if (calleeNoOperand is StaticInstance st2)
            {
                object ctorVal = GetIndexedValue(st2, "new", AsInternalMemberAccess(instr));
                if (ctorVal is BoundMethod ctorBound)
                {
                    fNoOperand = ctorBound.Function;
                    receiverNoOperand = ctorBound.Receiver;
                    accessTypeNoOperand = ctorBound.DeclaringType ?? st2;
                }
                else if (ctorVal is Closure ctorClos)
                {
                    fNoOperand = ctorClos;
                    receiverNoOperand = st2;
                    accessTypeNoOperand = st2;
                }
                else
                {
                    throw new VMException("Runtime error: type has no constructor 'new'.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                }
            }
            else
            {
                throw new VMException($"Runtime error: attempt to call non-function value ({instr.Code})", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }

            int piStart = (fNoOperand.Parameters.Count > 0 && IsReceiverName(fNoOperand.Parameters[0])) ? 1 : 0;
            int expected = fNoOperand.Parameters.Count - piStart;

            List<Value> argsList2 = new();
            for (int pi = fNoOperand.Parameters.Count - 1; pi >= piStart; pi--)
            {
                if (_valueStack.Count == 0)
                    throw new VMException("Runtime error: insufficient args for call", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                argsList2.Insert(0, _valueStack.Pop());
            }

            if (argsList2.Count != expected)
                throw new VMException(
                    $"Runtime error: argument count mismatch (expected {expected}, got {argsList2.Count})",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            if (TryStartHotAsyncCall(fNoOperand, argsList2, receiverNoOperand, accessTypeNoOperand, instr, out Task<object?> hotTaskNoOperand))
            {
                PushRuntimeObject(hotTaskNoOperand);
                return StepResult.Continue;
            }

            Env callEnvNoOperand = BuildCallEnv(fNoOperand, argsList2, instr);
            _scopes.Add(callEnvNoOperand);
            _callStack.Push(new CallFrame(_ip, _scopes.Count - 1, receiverNoOperand, accessTypeNoOperand, fNoOperand.IsAsync));
            _ip = fNoOperand.Address;
            return StepResult.Continue;
        }

        /// <summary>
        /// Handles the RET opcode.
        /// </summary>
        private StepResult HandleReturnInstruction(ref int _ip, Instruction instr)
        {
            object? retVal = _valueStack.Count > 0 ? _valueStack.Pop() : null;

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
                nextFinally.HasPendingReturn = true;
                nextFinally.PendingReturnValue = retVal;
                nextFinally.InFinally = true;
                nextFinally.CatchAddr = -1;

                int nip = nextFinally.FinallyAddr;
                nextFinally.FinallyAddr = -1;
                _ip = nip;
                return StepResult.Routed;
            }

            if (_callStack.Count == 0)
                throw new VMException("Runtime error: return with empty call stack", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            CallFrame fr = _callStack.Pop();

            while (_scopes.Count > fr.BaseScopeDepth)
                _scopes.RemoveAt(_scopes.Count - 1);

            while (_tryHandlers.Count > 0 && _tryHandlers.Peek().CallDepth > _callStack.Count)
                _tryHandlers.Pop();

            _ip = fr.ReturnIp;
            PushRuntimeObject(WrapReturnForFrame(fr, retVal));
            return StepResult.Continue;
        }
    }
}
