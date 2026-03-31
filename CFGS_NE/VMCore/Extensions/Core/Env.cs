namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Defines the <see cref="Env" />
    /// </summary>
    public class Env
    {
        /// <summary>
        /// Defines the SyncRoot
        /// </summary>
        public readonly object SyncRoot = new();

        /// <summary>
        /// Coordinates serialized async execution for closures that share this environment root.
        /// </summary>
        public readonly AsyncExecutionCoordinator AsyncCoordinator = new();

        /// <summary>
        /// Defines the Vars
        /// </summary>
        public Dictionary<string, object> Vars = new();

        /// <summary>
        /// Defines the ConstVars
        /// </summary>
        public HashSet<string> ConstVars = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines the Parent
        /// </summary>
        public Env? Parent;

        /// <summary>
        /// Initializes a new instance of the <see cref="Env"/> class.
        /// </summary>
        /// <param name="parent">The parent<see cref="Env?"/></param>
        public Env(Env? parent)
        {
            Parent = parent;
        }

        /// <summary>
        /// The TryGetValue
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object?"/></param>
        /// <returns>The <see cref="bool"/></returns>
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

        /// <summary>
        /// The HasLocal
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public bool HasLocal(string name)
        {
            lock (SyncRoot)
                return Vars.ContainsKey(name);
        }

        /// <summary>
        /// The Set
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
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

        /// <summary>
        /// The Define
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object"/></param>
        public void Define(string name, object value)
        {
            lock (SyncRoot)
                Vars[name] = value;
        }

        /// <summary>
        /// The DefineConst
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object"/></param>
        public void DefineConst(string name, object value)
        {
            lock (SyncRoot)
            {
                Vars[name] = value;
                ConstVars.Add(name);
            }
        }

        /// <summary>
        /// The IsConstLocal
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public bool IsConstLocal(string name)
        {
            lock (SyncRoot)
                return ConstVars.Contains(name);
        }

        /// <summary>
        /// The RemoveLocal
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public bool RemoveLocal(string name)
        {
            lock (SyncRoot)
            {
                ConstVars.Remove(name);
                return Vars.Remove(name);
            }
        }
    }

    /// <summary>
    /// Serializes async CFGS execution for a shared environment root.
    /// </summary>
    public sealed class AsyncExecutionCoordinator
    {
        /// <summary>
        /// Guards entry into serialized async execution.
        /// </summary>
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
}
