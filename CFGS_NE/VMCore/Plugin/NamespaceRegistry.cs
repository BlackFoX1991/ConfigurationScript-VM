using System;
using System.Collections.Generic;
using System.Linq;

namespace CFGS_VM.VMCore.Plugin
{
    public sealed class NamespaceRegistry : INamespaceRegistry
    {
        private readonly Dictionary<string, NamespaceDescriptor> _map = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object> _roots = new(StringComparer.Ordinal);

        public void Register(NamespaceDescriptor descriptor)
        {
            if (_map.ContainsKey(descriptor.Path))
                throw new InvalidOperationException($"Duplicate namespace '{descriptor.Path}'.");

            _map[descriptor.Path] = descriptor;
            AddRootPath(descriptor.Path, descriptor.Instance);
        }

        public bool TryGet(string path, out NamespaceDescriptor descriptor)
            => _map.TryGetValue(path, out descriptor!);

        public bool Contains(string path)
            => _map.ContainsKey(path);

        public bool TryGetRoot(string name, out object value)
            => _roots.TryGetValue(name, out value!);

        public bool Remove(string path)
        {
            bool removed = _map.Remove(path);
            if (removed)
                RebuildRoots();
            return removed;
        }

        public IReadOnlyList<NamespaceDescriptor> Snapshot()
            => _map.Values.ToList();

        private void AddRootPath(string path, object instance)
        {
            string[] parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                _roots[parts[0]] = instance;
                return;
            }

            Dictionary<string, object> root = GetOrCreateRootDictionary(parts[0]);
            Dictionary<string, object> current = root;

            for (int i = 1; i < parts.Length - 1; i++)
            {
                string part = parts[i];
                if (!current.TryGetValue(part, out object? next) || next is not Dictionary<string, object> nested)
                {
                    nested = new Dictionary<string, object>(StringComparer.Ordinal);
                    current[part] = nested;
                }

                current = nested;
            }

            current[parts[^1]] = instance;
        }

        private Dictionary<string, object> GetOrCreateRootDictionary(string name)
        {
            if (_roots.TryGetValue(name, out object? existing))
            {
                if (existing is Dictionary<string, object> dict)
                    return dict;

                throw new InvalidOperationException($"Namespace root '{name}' is already registered as a concrete namespace.");
            }

            Dictionary<string, object> root = new(StringComparer.Ordinal);
            _roots[name] = root;
            return root;
        }

        private void RebuildRoots()
        {
            _roots.Clear();
            foreach (NamespaceDescriptor descriptor in _map.Values)
                AddRootPath(descriptor.Path, descriptor.Instance);
        }
    }
}
