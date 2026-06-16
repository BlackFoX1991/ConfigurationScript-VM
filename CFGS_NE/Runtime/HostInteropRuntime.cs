using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;
using CFGS_VM.VMCore.Extensions.Intrinsics.Core;
using CFGS_VM.VMCore.Interop;
using CFGS_VM.VMCore.Plugin;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        public object? GetMember(object? target, string memberName, string? originFile = null)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrWhiteSpace(memberName))
                throw new ArgumentException("Member name must not be empty.", nameof(memberName));

            Instruction instr = CreateHostMemberInstruction(OpCode.INDEX_GET, originFile);
            return GetIndexedValue(target, memberName, instr);
        }

        public void SetMember(object? target, string memberName, object? value, string? originFile = null)
        {
            if (target is null)
                throw new ArgumentNullException(nameof(target));
            if (string.IsNullOrWhiteSpace(memberName))
                throw new ArgumentException("Member name must not be empty.", nameof(memberName));

            object normalizedTarget = target;
            object? normalizedValue = NormalizeHostValue(value);
            Instruction instr = CreateHostMemberInstruction(OpCode.INDEX_SET, originFile);
            SetIndexedValue(ref normalizedTarget, memberName, normalizedValue!, instr, PropertyWriteMode.Normal);
        }

        public Task<object?> InvokeMemberAsync(object? target, string memberName, IEnumerable<object?>? args = null, string? originFile = null, CancellationToken ct = default)
        {
            object? member = GetMember(target, memberName, originFile);
            return InvokeValueAsync(member, args, originFile, ct);
        }

        public Task<object?> NewAsync(string className, IEnumerable<object?>? args = null, string? originFile = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(className))
                throw new ArgumentException("Class name must not be empty.", nameof(className));

            object? target = ResolveQualifiedRuntimeValue(className, originFile);
            return InvokeValueAsync(target, args, originFile, ct);
        }

        public async Task<object?> InvokeValueAsync(object? callable, IEnumerable<object?>? args = null, string? originFile = null, CancellationToken ct = default)
        {
            if (callable is null)
                throw new ArgumentNullException(nameof(callable));
            ct.ThrowIfCancellationRequested();

            Instruction instr = new(OpCode.CALL_INDIRECT, null, -1, -1, originFile ?? string.Empty);
            List<object> runtimeArgs = args?.Select(static arg => NormalizeHostValue(arg)!).ToList() ?? [];
            object? result;

            switch (callable)
            {
                case IntrinsicBound intrinsic:
                    if (runtimeArgs.Count < intrinsic.Method.ArityMin || runtimeArgs.Count > intrinsic.Method.ArityMax)
                        throw new VMException(
                            $"Runtime error: {intrinsic.Method.Name} expects {intrinsic.Method.ArityMin}..{intrinsic.Method.ArityMax} args, got {runtimeArgs.Count}",
                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    result = InvokeIntrinsicForCall(intrinsic.Method, intrinsic.Receiver, runtimeArgs, instr);
                    break;

                case BuiltinCallable builtin:
                    if (!TryGetBuiltin(builtin.Name, out BuiltinDescriptor builtinDescriptor))
                        throw new VMException($"Runtime error: unknown builtin '{builtin.Name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    if (runtimeArgs.Count < builtinDescriptor.ArityMin || runtimeArgs.Count > builtinDescriptor.ArityMax)
                        throw new VMException(
                            $"Runtime error: builtin '{builtin.Name}' expects {builtinDescriptor.ArityMin}..{builtinDescriptor.ArityMax} args, got {runtimeArgs.Count}",
                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    result = InvokeBuiltinForCall(builtinDescriptor, runtimeArgs, instr);
                    break;

                case NamespaceBuiltinCallable namespaceBuiltin:
                    BuiltinDescriptor namespaceDescriptor = namespaceBuiltin.Descriptor;
                    if (runtimeArgs.Count < namespaceDescriptor.ArityMin || runtimeArgs.Count > namespaceDescriptor.ArityMax)
                        throw new VMException(
                            $"Runtime error: builtin '{namespaceBuiltin.MemberName}' expects {namespaceDescriptor.ArityMin}..{namespaceDescriptor.ArityMax} args, got {runtimeArgs.Count}",
                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    result = InvokeBuiltinForCall(namespaceDescriptor, runtimeArgs, instr);
                    break;

                case ClrCallable or ClrDelegateHandle or ClrEventAccessor:
                    if (!AllowNativeInterop)
                        throw new VMException("Runtime error: native .NET interop is disabled for this VM.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    result = NativeInterop.Invoke(callable, runtimeArgs, this, instr);
                    break;

                case BoundMethod boundMethod:
                    result = await InvokeClosureHostAsync(
                        boundMethod.Function,
                        runtimeArgs,
                        boundMethod.Receiver,
                        boundMethod.DeclaringType ?? ResolveAccessType(boundMethod.Receiver),
                        instr,
                        ct).ConfigureAwait(false);
                    break;

                case BoundType boundType:
                    object? nestedCtor = GetIndexedValue(boundType.Type, "new", CreateHostMemberInstruction(OpCode.INDEX_GET, originFile));
                    runtimeArgs.Insert(0, boundType.Outer);
                    result = await InvokeValueAsync(nestedCtor, runtimeArgs, originFile, ct).ConfigureAwait(false);
                    break;

                case Closure closure:
                    object? receiver = null;
                    StaticInstance? accessType = null;
                    if (closure.Parameters.Count > 0 && IsReceiverName(closure.Parameters[0]))
                    {
                        if (runtimeArgs.Count == 0)
                            throw new VMException($"Runtime error: missing '{closure.Parameters[0]}' for method call.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        receiver = runtimeArgs[0];
                        runtimeArgs.RemoveAt(0);
                        accessType = ResolveAccessType(receiver);
                    }

                    result = await InvokeClosureHostAsync(closure, runtimeArgs, receiver, accessType, instr, ct).ConfigureAwait(false);
                    break;

                case StaticInstance staticType:
                    object? ctor = GetIndexedValue(staticType, "new", CreateHostMemberInstruction(OpCode.INDEX_GET, originFile));
                    result = await InvokeValueAsync(ctor, runtimeArgs, originFile, ct).ConfigureAwait(false);
                    break;

                default:
                    throw new VMException($"Runtime error: attempt to call non-function value ({callable.GetType().Name})", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }

            if (AwaitableAdapter.TryGetTask(result, out Task<object?> awaited))
                return await AwaitTaskAsync(awaited).WaitAsync(ct).ConfigureAwait(false);

            return result;
        }

        private async Task<object?> InvokeClosureHostAsync(
            Closure closure,
            List<object> args,
            object? receiver,
            StaticInstance? accessType,
            Instruction instr,
            CancellationToken ct = default)
        {
            if (TryStartHotAsyncCall(closure, args, receiver, accessType, instr, out Task<object?> startedTask))
                return await AwaitTaskAsync(startedTask).WaitAsync(ct).ConfigureAwait(false);

            return InvokeClosureSync(closure, args, instr, receiver, accessType, ct);
        }

        private object? ResolveQualifiedRuntimeValue(string qualifiedName, string? originFile)
        {
            string[] parts = qualifiedName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                throw new ArgumentException("Qualified name must not be empty.", nameof(qualifiedName));

            object? current = GetGlobal(parts[0]);
            for (int i = 1; i < parts.Length; i++)
                current = GetIndexedValue(current!, parts[i], CreateHostMemberInstruction(OpCode.INDEX_GET, originFile));

            return current;
        }

        private static Instruction CreateHostMemberInstruction(OpCode code, string? originFile)
            => new(code, true, -1, -1, originFile ?? string.Empty);

        private static StaticInstance? ResolveAccessType(object? receiver)
            => receiver switch
            {
                StaticInstance staticInstance => staticInstance,
                ClassInstance classInstance when TryGetStaticType(classInstance, out StaticInstance type) => type,
                _ => null
            };
    }
}
