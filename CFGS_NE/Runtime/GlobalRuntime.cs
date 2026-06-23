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

        public bool TryGetGlobal(string name, out object? value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Global name must not be empty.", nameof(name));

            if (_scopes.Count == 0)
            {
                value = null;
                return false;
            }

            Env global = _scopes[0];
            lock (global.SyncRoot)
            {
                if (global.Vars.TryGetValue(name, out Value local))
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

            Env global = _scopes[0];
            lock (global.SyncRoot)
                return global.Vars.ToDictionary(static kv => kv.Key, static kv => ToHostObject(kv.Value), StringComparer.Ordinal);
        }

        public void SetGlobal(string name, object? value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Global name must not be empty.", nameof(name));

            if (_scopes.Count == 0)
                _scopes.Add(new Env(null));

            Env global = _scopes[0];
            lock (global.SyncRoot)
            {
                if (global.ConstVars.Contains(name))
                    throw new InvalidOperationException($"Global variable '{name}' is const and cannot be changed.");

                global.Vars[name] = ToRuntimeValue(NormalizeHostValue(value));
            }
        }

        public bool TrySetGlobal(string name, object? value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Global name must not be empty.", nameof(name));

            if (_scopes.Count == 0)
                _scopes.Add(new Env(null));

            Env global = _scopes[0];
            lock (global.SyncRoot)
            {
                if (global.ConstVars.Contains(name))
                    return false;

                global.Vars[name] = ToRuntimeValue(NormalizeHostValue(value));
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
