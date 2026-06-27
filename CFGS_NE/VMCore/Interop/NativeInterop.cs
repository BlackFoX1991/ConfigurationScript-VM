using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions.Instance;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;

namespace CFGS_VM.VMCore.Interop
{
    public sealed record NativeAssemblyReference(string Alias, string Path);

    internal sealed record ClrAssemblyHandle(string Alias, string Path, Assembly Assembly);

    internal sealed record ClrNamespaceHandle(string Alias, string Namespace, Assembly Assembly);

    internal sealed record ClrTypeHandle(Type Type);

    internal sealed record ClrObjectHandle(object Instance)
    {
        public Type Type => Instance.GetType();
    }

    internal sealed record ClrCallable(string Name, Type? DeclaringType, object? Target, MethodBase[] Candidates);

    internal sealed record ClrDelegateHandle(Delegate Delegate);

    internal sealed record ClrEventHandle(EventInfo Event, object? Target);

    internal sealed record ClrEventAccessor(string Name, EventInfo Event, object? Target);

    internal static class NativeInterop
    {
        public static ClrAssemblyHandle LoadAssembly(NativeAssemblyReference reference, string? baseDirectory = null)
        {
            string path = ResolveAssemblyPath(reference.Path, baseDirectory);
            Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            return new ClrAssemblyHandle(reference.Alias, path, assembly);
        }

        public static object? GetMember(object target, string name)
        {
            return target switch
            {
                ClrAssemblyHandle assembly => GetAssemblyMember(assembly, name),
                ClrNamespaceHandle ns => GetNamespaceMember(ns, name),
                ClrTypeHandle type => GetTypeMember(type.Type, name),
                ClrObjectHandle obj => GetObjectMember(obj.Instance, name),
                ClrEventHandle ev => GetEventMember(ev, name),
                _ => throw new InvalidOperationException($"native interop target '{target.GetType().Name}' has no member '{name}'")
            };
        }

        public static object? GetIndex(object target, IReadOnlyList<object> indexes)
        {
            object? instance;
            Type type;
            BindingFlags flags;
            switch (target)
            {
                case ClrTypeHandle typeHandle:
                    instance = null;
                    type = typeHandle.Type;
                    flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
                    break;
                case ClrObjectHandle objectHandle:
                    instance = objectHandle.Instance;
                    type = objectHandle.Type;
                    flags = BindingFlags.Public | BindingFlags.Instance;
                    break;
                default:
                    throw new InvalidOperationException($"native interop target '{target.GetType().Name}' is not indexable");
            }

            PropertyInfo[] indexers = GetIndexerProperties(type, flags, requireSetter: false);
            if (indexers.Length == 0)
                throw new InvalidOperationException($"native type '{type.FullName}' has no readable indexer");

            (PropertyInfo property, object?[] converted) = SelectIndexer(type, indexers, indexes);
            return ConvertFromClr(property.GetValue(instance, converted));
        }

        public static void SetIndex(object target, IReadOnlyList<object> indexes, object? value, VM? vm, Instruction instr)
        {
            object? instance;
            Type type;
            BindingFlags flags;
            switch (target)
            {
                case ClrTypeHandle typeHandle:
                    instance = null;
                    type = typeHandle.Type;
                    flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
                    break;
                case ClrObjectHandle objectHandle:
                    instance = objectHandle.Instance;
                    type = objectHandle.Type;
                    flags = BindingFlags.Public | BindingFlags.Instance;
                    break;
                default:
                    throw new InvalidOperationException($"native interop target '{target.GetType().Name}' is not indexable");
            }

            PropertyInfo[] indexers = GetIndexerProperties(type, flags, requireSetter: true);
            if (indexers.Length == 0)
                throw new InvalidOperationException($"native type '{type.FullName}' has no writable indexer");

            (PropertyInfo property, object?[] converted) = SelectIndexer(type, indexers, indexes);
            object? convertedValue = ConvertToClr(value, property.PropertyType, vm, instr);
            property.SetValue(instance, convertedValue, converted);
        }

        public static void SetMember(object target, string name, object? value, VM? vm, Instruction instr)
        {
            switch (target)
            {
                case ClrTypeHandle type:
                    SetTypeMember(type.Type, null, name, value, vm, instr);
                    return;
                case ClrObjectHandle obj:
                    SetTypeMember(obj.Type, obj.Instance, name, value, vm, instr);
                    return;
                default:
                    throw new InvalidOperationException($"native interop target '{target.GetType().Name}' does not support member assignment");
            }
        }

        public static object? Invoke(object callable, IReadOnlyList<object> args, VM? vm, Instruction instr)
        {
            return callable switch
            {
                ClrCallable clrCallable => InvokeCallable(clrCallable, args, vm, instr),
                ClrDelegateHandle delegateHandle => InvokeDelegate(delegateHandle.Delegate, args, vm, instr),
                ClrEventAccessor eventAccessor => InvokeEventAccessor(eventAccessor, args, vm, instr),
                _ => throw new InvalidOperationException($"native interop target '{callable.GetType().Name}' is not callable")
            };
        }

        public static object? Invoke(object callable, IReadOnlyList<Value> args, VM? vm, Instruction instr)
        {
            List<object> convertedArgs = new(args.Count);
            foreach (Value arg in args)
                convertedArgs.Add(arg.ToObject()!);

            return Invoke(callable, convertedArgs, vm, instr);
        }

        private static object? InvokeCallable(ClrCallable callable, IReadOnlyList<object> args, VM? vm, Instruction instr)
        {
            (MethodBase member, object?[] converted) = SelectCallable(callable, args, vm, instr);
            object? result = member switch
            {
                ConstructorInfo ctor => ctor.Invoke(converted),
                MethodInfo method => method.Invoke(callable.Target, converted),
                _ => throw new InvalidOperationException($"unsupported native callable '{callable.Name}'")
            };

            return ConvertFromClr(result);
        }

        private static object GetAssemblyMember(ClrAssemblyHandle assembly, string name)
        {
            string candidate = name;
            Type? type = assembly.Assembly.GetType(candidate, throwOnError: false, ignoreCase: false);
            if (type is not null)
                return new ClrTypeHandle(type);

            if (HasNamespacePrefix(assembly.Assembly, candidate))
                return new ClrNamespaceHandle(assembly.Alias, candidate, assembly.Assembly);

            throw new InvalidOperationException($"native assembly alias '{assembly.Alias}' has no namespace or type '{name}'");
        }

        private static object GetNamespaceMember(ClrNamespaceHandle ns, string name)
        {
            string candidate = string.IsNullOrWhiteSpace(ns.Namespace) ? name : $"{ns.Namespace}.{name}";
            Type? type = ns.Assembly.GetType(candidate, throwOnError: false, ignoreCase: false);
            if (type is not null)
                return new ClrTypeHandle(type);

            if (HasNamespacePrefix(ns.Assembly, candidate))
                return ns with { Namespace = candidate };

            throw new InvalidOperationException($"native namespace '{ns.Namespace}' has no namespace or type '{name}'");
        }

        private static object? GetTypeMember(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

            if (string.Equals(name, "new", StringComparison.Ordinal))
                return new ClrCallable(name, type, null, type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));

            PropertyInfo? property = type.GetProperty(name, flags);
            if (property is not null && property.GetMethod is not null)
                return ConvertFromClr(property.GetValue(null));

            FieldInfo? field = type.GetField(name, flags);
            if (field is not null)
                return ConvertFromClr(field.GetValue(null));

            EventInfo? ev = type.GetEvent(name, flags);
            if (ev is not null)
                return new ClrEventHandle(ev, null);

            MethodBase[] methods = type.GetMethods(flags)
                .Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                .Cast<MethodBase>()
                .ToArray();
            if (methods.Length > 0)
                return new ClrCallable(name, type, null, methods);

            Type? nestedType = type.GetNestedType(name, BindingFlags.Public);
            if (nestedType is not null)
                return new ClrTypeHandle(nestedType);

            throw new InvalidOperationException($"native type '{type.FullName}' has no static member '{name}'");
        }

        private static object? GetObjectMember(object instance, string name)
        {
            Type type = instance.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            PropertyInfo? property = type.GetProperty(name, flags);
            if (property is not null && property.GetMethod is not null)
                return ConvertFromClr(property.GetValue(instance));

            FieldInfo? field = type.GetField(name, flags);
            if (field is not null)
                return ConvertFromClr(field.GetValue(instance));

            EventInfo? ev = type.GetEvent(name, flags);
            if (ev is not null)
                return new ClrEventHandle(ev, instance);

            MethodBase[] methods = type.GetMethods(flags)
                .Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                .Cast<MethodBase>()
                .ToArray();
            if (methods.Length > 0)
                return new ClrCallable(name, type, instance, methods);

            throw new InvalidOperationException($"native object '{type.FullName}' has no member '{name}'");
        }

        private static object GetEventMember(ClrEventHandle ev, string name)
        {
            if (string.Equals(name, "add", StringComparison.Ordinal))
                return new ClrEventAccessor(name, ev.Event, ev.Target);
            if (string.Equals(name, "remove", StringComparison.Ordinal))
                return new ClrEventAccessor(name, ev.Event, ev.Target);

            throw new InvalidOperationException($"native event '{ev.Event.Name}' has no member '{name}'");
        }

        private static void SetTypeMember(Type type, object? target, string name, object? value, VM? vm, Instruction instr)
        {
            BindingFlags flags = BindingFlags.Public | (target is null ? BindingFlags.Static | BindingFlags.FlattenHierarchy : BindingFlags.Instance);

            PropertyInfo? property = type.GetProperty(name, flags);
            if (property is not null && property.SetMethod is not null)
            {
                property.SetValue(target, ConvertToClr(value, property.PropertyType, vm, instr));
                return;
            }

            FieldInfo? field = type.GetField(name, flags);
            if (field is not null && !field.IsInitOnly)
            {
                field.SetValue(target, ConvertToClr(value, field.FieldType, vm, instr));
                return;
            }

            throw new InvalidOperationException($"native member '{type.FullName}.{name}' is not writable");
        }

        private static object? InvokeDelegate(Delegate callable, IReadOnlyList<object> args, VM? vm, Instruction instr)
        {
            MethodInfo invoke = callable.GetType().GetMethod("Invoke") ?? throw new InvalidOperationException("native delegate has no Invoke method");
            (object?[] converted, int _) = BindParameters(callable.GetType().FullName ?? "delegate", invoke.GetParameters(), args, vm, instr);
            return ConvertFromClr(callable.DynamicInvoke(converted));
        }

        private static object? InvokeEventAccessor(ClrEventAccessor accessor, IReadOnlyList<object> args, VM? vm, Instruction instr)
        {
            if (args.Count != 1)
                throw new InvalidOperationException($"native event '{accessor.Event.Name}.{accessor.Name}' expects exactly one handler");

            Type handlerType = accessor.Event.EventHandlerType
                ?? throw new InvalidOperationException($"native event '{accessor.Event.Name}' has no handler type");
            Delegate handler = (Delegate)ConvertToClr(args[0], handlerType, vm, instr)!;

            if (string.Equals(accessor.Name, "add", StringComparison.Ordinal))
                accessor.Event.AddEventHandler(accessor.Target, handler);
            else
                accessor.Event.RemoveEventHandler(accessor.Target, handler);

            return null;
        }

        private static (MethodBase Member, object?[] Args) SelectCallable(ClrCallable callable, IReadOnlyList<object> args, VM? vm, Instruction instr)
        {
            List<(MethodBase Member, object?[] Args, int Score)> matches = [];
            foreach (MethodBase candidate in callable.Candidates)
            {
                MethodBase closedCandidate = TryCloseGenericMethod(candidate, args, out MethodBase genericClosed)
                    ? genericClosed
                    : candidate;

                if (closedCandidate.ContainsGenericParameters)
                    continue;

                if (TryBindParameters(callable.Name, closedCandidate.GetParameters(), args, vm, instr, out object?[] converted, out int score))
                    matches.Add((closedCandidate, converted, score));
            }

            if (matches.Count == 0)
                throw new InvalidOperationException($"no matching native overload for '{callable.Name}' with {args.Count} argument(s)");

            int bestScore = matches.Min(static m => m.Score);
            List<(MethodBase Member, object?[] Args, int Score)> best = matches.Where(m => m.Score == bestScore).ToList();
            if (best.Count > 1)
                throw new InvalidOperationException($"ambiguous native overload for '{callable.Name}' with {args.Count} argument(s)");

            return (best[0].Member, best[0].Args);
        }

        private static bool TryBindParameters(
            string callableName,
            ParameterInfo[] parameters,
            IReadOnlyList<object> args,
            VM? vm,
            Instruction instr,
            out object?[] converted,
            out int score)
        {
            try
            {
                (converted, score) = BindParameters(callableName, parameters, args, vm, instr);
                return true;
            }
            catch
            {
                converted = [];
                score = 0;
                return false;
            }
        }

        private static (object?[] Args, int Score) BindParameters(
            string callableName,
            ParameterInfo[] parameters,
            IReadOnlyList<object> args,
            VM? vm,
            Instruction instr)
        {
            SplitArguments(args, out List<object?> positional, out Dictionary<string, object?> named);

            int paramsIndex = GetParamsArrayIndex(parameters);
            int fixedCount = paramsIndex >= 0 ? paramsIndex : parameters.Length;
            if (positional.Count > parameters.Length && paramsIndex < 0)
                throw new InvalidOperationException($"too many positional arguments for native call '{callableName}'");

            object?[] converted = new object?[parameters.Length];
            bool[] assigned = new bool[parameters.Length];
            int score = 0;

            int positionalFixedCount = Math.Min(positional.Count, fixedCount);
            for (int i = 0; i < positionalFixedCount; i++)
            {
                converted[i] = ConvertToClr(positional[i], parameters[i].ParameterType, vm, instr, out int argScore);
                assigned[i] = true;
                score += argScore;
            }

            Dictionary<string, int> parameterByName = new(StringComparer.Ordinal);
            for (int i = 0; i < parameters.Length; i++)
                parameterByName[parameters[i].Name ?? string.Empty] = i;

            foreach (KeyValuePair<string, object?> kv in named)
            {
                if (!parameterByName.TryGetValue(kv.Key, out int index))
                    throw new InvalidOperationException($"unknown native named argument '{kv.Key}'");
                if (index == paramsIndex)
                    throw new InvalidOperationException($"native params array '{kv.Key}' cannot be passed as a named argument");
                if (assigned[index])
                    throw new InvalidOperationException($"native argument '{kv.Key}' was provided multiple times");

                converted[index] = ConvertToClr(kv.Value, parameters[index].ParameterType, vm, instr, out int argScore);
                assigned[index] = true;
                score += argScore;
            }

            for (int i = 0; i < fixedCount; i++)
            {
                if (assigned[i])
                    continue;

                if (TryGetDefaultParameterValue(parameters[i], out object? defaultValue))
                {
                    converted[i] = defaultValue;
                    score += 4;
                    continue;
                }

                throw new InvalidOperationException($"missing native argument '{parameters[i].Name}'");
            }

            if (paramsIndex >= 0)
            {
                ParameterInfo paramsParameter = parameters[paramsIndex];
                Type arrayType = paramsParameter.ParameterType;
                Type elementType = arrayType.GetElementType() ?? typeof(object);
                int restCount = Math.Max(0, positional.Count - fixedCount);
                Array restArray = Array.CreateInstance(elementType, restCount);
                for (int i = 0; i < restCount; i++)
                {
                    object? element = ConvertToClr(positional[fixedCount + i], elementType, vm, instr, out int argScore);
                    restArray.SetValue(element, i);
                    score += argScore + 1;
                }

                converted[paramsIndex] = restArray;
            }

            return (converted, score);
        }

        private static void SplitArguments(
            IReadOnlyList<object> args,
            out List<object?> positional,
            out Dictionary<string, object?> named)
        {
            positional = [];
            named = new Dictionary<string, object?>(StringComparer.Ordinal);
            bool sawNamed = false;

            foreach (object arg in args)
            {
                if (arg is NamedArgument namedArgument)
                {
                    sawNamed = true;
                    if (named.ContainsKey(namedArgument.Name))
                        throw new InvalidOperationException($"duplicate native named argument '{namedArgument.Name}'");
                    named[namedArgument.Name] = namedArgument.Value;
                    continue;
                }

                if (sawNamed)
                    throw new InvalidOperationException("positional native argument cannot follow named arguments");
                positional.Add(arg);
            }
        }

        private static int GetParamsArrayIndex(ParameterInfo[] parameters)
        {
            if (parameters.Length == 0)
                return -1;

            int last = parameters.Length - 1;
            return parameters[last].GetCustomAttribute<ParamArrayAttribute>() is not null &&
                   parameters[last].ParameterType.IsArray
                ? last
                : -1;
        }

        private static bool TryGetDefaultParameterValue(ParameterInfo parameter, out object? value)
        {
            if (!parameter.HasDefaultValue && !parameter.IsOptional)
            {
                value = null;
                return false;
            }

            value = parameter.DefaultValue;
            if (value == DBNull.Value || value == Type.Missing)
                value = parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
            return true;
        }

        private static bool TryCloseGenericMethod(MethodBase candidate, IReadOnlyList<object> args, out MethodBase closed)
        {
            closed = candidate;
            if (candidate is not MethodInfo method || !method.IsGenericMethodDefinition)
                return false;

            Type[] genericArguments = method.GetGenericArguments();
            Dictionary<string, Type> inferred = new(StringComparer.Ordinal);
            SplitArguments(args, out List<object?> positional, out Dictionary<string, object?> named);
            ParameterInfo[] parameters = method.GetParameters();
            int paramsIndex = GetParamsArrayIndex(parameters);
            int fixedCount = paramsIndex >= 0 ? paramsIndex : parameters.Length;

            int positionalFixedCount = Math.Min(positional.Count, fixedCount);
            for (int i = 0; i < positionalFixedCount; i++)
                InferGenericArgument(parameters[i].ParameterType, positional[i], inferred);

            Dictionary<string, ParameterInfo> byName = parameters
                .Where(static p => p.Name is not null)
                .ToDictionary(static p => p.Name!, StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> kv in named)
            {
                if (byName.TryGetValue(kv.Key, out ParameterInfo? parameter))
                    InferGenericArgument(parameter.ParameterType, kv.Value, inferred);
            }

            if (paramsIndex >= 0)
            {
                Type elementType = parameters[paramsIndex].ParameterType.GetElementType() ?? typeof(object);
                for (int i = fixedCount; i < positional.Count; i++)
                    InferGenericArgument(elementType, positional[i], inferred);
            }

            Type[] closedArguments = new Type[genericArguments.Length];
            for (int i = 0; i < genericArguments.Length; i++)
            {
                if (!inferred.TryGetValue(genericArguments[i].Name, out Type? inferredType))
                    return false;
                closedArguments[i] = inferredType;
            }

            try
            {
                closed = method.MakeGenericMethod(closedArguments);
                return true;
            }
            catch (ArgumentException)
            {
                closed = candidate;
                return false;
            }
        }

        private static void InferGenericArgument(Type parameterType, object? value, Dictionary<string, Type> inferred)
        {
            value = UnwrapHandle(value);
            if (value is null)
                return;

            if (parameterType.IsGenericParameter)
            {
                inferred.TryAdd(parameterType.Name, value.GetType());
                return;
            }

            if (parameterType.IsArray && parameterType.GetElementType() is { IsGenericParameter: true } elementType && value is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    object? unwrapped = UnwrapHandle(item);
                    if (unwrapped is not null)
                    {
                        inferred.TryAdd(elementType.Name, unwrapped.GetType());
                        return;
                    }
                }
            }
        }

        private static PropertyInfo[] GetIndexerProperties(Type type, BindingFlags flags, bool requireSetter)
            => type.GetProperties(flags)
                .Where(p => p.GetIndexParameters().Length > 0)
                .Where(p => requireSetter ? p.SetMethod is not null : p.GetMethod is not null)
                .ToArray();

        private static (PropertyInfo Property, object?[] Args) SelectIndexer(Type type, PropertyInfo[] indexers, IReadOnlyList<object> indexes)
        {
            List<(PropertyInfo Property, object?[] Args, int Score)> matches = [];
            foreach (PropertyInfo indexer in indexers)
            {
                ParameterInfo[] parameters = indexer.GetIndexParameters();
                if (parameters.Length != indexes.Count)
                    continue;

                object?[] converted = new object?[indexes.Count];
                int score = 0;
                bool ok = true;
                for (int i = 0; i < indexes.Count; i++)
                {
                    if (!TryConvertToClr(indexes[i], parameters[i].ParameterType, null, default!, out object? convertedArg, out int argScore))
                    {
                        ok = false;
                        break;
                    }

                    converted[i] = convertedArg;
                    score += argScore;
                }

                if (ok)
                    matches.Add((indexer, converted, score));
            }

            if (matches.Count == 0)
                throw new InvalidOperationException($"no matching native indexer on '{type.FullName}' with {indexes.Count} index argument(s)");

            int bestScore = matches.Min(static m => m.Score);
            List<(PropertyInfo Property, object?[] Args, int Score)> best = matches.Where(m => m.Score == bestScore).ToList();
            if (best.Count > 1)
                throw new InvalidOperationException($"ambiguous native indexer on '{type.FullName}' with {indexes.Count} index argument(s)");

            return (best[0].Property, best[0].Args);
        }

        /// <summary>
        /// Upper bound on conversion recursion depth, guarding against pathologically deep or
        /// (for <see cref="ConvertFromClr(object?)"/>) cyclic value graphs.
        /// </summary>
        private const int MaxInteropConversionDepth = 128;

        private static bool TryConvertToClr(object? value, Type targetType, VM? vm, Instruction instr, out object? converted, out int score)
            => TryConvertToClr(value, targetType, vm, instr, 0, out converted, out score);

        private static bool TryConvertToClr(object? value, Type targetType, VM? vm, Instruction instr, int depth, out object? converted, out int score)
        {
            converted = null;
            score = 0;

            Type nonNullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
            value = UnwrapHandle(value);

            if (value is null)
            {
                if (!nonNullableTarget.IsValueType || Nullable.GetUnderlyingType(targetType) is not null)
                {
                    converted = null;
                    return true;
                }

                return false;
            }

            Type valueType = value.GetType();
            if (nonNullableTarget.IsAssignableFrom(valueType))
            {
                converted = value;
                return true;
            }

            try
            {
                if (typeof(Delegate).IsAssignableFrom(nonNullableTarget))
                {
                    if (value is Delegate del && nonNullableTarget.IsAssignableFrom(del.GetType()))
                    {
                        converted = del;
                        return true;
                    }

                    if (value is Closure closure)
                    {
                        if (vm is null)
                            return false;
                        converted = CreateClosureDelegate(nonNullableTarget, closure, vm, instr, receiver: null, accessType: null);
                        score = 3;
                        return true;
                    }

                    if (value is BoundMethod boundMethod)
                    {
                        if (vm is null)
                            return false;
                        converted = CreateClosureDelegate(nonNullableTarget, boundMethod.Function, vm, instr, boundMethod.Receiver, boundMethod.DeclaringType);
                        score = 3;
                        return true;
                    }
                }

                if (nonNullableTarget.IsEnum)
                {
                    converted = value is string s
                        ? Enum.Parse(nonNullableTarget, s, ignoreCase: false)
                        : Enum.ToObject(nonNullableTarget, value);
                    score = 2;
                    return true;
                }

                if (nonNullableTarget == typeof(string))
                {
                    converted = value.ToString();
                    score = value is string ? 0 : 3;
                    return true;
                }

                // BigInteger target: accept BigInteger or any CFGS integral value (B4).
                if (nonNullableTarget == typeof(System.Numerics.BigInteger))
                {
                    converted = value switch
                    {
                        System.Numerics.BigInteger b => b,
                        sbyte or byte or short or ushort or int or uint or long =>
                            new System.Numerics.BigInteger(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)),
                        ulong ul => new System.Numerics.BigInteger(ul),
                        _ => null
                    };

                    if (converted is not null)
                    {
                        score = value is System.Numerics.BigInteger ? 0 : 1;
                        return true;
                    }

                    return false;
                }

                // BigInteger value into a native numeric parameter: BigInteger is not IConvertible,
                // so Convert.ChangeType cannot handle it. Use range-checked explicit casts (B4).
                if (value is System.Numerics.BigInteger bigValue && IsNumericType(nonNullableTarget))
                {
                    if (TryConvertBigInteger(bigValue, nonNullableTarget, out converted))
                    {
                        score = 1;
                        return true;
                    }

                    return false;
                }

                if (IsNumericType(nonNullableTarget) || nonNullableTarget == typeof(bool) || nonNullableTarget == typeof(char))
                {
                    converted = Convert.ChangeType(value, nonNullableTarget, System.Globalization.CultureInfo.InvariantCulture);
                    score = valueType == nonNullableTarget ? 0 : 1;
                    return true;
                }

                // CFGS array into a native array or generic collection (List<T>, IEnumerable<T>, … — B5).
                if (value is List<object> list && TryGetClrCollectionElementType(nonNullableTarget, out Type elementType, out bool targetIsArray))
                {
                    // Recursion here is bounded by the target type's nesting depth, but guard anyway.
                    if (depth >= MaxInteropConversionDepth)
                        return false;

                    if (targetIsArray)
                    {
                        Array array = Array.CreateInstance(elementType, list.Count);
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (!TryConvertToClr(list[i], elementType, vm, instr, depth + 1, out object? element, out int elementScore))
                                return false;
                            array.SetValue(element, i);
                            score += elementScore + 1;
                        }

                        converted = array;
                        return true;
                    }

                    Type listType = typeof(List<>).MakeGenericType(elementType);
                    System.Collections.IList typed = (System.Collections.IList)Activator.CreateInstance(listType)!;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (!TryConvertToClr(list[i], elementType, vm, instr, depth + 1, out object? element, out int elementScore))
                            return false;
                        typed.Add(element);
                        score += elementScore + 1;
                    }

                    converted = typed;
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool TryConvertBigInteger(System.Numerics.BigInteger value, Type target, out object? converted)
        {
            converted = null;
            try
            {
                if (target == typeof(long)) converted = (long)value;
                else if (target == typeof(ulong)) converted = (ulong)value;
                else if (target == typeof(int)) converted = (int)value;
                else if (target == typeof(uint)) converted = (uint)value;
                else if (target == typeof(short)) converted = (short)value;
                else if (target == typeof(ushort)) converted = (ushort)value;
                else if (target == typeof(byte)) converted = (byte)value;
                else if (target == typeof(sbyte)) converted = (sbyte)value;
                else if (target == typeof(double)) converted = (double)value;
                else if (target == typeof(float)) converted = (float)value;
                else if (target == typeof(decimal)) converted = (decimal)value;
                else return false;

                return true;
            }
            catch (OverflowException)
            {
                converted = null;
                return false;
            }
        }

        private static bool TryGetClrCollectionElementType(Type target, out Type elementType, out bool isArray)
        {
            if (target.IsArray)
            {
                elementType = target.GetElementType() ?? typeof(object);
                isArray = true;
                return true;
            }

            isArray = false;
            if (target.IsGenericType)
            {
                Type definition = target.GetGenericTypeDefinition();
                if (definition == typeof(List<>) ||
                    definition == typeof(IList<>) ||
                    definition == typeof(ICollection<>) ||
                    definition == typeof(IEnumerable<>) ||
                    definition == typeof(IReadOnlyList<>) ||
                    definition == typeof(IReadOnlyCollection<>))
                {
                    Type arg = target.GetGenericArguments()[0];
                    if (target.IsAssignableFrom(typeof(List<>).MakeGenericType(arg)))
                    {
                        elementType = arg;
                        return true;
                    }
                }
            }

            elementType = typeof(object);
            return false;
        }

        private static object? ConvertToClr(object? value, Type targetType, VM? vm, Instruction instr)
            => ConvertToClr(value, targetType, vm, instr, out _);

        private static object? ConvertToClr(object? value, Type targetType, VM? vm, Instruction instr, out int score)
        {
            if (TryConvertToClr(value, targetType, vm, instr, out object? converted, out score))
                return converted;

            throw new InvalidOperationException($"cannot convert CFGS value to native type '{targetType.FullName}'");
        }

        private static object? ConvertFromClr(object? value)
            => ConvertFromClr(value, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));

        private static object? ConvertFromClr(object? value, int depth, HashSet<object> visited)
        {
            if (value is null)
                return null;

            Type type = value.GetType();
            if (type.IsPrimitive || value is string or decimal or System.Numerics.BigInteger)
                return value;

            if (value is Delegate del)
                return new ClrDelegateHandle(del);

            if (value is Task task)
                return ConvertTaskFromClr(task);

            // The recursion below follows the runtime value graph with no type guidance, so a
            // self-referencing CLR collection would otherwise recurse forever into an unrecoverable
            // StackOverflowException. Guard against cycles and pathological depth (B3).
            if (value is IDictionary dictionary)
            {
                EnterClrConversion(value, depth, visited);
                try
                {
                    Dictionary<string, object> result = new(StringComparer.Ordinal);
                    foreach (DictionaryEntry entry in dictionary)
                        result[entry.Key?.ToString() ?? string.Empty] = ConvertFromClr(entry.Value, depth + 1, visited)!;
                    return result;
                }
                finally
                {
                    visited.Remove(value);
                }
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                EnterClrConversion(value, depth, visited);
                try
                {
                    List<object> result = [];
                    foreach (object? item in enumerable)
                        result.Add(ConvertFromClr(item, depth + 1, visited)!);
                    return result;
                }
                finally
                {
                    visited.Remove(value);
                }
            }

            return new ClrObjectHandle(value);
        }

        private static void EnterClrConversion(object value, int depth, HashSet<object> visited)
        {
            if (depth >= MaxInteropConversionDepth)
                throw new InvalidOperationException($"native interop conversion exceeded the maximum depth of {MaxInteropConversionDepth}");

            if (!visited.Add(value))
                throw new InvalidOperationException("native interop conversion detected a cyclic value graph");
        }

        private static Task<object?> ConvertTaskFromClr(Task task)
            => AwaitClrTaskAsync(task);

        private static async Task<object?> AwaitClrTaskAsync(Task task)
        {
            await task.ConfigureAwait(false);
            Type taskType = task.GetType();
            if (taskType.IsGenericType)
            {
                PropertyInfo? resultProperty = taskType.GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
                if (resultProperty is not null)
                    return ConvertFromClr(resultProperty.GetValue(task));
            }

            return null;
        }

        private static object? UnwrapHandle(object? value)
            => value is ClrObjectHandle obj ? obj.Instance :
               value is ClrTypeHandle type ? type.Type :
               value is ClrDelegateHandle del ? del.Delegate :
               value;

        private static Delegate CreateClosureDelegate(
            Type delegateType,
            Closure closure,
            VM vm,
            Instruction instr,
            object? receiver,
            StaticInstance? accessType)
        {
            return vm.NativeClosureDelegateCache.GetOrAdd(closure, receiver, delegateType, () =>
            {
                MethodInfo invoke = delegateType.GetMethod("Invoke")
                    ?? throw new InvalidOperationException($"delegate type '{delegateType.FullName}' has no Invoke method");
                ParameterInfo[] invokeParameters = invoke.GetParameters();
                ParameterExpression[] lambdaParameters = invokeParameters
                    .Select(p => Expression.Parameter(p.ParameterType, p.Name ?? "arg"))
                    .ToArray();
                Expression[] boxed = lambdaParameters
                    .Select(p => Expression.Convert(p, typeof(object)))
                    .ToArray();

                MethodInfo helper = typeof(NativeInterop).GetMethod(nameof(InvokeClosureDelegate), BindingFlags.NonPublic | BindingFlags.Static)
                    ?? throw new InvalidOperationException("missing native closure delegate helper");
                Expression helperCall = Expression.Call(
                    helper,
                    Expression.Constant(vm),
                    Expression.Constant(closure),
                    Expression.Constant(instr),
                    Expression.Constant(receiver, typeof(object)),
                    Expression.Constant(accessType, typeof(StaticInstance)),
                    Expression.NewArrayInit(typeof(object), boxed),
                    Expression.Constant(invoke.ReturnType, typeof(Type)));

                Expression body = invoke.ReturnType == typeof(void)
                    ? Expression.Block(helperCall, Expression.Empty())
                    : Expression.Convert(helperCall, invoke.ReturnType);

                return Expression.Lambda(delegateType, body, lambdaParameters).Compile();
            });
        }

        private static object? InvokeClosureDelegate(
            VM vm,
            Closure closure,
            Instruction instr,
            object? receiver,
            StaticInstance? accessType,
            object?[] args,
            Type returnType)
        {
            List<Value> convertedArgs = args
                .Select(static arg => Value.FromObject(ConvertFromClr(arg)))
                .ToList();
            Value result = vm.InvokeClosureSyncValue(closure, convertedArgs, instr, receiver, accessType);
            if (returnType == typeof(void))
                return null;

            return ConvertToClr(result.ToObject(), returnType, vm, instr);
        }

        private static bool IsNumericType(Type type)
            => type == typeof(byte) ||
               type == typeof(sbyte) ||
               type == typeof(short) ||
               type == typeof(ushort) ||
               type == typeof(int) ||
               type == typeof(uint) ||
               type == typeof(long) ||
               type == typeof(ulong) ||
               type == typeof(float) ||
               type == typeof(double) ||
               type == typeof(decimal);

        private static bool HasNamespacePrefix(Assembly assembly, string prefix)
        {
            string dottedPrefix = prefix + ".";
            return assembly.GetExportedTypes().Any(t =>
                t.FullName is not null &&
                t.FullName.StartsWith(dottedPrefix, StringComparison.Ordinal));
        }

        private static string ResolveAssemblyPath(string path, string? baseDirectory)
        {
            string expanded = Environment.ExpandEnvironmentVariables(path.Trim());
            if (Path.IsPathFullyQualified(expanded))
                return Path.GetFullPath(expanded);

            List<string> candidates = [];
            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                candidates.Add(Path.Combine(baseDirectory, expanded));
                candidates.Add(Path.Combine(baseDirectory, ".cfgs", "plugins", expanded));
                candidates.Add(Path.Combine(baseDirectory, "plugins", expanded));
            }

            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), expanded));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), ".cfgs", "plugins", expanded));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, expanded));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "plugins", expanded));

            foreach (string candidate in candidates)
            {
                string full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                    return full;
            }

            return Path.GetFullPath(expanded);
        }
    }
}
