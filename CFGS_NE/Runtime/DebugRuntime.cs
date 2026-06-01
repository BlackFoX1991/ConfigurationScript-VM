using CFGS_VM.VMCore.Debugging;
using CFGS_VM.VMCore.Extensions.Core;
using System.Globalization;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        public IVmDebugger? Debugger { get; set; }

        public int DebugCallStackDepth => _callStack.Count;

        public int DebugScopeCount => _scopes.Count;

        public int DebugStackCount => _stack.Count;

        public IReadOnlyList<object?> GetDebugStackSnapshot()
            => _stack.ToArray();

        public IReadOnlyList<VmDebugVariable> GetDebugLocalsSnapshot()
            => _scopes.Count == 0 ? [] : SnapshotEnv(_scopes[^1]);

        public IReadOnlyList<VmDebugVariable> GetDebugGlobalsSnapshot()
            => _scopes.Count == 0 ? [] : SnapshotEnv(_scopes[0]);

        public IReadOnlyList<VmDebugFrame> GetDebugCallStackSnapshot()
        {
            if (_program is null || _program.Count == 0 || _callStack.Count == 0)
                return [];

            List<VmDebugFrame> frames = [];
            int index = 0;
            foreach (CallFrame frame in _callStack)
            {
                int ip = Math.Clamp(frame.ReturnIp, 0, _program.Count - 1);
                Instruction instr = _program[ip];
                frames.Add(new VmDebugFrame(index++, frame.ReturnIp, frame.BaseScopeDepth, instr.OriginFile, instr.Line, instr.Col));
            }

            return frames;
        }

        public bool TryGetDebugVariable(string name, out object? value)
        {
            if (_scopes.Count > 0 && _scopes[^1].TryGetValue(name, out value))
                return true;

            value = null;
            return false;
        }

        public string FormatDebugValue(object? value)
        {
            using StringWriter writer = new(CultureInfo.InvariantCulture);
            PrintValue(value!, writer);
            return writer.ToString();
        }

        public string FormatDebugInstruction(Instruction instruction)
            => instruction.Operand is null ? instruction.Code.ToString() : $"{instruction.Code} {instruction.Operand}";

        private static IReadOnlyList<VmDebugVariable> SnapshotEnv(Env env)
        {
            lock (env.SyncRoot)
            {
                return env.Vars
                    .OrderBy(static kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new VmDebugVariable(kv.Key, kv.Value, env.ConstVars.Contains(kv.Key)))
                    .ToList();
            }
        }
    }
}
