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

        private string UndefinedVariableMessage(string name)
        {
            string message = $"Runtime error: undefined variable '{name}'";
            return TryFindClosestRuntimeName(name, out string? closest)
                ? $"{message}; did you mean '{closest}'?"
                : message;
        }

        private string UndefinedAssignmentMessage(string name)
        {
            string message = $"Runtime error: assignment to undeclared variable '{name}'";
            return TryFindClosestRuntimeName(name, out string? closest)
                ? $"{message}; did you mean '{closest}'?"
                : message;
        }

        private bool TryFindClosestRuntimeName(string name, out string? closest)
        {
            HashSet<string> candidates = new(StringComparer.Ordinal);

            foreach (Env scope in _scopes)
            {
                lock (scope.SyncRoot)
                {
                    foreach (string candidate in scope.Vars.Keys)
                        candidates.Add(candidate);
                }
            }

            foreach (string functionName in Functions.Keys)
                candidates.Add(functionName);

            foreach (string builtinName in SnapshotBuiltinNames())
                candidates.Add(builtinName);

            return TryFindClosestName(name, candidates, out closest);
        }

        private static bool TryFindClosestName(string name, IEnumerable<string> candidates, out string? closest)
        {
            closest = null;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            int bestDistance = int.MaxValue;
            foreach (string candidate in candidates)
            {
                int distance = EditDistance(name, candidate);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = candidate;
                }
            }

            int threshold = Math.Max(2, name.Length / 3);
            return closest is not null && bestDistance <= threshold;
        }

        private static int EditDistance(string left, string right)
        {
            if (left.Length == 0)
                return right.Length;
            if (right.Length == 0)
                return left.Length;

            int[] previous = new int[right.Length + 1];
            int[] current = new int[right.Length + 1];

            for (int j = 0; j <= right.Length; j++)
                previous[j] = j;

            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + cost);
                }

                (previous, current) = (current, previous);
            }

            return previous[right.Length];
        }

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
