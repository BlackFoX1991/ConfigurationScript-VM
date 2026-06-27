using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    public sealed class CompiledScript
    {
        // v3: char operands are serialized as raw UTF-16 code units (ushort) instead of via the
        // UTF-8 char writer, so lone surrogate char literals round-trip and corrupt char bytes
        // fail cleanly. Bumped to reject older artifacts that used the encoder-based char format.
        public const int CurrentBytecodeVersion = 3;

        public string Name { get; }

        public string SourceHash { get; }

        public string CompilerVersion { get; }

        public int BytecodeVersion { get; }

        public bool ShouldAutoInvokeMain { get; }

        public IReadOnlyList<string> RequiredPlugins { get; }

        public List<Instruction> Instructions { get; }

        public Dictionary<string, FunctionInfo> Functions { get; }

        public CompiledScript(
            string name,
            string sourceHash,
            string compilerVersion,
            int bytecodeVersion,
            bool shouldAutoInvokeMain,
            IEnumerable<string> requiredPlugins,
            IEnumerable<Instruction> instructions,
            IDictionary<string, FunctionInfo> functions)
        {
            Name = name;
            SourceHash = sourceHash;
            CompilerVersion = compilerVersion;
            BytecodeVersion = bytecodeVersion;
            ShouldAutoInvokeMain = shouldAutoInvokeMain;
            RequiredPlugins = requiredPlugins.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Instructions = instructions.ToList();
            Functions = functions.ToDictionary(
                static kv => kv.Key,
                static kv => kv.Value,
                StringComparer.Ordinal);
        }
    }
}
