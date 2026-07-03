using CFGS_VM.VMCore;

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
    /// Variable access is NOT internally synchronized. It does not need to be: all bytecode execution
    /// (the only path that reads or writes environments) is serialized by the VM execution gate
    /// (<c>VM._executionGate</c>), so at most one context ever touches an environment at a time, even
    /// across hot-started async child VMs that share environments by reference. The debugger only reads
    /// locals while execution is paused, i.e. while no other context runs. <see cref="SyncRoot"/> is kept
    /// for the few remaining call sites that still lock on it; that locking is likewise redundant under the
    /// gate. Removing the per-access lock takes a Monitor.Enter/Exit off every LOAD_VAR/STORE_VAR.
    /// </summary>
    public class Env
    {
        public readonly object SyncRoot = new();

        public Dictionary<string, Value> Vars = new();

        public HashSet<string> ConstVars = new(StringComparer.Ordinal);

        public Env? Parent;

        /// <summary>
        /// Initializes a new instance of the <see cref="Env"/> class.
        /// </summary>
        /// <param name="parent">The parent<see cref="Env?"/></param>
        public Env(Env? parent)
        {
            Parent = parent;
        }

        public bool TryGetValueValue(string name, out Value value)
        {
            for (Env? env = this; env != null; env = env.Parent)
            {
                if (env.Vars.TryGetValue(name, out Value local))
                {
                    value = local;
                    return true;
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

        public bool HasLocal(string name)
            => Vars.ContainsKey(name);

        public bool SetValue(string name, Value value)
        {
            for (Env? env = this; env != null; env = env.Parent)
            {
                if (env.Vars.ContainsKey(name))
                {
                    env.Vars[name] = value;
                    return true;
                }
            }

            return false;
        }

        public bool Set(string name, object? value)
            => SetValue(name, Value.FromObject(value));

        public void DefineValue(string name, Value value)
            => Vars[name] = value;

        public void Define(string name, object? value)
            => DefineValue(name, Value.FromObject(value));

        public void DefineConstValue(string name, Value value)
        {
            Vars[name] = value;
            ConstVars.Add(name);
        }

        public void DefineConst(string name, object? value)
            => DefineConstValue(name, Value.FromObject(value));

        public bool IsConstLocal(string name)
            => ConstVars.Contains(name);

        public bool RemoveLocal(string name)
        {
            ConstVars.Remove(name);
            return Vars.Remove(name);
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
            if (!Vars.ContainsKey(name))
                return LocalAssign.NotPresent;
            if (ConstVars.Contains(name))
                return LocalAssign.IsConst;
            Vars[name] = value;
            return LocalAssign.Ok;
        }

        public LocalAssign SetLocalIfNotConst(string name, object? value)
            => SetLocalIfNotConst(name, Value.FromObject(value));
    }
}
