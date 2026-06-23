using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;
using CFGS_VM.VMCore.Debugging;
using System.Text;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        private enum RunStopReason
        {
            Halted,

            AwaitPending
        }

        public void Run(bool debugging = false, int lastPos = 0)
        {
            if (_program is null || _program.Count == 0)
                return;

            RunAsyncCore(debugging, lastPos, CancellationToken.None, resetDebugStream: true)
                .GetAwaiter()
                .GetResult();
        }

        private RunStopReason RunUntilAwaitOrHalt(bool debugging = false, int lastPos = 0, int minRouteCallDepth = 0, CancellationToken ct = default)
        {
            if (_program is null || _program.Count == 0)
                return RunStopReason.Halted;

            VM? prevVm = CurrentVm;
            int previousMinRouteCallDepth = _minRouteCallDepth;
            CurrentVm = this;
            _minRouteCallDepth = Math.Max(_minRouteCallDepth, minRouteCallDepth);
            try
            {
                bool routed = false;

                _isDebugging = debugging;
                int _ip = lastPos;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    // A well-formed program terminates via HALT; reaching exactly Count is the
                    // expected fall-through / return-to-caller signal (synchronous closure invokes
                    // set the frame return IP to Count). Any other out-of-range IP is an emitter or
                    // jump-target bug, so surface it as a clean runtime error instead of silently
                    // clamping (which would re-execute the last instruction forever) or indexing
                    // out of bounds.
                    if (_ip == _program.Count)
                        break;

                    if (_ip < 0 || _ip > _program.Count)
                        throw new VMException(
                            $"Runtime error: instruction pointer {_ip} is out of range [0,{_program.Count}]",
                            -1, -1, string.Empty, IsDebugging, DebugStream!);

                    try
                    {
                        Instruction instr = _program[_ip];
                        VmDebugContext debugContext = CreateDebugContext(_ip, instr);

                        if (Debugger?.BeforeInstruction(this, debugContext) == VmDebuggerControl.Stop)
                            return RunStopReason.Halted;

                        if (debugging)
                            WriteDebugTrace(_ip);

                        _ip++;
                        StepResult res = HandleInstruction(ref _ip, _program, instr);
                        Debugger?.AfterInstruction(this, debugContext);

                        if (res == StepResult.Halt)
                            return RunStopReason.Halted;

                        if (res == StepResult.Await)
                            return RunStopReason.AwaitPending;

                        if (res == StepResult.Continue)
                            continue;

                        if (res == StepResult.Routed)
                            routed = true;
                    }
                    catch (ScriptExceptionSignal signal)
                    {
                        if (RouteExceptionToTryHandlers(signal.Payload, signal.Instruction, out int nip))
                        {
                            _ip = nip;
                            routed = true;
                        }
                        else if (_minRouteCallDepth > 0)
                        {
                            throw;
                        }
                        else
                        {
                            throw CreateUncaughtScriptException(signal.Payload, signal.Instruction, _program);
                        }
                    }
                    catch (VMException ex)
                    {
                        int safeIp = Math.Min(_ip, _program.Count - 1);
                        Debugger?.OnException(this, CreateDebugContext(safeIp, _program[safeIp]), ex);

                        ExceptionObject payload = new(
                            type: "RuntimeError",
                            message: ex.Message,
                            file: _program[safeIp].OriginFile,
                            line: _program[safeIp].Line,
                            col: _program[safeIp].Col,
                            stack: BuildStackString(_program, _program[safeIp])
                        );

                        if (RouteExceptionToTryHandlers(payload, _program[safeIp], out int nip))
                        {
                            _ip = nip;
                            routed = true;
                        }
                        else
                        {
                            throw AttachLanguageStack(ex, _program, _program[safeIp]);
                        }
                    }
                    catch (Exception sysEx)
                    {
                        // Cancellation (host MaxExecutionTime or a caller-supplied token) and fatal
                        // CLR conditions must never be routed into script try/catch handlers: a
                        // script could otherwise swallow its own enforced timeout or mask
                        // process-level memory corruption. Let those propagate untouched.
                        if (sysEx is OperationCanceledException || sysEx is OutOfMemoryException)
                            throw;

                        int safeIp = Math.Min(_ip, _program.Count - 1);
                        Debugger?.OnException(this, CreateDebugContext(safeIp, _program[safeIp]), sysEx);
                        ExceptionObject payload = new(
                            type: "SystemError",
                            message: sysEx.Message,
                            file: _program[safeIp].OriginFile,
                            line: _program[safeIp].Line,
                            col: _program[safeIp].Col,
                            stack: BuildStackString(_program, _program[safeIp])
                        );

                        if (RouteExceptionToTryHandlers(payload, _program[safeIp], out int nip))
                        {
                            _ip = nip;
                            routed = true;
                        }
                        else
                        {
                            if (_minRouteCallDepth > 0)
                                throw;

                            throw new VMException(
                                $"Uncaught system exception : {sysEx.Message}\n{sysEx.Source}",
                                _program[safeIp].Line,
                                _program[safeIp].Col,
                                _program[safeIp].OriginFile,
                                IsDebugging,
                                DebugStream!,
                                BuildStackString(_program, _program[safeIp]));
                        }
                    }

                    if (!routed)
                        continue;

                    int routedSafeIp = Math.Min(_ip, _program.Count - 1);
                    routed = false;

                    if (_tryHandlers.Count == 0)
                        continue;

                    TryHandler top = _tryHandlers.Peek();

                    if (!top.Exception.HasValue || top.InFinally)
                        continue;

                    Value deferredEx = top.Exception.Value;
                    top.Exception = null;

                    if (RouteExceptionToTryHandlers(deferredEx, _program[routedSafeIp], out int routedIp))
                    {
                        _ip = routedIp;
                        routed = true;
                    }
                    else
                    {
                        if (debugging)
                            TryWriteEmergencyDebugLog();

                        throw new VMException(
                            "Uncaught system exception",
                            _program[routedSafeIp].Line,
                            _program[routedSafeIp].Col,
                            _program[routedSafeIp].OriginFile,
                            IsDebugging,
                            DebugStream!,
                            BuildStackString(_program, _program[routedSafeIp]));
                    }
                }

                return RunStopReason.Halted;
            }
            finally
            {
                _minRouteCallDepth = previousMinRouteCallDepth;
                CurrentVm = prevVm;
            }
        }

        private async Task RunAsyncCore(bool debugging, int lastPos, CancellationToken ct, bool resetDebugStream)
        {
            if (resetDebugStream)
                _debugStream = new MemoryStream();

            int startIp = lastPos;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                RunStopReason reason = RunUntilAwaitOrHalt(debugging, startIp, ct: ct);
                if (reason == RunStopReason.Halted)
                    return;

                Task<object?> task = _awaitTask!;
                try
                {
                    object? res = await AwaitTaskAsync(task).ConfigureAwait(false);
                    _valueStack.Push(ToRuntimeValue(res));
                }
                catch (Exception ex)
                {
                    // See HandleInstruction's dispatch catch: cancellation and fatal CLR conditions
                    // must propagate rather than be surfaced as a script-catchable AwaitError.
                    if (ex is OperationCanceledException || ex is OutOfMemoryException)
                        throw;

                    Instruction at = SafeCurrentInstr(_program!, _awaitResumeIp);

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
                        startIp = nip;
                        _awaitTask = null;
                        continue;
                    }

                    throw new VMException(payload.ToString()!, at.Line, at.Col, at.OriginFile, IsDebugging, DebugStream!, payload.Stack);
                }

                startIp = _awaitResumeIp;
                _awaitTask = null;
            }
        }

        public Task RunAsync(bool debugging = false, int lastPos = 0, CancellationToken ct = default)
            => RunAsyncCore(debugging, lastPos, ct, resetDebugStream: true);

        /// <summary>
        /// Writes the current debug trace line pair.
        /// </summary>
        /// <param name="ip">The current instruction pointer.</param>
        private void WriteDebugTrace(int ip)
        {
            int di = Math.Clamp(ip, 0, _program!.Count - 1);
            Instruction dinstr = _program[di];
            string stackText = string.Join(", ", _valueStack.Reverse().Select(FormatVal));
            DebugStream!.Write(Encoding.Default.GetBytes(
                $"[DEBUG] {dinstr.Line} ->  IP={ip}, STACK=[{stackText}], SCOPES={_scopes.Count}, CALLSTACK={_callStack.Count}\n"));
            DebugStream!.Write(Encoding.Default.GetBytes(
                $"[DEBUG] {dinstr} (Line {dinstr.Line}, Col {dinstr.Col})\n"));
        }

        private void TryWriteEmergencyDebugLog()
        {
            if (DebugStream is null)
                return;

            try
            {
                DebugStream.Position = 0;
                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
                string path = Path.Combine(Path.GetTempPath(), $"cfgs_debug_{timestamp}_{Environment.ProcessId}_{Guid.NewGuid():N}.log");
                using FileStream file = File.Create(path);
                DebugStream.CopyTo(file);
                DebugStream.Position = 0;
            }
            catch
            {
                // Best effort only: the original VM exception must remain the observable failure.
            }
        }

        private VmDebugContext CreateDebugContext(int ip, Instruction instruction)
            => new(ip, instruction, _valueStack.Count, _scopes.Count, _callStack.Count);

        private static Instruction SafeCurrentInstr(List<Instruction> insns, int ip)
        {
            if (insns == null || insns.Count == 0)
                return new Instruction(OpCode.HALT, null, -1, -1, "");

            int i = ip;
            if (i < 0)
                i = 0;
            if (i >= insns.Count)
                i = insns.Count - 1;
            return insns[i];
        }
    }
}
