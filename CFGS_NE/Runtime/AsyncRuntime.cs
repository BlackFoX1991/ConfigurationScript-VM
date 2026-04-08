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
        /// <summary>
        /// The FinalizeAsyncResult
        /// </summary>
        /// <param name="value">The value<see cref="object?"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static Task<object?> FinalizeAsyncResult(object? value)
        {
            if (AwaitableAdapter.TryGetDirectTask(value, out Task<object?> flattened))
                return flattened;

            return Task.FromResult(value);
        }

        /// <summary>
        /// The AwaitTaskAsync
        /// </summary>
        /// <param name="task">The task<see cref="Task{object?}"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static async Task<object?> AwaitTaskAsync(Task<object?> task)
            => await task.ConfigureAwait(false);

        /// <summary>
        /// Handles the AWAIT opcode.
        /// </summary>
        private StepResult HandleAwaitInstruction(ref int _ip, List<Instruction> _insns, Instruction instr)
        {
            RequireStack(1, instr, "AWAIT");
            object? awaited = _stack.Pop();

            if (!AwaitableAdapter.TryGetTask(awaited, out Task<object?>? task))
            {
                _stack.Push(awaited);
                return StepResult.Next;
            }

            if (task.IsCompleted)
            {
                if (task.IsFaulted)
                {
                    Exception ex = task.Exception?.InnerException ?? task.Exception ?? new Exception("await faulted");
                    ExceptionObject payload = new(
                        type: "AwaitError",
                        message: ex.Message,
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

                _stack.Push(task.Result!);
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

        /// <summary>
        /// The RunHotStartEntryAsync
        /// </summary>
        /// <param name="startIp">The startIp<see cref="int"/></param>
        /// <param name="ct">The ct<see cref="CancellationToken"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
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
                    _stack.Push(res!);
                    ip = resumeIp;
                }
                catch (Exception ex)
                {
                    Instruction at = SafeCurrentInstr(_program!, resumeIp);
                    ExceptionObject payload = new(
                        type: "AwaitError",
                        message: ex.Message,
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

                    throw new VMException(payload.ToString()!, at.Line, at.Col, at.OriginFile, IsDebugging, DebugStream!, payload.Stack);
                }
            }
        }
    }
}
