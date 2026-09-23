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
        // v6: default parameters now lower to an IS_ARG_UNSET presence guard instead of `param == null`,
        // so an explicitly passed null no longer triggers the default; only an omitted argument does.
        // Bumped to reject older artifacts whose prologue still carries the null-based default semantics.
        // v7: a three-part `for` now emits a per-iteration scope, so its loop variable gets a fresh
        // binding each pass and closures capture that iteration's value. The change lives in the
        // emitter, so an older artifact would keep the old shared-counter semantics in its instruction
        // stream with no indication. Bumped to reject those rather than run them with different
        // meaning. The same bump also stops a v7 artifact from reaching an older VM and failing at the
        // appended IS_PRIMITIVE_TYPE opcode, which would look like a corrupt file instead of an
        // outdated VM.
        // v8: fields without an initializer now start as null instead of 0, a `|` in a value pattern
        // now separates or-pattern alternatives instead of computing a bitwise value, and const
        // instance fields are sealed by the appended SEAL_CONST_FIELDS opcode at the end of their
        // class's constructor. All three live in the emitted code, so an older artifact would keep the
        // old meaning silently.
        // v9: script namespaces are compile-time only. Members live in one global binding each and are
        // read and written through the appended LOAD_GLOBAL/STORE_GLOBAL opcodes; NEW_NAMESPACE and
        // INDEX_GET_OPT, which built the old runtime namespace objects, are gone, so the opcode numbers
        // after them moved.
        public const int CurrentBytecodeVersion = 9;

        public string Name { get; }

        public string SourceHash { get; }

        public string CompilerVersion { get; }

        public int BytecodeVersion { get; }

        public IReadOnlyList<string> RequiredPlugins { get; }

        public List<Instruction> Instructions { get; }

        public Dictionary<string, FunctionInfo> Functions { get; }

        /// <summary>
        /// Function-address → call-scope descriptor (params + body-top-level locals). Serialized to
        /// <c>.cfb</c> since bytecode v5 (alongside the per-block PUSH_SCOPE descriptor operands), so
        /// slot-based local access round-trips through compiled artifacts.
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
