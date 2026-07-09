using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Codegen;

namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Outcome of an atomic in-place assignment to a local variable.
    /// </summary>
    public enum LocalAssign
    {
        /// <summary>The local does not exist in the target environment.</summary>
        NotPresent,

        /// <summary>The local exists but is declared <c>const</c> and was not modified.</summary>
        IsConst,

        /// <summary>The local existed and its value was updated.</summary>
        Ok
    }

    /// <summary>
    /// A lexical scope: variable bindings plus a link to the enclosing scope.
    ///
    /// Storage is a small <see cref="Value"/>[] (the "slots") plus a parallel const-flag array. A scope
    /// built from a compile-time <see cref="ScopeDescriptor"/> SHARES that descriptor's immutable name→slot
    /// map read-only (no per-activation copy); slot access (LOAD_SLOT/STORE_SLOT) then indexes the array
    /// directly, bypassing the name lookup entirely. Names the descriptor did not predict (globals,
    /// captures on a descriptor-less scope, delete redefines) are appended to a lazily-created overflow map.
    ///
    /// Variable access is NOT internally synchronized. It does not need to be: all bytecode execution
    /// (the only path that reads or writes environments) is serialized by the VM execution gate
    /// (<c>VM._executionGate</c>), so at most one context ever touches an environment at a time, even
    /// across hot-started async child VMs that share environments by reference. The debugger only reads
    /// locals while execution is paused, i.e. while no other context runs. <see cref="SyncRoot"/> is kept
    /// for the few remaining call sites that still lock on it; that locking is likewise redundant under the
    /// gate.
    /// </summary>
    public class Env
    {
        public readonly object SyncRoot = new();

        // The shared, immutable compile-time name→slot map for this scope's own declarations (null for a
        // dynamic scope). Never mutated — extra names go into _overflow.
        private readonly ScopeDescriptor? _descriptor;

        // Lazily-created name→slot map for names not in _descriptor (globals/builtins on the global scope,
        // or the rare descriptor mismatch / delete-then-redefine). Its indices continue past the
        // descriptor's slot count into the grown region of _slots.
        private Dictionary<string, int>? _overflow;

        // Slot storage. For a descriptor scope the first SlotCount slots are pre-filled with Value.Undefined
        // (the "declared but not yet assigned" sentinel) so name resolution skips them until their VAR_DECL
        // runs — reproducing temporal-dead-zone / shadowing semantics. _count is the high-water mark of
        // allocated slots (>= descriptor.SlotCount).
        private Value[] _slots;
        private bool[] _isConst;
        private int _count;

        public Env? Parent;

        /// <summary>
        /// Initializes a dynamic (descriptor-less) scope whose slots grow on demand. Used for the global
        /// scope, host/REPL scopes, and any scope the compiler did not describe.
        /// </summary>
        /// <param name="parent">The parent<see cref="Env?"/></param>
        public Env(Env? parent)
            : this(parent, null)
        {
        }

        /// <summary>
        /// Initializes a scope, pre-sizing its slot storage from <paramref name="descriptor"/> when one is
        /// supplied. The descriptor's name map is shared (not copied); its slots start as
        /// <see cref="Value.Undefined"/> until a <see cref="DefineValue"/> fills the real value. Names
        /// outside the descriptor still grow the storage on demand.
        /// </summary>
        public Env(Env? parent, ScopeDescriptor? descriptor)
        {
            Parent = parent;

            if (descriptor != null && descriptor.SlotCount > 0)
            {
                _descriptor = descriptor;
                _slots = new Value[descriptor.SlotCount];
                System.Array.Fill(_slots, Value.Undefined);
                _isConst = new bool[descriptor.SlotCount];
                _count = descriptor.SlotCount;
            }
            else
            {
                _slots = System.Array.Empty<Value>();
                _isConst = System.Array.Empty<bool>();
                _count = 0;
            }
        }

        // Resolves a name to its slot index in this scope (shared descriptor first, then overflow).
        private bool TryIndexOf(string name, out int idx)
        {
            if (_descriptor != null && _descriptor.NameIndex.TryGetValue(name, out idx))
                return true;
            if (_overflow != null && _overflow.TryGetValue(name, out idx))
                return true;
            idx = 0;
            return false;
        }

        // Returns the slot index for <paramref name="name"/>, appending a fresh overflow slot (growing the
        // backing arrays as needed) if it is not yet bound in this scope.
        private int EnsureSlot(string name)
        {
            if (TryIndexOf(name, out int existing))
                return existing;

            _overflow ??= new Dictionary<string, int>(StringComparer.Ordinal);

            if (_count == _slots.Length)
            {
                int newCapacity = _count == 0 ? 4 : _count * 2;
                System.Array.Resize(ref _slots, newCapacity);
                System.Array.Resize(ref _isConst, newCapacity);
            }

            int idx = _count++;
            _overflow[name] = idx;
            _isConst[idx] = false;
            return idx;
        }

        public bool TryGetValueValue(string name, out Value value)
        {
            for (Env? env = this; env != null; env = env.Parent)
            {
                if (env.TryIndexOf(name, out int idx))
                {
                    Value local = env._slots[idx];
                    // An Undefined slot is a pre-sized-but-not-yet-declared binding: treat it as absent and
                    // keep walking, so an inner TDZ slot does not shadow an outer real binding.
                    if (!local.IsUndefined)
                    {
                        // An Unset slot is an omitted optional parameter: a present binding that reads as
                        // null everywhere except the IS_ARG_UNSET default-parameter guard.
                        value = local.IsUnset ? Value.Null : local;
                        return true;
                    }
                }
            }

            value = Value.Null;
            return false;
        }

        public bool TryGetValue(string name, out object? value)
        {
            if (TryGetValueValue(name, out Value local))
            {
                value = local.ToObject();
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Direct slot read (LOAD_SLOT). The caller has already walked to the owning scope. An Unset slot
        /// (an omitted optional parameter, only reachable transiently from a forward-referencing default
        /// expression) reads as null; only the IS_ARG_UNSET guard sees the raw sentinel.
        /// </summary>
        public Value GetSlotValue(int index)
        {
            Value slot = _slots[index];
            return slot.IsUnset ? Value.Null : slot;
        }

        /// <summary>
        /// Raw presence test for the default-parameter prologue (IS_ARG_UNSET): true only when the nearest
        /// binding of <paramref name="name"/> is the <see cref="Value.Unset"/> omitted-argument sentinel.
        /// Bypasses the null coercion that every ordinary read applies, and skips Undefined TDZ slots the
        /// same way normal resolution does.
        /// </summary>
        public bool IsArgUnset(string name)
        {
            for (Env? env = this; env != null; env = env.Parent)
            {
                if (env.TryIndexOf(name, out int idx))
                {
                    Value local = env._slots[idx];
                    if (!local.IsUndefined)
                        return local.IsUnset;
                }
            }

            return false;
        }

        /// <summary>Direct slot write (STORE_SLOT). The caller has already walked to the owning scope.</summary>
        public void SetSlotValue(int index, Value value) => _slots[index] = value;

        /// <summary>Whether the slot at <paramref name="index"/> holds a <c>const</c> binding.</summary>
        public bool IsSlotConst(int index) => _isConst[index];

        /// <summary>Marks the slot at <paramref name="index"/> as const (CONST_DECL_SLOT).</summary>
        public void SetSlotConst(int index, bool value) => _isConst[index] = value;

        /// <summary>Reverse-maps a slot index to its bound name (cold path, for diagnostics).</summary>
        public string SlotName(int index)
        {
            if (_descriptor != null && index < _descriptor.SlotNames.Count)
                return _descriptor.SlotNames[index];
            if (_overflow != null)
                foreach (KeyValuePair<string, int> entry in _overflow)
                    if (entry.Value == index)
                        return entry.Key;
            return "<slot>";
        }

        /// <summary>Non-walking lookup of a binding declared directly in this environment.</summary>
        public bool TryGetLocalValue(string name, out Value value)
        {
            if (TryIndexOf(name, out int idx))
            {
                Value local = _slots[idx];
                if (!local.IsUndefined)
                {
                    // An Unset slot is an omitted optional parameter: reads as null outside the guard.
                    value = local.IsUnset ? Value.Null : local;
                    return true;
                }
            }

            value = Value.Null;
            return false;
        }

        public bool HasLocal(string name)
            => TryIndexOf(name, out int idx) && !_slots[idx].IsUndefined;

        public bool SetValue(string name, Value value)
        {
            for (Env? env = this; env != null; env = env.Parent)
            {
                if (env.TryIndexOf(name, out int idx) && !env._slots[idx].IsUndefined)
                {
                    env._slots[idx] = value;
                    return true;
                }
            }

            return false;
        }

        public bool Set(string name, object? value)
            => SetValue(name, Value.FromObject(value));

        public void DefineValue(string name, Value value)
        {
            // Resolve the slot first: EnsureSlot may reassign _slots (Array.Resize), and
            // `_slots[EnsureSlot(name)] = value` would evaluate the (stale) array reference before the
            // resize, storing into the old, smaller array.
            int idx = EnsureSlot(name);
            _slots[idx] = value;
        }

        public void Define(string name, object? value)
            => DefineValue(name, Value.FromObject(value));

        public void DefineConstValue(string name, Value value)
        {
            int idx = EnsureSlot(name);
            _slots[idx] = value;
            _isConst[idx] = true;
        }

        public void DefineConst(string name, object? value)
            => DefineConstValue(name, Value.FromObject(value));

        public bool IsConstLocal(string name)
            => TryIndexOf(name, out int idx) && _isConst[idx];

        public bool RemoveLocal(string name)
        {
            if (!TryIndexOf(name, out int idx))
                return false;

            // Drop an overflow binding outright; a descriptor name can't be removed from the shared map, so
            // mark its slot Undefined to make it invisible to resolution (and redefinable). Either way the
            // reference is released for GC.
            _overflow?.Remove(name);
            _slots[idx] = Value.Undefined;
            _isConst[idx] = false;
            return true;
        }

        /// <summary>Enumerates the names really bound in this environment (skips unassigned TDZ slots).</summary>
        public IEnumerable<string> LocalNames
        {
            get
            {
                if (_descriptor != null)
                    for (int i = 0; i < _descriptor.SlotNames.Count; i++)
                        if (!_slots[i].IsUndefined)
                            yield return _descriptor.SlotNames[i];

                if (_overflow != null)
                    foreach (KeyValuePair<string, int> entry in _overflow)
                        if (!_slots[entry.Value].IsUndefined)
                            yield return entry.Key;
            }
        }

        /// <summary>Enumerates the (name, value) bindings really declared in this environment.</summary>
        public IEnumerable<KeyValuePair<string, Value>> LocalBindings()
        {
            if (_descriptor != null)
            {
                for (int i = 0; i < _descriptor.SlotNames.Count; i++)
                {
                    Value local = _slots[i];
                    if (!local.IsUndefined)
                        yield return new KeyValuePair<string, Value>(_descriptor.SlotNames[i], local);
                }
            }

            if (_overflow != null)
            {
                foreach (KeyValuePair<string, int> entry in _overflow)
                {
                    Value local = _slots[entry.Value];
                    if (!local.IsUndefined)
                        yield return new KeyValuePair<string, Value>(entry.Key, local);
                }
            }
        }

        /// <summary>
        /// Updates an existing local in this environment, honoring const-ness. Single-threaded under the
        /// VM execution gate, so the existence check, const check, and write are naturally atomic.
        /// </summary>
        /// <param name="name">The local name.</param>
        /// <param name="value">The new value.</param>
        /// <returns>The <see cref="LocalAssign"/> outcome.</returns>
        public LocalAssign SetLocalIfNotConst(string name, Value value)
        {
            if (!TryIndexOf(name, out int idx) || _slots[idx].IsUndefined)
                return LocalAssign.NotPresent;
            if (_isConst[idx])
                return LocalAssign.IsConst;
            _slots[idx] = value;
            return LocalAssign.Ok;
        }

        public LocalAssign SetLocalIfNotConst(string name, object? value)
            => SetLocalIfNotConst(name, Value.FromObject(value));
    }
}
