using System;
using System.Collections.Generic;
using System.Linq;
using CFGS_VM.VMCore.Extensions.Instance;

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
                _roots[parts[0]] = WrapNamespaceMember(path, instance);
                return;
            }

            // Multi-part native namespace roots are exposed to scripts as distinct namespace values
            // (accessed by dot), not user dictionaries, so member access is not subject to the
            // dictionary bracket-only-for-data rule.
            NamespaceInstance root = GetOrCreateRootNamespace(parts[0]);
            NamespaceInstance current = root;

            for (int i = 1; i < parts.Length - 1; i++)
            {
                string part = parts[i];
                if (!current.Members.TryGetValue(part, out object? next) || next is not NamespaceInstance nested)
                {
                    nested = new NamespaceInstance(string.Join(".", parts.Take(i + 1)));
                    current.Members[part] = nested;
                }

                current = nested;
            }

            current.Members[parts[^1]] = WrapNamespaceMember(path, instance);
        }

        // A plugin may register a namespace whose instance is a dictionary of members; expose it as a
        // distinct namespace value (dot access) rather than a user dictionary, sharing the member map.
        // Non-dictionary instances (native singletons resolved via intrinsics) are exposed unchanged.
        private static object WrapNamespaceMember(string path, object instance)
            => instance is Dictionary<string, object> dictInstance
                ? new NamespaceInstance(path, dictInstance)
                : instance;

        private NamespaceInstance GetOrCreateRootNamespace(string name)
        {
            if (_roots.TryGetValue(name, out object? existing))
            {
                if (existing is NamespaceInstance ns)
                    return ns;

                throw new InvalidOperationException($"Namespace root '{name}' is already registered as a concrete namespace.");
            }

            NamespaceInstance root = new(name);
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
