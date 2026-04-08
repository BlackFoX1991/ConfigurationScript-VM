using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;
using CFGS_VM.VMCore.Extensions.Intrinsics.Core;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;
using System.Numerics;
using System.Linq;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        /// <summary>
        /// Explicitly destroys a runtime value.
        /// </summary>
        public bool DestroyValue(object? value, Instruction instr, bool recursive = false)
        {
            HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
            return DestroyValueCore(value, instr, recursive, visited);
        }

        /// <summary>
        /// The DestroyValueCore.
        /// </summary>
        private bool DestroyValueCore(object? value, Instruction instr, bool recursive, HashSet<object> visited)
        {
            if (value is null)
                return false;

            Type valueType = value.GetType();
            if (!valueType.IsValueType && !visited.Add(value))
                return false;

            switch (value)
            {
                case ClassInstance obj:
                    return DestroyClassInstance(obj, instr, recursive, visited);

                case List<object> list:
                    {
                        bool any = false;
                        if (recursive)
                        {
                            foreach (object item in SnapshotList(list))
                                any |= DestroyValueCore(item, instr, recursive: true, visited);
                        }

                        return any;
                    }

                case Dictionary<string, object> dict:
                    {
                        bool any = false;
                        if (recursive)
                        {
                            foreach (object item in SnapshotDictionaryEntries(dict).Select(static kv => kv.Value))
                                any |= DestroyValueCore(item, instr, recursive: true, visited);
                        }

                        return any;
                    }

                case IDisposable disposable:
                    disposable.Dispose();
                    return true;
            }

            if (TryInvokeLifecycleIntrinsic(value, "close", instr))
                return true;

            if (TryInvokeLifecycleIntrinsic(value, "dispose", instr))
                return true;

            return false;
        }

        /// <summary>
        /// The DestroyClassInstance.
        /// </summary>
        private bool DestroyClassInstance(ClassInstance obj, Instruction instr, bool recursive, HashSet<object> visited)
        {
            if (IsInstanceDestroyed(obj) || IsInstanceDestroying(obj))
                return false;

            SetRuntimeFlag(obj, "__destroying", true);
            try
            {
                if (TryResolveInstanceMemberInHierarchy(obj, "destroy", out ClassInstance ownerInst, out StaticInstance? ownerType, out object? member) &&
                    member is Closure clos &&
                    clos.Parameters.Count > 0 &&
                    clos.Parameters[0] == "this")
                {
                    if (clos.IsAsync)
                    {
                        throw new VMException(
                            "Runtime error: destroy method must not use await.",
                            instr.Line,
                            instr.Col,
                            instr.OriginFile,
                            IsDebugging,
                            DebugStream!);
                    }

                    InvokeClosureSync(clos, [], instr, ownerInst, ownerType);
                }

                if (recursive)
                {
                    foreach (KeyValuePair<string, object> kv in SnapshotInstanceFields(obj))
                    {
                        if (kv.Key.StartsWith("__", StringComparison.Ordinal))
                            continue;

                        DestroyValueCore(kv.Value, instr, recursive: true, visited);
                    }
                }
            }
            finally
            {
                SetRuntimeFlag(obj, "__destroying", false);
                SetRuntimeFlag(obj, "__destroyed", true);
            }

            return true;
        }

        /// <summary>
        /// The TryInvokeLifecycleIntrinsic.
        /// </summary>
        private bool TryInvokeLifecycleIntrinsic(object receiver, string name, Instruction instr)
        {
            if (!TryBindIntrinsic(receiver, name, out IntrinsicBound bound, instr))
                return false;

            if (bound.Method.ArityMin != 0 || bound.Method.ArityMax != 0)
                return false;

            object? result = InvokeIntrinsicForCall(bound.Method, bound.Receiver, [], instr);
            if (AwaitableAdapter.TryGetTask(result, out _))
            {
                throw new VMException(
                    $"Runtime error: lifecycle intrinsic '{name}' must not be async.",
                    instr.Line,
                    instr.Col,
                    instr.OriginFile,
                    IsDebugging,
                    DebugStream!);
            }

            return true;
        }

        /// <summary>
        /// The IsInstanceDestroyed.
        /// </summary>
        private static bool IsInstanceDestroyed(ClassInstance obj)
            => TryGetRuntimeFlag(obj, "__destroyed");

        /// <summary>
        /// The IsInstanceDestroying.
        /// </summary>
        private static bool IsInstanceDestroying(ClassInstance obj)
            => TryGetRuntimeFlag(obj, "__destroying");

        /// <summary>
        /// The TryGetRuntimeFlag.
        /// </summary>
        private static bool TryGetRuntimeFlag(ClassInstance obj, string slotName)
        {
            return TryGetInstanceField(obj, slotName, out object? raw) &&
                   raw is bool flag &&
                   flag;
        }

        /// <summary>
        /// The SetRuntimeFlag.
        /// </summary>
        private static void SetRuntimeFlag(ClassInstance obj, string slotName, bool value)
        {
            SetInstanceField(obj, slotName, value);
        }

        /// <summary>
        /// Tries to read an instance field.
        /// </summary>
        private static bool TryGetInstanceField(ClassInstance obj, string name, out object? value)
        {
            object? local = null;
            bool found = WithMutableLock(obj, () => obj.Fields.TryGetValue(name, out local));
            value = local;
            return found;
        }

        /// <summary>
        /// Writes an instance field.
        /// </summary>
        private static void SetInstanceField(ClassInstance obj, string name, object value)
            => WithMutableLock(obj, () => obj.Fields[name] = value);

        /// <summary>
        /// Returns a snapshot copy of instance fields.
        /// </summary>
        private static List<KeyValuePair<string, object>> SnapshotInstanceFields(ClassInstance obj)
            => WithMutableLock(obj, () => obj.Fields.ToList());

        /// <summary>
        /// Tries to read a static field.
        /// </summary>
        private static bool TryGetStaticField(StaticInstance st, string name, out object? value)
        {
            object? local = null;
            bool found = WithMutableLock(st, () => st.Fields.TryGetValue(name, out local));
            value = local;
            return found;
        }

        /// <summary>
        /// Writes a static field.
        /// </summary>
        private static void SetStaticField(StaticInstance st, string name, object value)
            => WithMutableLock(st, () => st.Fields[name] = value);

        /// <summary>
        /// The CreateIndexException
        /// </summary>
        private VMException CreateIndexException(object target, object idxObj, Instruction instr)
        {
            string tid = target?.GetType().FullName ?? "null";
            string tval; try { tval = target?.ToString() ?? "null"; } catch { tval = "<ToString() failed>"; }

            string iid = idxObj?.GetType().FullName ?? "null";
            string ival; try { ival = idxObj?.ToString() ?? "null"; } catch { ival = "<ToString() failed>"; }

            return new VMException(
                "Runtime error: INDEX_GET target is not indexable.\n" +
                $"  target type: {tid}\n  target value: {tval}\n" +
                $"  index type: {iid}\n  index value: {ival}",
                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
            );
        }

        /// <summary>
        /// Defines the VisibilityPublic
        /// </summary>
        private const int VisibilityPublic = 0;

        /// <summary>
        /// Defines the VisibilityPrivate
        /// </summary>
        private const int VisibilityPrivate = 1;

        /// <summary>
        /// Defines the VisibilityProtected
        /// </summary>
        private const int VisibilityProtected = 2;

        /// <summary>
        /// The TryGetStaticType
        /// </summary>
        private static bool TryGetStaticType(ClassInstance inst, out StaticInstance type)
        {
            if (TryGetInstanceField(inst, "__type", out object? tObj) && tObj is StaticInstance st)
            {
                type = st;
                return true;
            }

            type = null!;
            return false;
        }

        /// <summary>
        /// The IsInterfaceType
        /// </summary>
        private static bool IsInterfaceType(StaticInstance type)
        {
            if (!TryGetStaticField(type, "__is_interface", out object? raw) || raw == null)
                return false;

            return raw switch
            {
                bool b => b,
                int i => i != 0,
                long l => l != 0,
                short s => s != 0,
                byte b => b != 0,
                BigInteger bi => bi != BigInteger.Zero,
                _ => true
            };
        }

        /// <summary>
        /// Enumerates direct interfaces attached to a class or interface static descriptor.
        /// </summary>
        private static IEnumerable<StaticInstance> EnumerateDirectInterfaces(StaticInstance type)
        {
            if (!TryGetStaticField(type, "__interfaces", out object? raw) || raw is not List<object> entries)
                yield break;

            foreach (object? entry in SnapshotList(entries))
            {
                if (entry is StaticInstance iface)
                    yield return iface;
            }
        }

        /// <summary>
        /// The InterfaceExtendsOrEquals
        /// </summary>
        private static bool InterfaceExtendsOrEquals(StaticInstance iface, StaticInstance target, HashSet<StaticInstance> visited)
        {
            if (!visited.Add(iface))
                return false;

            if (ReferenceEquals(iface, target))
                return true;

            foreach (StaticInstance baseIface in EnumerateDirectInterfaces(iface))
            {
                if (InterfaceExtendsOrEquals(baseIface, target, visited))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The ImplementsInterface
        /// </summary>
        private static bool ImplementsInterface(StaticInstance classType, StaticInstance targetInterface)
        {
            StaticInstance? current = classType;
            HashSet<StaticInstance> visitedInterfaces = new();

            while (current != null)
            {
                foreach (StaticInstance iface in EnumerateDirectInterfaces(current))
                {
                    if (InterfaceExtendsOrEquals(iface, targetInterface, visitedInterfaces))
                        return true;
                }

                current = TryGetStaticField(current, "__base", out object? bObj) && bObj is StaticInstance baseType
                    ? baseType
                    : null;
            }

            return false;
        }

        /// <summary>
        /// The NormalizeVisibilityCode
        /// </summary>
        private static int NormalizeVisibilityCode(object? raw)
        {
            int code = raw switch
            {
                int i => i,
                long l when l >= int.MinValue && l <= int.MaxValue => (int)l,
                short s => s,
                byte b => b,
                BigInteger bi when bi >= int.MinValue && bi <= int.MaxValue => (int)bi,
                _ => VisibilityPublic
            };

            return code switch
            {
                VisibilityPrivate => VisibilityPrivate,
                VisibilityProtected => VisibilityProtected,
                _ => VisibilityPublic
            };
        }

        /// <summary>
        /// The VisibilityLabel
        /// </summary>
        private static string VisibilityLabel(int code)
        {
            return code switch
            {
                VisibilityPrivate => "private",
                VisibilityProtected => "protected",
                _ => "public"
            };
        }

        /// <summary>
        /// The TryGetDeclaredVisibilityCode
        /// </summary>
        private static bool TryGetDeclaredVisibilityCode(
            StaticInstance ownerType,
            bool expectInstance,
            string memberName,
            out int code)
        {
            code = VisibilityPublic;
            string mapName = expectInstance ? "__vis_inst" : "__vis_static";
            if (!TryGetStaticField(ownerType, mapName, out object? mapObj) || mapObj is not Dictionary<string, object> map)
                return false;

            if (!TryGetDictionaryValue(map, memberName, out object? rawCode))
                return false;

            code = NormalizeVisibilityCode(rawCode);
            return true;
        }

        /// <summary>
        /// The TryResolveDeclaredVisibilityInHierarchy
        /// </summary>
        private static bool TryResolveDeclaredVisibilityInHierarchy(
            StaticInstance startType,
            bool expectInstance,
            string memberName,
            out StaticInstance ownerType,
            out int code)
        {
            StaticInstance current = startType;
            while (true)
            {
                if (TryGetDeclaredVisibilityCode(current, expectInstance, memberName, out code))
                {
                    ownerType = current;
                    return true;
                }

                if (!TryGetStaticField(current, "__base", out object? bObj) || bObj is not StaticInstance baseType)
                    break;

                current = baseType;
            }

            ownerType = null!;
            code = VisibilityPublic;
            return false;
        }

        /// <summary>
        /// Checks whether a field is declared as const anywhere in the class hierarchy.
        /// </summary>
        private static bool IsConstFieldInHierarchy(StaticInstance startType, string fieldName, bool isStatic)
        {
            string mapName = isStatic ? "__const_static" : "__const_inst";
            StaticInstance current = startType;
            while (true)
            {
                if (TryGetStaticField(current, mapName, out object? mapObj) &&
                    mapObj is Dictionary<string, object> map &&
                    ContainsDictionaryKey(map, fieldName))
                    return true;

                if (!TryGetStaticField(current, "__base", out object? bObj) || bObj is not StaticInstance baseType)
                    break;

                current = baseType;
            }
            return false;
        }

        /// <summary>
        /// The TryResolveInstanceMemberInHierarchy
        /// </summary>
        private static bool TryResolveInstanceMemberInHierarchy(
            ClassInstance start,
            string memberName,
            out ClassInstance ownerInst,
            out StaticInstance? ownerType,
            out object? value)
        {
            ClassInstance current = start;
            while (true)
            {
                if (TryGetInstanceField(current, memberName, out value))
                {
                    ownerInst = current;
                    ownerType = TryGetStaticType(current, out StaticInstance st) ? st : null;
                    return true;
                }

                if (!TryGetInstanceField(current, "__base", out object? bObj) || bObj is not ClassInstance baseInst)
                    break;

                current = baseInst;
            }

            ownerInst = null!;
            ownerType = null;
            value = null;
            return false;
        }

        /// <summary>
        /// The TryResolveStaticMemberInHierarchy
        /// </summary>
        private static bool TryResolveStaticMemberInHierarchy(
            StaticInstance start,
            string memberName,
            out StaticInstance ownerType,
            out object? value)
        {
            StaticInstance current = start;
            while (true)
            {
                if (TryGetStaticField(current, memberName, out value))
                {
                    ownerType = current;
                    return true;
                }

                if (!TryGetStaticField(current, "__base", out object? bObj) || bObj is not StaticInstance baseType)
                    break;

                current = baseType;
            }

            ownerType = null!;
            value = null;
            return false;
        }

        /// <summary>
        /// The GetCurrentAccessorType
        /// </summary>
        private StaticInstance? GetCurrentAccessorType()
        {
            if (_callStack.Count > 0)
            {
                StaticInstance? frameType = _callStack.Peek().AccessType;
                if (frameType != null)
                    return frameType;
            }

            if (CurrentThis is StaticInstance st)
                return st;

            if (CurrentThis is ClassInstance inst && TryGetStaticType(inst, out StaticInstance st2))
                return st2;

            return null;
        }

        /// <summary>
        /// The IsSameOrDerivedStaticType
        /// </summary>
        private static bool IsSameOrDerivedStaticType(StaticInstance candidate, StaticInstance baseType)
        {
            StaticInstance current = candidate;
            while (true)
            {
                if (ReferenceEquals(current, baseType))
                    return true;

                if (!TryGetStaticField(current, "__base", out object? bObj) || bObj is not StaticInstance parent)
                    break;

                current = parent;
            }

            return false;
        }

        /// <summary>
        /// The IsRuntimeAccessAllowed
        /// </summary>
        private bool IsRuntimeAccessAllowed(StaticInstance ownerType, int visibilityCode)
        {
            if (visibilityCode == VisibilityPublic)
                return true;

            StaticInstance? accessor = GetCurrentAccessorType();
            if (accessor == null)
                return false;

            return visibilityCode switch
            {
                VisibilityPrivate => ReferenceEquals(accessor, ownerType),
                VisibilityProtected => IsSameOrDerivedStaticType(accessor, ownerType),
                _ => true
            };
        }

        /// <summary>
        /// The EnforceRuntimeVisibility
        /// </summary>
        private void EnforceRuntimeVisibility(
            StaticInstance ownerType,
            int visibilityCode,
            string memberName,
            bool isStatic,
            Instruction instr)
        {
            if (visibilityCode == VisibilityPublic)
                return;

            if (IsRuntimeAccessAllowed(ownerType, visibilityCode))
                return;

            string kind = isStatic ? "static" : "instance";
            throw new VMException(
                $"Runtime error: inaccessible {kind} member '{memberName}' in class '{ownerType.ClassName}': '{VisibilityLabel(visibilityCode)}' access",
                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
            );
        }

        /// <summary>
        /// The GetIndexedValue
        /// </summary>
        private object GetIndexedValue(object target, object idxObj, Instruction instr)
        {
            switch (target)
            {
                case System.Threading.Tasks.Task<object> _:
                    {
                        string key = idxObj?.ToString() ?? "";
                        throw new VMException(key == string.Empty ? "Task value encountered; use 'await'"
                                                                    : $"Task value encountered; use 'await' -> ( {key} )"
                                                                    , instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    }

                case List<object> arr:
                    {
                        MarkAsyncHazardForMutableCollection(arr);
                        if (idxObj is string mname && TryBindIntrinsic(arr, mname, out IntrinsicBound? bound, instr))
                            return bound;

                        int index = RequireIntIndex(idxObj, instr);
                        int count = GetListCount(arr);
                        if (index < 0 || index >= count)
                            throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        return GetListValue(arr, index);
                    }

                case FileHandle fh:
                    {
                        if (idxObj is string mname && TryBindIntrinsic(fh, mname, out IntrinsicBound? bound, instr))
                            return bound;
                        throw new VMException($"invalid file member '{idxObj}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    }

                case ExceptionObject exo:
                    {
                        string key = idxObj?.ToString() ?? "";
                        if (idxObj is string mname && TryBindIntrinsic(exo, mname, out IntrinsicBound? bound, instr))
                            return bound;
                        if (string.Equals(key, "message$", StringComparison.Ordinal)) return exo.eMessage;
                        if (string.Equals(key, "type$", StringComparison.Ordinal)) return exo.Type;
                        throw new VMException($"invalid member '{key}' on Exception", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    }

                case string strv:
                    {
                        if (idxObj is string mname && TryBindIntrinsic(strv, mname, out IntrinsicBound? bound, instr))
                            return bound;

                        int index = RequireIntIndex(idxObj, instr);
                        if (index < 0 || index >= strv.Length)
                            throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        return (char)strv[index];
                    }

                case Dictionary<string, object> dict:
                    {
                        MarkAsyncHazardForMutableCollection(dict);
                        if (idxObj is string mname && TryBindIntrinsic(dict, mname, out IntrinsicBound? bound, instr))
                            return bound;
                        string key = idxObj?.ToString() ?? "";
                        if (TryGetDictionaryValue(dict, key, out object? val))
                            return val!;
                        return null!;
                    }

                case ClassInstance obj:
                    {
                        MarkCurrentAsyncSharedStateHazard();
                        string key = idxObj?.ToString() ?? "";

                        if (IsInstanceDestroyed(obj))
                        {
                            throw new VMException(
                                $"Runtime error: instance of class '{obj.ClassName}' has been destroyed.",
                                instr.Line,
                                instr.Col,
                                instr.OriginFile,
                                IsDebugging,
                                DebugStream!);
                        }

                        if (key == "outer")
                        {
                            if (TryGetInstanceField(obj, "__outer", out object? outerVal))
                                return outerVal!;

                            throw new VMException(
                                "Runtime error: missing '__outer' on instance for 'outer'.",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                            );
                        }
                        if (TryResolveInstanceMemberInHierarchy(obj, key, out _, out StaticInstance? ownerInstanceType, out object? instanceValue))
                        {
                            if (ownerInstanceType != null &&
                                TryGetDeclaredVisibilityCode(ownerInstanceType, expectInstance: true, key, out int visCode))
                            {
                                EnforceRuntimeVisibility(ownerInstanceType, visCode, key, isStatic: false, instr);
                            }

                            if (instanceValue is Closure clos &&
                                clos.Parameters.Count > 0 && clos.Parameters[0] == "this")
                            {
                                return new BoundMethod(clos, obj, ownerInstanceType);
                            }

                            return instanceValue!;
                        }

                        if (TryGetStaticType(obj, out StaticInstance objType) &&
                            TryResolveStaticMemberInHierarchy(objType, key, out StaticInstance ownerStaticType, out object? staticValue))
                        {
                            if (TryGetDeclaredVisibilityCode(ownerStaticType, expectInstance: false, key, out int visCode))
                                EnforceRuntimeVisibility(ownerStaticType, visCode, key, isStatic: true, instr);

                            if (staticValue is Closure sClos)
                            {
                                if ((sClos.Parameters.Count > 0 && sClos.Parameters[0] == "type") ||
                                    string.Equals(key, "new", StringComparison.Ordinal))
                                {
                                    return new BoundMethod(sClos, objType, ownerStaticType);
                                }

                                return staticValue;
                            }

                            if (staticValue is StaticInstance nestedType)
                                return new BoundType(nestedType, obj);

                            return staticValue!;
                        }

                        throw new VMException($"Runtime error: invalid instance member '{key}' in class '{obj.ClassName}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    }

                case StaticInstance st:
                    {
                        MarkCurrentAsyncSharedStateHazard();
                        string key = idxObj?.ToString() ?? "";

                        if (key == "outer")
                        {
                            throw new VMException(
                                $"Runtime error: invalid static member 'outer' in class '{st.ClassName}'.",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                            );
                        }
                        if (TryResolveStaticMemberInHierarchy(st, key, out StaticInstance ownerStaticType, out object? staticValue))
                        {
                            if (TryGetDeclaredVisibilityCode(ownerStaticType, expectInstance: false, key, out int visCode))
                                EnforceRuntimeVisibility(ownerStaticType, visCode, key, isStatic: true, instr);

                            if (staticValue is Closure clos &&
                                ((clos.Parameters.Count > 0 && clos.Parameters[0] == "type") ||
                                 string.Equals(key, "new", StringComparison.Ordinal)))
                            {
                                return new BoundMethod(clos, st, ownerStaticType);
                            }

                            return staticValue!;
                        }

                        throw new VMException(
                            $"Runtime error: invalid static member '{key}' in class '{st.ClassName}'.",
                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                        );
                    }
                case EnumInstance en:
                    {
                        string key = idxObj?.ToString() ?? "";

                        if (string.Equals(key, "name", StringComparison.Ordinal))
                        {
                            return new IntrinsicBound(new IntrinsicMethod("name", 0, 1, (recv, args, ins) =>
                            {
                                if (args.Count == 0) return en.EnumName;
                                return (from sk in en.Values where sk.Value == (int)args[0] select sk.Key).FirstOrDefault() ?? "null";
                            }), en);
                        }
                        if (string.Equals(key, "contains", StringComparison.Ordinal))
                        {
                            return new IntrinsicBound(new IntrinsicMethod("contains", 1, 1, (recv, args, ins) =>
                            {
                                string m = args[0]?.ToString() ?? "";
                                return en.Values.ContainsKey(m);
                            }), en);
                        }
                        if (en.TryGet(key, out int enumVal))
                            return enumVal;

                        throw new VMException($"Runtime error: invalid enum member '{key}' in enum '{en.EnumName}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    }

                default:
                    if (idxObj is string defName && TryBindIntrinsic(target, defName, out IntrinsicBound? defbound, instr))
                        return defbound;
                    throw CreateIndexException(target, idxObj, instr);
            }
        }

        /// <summary>
        /// The SetIndexedValue
        /// </summary>
        private void SetIndexedValue(ref object target, object idxObj, object value, Instruction instr, bool allowReservedRuntimeSlotWrites = false)
        {
            switch (target)
            {
                case List<object> arr:
                    {
                        MarkAsyncHazardForMutableCollection(arr);
                        if (IsReservedIntrinsicName(arr, idxObj))
                            throw new VMException($"Runtime error: cannot assign to array intrinsic '{idxObj}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        int index = RequireIntIndex(idxObj, instr);
                        int count = GetListCount(arr);
                        if (index < 0 || index >= count)
                            throw new VMException($"Runtime error: index {index} out of range (0..{count - 1})", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        SetListValue(arr, index, value);
                        break;
                    }

                case string _:
                    throw new VMException("Runtime error: INDEX_SET on string. Strings are immutable.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                case Dictionary<string, object> dict:
                    {
                        MarkAsyncHazardForMutableCollection(dict);
                        string key = NormalizeDictionaryWriteKey(dict, idxObj, instr);
                        SetDictionaryValue(dict, key, value);
                        break;
                    }

                case ClassInstance obj:
                    {
                        MarkCurrentAsyncSharedStateHazard();
                        string key = idxObj?.ToString() ?? "";
                        if (key.Length == 0)
                            throw new VMException("Runtime error: field name cannot be empty", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        if (IsInstanceDestroyed(obj) && !allowReservedRuntimeSlotWrites)
                        {
                            throw new VMException(
                                $"Runtime error: instance of class '{obj.ClassName}' has been destroyed.",
                                instr.Line,
                                instr.Col,
                                instr.OriginFile,
                                IsDebugging,
                                DebugStream!);
                        }

                        if (IsReservedRuntimeSlotName(key) && !allowReservedRuntimeSlotWrites)
                        {
                            throw new VMException(
                                $"Runtime error: cannot assign to reserved runtime member '{key}'",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                            );
                        }

                        if (key == "outer")
                        {
                            throw new VMException(
                                "Runtime error: cannot assign to 'outer' (read-only).",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                            );
                        }

                        if (!allowReservedRuntimeSlotWrites &&
                            TryGetStaticType(obj, out StaticInstance objType) &&
                            TryResolveDeclaredVisibilityInHierarchy(objType, expectInstance: true, key, out StaticInstance ownerType, out int visCode))
                        {
                            EnforceRuntimeVisibility(ownerType, visCode, key, isStatic: false, instr);
                        }

                        if (!allowReservedRuntimeSlotWrites &&
                            TryGetStaticType(obj, out StaticInstance constCheckType) &&
                            IsConstFieldInHierarchy(constCheckType, key, isStatic: false))
                        {
                            throw new VMException(
                                $"Runtime error: cannot assign to const field '{key}'.",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

                        SetInstanceField(obj, key, value);
                        break;
                    }

                case StaticInstance st:
                    {
                        MarkCurrentAsyncSharedStateHazard();
                        string key = idxObj?.ToString() ?? "";
                        if (key.Length == 0)
                            throw new VMException("Runtime error: static member name cannot be empty", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        if (IsReservedRuntimeSlotName(key) && !allowReservedRuntimeSlotWrites)
                        {
                            throw new VMException(
                                $"Runtime error: cannot assign to reserved runtime member '{key}'",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                            );
                        }

                        if (key == "outer")
                        {
                            throw new VMException(
                                "Runtime error: cannot assign to 'outer' on static type.",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                            );
                        }

                        if (!allowReservedRuntimeSlotWrites &&
                            TryResolveDeclaredVisibilityInHierarchy(st, expectInstance: false, key, out StaticInstance ownerType, out int visCode))
                        {
                            EnforceRuntimeVisibility(ownerType, visCode, key, isStatic: true, instr);
                        }

                        if (!allowReservedRuntimeSlotWrites &&
                            IsConstFieldInHierarchy(st, key, isStatic: true))
                        {
                            throw new VMException(
                                $"Runtime error: cannot assign to const static field '{key}'.",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

                        SetStaticField(st, key, value);
                        break;
                    }

                case System.Threading.Tasks.Task<object> _:
                    {
                        string key = idxObj?.ToString() ?? "";
                        throw new VMException(key == string.Empty ? "Task value encountered; use 'await'"
                                                                    : $"Task value encountered; use 'await' -> ( {key} )"
                                                                    , instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    }

                case EnumInstance en:
                    throw new VMException($"Runtime error: cannot assign to enum '{en.EnumName}' members", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                case null:
                    throw new VMException("Runtime error: INDEX_SET on null target", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                default:
                    throw new VMException("Runtime error: target is not index-assignable", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }
        }

        /// <summary>
        /// Handles the INDEX_GET opcode.
        /// </summary>
        private StepResult HandleIndexGetInstruction(Instruction instr)
        {
            if (instr.Operand is string)
                RequireStack(1, instr, "INDEX_GET");
            else
                RequireStack(2, instr, "INDEX_GET");

            object idxObj = _stack.Pop();
            object target;

            if (instr.Operand is string nameFromEnv)
            {
                Env owner = FindEnvWithLocal(nameFromEnv)
                    ?? throw new VMException($"Runtime error: undefined variable '{nameFromEnv}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                MarkAsyncHazardForEnvAccess(owner);
                target = GetLocalVar(owner, nameFromEnv)!;
            }
            else
            {
                target = _stack.Pop();
            }

            _stack.Push(GetIndexedValue(target, idxObj, instr));
            return StepResult.Next;
        }

        /// <summary>
        /// Handles the INDEX_SET and INDEX_SET_INTERNAL opcodes.
        /// </summary>
        private StepResult HandleIndexSetInstruction(Instruction instr)
        {
            bool allowReservedRuntimeSlotWrites = instr.Code == OpCode.INDEX_SET_INTERNAL;
            if (instr.Operand is string)
                RequireStack(2, instr, "INDEX_SET");
            else
                RequireStack(3, instr, "INDEX_SET");

            object value = _stack.Pop();
            object idxObj = _stack.Pop();
            object target;

            if (instr.Operand is string nameFromEnv)
            {
                Env env = FindEnvWithLocal(nameFromEnv)
                    ?? throw new VMException($"Runtime error: undefined variable '{nameFromEnv}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                MarkAsyncHazardForEnvAccess(env);
                target = GetLocalVar(env, nameFromEnv)!;
                SetIndexedValue(ref target, idxObj, value, instr, allowReservedRuntimeSlotWrites);
                SetLocalVar(env, nameFromEnv, target);
            }
            else
            {
                target = _stack.Pop();
                SetIndexedValue(ref target, idxObj, value, instr, allowReservedRuntimeSlotWrites);
            }

            return StepResult.Next;
        }
    }
}
