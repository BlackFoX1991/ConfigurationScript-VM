using System;
using System.Collections.Generic;

namespace CFGS_VM.VMCore.Plugin
{
    public sealed class IntrinsicRegistry : IIntrinsicRegistry
    {
        private readonly Dictionary<Type, Dictionary<string, IntrinsicDescriptor>> _map = new();

        public void Register(Type receiverType, IntrinsicDescriptor d)
        {
            if (!_map.TryGetValue(receiverType, out var bucket))
            {
                bucket = new Dictionary<string, IntrinsicDescriptor>(StringComparer.Ordinal);
                _map[receiverType] = bucket;
            }
            if (bucket.ContainsKey(d.Name))
                throw new InvalidOperationException($"Duplicate intrinsic '{d.Name}' for {receiverType.Name}.");
            bucket[d.Name] = d;
        }

        public void Register(Type receiverType, IEnumerable<IntrinsicDescriptor> ds)
        {
            foreach (var d in ds) Register(receiverType, d);
        }

        public bool TryGet(Type receiverType, string name, out IntrinsicDescriptor d)
        {
            // Exact type match first
            if (_map.TryGetValue(receiverType, out var bucket) && bucket.TryGetValue(name, out d!))
                return true;

            // Walk up the inheritance chain/interfaces (e.g., custom wrappers)
            var t = receiverType.BaseType;
            while (t != null)
            {
                if (_map.TryGetValue(t, out bucket) && bucket.TryGetValue(name, out d!))
                    return true;
                t = t.BaseType;
            }

            foreach (var iface in receiverType.GetInterfaces())
            {
                if (_map.TryGetValue(iface, out bucket) && bucket.TryGetValue(name, out d!))
                    return true;
            }

            d = null!;
            return false;
        }
    }
}