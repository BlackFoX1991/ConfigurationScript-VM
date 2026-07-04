using System;
using System.Collections.Generic;

namespace CFGS_VM.VMCore.Codegen
{
    /// <summary>
    /// A compile-time, immutable map from the variable names declared directly in one lexical scope to
    /// their slot indices in that scope's runtime <c>Env</c> storage. Shared across every activation of
    /// the scope: the runtime uses it to pre-size and pre-populate the scope's slot array so a variable
    /// read is a direct array index instead of a name hash + scope walk.
    ///
    /// The map lists ONLY the names a scope declares itself (params/locals/foreach/destructure/synthetic
    /// temps for a function-root; block-local declarations for a block). Names not present are resolved
    /// by name (globals, builtins, captures) or appended dynamically, so a descriptor that omits a name
    /// stays correct — it only forgoes the pre-sizing for that name.
    /// </summary>
    public sealed class ScopeDescriptor
    {
        /// <summary>Slot index → declared name (parallel to the runtime slot array).</summary>
        public IReadOnlyList<string> SlotNames { get; }

        /// <summary>Declared name → slot index.</summary>
        public IReadOnlyDictionary<string, int> NameIndex { get; }

        /// <summary>Number of slots this scope reserves.</summary>
        public int SlotCount => SlotNames.Count;

        public ScopeDescriptor(IReadOnlyList<string> slotNames, IReadOnlyDictionary<string, int> nameIndex)
        {
            SlotNames = slotNames;
            NameIndex = nameIndex;
        }

        /// <summary>A descriptor that reserves no slots (an empty scope).</summary>
        public static readonly ScopeDescriptor Empty =
            new(Array.Empty<string>(), new Dictionary<string, int>(StringComparer.Ordinal));

        /// <summary>
        /// Rebuilds a descriptor from its slot-name list (slot index == list position), used when
        /// deserializing from <c>.cfb</c>. Mirrors how <see cref="ScopeDescriptorBuilder"/> assigns slots.
        /// </summary>
        public static ScopeDescriptor FromSlotNames(IReadOnlyList<string> slotNames)
        {
            if (slotNames.Count == 0)
                return Empty;

            Dictionary<string, int> nameIndex = new(slotNames.Count, StringComparer.Ordinal);
            for (int i = 0; i < slotNames.Count; i++)
                nameIndex[slotNames[i]] = i;
            return new ScopeDescriptor(slotNames, nameIndex);
        }
    }

    /// <summary>
    /// Accumulates a scope's declared names in declaration order while the compiler walks it, then
    /// freezes them into an immutable <see cref="ScopeDescriptor"/>. Redeclaring a name is a no-op
    /// (returns the existing slot); the runtime enforces the "already declared" rule.
    /// </summary>
    public sealed class ScopeDescriptorBuilder
    {
        private readonly List<string> _names = new();
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

        public int Declare(string name)
        {
            if (_index.TryGetValue(name, out int existing))
                return existing;

            int idx = _names.Count;
            _names.Add(name);
            _index[name] = idx;
            return idx;
        }

        public ScopeDescriptor Build()
        {
            if (_names.Count == 0)
                return ScopeDescriptor.Empty;

            return new ScopeDescriptor(
                _names.ToArray(),
                new Dictionary<string, int>(_index, StringComparer.Ordinal));
        }
    }
}
