namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Defines the <see cref="Env" />
    /// </summary>
    public class Env
    {
        /// <summary>
        /// Defines the Vars
        /// </summary>
        public Dictionary<string, object> Vars = new();

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
            if (Vars.TryGetValue(name, out value)) return true;
            if (Parent != null) return Parent.TryGetValue(name, out value);
            value = null;
            return false;
        }

        /// <summary>
        /// The HasLocal
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public bool HasLocal(string name) => Vars.ContainsKey(name);

        /// <summary>
        /// The Set
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public bool Set(string name, object value)
        {
            if (Vars.ContainsKey(name))
            {
                Vars[name] = value;
                return true;
            }
            if (Parent != null) return Parent.Set(name, value);
            return false;
        }

        /// <summary>
        /// The Define
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object"/></param>
        public void Define(string name, object value)
        {
            Vars[name] = value;
        }

        /// <summary>
        /// The RemoveLocal
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public bool RemoveLocal(string name) => Vars.Remove(name);
    }
}

