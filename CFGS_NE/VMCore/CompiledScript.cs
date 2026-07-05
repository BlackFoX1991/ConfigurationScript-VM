using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    public sealed class CompiledScript
    {
        // v3: char operands are serialized as raw UTF-16 code units (ushort) instead of via the
        // UTF-8 char writer, so lone surrogate char literals round-trip and corrupt char bytes
        // fail cleanly. Bumped to reject older artifacts that used the encoder-based char format.
        // v4: the auto-main flag was removed from the format (auto-main no longer exists; a script
        // must call main() explicitly or use imperative top-level). Bumped to reject older artifacts
        // that still carried the flag byte.
        // v5: scope descriptors (per-block PUSH_SCOPE operands + a function-address→root-descriptor
        // table) are now serialized so slot-based local access (LOAD_SLOT/STORE_SLOT) round-trips through
        // .cfb. Bumped to reject older artifacts that lacked descriptors while carrying slot opcodes.
        public const int CurrentBytecodeVersion = 5;

        public string Name { get; }

        public string SourceHash { get; }

        public string CompilerVersion { get; }

        public int BytecodeVersion { get; }

        public IReadOnlyList<string> RequiredPlugins { get; }

        public List<Instruction> Instructions { get; }

        public Dictionary<string, FunctionInfo> Functions { get; }

        /// <summary>
        /// Function-address → call-scope descriptor (params + body-top-level locals). In-memory only: not
        /// serialized to <c>.cfb</c> (a loaded artifact runs those scopes dynamically). Empty for a script
        /// loaded from <c>.cfb</c>.
        /// </summary>
        public IReadOnlyDictionary<int, ScopeDescriptor> RootScopeDescriptors { get; }

        /// <summary>
        /// Class simple name → instance field shape (null = ambiguous). In-memory only, not persisted to .cfb;
        /// a .cfb-loaded class stores its fields in a dictionary (correct, just not pre-sized).
        /// </summary>
        public IReadOnlyDictionary<string, ScopeDescriptor?> ClassFieldShapes { get; }

        public CompiledScript(
            string name,
            string sourceHash,
            string compilerVersion,
            int bytecodeVersion,
            IEnumerable<string> requiredPlugins,
            IEnumerable<Instruction> instructions,
            IDictionary<string, FunctionInfo> functions,
            IReadOnlyDictionary<int, ScopeDescriptor>? rootScopeDescriptors = null,
            IReadOnlyDictionary<string, ScopeDescriptor?>? classFieldShapes = null)
        {
            Name = name;
            SourceHash = sourceHash;
            CompilerVersion = compilerVersion;
            BytecodeVersion = bytecodeVersion;
            RequiredPlugins = requiredPlugins.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Instructions = instructions.ToList();
            Functions = functions.ToDictionary(
                static kv => kv.Key,
                static kv => kv.Value,
                StringComparer.Ordinal);
            RootScopeDescriptors = rootScopeDescriptors ?? new Dictionary<int, ScopeDescriptor>();
            ClassFieldShapes = classFieldShapes ?? new Dictionary<string, ScopeDescriptor?>(StringComparer.Ordinal);
        }
    }
}
