using CFGS_VM.Analytic.Semantics;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;
using System.Collections;
using System.Numerics;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        public static object? NormalizeHostValue(object? value)
        {
            switch (value)
            {
                case Value tagged:
                    return tagged.ToObject();

                case null:
                case string:
                case char:
                case bool:
                case sbyte:
                case byte:
                case short:
                case ushort:
                case int:
                case uint:
                case long:
                case ulong:
                case float:
                case double:
                case decimal:
                case BigInteger:
                case DateTime:
                case DateTimeOffset:
                case TimeSpan:
                case Guid:
                case byte[]:
                case ClassInstance:
                case StaticInstance:
                case Delegate:
                    return value;

                case List<object> list:
                    for (int i = 0; i < list.Count; i++)
                        list[i] = NormalizeHostValue(list[i])!;
                    return CaptureMutableCollectionOwnership(list);

                case Dictionary<string, object> dict:
                    foreach (string key in dict.Keys.ToList())
                        dict[key] = NormalizeHostValue(dict[key])!;
                    return CaptureMutableCollectionOwnership(dict);

                case IDictionary dictionary:
                    return CaptureMutableCollectionOwnership(NormalizeHostDictionary(dictionary));

                case IEnumerable enumerable:
                    return CaptureMutableCollectionOwnership(NormalizeHostEnumerable(enumerable));

                default:
                    return value;
            }
        }

        public static List<object> CreateHostArray(params object?[] values)
            => values.Select(static value => NormalizeHostValue(value)!).ToList();

        public static Dictionary<string, object> CreateHostDictionary(IDictionary<string, object?> values)
            => values.ToDictionary(static kv => kv.Key, static kv => NormalizeHostValue(kv.Value)!, StringComparer.Ordinal);

        /// <summary>
        /// Maps a qualified namespace member name such as <c>App.Core.seed</c> to the global binding that
        /// stores the member. Script namespaces exist only at compile time, so a host reaches a member
        /// through its qualified name. Any other name is returned unchanged.
        /// </summary>
        private static string ResolveGlobalBindingName(Env global, string name)
        {
            if (global.HasLocal(name))
                return name;

            int lastDot = name.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == name.Length - 1)
                return name;

            string bindingName = NamespaceRuntimeBindingNames.GetHiddenMemberBindingName(name[..lastDot], name[(lastDot + 1)..]);
            return global.HasLocal(bindingName) ? bindingName : name;
        }

        public bool TryGetGlobal(string name, out object? value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Global name must not be empty.", nameof(name));

            if (_scopes.Count == 0)
            {
                value = null;
                return false;
            }

            Env global = GlobalEnv();
            lock (global.SyncRoot)
            {
                name = ResolveGlobalBindingName(global, name);
                if (global.TryGetLocalValue(name, out Value local))
                {
                    value = ToHostObject(local);
                    return true;
                }
            }

            value = null;
            return false;
        }

        public object? GetGlobal(string name)
        {
            if (TryGetGlobal(name, out object? value))
                return value;

            throw new KeyNotFoundException($"Global variable '{name}' is not defined.");
        }

        public T? GetGlobal<T>(string name)
            => ConvertToHostValue<T>(GetGlobal(name));

        public IReadOnlyDictionary<string, object?> GetGlobals()
        {
            if (_scopes.Count == 0)
                return new Dictionary<string, object?>(StringComparer.Ordinal);

            // Namespace members are listed under their qualified name (App.Core.seed).
            Env global = GlobalEnv();
            lock (global.SyncRoot)
                return global.LocalBindings().ToDictionary(static kv => NamespaceRuntimeBindingNames.ToDisplayName(kv.Key), static kv => ToHostObject(kv.Value), StringComparer.Ordinal);
        }

        public void SetGlobal(string name, object? value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Global name must not be empty.", nameof(name));

            if (_scopes.Count == 0)
                _scopes.Add(new Env(null));

            Env global = GlobalEnv();
            lock (global.SyncRoot)
            {
                string bindingName = ResolveGlobalBindingName(global, name);
                if (global.IsConstLocal(bindingName))
                    throw new InvalidOperationException($"Global variable '{name}' is const and cannot be changed.");

                global.DefineValue(bindingName, ToRuntimeValue(NormalizeHostValue(value)));
            }
        }

        public bool TrySetGlobal(string name, object? value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Global name must not be empty.", nameof(name));

            if (_scopes.Count == 0)
                _scopes.Add(new Env(null));

            Env global = GlobalEnv();
            lock (global.SyncRoot)
            {
                string bindingName = ResolveGlobalBindingName(global, name);
                if (global.IsConstLocal(bindingName))
                    return false;

                global.DefineValue(bindingName, ToRuntimeValue(NormalizeHostValue(value)));
                return true;
            }
        }

        public static T? ConvertToHostValue<T>(object? value)
        {
            if (value is T typed)
                return typed;

            return ConvertHostValue<T>(ToHostObject(value));
        }

        private static Dictionary<string, object> NormalizeHostDictionary(IDictionary dictionary)
        {
            Dictionary<string, object> result = new(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
            {
                string key = Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture)
                    ?? throw new InvalidOperationException("Dictionary key cannot be converted to a CFGS string key.");
                result[key] = NormalizeHostValue(entry.Value)!;
            }

            return result;
        }

        private static List<object> NormalizeHostEnumerable(IEnumerable enumerable)
        {
            List<object> result = [];
            foreach (object? item in enumerable)
                result.Add(NormalizeHostValue(item)!);
            return result;
        }

        private static T? ConvertHostValue<T>(object? value)
        {
            if (value is null)
                return default;

            if (value is T typed)
                return typed;

            Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T?)Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
