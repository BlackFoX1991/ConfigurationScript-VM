using System;
using System.Collections.Generic;

namespace CFGS_VM.VMCore.Plugin
{
    public sealed class BuiltinRegistry : IBuiltinRegistry
    {
        private readonly Dictionary<string, BuiltinDescriptor> _map = new(StringComparer.Ordinal);

        public void Register(BuiltinDescriptor d)
        {
            if (_map.ContainsKey(d.Name))
                throw new InvalidOperationException($"Duplicate builtin '{d.Name}'.");
            _map[d.Name] = d;
        }

        public bool TryGet(string name, out BuiltinDescriptor d)
            => _map.TryGetValue(name, out d!);
    }
}