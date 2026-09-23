using CFGS_VM.VMCore.Codegen;
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

        /// <summary>
        /// Upper bound on nested <em>native</em> re-entries into the interpreter.
        /// <para>
        /// Ordinary CFGS calls are iterative and bounded by <see cref="MaxCallDepth"/>, which guards the
        /// heap call stack. A builtin or intrinsic that invokes a script callback (a map/filter/sort
        /// callback, <c>interface_call</c>, a native delegate) takes a different route: it calls
        /// <see cref="InvokeClosureSyncValue"/>, which re-enters <c>RunUntilAwaitOrHalt</c> on the real CLR
        /// stack. That path had no limit at all, so deep enough callback nesting overflowed the CLR stack.
        /// A <c>StackOverflowException</c> cannot be caught in .NET and takes the whole process down with
        /// it, including a hosting language server, bypassing every try/catch on both sides.
        /// </para>
        /// <para>
        /// The limit therefore has to stop well short of the real stack, the same reasoning as
        /// <c>ParserContext.DefaultMaxRecursionDepth</c> on the parser side. Measured on the default 1 MB
        /// stack with a minimal callback (<c>[1].map(func(x){ return deep(n-1); })</c>): Release survives
        /// ~110 levels and dies between 110 and 120, Debug dies between 80 and 100. A level costs one
        /// RunUntilAwaitOrHalt frame plus the intrinsic's own frames, and a callback doing real work costs
        /// more than the probe did, so the budget is set at roughly half the Debug figure. That is still
        /// far deeper callback nesting than real code uses, and the failure is now a catchable runtime
        /// error instead of a process kill.
        /// </para>
        /// </summary>
        private const int MaxNativeInvokeDepth = 48;

        private int _nativeInvokeDepth;

        /// <summary>
        /// Throws a runtime error when native callback re-entry has reached <see cref="MaxNativeInvokeDepth"/>.
        /// </summary>
        private void EnsureNativeInvokeDepthWithinLimit(Instruction instr)
        {
            if (_nativeInvokeDepth >= MaxNativeInvokeDepth)
                throw new VMException(
                    $"Runtime error: maximum native callback nesting ({MaxNativeInvokeDepth}) exceeded (possible infinite recursion through a callback)",
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
                // A namespace function (App.Core.add) is a closure in its member binding, not a hoisted
                // top-level function.
                if (name.Contains('.') && TryResolveQualifiedCallable(name, originFile, out object? callable))
                    return await InvokeRuntimeValueAsync(callable, args ?? [], originFile, ct).ConfigureAwait(false);

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
            EnsureNativeInvokeDepthWithinLimit(instr);

            Env callEnv = BuildCallEnv(closure, args, instr);
            int callerScopeDepth = _scopes.Count;
            int callerFrameDepth = _callStack.Count;
            _scopes.Add(callEnv);
            _callStack.Push(new CallFrame(_program.Count, callerScopeDepth, receiver, accessType));

            _nativeInvokeDepth++;
            try
            {
                RunStopReason reason = RunUntilAwaitOrHalt(false, closure.Address, _callStack.Count, ct);

                if (reason == RunStopReason.AwaitPending)
                    throw new VMException("Runtime error: callback function must not use await.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                return _valueStack.Count > 0 ? _valueStack.Pop() : Value.Null;
            }
            finally
            {
                _nativeInvokeDepth--;

                // A normal return already unwound both stacks through RET, so these loops are no-ops on
                // the happy path. They matter when the callback throws: the only cleanup used to come from
                // RouteExceptionToTryHandlers, which never runs if an intrinsic swallows the exception (or
                // if we bail out on AwaitPending above). The pushed scope and call frame would then survive
                // the call and corrupt every later name resolution on this VM.
                while (_callStack.Count > callerFrameDepth)
                    _callStack.Pop();
                while (_scopes.Count > callerScopeDepth)
                    _scopes.RemoveAt(_scopes.Count - 1);
            }
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
                AllowFileIO = AllowFileIO,
                AllowNativeInterop = AllowNativeInterop,
                AllowPluginLoading = AllowPluginLoading,
                EntryScriptPath = EntryScriptPath,
                WorkingDirectory = WorkingDirectory,
                NativeAssemblyBaseDirectory = NativeAssemblyBaseDirectory,
                RestrictFileIoToWorkspace = RestrictFileIoToWorkspace
            };
            // Program, inline caches and function tables are taken over by reference rather than rebuilt.
            // See VM.AdoptProgramFrom for why that is safe and what it used to cost per async call.
            child.AdoptProgramFrom(this);

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
            _callStack.Push(new CallFrame(_program.Count, _scopes.Count, receiver, accessType));
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

            // Fast path for the overwhelmingly common call shape: no rest parameter, every argument passed
            // positionally (no named/spread markers), and an in-range arg count. This is what a plain call
            // like fib(n - 1) produces. It binds the args straight into the pre-sized call Env, skipping the
            // six bookkeeping collections (positional/named/paramIndex/finalArgs/assigned/rest) the general
            // path allocates. Any non-positional arg or out-of-range count falls through to the full path,
            // which also produces the precise too-many / insufficient / unknown-named-arg diagnostics.
            if (string.IsNullOrWhiteSpace(f.RestParameter))
            {
                int argCount = rawArgs.Count;
                if (argCount >= min && argCount <= total)
                {
                    bool allPositional = true;
                    for (int i = 0; i < argCount; i++)
                    {
                        Value a = rawArgs[i];
                        if (a.Kind == ValueKind.Ref && a.Reference is NamedArgument)
                        {
                            allPositional = false;
                            break;
                        }
                    }

                    if (allPositional)
                    {
                        _rootDescriptorByAddress.TryGetValue(f.Address, out ScopeDescriptor? fastDescriptor);
                        Env fastEnv = new(f.CapturedEnv, fastDescriptor);
                        for (int i = 0; i < argCount; i++)
                            fastEnv.DefineValue(f.Parameters[piStart + i], rawArgs[i]);
                        // Omitted optional parameters get the Unset sentinel (not Null) so the default-
                        // parameter prologue can tell an omitted argument apart from an explicit null.
                        for (int i = argCount; i < total; i++)
                            fastEnv.DefineValue(f.Parameters[piStart + i], Value.Unset);
                        return fastEnv;
                    }
                }
            }

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

                    // Omitted optional parameter: the Unset sentinel lets the default-parameter prologue
                    // distinguish it from an explicitly passed null.
                    finalArgs[i] = Value.Unset;
                }
            }

            // Pre-size the call scope from the function-root descriptor (params + body-top-level locals) when
            // one is known; params are then placed into their descriptor slots by name below. Falls back to a
            // dynamic scope (e.g. for a .cfb-loaded function that carries no descriptor).
            _rootDescriptorByAddress.TryGetValue(f.Address, out ScopeDescriptor? rootDescriptor);
            Env callEnv = new(f.CapturedEnv, rootDescriptor);
            for (int i = 0; i < fixedCount; i++)
                callEnv.DefineValue(f.Parameters[piStart + i], finalArgs[i]);

            if (restIndex >= 0)
                callEnv.DefineValue(f.Parameters[piStart + restIndex], ToRuntimeValue(CaptureMutableCollectionOwnership(restValues)));

            return callEnv;
        }

        /// <summary>
        /// Handles the CALL opcode.
        /// <para>
        /// No emitter produces CALL; the only instance in the codebase is a synthetic diagnostic operand
        /// built by <see cref="InvokeGlobalFunctionAsyncValue"/>, which is never executed. The builtin
        /// branch that used to live here was therefore unreachable, and it was also wrong: it popped
        /// exactly <c>ArityMin</c> operands no matter how many the call site had pushed, so a variadic
        /// builtin would have received the wrong arguments and left the rest on the stack. Builtin calls
        /// go through CALL_INDIRECT, which checks the full arity range.
        /// </para>
        /// </summary>
        private StepResult HandleCallInstruction(ref int _ip, Instruction instr)
        {
            if (instr.Operand is string funcName)
            {
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
                _callStack.Push(new CallFrame(_ip, callerDepth, receiver, accessType));
                _ip = f.Address;
                return StepResult.Continue;
            }

            // Every emitter passes the argument count as an int operand (CallEmitter, ClassEmitter,
            // ExprEmitter). The operand-less form used to carry its own ~200-line implementation that no
            // compiled program could reach, and which popped a fixed arity regardless of how many values
            // the call site had actually pushed. It is gone: a stream that still uses the form (only
            // hand-written bytecode can) now fails cleanly instead of binding the wrong arguments.
            throw new VMException(
                "Runtime error: CALL_INDIRECT requires an argument-count operand",
                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        /// <summary>
        /// Handles the RET opcode.
        /// </summary>
        private StepResult HandleReturnInstruction(ref int _ip, Instruction instr)
        {
            // Keep this a Value. The old form was `object? retVal = ... ? _valueStack.Pop() : null`, whose
            // ternary is target-typed to object, so every single function return boxed the tagged value.
            // That is the hottest path in the VM and exactly the allocation Value (and its box cache)
            // exists to avoid.
            Value retVal = _valueStack.Count > 0 ? _valueStack.Pop() : Value.Null;

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
            _valueStack.Push(retVal);
            return StepResult.Continue;
        }
    }
}
