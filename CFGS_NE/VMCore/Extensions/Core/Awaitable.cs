namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Defines the <see cref="IAwaitable" />
    /// </summary>
    public interface IAwaitable
    {
        /// <summary>
        /// The AsTask
        /// </summary>
        /// <returns>The <see cref="Task{object?}"/></returns>
        Task<object?> AsTask();
    }

    /// <summary>
    /// Defines the <see cref="AwaitableAdapter" />
    /// </summary>
    internal static class AwaitableAdapter
    {
        /// <summary>
        /// The TryGetTask
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <param name="task">The task<see cref="Task{object?}"/></param>
        /// <returns>The <see cref="bool"/></returns>
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

        /// <summary>
        /// The ToTask
        /// </summary>
        /// <param name="value">The value<see cref="object?"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static Task<object?> ToTask(object? value)
        {
            if (TryGetTask(value, out Task<object?> awaited))
                return awaited;

            return Task.FromResult(value);
        }

        /// <summary>
        /// The AwaitNonGenericTaskAsync
        /// </summary>
        /// <param name="task">The task<see cref="Task"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static async Task<object?> AwaitNonGenericTaskAsync(Task task)
        {
            await task.ConfigureAwait(false);
            return null;
        }

        /// <summary>
        /// The AwaitGenericTaskAsync
        /// </summary>
        /// <param name="task">The task<see cref="Task"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static async Task<object?> AwaitGenericTaskAsync(Task task)
        {
            await task.ConfigureAwait(false);
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }

        /// <summary>
        /// The AwaitNonGenericValueTaskAsync
        /// </summary>
        /// <param name="task">The task<see cref="ValueTask"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static async Task<object?> AwaitNonGenericValueTaskAsync(ValueTask task)
        {
            await task.ConfigureAwait(false);
            return null;
        }

        /// <summary>
        /// The AwaitGenericValueTaskAsync
        /// </summary>
        /// <param name="valueTask">The valueTask<see cref="object"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static Task<object?> AwaitGenericValueTaskAsync(object valueTask)
        {
            object? taskObj = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)?.Invoke(valueTask, null);
            if (taskObj is not Task task)
                throw new InvalidOperationException("Runtime error: failed to adapt ValueTask<T> to Task");

            if (IsGenericTaskType(task.GetType()))
                return AwaitGenericTaskAsync(task);

            return AwaitNonGenericTaskAsync(task);
        }

        /// <summary>
        /// The IsGenericTaskType
        /// </summary>
        /// <param name="type">The type<see cref="Type"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsGenericTaskType(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>);

        /// <summary>
        /// The IsGenericValueTaskType
        /// </summary>
        /// <param name="type">The type<see cref="Type"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsGenericValueTaskType(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);

        /// <summary>
        /// The IsDirectAwaitable
        /// </summary>
        /// <param name="item">The item<see cref="object?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsDirectAwaitable(object? item)
        {
            if (item is null)
                return false;

            if (item is Task or ValueTask or IAwaitable)
                return true;

            Type itemType = item.GetType();
            return typeof(Task).IsAssignableFrom(itemType) || IsGenericValueTaskType(itemType);
        }

        /// <summary>
        /// The ContainsAwaitable
        /// </summary>
        /// <param name="values">The values<see cref="IEnumerable{object}"/></param>
        /// <returns>The <see cref="bool"/></returns>
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

        /// <summary>
        /// The AwaitListAsync
        /// </summary>
        /// <param name="list">The list<see cref="List{object}"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
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

        /// <summary>
        /// The AwaitDictionaryAsync
        /// </summary>
        /// <param name="dict">The dict<see cref="Dictionary{string, object}"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
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
