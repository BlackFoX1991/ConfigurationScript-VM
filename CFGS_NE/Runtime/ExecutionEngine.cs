using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;
using System.Text;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        /// <summary>
        /// Defines the RunStopReason
        /// </summary>
        private enum RunStopReason
        {
            /// <summary>
            /// Defines the Halted
            /// </summary>
            Halted,

            /// <summary>
            /// Defines the AwaitPending
            /// </summary>
            AwaitPending
        }

        /// <summary>
        /// The Run
        /// </summary>
        /// <param name="debugging">The debugging<see cref="bool"/></param>
        /// <param name="lastPos">The lastPos<see cref="int"/></param>
        public void Run(bool debugging = false, int lastPos = 0)
        {
            if (_program is null || _program.Count == 0)
                return;

            RunAsyncCore(debugging, lastPos, CancellationToken.None, resetDebugStream: true)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// The RunUntilAwaitOrHalt
        /// </summary>
        /// <param name="debugging">The debugging<see cref="bool"/></param>
        /// <param name="lastPos">The lastPos<see cref="int"/></param>
        /// <returns>The <see cref="RunStopReason"/></returns>
        private RunStopReason RunUntilAwaitOrHalt(bool debugging = false, int lastPos = 0)
        {
            if (_program is null || _program.Count == 0)
                return RunStopReason.Halted;

            VM? prevVm = CurrentVm;
            CurrentVm = this;
            try
            {
                bool routed = false;

                _isDebugging = debugging;
                int _ip = lastPos;

                while (_ip < _program.Count)
                {
                    try
                    {
                        if (debugging)
                            WriteDebugTrace(_ip);

                        Instruction instr = _program[_ip++];
                        _ip = Math.Clamp(_ip, 0, _program.Count - 1);
                        StepResult res = HandleInstruction(ref _ip, _program, instr);

                        if (res == StepResult.Halt)
                            return RunStopReason.Halted;

                        if (res == StepResult.Await)
                            return RunStopReason.AwaitPending;

                        if (res == StepResult.Continue)
                            continue;

                        if (res == StepResult.Routed)
                            routed = true;
                    }
                    catch (VMException ex)
                    {
                        int safeIp = Math.Min(_ip, _program.Count - 1);

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
                        int safeIp = Math.Min(_ip, _program.Count - 1);
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

                    if (top.Exception is not object deferredEx || top.InFinally)
                        continue;

                    top.Exception = null;

                    if (RouteExceptionToTryHandlers(deferredEx, _program[routedSafeIp], out int routedIp))
                    {
                        _ip = routedIp;
                        routed = true;
                    }
                    else
                    {
                        if (debugging)
                        {
                            DebugStream!.Position = 0;
                            using FileStream file = File.Create("log_file.log");
                            DebugStream.CopyTo(file);
                        }

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
                CurrentVm = prevVm;
            }
        }

        /// <summary>
        /// The RunAsyncCore
        /// </summary>
        /// <param name="debugging">The debugging<see cref="bool"/></param>
        /// <param name="lastPos">The lastPos<see cref="int"/></param>
        /// <param name="ct">The ct<see cref="CancellationToken"/></param>
        /// <param name="resetDebugStream">The resetDebugStream<see cref="bool"/></param>
        /// <returns>The <see cref="Task"/></returns>
        private async Task RunAsyncCore(bool debugging, int lastPos, CancellationToken ct, bool resetDebugStream)
        {
            if (resetDebugStream)
                _debugStream = new MemoryStream();

            int startIp = lastPos;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                RunStopReason reason = RunUntilAwaitOrHalt(debugging, startIp);
                if (reason == RunStopReason.Halted)
                    return;

                Task<object?> task = _awaitTask!;
                try
                {
                    object? res = await AwaitTaskAsync(task).ConfigureAwait(false);
                    _stack.Push(res!);
                }
                catch (Exception ex)
                {
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

        /// <summary>
        /// The RunAsync
        /// </summary>
        /// <param name="debugging">The debugging<see cref="bool"/></param>
        /// <param name="lastPos">The lastPos<see cref="int"/></param>
        /// <param name="ct">The ct<see cref="CancellationToken"/></param>
        /// <returns>The <see cref="Task"/></returns>
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
            DebugStream!.Write(Encoding.Default.GetBytes(
                $"[DEBUG] {dinstr.Line} ->  IP={ip}, STACK=[{string.Join(", ", _stack.Reverse())}], SCOPES={_scopes.Count}, CALLSTACK={_callStack.Count}\n"));
            DebugStream!.Write(Encoding.Default.GetBytes(
                $"[DEBUG] {dinstr} (Line {dinstr.Line}, Col {dinstr.Col})\n"));
        }

        /// <summary>
        /// The SafeCurrentInstr
        /// </summary>
        /// <param name="insns">The insns<see cref="List{Instruction}"/></param>
        /// <param name="ip">The ip<see cref="int"/></param>
        /// <returns>The <see cref="Instruction"/></returns>
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
