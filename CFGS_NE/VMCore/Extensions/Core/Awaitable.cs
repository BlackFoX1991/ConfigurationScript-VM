namespace CFGS_VM.VMCore.Extensions.Core
{
    public interface IAwaitable
    {
        Task<object?> AsTask();
    }

    internal static class AwaitableAdapter
    {
        public static bool TryGetTask(object? v, out Task<object?> task)
        {
            if (v is not null && IsGenericValueTaskType(v.GetType()))
            {
                task = AwaitGenericValueTaskAsync(v);
                return true;
            }

            switch (v)
            {
                case Task<object?> tObj:
                    task = tObj;
                    return true;

                case Task t:
                    if (IsGenericTaskType(t.GetType()))
                        task = AwaitGenericTaskAsync(t);
                    else
                        task = AwaitNonGenericTaskAsync(t);
                    return true;

                case ValueTask<object?> vtObj:
                    task = vtObj.AsTask();
                    return true;

                case ValueTask vt:
                    task = AwaitNonGenericValueTaskAsync(vt);
                    return true;

                case IAwaitable aw:
                    task = aw.AsTask();
                    return true;

                case List<object> list when ContainsAwaitable(list):
                    task = AwaitListAsync(list);
                    return true;

                case Dictionary<string, object> dict when ContainsAwaitable(dict.Values):
                    task = AwaitDictionaryAsync(dict);
                    return true;

                default:
                    task = null!;
                    return false;
            }
        }

        public static bool TryGetDirectTask(object? value, out Task<object?> task)
        {
            if (!IsDirectAwaitable(value))
            {
                task = null!;
                return false;
            }

            return TryGetTask(value, out task);
        }

        private static Task<object?> ToTask(object? value)
        {
            if (TryGetTask(value, out Task<object?> awaited))
                return awaited;

            return Task.FromResult(value);
        }

        private static async Task<object?> AwaitNonGenericTaskAsync(Task task)
        {
            await task.ConfigureAwait(false);
            return null;
        }

        private static async Task<object?> AwaitGenericTaskAsync(Task task)
        {
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }

        private static async Task<object?> AwaitNonGenericValueTaskAsync(ValueTask task)
        {
            await task.ConfigureAwait(false);
            return null;
        }

        private static Task<object?> AwaitGenericValueTaskAsync(object valueTask)
        {
            object? taskObj = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)?.Invoke(valueTask, null);
            if (taskObj is not Task task)
                throw new InvalidOperationException("Runtime error: failed to adapt ValueTask<T> to Task");

            if (IsGenericTaskType(task.GetType()))
                return AwaitGenericTaskAsync(task);

            return AwaitNonGenericTaskAsync(task);
        }

        private static bool IsGenericTaskType(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>);

        private static bool IsGenericValueTaskType(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);

        private static bool IsDirectAwaitable(object? item)
        {
            if (item is null)
                return false;

            if (item is Task or ValueTask or IAwaitable)
                return true;

            Type itemType = item.GetType();
            return typeof(Task).IsAssignableFrom(itemType) || IsGenericValueTaskType(itemType);
        }

        private static bool ContainsAwaitable(IEnumerable<object> values)
        {
            foreach (object item in values)
            {
                if (IsDirectAwaitable(item))
                    return true;

                if (item is List<object> nestedList && ContainsAwaitable(nestedList))
                    return true;

                if (item is Dictionary<string, object> nestedDict && ContainsAwaitable(nestedDict.Values))
                    return true;
            }

            return false;
        }

        private static async Task<object?> AwaitListAsync(List<object> list)
        {
            Task<object?>[] tasks = new Task<object?>[list.Count];
            for (int i = 0; i < list.Count; i++)
                tasks[i] = ToTask(list[i]);

            object?[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

            List<object> resolved = new(results.Length);
            foreach (object? r in results)
                resolved.Add(r!);

            return resolved;
        }

        private static async Task<object?> AwaitDictionaryAsync(Dictionary<string, object> dict)
        {
            List<string> keys = new(dict.Count);
            List<Task<object?>> tasks = new(dict.Count);

            foreach (KeyValuePair<string, object> kv in dict)
            {
                keys.Add(kv.Key);
                tasks.Add(ToTask(kv.Value));
            }

            object?[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

            Dictionary<string, object> resolved = new(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
                resolved[keys[i]] = results[i]!;

            return resolved;
        }
    }

}
