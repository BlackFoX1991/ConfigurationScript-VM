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
            // yield ist ein kooperativer Suspensionspunkt ohne echtes IO: der Trampolin gibt an dieser
            // Await-Stelle die Execution-Gate frei, sodass andere pendente async-Aufrufe drankommen. Ein
            // einmaliges Task.Yield genuegt dafuer. Der frueere Task.Run-Wrapper erzwang einen zweiten,
            // ueberfluessigen Threadpool-Hop und allokierte pro yield eine Closure; YieldOnceAsync hopt genau
            // einmal ohne Closure.
            _awaitTask = YieldOnceAsync();
            _awaitResumeIp = _ip;
            return StepResult.Await;
        }

        private static async Task<object?> YieldOnceAsync()
        {
            await Task.Yield();
            return null;
        }

        private async Task<object?> RunHotStartEntryAsync(
            int startIp,
            CancellationToken ct = default)
        {
            int ip = startIp;

            // Die erste (synchrone) Hot-Start-Phase laeuft unter der Execution-Gate des AUFRUFERS: dieser
            // haelt sie, waehrend die CALL-Instruktion diesen Child-Trampolin startet. Erst nach dem ersten
            // echten IO-Await uebernimmt dieser Trampolin ein eigenes Permit. ownsGate verfolgt genau dieses
            // selbst gehaltene Permit, damit niemals das Permit des Aufrufers freigegeben wird. So laeuft zu
            // jedem Zeitpunkt hoechstens ein VM-Kontext Bytecode aus (siehe VM._executionGate).
            bool ownsGate = false;
            try
            {
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

                    // IO-Await liegt ausserhalb der Gate: ein selbst gehaltenes Permit vorher freigeben.
                    if (ownsGate)
                    {
                        _executionGate.Release();
                        ownsGate = false;
                    }

                    object? res;
                    try
                    {
                        res = await AwaitTaskAsync(task).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Fehler-Routing mutiert VM-Kontrollzustand (Try-Handler, Scopes), also erst die
                        // Gate wieder uebernehmen. Ein bereits abgebrochenes ct laesst WaitAsync sofort mit
                        // OperationCanceledException durchschlagen, was hier korrekt propagiert.
                        await _executionGate.WaitAsync(ct).ConfigureAwait(false);
                        ownsGate = true;

                        ThrowOrRouteHotStartAwaitFailure(ex, resumeIp, ref ip);
                        continue;
                    }

                    // Erfolgreiche Fortsetzung: Gate wieder uebernehmen, dann Bytecode fortsetzen.
                    await _executionGate.WaitAsync(ct).ConfigureAwait(false);
                    ownsGate = true;
                    _valueStack.Push(ToRuntimeValue(res));
                    ip = resumeIp;
                }
            }
            finally
            {
                if (ownsGate)
                    _executionGate.Release();
            }
        }

        /// <summary>
        /// Routet einen im Hot-Start-Await aufgetretenen Fehler zu einem Try-Handler (setzt dann
        /// <paramref name="ip"/>) oder wirft eine <see cref="VMException"/>, wenn kein Handler greift.
        /// Der Aufrufer haelt beim Aufruf die Execution-Gate.
        /// </summary>
        private void ThrowOrRouteHotStartAwaitFailure(Exception ex, int resumeIp, ref int ip)
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
                return;
            }

            if (awaitTranslated)
                throw new VMException(awaitClrMessage, at.Line, at.Col, at.OriginFile, IsDebugging, DebugStream!, ex, payload.Stack, awaitClrCode);

            throw new VMException(payload.ToString()!, at.Line, at.Col, at.OriginFile, IsDebugging, DebugStream!, payload.Stack);
        }
    }
}
