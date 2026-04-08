using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;
using System.Linq;
using System.Text;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        private StepResult HandleTryPushInstruction(Instruction instr)
        {
            object[] arr = (object[])instr.Operand!;
            int catchIp = (int)arr[0];
            int finallyIp = (int)arr[1];

            TryHandler th = new(catchIp, finallyIp, _scopes.Count, _callStack.Count);
            _tryHandlers.Push(th);
            return StepResult.Next;
        }

        private StepResult HandleTryPopInstruction(ref int _ip, List<Instruction> _insns, Instruction instr)
        {
            if (_tryHandlers.Count == 0)
                return StepResult.Next;

            TryHandler h = _tryHandlers.Peek();
            if (!h.InFinally && h.FinallyAddr >= 0)
            {
                if (h.FinallyAddr >= _ip)
                {
                    h.InFinally = true;
                    h.CatchAddr = -1;
                    int nip = h.FinallyAddr;
                    h.FinallyAddr = -1;
                    _ip = nip;
                    return StepResult.Routed;
                }

                h.FinallyAddr = -1;
            }

            if (h.HasPendingReturn)
            {
                object? retVal = h.PendingReturnValue;
                _tryHandlers.Pop();

                TryHandler? outerWithFinally = null;
                foreach (TryHandler th in _tryHandlers)
                {
                    if (th.FinallyAddr >= 0 && !th.InFinally && th.CallDepth == _callStack.Count)
                    {
                        outerWithFinally = th;
                        break;
                    }
                }

                if (outerWithFinally != null)
                {
                    outerWithFinally.HasPendingReturn = true;
                    outerWithFinally.PendingReturnValue = retVal;
                    outerWithFinally.InFinally = true;
                    outerWithFinally.CatchAddr = -1;
                    int nip = outerWithFinally.FinallyAddr;
                    outerWithFinally.FinallyAddr = -1;
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
                _stack.Push(WrapReturnForFrame(fr, retVal)!);
                return StepResult.Continue;
            }

            if (h.HasPendingLeave)
            {
                int leaveTarget = h.PendingLeaveTargetIp;
                int leaveScopes = h.PendingLeaveScopes;
                _tryHandlers.Pop();

                TryHandler? outerWithFinally = null;
                foreach (TryHandler th in _tryHandlers)
                {
                    if (th.FinallyAddr >= 0 && !th.InFinally && th.CallDepth == _callStack.Count)
                    {
                        outerWithFinally = th;
                        break;
                    }
                }

                if (outerWithFinally != null)
                {
                    outerWithFinally.HasPendingLeave = true;
                    outerWithFinally.PendingLeaveTargetIp = leaveTarget;
                    outerWithFinally.PendingLeaveScopes = leaveScopes;
                    outerWithFinally.InFinally = true;
                    outerWithFinally.CatchAddr = -1;
                    int nip = outerWithFinally.FinallyAddr;
                    outerWithFinally.FinallyAddr = -1;
                    _ip = nip;
                    return StepResult.Routed;
                }

                for (int i = 0; i < leaveScopes; i++)
                {
                    if (_scopes.Count <= 1)
                        throw new VMException("Runtime error: cannot pop global scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    _scopes.RemoveAt(_scopes.Count - 1);
                }

                _ip = leaveTarget;
                return StepResult.Continue;
            }

            if (h.Exception is object ex)
            {
                _tryHandlers.Pop();
                if (RouteExceptionToTryHandlers(ex, SafeCurrentInstr(_insns, _ip), out int nip))
                {
                    _ip = nip;
                    return StepResult.Routed;
                }

                Instruction instrNow = SafeCurrentInstr(_insns, _ip);
                throw new VMException(ex.ToString()!, instrNow.Line, instrNow.Col, instrNow.OriginFile, IsDebugging, DebugStream!, BuildStackString(_insns, instrNow));
            }

            _tryHandlers.Pop();
            return StepResult.Next;
        }

        private StepResult HandleThrowInstruction(ref int _ip, List<Instruction> _insns, Instruction instr)
        {
            object? thrown = _stack.Count > 0 ? _stack.Pop() : null;
            object exPayload = thrown is ExceptionObject eo
                ? eo
                : (thrown ?? new ExceptionObject(
                    type: "UserError",
                    message: "throw",
                    file: instr.OriginFile,
                    line: instr.Line,
                    col: instr.Col,
                    stack: BuildStackString(_insns, instr)
                ));

            if (RouteExceptionToTryHandlers(exPayload, instr, out int nip))
            {
                _ip = nip;
                return StepResult.Routed;
            }

            ExceptionObject uncaught = exPayload as ExceptionObject
                ?? new ExceptionObject(
                    type: "UserError",
                    message: exPayload.ToString() ?? "throw",
                    file: instr.OriginFile,
                    line: instr.Line,
                    col: instr.Col,
                    stack: BuildStackString(_insns, instr)
                );

            throw new VMException(uncaught.ToString()!, instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!, uncaught.Stack);
        }

        private bool RouteExceptionToTryHandlers(object exPayload, Instruction instr, out int newIp)
        {
            while (_tryHandlers.Count > 0)
            {
                TryHandler h = _tryHandlers.Peek();
                if (h.InFinally)
                {
                    _tryHandlers.Pop();
                    continue;
                }

                PopScopesToBase(h.ScopeDepthAtTry);
                if (h.CatchAddr >= 0)
                {
                    _stack.Push(exPayload);
                    newIp = h.CatchAddr;
                    h.CatchAddr = -1;
                    h.Exception = null;

                    while (_callStack.Count > h.CallDepth)
                    {
                        CallFrame fr = _callStack.Pop();
                        while (_scopes.Count > fr.BaseScopeDepth)
                            _scopes.RemoveAt(_scopes.Count - 1);
                        while (_tryHandlers.Count > 0 && _tryHandlers.Peek().CallDepth > _callStack.Count)
                            _tryHandlers.Pop();
                    }

                    return true;
                }

                if (h.FinallyAddr >= 0)
                {
                    h.Exception = exPayload;
                    newIp = h.FinallyAddr;
                    h.FinallyAddr = -1;
                    h.InFinally = true;
                    h.CatchAddr = -1;

                    while (_callStack.Count > h.CallDepth)
                    {
                        CallFrame fr = _callStack.Pop();
                        while (_scopes.Count > fr.BaseScopeDepth)
                            _scopes.RemoveAt(_scopes.Count - 1);
                        while (_tryHandlers.Count > 0 && _tryHandlers.Peek().CallDepth > _callStack.Count)
                            _tryHandlers.Pop();
                    }

                    return true;
                }

                _tryHandlers.Pop();
            }

            newIp = -1;
            return false;
        }

        private VMException AttachLanguageStack(VMException ex, List<Instruction> insns, Instruction current)
        {
            if (!string.IsNullOrWhiteSpace(ex.LanguageStackTrace))
                return ex;

            string stack = BuildStackString(insns, current);
            int line = ex.Line >= 0 ? ex.Line : current.Line;
            int col = ex.Column >= 0 ? ex.Column : current.Col;
            string? file = !string.IsNullOrWhiteSpace(ex.FileSource) ? ex.FileSource : current.OriginFile;

            return new VMException(ex.RawMessage, line, col, file, IsDebugging, DebugStream!, stack);
        }

        private string BuildStackString(List<Instruction> insns, Instruction current)
        {
            StringBuilder sb = new();
            sb.Append("  at ")
              .Append(current.OriginFile).Append(':')
              .Append(current.Line).Append(':')
              .Append(current.Col).AppendLine();

            foreach (CallFrame frame in _callStack.Reverse())
            {
                int ip = Math.Clamp(frame.ReturnIp, 0, insns.Count - 1);
                Instruction i = insns[ip];
                sb.Append("  at ")
                  .Append(i.OriginFile).Append(':')
                  .Append(i.Line).Append(':')
                  .Append(i.Col).AppendLine();
            }

            return sb.ToString();
        }

        private string DumpStack()
        {
            if (_stack == null || _stack.Count == 0) return "<empty>";
            object[] arr = _stack.ToArray();
            IEnumerable<string> parts = arr.Select(FormatVal);
            return string.Join(" | ", parts);
        }

        private string DumpCallStack()
        {
            if (_callStack == null || _callStack.Count == 0) return "<empty>";

            CallFrame[] arr = _callStack.ToArray();
            int curDepth = _scopes?.Count ?? 0;
            IEnumerable<string> parts = arr.Select((fr, i) =>
            {
                bool isTop = i == 0;
                int scopesPlus = Math.Max(0, curDepth - fr.BaseScopeDepth);
                string thisPart = fr.ThisRef != null ? FormatVal(fr.ThisRef) : "null";
                string scopeInfo = isTop ? $"base={fr.BaseScopeDepth}, scopes+={scopesPlus}" : $"base={fr.BaseScopeDepth}";
                return $"#{i}: ret={fr.ReturnIp}, {scopeInfo}, this={thisPart}";
            });

            return string.Join(" ; ", parts);
        }
    }
}
