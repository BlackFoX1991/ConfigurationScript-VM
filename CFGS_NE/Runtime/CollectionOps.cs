using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;
using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        /// <summary>
        /// Stores synchronization state for mutable runtime collections.
        /// </summary>
        private static readonly ConditionalWeakTable<object, MutableRuntimeState> MutableRuntimeStates = new();

        private sealed class MutableRuntimeState
        {
            public object SyncRoot { get; } = new();
        }

        /// <summary>
        /// Returns the synchronization root for a mutable runtime object.
        /// </summary>
        private static object GetMutableSyncRoot(object value)
        {
            return value switch
            {
                List<object> or Dictionary<string, object> => MutableRuntimeStates.GetValue(value, static _ => new MutableRuntimeState()).SyncRoot,
                ClassInstance obj => obj.SyncRoot,
                StaticInstance st => st.SyncRoot,
                Env env => env.SyncRoot,
                _ => throw new InvalidOperationException($"No sync root available for '{value.GetType().FullName}'.")
            };
        }

        /// <summary>
        /// Executes the supplied action while holding the mutable object's lock.
        /// </summary>
        private static void WithMutableLock(object value, Action action)
        {
            lock (GetMutableSyncRoot(value))
                action();
        }

        /// <summary>
        /// Executes the supplied function while holding the mutable object's lock.
        /// </summary>
        private static T WithMutableLock<T>(object value, Func<T> func)
        {
            lock (GetMutableSyncRoot(value))
                return func();
        }

        /// <summary>
        /// Executes work while holding the synchronization lock for a mutable CFGS runtime object.
        /// </summary>
        public static void LockMutableRuntime(object value, Action action)
            => WithMutableLock(value, action);

        /// <summary>
        /// Executes work while holding the synchronization lock for a mutable CFGS runtime object.
        /// </summary>
        public static T LockMutableRuntime<T>(object value, Func<T> func)
            => WithMutableLock(value, func);

        /// <summary>
        /// Registers runtime synchronization state for mutable collections created by the VM.
        /// </summary>
        private static TCollection CaptureMutableCollectionOwnership<TCollection>(TCollection collection)
            where TCollection : class
        {
            if (collection is List<object> or Dictionary<string, object>)
                _ = MutableRuntimeStates.GetValue(collection, static _ => new MutableRuntimeState());
            return collection;
        }

        /// <summary>
        /// Preserved as a no-op to keep existing VM call sites stable now that async execution is no longer globally serialized.
        /// </summary>
        private static void MarkCurrentAsyncSharedStateHazard() { }

        /// <summary>
        /// Preserved as a no-op to keep existing VM call sites stable now that async execution is no longer globally serialized.
        /// </summary>
        private void MarkAsyncHazardForEnvAccess(Env env) { }

        /// <summary>
        /// Preserved as a no-op to keep existing VM call sites stable now that async execution is no longer globally serialized.
        /// </summary>
        private static void MarkAsyncHazardForMutableCollection(object? value) { }

        /// <summary>
        /// Preserved as a no-op to keep existing VM call sites stable now that async execution is no longer globally serialized.
        /// </summary>
        private static void MarkAsyncHazardForMutableReceiver(object? receiver) { }

        /// <summary>
        /// Returns the current item count of a runtime list.
        /// </summary>
        private static int GetListCount(List<object> list)
            => WithMutableLock(list, () => list.Count);

        /// <summary>
        /// Returns a snapshot copy of a runtime list.
        /// </summary>
        private static List<object> SnapshotList(List<object> list)
            => WithMutableLock(list, () => list.ToList());

        /// <summary>
        /// Returns a snapshot copy of a mutable CFGS runtime list.
        /// </summary>
        public static List<object> SnapshotMutableList(List<object> list)
            => SnapshotList(list);

        /// <summary>
        /// Returns the item at the supplied list index.
        /// </summary>
        private static object GetListValue(List<object> list, int index)
            => WithMutableLock(list, () => list[index]);

        /// <summary>
        /// Sets the item at the supplied list index.
        /// </summary>
        private static void SetListValue(List<object> list, int index, object value)
            => WithMutableLock(list, () => list[index] = value);

        /// <summary>
        /// Appends a value to a runtime list.
        /// </summary>
        private static void AddListValue(List<object> list, object value)
            => WithMutableLock(list, () => list.Add(value));

        /// <summary>
        /// Removes a range from a runtime list.
        /// </summary>
        private static void RemoveListRange(List<object> list, int index, int count)
            => WithMutableLock(list, () => list.RemoveRange(index, count));

        /// <summary>
        /// Removes the item at the supplied list index.
        /// </summary>
        private static void RemoveListAt(List<object> list, int index)
            => WithMutableLock(list, () => list.RemoveAt(index));

        /// <summary>
        /// Clears a runtime list.
        /// </summary>
        private static void ClearList(List<object> list)
            => WithMutableLock(list, list.Clear);

        /// <summary>
        /// Returns a copied slice of a runtime list.
        /// </summary>
        private static List<object> GetListRange(List<object> list, int start, int count)
            => WithMutableLock(list, () => list.GetRange(start, count));

        /// <summary>
        /// Returns the current item count of a runtime dictionary.
        /// </summary>
        private static int GetDictionaryCount(Dictionary<string, object> dict)
            => WithMutableLock(dict, () => dict.Count);

        /// <summary>
        /// Returns whether the supplied key exists in the runtime dictionary.
        /// </summary>
        private static bool ContainsDictionaryKey(Dictionary<string, object> dict, string key)
            => WithMutableLock(dict, () => dict.ContainsKey(key));

        /// <summary>
        /// Tries to read a value from a runtime dictionary.
        /// </summary>
        private static bool TryGetDictionaryValue(Dictionary<string, object> dict, string key, out object? value)
        {
            object? local = null;
            bool found = WithMutableLock(dict, () => dict.TryGetValue(key, out local));
            value = local;
            return found;
        }

        /// <summary>
        /// Writes a value into a runtime dictionary.
        /// </summary>
        private static void SetDictionaryValue(Dictionary<string, object> dict, string key, object value)
            => WithMutableLock(dict, () => dict[key] = value);

        /// <summary>
        /// Removes a value from a runtime dictionary.
        /// </summary>
        private static bool RemoveDictionaryValue(Dictionary<string, object> dict, string key)
            => WithMutableLock(dict, () => dict.Remove(key));

        /// <summary>
        /// Clears a runtime dictionary.
        /// </summary>
        private static void ClearDictionary(Dictionary<string, object> dict)
            => WithMutableLock(dict, dict.Clear);

        /// <summary>
        /// Returns a snapshot copy of runtime dictionary keys.
        /// </summary>
        private static List<string> SnapshotDictionaryKeys(Dictionary<string, object> dict)
            => WithMutableLock(dict, () => dict.Keys.ToList());

        /// <summary>
        /// Returns a snapshot copy of a mutable CFGS runtime dictionary's keys.
        /// </summary>
        public static List<string> SnapshotMutableDictionaryKeys(Dictionary<string, object> dict)
            => SnapshotDictionaryKeys(dict);

        /// <summary>
        /// Returns a snapshot copy of runtime dictionary entries.
        /// </summary>
        private static List<KeyValuePair<string, object>> SnapshotDictionaryEntries(Dictionary<string, object> dict)
            => WithMutableLock(dict, () => dict.ToList());

        /// <summary>
        /// Returns a snapshot copy of a mutable CFGS runtime dictionary's entries.
        /// </summary>
        public static List<KeyValuePair<string, object>> SnapshotMutableDictionaryEntries(Dictionary<string, object> dict)
            => SnapshotDictionaryEntries(dict);

        /// <summary>
        /// Returns an ordered snapshot copy of runtime dictionary entries.
        /// </summary>
        private static List<KeyValuePair<string, object>> SnapshotOrderedDictionaryEntries(Dictionary<string, object> dict)
            => WithMutableLock(dict, () => dict.OrderBy(k => k.Key, StringComparer.Ordinal).ToList());

        /// <summary>
        /// The RequireIntIndex
        /// </summary>
        private int RequireIntIndex(object idxObj, Instruction instr)
        {
            if (idxObj is int i) return i;
            if (idxObj is long l)
            {
                if (l < int.MinValue || l > int.MaxValue)
                    throw new VMException($"Runtime error: index {l} outside Int32 range", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                return (int)l;
            }
            if (idxObj is short s) return (int)s;
            if (idxObj is byte b) return (int)b;

            if (idxObj is string sVal && int.TryParse(sVal, out int parsed))
                return parsed;

            throw new VMException($"Runtime error: index must be an integer, got '{idxObj?.GetType().Name ?? "null"}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        /// <summary>
        /// The DeleteSliceOnTarget
        /// </summary>
        private void DeleteSliceOnTarget(ref object target, object startObj, object endObj, Instruction instr)
        {
            if (target is string)
                throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            switch (target)
            {
                case List<object> arr:
                    {
                        int count = GetListCount(arr);
                        (int start, int end) = NormalizeSliceBounds(startObj, endObj, count, instr);

                        if (start < end)
                            RemoveListRange(arr, start, end - start);
                        return;
                    }

                case Dictionary<string, object> dict:
                    {
                        List<string> keys = SnapshotDictionaryKeys(dict);
                        (int start, int end) = NormalizeSliceBounds(startObj, endObj, keys.Count, instr);

                        for (int i = end - 1; i >= start; i--)
                            RemoveDictionaryValue(dict, keys[i]);

                        return;
                    }

                default:
                    throw new VMException($"Runtime error: delete slice target must be array, dictionary, or string", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }
        }

        /// <summary>
        /// The NormalizeSliceBounds
        /// </summary>
        private (int start, int endEx) NormalizeSliceBounds(object? startObj, object? endObj, int len, Instruction instr)
        {
            int start = startObj == null ? 0 : RequireIntIndex(startObj, instr);
            if (start < 0) start += len;

            int endEx = endObj == null ? len : RequireIntIndex(endObj, instr);
            if (endEx < 0) endEx += len;

            start = Math.Clamp(start, 0, len);
            endEx = Math.Clamp(endEx, 0, len);
            if (endEx < start) endEx = start;

            return (start, endEx);
        }

        /// <summary>
        /// The NormalizeDictionaryWriteKey
        /// </summary>
        private string NormalizeDictionaryWriteKey(Dictionary<string, object> dict, object? idxObj, Instruction instr)
        {
            string key = idxObj?.ToString() ?? string.Empty;
            if (key.Length == 0)
                throw new VMException("Runtime error: dictionary key cannot be empty", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            if (IsReservedIntrinsicName(dict, key))
                throw new VMException($"Runtime error: key '{key}' is reserved for dictionary intrinsics", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            return key;
        }

        /// <summary>
        /// Handles the NEW_ARRAY opcode.
        /// </summary>
        private StepResult HandleNewArrayInstruction(Instruction instr)
        {
            if (instr.Operand is null)
                return StepResult.Next;

            int ecount = (int)instr.Operand;
            RequireStack(ecount, instr, "NEW_ARRAY");
            object[] temp = new object[ecount];
            for (int i = ecount - 1; i >= 0; i--)
                temp[i] = _stack.Pop();
            List<object> list = CaptureMutableCollectionOwnership(new List<object>(temp));
            _stack.Push(list);
            return StepResult.Next;
        }

        /// <summary>
        /// Handles the SLICE_GET opcode.
        /// </summary>
        private StepResult HandleSliceGetInstruction(Instruction instr)
        {
            if (instr.Operand is string)
                RequireStack(2, instr, "SLICE_GET");
            else
                RequireStack(3, instr, "SLICE_GET");

            object endObj = _stack.Pop();
            object startObj = _stack.Pop();

            object target;
            if (instr.Operand is string name)
            {
                Env owner = FindEnvWithLocal(name)
                    ?? throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                MarkAsyncHazardForEnvAccess(owner);
                target = GetLocalVar(owner, name)!;
            }
            else
            {
                target = _stack.Pop();
            }

            switch (target)
            {
                case List<object> arr:
                    {
                        MarkAsyncHazardForMutableCollection(arr);
                        int count = GetListCount(arr);
                        (int start, int end) = NormalizeSliceBounds(startObj, endObj, count, instr);
                        _stack.Push(CaptureMutableCollectionOwnership(GetListRange(arr, start, end - start)));
                        return StepResult.Next;
                    }

                case Dictionary<string, object> dict:
                    {
                        MarkAsyncHazardForMutableCollection(dict);
                        List<string> keys = SnapshotDictionaryKeys(dict);
                        (int start, int end) = NormalizeSliceBounds(startObj, endObj, keys.Count, instr);

                        Dictionary<string, object> slice = CaptureMutableCollectionOwnership(new Dictionary<string, object>());
                        for (int i = start; i < end; i++)
                        {
                            if (TryGetDictionaryValue(dict, keys[i], out object? dictValue))
                                slice[keys[i]] = dictValue!;
                        }

                        _stack.Push(slice);
                        return StepResult.Next;
                    }

                case string s:
                    {
                        (int start, int end) = NormalizeSliceBounds(startObj, endObj, s.Length, instr);
                        _stack.Push(s.Substring(start, end - start));
                        return StepResult.Next;
                    }

                default:
                    throw new VMException(
                        "Runtime error: SLICE_GET target must be array, dictionary, or string",
                        instr.Line,
                        instr.Col,
                        instr.OriginFile,
                        IsDebugging,
                        DebugStream!);
            }
        }

        /// <summary>
        /// Handles the SLICE_SET opcode.
        /// </summary>
        private StepResult HandleSliceSetInstruction(Instruction instr)
        {
            if (instr.Operand is string)
                RequireStack(3, instr, "SLICE_SET");
            else
                RequireStack(4, instr, "SLICE_SET");

            object value = _stack.Pop();
            object endObj = _stack.Pop();
            object startObj = _stack.Pop();

            object target;
            if (instr.Operand is string name)
            {
                Env env = FindEnvWithLocal(name)
                    ?? throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                MarkAsyncHazardForEnvAccess(env);
                target = GetLocalVar(env, name)!;
                MarkAsyncHazardForMutableCollection(target);
                ApplySliceSet(ref target, startObj, endObj, value, instr);
                SetLocalVar(env, name, target);
            }
            else
            {
                target = _stack.Pop();
                MarkAsyncHazardForMutableCollection(target);
                ApplySliceSet(ref target, startObj, endObj, value, instr);
            }

            return StepResult.Next;
        }

        /// <summary>
        /// Applies a slice assignment to a runtime collection target.
        /// </summary>
        private void ApplySliceSet(ref object target, object startObj, object endObj, object value, Instruction instr)
        {
            if (target is string)
            {
                throw new VMException(
                    "Runtime error: SLICE_SET on string. Strings are immutable.",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    IsDebugging,
                    DebugStream!);
            }

            switch (target)
            {
                case List<object> arr:
                    {
                        int targetCount = GetListCount(arr);
                        (int start, int end) = NormalizeSliceBounds(startObj, endObj, targetCount, instr);

                        if (value is not List<object> lst)
                        {
                            throw new VMException(
                                "Runtime error: trying to assign non-list to array slice",
                                instr.Line,
                                instr.Col,
                                instr.OriginFile,
                                IsDebugging,
                                DebugStream!);
                        }

                        List<object> source = SnapshotList(lst);
                        int count = Math.Min(end - start, source.Count);
                        for (int i = 0; i < count; i++)
                            SetListValue(arr, start + i, source[i]);

                        return;
                    }

                case Dictionary<string, object> dict:
                    {
                        List<string> keys = SnapshotDictionaryKeys(dict);
                        (int start, int end) = NormalizeSliceBounds(startObj, endObj, keys.Count, instr);

                        if (value is not Dictionary<string, object> valDict)
                        {
                            throw new VMException(
                                "Runtime error: trying to assign non-dictionary to dictionary slice",
                                instr.Line,
                                instr.Col,
                                instr.OriginFile,
                                IsDebugging,
                                DebugStream!);
                        }

                        List<KeyValuePair<string, object>> source = SnapshotDictionaryEntries(valDict);
                        int i = 0;
                        for (int k = start; k < end && i < source.Count; k++, i++)
                            SetDictionaryValue(dict, keys[k], source[i].Value);

                        return;
                    }

                default:
                    throw new VMException(
                        "Runtime error: SLICE_SET target must be array or dictionary",
                        instr.Line,
                        instr.Col,
                        instr.OriginFile,
                        IsDebugging,
                        DebugStream!);
            }
        }

        /// <summary>
        /// Handles the NEW_DICT opcode.
        /// </summary>
        private StepResult HandleNewDictionaryInstruction(Instruction instr)
        {
            if (instr.Operand is null)
                return StepResult.Next;

            int dcount = (int)instr.Operand;
            RequireStack(dcount * 2, instr, "NEW_DICT");

            (string key, object val)[] pairs = new (string key, object val)[dcount];
            for (int i = dcount - 1; i >= 0; i--)
            {
                object value = _stack.Pop();
                object key = _stack.Pop();
                string sk = key?.ToString() ?? string.Empty;
                pairs[i] = (sk, value);
            }

            Dictionary<string, object> dict = CaptureMutableCollectionOwnership(new Dictionary<string, object>(dcount));
            for (int i = 0; i < dcount; i++)
            {
                string key = NormalizeDictionaryWriteKey(dict, pairs[i].key, instr);
                dict[key] = pairs[i].val;
            }

            _stack.Push(dict);
            return StepResult.Next;
        }

        /// <summary>
        /// Handles the IS_DICT opcode.
        /// </summary>
        private StepResult HandleIsDictionaryInstruction(Instruction instr)
        {
            if (_stack.Count < 1)
                throw new VMException("Stack underflow in IS_DICT", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            object v = _stack.Pop();
            _stack.Push(v is Dictionary<string, object>);
            return StepResult.Next;
        }

        /// <summary>
        /// Handles the IS_ARRAY opcode.
        /// </summary>
        private StepResult HandleIsArrayInstruction(Instruction instr)
        {
            if (_stack.Count < 1)
                throw new VMException("Stack underflow in IS_ARRAY", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            object v = _stack.Pop();
            _stack.Push(v is List<object>);
            return StepResult.Next;
        }

        /// <summary>
        /// Handles the DICT_KEYS opcode.
        /// </summary>
        private StepResult HandleDictionaryKeysInstruction(Instruction instr)
        {
            RequireStack(1, instr, "DICT_KEYS");
            object v = _stack.Pop();
            if (v is not Dictionary<string, object> dict)
                throw new VMException("Runtime error: DICT_KEYS target must be dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            List<object> keys = CaptureMutableCollectionOwnership(SnapshotDictionaryKeys(dict).Cast<object>().ToList());
            _stack.Push(keys);
            return StepResult.Next;
        }

        /// <summary>
        /// Handles the LEN opcode.
        /// </summary>
        private StepResult HandleLengthInstruction(Instruction instr)
        {
            RequireStack(1, instr, "LEN");
            object v = _stack.Pop();
            switch (v)
            {
                case List<object> arr:
                    _stack.Push(GetListCount(arr));
                    break;
                case Dictionary<string, object> dict:
                    _stack.Push(GetDictionaryCount(dict));
                    break;
                case string str:
                    _stack.Push(str.Length);
                    break;
                default:
                    throw new VMException("Runtime error: LEN target must be array, dictionary, or string", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }

            return StepResult.Next;
        }

        /// <summary>
        /// Handles the HAS_KEY opcode.
        /// </summary>
        private StepResult HandleHasKeyInstruction(Instruction instr)
        {
            RequireStack(2, instr, "HAS_KEY");
            object keyObj = _stack.Pop();
            object target = _stack.Pop();
            if (target is not Dictionary<string, object> dict)
                throw new VMException("Runtime error: HAS_KEY target must be dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            string key = keyObj?.ToString() ?? "";
            _stack.Push(ContainsDictionaryKey(dict, key));
            return StepResult.Next;
        }

        /// <summary>
        /// Handles the ARRAY_PUSH opcode.
        /// </summary>
        private StepResult HandleArrayPushInstruction(Instruction instr)
        {
            if (instr.Operand == null)
            {
                RequireStack(2, instr, "ARRAY_PUSH");
                object target = _stack.Pop();
                object value = _stack.Pop();
                AppendToCollectionTarget(target, value, instr, null);
                return StepResult.Next;
            }

            RequireStack(1, instr, "ARRAY_PUSH");
            object pushedValue = _stack.Pop();
            string name = (string)instr.Operand;
            Env? env = FindEnvWithLocal(name);
            if (env == null || !TryGetLocalVar(env, name, out object? obj))
                throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            MarkAsyncHazardForEnvAccess(env);
            AppendToCollectionTarget(obj!, pushedValue, instr, name);
            return StepResult.Next;
        }

        /// <summary>
        /// Appends or merges a value into a mutable collection target.
        /// </summary>
        private void AppendToCollectionTarget(object target, object value, Instruction instr, string? variableName)
        {
            if (target is List<object> arr)
            {
                MarkAsyncHazardForMutableCollection(arr);
                AddListValue(arr, value);
                return;
            }

            if (target is Dictionary<string, object> dict)
            {
                MarkAsyncHazardForMutableCollection(dict);
                if (value is Dictionary<string, object> literal && GetDictionaryCount(literal) == 1)
                {
                    foreach (KeyValuePair<string, object> kv in SnapshotDictionaryEntries(literal))
                    {
                        string key = NormalizeDictionaryWriteKey(dict, kv.Key, instr);
                        SetDictionaryValue(dict, key, kv.Value);
                    }
                }
                else
                {
                    WithMutableLock(dict, () =>
                    {
                        int k = 0;
                        while (dict.ContainsKey(k.ToString(CultureInfo.InvariantCulture)))
                            k++;
                        dict[k.ToString(CultureInfo.InvariantCulture)] = value;
                    });
                }

                return;
            }

            if (variableName != null)
            {
                throw new VMException(
                    $"Runtime error: variable '{variableName}' is not an array or dictionary",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    IsDebugging,
                    DebugStream!);
            }

            throw new VMException(
                "Runtime error: ARRAY_PUSH target is not an array or dictionary",
                instr.Line,
                instr.Col,
                instr.OriginFile,
                IsDebugging,
                DebugStream!);
        }

        /// <summary>
        /// Handles the ARRAY_DELETE_SLICE opcode.
        /// </summary>
        private StepResult HandleArrayDeleteSliceInstruction(Instruction instr)
        {
            if (instr.Operand is string)
                RequireStack(2, instr, "ARRAY_DELETE_SLICE");
            else
                RequireStack(3, instr, "ARRAY_DELETE_SLICE");

            object endObj = _stack.Pop();
            object startObj = _stack.Pop();

            object target;
            if (instr.Operand is string name)
            {
                Env env = FindEnvWithLocal(name)
                    ?? throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                MarkAsyncHazardForEnvAccess(env);
                target = GetLocalVar(env, name)!;
                MarkAsyncHazardForMutableCollection(target);
                DeleteSliceOnTarget(ref target, startObj, endObj, instr);
                SetLocalVar(env, name, target);
            }
            else
            {
                target = _stack.Pop();
                DeleteSliceOnTarget(ref target, startObj, endObj, instr);
            }

            return StepResult.Next;
        }

        /// <summary>
        /// Handles the ARRAY_DELETE_SLICE_ALL opcode.
        /// </summary>
        private StepResult HandleArrayDeleteSliceAllInstruction(Instruction instr)
        {
            RequireStack(3, instr, "ARRAY_DELETE_SLICE_ALL");
            object endObj = _stack.Pop();
            object startObj = _stack.Pop();
            object target = _stack.Pop();
            MarkAsyncHazardForMutableCollection(target);

            if (target is string)
            {
                throw new VMException(
                    "Runtime error: delete on strings is not allowed",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    IsDebugging,
                    DebugStream!);
            }

            if (target is List<object> arr)
            {
                int count = GetListCount(arr);
                (int start, int endEx) = NormalizeSliceBounds(startObj, endObj, count, instr);
                int deleteCount = endEx - start;
                if (deleteCount > 0)
                    RemoveListRange(arr, start, deleteCount);
                return StepResult.Next;
            }

            if (target is Dictionary<string, object> dict)
            {
                List<string> keys = SnapshotDictionaryKeys(dict);
                (int start, int endEx) = NormalizeSliceBounds(startObj, endObj, keys.Count, instr);
                for (int i = start; i < endEx; i++)
                    RemoveDictionaryValue(dict, keys[i]);
                return StepResult.Next;
            }

            throw new VMException(
                "Runtime error: delete target is not an array or dictionary",
                instr.Line,
                instr.Col,
                instr.OriginFile,
                IsDebugging,
                DebugStream!);
        }

        /// <summary>
        /// Handles the ARRAY_DELETE_ELEM opcode.
        /// </summary>
        private StepResult HandleArrayDeleteElementInstruction(Instruction instr)
        {
            if (instr.Operand != null)
                RequireStack(1, instr, "ARRAY_DELETE_ELEM");
            else
                RequireStack(2, instr, "ARRAY_DELETE_ELEM");

            object idxObj = _stack.Pop();

            if (instr.Operand != null)
            {
                string name = (string)instr.Operand;
                Env? owner = FindEnvWithLocal(name);
                if (owner == null)
                    throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                MarkAsyncHazardForEnvAccess(owner);
                object target = GetLocalVar(owner, name)!;
                MarkAsyncHazardForMutableCollection(target);
                DeleteSingleCollectionElement(target, idxObj, instr, name);
                return StepResult.Next;
            }

            object nonLocalTarget = _stack.Pop();
            MarkAsyncHazardForMutableCollection(nonLocalTarget);

            if (nonLocalTarget is ClassInstance obj)
            {
                MarkCurrentAsyncSharedStateHazard();
                string key = idxObj?.ToString() ?? "";
                if (key.Length == 0)
                    throw new VMException("Runtime error: field name cannot be empty", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                if (!TryGetInstanceField(obj, key, out object? field))
                    throw new VMException($"Runtime error: invalid instance member '{key}' in class '{obj.ClassName}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                if (field is List<object>)
                {
                    SetInstanceField(obj, key, CaptureMutableCollectionOwnership(new List<object>()));
                    return StepResult.Next;
                }

                if (field is Dictionary<string, object>)
                {
                    SetInstanceField(obj, key, CaptureMutableCollectionOwnership(new Dictionary<string, object>()));
                    return StepResult.Next;
                }

                throw new VMException(
                    $"Runtime error: field '{key}' on class '{obj.ClassName}' is not an array or dictionary",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    IsDebugging,
                    DebugStream!);
            }

            if (nonLocalTarget is StaticInstance st)
            {
                MarkCurrentAsyncSharedStateHazard();
                string key = idxObj?.ToString() ?? "";
                if (key.Length == 0)
                    throw new VMException("Runtime error: static member name cannot be empty", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                if (!TryGetStaticField(st, key, out object? field))
                {
                    throw new VMException(
                        $"Runtime error: invalid static member '{key}' in class '{st.ClassName}'",
                        instr.Line,
                        instr.Col,
                        instr.OriginFile,
                        IsDebugging,
                        DebugStream!);
                }

                if (field is List<object>)
                {
                    SetStaticField(st, key, CaptureMutableCollectionOwnership(new List<object>()));
                    return StepResult.Next;
                }

                if (field is Dictionary<string, object>)
                {
                    SetStaticField(st, key, CaptureMutableCollectionOwnership(new Dictionary<string, object>()));
                    return StepResult.Next;
                }

                throw new VMException(
                    $"Runtime error: static member '{key}' in class '{st.ClassName}' is not an array or dictionary",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    IsDebugging,
                    DebugStream!);
            }

            DeleteSingleCollectionElement(nonLocalTarget, idxObj, instr, null);
            return StepResult.Next;
        }

        /// <summary>
        /// Deletes a single list or dictionary element.
        /// </summary>
        private void DeleteSingleCollectionElement(object target, object idxObj, Instruction instr, string? variableName)
        {
            if (target is string)
            {
                throw new VMException(
                    "Runtime error: delete on strings is not allowed",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    IsDebugging,
                    DebugStream!);
            }

            if (target is List<object> arr)
            {
                int index = RequireIntIndex(idxObj, instr);
                int count = GetListCount(arr);
                if (index >= 0 && index < count)
                {
                    RemoveListAt(arr, index);
                    return;
                }

                throw new VMException(
                    $"Runtime error: index {index} out of range",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    IsDebugging,
                    DebugStream!);
            }

            if (target is Dictionary<string, object> dict)
            {
                string key = Convert.ToString(idxObj, CultureInfo.InvariantCulture) ?? string.Empty;
                if (RemoveDictionaryValue(dict, key))
                    return;

                throw new VMException(
                    $"Runtime error: key '{key}' not found in dictionary",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    IsDebugging,
                    DebugStream!);
            }

            string message = variableName != null
                ? $"Runtime error: variable '{variableName}' is not an array or dictionary"
                : "Runtime error: delete target is not an array or dictionary";

            throw new VMException(message, instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        /// <summary>
        /// Handles the ARRAY_DELETE_ALL opcode.
        /// </summary>
        private StepResult HandleArrayDeleteAllInstruction(Instruction instr)
        {
            if (instr.Operand is null)
                return StepResult.Next;

            string name = (string)instr.Operand;
            Env? env = FindEnvWithLocal(name);
            if (env == null || !TryGetLocalVar(env, name, out object? target))
                throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            MarkAsyncHazardForEnvAccess(env);
            MarkAsyncHazardForMutableCollection(target);
            if (target is List<object>)
            {
                SetLocalVar(env, name, CaptureMutableCollectionOwnership(new List<object>()));
                return StepResult.Next;
            }

            if (target is Dictionary<string, object>)
            {
                SetLocalVar(env, name, CaptureMutableCollectionOwnership(new Dictionary<string, object>()));
                return StepResult.Next;
            }

            throw new VMException(
                $"Runtime error: variable '{name}' is not an array or dictionary",
                instr.Line,
                instr.Col,
                instr.OriginFile,
                IsDebugging,
                DebugStream!);
        }

        /// <summary>
        /// Handles the ARRAY_CLEAR opcode.
        /// </summary>
        private StepResult HandleArrayClearInstruction(Instruction instr)
        {
            RequireStack(1, instr, "ARRAY_CLEAR");

            object target = _stack.Pop();
            MarkAsyncHazardForMutableCollection(target);

            if (target is List<object> list)
            {
                ClearList(list);
                return StepResult.Next;
            }

            if (target is Dictionary<string, object> dict)
            {
                ClearDictionary(dict);
                return StepResult.Next;
            }

            throw new VMException(
                "Runtime error: delete target is not an array or dictionary",
                instr.Line,
                instr.Col,
                instr.OriginFile,
                IsDebugging,
                DebugStream!);
        }

        /// <summary>
        /// Handles the ARRAY_DELETE_ELEM_ALL opcode.
        /// </summary>
        private StepResult HandleArrayDeleteElementAllInstruction(Instruction instr)
        {
            RequireStack(2, instr, "ARRAY_DELETE_ELEM_ALL");

            object idxObj = _stack.Pop();
            object target = _stack.Pop();
            if (target is string)
            {
                throw new VMException(
                    "Runtime error: delete on strings is not allowed",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    IsDebugging,
                    DebugStream!);
            }

            if (target is List<object> arr)
            {
                int index = RequireIntIndex(idxObj, instr);
                int count = GetListCount(arr);
                if (index < 0 || index >= count)
                {
                    throw new VMException(
                        $"Runtime error: index {index} out of range",
                        instr.Line,
                        instr.Col,
                        instr.OriginFile,
                        IsDebugging,
                        DebugStream!);
                }

                RemoveListAt(arr, index);
                return StepResult.Next;
            }

            if (target is Dictionary<string, object> dict)
            {
                string key = idxObj?.ToString() ?? throw new VMException(
                    "Runtime error: dictionary key cannot be null",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    IsDebugging,
                    DebugStream!);

                if (!RemoveDictionaryValue(dict, key))
                {
                    throw new VMException(
                        $"Runtime error: key '{key}' not found in dictionary",
                        instr.Line,
                        instr.Col,
                        instr.OriginFile,
                        IsDebugging,
                        DebugStream!);
                }

                return StepResult.Next;
            }

            throw new VMException(
                "Runtime error: delete target is not an array or dictionary",
                instr.Line,
                instr.Col,
                instr.OriginFile,
                IsDebugging,
                DebugStream!);
        }
    }
}
