using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;
using CFGS_VM.VMCore.Extensions.Intrinsics.Core;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;
using CFGS_VM.VMCore.Interop;
using CFGS_VM.VMCore.Plugin;
using System.Globalization;
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
                if (TryResolveInstanceMemberValueInHierarchy(obj, "destroy", out ClassInstance ownerInst, out StaticInstance? ownerType, out Value memberValue) &&
                    memberValue.Reference is Closure clos &&
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

                    InvokeClosureSyncValue(clos, [], instr, ownerInst, ownerType);
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

            object? result = InvokeIntrinsicForCall(bound.Method, bound.Receiver, new List<Value>(), instr);
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
            return TryGetInstanceFieldValue(obj, slotName, out Value raw) &&
                   raw.Kind == ValueKind.Bool &&
                   raw.AsBool;
        }

        /// <summary>
        /// The SetRuntimeFlag.
        /// </summary>
        private static void SetRuntimeFlag(ClassInstance obj, string slotName, bool value)
        {
            SetInstanceFieldValue(obj, slotName, Value.FromBool(value));
        }

        /// <summary>
        /// Tries to read an instance field.
        /// </summary>
        private static bool TryGetInstanceFieldValue(ClassInstance obj, string name, out Value value)
        {
            Value local = Value.Null;
            bool found = WithMutableLock(obj, () => obj.Fields.TryGetValue(name, out local));
            value = local;
            return found;
        }

        /// <summary>
        /// Writes an instance field.
        /// </summary>
        private static void SetInstanceFieldValue(ClassInstance obj, string name, Value value)
            => WithMutableLock(obj, () => obj.Fields[name] = value);

        /// <summary>
        /// Returns a snapshot copy of instance fields.
        /// </summary>
        private static List<KeyValuePair<string, object>> SnapshotInstanceFields(ClassInstance obj)
            => WithMutableLock(obj, () => obj.Fields
                .Select(static kv => new KeyValuePair<string, object>(kv.Key, kv.Value.ToObject()!))
                .ToList());

        /// <summary>
        /// Tries to read a static field.
        /// </summary>
        private static bool TryGetStaticFieldValue(StaticInstance st, string name, out Value value)
        {
            Value local = Value.Null;
            bool found = WithMutableLock(st, () => st.Fields.TryGetValue(name, out local));
            value = local;
            return found;
        }

        /// <summary>
        /// Writes a static field.
        /// </summary>
        private static void SetStaticFieldValue(StaticInstance st, string name, Value value)
            => WithMutableLock(st, () => st.Fields[name] = value);

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

        private static bool IsMemberAccessInstruction(Instruction instr)
            => instr.Operand is bool memberAccess && memberAccess;

        private const int VisibilityPublic = 0;

        private const int VisibilityPrivate = 1;

        private const int VisibilityProtected = 2;

        /// <summary>
        /// Defines property write modes.
        /// </summary>
        private enum PropertyWriteMode
        {
            Normal,
            Init
        }

        private static bool TryGetStaticType(ClassInstance inst, out StaticInstance type)
        {
            if (TryGetInstanceFieldValue(inst, "__type", out Value typeValue) && typeValue.Reference is StaticInstance st)
            {
                type = st;
                return true;
            }

            type = null!;
            return false;
        }

        private static bool TryFindInstanceLayerForType(ClassInstance start, StaticInstance targetType, out ClassInstance layer)
        {
            ClassInstance current = start;
            while (true)
            {
                if (TryGetStaticType(current, out StaticInstance currentType) && ReferenceEquals(currentType, targetType))
                {
                    layer = current;
                    return true;
                }

                if (!TryGetInstanceFieldValue(current, "__base", out Value baseValue) || baseValue.Reference is not ClassInstance baseInst)
                    break;

                current = baseInst;
            }

            layer = null!;
            return false;
        }

        private static bool IsInterfaceType(StaticInstance type)
        {
            if (!TryGetStaticFieldValue(type, "__is_interface", out Value raw))
                return false;

            return raw.Kind switch
            {
                ValueKind.Bool => raw.AsBool,
                ValueKind.Int => raw.AsInt != 0,
                ValueKind.Long => raw.AsLong != 0,
                ValueKind.Char => raw.AsChar != '\0',
                ValueKind.Ref => raw.Reference switch
                {
                    null => false,
                    bool b => b,
                    short s => s != 0,
                    byte b => b != 0,
                    BigInteger bi => bi != BigInteger.Zero,
                    _ => true
                },
                _ => true
            };
        }

        /// <summary>
        /// Enumerates direct interfaces attached to a class or interface static descriptor.
        /// </summary>
        private static IEnumerable<StaticInstance> EnumerateDirectInterfaces(StaticInstance type)
        {
            if (!TryGetStaticFieldValue(type, "__interfaces", out Value interfacesValue) || interfacesValue.Reference is not List<object> entries)
                yield break;

            foreach (object? entry in SnapshotList(entries))
            {
                if (entry is StaticInstance iface)
                    yield return iface;
            }
        }

        private static IEnumerable<StaticInstance> EnumerateInterfaceSelfAndBases(StaticInstance iface)
        {
            HashSet<StaticInstance> visited = new();
            foreach (StaticInstance current in EnumerateInterfaceSelfAndBasesCore(iface, visited))
                yield return current;
        }

        private static IEnumerable<StaticInstance> EnumerateInterfaceSelfAndBasesCore(StaticInstance iface, HashSet<StaticInstance> visited)
        {
            if (!visited.Add(iface))
                yield break;

            yield return iface;

            foreach (StaticInstance baseIface in EnumerateDirectInterfaces(iface))
            {
                foreach (StaticInstance current in EnumerateInterfaceSelfAndBasesCore(baseIface, visited))
                    yield return current;
            }
        }

        private static IEnumerable<string> EnumerateRuntimeTypeNames(StaticInstance type)
        {
            HashSet<string> yielded = new(StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(type.ClassName) && yielded.Add(type.ClassName))
                yield return type.ClassName;

            if (TryGetStaticFieldValue(type, "__qualified_name", out Value qualifiedNameValue) &&
                qualifiedNameValue.ToObject() is string qualifiedName &&
                !string.IsNullOrWhiteSpace(qualifiedName) &&
                yielded.Add(qualifiedName))
            {
                yield return qualifiedName;
            }
        }

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

                current = TryGetStaticFieldValue(current, "__base", out Value baseValue) && baseValue.Reference is StaticInstance baseType
                    ? baseType
                    : null;
            }

            return false;
        }

        private bool StructurallyImplementsInterface(ClassInstance instance, StaticInstance instanceType, StaticInstance targetInterface)
        {
            if (!IsInterfaceType(targetInterface))
                return false;

            return StructurallyImplementsInterfaceCore(instance, instanceType, targetInterface, new HashSet<StaticInstance>());
        }

        private bool StructurallyImplementsInterfaceCore(
            ClassInstance instance,
            StaticInstance instanceType,
            StaticInstance iface,
            HashSet<StaticInstance> visited)
        {
            if (!visited.Add(iface))
                return true;

            foreach (StaticInstance baseIface in EnumerateDirectInterfaces(iface))
            {
                if (!StructurallyImplementsInterfaceCore(instance, instanceType, baseIface, visited))
                    return false;
            }

            return StructuralMethodsMatch(instance, instanceType, iface) &&
                   StructuralPropertiesMatch(instance, instanceType, iface) &&
                   StructuralIndexersMatch(instance, instanceType, iface);
        }

        private bool StructuralMethodsMatch(ClassInstance instance, StaticInstance instanceType, StaticInstance iface)
        {
            foreach (Dictionary<string, object> method in ReadInterfaceMetadataList(iface, "__interface_methods"))
            {
                string name = MetadataString(method, "name");
                bool isStatic = MetadataBool(method, "static");
                int arity = MetadataInt(method, "arity");
                int minArgs = MetadataInt(method, "minArgs");
                bool isAsync = MetadataBool(method, "async");

                StaticInstance? ownerType;
                if (isStatic)
                {
                    if (!TryResolveStaticMemberValueInHierarchy(instanceType, name, out StaticInstance staticOwner, out Value memberValue) ||
                        memberValue.Reference is not Closure staticClosure ||
                        !IsPublicMember(staticOwner, expectInstance: false, name) ||
                        !ClosureShapeMatches(staticClosure, arity, minArgs, isAsync))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!TryResolveInstanceMemberValueInHierarchy(instance, name, out _, out ownerType, out Value memberValue) ||
                        memberValue.Reference is not Closure closure ||
                        ownerType == null ||
                        !IsPublicMember(ownerType, expectInstance: true, name) ||
                        !ClosureShapeMatches(closure, arity, minArgs, isAsync))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool StructuralPropertiesMatch(ClassInstance instance, StaticInstance instanceType, StaticInstance iface)
        {
            foreach (Dictionary<string, object> property in ReadInterfaceMetadataList(iface, "__interface_properties"))
            {
                string name = MetadataString(property, "name");
                bool isStatic = MetadataBool(property, "static");

                if (isStatic)
                {
                    if (!TryResolveStaticPropertyInHierarchy(instanceType, name, out StaticInstance ownerType, out _))
                        return false;

                    if (!IsPublicMember(ownerType, expectInstance: false, name))
                        return false;
                }
                else
                {
                    if (!TryResolveInstancePropertyInHierarchy(instance, name, out _, out StaticInstance ownerType, out _))
                        return false;

                    if (!IsPublicMember(ownerType, expectInstance: true, name))
                        return false;
                }
            }

            return true;
        }

        private bool StructuralIndexersMatch(ClassInstance instance, StaticInstance instanceType, StaticInstance iface)
        {
            foreach (Dictionary<string, object> indexer in ReadInterfaceMetadataList(iface, "__interface_indexers"))
            {
                string methodName = MetadataString(indexer, "method");
                int arity = MetadataInt(indexer, "arity");
                int minArgs = MetadataInt(indexer, "minArgs");

                if (!TryResolveInstanceMemberValueInHierarchy(instance, methodName, out _, out StaticInstance? ownerType, out Value memberValue) ||
                    memberValue.Reference is not Closure closure ||
                    ownerType == null ||
                    !IsPublicMember(ownerType, expectInstance: true, methodName) ||
                    !ClosureShapeMatches(closure, arity, minArgs, isAsync: false))
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<Dictionary<string, object>> ReadInterfaceMetadataList(StaticInstance iface, string slot)
        {
            if (!TryGetStaticFieldValue(iface, slot, out Value slotValue) || slotValue.Reference is not List<object> entries)
                yield break;

            foreach (object? entry in SnapshotList(entries))
            {
                if (entry is Dictionary<string, object> dict)
                    yield return dict;
            }
        }

        private static bool InterfaceDeclaresInstanceMethod(StaticInstance iface, string methodName)
        {
            foreach (StaticInstance contractIface in EnumerateInterfaceSelfAndBases(iface))
            {
                foreach (Dictionary<string, object> method in ReadInterfaceMetadataList(contractIface, "__interface_methods"))
                {
                    if (MetadataBool(method, "static"))
                        continue;

                    if (string.Equals(MetadataString(method, "name"), methodName, StringComparison.Ordinal))
                        return true;
                }
            }

            return false;
        }

        private static string MetadataString(Dictionary<string, object> metadata, string key)
            => metadata.TryGetValue(key, out object? value) ? value?.ToString() ?? string.Empty : string.Empty;

        private static bool MetadataBool(Dictionary<string, object> metadata, string key)
            => metadata.TryGetValue(key, out object? value) && IsTruthyMetadataValue(value);

        private static int MetadataInt(Dictionary<string, object> metadata, string key)
            => metadata.TryGetValue(key, out object? value) ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : 0;

        private static bool IsTruthyMetadataValue(object? value)
            => value switch
            {
                bool b => b,
                int i => i != 0,
                long l => l != 0,
                short s => s != 0,
                byte b => b != 0,
                BigInteger bi => bi != BigInteger.Zero,
                _ => value != null
            };

        private static bool ClosureShapeMatches(Closure closure, int arity, int minArgs, bool isAsync)
        {
            int receiverOffset = closure.Parameters.Count > 0 &&
                (string.Equals(closure.Parameters[0], "this", StringComparison.Ordinal) ||
                 string.Equals(closure.Parameters[0], "type", StringComparison.Ordinal))
                ? 1
                : 0;

            return closure.Parameters.Count - receiverOffset == arity &&
                   Math.Max(0, closure.MinArgs - receiverOffset) == minArgs &&
                   closure.IsAsync == isAsync;
        }

        private static bool IsPublicMember(StaticInstance ownerType, bool expectInstance, string memberName)
        {
            if (!TryGetDeclaredVisibilityCode(ownerType, expectInstance, memberName, out int visCode))
                return true;

            return NormalizeVisibilityCode(visCode) == VisibilityPublic;
        }

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

        private static string VisibilityLabel(int code)
        {
            return code switch
            {
                VisibilityPrivate => "private",
                VisibilityProtected => "protected",
                _ => "public"
            };
        }

        private static bool TryGetDeclaredVisibilityCode(
            StaticInstance ownerType,
            bool expectInstance,
            string memberName,
            out int code)
        {
            code = VisibilityPublic;
            string mapName = expectInstance ? "__vis_inst" : "__vis_static";
            if (!TryGetStaticFieldValue(ownerType, mapName, out Value mapValue) || mapValue.Reference is not Dictionary<string, object> map)
                return false;

            if (!TryGetDictionaryValue(map, memberName, out object? rawCode))
                return false;

            code = NormalizeVisibilityCode(rawCode);
            return true;
        }

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

                if (!TryGetStaticFieldValue(current, "__base", out Value baseValue) || baseValue.Reference is not StaticInstance baseType)
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
                if (TryGetStaticFieldValue(current, mapName, out Value mapValue) &&
                    mapValue.Reference is Dictionary<string, object> map &&
                    ContainsDictionaryKey(map, fieldName))
                {
                    return true;
                }

                if (!TryGetStaticFieldValue(current, "__base", out Value baseValue) || baseValue.Reference is not StaticInstance baseType)
                    break;

                current = baseType;
            }
            return false;
        }

        private static bool TryGetDeclaredPropertyDescriptor(
            StaticInstance ownerType,
            bool isStatic,
            string memberName,
            out RuntimePropertyDescriptor descriptor)
        {
            string mapName = isStatic ? "__props_static" : "__props_inst";
            if (TryGetStaticFieldValue(ownerType, mapName, out Value mapValue) &&
                mapValue.Reference is Dictionary<string, object> map &&
                TryGetDictionaryValue(map, memberName, out object? raw) &&
                raw is RuntimePropertyDescriptor runtimePropertyDescriptor)
            {
                descriptor = runtimePropertyDescriptor;
                return true;
            }

            descriptor = null!;
            return false;
        }

        private static bool TryResolveInstancePropertyInHierarchy(
            ClassInstance start,
            string memberName,
            out ClassInstance ownerInst,
            out StaticInstance ownerType,
            out RuntimePropertyDescriptor descriptor)
        {
            ClassInstance current = start;
            while (true)
            {
                if (TryGetStaticType(current, out StaticInstance currentType) &&
                    TryGetDeclaredPropertyDescriptor(currentType, isStatic: false, memberName, out descriptor))
                {
                    ownerInst = current;
                    ownerType = currentType;
                    return true;
                }

                if (!TryGetInstanceFieldValue(current, "__base", out Value baseValue) || baseValue.Reference is not ClassInstance baseInst)
                    break;

                current = baseInst;
            }

            ownerInst = null!;
            ownerType = null!;
            descriptor = null!;
            return false;
        }

        private static bool TryResolveStaticPropertyInHierarchy(
            StaticInstance start,
            string memberName,
            out StaticInstance ownerType,
            out RuntimePropertyDescriptor descriptor)
        {
            StaticInstance current = start;
            while (true)
            {
                if (TryGetDeclaredPropertyDescriptor(current, isStatic: true, memberName, out descriptor))
                {
                    ownerType = current;
                    return true;
                }

                if (!TryGetStaticFieldValue(current, "__base", out Value baseValue) || baseValue.Reference is not StaticInstance baseType)
                    break;

                current = baseType;
            }

            ownerType = null!;
            descriptor = null!;
            return false;
        }

        private static bool TryResolveInstanceMemberValueInHierarchy(
            ClassInstance start,
            string memberName,
            out ClassInstance ownerInst,
            out StaticInstance? ownerType,
            out Value value)
        {
            ClassInstance current = start;
            while (true)
            {
                if (TryGetInstanceFieldValue(current, memberName, out Value memberValue))
                {
                    ownerInst = current;
                    ownerType = TryGetStaticType(current, out StaticInstance st) ? st : null;
                    value = memberValue;
                    return true;
                }

                if (!TryGetInstanceFieldValue(current, "__base", out Value baseValue) || baseValue.Reference is not ClassInstance baseInst)
                    break;

                current = baseInst;
            }

            ownerInst = null!;
            ownerType = null;
            value = Value.Null;
            return false;
        }

        private static bool TryResolveStaticMemberValueInHierarchy(
            StaticInstance start,
            string memberName,
            out StaticInstance ownerType,
            out Value value)
        {
            StaticInstance current = start;
            while (true)
            {
                if (TryGetStaticFieldValue(current, memberName, out Value memberValue))
                {
                    ownerType = current;
                    value = memberValue;
                    return true;
                }

                if (!TryGetStaticFieldValue(current, "__base", out Value baseValue) || baseValue.Reference is not StaticInstance baseType)
                    break;

                current = baseType;
            }

            ownerType = null!;
            value = Value.Null;
            return false;
        }

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

        private static bool IsSameOrDerivedStaticType(StaticInstance candidate, StaticInstance baseType)
        {
            StaticInstance current = candidate;
            while (true)
            {
                if (ReferenceEquals(current, baseType))
                    return true;

                if (!TryGetStaticFieldValue(current, "__base", out Value baseValue) || baseValue.Reference is not StaticInstance parent)
                    break;

                current = parent;
            }

            return false;
        }

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

        private void EnforcePropertyAccessorVisibility(
            StaticInstance ownerType,
            int visibilityCode,
            string propertyName,
            bool isStatic,
            string accessorLabel,
            Instruction instr)
        {
            if (visibilityCode == VisibilityPublic)
                return;

            if (IsRuntimeAccessAllowed(ownerType, visibilityCode))
                return;

            string kind = isStatic ? "static" : "instance";
            throw new VMException(
                $"Runtime error: inaccessible {accessorLabel} for {kind} property '{propertyName}' in class '{ownerType.ClassName}': '{VisibilityLabel(visibilityCode)}' access",
                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
            );
        }

        private Closure GetPropertyAccessorClosure(object owner, string slotName, string propertyName, string accessorLabel, Instruction instr)
        {
            Closure? closure = owner switch
            {
                ClassInstance inst when TryGetInstanceFieldValue(inst, slotName, out Value value) => value.Reference as Closure,
                StaticInstance st when TryGetStaticFieldValue(st, slotName, out Value value) => value.Reference as Closure,
                _ => null
            };

            if (closure != null)
                return closure;

            throw new VMException(
                $"Runtime error: missing {accessorLabel} implementation for property '{propertyName}'.",
                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private bool TryInvokeInstanceIndexerGetter(ClassInstance obj, Value idxValue, Instruction instr, out Value value)
        {
            value = Value.Null;
            if (!TryResolveInstanceMemberValueInHierarchy(obj, "get_index", out _, out StaticInstance? ownerType, out Value memberValue) ||
                memberValue.Reference is not Closure closure)
            {
                return false;
            }

            if (ownerType != null && TryGetDeclaredVisibilityCode(ownerType, expectInstance: true, "get_index", out int visCode))
                EnforceRuntimeVisibility(ownerType, visCode, "get_index", isStatic: false, instr);

            value = InvokeClosureSyncValue(closure, [idxValue], instr, obj, ownerType);
            return true;
        }

        private bool TryInvokeInstanceIndexerSetter(ClassInstance obj, Value idxValue, Value value, Instruction instr)
        {
            if (!TryResolveInstanceMemberValueInHierarchy(obj, "set_index", out _, out StaticInstance? ownerType, out Value memberValue) ||
                memberValue.Reference is not Closure closure)
            {
                return false;
            }

            if (ownerType != null && TryGetDeclaredVisibilityCode(ownerType, expectInstance: true, "set_index", out int visCode))
                EnforceRuntimeVisibility(ownerType, visCode, "set_index", isStatic: false, instr);

            InvokeClosureSyncValue(closure, [idxValue, value], instr, obj, ownerType);
            return true;
        }

        private object? ReadInstanceProperty(
            ClassInstance receiverInst,
            ClassInstance ownerInst,
            StaticInstance ownerType,
            RuntimePropertyDescriptor descriptor,
            Instruction instr)
        {
            if (!descriptor.HasGetter)
            {
                throw new VMException(
                    $"Runtime error: property '{descriptor.Name}' in class '{ownerType.ClassName}' has no getter.",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }

            EnforcePropertyAccessorVisibility(ownerType, descriptor.GetterVisibilityCode, descriptor.Name, isStatic: false, "getter", instr);

            if (descriptor.GetterSlotName != null)
            {
                Closure closure = GetPropertyAccessorClosure(ownerInst, descriptor.GetterSlotName, descriptor.Name, "getter", instr);
                // Property access follows normal instance dispatch semantics: resolution happens on
                // the declaring layer, but accessor bodies execute with the original receiver as
                // 'this' so inherited properties see the derived instance state.
                return InvokeClosureSyncValue(closure, [], instr, receiverInst, ownerType).ToObject();
            }

            if (descriptor.HasAutoStorage && descriptor.BackingFieldName != null)
            {
                return TryGetInstanceFieldValue(ownerInst, descriptor.BackingFieldName, out Value value)
                    ? value.ToObject()
                    : null;
            }

            throw new VMException(
                $"Runtime error: property '{descriptor.Name}' in class '{ownerType.ClassName}' has no runtime getter implementation.",
                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private object? ReadStaticProperty(StaticInstance ownerType, RuntimePropertyDescriptor descriptor, Instruction instr)
        {
            if (!descriptor.HasGetter)
            {
                throw new VMException(
                    $"Runtime error: property '{descriptor.Name}' in class '{ownerType.ClassName}' has no getter.",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }

            EnforcePropertyAccessorVisibility(ownerType, descriptor.GetterVisibilityCode, descriptor.Name, isStatic: true, "getter", instr);

            if (descriptor.GetterSlotName != null)
            {
                Closure closure = GetPropertyAccessorClosure(ownerType, descriptor.GetterSlotName, descriptor.Name, "getter", instr);
                return InvokeClosureSyncValue(closure, [], instr, ownerType, ownerType).ToObject();
            }

            if (descriptor.HasAutoStorage && descriptor.BackingFieldName != null)
            {
                return TryGetStaticFieldValue(ownerType, descriptor.BackingFieldName, out Value value)
                    ? value.ToObject()
                    : null;
            }

            throw new VMException(
                $"Runtime error: property '{descriptor.Name}' in class '{ownerType.ClassName}' has no runtime getter implementation.",
                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private void WriteInstanceProperty(
            ClassInstance receiverInst,
            ClassInstance ownerInst,
            StaticInstance ownerType,
            RuntimePropertyDescriptor descriptor,
            object value,
            Instruction instr,
            PropertyWriteMode writeMode)
        {
            bool useInit = writeMode == PropertyWriteMode.Init && descriptor.HasInit;
            if (!useInit && !descriptor.HasSetter)
            {
                string reason = writeMode == PropertyWriteMode.Init ? "has no init or set accessor" : "has no setter";
                throw new VMException(
                    $"Runtime error: property '{descriptor.Name}' in class '{ownerType.ClassName}' {reason}.",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }

            if (useInit)
            {
                EnforcePropertyAccessorVisibility(ownerType, descriptor.InitVisibilityCode, descriptor.Name, isStatic: false, "init accessor", instr);
                if (descriptor.InitSlotName != null)
                {
                    Closure closure = GetPropertyAccessorClosure(ownerInst, descriptor.InitSlotName, descriptor.Name, "init accessor", instr);
                    InvokeClosureSyncValue(closure, [Value.FromObject(value)], instr, receiverInst, ownerType);
                    return;
                }
            }
            else
            {
                EnforcePropertyAccessorVisibility(ownerType, descriptor.SetterVisibilityCode, descriptor.Name, isStatic: false, "setter", instr);
                if (descriptor.SetterSlotName != null)
                {
                    Closure closure = GetPropertyAccessorClosure(ownerInst, descriptor.SetterSlotName, descriptor.Name, "setter", instr);
                    InvokeClosureSyncValue(closure, [Value.FromObject(value)], instr, receiverInst, ownerType);
                    return;
                }
            }

            if (descriptor.HasAutoStorage && descriptor.BackingFieldName != null)
            {
                SetInstanceFieldValue(ownerInst, descriptor.BackingFieldName, Value.FromObject(value));
                return;
            }

            throw new VMException(
                $"Runtime error: property '{descriptor.Name}' in class '{ownerType.ClassName}' has no runtime write implementation.",
                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private void WriteStaticProperty(
            StaticInstance ownerType,
            RuntimePropertyDescriptor descriptor,
            object value,
            Instruction instr,
            PropertyWriteMode writeMode)
        {
            bool useInit = writeMode == PropertyWriteMode.Init && descriptor.HasInit;
            if (!useInit && !descriptor.HasSetter)
            {
                string reason = writeMode == PropertyWriteMode.Init ? "has no init or set accessor" : "has no setter";
                throw new VMException(
                    $"Runtime error: property '{descriptor.Name}' in class '{ownerType.ClassName}' {reason}.",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }

            if (useInit)
            {
                EnforcePropertyAccessorVisibility(ownerType, descriptor.InitVisibilityCode, descriptor.Name, isStatic: true, "init accessor", instr);
                if (descriptor.InitSlotName != null)
                {
                    Closure closure = GetPropertyAccessorClosure(ownerType, descriptor.InitSlotName, descriptor.Name, "init accessor", instr);
                    InvokeClosureSyncValue(closure, [Value.FromObject(value)], instr, ownerType, ownerType);
                    return;
                }
            }
            else
            {
                EnforcePropertyAccessorVisibility(ownerType, descriptor.SetterVisibilityCode, descriptor.Name, isStatic: true, "setter", instr);
                if (descriptor.SetterSlotName != null)
                {
                    Closure closure = GetPropertyAccessorClosure(ownerType, descriptor.SetterSlotName, descriptor.Name, "setter", instr);
                    InvokeClosureSyncValue(closure, [Value.FromObject(value)], instr, ownerType, ownerType);
                    return;
                }
            }

            if (descriptor.HasAutoStorage && descriptor.BackingFieldName != null)
            {
                SetStaticFieldValue(ownerType, descriptor.BackingFieldName, Value.FromObject(value));
                return;
            }

            throw new VMException(
                $"Runtime error: property '{descriptor.Name}' in class '{ownerType.ClassName}' has no runtime write implementation.",
                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private object GetIndexedValue(object target, object idxObj, Instruction instr)
        {
            switch (target)
            {
                case SuperReference sr:
                {
                    // Resolve the member on the base layer (skips a derived override, uses base
                    // visibility), then rebind the bound method's receiver to the derived instance so
                    // the base method runs with the real 'this' (C7).
                    object superRes = GetIndexedValue(sr.BaseLayer, idxObj, instr);
                    return superRes is BoundMethod superBound && ReferenceEquals(superBound.Receiver, sr.BaseLayer)
                        ? new BoundMethod(superBound.Function, sr.Derived, superBound.DeclaringType)
                        : superRes;
                }

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

                        int count = GetListCount(arr);
                        int index = ResolveElementIndex(idxObj, count, instr);
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

                        int index = ResolveElementIndex(idxObj, strv.Length, instr);
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

                case ClrAssemblyHandle or ClrNamespaceHandle or ClrTypeHandle or ClrObjectHandle or ClrEventHandle:
                    {
                        try
                        {
                            if (!IsMemberAccessInstruction(instr))
                                return NativeInterop.GetIndex(target, [idxObj])!;

                            string key = idxObj?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(key))
                                throw new VMException("Runtime error: native member name cannot be empty", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            return NativeInterop.GetMember(target, key)!;
                        }
                        catch (Exception ex)
                        {
                            string op = IsMemberAccessInstruction(instr) ? "member access" : "index access";
                            throw new VMException($"Runtime error: native interop {op} failed: {ex.Message}", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!, ex);
                        }
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

                        if (!IsMemberAccessInstruction(instr))
                        {
                            if (TryInvokeInstanceIndexerGetter(obj, ToRuntimeValue(idxObj), instr, out Value indexValue))
                                return indexValue.ToObject()!;

                            throw CreateIndexException(target, idxObj!, instr);
                        }

                        if (key == "outer")
                        {
                            if (TryGetInstanceFieldValue(obj, "__outer", out Value outerValue))
                                return outerValue.ToObject()!;

                            throw new VMException(
                                "Runtime error: missing '__outer' on instance for 'outer'.",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                            );
                        }

                        if (TryResolveInstancePropertyInHierarchy(obj, key, out ClassInstance ownerPropertyInst, out StaticInstance ownerPropertyType, out RuntimePropertyDescriptor propertyDescriptor))
                            return ReadInstanceProperty(obj, ownerPropertyInst, ownerPropertyType, propertyDescriptor, instr)!;

                        if (TryResolveInstanceMemberValueInHierarchy(obj, key, out _, out StaticInstance? ownerInstanceType, out Value instanceValue))
                        {
                            if (ownerInstanceType != null &&
                                TryGetDeclaredVisibilityCode(ownerInstanceType, expectInstance: true, key, out int visCode))
                            {
                                EnforceRuntimeVisibility(ownerInstanceType, visCode, key, isStatic: false, instr);
                            }

                            if (instanceValue.Reference is Closure clos &&
                                clos.Parameters.Count > 0 && clos.Parameters[0] == "this")
                            {
                                return new BoundMethod(clos, obj, ownerInstanceType);
                            }

                            return instanceValue.ToObject()!;
                        }

                        if (TryGetStaticType(obj, out StaticInstance objType) &&
                            TryResolveStaticMemberValueInHierarchy(objType, key, out StaticInstance ownerStaticType, out Value staticValue))
                        {
                            if (TryGetDeclaredVisibilityCode(ownerStaticType, expectInstance: false, key, out int visCode))
                                EnforceRuntimeVisibility(ownerStaticType, visCode, key, isStatic: true, instr);

                            if (staticValue.Reference is Closure sClos)
                            {
                                if ((sClos.Parameters.Count > 0 && sClos.Parameters[0] == "type") ||
                                    string.Equals(key, "new", StringComparison.Ordinal))
                                {
                                    return new BoundMethod(sClos, objType, ownerStaticType);
                                }

                                return staticValue.Reference;
                            }

                            if (staticValue.Reference is StaticInstance nestedType)
                                return new BoundType(nestedType, obj);

                            return staticValue.ToObject()!;
                        }

                        throw new VMException($"Runtime error: invalid instance member '{key}' in class '{obj.ClassName}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    }

                case StaticInstance st:
                    {
                        if (!IsMemberAccessInstruction(instr))
                            throw CreateIndexException(target, idxObj, instr);

                        MarkCurrentAsyncSharedStateHazard();
                        string key = idxObj?.ToString() ?? "";

                        if (key == "outer")
                        {
                            throw new VMException(
                                $"Runtime error: invalid static member 'outer' in class '{st.ClassName}'.",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                            );
                        }

                        if (TryResolveStaticPropertyInHierarchy(st, key, out StaticInstance ownerPropertyType, out RuntimePropertyDescriptor propertyDescriptor))
                            return ReadStaticProperty(ownerPropertyType, propertyDescriptor, instr)!;

                        if (TryResolveStaticMemberValueInHierarchy(st, key, out StaticInstance ownerStaticType, out Value staticValue))
                        {
                            if (TryGetDeclaredVisibilityCode(ownerStaticType, expectInstance: false, key, out int visCode))
                                EnforceRuntimeVisibility(ownerStaticType, visCode, key, isStatic: true, instr);

                            if (staticValue.Reference is Closure clos &&
                                ((clos.Parameters.Count > 0 && clos.Parameters[0] == "type") ||
                                 string.Equals(key, "new", StringComparison.Ordinal)))
                            {
                                return new BoundMethod(clos, st, ownerStaticType);
                            }

                            return staticValue.ToObject()!;
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

        private void SetIndexedValue(
            ref object target,
            object idxObj,
            object value,
            Instruction instr,
            PropertyWriteMode writeMode,
            bool allowReservedRuntimeSlotWrites = false)
        {
            switch (target)
            {
                case List<object> arr:
                    {
                        MarkAsyncHazardForMutableCollection(arr);
                        if (IsReservedIntrinsicName(arr, idxObj))
                            throw new VMException($"Runtime error: cannot assign to array intrinsic '{idxObj}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        int count = GetListCount(arr);
                        int index = ResolveElementIndex(idxObj, count, instr);
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

                case ClrTypeHandle or ClrObjectHandle:
                    {
                        try
                        {
                            if (!IsMemberAccessInstruction(instr))
                            {
                                NativeInterop.SetIndex(target, [idxObj], value, this, instr);
                                break;
                            }

                            string key = idxObj?.ToString() ?? "";
                            if (string.IsNullOrWhiteSpace(key))
                                throw new VMException("Runtime error: native member name cannot be empty", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            NativeInterop.SetMember(target, key, value, this, instr);
                        }
                        catch (Exception ex)
                        {
                            string op = IsMemberAccessInstruction(instr) ? "member assignment" : "index assignment";
                            throw new VMException($"Runtime error: native interop {op} failed: {ex.Message}", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!, ex);
                        }

                        break;
                    }

                case ClassInstance obj:
                    {
                        if (!allowReservedRuntimeSlotWrites &&
                            writeMode != PropertyWriteMode.Init &&
                            !IsMemberAccessInstruction(instr))
                        {
                            if (TryInvokeInstanceIndexerSetter(obj, ToRuntimeValue(idxObj), Value.FromObject(value), instr))
                                break;

                            throw new VMException("Runtime error: class instances are not indexable; use dot access for members.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

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
                            TryResolveInstancePropertyInHierarchy(obj, key, out ClassInstance ownerPropertyInst, out StaticInstance ownerPropertyType, out RuntimePropertyDescriptor propertyDescriptor))
                        {
                            WriteInstanceProperty(obj, ownerPropertyInst, ownerPropertyType, propertyDescriptor, value, instr, writeMode);
                            break;
                        }

                        ClassInstance? ownerFieldInst = null;
                        if (!allowReservedRuntimeSlotWrites)
                        {
                            if (!TryResolveInstanceMemberValueInHierarchy(obj, key, out ClassInstance resolvedOwnerInst, out _, out _))
                            {
                                throw new VMException(
                                    $"Runtime error: invalid instance member '{key}' in class '{obj.ClassName}'",
                                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                                );
                            }

                            ownerFieldInst = resolvedOwnerInst;
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

                        // Preserve per-layer field storage: writes to inherited fields must update the
                        // layer that actually declares the field instead of creating a shadowing copy
                        // on the current receiver object.
                        SetInstanceFieldValue(ownerFieldInst ?? obj, key, Value.FromObject(value));
                        break;
                    }

                case StaticInstance st:
                    {
                        if (!allowReservedRuntimeSlotWrites &&
                            writeMode != PropertyWriteMode.Init &&
                            !IsMemberAccessInstruction(instr))
                        {
                            throw new VMException("Runtime error: class static members are not indexable; use dot access for static members.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

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
                            TryResolveStaticPropertyInHierarchy(st, key, out StaticInstance ownerPropertyType, out RuntimePropertyDescriptor propertyDescriptor))
                        {
                            WriteStaticProperty(ownerPropertyType, propertyDescriptor, value, instr, writeMode);
                            break;
                        }

                        if (!allowReservedRuntimeSlotWrites &&
                            !TryResolveStaticMemberValueInHierarchy(st, key, out _, out _))
                        {
                            throw new VMException(
                                $"Runtime error: invalid static member '{key}' in class '{st.ClassName}'.",
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

                        SetStaticFieldValue(st, key, Value.FromObject(value));
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

            object idxObj = PopRuntimeObject()!;
            object target;

            if (instr.Operand is string nameFromEnv)
            {
                if (!TryResolveVarOwnerValue(nameFromEnv, out Env owner, out Value current))
                    throw new VMException(UndefinedVariableMessage(nameFromEnv), instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                MarkAsyncHazardForEnvAccess(owner);
                target = current.ToObject()!;
            }
            else
            {
                target = PopRuntimeObject()!;
            }

            PushRuntimeObject(GetIndexedValue(target, idxObj, instr));
            return StepResult.Next;
        }

        /// <summary>
        /// Handles the INDEX_SET, INDEX_SET_INTERNAL and INDEX_INIT opcodes.
        /// </summary>
        private StepResult HandleIndexSetInstruction(Instruction instr)
        {
            bool allowReservedRuntimeSlotWrites = instr.Code == OpCode.INDEX_SET_INTERNAL;
            PropertyWriteMode writeMode = instr.Code == OpCode.INDEX_INIT ? PropertyWriteMode.Init : PropertyWriteMode.Normal;
            if (instr.Operand is string)
                RequireStack(2, instr, "INDEX_SET");
            else
                RequireStack(3, instr, "INDEX_SET");

            object value = PopRuntimeObject()!;
            object idxObj = PopRuntimeObject()!;
            object target;

            if (instr.Operand is string nameFromEnv)
            {
                if (!TryResolveVarOwnerValue(nameFromEnv, out Env env, out Value current))
                    throw new VMException(UndefinedVariableMessage(nameFromEnv), instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                MarkAsyncHazardForEnvAccess(env);
                target = current.ToObject()!;
                SetIndexedValue(ref target, idxObj, value, instr, writeMode, allowReservedRuntimeSlotWrites);
                SetLocalVarValue(env, nameFromEnv, Value.FromObject(target));
            }
            else
            {
                target = PopRuntimeObject()!;
                SetIndexedValue(ref target, idxObj, value, instr, writeMode, allowReservedRuntimeSlotWrites);
            }

            return StepResult.Next;
        }
    }
}
