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

    public class Env
    {
        public readonly object SyncRoot = new();

        public Dictionary<string, object> Vars = new();

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

        public bool TryGetValue(string name, out object? value)
        {
            Env? parent;
            lock (SyncRoot)
            {
                if (Vars.TryGetValue(name, out object? local))
                {
                    value = local;
                    return true;
                }
                parent = Parent;
            }
            if (parent != null) return parent.TryGetValue(name, out value);
            value = null;
            return false;
        }

        public bool HasLocal(string name)
        {
            lock (SyncRoot)
                return Vars.ContainsKey(name);
        }

        public bool Set(string name, object value)
        {
            Env? parent;
            lock (SyncRoot)
            {
                if (Vars.ContainsKey(name))
                {
                    Vars[name] = value;
                    return true;
                }
                parent = Parent;
            }
            if (parent != null) return parent.Set(name, value);
            return false;
        }

        public void Define(string name, object value)
        {
            lock (SyncRoot)
                Vars[name] = value;
        }

        public void DefineConst(string name, object value)
        {
            lock (SyncRoot)
            {
                Vars[name] = value;
                ConstVars.Add(name);
            }
        }

        public bool IsConstLocal(string name)
        {
            lock (SyncRoot)
                return ConstVars.Contains(name);
        }

        public bool RemoveLocal(string name)
        {
            lock (SyncRoot)
            {
                ConstVars.Remove(name);
                return Vars.Remove(name);
            }
        }

        /// <summary>
        /// Atomically updates an existing local in this environment, honoring const-ness. The
        /// existence check, const check, and write all happen under a single lock so a concurrent
        /// remove or const declaration cannot be observed in a torn state between them.
        /// </summary>
        /// <param name="name">The local name.</param>
        /// <param name="value">The new value.</param>
        /// <returns>The <see cref="LocalAssign"/> outcome.</returns>
        public LocalAssign SetLocalIfNotConst(string name, object value)
        {
            lock (SyncRoot)
            {
                if (!Vars.ContainsKey(name))
                    return LocalAssign.NotPresent;
                if (ConstVars.Contains(name))
                    return LocalAssign.IsConst;
                Vars[name] = value;
                return LocalAssign.Ok;
            }
        }
    }
}
