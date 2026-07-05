using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Codegen;

namespace CFGS_VM.VMCore.Extensions.Instance
{
    public sealed class ClassInstance
    {
        /// <summary>
        /// Synchronizes field access for this runtime instance.
        /// </summary>
        public object SyncRoot { get; } = new();

        /// <summary>
        /// Gets the ClassName
        /// </summary>
        public string ClassName { get; }

        /// <summary>Lifecycle flags kept as fields (not dictionary keys) so the destroyed check is a plain read.</summary>
        public bool Destroyed;
        public bool Destroying;

        // The class's declared instance fields (own layer only — inheritance is layered via __base) live in a
        // Value[] indexed by a shared compile-time shape, instead of a per-instance dictionary. The dictionary
        // is kept for the `__`-metadata keys (__type/__base/__outer) and for any name the shape did not predict.
        private readonly ScopeDescriptor? _shape;
        // Concrete name→slot map (the descriptor's NameIndex is always a Dictionary underneath); using the
        // concrete type avoids an interface dispatch on the hot field-access path.
        private readonly Dictionary<string, int>? _shapeIndex;
        private readonly Value[] _slots;

        /// <summary>
        /// Backing storage for `__`-metadata keys and names outside the field shape.
        /// </summary>
        public Dictionary<string, Value> Fields { get; }

        public ClassInstance(string className)
            : this(className, null)
        {
        }

        public ClassInstance(string className, ScopeDescriptor? shape)
        {
            ClassName = className;
            _shape = shape;
            _shapeIndex = shape != null && shape.SlotCount > 0 ? shape.NameIndex as Dictionary<string, int> : null;
            _slots = _shapeIndex != null ? new Value[shape!.SlotCount] : System.Array.Empty<Value>();
            Fields = new Dictionary<string, Value>();
        }

        /// <summary>Reads a field: a shape slot when the name is a declared field, else the metadata dict.</summary>
        public bool TryGetField(string name, out Value value)
        {
            if (_shapeIndex != null && _shapeIndex.TryGetValue(name, out int idx))
            {
                value = _slots[idx];
                return true;
            }

            return Fields.TryGetValue(name, out value);
        }

        /// <summary>Writes a field: into its shape slot when declared, else the metadata dict (overflow).</summary>
        public void SetField(string name, Value value)
        {
            if (_shapeIndex != null && _shapeIndex.TryGetValue(name, out int idx))
                _slots[idx] = value;
            else
                Fields[name] = value;
        }

        /// <summary>The shared field shape (identity used as an inline-cache key), or null for dict storage.</summary>
        public ScopeDescriptor? Shape => _shape;

        /// <summary>Direct slot read by index (inline-cache fast path — no name lookup, no resolution).</summary>
        public Value GetSlotByIndex(int index) => _slots[index];

        /// <summary>Direct slot write by index (inline-cache fast path — no name lookup, no resolution, no lock).</summary>
        public void SetSlotByIndex(int index, Value value) => _slots[index] = value;

        /// <summary>Resolves a name to its OWN-layer field slot (not inherited); for populating an inline cache.</summary>
        public bool TryGetOwnFieldSlot(string name, out int slot)
        {
            if (_shapeIndex != null)
                return _shapeIndex.TryGetValue(name, out slot);
            slot = 0;
            return false;
        }

        /// <summary>Enumerates every (name, value) binding — shape fields followed by metadata/overflow.</summary>
        public IEnumerable<KeyValuePair<string, Value>> EnumerateFields()
        {
            if (_shape != null)
            {
                for (int i = 0; i < _shape.SlotNames.Count; i++)
                    yield return new KeyValuePair<string, Value>(_shape.SlotNames[i], _slots[i]);
            }

            foreach (KeyValuePair<string, Value> entry in Fields)
                yield return entry;
        }
    }
}
