using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;
using CFGS_VM.VMCore.Extensions.Intrinsics.Core;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        private static Task<object?> FinalizeAsyncResult(object? value)
        {
            if (AwaitableAdapter.TryGetDirectTask(value, out Task<object?> flattened))
                return flattened;

            return Task.FromResult(value);
        }

        private static async Task<object?> AwaitTaskAsync(Task<object?> task)
            => await task.ConfigureAwait(false);

        /// <summary>
        /// Handles the AWAIT opcode.
        /// </summary>
        private StepResult HandleAwaitInstruction(ref int _ip, List<Instruction> _insns, Instruction instr)
        {
            RequireStack(1, instr, "AWAIT");
            Value awaited = _valueStack.Pop();
            object? awaitedObject = awaited.ToObject();

            if (!AwaitableAdapter.TryGetTask(awaitedObject, out Task<object?>? task))
            {
                _valueStack.Push(awaited);
                return StepResult.Next;
            }

            if (task.IsCompleted)
            {
                if (task.IsFaulted)
                {
                    Exception ex = task.Exception?.InnerException ?? task.Exception ?? new Exception("await faulted");
                    bool awaitTranslated = HostExceptionTranslator.TryTranslate(ex, out string awaitClrMessage, out string awaitClrCode);
                    ExceptionObject payload = new(
                        type: awaitTranslated ? HostExceptionTranslator.RuntimeErrorType : "AwaitError",
                        message: awaitTranslated ? awaitClrMessage : ex.Message,
                        file: instr.OriginFile,
                        line: instr.Line,
                        col: instr.Col,
                        stack: BuildStackString(_insns, instr)
                    );

                    if (RouteExceptionToTryHandlers(payload, instr, out int nip))
                    {
                        _ip = nip;
                        return StepResult.Routed;
                    }

                    if (awaitTranslated)
                        throw new VMException(awaitClrMessage, instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!, ex, payload.Stack, awaitClrCode);

                    throw new VMException(payload.ToString()!, instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!, payload.Stack);
                }

                if (task.IsCanceled)
                {
                    ExceptionObject payload = new(
                        type: "AwaitCanceled",
                        message: "await canceled",
                        file: instr.OriginFile,
                        line: instr.Line,
                        col: instr.Col,
                        stack: BuildStackString(_insns, instr)
                    );

                    if (RouteExceptionToTryHandlers(payload, instr, out int nip))
                    {
                        _ip = nip;
                        return StepResult.Routed;
                    }

                    throw new VMException(payload.ToString()!, instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!, payload.Stack);
                }

                _valueStack.Push(ToRuntimeValue(task.Result));
                return StepResult.Next;
            }

            _awaitTask = task;
            _awaitResumeIp = _ip;
            return StepResult.Await;
        }

        /// <summary>
        /// Handles the YIELD opcode.
        /// </summary>
        private StepResult HandleYieldInstruction(ref int _ip)
        {
            _awaitTask = Task.Run<object?>(async () =>
            {
                await Task.Yield();
                return null;
            });
            _awaitResumeIp = _ip;
            return StepResult.Await;
        }

        private async Task<object?> RunHotStartEntryAsync(
            int startIp,
            CancellationToken ct = default)
        {
            int ip = startIp;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                RunStopReason reason = RunUntilAwaitOrHalt(false, ip);
                if (reason == RunStopReason.Halted)
                {
                    object? finalValue = ConsumeHotStartResult();
                    return await FinalizeAsyncResult(finalValue).ConfigureAwait(false);
                }

                Task<object?> task = _awaitTask!;
                int resumeIp = _awaitResumeIp;
                _awaitTask = null;

                try
                {
                    object? res = await AwaitTaskAsync(task).ConfigureAwait(false);
                    _valueStack.Push(ToRuntimeValue(res));
                    ip = resumeIp;
                }
                catch (Exception ex)
                {
                    Instruction at = SafeCurrentInstr(_program!, resumeIp);
                    bool awaitTranslated = HostExceptionTranslator.TryTranslate(ex, out string awaitClrMessage, out string awaitClrCode);
                    ExceptionObject payload = new(
                        type: awaitTranslated ? HostExceptionTranslator.RuntimeErrorType : "AwaitError",
                        message: awaitTranslated ? awaitClrMessage : ex.Message,
                        file: at.OriginFile,
                        line: at.Line,
                        col: at.Col,
                        stack: BuildStackString(_program!, at)
                    );

                    if (RouteExceptionToTryHandlers(payload, at, out int nip))
                    {
                        ip = nip;
                        continue;
                    }

                    if (awaitTranslated)
                        throw new VMException(awaitClrMessage, at.Line, at.Col, at.OriginFile, IsDebugging, DebugStream!, ex, payload.Stack, awaitClrCode);

                    throw new VMException(payload.ToString()!, at.Line, at.Col, at.OriginFile, IsDebugging, DebugStream!, payload.Stack);
                }
            }
        }
    }
}
