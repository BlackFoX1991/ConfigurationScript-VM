using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;
using CFGS_VM.VMCore.Extensions.Intrinsics.Core;
using CFGS_VM.VMCore.Extensions.Intrinsics.Handles;
using CFGS_VM.VMCore.Plugin;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Runtime.CompilerServices;

namespace CFGS_VM.VMCore
{
    /// <summary>
    /// Defines the <see cref="VM" />
    /// </summary>
    public class VM
    {
        /// <summary>
        /// Defines the NumKind
        /// </summary>
        private enum NumKind
        {
            /// <summary>
            /// Defines the Int32
            /// </summary>
            Int32,

            /// <summary>
            /// Defines the Int64
            /// </summary>
            Int64,

            /// <summary>
            /// Defines the UInt64
            /// </summary>
            UInt64,

            /// <summary>
            /// Defines the Double
            /// </summary>
            Double,

            /// <summary>
            /// Defines the Decimal
            /// </summary>
            Decimal,

            /// <summary>
            /// Defines the Maximal
            /// </summary>
            Maximal,
        }

        /// <summary>
        /// Defines the DEBUG_BUFFER
        /// </summary>
        public static int DEBUG_BUFFER = 100;

        [ThreadStatic]
        public static VM? CurrentVm;

        /// <summary>
        /// Invokes a CFGS closure synchronously from within an intrinsic/builtin.
        /// </summary>
        public object? InvokeClosureSync(Closure closure, List<object> args, Instruction instr)
            => InvokeClosureSync(closure, args, instr, receiver: null, accessType: null);

        /// <summary>
        /// Invokes a CFGS closure synchronously with an optional implicit receiver.
        /// </summary>
        /// <param name="closure">The closure.</param>
        /// <param name="args">The explicit call arguments.</param>
        /// <param name="instr">The source instruction for diagnostics.</param>
        /// <param name="receiver">The implicit receiver for instance/static methods.</param>
        /// <param name="accessType">The access type for runtime visibility checks.</param>
        /// <returns>The call result.</returns>
        public object? InvokeClosureSync(Closure closure, List<object> args, Instruction instr, object? receiver, StaticInstance? accessType)
        {
            if (_program is null || _program.Count == 0)
                throw new VMException("Cannot invoke closure: no program loaded.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            Env callEnv = BuildCallEnv(closure, args, instr);
            int callerDepth = _scopes.Count;
            _scopes.Add(callEnv);
            _callStack.Push(new CallFrame(_program.Count, callerDepth, receiver, accessType, false));

            RunStopReason reason = RunUntilAwaitOrHalt(false, closure.Address);

            if (reason == RunStopReason.AwaitPending)
                throw new VMException("Runtime error: callback function must not use await.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            return _stack.Count > 0 ? _stack.Pop() : null;
        }

        /// <summary>
        /// Explicitly destroys a runtime value.
        /// </summary>
        /// <param name="value">The value to destroy.</param>
        /// <param name="instr">The source instruction for diagnostics.</param>
        /// <param name="recursive">Whether nested values should be destroyed recursively.</param>
        /// <returns>True when destruction work was performed; otherwise false.</returns>
        public bool DestroyValue(object? value, Instruction instr, bool recursive = false)
        {
            HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
            return DestroyValueCore(value, instr, recursive, visited);
        }

        /// <summary>
        /// The DestroyValueCore.
        /// </summary>
        /// <param name="value">The value<see cref="object?"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <param name="recursive">The recursive<see cref="bool"/></param>
        /// <param name="visited">The visited<see cref="HashSet{object}"/></param>
        /// <returns>The <see cref="bool"/></returns>
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
                            foreach (object item in list.ToArray())
                                any |= DestroyValueCore(item, instr, recursive: true, visited);
                        }

                        return any;
                    }

                case Dictionary<string, object> dict:
                    {
                        bool any = false;
                        if (recursive)
                        {
                            foreach (object item in dict.Values.ToArray())
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
        /// <param name="obj">The obj<see cref="ClassInstance"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <param name="recursive">The recursive<see cref="bool"/></param>
        /// <param name="visited">The visited<see cref="HashSet{object}"/></param>
        /// <returns>The <see cref="bool"/></returns>
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
                    foreach (KeyValuePair<string, object> kv in obj.Fields.ToArray())
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
        /// <param name="receiver">The receiver<see cref="object"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="bool"/></returns>
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
        /// <param name="obj">The obj<see cref="ClassInstance"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsInstanceDestroyed(ClassInstance obj)
            => TryGetRuntimeFlag(obj, "__destroyed");

        /// <summary>
        /// The IsInstanceDestroying.
        /// </summary>
        /// <param name="obj">The obj<see cref="ClassInstance"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsInstanceDestroying(ClassInstance obj)
            => TryGetRuntimeFlag(obj, "__destroying");

        /// <summary>
        /// The TryGetRuntimeFlag.
        /// </summary>
        /// <param name="obj">The obj<see cref="ClassInstance"/></param>
        /// <param name="slotName">The slotName<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetRuntimeFlag(ClassInstance obj, string slotName)
        {
            return obj.Fields.TryGetValue(slotName, out object? raw) &&
                   raw is bool flag &&
                   flag;
        }

        /// <summary>
        /// The SetRuntimeFlag.
        /// </summary>
        /// <param name="obj">The obj<see cref="ClassInstance"/></param>
        /// <param name="slotName">The slotName<see cref="string"/></param>
        /// <param name="value">The value<see cref="bool"/></param>
        private static void SetRuntimeFlag(ClassInstance obj, string slotName, bool value)
        {
            obj.Fields[slotName] = value;
        }

        /// <summary>
        /// The IsNumber
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public static bool IsNumber(object x) =>
            x is sbyte or byte or short or ushort or int or uint or long or ulong
              or float or double or decimal or char or BigInteger;

        /// <summary>
        /// The IsSignedIntegral
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsSignedIntegral(object x) =>
            x is sbyte or short or int or long or nint;

        /// <summary>
        /// The IsUnsignedIntegral
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsUnsignedIntegral(object x) =>
            x is byte or ushort or uint or ulong or nuint or char;

        /// <summary>
        /// The IsNegative
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsNegative(object x) => x switch
        {
            sbyte v => v < 0,
            short v => v < 0,
            int v => v < 0,
            long v => v < 0,
            nint v => v < 0,
            _ => false
        };

        /// <summary>
        /// The PromoteKind
        /// </summary>
        /// <param name="a">The a<see cref="object"/></param>
        /// <param name="b">The b<see cref="object"/></param>
        /// <returns>The <see cref="NumKind"/></returns>
        private static NumKind PromoteKind(object a, object b)
        {
            if (a is BigInteger || b is BigInteger) return NumKind.Maximal;

            if (a is decimal || b is decimal) return NumKind.Decimal;

            if (a is double || b is double || a is float || b is float) return NumKind.Double;

            bool aUWide = a is ulong || a is nuint;
            bool bUWide = b is ulong || b is nuint;
            if ((aUWide && IsSignedIntegral(b) && IsNegative(b)) ||
                (bUWide && IsSignedIntegral(a) && IsNegative(a)))
                return NumKind.Maximal;

            if (a is ulong || b is ulong) return NumKind.UInt64;
            if (a is nuint || b is nuint)
                return NumKind.Int64;

            if (a is long || b is long || a is nint || b is nint) return NumKind.Int64;

            return NumKind.Int32;
        }

        /// <summary>
        /// The CharToNumeric
        /// </summary>
        /// <param name="o">The o<see cref="object"/></param>
        /// <returns>The <see cref="object"/></returns>
        internal static object CharToNumeric(object o)
        => o is char ch ? (char.IsDigit(ch) ? (int)(ch - '0') : (int)ch) : o;

        /// <summary>
        /// The CoercePair
        /// </summary>
        /// <param name="a">The a<see cref="object"/></param>
        /// <param name="b">The b<see cref="object"/></param>
        /// <returns>The <see cref="(object A, object B, NumKind K)"/></returns>
        private static (object A, object B, NumKind K) CoercePair(object a, object b)
        {
            a = a is char ? CharToNumeric(a) : a;
            b = b is char ? CharToNumeric(b) : b;

            NumKind k = PromoteKind(a, b);

            switch (k)
            {
                case NumKind.Maximal:
                    return (ToBigIntegerInvariant(a), ToBigIntegerInvariant(b), k);

                case NumKind.Decimal:
                    return (ToDecimalInvariant(a), ToDecimalInvariant(b), k);

                case NumKind.Double:
                    return (ToDoubleInvariant(a), ToDoubleInvariant(b), k);

                case NumKind.UInt64:
                    try
                    {
                        return (ToUInt64Invariant(a), ToUInt64Invariant(b), k);
                    }
                    catch (OverflowException)
                    {
                        return (ToBigIntegerInvariant(a), ToBigIntegerInvariant(b), NumKind.Maximal);
                    }

                case NumKind.Int64:
                    try
                    {
                        return (ToInt64Invariant(a), ToInt64Invariant(b), k);
                    }
                    catch (OverflowException)
                    {
                        return (ToBigIntegerInvariant(a), ToBigIntegerInvariant(b), NumKind.Maximal);
                    }

                default:
                    try
                    {
                        return (ToInt32Invariant(a), ToInt32Invariant(b), NumKind.Int32);
                    }
                    catch (OverflowException)
                    {
                        try
                        {
                            return (ToInt64Invariant(a), ToInt64Invariant(b), NumKind.Int64);
                        }
                        catch (OverflowException)
                        {
                            return (ToBigIntegerInvariant(a), ToBigIntegerInvariant(b), NumKind.Maximal);
                        }
                    }
            }
        }

        /// <summary>
        /// The ToInt32Invariant
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="int"/></returns>
        private static int ToInt32Invariant(object x) => x switch
        {
            string s => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture),
            IConvertible _ => Convert.ToInt32(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        /// <summary>
        /// The ToInt64Invariant
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="long"/></returns>
        private static long ToInt64Invariant(object x) => x switch
        {
            string s => long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture),
            IConvertible _ => Convert.ToInt64(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        /// <summary>
        /// The ToUInt64Invariant
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="ulong"/></returns>
        private static ulong ToUInt64Invariant(object x) => x switch
        {
            string s => ulong.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture),
            IConvertible _ => Convert.ToUInt64(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        /// <summary>
        /// The ToDoubleInvariant
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="double"/></returns>
        private static double ToDoubleInvariant(object x) => x switch
        {
            string s => double.Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
            IConvertible _ => Convert.ToDouble(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        /// <summary>
        /// The ToDecimalInvariant
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="decimal"/></returns>
        private static decimal ToDecimalInvariant(object x) => x switch
        {
            string s => decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture),
            IConvertible _ => Convert.ToDecimal(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        /// <summary>
        /// The ToBigIntegerInvariant
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="BigInteger"/></returns>
        private static BigInteger ToBigIntegerInvariant(object x) => x switch
        {
            null => throw new InvalidOperationException("Not numeric: null"),

            BigInteger bi => bi,

            string s => BigInteger.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture),

            sbyte v => new BigInteger(v),
            byte v => new BigInteger(v),
            short v => new BigInteger(v),
            ushort v => new BigInteger(v),
            int v => new BigInteger(v),
            uint v => new BigInteger(v),
            long v => new BigInteger(v),
            ulong v => new BigInteger(v),
            char v => new BigInteger((uint)v),

            float => throw new InvalidOperationException("Cannot coerce floating-point to BigInteger without explicit rounding."),
            double => throw new InvalidOperationException("Cannot coerce floating-point to BigInteger without explicit rounding."),
            decimal => throw new InvalidOperationException("Cannot coerce decimal to BigInteger without explicit rounding."),

            _ => throw new InvalidOperationException($"Not numeric: {x.GetType().Name}")
        };

        /// <summary>
        /// The CompareAsDecimal
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="decimal"/></returns>
        public static decimal CompareAsDecimal(object x) => x switch
        {
            sbyte v => v,
            byte v => v,
            short v => v,
            ushort v => v,
            int v => v,
            uint v => v,
            long v => v,
            ulong v => (decimal)v,
            float v => (decimal)v,
            double v => (decimal)v,
            decimal v => v,
            char v => (decimal)v,
            BigInteger v when v >= (BigInteger)decimal.MinValue && v <= (BigInteger)decimal.MaxValue
                => (decimal)v,
            BigInteger _
                => throw new OverflowException("BigInteger value is outside the representable decimal range."),
            null
                => throw new InvalidOperationException("Not numeric: null"),
            _
                => throw new InvalidOperationException($"Not numeric: {x.GetType().Name}")
        };

        /// <summary>
        /// The IsIntegralLike
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsIntegralLike(object x) =>
    x is sbyte or byte or short or ushort or int or uint or long or ulong or char or BigInteger;

        /// <summary>
        /// The ToBigInt
        /// </summary>
        /// <param name="x">The x<see cref="object"/></param>
        /// <returns>The <see cref="BigInteger"/></returns>
        private static BigInteger ToBigInt(object x) => x switch
        {
            BigInteger bi => bi,
            sbyte v => new BigInteger(v),
            byte v => new BigInteger(v),
            short v => new BigInteger(v),
            ushort v => new BigInteger(v),
            int v => new BigInteger(v),
            uint v => new BigInteger(v),
            long v => new BigInteger(v),
            ulong v => new BigInteger(v),
            char v => new BigInteger((uint)v),
            _ => throw new InvalidOperationException($"Not integral: {x?.GetType().Name ?? "null"}")
        };

        /// <summary>
        /// The PowBigIntegerOrThrow
        /// </summary>
        /// <param name="a">The a<see cref="BigInteger"/></param>
        /// <param name="b">The b<see cref="BigInteger"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="object"/></returns>
        private static object PowBigIntegerOrThrow(BigInteger a, BigInteger b, Instruction instr)
        {
            if (b.Sign < 0)
                throw new VMException("EXPO with BigInteger requires non-negative exponent", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            if (b > int.MaxValue)
                throw new VMException("EXPO exponent too large for BigInteger.Pow", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            return BigInteger.Pow(a, (int)b);
        }

        /// <summary>
        /// The ToInt32ForRepeat
        /// </summary>
        /// <param name="n">The n<see cref="object"/></param>
        /// <returns>The <see cref="int"/></returns>
        private static int ToInt32ForRepeat(object n)
        {
            BigInteger bi = n is BigInteger B ? B : ToBigInt(n);
            if (bi.Sign < 0) throw new VMException("repeat count must be non-negative", 0, 0, null, IsDebugging, DebugStream!);
            return checked((int)bi);
        }

        /// <summary>
        /// The ToBool
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool ToBool(object? v)
        {
            switch (v)
            {
                case null: return false;

                case bool b: return b;

                case sbyte x: return x != 0;
                case byte x: return x != 0;
                case short x: return x != 0;
                case ushort x: return x != 0;
                case int x: return x != 0;
                case uint x: return x != 0;
                case long x: return x != 0L;
                case ulong x: return x != 0UL;
                case nint x: return x != 0;
                case nuint x: return x != 0;

                case BigInteger bi: return !bi.IsZero;
                case decimal dec: return dec != 0m;

                case double d: return d != 0.0 || double.IsNaN(d);
                case float f: return f != 0f || float.IsNaN(f);

                case char ch: return ch != '\0';

                case string s: return s.Length != 0;
                case Array arr: return arr.Length != 0;

                case ICollection c: return c.Count != 0;

                case IEnumerable en:
                    {
                        IEnumerator e = en.GetEnumerator();
                        try { return e.MoveNext(); } finally { (e as IDisposable)?.Dispose(); }
                    }

                default:
                    return true;
            }
        }

        /// <summary>
        /// Defines the _tryHandlers
        /// </summary>
        private readonly Stack<TryHandler> _tryHandlers = new();

        /// <summary>
        /// Tracks the currently active serialized async execution context on this logical async flow.
        /// </summary>
        private static readonly AsyncLocal<AsyncExecutionContext?> CurrentAsyncContext = new();

        /// <summary>
        /// Associates started CFGS async tasks with the coordinator that serializes them.
        /// </summary>
        private static readonly ConditionalWeakTable<Task, AsyncExecutionCoordinator> AsyncTaskCoordinators = new();

        /// <summary>
        /// Tracks ownership for mutable collection instances that were created within a serialized async execution.
        /// </summary>
        private static readonly ConditionalWeakTable<object, MutableCollectionOwnership> MutableCollectionOwnerships = new();

        /// <summary>
        /// Defines the next unique async execution id.
        /// </summary>
        private static long NextAsyncExecutionId;

        private sealed class AsyncExecutionContext
        {
            public AsyncExecutionContext(AsyncExecutionCoordinator coordinator, long executionId)
            {
                Coordinator = coordinator;
                ExecutionId = executionId;
            }

            public AsyncExecutionCoordinator Coordinator { get; }

            public long ExecutionId { get; }

            public bool HasSharedStateHazard { get; set; }

            public bool OwnsGate { get; set; } = true;
        }

        private sealed class MutableCollectionOwnership
        {
            public MutableCollectionOwnership(long executionId)
            {
                ExecutionId = executionId;
            }

            public long ExecutionId { get; }
        }

        private record CallFrame(int ReturnIp, int BaseScopeDepth, object? ThisRef, StaticInstance? AccessType, bool IsAsync);

        /// <summary>
        /// The WrapReturnForFrame
        /// </summary>
        /// <param name="frame">The frame<see cref="CallFrame"/></param>
        /// <param name="retVal">The retVal<see cref="object?"/></param>
        /// <returns>The <see cref="object?"/></returns>
        private static object? WrapReturnForFrame(CallFrame frame, object? retVal)
        {
            if (!frame.IsAsync)
                return retVal;

            if (AwaitableAdapter.TryGetDirectTask(retVal, out Task<object?> flattened))
                return flattened;

            return Task.FromResult(retVal);
        }

        /// <summary>
        /// The GetAsyncCoordinatorRoot
        /// </summary>
        /// <param name="env">The env<see cref="Env"/></param>
        /// <returns>The <see cref="Env"/></returns>
        private static Env GetAsyncCoordinatorRoot(Env env)
        {
            Env current = env;
            while (current.Parent != null)
                current = current.Parent;

            return current;
        }

        /// <summary>
        /// The StartSerializedAsyncCall
        /// </summary>
        /// <param name="coordinator">The coordinator<see cref="AsyncExecutionCoordinator"/></param>
        /// <param name="work">The work<see cref="Func{Task}"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static Task<object?> StartSerializedAsyncCall(AsyncExecutionCoordinator coordinator, Func<Task<object?>> work)
        {
            Task<object?> started = coordinator.Gate.Wait(0)
                ? RunSerializedAsyncCallCore(coordinator, work, acquired: true)
                : RunSerializedAsyncCallCore(coordinator, work, acquired: false);

            AsyncTaskCoordinators.Remove(started);
            AsyncTaskCoordinators.Add(started, coordinator);
            return started;
        }

        /// <summary>
        /// The RunSerializedAsyncCallCore
        /// </summary>
        /// <param name="coordinator">The coordinator<see cref="AsyncExecutionCoordinator"/></param>
        /// <param name="work">The work<see cref="Func{Task}"/></param>
        /// <param name="acquired">The acquired<see cref="bool"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static async Task<object?> RunSerializedAsyncCallCore(
            AsyncExecutionCoordinator coordinator,
            Func<Task<object?>> work,
            bool acquired)
        {
            if (!acquired)
                await coordinator.Gate.WaitAsync().ConfigureAwait(false);

            AsyncExecutionContext? previous = CurrentAsyncContext.Value;
            AsyncExecutionContext context = new(coordinator, Interlocked.Increment(ref NextAsyncExecutionId));
            CurrentAsyncContext.Value = context;
            try
            {
                Task<object?> started = work();
                return await started.ConfigureAwait(false);
            }
            finally
            {
                CurrentAsyncContext.Value = previous;
                if (context.OwnsGate)
                    coordinator.Gate.Release();
            }
        }

        /// <summary>
        /// Marks the current serialized async context as having observed shared mutable state.
        /// </summary>
        private static void MarkCurrentAsyncSharedStateHazard()
        {
            AsyncExecutionContext? current = CurrentAsyncContext.Value;
            if (current != null)
                current.HasSharedStateHazard = true;
        }

        /// <summary>
        /// Marks env access as shared-state relevant when it escapes the current local scope.
        /// </summary>
        /// <param name="env">The env<see cref="Env"/></param>
        private void MarkAsyncHazardForEnvAccess(Env env)
        {
            if (_scopes.Count == 0)
                return;

            if (!ReferenceEquals(env, _scopes[^1]))
                MarkCurrentAsyncSharedStateHazard();
        }

        /// <summary>
        /// Captures ownership for mutable collections that are created within the current serialized async execution.
        /// </summary>
        /// <typeparam name="TCollection">The collection type.</typeparam>
        /// <param name="collection">The collection.</param>
        /// <returns>The same collection instance.</returns>
        private static TCollection CaptureMutableCollectionOwnership<TCollection>(TCollection collection)
            where TCollection : class
        {
            AsyncExecutionContext? current = CurrentAsyncContext.Value;
            if (current == null ||
                collection is not List<object> &&
                collection is not Dictionary<string, object>)
            {
                return collection;
            }

            MutableCollectionOwnerships.Remove(collection);
            MutableCollectionOwnerships.Add(collection, new MutableCollectionOwnership(current.ExecutionId));
            return collection;
        }

        /// <summary>
        /// Marks the current async execution as hazardous when accessing a mutable collection that is not owned by it.
        /// </summary>
        /// <param name="value">The value.</param>
        private static void MarkAsyncHazardForMutableCollection(object? value)
        {
            AsyncExecutionContext? current = CurrentAsyncContext.Value;
            if (current == null ||
                value is not List<object> &&
                value is not Dictionary<string, object>)
            {
                return;
            }

            if (!MutableCollectionOwnerships.TryGetValue(value, out MutableCollectionOwnership? owner) ||
                owner.ExecutionId != current.ExecutionId)
            {
                current.HasSharedStateHazard = true;
            }
        }

        /// <summary>
        /// Marks async hazards for mutable receivers before intrinsic access.
        /// </summary>
        /// <param name="receiver">The receiver.</param>
        private static void MarkAsyncHazardForMutableReceiver(object? receiver)
        {
            switch (receiver)
            {
                case List<object>:
                case Dictionary<string, object>:
                    MarkAsyncHazardForMutableCollection(receiver);
                    break;

                case ClassInstance:
                case StaticInstance:
                    MarkCurrentAsyncSharedStateHazard();
                    break;
            }
        }

        /// <summary>
        /// The FinalizeAsyncResult
        /// </summary>
        /// <param name="value">The value<see cref="object?"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static Task<object?> FinalizeAsyncResult(object? value)
        {
            if (AwaitableAdapter.TryGetDirectTask(value, out Task<object?> flattened))
                return AwaitTaskRespectingCoordinatorAsync(flattened);

            return Task.FromResult(value);
        }

        /// <summary>
        /// The AwaitTaskRespectingCoordinatorAsync
        /// </summary>
        /// <param name="task">The task<see cref="Task{object?}"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static async Task<object?> AwaitTaskRespectingCoordinatorAsync(Task<object?> task)
        {
            AsyncExecutionContext? current = CurrentAsyncContext.Value;
            if (current == null || task.IsCompleted)
            {
                return await task.ConfigureAwait(false);
            }

            bool awaitingSerializedCfgsTask =
                AsyncTaskCoordinators.TryGetValue(task, out AsyncExecutionCoordinator? awaitedCoordinator) &&
                ReferenceEquals(current.Coordinator, awaitedCoordinator);

            if (!awaitingSerializedCfgsTask && current.HasSharedStateHazard)
                return await task.ConfigureAwait(false);

            if (!current.OwnsGate)
                return await task.ConfigureAwait(false);

            current.OwnsGate = false;
            CurrentAsyncContext.Value = null;
            current.Coordinator.Gate.Release();
            try
            {
                return await task.ConfigureAwait(false);
            }
            finally
            {
                await current.Coordinator.Gate.WaitAsync().ConfigureAwait(false);
                current.OwnsGate = true;
                CurrentAsyncContext.Value = current;
            }
        }

        /// <summary>
        /// The ContainsTaskForCoordinator
        /// </summary>
        /// <param name="value">The value<see cref="object?"/></param>
        /// <param name="coordinator">The coordinator<see cref="AsyncExecutionCoordinator"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool ContainsTaskForCoordinator(object? value, AsyncExecutionCoordinator coordinator)
        {
            switch (value)
            {
                case Task task:
                    return AsyncTaskCoordinators.TryGetValue(task, out AsyncExecutionCoordinator? taskCoordinator) &&
                           ReferenceEquals(taskCoordinator, coordinator);

                case List<object> list:
                    foreach (object item in list)
                    {
                        if (ContainsTaskForCoordinator(item, coordinator))
                            return true;
                    }
                    return false;

                case Dictionary<string, object> dict:
                    foreach (object item in dict.Values)
                    {
                        if (ContainsTaskForCoordinator(item, coordinator))
                            return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// The CreateHotStartChildVm
        /// </summary>
        /// <returns>The <see cref="VM"/></returns>
        private VM CreateHotStartChildVm()
        {
            if (_program is null || _program.Count == 0)
                throw new InvalidOperationException("Hot-start async call requires loaded program instructions.");

            VM child = new();
            child.LoadInstructions(_program);
            child.LoadFunctions(Functions);

            foreach (BuiltinDescriptor desc in Builtins.Snapshot())
                child.Builtins.Register(desc);

            foreach ((Type ReceiverType, IntrinsicDescriptor Descriptor) entry in Intrinsics.Snapshot())
                child.Intrinsics.Register(entry.ReceiverType, entry.Descriptor);

            return child;
        }

        /// <summary>
        /// The PrepareHotStartEntry
        /// </summary>
        /// <param name="callEnv">The callEnv<see cref="Env"/></param>
        /// <param name="receiver">The receiver<see cref="object?"/></param>
        /// <param name="accessType">The accessType<see cref="StaticInstance?"/></param>
        private void PrepareHotStartEntry(Env callEnv, object? receiver, StaticInstance? accessType)
        {
            if (_program is null || _program.Count == 0)
                throw new InvalidOperationException("Hot-start async entry requires loaded program instructions.");

            _stack.Clear();
            _tryHandlers.Clear();
            _callStack.Clear();
            _scopes.Clear();
            _scopes.Add(callEnv);
            _callStack.Push(new CallFrame(_program.Count, _scopes.Count, receiver, accessType, false));
        }

        /// <summary>
        /// The ConsumeHotStartResult
        /// </summary>
        /// <returns>The <see cref="object?"/></returns>
        private object? ConsumeHotStartResult()
        {
            if (_stack.Count == 0)
                return null;
            return _stack.Pop();
        }

        /// <summary>
        /// The RunHotStartEntryAsync
        /// </summary>
        /// <param name="startIp">The startIp<see cref="int"/></param>
        /// <param name="ct">The ct<see cref="CancellationToken"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private Task<object?> RunHotStartEntryAsync(int startIp, CancellationToken ct = default)
        {
            RunStopReason reason = RunUntilAwaitOrHalt(false, startIp);
            if (reason == RunStopReason.Halted)
                return FinalizeAsyncResult(ConsumeHotStartResult());

            Task<object?> firstPending = _awaitTask!;
            int resumeIp = _awaitResumeIp;
            _awaitTask = null;

            return ContinueHotStartAfterAwaitAsync(firstPending, resumeIp, ct);
        }

        /// <summary>
        /// The ContinueHotStartAfterAwaitAsync
        /// </summary>
        /// <param name="pendingTask">The pendingTask<see cref="Task{object?}"/></param>
        /// <param name="resumeIp">The resumeIp<see cref="int"/></param>
        /// <param name="ct">The ct<see cref="CancellationToken"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private async Task<object?> ContinueHotStartAfterAwaitAsync(Task<object?> pendingTask, int resumeIp, CancellationToken ct)
        {
            await Task.Yield();

            Task<object?> task = pendingTask;
            int startIp = resumeIp;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    object? res = await AwaitTaskRespectingCoordinatorAsync(task).ConfigureAwait(false);
                    _stack.Push(res!);
                }
                catch (Exception ex)
                {
                    Instruction at = SafeCurrentInstr(_program!, startIp);
                    ExceptionObject payload = new(
                        type: "AwaitError",
                        message: ex.Message,
                        file: at.OriginFile,
                        line: at.Line,
                        col: at.Col,
                        stack: BuildStackString(_program!, at)
                    );

                    if (RouteExceptionToTryHandlers(payload, at, out int nip))
                    {
                        startIp = nip;
                        RunStopReason routedReason = RunUntilAwaitOrHalt(false, startIp);
                        if (routedReason == RunStopReason.Halted)
                            return ConsumeHotStartResult();

                        task = _awaitTask!;
                        startIp = _awaitResumeIp;
                        _awaitTask = null;
                        continue;
                    }

                    throw new VMException(payload.ToString()!, at.Line, at.Col, at.OriginFile, IsDebugging, DebugStream!, payload.Stack);
                }

                RunStopReason reason = RunUntilAwaitOrHalt(false, startIp);
                if (reason == RunStopReason.Halted)
                    return await FinalizeAsyncResult(ConsumeHotStartResult()).ConfigureAwait(false);

                task = _awaitTask!;
                startIp = _awaitResumeIp;
                _awaitTask = null;
            }
        }

        /// <summary>
        /// The TryStartHotAsyncCall
        /// </summary>
        /// <param name="f">The f<see cref="Closure"/></param>
        /// <param name="args">The args<see cref="List{object}"/></param>
        /// <param name="receiver">The receiver<see cref="object?"/></param>
        /// <param name="accessType">The accessType<see cref="StaticInstance?"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <param name="startedTask">The startedTask<see cref="Task{object?}"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryStartHotAsyncCall(
            Closure f,
            List<object> args,
            object? receiver,
            StaticInstance? accessType,
            Instruction instr,
            out Task<object?> startedTask)
        {
            if (!f.IsAsync)
            {
                startedTask = null!;
                return false;
            }

            Env callEnv = BuildCallEnv(f, args, instr);
            VM child = CreateHotStartChildVm();
            child.PrepareHotStartEntry(callEnv, receiver, accessType);
            AsyncExecutionCoordinator coordinator = GetAsyncCoordinatorRoot(f.CapturedEnv).AsyncCoordinator;
            startedTask = StartSerializedAsyncCall(coordinator, () => child.RunHotStartEntryAsync(f.Address));
            return true;
        }
        /// <summary>
        /// The PopScopesToBase
        /// </summary>
        /// <param name="baseDepth">The baseDepth<see cref="int"/></param>
        private void PopScopesToBase(int baseDepth)
        {
            while (_scopes.Count > baseDepth)
                _scopes.RemoveAt(_scopes.Count - 1);
        }

        /// <summary>
        /// Gets the CurrentThis
        /// </summary>
        private object? CurrentThis => _callStack.Count > 0 ? _callStack.Peek().ThisRef : null;

        /// <summary>
        /// Defines the _stack
        /// </summary>
        private readonly Stack<object> _stack = new();

        /// <summary>
        /// Defines the _scopes
        /// </summary>
        private readonly List<Env> _scopes = new() { new Env(null) };

        /// <summary>
        /// Gets the Functions
        /// </summary>
        public Dictionary<string, FunctionInfo> Functions { get; } = [];
        private readonly Dictionary<int, FunctionInfo> _functionsByAddress = new();

        /// <summary>
        /// Gets the Builtins
        /// </summary>
        public BuiltinRegistry Builtins { get; } = new();

        /// <summary>
        /// Defines the _callStack
        /// </summary>
        private readonly Stack<CallFrame> _callStack = new();

        /// <summary>
        /// The LoadPluginsFrom
        /// </summary>
        /// <param name="directory">The directory<see cref="string"/></param>
        public void LoadPluginsFrom(string directory)
        {

            PluginLoader.LoadDirectory(directory, Builtins, Intrinsics);
        }

        /// <summary>
        /// The LoadPlugin
        /// </summary>
        /// <param name="dllPath">The dllPath<see cref="string"/></param>
        public void LoadPlugin(string dllPath)
        {
            PluginLoader.LoadDll(dllPath, Builtins, Intrinsics);
        }

        /// <summary>
        /// The LoadFunctions
        /// </summary>
        /// <param name="funcs">The funcs<see cref="Dictionary{string, FunctionInfo}"/></param>
        public void LoadFunctions(Dictionary<string, FunctionInfo> funcs)
        {
            foreach (KeyValuePair<string, FunctionInfo> kv in funcs)
            {
                if (Functions.ContainsKey(kv.Key))
                    throw new VMException(
                        $"Runtime error: multiple declarations for function '{kv.Key}'",
                        -1, -1, string.Empty, IsDebugging, DebugStream!);
                Functions[kv.Key] = kv.Value;
                _functionsByAddress[kv.Value.Address] = kv.Value;
            }
        }

        /// <summary>
        /// The HasLocalVar
        /// </summary>
        /// <param name="env">The env<see cref="Env"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool HasLocalVar(Env env, string name)
        {
            lock (env.SyncRoot)
                return env.Vars.ContainsKey(name);
        }

        /// <summary>
        /// The TryGetLocalVar
        /// </summary>
        /// <param name="env">The env<see cref="Env"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetLocalVar(Env env, string name, out object? value)
        {
            lock (env.SyncRoot)
            {
                if (env.Vars.TryGetValue(name, out object? local))
                {
                    value = local;
                    return true;
                }
            }

            value = null;
            return false;
        }

        /// <summary>
        /// The GetLocalVar
        /// </summary>
        /// <param name="env">The env<see cref="Env"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="object?"/></returns>
        private static object? GetLocalVar(Env env, string name)
        {
            lock (env.SyncRoot)
                return env.Vars[name];
        }

        /// <summary>
        /// The SetLocalVar
        /// </summary>
        /// <param name="env">The env<see cref="Env"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="value">The value<see cref="object?"/></param>
        private static void SetLocalVar(Env env, string name, object? value)
        {
            lock (env.SyncRoot)
                env.Vars[name] = value!;
        }

        /// <summary>
        /// The FindEnvWithLocal
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="Env?"/></returns>
        private Env? FindEnvWithLocal(string name)
        {
            for (Env? env = _scopes.Count > 0 ? _scopes[^1] : null; env != null; env = env.Parent)
            {
                if (HasLocalVar(env, name))
                    return env;
            }

            if (_scopes.Count > 0)
            {
                Env root = _scopes[0];
                if (root != null && HasLocalVar(root, name))
                    return root;
            }

            return null;
        }

        /// <summary>
        /// The IsReceiverName
        /// </summary>
        /// <param name="s">The s<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsReceiverName(string s) => s == "this" || s == "type";

        /// <summary>
        /// The ExpandSpreadArguments
        /// </summary>
        /// <param name="rawArgs">The rawArgs<see cref="List{object}"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="List{object}"/></returns>
        private List<object> ExpandSpreadArguments(List<object> rawArgs, Instruction instr)
        {
            List<object> expanded = new();
            foreach (object arg in rawArgs)
            {
                if (arg is not SpreadArgument spread)
                {
                    expanded.Add(arg);
                    continue;
                }

                if (spread.Value is null)
                    throw new VMException("Runtime error: cannot spread null argument", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                if (spread.Value is not IList list)
                    throw new VMException(
                        $"Runtime error: spread argument must be an array/list (got {spread.Value.GetType().Name})",
                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                foreach (object? item in list)
                    expanded.Add(item!);
            }

            return expanded;
        }

        /// <summary>
        /// The RunNonBlockingInvocation
        /// </summary>
        /// <param name="invoke">The invoke<see cref="Func{object?}"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static Task<object?> RunNonBlockingInvocation(Func<object?> invoke)
        {
            return Task.Run<object?>(async () =>
            {
                object? raw = invoke();
                if (AwaitableAdapter.TryGetTask(raw, out Task<object?>? awaited))
                    return await awaited.ConfigureAwait(false);
                return raw;
            });
        }

        /// <summary>
        /// The InvokeBuiltinForCall
        /// </summary>
        /// <param name="desc">The desc<see cref="BuiltinDescriptor"/></param>
        /// <param name="args">The args<see cref="List{object}"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="object?"/></returns>
        private static object? InvokeBuiltinForCall(BuiltinDescriptor desc, List<object> args, Instruction instr)
        {
            if (!desc.NonBlocking)
                return desc.Invoke(args, instr);

            List<object> argsCopy = new(args);
            return RunNonBlockingInvocation(() => desc.Invoke(argsCopy, instr));
        }

        /// <summary>
        /// The InvokeIntrinsicForCall
        /// </summary>
        /// <param name="method">The method<see cref="IntrinsicMethod"/></param>
        /// <param name="receiver">The receiver<see cref="object"/></param>
        /// <param name="args">The args<see cref="List{object}"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="object?"/></returns>
        private static object? InvokeIntrinsicForCall(IntrinsicMethod method, object receiver, List<object> args, Instruction instr)
        {
            MarkAsyncHazardForMutableReceiver(receiver);

            if (!method.NonBlocking)
                return method.Invoke(receiver, args, instr);

            List<object> argsCopy = new(args);
            return RunNonBlockingInvocation(() => method.Invoke(receiver, argsCopy, instr));
        }

        /// <summary>
        /// The BuildCallEnv
        /// </summary>
        /// <param name="f">The f<see cref="Closure"/></param>
        /// <param name="rawArgs">The rawArgs<see cref="List{object}"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="Env"/></returns>
        private Env BuildCallEnv(Closure f, List<object> rawArgs, Instruction instr)
        {
            int piStart = (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0])) ? 1 : 0;
            int total = f.Parameters.Count - piStart;
            int min = Math.Max(0, f.MinArgs - piStart);

            int restIndex = -1;
            if (!string.IsNullOrWhiteSpace(f.RestParameter))
            {
                for (int i = 0; i < total; i++)
                {
                    if (string.Equals(f.Parameters[piStart + i], f.RestParameter, StringComparison.Ordinal))
                    {
                        restIndex = i;
                        break;
                    }
                }

                if (restIndex < 0)
                {
                    throw new VMException(
                        $"Runtime error: invalid function metadata for rest parameter '{f.RestParameter}'",
                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                }

                if (restIndex != total - 1)
                {
                    throw new VMException(
                        $"Runtime error: rest parameter '{f.RestParameter}' must be last",
                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                }
            }

            int fixedCount = restIndex >= 0 ? restIndex : total;

            List<object?> positional = new();
            Dictionary<string, object?> named = new(StringComparer.Ordinal);
            bool sawNamed = false;

            foreach (object arg in rawArgs)
            {
                if (arg is NamedArgument na)
                {
                    sawNamed = true;
                    if (named.ContainsKey(na.Name))
                        throw new VMException($"Runtime error: duplicate named argument '{na.Name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    named[na.Name] = na.Value;
                }
                else
                {
                    if (sawNamed)
                        throw new VMException("Runtime error: positional argument cannot follow named arguments", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    positional.Add(arg);
                }
            }

            if (restIndex < 0 && positional.Count > total)
                throw new VMException(
                    $"Runtime error: too many args for call (expected {total}, got {positional.Count})",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            Dictionary<string, int> paramIndex = new(StringComparer.Ordinal);
            for (int i = 0; i < fixedCount; i++)
                paramIndex[f.Parameters[piStart + i]] = i;

            object sentinel = new();
            object?[] finalArgs = new object?[fixedCount];
            for (int i = 0; i < fixedCount; i++) finalArgs[i] = sentinel;

            int positionalToAssign = Math.Min(positional.Count, fixedCount);
            for (int i = 0; i < positionalToAssign; i++)
                finalArgs[i] = positional[i];

            List<object?> restValues = new();
            if (restIndex >= 0)
            {
                for (int i = fixedCount; i < positional.Count; i++)
                    restValues.Add(positional[i]);
            }

            foreach (KeyValuePair<string, object?> kv in named)
            {
                if (restIndex >= 0 && string.Equals(kv.Key, f.RestParameter, StringComparison.Ordinal))
                    throw new VMException(
                        $"Runtime error: rest parameter '{kv.Key}' cannot be passed as named argument",
                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                if (!paramIndex.TryGetValue(kv.Key, out int idx))
                    throw new VMException($"Runtime error: unknown named argument '{kv.Key}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                if (!ReferenceEquals(finalArgs[idx], sentinel))
                    throw new VMException($"Runtime error: argument '{kv.Key}' provided multiple times", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                finalArgs[idx] = kv.Value;
            }

            for (int i = 0; i < fixedCount; i++)
            {
                if (ReferenceEquals(finalArgs[i], sentinel))
                {
                    if (i < min)
                    {
                        string missingParam = f.Parameters[piStart + i];
                        if (string.Equals(missingParam, "__outer", StringComparison.Ordinal))
                        {
                            throw new VMException(
                                "Runtime error: nested constructor requires an outer instance argument '__outer'",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

                        throw new VMException(
                            $"Runtime error: insufficient args for call (expected at least {min})",
                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                    }

                    finalArgs[i] = null;
                }
            }

            Env callEnv = new(f.CapturedEnv);
            for (int i = 0; i < fixedCount; i++)
                callEnv.Define(f.Parameters[piStart + i], finalArgs[i]!);

            if (restIndex >= 0)
                callEnv.Define(f.Parameters[piStart + restIndex], CaptureMutableCollectionOwnership(restValues));

            return callEnv;
        }

        /// <summary>
        /// Gets or sets a value indicating whether AllowFileIO
        /// </summary>
        public static bool AllowFileIO { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether IsDebugging
        /// </summary>
        public static bool IsDebugging { get; set; } = false;

        /// <summary>
        /// Defines the _program
        /// </summary>
        private List<Instruction>? _program;

        /// <summary>
        /// Defines the _awaitTask
        /// </summary>
        private Task<object?>? _awaitTask;

        /// <summary>
        /// Defines the _awaitResumeIp
        /// </summary>
        private int _awaitResumeIp;

        /// <summary>
        /// Defines the StepResult
        /// </summary>
        private enum StepResult
        {
            /// <summary>
            /// Defines the Next
            /// </summary>
            Next,

            /// <summary>
            /// Defines the Continue
            /// </summary>
            Continue,

            /// <summary>
            /// Defines the Routed
            /// </summary>
            Routed,

            /// <summary>
            /// Defines the Halt
            /// </summary>
            Halt,

            /// <summary>
            /// Defines the Await
            /// </summary>
            Await
        }

        /// <summary>
        /// The HandleInstruction
        /// </summary>
        /// <param name="_ip">The _ip<see cref="int"/></param>
        /// <param name="_insns">The _insns<see cref="List{Instruction}"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="StepResult"/></returns>
        private StepResult HandleInstruction(ref int _ip, List<Instruction> _insns, Instruction instr)
        {
            switch (instr.Code)
            {
                case OpCode.PUSH_INT:
                    if (instr.Operand is null) _stack.Push(0);
                    else _stack.Push((int)instr.Operand);
                    break;

                case OpCode.PUSH_LNG:
                    if (instr.Operand is null) _stack.Push((long)0);
                    else _stack.Push((long)instr.Operand);
                    break;

                case OpCode.PUSH_FLT:
                    if (instr.Operand is null) _stack.Push((float)0);
                    else _stack.Push((float)instr.Operand);
                    break;

                case OpCode.PUSH_DBL:
                    if (instr.Operand is null) _stack.Push(0.0);
                    else _stack.Push((double)instr.Operand);
                    break;

                case OpCode.PUSH_DEC:
                    if (instr.Operand is null) _stack.Push((decimal)0);
                    else _stack.Push((decimal)instr.Operand);
                    break;

                case OpCode.PUSH_SPC:
                    if (instr.Operand is null) _stack.Push((BigInteger)0);
                    else _stack.Push((BigInteger)instr.Operand);
                    break;

                case OpCode.PUSH_STR:
                    if (instr.Operand is null) _stack.Push("");
                    else _stack.Push((string)instr.Operand);
                    break;

                case OpCode.PUSH_CHR:
                    if (instr.Operand is null) _stack.Push((char)0);
                    else _stack.Push((char)instr.Operand);
                    break;

                case OpCode.PUSH_BOOL:
                    if (instr.Operand is null) _stack.Push(false);
                    else _stack.Push((bool)instr.Operand);
                    break;

                case OpCode.PUSH_NULL:
                    _stack.Push(null!);
                    break;

                case OpCode.PUSH_SCOPE:
                    {
                        _scopes.Add(new Env(_scopes[^1]));
                        break;
                    }

                case OpCode.POP_SCOPE:
                    {
                        if (_scopes.Count <= 1)
                            throw new VMException("Runtime error: cannot pop global scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        _scopes.RemoveAt(_scopes.Count - 1);
                        break;
                    }
                case OpCode.LEAVE:
                    {
                        if (instr.Operand is not object[] arr || arr.Length < 2)
                            throw new VMException("Runtime error: LEAVE requires [targetIp, scopesToPop]", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        int targetIp = Convert.ToInt32(arr[0]);
                        int scopesToPop = Convert.ToInt32(arr[1]);

                        TryHandler? nextFinally = null;
                        foreach (TryHandler th in _tryHandlers)
                        {
                            if (th.FinallyAddr >= 0 && !th.InFinally && th.CallDepth == _callStack.Count)
                            {
                                nextFinally = th;
                                break;
                            }
                        }

                        if (nextFinally != null)
                        {
                            nextFinally.HasPendingLeave = true;
                            nextFinally.PendingLeaveTargetIp = targetIp;
                            nextFinally.PendingLeaveScopes = scopesToPop;
                            nextFinally.InFinally = true;
                            nextFinally.CatchAddr = -1;

                            int nip = nextFinally.FinallyAddr;
                            nextFinally.FinallyAddr = -1;
                            _ip = nip;
                            return StepResult.Routed;
                        }

                        for (int i = 0; i < scopesToPop; i++)
                        {
                            if (_scopes.Count <= 1)
                                throw new VMException("Runtime error: cannot pop global scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            _scopes.RemoveAt(_scopes.Count - 1);
                        }

                        _ip = targetIp;
                        return StepResult.Continue;
                    }

                case OpCode.NEW_OBJECT:
                    {
                        string className = instr.Operand?.ToString() ?? "<anon>";
                        ClassInstance obj = new(className);
                        _stack.Push(obj);
                        break;
                    }
                case OpCode.NEW_STATIC:
                    {
                        string className = instr.Operand?.ToString() ?? "<anon>";
                        StaticInstance st = new(className);
                        _stack.Push(st);
                        break;
                    }

                case OpCode.NEW_ARRAY:
                    {
                        if (instr.Operand is null) break;
                        int ecount = (int)instr.Operand;
                        RequireStack(ecount, instr, "NEW_ARRAY");
                        object[] temp = new object[ecount];
                        for (int i = ecount - 1; i >= 0; i--) temp[i] = _stack.Pop();
                        List<object> list = CaptureMutableCollectionOwnership(new List<object>(temp));
                        _stack.Push(list);
                        break;
                    }

                case OpCode.SLICE_GET:
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
                                    (int start, int end) = NormalizeSliceBounds(startObj, endObj, arr.Count, instr);
                                    _stack.Push(CaptureMutableCollectionOwnership(arr.GetRange(start, end - start)));
                                    break;
                                }

                            case Dictionary<string, object> dict:
                                {
                                    MarkAsyncHazardForMutableCollection(dict);
                                    List<string> keys = dict.Keys.ToList();
                                    (int start, int end) = NormalizeSliceBounds(startObj, endObj, keys.Count, instr);

                                    Dictionary<string, object> slice = CaptureMutableCollectionOwnership(new Dictionary<string, object>());
                                    for (int i = start; i < end; i++)
                                        slice[keys[i]] = dict[keys[i]];

                                    _stack.Push(slice);
                                    break;
                                }

                            case string s:
                                {
                                    (int start, int end) = NormalizeSliceBounds(startObj, endObj, s.Length, instr);
                                    _stack.Push(s.Substring(start, end - start));
                                    break;
                                }

                            default:
                                throw new VMException($"Runtime error: SLICE_GET target must be array, dictionary, or string",
                                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        break;
                    }

                case OpCode.SLICE_SET:
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

                            DoSliceSet(ref target, startObj, endObj, value, instr);
                            SetLocalVar(env, name, target);
                        }
                        else
                        {
                            target = _stack.Pop();
                            MarkAsyncHazardForMutableCollection(target);
                            DoSliceSet(ref target, startObj, endObj, value, instr);
                        }
                        break;

                        static void DoSliceSet(ref object target, object startObj, object endObj, object value, Instruction instr)
                        {
                            if (target is string)
                                throw new VMException("Runtime error: SLICE_SET on string. Strings are immutable.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            switch (target)
                            {
                                case List<object> arr:
                                {
                                        (int start, int end) = NormalizeSliceBounds(startObj, endObj, arr.Count, instr);

                                        if (value is List<object> lst)
                                        {
                                            int count = Math.Min(end - start, lst.Count);
                                            for (int i = 0; i < count; i++)
                                                arr[start + i] = lst[i];

                                        }
                                        else
                                        {
                                            throw new VMException($"Runtime error: trying to assign non-list to array slice",
                                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                                        }
                                        break;
                                    }

                                case Dictionary<string, object> dict:
                                    {
                                        List<string> keys = dict.Keys.ToList();
                                        (int start, int end) = NormalizeSliceBounds(startObj, endObj, keys.Count, instr);

                                        if (value is Dictionary<string, object> valDict)
                                        {
                                            int i = 0;
                                            for (int k = start; k < end && i < valDict.Count; k++, i++)
                                            {
                                                KeyValuePair<String, object> kv = valDict.ElementAt(i);
                                                dict[keys[k]] = kv.Value;
                                            }
                                        }
                                        else
                                        {
                                            throw new VMException($"Runtime error: trying to assign non-dictionary to dictionary slice",
                                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                                        }
                                        break;
                                    }

                                default:
                                    throw new VMException($"Runtime error: SLICE_SET target must be array or dictionary",
                                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }
                        }
                    }

                case OpCode.INDEX_GET:
                    {
                        if (instr.Operand is string)
                            RequireStack(1, instr, "INDEX_GET");
                        else
                            RequireStack(2, instr, "INDEX_GET");

                        object target;
                        object idxObj = _stack.Pop();

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
                        break;
                    }

                case OpCode.INDEX_SET:
                case OpCode.INDEX_SET_INTERNAL:
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

                        break;
                    }

                case OpCode.NEW_DICT:
                    {
                        if (instr.Operand is null) break;
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
                        break;
                    }

                case OpCode.NEW_ENUM:
                    {
                        if (instr.Operand is null) break;

                        if (_stack.Count < 1)
                            throw new VMException("Runtime error: stack underflow (NEW_ENUM needs count)", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        int count = Convert.ToInt32(_stack.Pop());

                        if (count < 0)
                            throw new VMException($"Runtime error: enum member count cannot be negative ({count})", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        RequireStack(2 * count, instr, "NEW_ENUM");

                        EnumInstance ei = new(instr.Operand.ToString() ?? "null");

                        for (int i = 0; i < count; i++)
                        {
                            object valueObj = _stack.Pop();
                            object keyObj = _stack.Pop();

                            string key = keyObj?.ToString() ?? "null";
                            int value = Convert.ToInt32(valueObj);

                            ei.Add(key, value);
                        }

                        _stack.Push(ei);
                        break;
                    }

                case OpCode.ROT:
                    {
                        RequireStack(3, instr, "ROT");
                        object a = _stack.Pop();
                        object b = _stack.Pop();
                        object c = _stack.Pop();
                        _stack.Push(b);
                        _stack.Push(a);
                        _stack.Push(c);
                        break;
                    }
                case OpCode.SWAP:
                    {
                        RequireStack(2, instr, "SWAP");

                        object a = _stack.Pop();
                        object b = _stack.Pop();
                        _stack.Push(a);
                        _stack.Push(b);
                        break;
                    }

                case OpCode.IS_DICT:
                    {
                        if (_stack.Count < 1)
                            throw new VMException("Stack underflow in IS_DICT", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        object v = _stack.Pop();
                        _stack.Push(v is Dictionary<string, object>);
                        break;
                    }

                case OpCode.IS_ARRAY:
                    {
                        if (_stack.Count < 1)
                            throw new VMException("Stack underflow in IS_ARRAY", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        object v = _stack.Pop();
                        _stack.Push(v is List<object>);
                        break;
                    }

                case OpCode.LEN:
                    {
                        RequireStack(1, instr, "LEN");
                        object v = _stack.Pop();
                        switch (v)
                        {
                            case List<object> arr:
                                _stack.Push(arr.Count);
                                break;
                            case Dictionary<string, object> dict:
                                _stack.Push(dict.Count);
                                break;
                            case string str:
                                _stack.Push(str.Length);
                                break;
                            default:
                                throw new VMException("Runtime error: LEN target must be array, dictionary, or string", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        break;
                    }

                case OpCode.HAS_KEY:
                    {
                        RequireStack(2, instr, "HAS_KEY");
                        object keyObj = _stack.Pop();
                        object target = _stack.Pop();
                        if (target is not Dictionary<string, object> dict)
                            throw new VMException("Runtime error: HAS_KEY target must be dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        string key = keyObj?.ToString() ?? "";
                        _stack.Push(dict.ContainsKey(key));
                        break;
                    }

                case OpCode.ARRAY_PUSH:
                    {
                        if (instr.Operand == null)
                        {
                            RequireStack(2, instr, "ARRAY_PUSH");
                            object arrObj = _stack.Pop();
                            object value = _stack.Pop();

                            if (arrObj is List<object> arr)
                            {
                                MarkAsyncHazardForMutableCollection(arr);
                                arr.Add(value);
                            }
                            else if (arrObj is Dictionary<string, object> dict)
                            {
                                MarkAsyncHazardForMutableCollection(dict);
                                if (value is Dictionary<string, object> literal && literal.Count == 1)
                                {
                                    foreach (KeyValuePair<string, object> kv in literal)
                                    {
                                        string key = NormalizeDictionaryWriteKey(dict, kv.Key, instr);
                                        dict[key] = kv.Value;
                                    }
                                }
                                else
                                {
                                    int k = 0;
                                    while (dict.ContainsKey(k.ToString(CultureInfo.InvariantCulture)))
                                    {
                                        k++;
                                    }
                                    dict[k.ToString(CultureInfo.InvariantCulture)] = value;
                                }
                            }
                            else
                            {
                                throw new VMException($"Runtime error: ARRAY_PUSH target is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }
                        }
                        else
                        {
                            RequireStack(1, instr, "ARRAY_PUSH");
                            object value = _stack.Pop();
                            string name = (string)instr.Operand;
                            Env? env = FindEnvWithLocal(name);
                            if (env == null || !TryGetLocalVar(env, name, out object? obj))
                                throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            MarkAsyncHazardForEnvAccess(env);
                            if (obj is List<object> arr)
                            {
                                MarkAsyncHazardForMutableCollection(arr);
                                arr.Add(value);
                            }
                            else if (obj is Dictionary<string, object> dict)
                            {
                                MarkAsyncHazardForMutableCollection(dict);
                                if (value is Dictionary<string, object> literal && literal.Count == 1)
                                {
                                    foreach (KeyValuePair<string, object> kv in literal)
                                    {
                                        string key = NormalizeDictionaryWriteKey(dict, kv.Key, instr);
                                        dict[key] = kv.Value;
                                    }
                                }
                                else
                                {
                                    int k = 0;
                                    while (dict.ContainsKey(k.ToString(CultureInfo.InvariantCulture)))
                                    {
                                        k++;
                                    }
                                    dict[k.ToString(CultureInfo.InvariantCulture)] = value;
                                }
                            }
                            else
                            {
                                throw new VMException($"Runtime error: variable '{name}' is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }
                        }
                        break;
                    }

                case OpCode.ARRAY_DELETE_SLICE:
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
                        break;
                    }

                case OpCode.ARRAY_DELETE_SLICE_ALL:
                    {
                        RequireStack(3, instr, "ARRAY_DELETE_SLICE_ALL");
                        object endObj = _stack.Pop();
                        object startObj = _stack.Pop();
                        object target = _stack.Pop();
                        MarkAsyncHazardForMutableCollection(target);

                        if (target is string)
                            throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        if (target is List<object> arr)
                        {
                            (int start, int endEx) = NormalizeSliceBounds(startObj, endObj, arr.Count, instr);
                            int sdcount = endEx - start;
                            if (sdcount > 0) arr.RemoveRange(start, sdcount);
                        }
                        else if (target is Dictionary<string, object> dict)
                        {
                            List<string> keys = dict.Keys.ToList();
                            (int start, int endEx) = NormalizeSliceBounds(startObj, endObj, keys.Count, instr);
                            for (int i = start; i < endEx; i++)
                                dict.Remove(keys[i]);
                        }
                        else
                        {
                            throw new VMException($"Runtime error: delete target is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        break;
                    }

                case OpCode.ARRAY_DELETE_ELEM:
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

                            if (target is string)
                                throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            if (target is List<object> arr)
                            {
                                int index = RequireIntIndex(idxObj, instr);
                                if (index >= 0 && index < arr.Count)
                                    arr.RemoveAt(index);
                                else
                                    throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }
                            else if (target is Dictionary<string, object> dict)
                            {
                                string key = Convert.ToString(idxObj, CultureInfo.InvariantCulture) ?? "";
                                if (!dict.Remove(key))
                                    throw new VMException($"Runtime error: key '{key}' not found in dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }
                            else
                            {
                                throw new VMException($"Runtime error: variable '{name}' is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }
                        }
                        else
                        {
                            object target = _stack.Pop();
                            MarkAsyncHazardForMutableCollection(target);

                            if (target is string)
                                throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            if (target is List<object> arr)
                            {
                                int index = RequireIntIndex(idxObj, instr);
                                if (index >= 0 && index < arr.Count)
                                    arr.RemoveAt(index);
                                else
                                    throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }
                            else if (target is Dictionary<string, object> dict)
                            {
                                string key = Convert.ToString(idxObj, CultureInfo.InvariantCulture) ?? "";
                                if (!dict.Remove(key))
                                    throw new VMException($"Runtime error: key '{key}' not found in dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }
                            else if (target is ClassInstance obj)
                            {
                                MarkCurrentAsyncSharedStateHazard();
                                string key = idxObj?.ToString() ?? "";
                                if (key.Length == 0)
                                    throw new VMException("Runtime error: field name cannot be empty", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                                if (!obj.Fields.TryGetValue(key, out object? field))
                                    throw new VMException($"Runtime error: invalid instance member '{key}' in class '{obj.ClassName}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                                if (field is List<object>)
                                {
                                    obj.Fields[key] = CaptureMutableCollectionOwnership(new List<object>());
                                }
                                else if (field is Dictionary<string, object>)
                                {
                                    obj.Fields[key] = CaptureMutableCollectionOwnership(new Dictionary<string, object>());
                                }
                                else
                                {
                                    throw new VMException(
                                        $"Runtime error: field '{key}' on class '{obj.ClassName}' is not an array or dictionary",
                                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                                    );
                                }
                            }
                            else if (target is StaticInstance st)
                            {
                                MarkCurrentAsyncSharedStateHazard();
                                string key = idxObj?.ToString() ?? "";
                                if (key.Length == 0)
                                    throw new VMException("Runtime error: static member name cannot be empty", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                                if (!st.Fields.TryGetValue(key, out object? field))
                                    throw new VMException(
                                        $"Runtime error: invalid static member '{key}' in class '{st.ClassName}'",
                                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                                    );

                                if (field is List<object>)
                                {
                                    st.Fields[key] = CaptureMutableCollectionOwnership(new List<object>());
                                }
                                else if (field is Dictionary<string, object>)
                                {
                                    st.Fields[key] = CaptureMutableCollectionOwnership(new Dictionary<string, object>());
                                }
                                else
                                {
                                    throw new VMException(
                                        $"Runtime error: static member '{key}' in class '{st.ClassName}' is not an array or dictionary",
                                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                                    );
                                }
                            }
                            else
                            {
                                throw new VMException($"Runtime error: delete target is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }
                        }
                        break;
                    }

                case OpCode.ARRAY_DELETE_ALL:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;
                        Env? env = FindEnvWithLocal(name);
                        if (env == null || !TryGetLocalVar(env, name, out object? target))
                            throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        MarkAsyncHazardForEnvAccess(env);
                        MarkAsyncHazardForMutableCollection(target);
                        if (target is List<object>)
                        {
                            SetLocalVar(env, name, CaptureMutableCollectionOwnership(new List<object>()));
                        }
                        else if (target is Dictionary<string, object>)
                        {
                            SetLocalVar(env, name, CaptureMutableCollectionOwnership(new Dictionary<string, object>()));
                        }
                        else
                        {
                            throw new VMException($"Runtime error: variable '{name}' is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        break;
                    }

                case OpCode.ARRAY_CLEAR:
                    {
                        RequireStack(1, instr, "ARRAY_CLEAR");

                        object target = _stack.Pop();
                        MarkAsyncHazardForMutableCollection(target);

                        if (target is List<object> list)
                        {
                            list.Clear();
                        }
                        else if (target is Dictionary<string, object> dict)
                        {
                            dict.Clear();
                        }
                        else
                        {
                            throw new VMException(
                                $"Runtime error: delete target is not an array or dictionary",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

                        break;
                    }

                case OpCode.ARRAY_DELETE_ELEM_ALL:
                    {
                        RequireStack(2, instr, "ARRAY_DELETE_ELEM_ALL");

                        object idxObj = _stack.Pop();
                        object target = _stack.Pop();
                        if (target is string)
                            throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        if (target is List<object> arr)
                        {
                            int index = RequireIntIndex(idxObj, instr);
                            if (index < 0 || index >= arr.Count)
                                throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            arr.RemoveAt(index);
                        }
                        else if (target is Dictionary<string, object> dict)
                        {
                            string key = idxObj?.ToString() ?? throw new VMException(
                                $"Runtime error: dictionary key cannot be null", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            if (!dict.Remove(key))
                                throw new VMException($"Runtime error: key '{key}' not found in dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        else
                        {
                            throw new VMException($"Runtime error: delete target is not an array or dictionary", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        break;
                    }

                case OpCode.LOAD_VAR:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this")
                        {
                            object? th = CurrentThis;
                            if (th == null)
                                throw new VMException("Runtime error: 'this' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            _stack.Push(th);
                            break;
                        }

                        if (name == "type")
                        {
                            object? recv = CurrentThis;
                            if (recv is StaticInstance st)
                            {
                                _stack.Push(st);
                                break;
                            }
                            if (recv is ClassInstance inst)
                            {
                                if (inst.Fields.TryGetValue("__type", out object? tObj) && tObj is StaticInstance st2)
                                {
                                    _stack.Push(st2);
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__type' on instance for 'type'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }
                            throw new VMException("Runtime error: 'type' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

                        if (name == "super")
                        {
                            object? recv = CurrentThis;

                            if (recv is ClassInstance inst)
                            {
                                if (inst.Fields.TryGetValue("__base", out object? bObj) && bObj is ClassInstance baseInst)
                                {
                                    _stack.Push(baseInst);
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__base' on instance for 'super'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }

                            if (recv is StaticInstance st)
                            {
                                if (st.Fields.TryGetValue("__base", out object? sbObj) && sbObj is StaticInstance baseType)
                                {
                                    _stack.Push(baseType);
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__base' on static type for 'super'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }

                            throw new VMException("Runtime error: 'super' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        if (name == "outer")
                        {
                            object? recv = CurrentThis;

                            if (recv is ClassInstance inst)
                            {
                                if (inst.Fields.TryGetValue("__outer", out object? oObj) && oObj is ClassInstance outerInst)
                                {
                                    _stack.Push(outerInst);
                                    break;
                                }
                                throw new VMException("Runtime error: missing '__outer' on instance for 'outer'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }

                            if (recv is StaticInstance)
                            {
                                throw new VMException("Runtime error: 'outer' not available in static context", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }

                            throw new VMException("Runtime error: 'outer' is not bound in current frame", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

                        Env? owner = FindEnvWithLocal(name);
                        if (owner != null && TryGetLocalVar(owner, name, out object? val))
                        {
                            MarkAsyncHazardForEnvAccess(owner);
                            _stack.Push(val!);
                            break;
                        }

                        if (Builtins.TryGet(name, out BuiltinDescriptor? d))
                        {
                            _stack.Push(new BuiltinCallable(d.Name, d.ArityMin, d.ArityMax));
                            break;
                        }

                        throw new VMException($"Runtime error: undefined variable '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                    }

                case OpCode.VAR_DECL:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this" || name == "type" || name == "super" || name == "outer")
                            throw new VMException($"Runtime error: cannot declare '{name}' as a variable", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        RequireStack(1, instr, "VAR_DECL");
                        object value = _stack.Pop();
                        Env scope = _scopes[^1];
                        if (scope.HasLocal(name))
                            throw new VMException($"Runtime error: variable '{name}' already declared in this scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        scope.Define(name, value);
                        break;
                    }

                case OpCode.CONST_DECL:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this" || name == "type" || name == "super" || name == "outer")
                            throw new VMException($"Runtime error: cannot declare '{name}' as a constant", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        RequireStack(1, instr, "CONST_DECL");
                        object value = _stack.Pop();
                        Env scope = _scopes[^1];
                        if (scope.HasLocal(name))
                            throw new VMException($"Runtime error: variable '{name}' already declared in this scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        scope.DefineConst(name, value);
                        break;
                    }

                case OpCode.STORE_VAR:
                    {
                        if (instr.Operand is null) break;
                        string name = (string)instr.Operand;

                        if (name == "this" || name == "type" || name == "super" || name == "outer")
                            throw new VMException($"Runtime error: cannot assign to '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        RequireStack(1, instr, "STORE_VAR");
                        object value = _stack.Pop();
                        Env? env = FindEnvWithLocal(name);
                        if (env == null)
                            throw new VMException($"Runtime error: assignment to undeclared variable '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        MarkAsyncHazardForEnvAccess(env);
                        if (env.IsConstLocal(name))
                            throw new VMException($"Runtime error: cannot assign to constant '{name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        SetLocalVar(env, name, value);
                        break;
                    }

                case OpCode.ADD:
                    {
                        RequireStack(2, instr, "ADD");
                        object r = _stack.Pop();
                        object l = _stack.Pop();

                        if (IsNumber(l) && IsNumber(r))
                        {
                            (object A, object B, NumKind K) = CoercePair(l, r);
                            object res = K switch
                            {
                                NumKind.Decimal => (object)((decimal)A + (decimal)B),
                                NumKind.Double => (double)A + (double)B,
                                NumKind.UInt64 => (ulong)A + (ulong)B,
                                NumKind.Int64 => (long)A + (long)B,
                                NumKind.Maximal => (BigInteger)A + (BigInteger)B,
                                _ => (int)A + (int)B,
                            };
                            _stack.Push(res);
                            break;
                        }

                        if (l is null || r is null)
                        {
                            if (l is string || r is string)
                                _stack.Push((l as string ?? "") + (r as string ?? ""));
                            else
                                _stack.Push(null!);
                            break;
                        }

                        if (l is List<object> || r is List<object> ||
                            l is Dictionary<string, object> || r is Dictionary<string, object>)
                        {
                            string ls, rs;
                            using (StringWriter lw = new()) { PrintValue(l, lw); ls = lw.ToString(); }
                            using (StringWriter rw = new()) { PrintValue(r, rw); rs = rw.ToString(); }
                            _stack.Push(ls + rs);
                            break;
                        }

                        _stack.Push(l.ToString() + r.ToString());
                        break;
                    }

                case OpCode.SUB:
                    {
                        RequireStack(2, instr, "SUB");
                        object r = _stack.Pop();
                        object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null!); break; }
                        if (!IsNumber(l) || !IsNumber(r))
                            throw new VMException("SUB on non-numeric types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        (object A, object B, NumKind K) = CoercePair(l, r);
                        object res = K switch
                        {
                            NumKind.Decimal => (object)((decimal)A - (decimal)B),
                            NumKind.Double => (double)A - (double)B,
                            NumKind.UInt64 => (ulong)A - (ulong)B,
                            NumKind.Int64 => (long)A - (long)B,
                            NumKind.Maximal => (BigInteger)A - (BigInteger)B,
                            _ => (int)A - (int)B,
                        };
                        _stack.Push(res);
                        break;
                    }

                case OpCode.MUL:
                    {
                        RequireStack(2, instr, "MUL");
                        object r = _stack.Pop();
                        object l = _stack.Pop();

                        if (l is null || r is null)
                        {
                            if (l is string && IsNumber(r!))
                            {
                                _stack.Push(string.Concat(Enumerable.Repeat(l as string ?? "", checked((int)ToInt32ForRepeat(r!)))));
                                break;
                            }
                            if (r is string && IsNumber(l!))
                            {
                                _stack.Push(string.Concat(Enumerable.Repeat(r as string ?? "", checked((int)ToInt32ForRepeat(l!)))));
                                break;
                            }
                            _stack.Push(null!);
                            break;
                        }

                        if (IsNumber(l) && IsNumber(r))
                        {
                            (object A, object B, NumKind K) = CoercePair(l, r);
                            object res = K switch
                            {
                                NumKind.Decimal => (object)((decimal)A * (decimal)B),
                                NumKind.Double => (double)A * (double)B,
                                NumKind.UInt64 => (ulong)A * (ulong)B,
                                NumKind.Int64 => (long)A * (long)B,
                                NumKind.Maximal => (BigInteger)A * (BigInteger)B,
                                _ => (int)A * (int)B,
                            };
                            _stack.Push(res);
                        }
                        else if (l is string && IsNumber(r))
                        {
                            _stack.Push(string.Concat(Enumerable.Repeat(l as string ?? "", checked((int)ToInt32ForRepeat(r)))));
                        }
                        else if (r is string && IsNumber(l))
                        {
                            _stack.Push(string.Concat(Enumerable.Repeat(r as string ?? "", checked((int)ToInt32ForRepeat(l)))));
                        }
                        else
                        {
                            throw new VMException("MUL on non-numeric types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        break;
                    }

                case OpCode.MOD:
                    {
                        RequireStack(2, instr, "MOD");
                        object r = _stack.Pop();
                        object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null!); break; }
                        if (!IsNumber(l) || !IsNumber(r))
                            throw new VMException("MOD on non-numeric types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        (object A, object B, NumKind K) = CoercePair(l, r);
                        bool isZero = K switch
                        {
                            NumKind.Int32 => Convert.ToInt32(r) == 0,
                            NumKind.Int64 => Convert.ToInt64(r) == 0L,
                            NumKind.UInt64 => Convert.ToUInt64(r) == 0UL,
                            NumKind.Decimal => Convert.ToDecimal(r) == 0m,
                            NumKind.Double => (double)B == 0.0,
                            NumKind.Maximal => ((BigInteger)B).IsZero,
                            _ => false
                        };
                        if (isZero)
                            throw new VMException("division by zero in MOD", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        object res = K switch
                        {
                            NumKind.Decimal => (object)((decimal)A % (decimal)B),
                            NumKind.Double => (double)A % (double)B,
                            NumKind.UInt64 => (ulong)A % (ulong)B,
                            NumKind.Int64 => (long)A % (long)B,
                            NumKind.Maximal => (BigInteger)A % (BigInteger)B,
                            _ => (int)A % (int)B,
                        };
                        _stack.Push(res);
                        break;
                    }

                case OpCode.DIV:
                    {
                        RequireStack(2, instr, "DIV");
                        object r = _stack.Pop();
                        object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null!); break; }
                        if (!IsNumber(l) || !IsNumber(r))
                            throw new VMException($"Runtime error: cannot DIV {l?.GetType()} and {r?.GetType()}",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        (object A, object B, NumKind K) = CoercePair(l, r);
                        bool isZero = K switch
                        {
                            NumKind.Int32 => Convert.ToInt32(r) == 0,
                            NumKind.Int64 => Convert.ToInt64(r) == 0L,
                            NumKind.UInt64 => Convert.ToUInt64(r) == 0UL,
                            NumKind.Decimal => Convert.ToDecimal(r) == 0m,
                            NumKind.Double => (double)B == 0.0,
                            NumKind.Maximal => ((BigInteger)B).IsZero,
                            _ => false
                        };
                        if (isZero)
                            throw new VMException("division by zero", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        object res = K switch
                        {
                            NumKind.Decimal => (object)((decimal)A / (decimal)B),
                            NumKind.Double => (double)A / (double)B,
                            NumKind.UInt64 => (ulong)A / (ulong)B,
                            NumKind.Int64 => (long)A / (long)B,
                            NumKind.Maximal => (BigInteger)A / (BigInteger)B,
                            _ => (int)A / (int)B,
                        };
                        _stack.Push(res);
                        break;
                    }

                case OpCode.EXPO:
                    {
                        RequireStack(2, instr, "EXPO");
                        object r = _stack.Pop();
                        object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null!); break; }
                        if (!IsNumber(l) || !IsNumber(r))
                            throw new VMException("EXPO on non-numeric types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        (object A, object B, NumKind K) = CoercePair(l, r);
                        object res = K switch
                        {
                            NumKind.Decimal => (object)((decimal)Math.Pow((double)(decimal)A, (double)(decimal)B)),
                            NumKind.Double => Math.Pow((double)A, (double)B),
                            NumKind.UInt64 => (ulong)Math.Pow((double)(ulong)A, (double)(ulong)B),
                            NumKind.Int64 => (long)Math.Pow((double)(long)A, (double)(long)B),

                            NumKind.Maximal => PowBigIntegerOrThrow((BigInteger)A, (BigInteger)B, instr),

                            _ => (int)Math.Pow((double)(int)A, (double)(int)B),
                        };
                        _stack.Push(res);
                        break;
                    }

                case OpCode.BIT_AND:
                    {
                        RequireStack(2, instr, "BIT_AND");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        if (l is null || r is null) { _stack.Push(null!); break; }
                        r = r is char ? CharToNumeric(r) : r;
                        l = l is char ? CharToNumeric(l) : l;

                        if (!(IsIntegralLike(l) && IsIntegralLike(r)))
                            throw new VMException("BIT_AND requires integral types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        if (l is BigInteger || r is BigInteger)
                            _stack.Push(ToBigInt(l) & ToBigInt(r));
                        else if (l is long or ulong || r is long or ulong)
                            _stack.Push(Convert.ToInt64(l) & Convert.ToInt64(r));
                        else
                            _stack.Push(Convert.ToInt32(l) & Convert.ToInt32(r));
                        break;
                    }

                case OpCode.BIT_OR:
                    {
                        RequireStack(2, instr, "BIT_OR");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        if (l is null || r is null) { _stack.Push(null!); break; }
                        r = r is char ? CharToNumeric(r) : r;
                        l = l is char ? CharToNumeric(l) : l;

                        if (!(IsIntegralLike(l) && IsIntegralLike(r)))
                            throw new VMException("BIT_OR requires integral types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        if (l is BigInteger || r is BigInteger)
                            _stack.Push(ToBigInt(l) | ToBigInt(r));
                        else if (l is long or ulong || r is long or ulong)
                            _stack.Push(Convert.ToInt64(l) | Convert.ToInt64(r));
                        else
                            _stack.Push(Convert.ToInt32(l) | Convert.ToInt32(r));
                        break;
                    }

                case OpCode.BIT_XOR:
                    {
                        RequireStack(2, instr, "BIT_XOR");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        if (l is null || r is null) { _stack.Push(null!); break; }
                        r = r is char ? CharToNumeric(r) : r;
                        l = l is char ? CharToNumeric(l) : l;

                        if (!(IsIntegralLike(l) && IsIntegralLike(r)))
                            throw new VMException("BIT_XOR requires integral types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        if (l is BigInteger || r is BigInteger)
                            _stack.Push(ToBigInt(l) ^ ToBigInt(r));
                        else if (l is long or ulong || r is long or ulong)
                            _stack.Push(Convert.ToInt64(l) ^ Convert.ToInt64(r));
                        else
                            _stack.Push(Convert.ToInt32(l) ^ Convert.ToInt32(r));
                        break;
                    }

                case OpCode.SHL:
                    {
                        RequireStack(2, instr, "SHL");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        if (l is null || r is null) { _stack.Push(null!); break; }
                        r = r is char ? CharToNumeric(r) : r;
                        l = l is char ? CharToNumeric(l) : l;

                        if (!(IsIntegralLike(l) && IsNumber(r)))
                            throw new VMException("SHL requires (integral) << int", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        int sh = Convert.ToInt32(r) & 0x3F;
                        if (l is BigInteger)
                            _stack.Push(((BigInteger)l) << sh);
                        else if (l is long or ulong)
                            _stack.Push(Convert.ToInt64(l) << sh);
                        else
                            _stack.Push(Convert.ToInt32(l) << (sh & 0x1F));
                        break;
                    }

                case OpCode.SHR:
                    {
                        RequireStack(2, instr, "SHR");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        if (l is null || r is null) { _stack.Push(null!); break; }
                        r = r is char ? CharToNumeric(r) : r;
                        l = l is char ? CharToNumeric(l) : l;

                        if (!(IsIntegralLike(l) && IsNumber(r)))
                            throw new VMException("SHR requires (integral) >> int", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        int sh = Convert.ToInt32(r) & 0x3F;
                        if (l is BigInteger)
                            _stack.Push(((BigInteger)l) >> sh);
                        else if (l is long or ulong)
                            _stack.Push(Convert.ToInt64(l) >> sh);
                        else
                            _stack.Push(Convert.ToInt32(l) >> (sh & 0x1F));
                        break;
                    }
                case OpCode.EQ:
                    {
                        RequireStack(2, instr, "EQ");
                        object r = _stack.Pop();
                        object l = _stack.Pop();
                        bool res;
                        if (IsNumber(l) && IsNumber(r))
                            res = CompareAsDecimal(l) == CompareAsDecimal(r);
                        else if (l is string ls && r is string rs)
                            res = string.Equals(ls, rs, StringComparison.Ordinal);
                        else
                            res = Equals(l, r);
                        _stack.Push(res);
                        break;
                    }

                case OpCode.NEQ:
                    {
                        RequireStack(2, instr, "NEQ");
                        object r = _stack.Pop();
                        object l = _stack.Pop();
                        bool res;
                        if (IsNumber(l) && IsNumber(r))
                            res = CompareAsDecimal(l) != CompareAsDecimal(r);
                        else if (l is string ls && r is string rs)
                            res = !string.Equals(ls, rs, StringComparison.Ordinal);
                        else
                            res = !Equals(l, r);
                        _stack.Push(res);
                        break;
                    }

                case OpCode.IS_TYPE:
                    {
                        RequireStack(2, instr, "IS_TYPE");
                        object? right = _stack.Pop();
                        object? left = _stack.Pop();

                        if (left is not ClassInstance ci || right is not StaticInstance target)
                        {
                            _stack.Push(false);
                            break;
                        }

                        bool found = false;
                        if (TryGetStaticType(ci, out StaticInstance instType))
                        {
                            found = IsInterfaceType(target)
                                ? ImplementsInterface(instType, target)
                                : IsSameOrDerivedStaticType(instType, target);
                        }
                        _stack.Push(found);
                        break;
                    }

                case OpCode.LT:
                    {
                        RequireStack(2, instr, "LT");
                        object r = _stack.Pop(); object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null!); break; }

                        if (IsNumber(l) && IsNumber(r))
                        {
                            (object A, object B, NumKind K) = CoercePair(l, r);
                            bool v = K switch
                            {
                                NumKind.Decimal => (decimal)A < (decimal)B,
                                NumKind.Double => (double)A < (double)B,
                                NumKind.UInt64 => (ulong)A < (ulong)B,
                                NumKind.Int64 => (long)A < (long)B,
                                _ => (int)A < (int)B,
                            };
                            _stack.Push(v);
                        }
                        else if (l is string ls && r is string rs)
                        {
                            _stack.Push(string.CompareOrdinal(ls, rs) < 0);
                        }
                        else
                        {
                            throw new VMException("Runtime error: LT on non-comparable types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        break;
                    }

                case OpCode.GT:
                    {
                        RequireStack(2, instr, "GT");
                        object r = _stack.Pop(); object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null!); break; }

                        if (IsNumber(l) && IsNumber(r))
                        {
                            (object A, object B, NumKind K) = CoercePair(l, r);
                            bool v = K switch
                            {
                                NumKind.Decimal => (decimal)A > (decimal)B,
                                NumKind.Double => (double)A > (double)B,
                                NumKind.UInt64 => (ulong)A > (ulong)B,
                                NumKind.Int64 => (long)A > (long)B,
                                _ => (int)A > (int)B,
                            };
                            _stack.Push(v);
                        }
                        else if (l is string ls && r is string rs)
                        {
                            _stack.Push(string.CompareOrdinal(ls, rs) > 0);
                        }
                        else
                        {
                            throw new VMException("Runtime error: GT on non-comparable types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        break;
                    }

                case OpCode.LE:
                    {
                        RequireStack(2, instr, "LE");
                        object r = _stack.Pop(); object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null!); break; }

                        if (IsNumber(l) && IsNumber(r))
                        {
                            (object A, object B, NumKind K) = CoercePair(l, r);
                            bool v = K switch
                            {
                                NumKind.Decimal => (decimal)A <= (decimal)B,
                                NumKind.Double => (double)A <= (double)B,
                                NumKind.UInt64 => (ulong)A <= (ulong)B,
                                NumKind.Int64 => (long)A <= (long)B,
                                _ => (int)A <= (int)B,
                            };
                            _stack.Push(v);
                        }
                        else if (l is string ls && r is string rs)
                        {
                            _stack.Push(string.CompareOrdinal(ls, rs) <= 0);
                        }
                        else
                        {
                            throw new VMException("Runtime error: LE on non-comparable types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        break;
                    }

                case OpCode.GE:
                    {
                        RequireStack(2, instr, "GE");
                        object r = _stack.Pop(); object l = _stack.Pop();

                        if (l is null || r is null) { _stack.Push(null!); break; }

                        if (IsNumber(l) && IsNumber(r))
                        {
                            (object A, object B, NumKind K) = CoercePair(l, r);
                            bool v = K switch
                            {
                                NumKind.Decimal => (decimal)A >= (decimal)B,
                                NumKind.Double => (double)A >= (double)B,
                                NumKind.UInt64 => (ulong)A >= (ulong)B,
                                NumKind.Int64 => (long)A >= (long)B,
                                _ => (int)A >= (int)B,
                            };
                            _stack.Push(v);
                        }
                        else if (l is string ls && r is string rs)
                        {
                            _stack.Push(string.CompareOrdinal(ls, rs) >= 0);
                        }
                        else
                        {
                            throw new VMException("Runtime error: GE on non-comparable types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }
                        break;
                    }

                case OpCode.NOT:
                    {
                        RequireStack(1, instr, "NOT");
                        object v = _stack.Pop();
                        if (v is null) { _stack.Push((v is null)); break; }
                        _stack.Push(!ToBool(v));
                        break;
                    }

                case OpCode.NEG:
                    {
                        RequireStack(1, instr, "NEG");
                        object? v = _stack.Pop();
                        if (v is null) { _stack.Push(null!); break; }
                        if (!IsNumber(v))
                            throw new VMException(
                                $"NEG only works on numeric types (got {v ?? "null"} of type {v?.GetType().Name ?? "null"})",
                                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                            );

                        object res =
                            v is decimal md ? (object)(-md) :
                            v is double dd ? (object)(-dd) :
                            v is float ff ? (object)(-ff) :
                            v is long ll ? (object)(-ll) :
                            v is ulong uu ? (object)unchecked((long)-(long)uu) :
                            (object)(-Convert.ToInt32(v));

                        _stack.Push(res);
                        break;
                    }

                case OpCode.DUP:
                    {
                        RequireStack(1, instr, "DUP");
                        object v = _stack.Peek();
                        _stack.Push(v);
                        break;
                    }

                case OpCode.POP:
                    {
                        RequireStack(1, instr, "POP");
                        _stack.Pop();
                        break;
                    }

                case OpCode.DESTROY:
                    {
                        RequireStack(1, instr, "DESTROY");
                        object? value = _stack.Pop();
                        DestroyValue(value, instr, recursive: false);
                        break;
                    }

                case OpCode.AND:
                    {
                        RequireStack(2, instr, "AND");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        bool lb = ToBool(l); bool rb = ToBool(r);
                        _stack.Push(lb && rb);
                        break;
                    }

                case OpCode.OR:
                    {
                        RequireStack(2, instr, "OR");
                        object r = _stack.Pop(); object l = _stack.Pop();
                        bool lb = ToBool(l); bool rb = ToBool(r);
                        _stack.Push(lb || rb);
                        break;
                    }

                case OpCode.LABEL:
                    return StepResult.Continue;

                case OpCode.JMP:
                    {
                        if (instr.Operand is null)
                            throw new VMException("Runtime error: JMP missing target", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        _ip = (int)instr.Operand;
                        return StepResult.Continue;
                    }

                case OpCode.JMP_IF_FALSE:
                    {
                        if (instr.Operand is null)
                            throw new VMException("Runtime error: JMP_IF_FALSE missing target", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        RequireStack(1, instr, "JMP_IF_FALSE");
                        object v = _stack.Pop();
                        if (!ToBool(v))
                        {
                            _ip = (int)instr.Operand;
                            return StepResult.Continue;
                        }
                        break;
                    }

                case OpCode.JMP_IF_TRUE:
                    {
                        if (instr.Operand is null)
                            throw new VMException("Runtime error: JMP_IF_TRUE missing target", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        RequireStack(1, instr, "JMP_IF_TRUE");
                        object v = _stack.Pop();
                        if (ToBool(v))
                        {
                            _ip = (int)instr.Operand;
                            return StepResult.Continue;
                        }
                        break;
                    }

                case OpCode.AWAIT:
                    {
                        RequireStack(1, instr, "AWAIT");
                        object? awaited = _stack.Pop();

                        if (!AwaitableAdapter.TryGetTask(awaited, out Task<object?>? task))
                        {
                            _stack.Push(awaited);
                            break;
                        }

                        AsyncExecutionContext? currentContext = CurrentAsyncContext.Value;
                        if (currentContext != null && ContainsTaskForCoordinator(awaited, currentContext.Coordinator))
                        {
                            AsyncTaskCoordinators.Remove(task);
                            AsyncTaskCoordinators.Add(task, currentContext.Coordinator);
                        }

                        if (task.IsCompleted)
                        {
                            if (task.IsFaulted)
                            {
                                Exception ex = task.Exception?.InnerException ?? task.Exception ?? new Exception("await faulted");
                                ExceptionObject payload = new(
                                    type: "AwaitError",
                                    message: ex.Message,
                                    file: instr.OriginFile,
                                    line: instr.Line,
                                    col: instr.Col,
                                    stack: BuildStackString(_insns, instr)
                                );

                                if (RouteExceptionToTryHandlers(payload, instr, out int nip))
                                {
                                    _ip = nip;
                                    return StepResult.Routed;
                                }
                                throw new VMException(payload.ToString()!, instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!, payload.Stack);
                            }

                            if (task.IsCanceled)
                            {
                                ExceptionObject payload = new(
                                    type: "AwaitCanceled",
                                    message: "await canceled",
                                    file: instr.OriginFile,
                                    line: instr.Line,
                                    col: instr.Col,
                                    stack: BuildStackString(_insns, instr)
                                );

                                if (RouteExceptionToTryHandlers(payload, instr, out int nip))
                                {
                                    _ip = nip;
                                    return StepResult.Routed;
                                }
                                throw new VMException(payload.ToString()!, instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!, payload.Stack);
                            }

                            _stack.Push(task.Result!);
                            break;
                        }

                        _awaitTask = task;
                        _awaitResumeIp = _ip;
                        return StepResult.Await;
                    }

                case OpCode.YIELD:
                    {
                        _awaitTask = Task.Run<object?>(async () =>
                        {
                            await Task.Yield();
                            return null;
                        });
                        _awaitResumeIp = _ip;
                        return StepResult.Await;
                    }

                case OpCode.HALT:
                    return StepResult.Halt;

                case OpCode.PUSH_CLOSURE:
                    {
                        if (instr.Operand == null)
                            throw new VMException($"Runtime error: PUSH_CLOSURE without operand", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        int funcAddr;
                        string? funcName = null;

                        switch (instr.Operand)
                        {
                            case int i:
                                funcAddr = i;
                                break;
                            case object[] arr when arr.Length >= 2:
                                funcAddr = (int)arr[0];
                                funcName = arr[1]?.ToString() ?? "";
                                break;
                            default:
                                throw new VMException($"Runtime error: Invalid PUSH_CLOSURE operand type {instr.Operand.GetType().Name}", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        }

                        _functionsByAddress.TryGetValue(funcAddr, out FunctionInfo? funcInfo);
                        if (funcInfo == null)
                            throw new VMException($"Runtime error: PUSH_CLOSURE unknown function address {funcAddr}", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        Env capturedEnv = _scopes[^1];
                        _stack.Push(new Closure(
                            funcAddr,
                            funcInfo.Parameters,
                            funcInfo.MinArgs,
                            capturedEnv,
                            funcName ?? throw new VMException("Invalid function-name", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!),
                            funcInfo.RestParameter,
                            funcInfo.isAsync));
                        break;
                    }

                case OpCode.CALL:
                    {
                        if (instr.Operand is string funcName)
                        {
                            if (Builtins.TryGet(funcName, out BuiltinDescriptor? desc))
                            {
                                List<object> args = new();
                                for (int i = desc.ArityMin - 1; i >= 0; i--)
                                {
                                    if (_stack.Count == 0)
                                        throw new VMException($"Runtime error: insufficient args for {funcName}()", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                                    args.Insert(0, _stack.Pop());
                                }
                                object? ret = InvokeBuiltinForCall(desc, args, instr);
                                _stack.Push(ret!);
                                break;
                            }

                            if (!Functions.TryGetValue(funcName, out FunctionInfo? func))
                                throw new VMException($"Runtime error: unknown function {funcName}", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            if (func.Parameters.Count > 0 && func.Parameters[0] == "this")
                                throw new VMException(
                                    $"Runtime error: cannot CALL method '{funcName}' without receiver. Use CALL_INDIRECT with a bound receiver.",
                                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            _stack.Push(new Closure(func.Address, func.Parameters, func.MinArgs, _scopes[^1], funcName, func.RestParameter, func.isAsync));
                            goto case OpCode.CALL_INDIRECT;
                        }
                        else
                        {
                            goto case OpCode.CALL_INDIRECT;
                        }
                    }

                case OpCode.MAKE_NAMED_ARG:
                    {
                        if (instr.Operand is not string argName || string.IsNullOrWhiteSpace(argName))
                            throw new VMException("Runtime error: MAKE_NAMED_ARG requires a non-empty argument name", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        RequireStack(1, instr, "MAKE_NAMED_ARG");
                        object? value = _stack.Pop();
                        _stack.Push(new NamedArgument(argName, value));
                        break;
                    }

                case OpCode.MAKE_SPREAD_ARG:
                    {
                        RequireStack(1, instr, "MAKE_SPREAD_ARG");
                        object? value = _stack.Pop();
                        _stack.Push(new SpreadArgument(value));
                        break;
                    }

                case OpCode.CALL_INDIRECT:
                    {
                        if (instr.Operand is int explicitArgCount)
                        {
                            if (explicitArgCount < 0)
                                throw new VMException("Runtime error: negative argument count in CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            List<object> argsList = new();
                            for (int i = 0; i < explicitArgCount; i++)
                            {
                                if (_stack.Count == 0)
                                    throw new VMException(
                                        $"Runtime error: not enough arguments for CALL_INDIRECT (expected {explicitArgCount})",
                                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                                    );
                                argsList.Add(_stack.Pop());
                            }

                            if (_stack.Count == 0)
                                throw new VMException("Runtime error: missing callee for CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            object callee = _stack.Pop();
                            argsList = ExpandSpreadArguments(argsList, instr);
                            int actualArgCount = argsList.Count;
                            Closure f;
                            object? receiver = null;
                            StaticInstance? accessType = null;

                            if (callee is IntrinsicBound ib_ex)
                            {
                                if (actualArgCount < ib_ex.Method.ArityMin || actualArgCount > ib_ex.Method.ArityMax)
                                    throw new VMException(
                                        $"Runtime error: {ib_ex.Method.Name} expects {ib_ex.Method.ArityMin}..{ib_ex.Method.ArityMax} args, got {actualArgCount}",
                                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                                    );

                                object? result = InvokeIntrinsicForCall(ib_ex.Method, ib_ex.Receiver, argsList, instr);
                                _stack.Push(result!);
                                return StepResult.Continue;
                            }
                            else if (callee is BuiltinCallable bc)
                            {
                                if (!Builtins.TryGet(bc.Name, out BuiltinDescriptor? desc))
                                    throw new VMException($"Runtime error: unknown builtin '{bc.Name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                                if (actualArgCount < desc.ArityMin || actualArgCount > desc.ArityMax)
                                    throw new VMException(
                                        $"Runtime error: builtin '{bc.Name}' expects {desc.ArityMin}..{desc.ArityMax} args, got {actualArgCount}",
                                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                                object? result = InvokeBuiltinForCall(desc, argsList, instr);
                                _stack.Push(result!);

                                return StepResult.Continue;

                            }
                            else if (callee is BoundMethod bm)
                            {
                                f = bm.Function;
                                receiver = bm.Receiver;
                                accessType = bm.DeclaringType;
                                if (accessType == null)
                                {
                                    if (receiver is StaticInstance rst)
                                        accessType = rst;
                                    else if (receiver is ClassInstance rinst && TryGetStaticType(rinst, out StaticInstance rst2))
                                        accessType = rst2;
                                }

                                if (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0]))
                                {
                                    if (argsList.Count > 0 && Equals(argsList[0], receiver))
                                        throw new VMException(
                                            "Runtime error: receiver provided twice (BoundMethod already has implicit receiver).",
                                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                                        );
                                }
                            }
                            else if (callee is BoundType bt)
                            {
                                object ctorVal = GetIndexedValue(bt.Type, "new", instr);
                                if (ctorVal is BoundMethod ctorBound)
                                {
                                    f = ctorBound.Function;
                                    receiver = ctorBound.Receiver;
                                    accessType = ctorBound.DeclaringType ?? bt.Type;
                                }
                                else if (ctorVal is Closure ctorClos)
                                {
                                    f = ctorClos;
                                    receiver = bt.Type;
                                    accessType = bt.Type;
                                }
                                else
                                {
                                    throw new VMException("Runtime error: nested type has no constructor 'new'.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                                }

                                argsList.Insert(0, bt.Outer);
                            }
                            else if (callee is Closure clos)
                            {
                                f = clos;

                                if (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0]))
                                {
                                    if (argsList.Count == 0)
                                        throw new VMException(
                                            $"Runtime error: missing '{f.Parameters[0]}' for method call.",
                                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                                        );
                                    if (argsList[0] is NamedArgument)
                                        throw new VMException(
                                            $"Runtime error: '{f.Parameters[0]}' must be provided as the first positional argument.",
                                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                                        );
                                    receiver = argsList[0];
                                    argsList.RemoveAt(0);

                                    if (receiver is StaticInstance rst)
                                        accessType = rst;
                                    else if (receiver is ClassInstance rinst && TryGetStaticType(rinst, out StaticInstance rst2))
                                        accessType = rst2;
                                }
                            }
                            else if (callee is StaticInstance st)
                            {
                                object ctorVal = GetIndexedValue(st, "new", instr);
                                if (ctorVal is BoundMethod ctorBound)
                                {
                                    f = ctorBound.Function;
                                    receiver = ctorBound.Receiver;
                                    accessType = ctorBound.DeclaringType ?? st;
                                }
                                else if (ctorVal is Closure ctorClos)
                                {
                                    f = ctorClos;
                                    receiver = st;
                                    accessType = st;
                                }
                                else
                                {
                                    throw new VMException("Runtime error: type has no constructor 'new'.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                                }
                            }
                            else
                            {
                                throw new VMException($"Runtime error: attempt to call non-function value ({instr.Code})", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }

                            if (TryStartHotAsyncCall(f, argsList, receiver, accessType, instr, out Task<object?> hotTask))
                            {
                                _stack.Push(hotTask);
                                return StepResult.Continue;
                            }

                            Env callEnv = BuildCallEnv(f, argsList, instr);

                            int callerDepth = _scopes.Count;
                            _scopes.Add(callEnv);
                            _callStack.Push(new CallFrame(_ip, callerDepth, receiver, accessType, f.IsAsync));
                            _ip = f.Address;
                            return StepResult.Continue;
                        }

                        else
                        {
                            if (_stack.Count == 0)
                                throw new VMException("Runtime error: missing callee for CALL_INDIRECT", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            object callee = _stack.Pop();
                            Closure f;
                            object? receiver = null;
                            StaticInstance? accessType = null;

                            if (callee is IntrinsicBound ib)
                            {
                                int need = ib.Method.ArityMin;
                                List<object> argsB = new();
                                for (int i = 0; i < need; i++)
                                {
                                    if (_stack.Count == 0)
                                        throw new VMException("Runtime error: insufficient args for intrinsic call",
                                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                                    argsB.Add(_stack.Pop());
                                }

                                object? result = InvokeIntrinsicForCall(ib.Method, ib.Receiver, argsB, instr);
                                _stack.Push(result!);
                                return StepResult.Continue;
                            }
                            else if (callee is BuiltinCallable bc2)
                            {
                                if (!Builtins.TryGet(bc2.Name, out BuiltinDescriptor? desc))
                                    throw new VMException($"Runtime error: unknown builtin '{bc2.Name}'", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                                int need = desc.ArityMin;
                                List<object> argsB = new();
                                for (int i = 0; i < need; i++)
                                {
                                    if (_stack.Count == 0)
                                        throw new VMException("Runtime error: insufficient args for builtin call", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                                    argsB.Add(_stack.Pop());
                                }
                                argsB.Reverse();

                                object? result = InvokeBuiltinForCall(desc, argsB, instr);

                                _stack.Push(result!);

                                return StepResult.Continue;

                            }

                            else if (callee is BoundMethod bm)
                            {
                                f = bm.Function;
                                receiver = bm.Receiver;
                                accessType = bm.DeclaringType;
                                if (accessType == null)
                                {
                                    if (receiver is StaticInstance rst)
                                        accessType = rst;
                                    else if (receiver is ClassInstance rinst && TryGetStaticType(rinst, out StaticInstance rst2))
                                        accessType = rst2;
                                }
                            }
                            else if (callee is BoundType bt)
                            {
                                object ctorVal = GetIndexedValue(bt.Type, "new", instr);
                                if (ctorVal is BoundMethod ctorBound)
                                {
                                    f = ctorBound.Function;
                                    receiver = ctorBound.Receiver;
                                    accessType = ctorBound.DeclaringType ?? bt.Type;
                                }
                                else if (ctorVal is Closure ctorClos)
                                {
                                    f = ctorClos;
                                    receiver = bt.Type;
                                    accessType = bt.Type;
                                }
                                else
                                {
                                    throw new VMException("Runtime error: nested type has no constructor 'new'.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                                }

                                int total = f.Parameters.Count;

                                List<object> argsTmp = new();
                                for (int i = 0; i < total - 1; i++)
                                {
                                    if (_stack.Count == 0)
                                        throw new VMException("Runtime error: insufficient args for constructor call", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                                    argsTmp.Add(_stack.Pop());
                                }
                                argsTmp.Reverse();
                                argsTmp.Insert(0, bt.Outer);

                                int piStart2 = (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0])) ? 1 : 0;
                                int expected2 = f.Parameters.Count - piStart2;

                                if (argsTmp.Count != expected2)
                                    throw new VMException(
                                        $"Runtime error: argument count mismatch (expected {expected2}, got {argsTmp.Count})",
                                        instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                                    );

                                if (TryStartHotAsyncCall(f, argsTmp, receiver, accessType, instr, out Task<object?> hotTask2))
                                {
                                    _stack.Push(hotTask2);
                                    return StepResult.Continue;
                                }

                                Env callEnv2 = BuildCallEnv(f, argsTmp, instr);
                                int callerDepth2 = _scopes.Count;
                                _scopes.Add(callEnv2);
                                _callStack.Push(new CallFrame(_ip, callerDepth2, receiver, accessType, f.IsAsync));
                                _ip = f.Address;
                                return StepResult.Continue;
                            }
                            else if (callee is Closure clos)
                            {
                                f = clos;

                                if (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0]))
                                {
                                    if (_stack.Count == 0)
                                        throw new VMException(
                                            $"Runtime error: missing '{f.Parameters[0]}' for method call.",
                                            instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                                        );
                                    receiver = _stack.Pop();

                                    if (receiver is StaticInstance rst)
                                        accessType = rst;
                                    else if (receiver is ClassInstance rinst && TryGetStaticType(rinst, out StaticInstance rst2))
                                        accessType = rst2;
                                }
                            }
                            else if (callee is StaticInstance st)
                            {
                                object ctorVal = GetIndexedValue(st, "new", instr);
                                if (ctorVal is BoundMethod ctorBound)
                                {
                                    f = ctorBound.Function;
                                    receiver = ctorBound.Receiver;
                                    accessType = ctorBound.DeclaringType ?? st;
                                }
                                else if (ctorVal is Closure ctorClos)
                                {
                                    f = ctorClos;
                                    receiver = st;
                                    accessType = st;
                                }
                                else
                                {
                                    throw new VMException("Runtime error: type has no constructor 'new'.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                                }
                            }
                            else
                            {
                                throw new VMException($"Runtime error: attempt to call non-function value ({instr.Code})", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                            }

                            int piStart = (f.Parameters.Count > 0 && IsReceiverName(f.Parameters[0])) ? 1 : 0;
                            int expected = f.Parameters.Count - piStart;

                            List<object> argsList2 = new();
                            for (int pi = f.Parameters.Count - 1; pi >= piStart; pi--)
                            {
                                if (_stack.Count == 0)
                                    throw new VMException("Runtime error: insufficient args for call", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                                argsList2.Insert(0, _stack.Pop());
                            }

                            if (argsList2.Count != expected)
                                throw new VMException(
                                    $"Runtime error: argument count mismatch (expected {expected}, got {argsList2.Count})",
                                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!
                                );

                            if (TryStartHotAsyncCall(f, argsList2, receiver, accessType, instr, out Task<object?> hotTask))
                            {
                                _stack.Push(hotTask);
                                return StepResult.Continue;
                            }

                            Env callEnv = BuildCallEnv(f, argsList2, instr);

                            _scopes.Add(callEnv);
                            _callStack.Push(new CallFrame(_ip, _scopes.Count - 1, receiver, accessType, f.IsAsync));
                            _ip = f.Address;
                            return StepResult.Continue;
                        }
                    }

                case OpCode.RET:
                    {
                        object? retVal = _stack.Count > 0 ? _stack.Pop() : null;

                        TryHandler? nextFinally = null;
                        foreach (TryHandler th in _tryHandlers)
                        {
                            if (th.FinallyAddr >= 0 && !th.InFinally && th.CallDepth == _callStack.Count)
                            {
                                nextFinally = th;
                                break;
                            }
                        }

                        if (nextFinally != null)
                        {
                            nextFinally.HasPendingReturn = true;
                            nextFinally.PendingReturnValue = retVal;
                            nextFinally.InFinally = true;
                            nextFinally.CatchAddr = -1;

                            int nip = nextFinally.FinallyAddr;
                            nextFinally.FinallyAddr = -1;
                            _ip = nip;
                            return StepResult.Routed;
                        }

                        if (_callStack.Count == 0)
                            throw new VMException("Runtime error: return with empty call stack", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                        CallFrame fr = _callStack.Pop();

                        while (_scopes.Count > fr.BaseScopeDepth)
                            _scopes.RemoveAt(_scopes.Count - 1);

                        while (_tryHandlers.Count > 0 && _tryHandlers.Peek().CallDepth > _callStack.Count)
                            _tryHandlers.Pop();

                        _ip = fr.ReturnIp;
                        _stack.Push(WrapReturnForFrame(fr, retVal)!);
                        return StepResult.Continue;
                    }

                case OpCode.TRY_PUSH:
                    {
                        object[] arr = (object[])instr.Operand!;
                        int catchIp = (int)arr[0];
                        int finallyIp = (int)arr[1];

                        TryHandler th = new(
                            catchAddr: catchIp,
                            finallyAddr: finallyIp,
                            scopeDepthAtTry: _scopes.Count,
                            callDepth: _callStack.Count
                        );

                        _tryHandlers.Push(th);
                        break;
                    }

                case OpCode.TRY_POP:
                    {
                        if (_tryHandlers.Count == 0)
                            break;

                        TryHandler h = _tryHandlers.Peek();

                        if (!h.InFinally && h.FinallyAddr >= 0)
                        {
                            if (h.FinallyAddr >= _ip)
                            {
                                h.InFinally = true;
                                h.CatchAddr = -1;
                                int nip = h.FinallyAddr;
                                h.FinallyAddr = -1;
                                _ip = nip;
                                return StepResult.Routed;
                            }
                            else
                            {
                                h.FinallyAddr = -1;
                            }
                        }

                        if (h.HasPendingReturn)
                        {
                            object? retVal = h.PendingReturnValue;

                            _tryHandlers.Pop();

                            TryHandler? outerWithFinally = null;
                            foreach (TryHandler th in _tryHandlers)
                            {
                                if (th.FinallyAddr >= 0 && !th.InFinally && th.CallDepth == _callStack.Count)
                                {
                                    outerWithFinally = th;
                                    break;
                                }
                            }

                            if (outerWithFinally != null)
                            {
                                outerWithFinally.HasPendingReturn = true;
                                outerWithFinally.PendingReturnValue = retVal;
                                outerWithFinally.InFinally = true;
                                outerWithFinally.CatchAddr = -1;

                                int nip = outerWithFinally.FinallyAddr;
                                outerWithFinally.FinallyAddr = -1;
                                _ip = nip;
                                return StepResult.Routed;
                            }

                            // All outer finally blocks have been handled; now return to the caller.
                            if (_callStack.Count == 0)
                                throw new VMException("Runtime error: return with empty call stack", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                            CallFrame fr = _callStack.Pop();

                            while (_scopes.Count > fr.BaseScopeDepth)
                                _scopes.RemoveAt(_scopes.Count - 1);

                            while (_tryHandlers.Count > 0 && _tryHandlers.Peek().CallDepth > _callStack.Count)
                                _tryHandlers.Pop();

                            _ip = fr.ReturnIp;
                            _stack.Push(WrapReturnForFrame(fr, retVal)!);
                            return StepResult.Continue;
                        }

                        if (h.HasPendingLeave)
                        {
                            int leaveTarget = h.PendingLeaveTargetIp;
                            int leaveScopes = h.PendingLeaveScopes;

                            _tryHandlers.Pop();

                            TryHandler? outerWithFinally = null;
                            foreach (TryHandler th in _tryHandlers)
                            {
                                if (th.FinallyAddr >= 0 && !th.InFinally && th.CallDepth == _callStack.Count)
                                {
                                    outerWithFinally = th;
                                    break;
                                }
                            }

                            if (outerWithFinally != null)
                            {
                                outerWithFinally.HasPendingLeave = true;
                                outerWithFinally.PendingLeaveTargetIp = leaveTarget;
                                outerWithFinally.PendingLeaveScopes = leaveScopes;
                                outerWithFinally.InFinally = true;
                                outerWithFinally.CatchAddr = -1;

                                int nip = outerWithFinally.FinallyAddr;
                                outerWithFinally.FinallyAddr = -1;
                                _ip = nip;
                                return StepResult.Routed;
                            }

                            for (int i = 0; i < leaveScopes; i++)
                            {
                                if (_scopes.Count <= 1)
                                    throw new VMException("Runtime error: cannot pop global scope", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                                _scopes.RemoveAt(_scopes.Count - 1);
                            }

                            _ip = leaveTarget;
                            return StepResult.Continue;
                        }

                        if (h.Exception is object ex)
                        {
                            _tryHandlers.Pop();

                            if (RouteExceptionToTryHandlers(ex, SafeCurrentInstr(_insns, _ip), out int nip))
                            {
                                _ip = nip;
                                return StepResult.Routed;
                            }

                            Instruction instrNow = SafeCurrentInstr(_insns, _ip);
                            throw new VMException(ex.ToString()!, instrNow.Line, instrNow.Col, instrNow.OriginFile, IsDebugging, DebugStream!, BuildStackString(_insns, instrNow));
                        }

                        _tryHandlers.Pop();
                        break;
                    }

                case OpCode.THROW:
                    {
                        object? thrown = _stack.Count > 0 ? _stack.Pop() : null;

                        // Preserve the original user-thrown payload for catch(e).
                        object exPayload = thrown is ExceptionObject eo
                            ? eo
                            : (thrown ?? new ExceptionObject(
                                type: "UserError",
                                message: "throw",
                                file: instr.OriginFile,
                                line: instr.Line,
                                col: instr.Col,
                                stack: BuildStackString(_insns, instr)
                            ));

                        if (RouteExceptionToTryHandlers(exPayload, instr, out int nip))
                        {
                            _ip = nip;
                            return StepResult.Routed;
                        }

                        ExceptionObject uncaught = exPayload as ExceptionObject
                            ?? new ExceptionObject(
                                type: "UserError",
                                message: exPayload.ToString() ?? "throw",
                                file: instr.OriginFile,
                                line: instr.Line,
                                col: instr.Col,
                                stack: BuildStackString(_insns, instr)
                            );

                        throw new VMException(uncaught.ToString()!, instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!, uncaught.Stack);
                    }

                default:
                    throw new VMException($"Runtime error: unknown opcode {instr.Code}", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }

            return StepResult.Next;
        }

        /// <summary>
        /// The Run
        /// </summary>
        /// <param name="debugging">The debugging<see cref="bool"/></param>
        /// <param name="lastPos">The lastPos<see cref="int"/></param>
        public void Run(bool debugging = false, int lastPos = 0)
        {
            if (_program is null || _program.Count == 0)
                return;

            // Keep sync API for compatibility; bridge directly into async core.
            RunAsyncCore(debugging, lastPos, CancellationToken.None, resetDebugStream: true)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Defines the RunStopReason
        /// </summary>
        private enum RunStopReason
        {
            /// <summary>
            /// Defines the Halted
            /// </summary>
            Halted,

            /// <summary>
            /// Defines the AwaitPending
            /// </summary>
            AwaitPending
        }

        /// <summary>
        /// The RunUntilAwaitOrHalt
        /// </summary>
        /// <param name="debugging">The debugging<see cref="bool"/></param>
        /// <param name="lastPos">The lastPos<see cref="int"/></param>
        /// <returns>The <see cref="RunStopReason"/></returns>
        private RunStopReason RunUntilAwaitOrHalt(bool debugging = false, int lastPos = 0)
        {
            if (_program is null || _program.Count == 0)
                return RunStopReason.Halted;

            VM? prevVm = CurrentVm;
            CurrentVm = this;
            try
            {

            bool routed = false;

            IsDebugging = debugging;
            int _ip = lastPos;

            while (_ip < _program.Count)
            {
                try
                {
                    if (debugging)
                    {
                        int di = Math.Clamp(_ip, 0, _program.Count - 1);
                        Instruction dinstr = _program[di];
                        DebugStream!.Write(System.Text.Encoding.Default.GetBytes(
                            $"[DEBUG] {dinstr.Line} ->  IP={_ip}, STACK=[{string.Join(", ", _stack.Reverse())}], SCOPES={_scopes.Count}, CALLSTACK={_callStack.Count}\n"));
                        DebugStream!.Write(System.Text.Encoding.Default.GetBytes(
                            $"[DEBUG] {dinstr} (Line {dinstr.Line}, Col {dinstr.Col})\n"));
                    }

                    Instruction instr = _program[_ip++];
                    _ip = Math.Clamp(_ip, 0, _program.Count - 1);
                    StepResult res = HandleInstruction(ref _ip, _program, instr);

                    if (res == StepResult.Halt) return RunStopReason.Halted;
                    if (res == StepResult.Await) return RunStopReason.AwaitPending;
                    if (res == StepResult.Continue) continue;
                    if (res == StepResult.Routed) routed = true;
                }
                catch (VMException ex)
                {
                    int safeIp = Math.Min(_ip, _program.Count - 1);

                    ExceptionObject payload = new(
                        type: "RuntimeError",
                        message: ex.Message,
                        file: _program[safeIp].OriginFile,
                        line: _program[safeIp].Line,
                        col: _program[safeIp].Col,
                        stack: BuildStackString(_program, _program[safeIp])
                    );

                    if (RouteExceptionToTryHandlers(payload, _program[safeIp], out int nip))
                    {
                        _ip = nip;
                        routed = true;
                    }
                    else
                    {
                        throw AttachLanguageStack(ex, _program, _program[safeIp]);
                    }
                }
                catch (Exception sysEx)
                {
                    int safeIp = Math.Min(_ip, _program.Count - 1);
                    ExceptionObject payload = new(
                        type: "SystemError",
                        message: sysEx.Message,
                        file: _program[safeIp].OriginFile,
                        line: _program[safeIp].Line,
                        col: _program[safeIp].Col,
                        stack: BuildStackString(_program, _program[safeIp])
                    );

                    if (RouteExceptionToTryHandlers(payload, _program[safeIp], out int nip))
                    {
                        _ip = nip;
                        routed = true;
                    }
                    else
                    {
                        throw new VMException(
                            $"Uncaught system exception : {sysEx.Message}\n{sysEx.Source}",
                            _program[safeIp].Line,
                            _program[safeIp].Col,
                            _program[safeIp].OriginFile,
                            IsDebugging,
                            DebugStream!,
                            BuildStackString(_program, _program[safeIp]));
                    }
                }

                if (routed)
                {
                    int safeIp = Math.Min(_ip, _program.Count - 1);
                    routed = false;

                    if (_tryHandlers.Count > 0)
                    {
                        TryHandler top = _tryHandlers.Peek();

                        if (top.Exception is object deferredEx && !top.InFinally)
                        {
                            top.Exception = null;

                            if (RouteExceptionToTryHandlers(deferredEx, _program[safeIp], out int nip2))
                            {
                                _ip = nip2;
                                routed = true;
                            }
                            else
                            {
                                if (debugging)
                                {
                                    DebugStream!.Position = 0;
                                    using FileStream file = File.Create("log_file.log");
                                    DebugStream.CopyTo(file);
                                }
                                throw new VMException("Uncaught system exception",
                                    _program[safeIp].Line, _program[safeIp].Col, _program[safeIp].OriginFile, IsDebugging, DebugStream!, BuildStackString(_program, _program[safeIp]));
                            }
                        }
                    }

                    continue;
                }
            }

            return RunStopReason.Halted;

            } // end try
            finally
            {
                CurrentVm = prevVm;
            }
        }

        /// <summary>
        /// The RunAsyncCore
        /// </summary>
        /// <param name="debugging">The debugging<see cref="bool"/></param>
        /// <param name="lastPos">The lastPos<see cref="int"/></param>
        /// <param name="ct">The ct<see cref="CancellationToken"/></param>
        /// <param name="resetDebugStream">The resetDebugStream<see cref="bool"/></param>
        /// <returns>The <see cref="Task"/></returns>
        private async Task RunAsyncCore(bool debugging, int lastPos, CancellationToken ct, bool resetDebugStream)
        {
            if (resetDebugStream)
                DebugStream = new MemoryStream();

            int startIp = lastPos;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                RunStopReason reason = RunUntilAwaitOrHalt(debugging, startIp);
                if (reason == RunStopReason.Halted)
                    return;

                Task<object?> task = _awaitTask!;
                try
                {
                    object? res = await AwaitTaskRespectingCoordinatorAsync(task).ConfigureAwait(false);
                    _stack.Push(res!);
                }
                catch (Exception ex)
                {
                    int safeIp = Math.Min(_awaitResumeIp, (_program?.Count ?? 1) - 1);
                    Instruction at = SafeCurrentInstr(_program!, _awaitResumeIp);

                    ExceptionObject payload = new(
                        type: "AwaitError",
                        message: ex.Message,
                        file: at.OriginFile,
                        line: at.Line,
                        col: at.Col,
                        stack: BuildStackString(_program!, at)
                    );

                    if (RouteExceptionToTryHandlers(payload, at, out int nip))
                    {
                        startIp = nip;
                        _awaitTask = null;
                        continue;
                    }

                    throw new VMException(payload.ToString()!, at.Line, at.Col, at.OriginFile, IsDebugging, DebugStream!, payload.Stack);
                }

                startIp = _awaitResumeIp;
                _awaitTask = null;
            }
        }

        /// <summary>
        /// The RunAsync
        /// </summary>
        /// <param name="debugging">The debugging<see cref="bool"/></param>
        /// <param name="lastPos">The lastPos<see cref="int"/></param>
        /// <param name="ct">The ct<see cref="CancellationToken"/></param>
        /// <returns>The <see cref="Task"/></returns>
        public Task RunAsync(bool debugging = false, int lastPos = 0, CancellationToken ct = default)
            => RunAsyncCore(debugging, lastPos, ct, resetDebugStream: true);

        /// <summary>
        /// The LoadInstructions
        /// </summary>
        /// <param name="inst">The inst<see cref="List{Instruction}"/></param>
        public void LoadInstructions(List<Instruction> inst)
        {
            _program = inst;
        }

        /// <summary>
        /// The SafeCurrentInstr
        /// </summary>
        /// <param name="insns">The insns<see cref="List{Instruction}"/></param>
        /// <param name="ip">The ip<see cref="int"/></param>
        /// <returns>The <see cref="Instruction"/></returns>
        private static Instruction SafeCurrentInstr(List<Instruction> insns, int ip)
        {
            if (insns == null || insns.Count == 0)
                return new Instruction(OpCode.HALT, null, -1, -1, "");
            int i = ip;
            if (i < 0) i = 0;
            if (i >= insns.Count) i = insns.Count - 1;
            return insns[i];
        }

        /// <summary>
        /// The RouteExceptionToTryHandlers
        /// </summary>
        /// <param name="exPayload">The exPayload<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <param name="newIp">The newIp<see cref="int"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool RouteExceptionToTryHandlers(object exPayload, Instruction instr, out int newIp)
        {
            while (_tryHandlers.Count > 0)
            {
                TryHandler h = _tryHandlers.Peek();

                if (h.InFinally)
                {
                    _tryHandlers.Pop();
                    continue;
                }

                PopScopesToBase(h.ScopeDepthAtTry);

                if (h.CatchAddr >= 0)
                {
                    _stack.Push(exPayload);
                    newIp = h.CatchAddr;

                    h.CatchAddr = -1;
                    h.Exception = null;

                    while (_callStack.Count > h.CallDepth)
                    {
                        CallFrame fr = _callStack.Pop();

                        while (_scopes.Count > fr.BaseScopeDepth)
                            _scopes.RemoveAt(_scopes.Count - 1);

                        while (_tryHandlers.Count > 0 && _tryHandlers.Peek().CallDepth > _callStack.Count)
                            _tryHandlers.Pop();
                    }

                    return true;
                }

                if (h.FinallyAddr >= 0)
                {
                    h.Exception = exPayload;
                    newIp = h.FinallyAddr;

                    h.FinallyAddr = -1;
                    h.InFinally = true;
                    h.CatchAddr = -1;

                    while (_callStack.Count > h.CallDepth)
                    {
                        CallFrame fr = _callStack.Pop();

                        while (_scopes.Count > fr.BaseScopeDepth)
                            _scopes.RemoveAt(_scopes.Count - 1);

                        while (_tryHandlers.Count > 0 && _tryHandlers.Peek().CallDepth > _callStack.Count)
                            _tryHandlers.Pop();
                    }

                    return true;
                }

                _tryHandlers.Pop();
            }

            newIp = -1;
            return false;
        }

        /// <summary>
        /// Gets the DebugStream
        /// </summary>
        public static MemoryStream? DebugStream { get; private set; }

        /// <summary>
        /// The RequireStack
        /// </summary>
        /// <param name="needed">The needed<see cref="int"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <param name="opName">The opName<see cref="string?"/></param>
        private void RequireStack(int needed, Instruction instr, string? opName = null)
        {
            if (_stack.Count < needed)
                throw new VMException(
                    $"Runtime error: {(opName ?? instr.Code.ToString())} needs {needed} stack values (have {_stack.Count})",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        /// <summary>
        /// The AttachLanguageStack
        /// </summary>
        /// <param name="ex">The ex<see cref="VMException"/></param>
        /// <param name="insns">The insns<see cref="List{Instruction}"/></param>
        /// <param name="current">The current<see cref="Instruction"/></param>
        /// <returns>The <see cref="VMException"/></returns>
        private VMException AttachLanguageStack(VMException ex, List<Instruction> insns, Instruction current)
        {
            if (!string.IsNullOrWhiteSpace(ex.LanguageStackTrace))
                return ex;

            string stack = BuildStackString(insns, current);
            int line = ex.Line >= 0 ? ex.Line : current.Line;
            int col = ex.Column >= 0 ? ex.Column : current.Col;
            string? file = !string.IsNullOrWhiteSpace(ex.FileSource) ? ex.FileSource : current.OriginFile;

            return new VMException(
                ex.RawMessage,
                line,
                col,
                file,
                IsDebugging,
                DebugStream!,
                stack);
        }

        /// <summary>
        /// The BuildStackString
        /// </summary>
        /// <param name="insns">The insns<see cref="List{Instruction}"/></param>
        /// <param name="current">The current<see cref="Instruction"/></param>
        /// <returns>The <see cref="string"/></returns>
        private string BuildStackString(List<Instruction> insns, Instruction current)
        {
            StringBuilder sb = new();

            sb.Append("  at ")
              .Append(current.OriginFile).Append(':')
              .Append(current.Line).Append(':')
              .Append(current.Col).AppendLine();

            foreach (CallFrame? frame in _callStack.Reverse())
            {
                int ip = Math.Clamp(frame.ReturnIp, 0, insns.Count - 1);
                Instruction i = insns[ip];
                sb.Append("  at ")
                  .Append(i.OriginFile).Append(':')
                  .Append(i.Line).Append(':')
                  .Append(i.Col).AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// The DumpStack
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        private string DumpStack()
        {
            if (_stack == null || _stack.Count == 0) return "<empty>";
            object[] arr = _stack.ToArray();
            IEnumerable<string> parts = arr.Select(FormatVal);
            return string.Join(" | ", parts);
        }

        /// <summary>
        /// The DumpCallStack
        /// </summary>
        /// <returns>The <see cref="string"/></returns>
        private string DumpCallStack()
        {
            if (_callStack == null || _callStack.Count == 0) return "<empty>";

            CallFrame[] arr = _callStack.ToArray();
            int curDepth = _scopes?.Count ?? 0;

            IEnumerable<string> parts = arr.Select((fr, i) =>
            {
                bool isTop = (i == 0);
                int scopesPlus = Math.Max(0, curDepth - fr.BaseScopeDepth);

                string thisPart = fr.ThisRef != null ? FormatVal(fr.ThisRef) : "null";
                string scopeInfo = isTop
                    ? $"base={fr.BaseScopeDepth}, scopes+={scopesPlus}"
                    : $"base={fr.BaseScopeDepth}";

                return $"#{i}: ret={fr.ReturnIp}, {scopeInfo}, this={thisPart}";
            });

            return string.Join(" ; ", parts);
        }

        /// <summary>
        /// The FormatVal
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <returns>The <see cref="string"/></returns>
        private string FormatVal(object? v)
        {
            if (v == null) return "null";
            switch (v)
            {
                case string s:
                    return $"\"{s}\"";
                case List<object> list:
                    return $"[{list.Count} elems]";
                case Dictionary<string, object> dict:
                    return $"{{{dict.Count} pairs}}";
                case ClassInstance ci:
                    return $"Object({ci.ClassName})";
                case Closure clos:
                    return $"Closure({clos.Name ?? clos.Address.ToString()})";
                case BoundMethod bm:
                    return $"BoundMethod({bm.Function.Name ?? bm.Function.Address.ToString()})";
                default:
                    return $"{v} : {v.GetType().Name}";
            }
        }

        /// <summary>
        /// The JsonEscapeString
        /// </summary>
        /// <param name="s">The s<see cref="string"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string JsonEscapeString(string s)
        {
            StringBuilder sb = new(s.Length + 8);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ')
                            sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:X4}", (int)ch);
                        else
                            sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// The WriteJsonValue
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <param name="w">The w<see cref="TextWriter"/></param>
        /// <param name="seen">The seen<see cref="HashSet{object}?"/></param>
        /// <param name="mode">The mode<see cref="int"/></param>
        private static void WriteJsonValue(object? v, TextWriter w, HashSet<object>? seen = null, int mode = 2)
        {
            seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

            if (v is null) { w.Write("null"); return; }

            switch (v)
            {
                case bool b:
                    w.Write(b ? "true" : "false"); return;

                case sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    w.Write(Convert.ToString(v, CultureInfo.InvariantCulture)); return;

                case string s:
                    w.Write('"'); w.Write(JsonEscapeString(s)); w.Write('"'); return;

                case ExceptionObject exo:
                    {
                        w.Write('{');
                        w.Write("\"type\":\""); w.Write(JsonEscapeString(exo.Type)); w.Write("\",");
                        w.Write("\"message\":\""); w.Write(JsonEscapeString(exo.eMessage)); w.Write("\",");
                        w.Write("\"file\":\""); w.Write(JsonEscapeString(exo.File)); w.Write("\",");
                        w.Write("\"line\":"); w.Write(exo.Line.ToString(CultureInfo.InvariantCulture)); w.Write(",");
                        w.Write("\"col\":"); w.Write(exo.Col.ToString(CultureInfo.InvariantCulture));
                        if (!string.IsNullOrEmpty(exo.Stack))
                        {
                            w.Write(",\"stack\":\""); w.Write(JsonEscapeString(exo.Stack)); w.Write('"');
                        }
                        w.Write('}');
                        return;
                    }

                case List<object> list:
                    {
                        if (seen.Contains(v)) { w.Write("[]"); return; }
                        seen.Add(v);
                        w.Write('[');
                        for (int i = 0; i < list.Count; i++)
                        {
                            WriteJsonValue(list[i], w, seen, mode);
                            if (i + 1 < list.Count) w.Write(',');
                        }
                        w.Write(']');
                        seen.Remove(v);
                        return;
                    }

                case Dictionary<string, object> dict:
                    {
                        if (seen.Contains(v)) { w.Write("{}"); return; }
                        seen.Add(v);

                        List<KeyValuePair<string, object>> entries = dict.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
                        if (mode == 2)
                        {
                            entries = [.. entries
                    .Where(kv => kv.Value != null
                                 && kv.Value is not Closure
                                 && kv.Value is not FunctionInfo
                                 && kv.Value is not Delegate)];
                        }

                        w.Write('{');
                        for (int i = 0; i < entries.Count; i++)
                        {
                            KeyValuePair<string, object> kv = entries[i];
                            w.Write('"'); w.Write(JsonEscapeString(kv.Key)); w.Write("\":");
                            WriteJsonValue(kv.Value, w, seen, mode);
                            if (i + 1 < entries.Count) w.Write(',');
                        }
                        w.Write('}');
                        seen.Remove(v);
                        return;
                    }

                case Closure clos:
                    w.Write('"'); w.Write(JsonEscapeString(clos.Name ?? "<closure>")); w.Write('"'); return;

                default:
                    w.Write('"'); w.Write(JsonEscapeString(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "")); w.Write('"');
                    return;
            }
        }

        /// <summary>
        /// The JsonStringify
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <param name="mode">The mode<see cref="int"/></param>
        /// <returns>The <see cref="string"/></returns>
        public static string JsonStringify(object? v, int mode = 2)
        {
            StringBuilder sb = new();
            using StringWriter sw = new(sb, CultureInfo.InvariantCulture);
            WriteJsonValue(v, sw, null, mode);
            return sb.ToString();
        }

        /// <summary>
        /// The PrintValue
        /// </summary>
        /// <param name="v">The v<see cref="object"/></param>
        /// <param name="w">The w<see cref="TextWriter"/></param>
        /// <param name="mode">The mode<see cref="int"/></param>
        /// <param name="seen">The seen<see cref="HashSet{object}?"/></param>
        /// <param name="escapeNewlines">The escapeNewlines<see cref="bool"/></param>
        public static void PrintValue(object v, TextWriter w, int mode = 2, HashSet<object>? seen = null, bool escapeNewlines = false)
        {
            static string UnescapeForPrinting(string s)
            {
                return s
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t")
                    .Replace("\\b", "\b")
                    .Replace("\\f", "\f");
            }

            seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

            if (v == null) { w.Write("null"); return; }

            if (v is List<object> list)
            {
                if (seen.Contains(v)) { w.Write("[...]"); return; }
                seen.Add(v);
                w.Write("[");
                for (int i = 0; i < list.Count; i++)
                {
                    PrintValue(list[i], w, mode, seen, escapeNewlines);
                    if (i + 1 < list.Count) w.Write(", ");
                }
                w.Write("]");
                seen.Remove(v);
                return;
            }

            if (v is Dictionary<string, object> dict)
            {
                if (seen.Contains(v)) { w.Write("{...}"); return; }
                seen.Add(v);

                List<KeyValuePair<string, object>> entries = dict.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
                if (mode == 2)
                {
                    entries = [.. entries
                .Where(kv => kv.Value != null
                             && kv.Value is not Closure
                             && kv.Value is not FunctionInfo
                             && kv.Value is not Delegate)];
                }

                w.Write("{");
                for (int i = 0; i < entries.Count; i++)
                {
                    KeyValuePair<string, object> kv = entries[i];
                    w.Write("\"");
                    w.Write(escapeNewlines ? UnescapeForPrinting(kv.Key) : kv.Key);
                    w.Write("\": ");
                    PrintValue(kv.Value, w, mode, seen, escapeNewlines);
                    if (i + 1 < entries.Count) w.Write(", ");
                }
                w.Write("}");
                seen.Remove(v);
                return;
            }
            if (v is ClassInstance ci)
            {
                w.Write(ci.ClassName);
                return;
            }

            if (v is Closure clos)
            {
                if (mode == 2)
                    w.Write("\"" + (escapeNewlines ? UnescapeForPrinting(clos.Name ?? "<closure>") : (clos.Name ?? "<closure>")) + "\"");
                else
                    w.Write(clos.ToString());
                return;
            }

            if (v is ExceptionObject exo)
            {
                w.Write(exo.ToString());
                return;
            }

            if (v is FunctionInfo fi) { w.Write($"<fn {fi.Address}>"); return; }
            if (v is Delegate) { w.Write("<delegate>"); return; }

            switch (v)
            {
                case string s:
                    w.Write(escapeNewlines ? UnescapeForPrinting(s) : s);
                    break;
                case double xd: w.Write(xd.ToString(CultureInfo.InvariantCulture)); break;
                case float f: w.Write(f.ToString(CultureInfo.InvariantCulture)); break;
                case decimal m: w.Write(m.ToString(CultureInfo.InvariantCulture)); break;
                case long l: w.Write(l.ToString(CultureInfo.InvariantCulture)); break;
                case int i: w.Write(i.ToString(CultureInfo.InvariantCulture)); break;
                case bool b: w.Write(b ? "true" : "false"); break;
                default:
                    w.Write(Convert.ToString(v, CultureInfo.InvariantCulture));
                    break;
            }
            w.Flush();
        }

        /// <summary>
        /// The RequireIntIndex
        /// </summary>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="int"/></returns>
        private static int RequireIntIndex(object idxObj, Instruction instr)
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
        /// The CreateIndexException
        /// </summary>
        /// <param name="target">The target<see cref="object"/></param>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="VMException"/></returns>
        private static VMException CreateIndexException(object target, object idxObj, Instruction instr)
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
        /// <param name="inst">The inst<see cref="ClassInstance"/></param>
        /// <param name="type">The type<see cref="StaticInstance"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetStaticType(ClassInstance inst, out StaticInstance type)
        {
            if (inst.Fields.TryGetValue("__type", out object? tObj) && tObj is StaticInstance st)
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
            if (!type.Fields.TryGetValue("__is_interface", out object? raw) || raw == null)
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
            if (!type.Fields.TryGetValue("__interfaces", out object? raw) || raw is not List<object> entries)
                yield break;

            foreach (object? entry in entries)
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

                current = current.Fields.TryGetValue("__base", out object? bObj) && bObj is StaticInstance baseType
                    ? baseType
                    : null;
            }

            return false;
        }

        /// <summary>
        /// The NormalizeVisibilityCode
        /// </summary>
        /// <param name="raw">The raw<see cref="object"/></param>
        /// <returns>The <see cref="int"/></returns>
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
        /// <param name="code">The code<see cref="int"/></param>
        /// <returns>The <see cref="string"/></returns>
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
        /// <param name="ownerType">The ownerType<see cref="StaticInstance"/></param>
        /// <param name="expectInstance">The expectInstance<see cref="bool"/></param>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="code">The code<see cref="int"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetDeclaredVisibilityCode(
            StaticInstance ownerType,
            bool expectInstance,
            string memberName,
            out int code)
        {
            code = VisibilityPublic;
            string mapName = expectInstance ? "__vis_inst" : "__vis_static";
            if (!ownerType.Fields.TryGetValue(mapName, out object? mapObj) || mapObj is not Dictionary<string, object> map)
                return false;

            if (!map.TryGetValue(memberName, out object? rawCode))
                return false;

            code = NormalizeVisibilityCode(rawCode);
            return true;
        }

        /// <summary>
        /// The TryResolveDeclaredVisibilityInHierarchy
        /// </summary>
        /// <param name="startType">The startType<see cref="StaticInstance"/></param>
        /// <param name="expectInstance">The expectInstance<see cref="bool"/></param>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="ownerType">The ownerType<see cref="StaticInstance"/></param>
        /// <param name="code">The code<see cref="int"/></param>
        /// <returns>The <see cref="bool"/></returns>
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

                if (!current.Fields.TryGetValue("__base", out object? bObj) || bObj is not StaticInstance baseType)
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
                if (current.Fields.TryGetValue(mapName, out object? mapObj) &&
                    mapObj is Dictionary<string, object> map &&
                    map.ContainsKey(fieldName))
                    return true;

                if (!current.Fields.TryGetValue("__base", out object? bObj) || bObj is not StaticInstance baseType)
                    break;

                current = baseType;
            }
            return false;
        }

        /// <summary>
        /// The TryResolveInstanceMemberInHierarchy
        /// </summary>
        /// <param name="start">The start<see cref="ClassInstance"/></param>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="ownerInst">The ownerInst<see cref="ClassInstance"/></param>
        /// <param name="ownerType">The ownerType<see cref="StaticInstance"/></param>
        /// <param name="value">The value<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
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
                if (current.Fields.TryGetValue(memberName, out value))
                {
                    ownerInst = current;
                    ownerType = TryGetStaticType(current, out StaticInstance st) ? st : null;
                    return true;
                }

                if (!current.Fields.TryGetValue("__base", out object? bObj) || bObj is not ClassInstance baseInst)
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
        /// <param name="start">The start<see cref="StaticInstance"/></param>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="ownerType">The ownerType<see cref="StaticInstance"/></param>
        /// <param name="value">The value<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryResolveStaticMemberInHierarchy(
            StaticInstance start,
            string memberName,
            out StaticInstance ownerType,
            out object? value)
        {
            StaticInstance current = start;
            while (true)
            {
                if (current.Fields.TryGetValue(memberName, out value))
                {
                    ownerType = current;
                    return true;
                }

                if (!current.Fields.TryGetValue("__base", out object? bObj) || bObj is not StaticInstance baseType)
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
        /// <returns>The <see cref="StaticInstance?"/></returns>
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
        /// <param name="candidate">The candidate<see cref="StaticInstance"/></param>
        /// <param name="baseType">The baseType<see cref="StaticInstance"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsSameOrDerivedStaticType(StaticInstance candidate, StaticInstance baseType)
        {
            StaticInstance current = candidate;
            while (true)
            {
                if (ReferenceEquals(current, baseType))
                    return true;

                if (!current.Fields.TryGetValue("__base", out object? bObj) || bObj is not StaticInstance parent)
                    break;

                current = parent;
            }

            return false;
        }

        /// <summary>
        /// The IsRuntimeAccessAllowed
        /// </summary>
        /// <param name="ownerType">The ownerType<see cref="StaticInstance"/></param>
        /// <param name="visibilityCode">The visibilityCode<see cref="int"/></param>
        /// <returns>The <see cref="bool"/></returns>
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
        /// <param name="ownerType">The ownerType<see cref="StaticInstance"/></param>
        /// <param name="visibilityCode">The visibilityCode<see cref="int"/></param>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="isStatic">The isStatic<see cref="bool"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
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
        /// <param name="target">The target<see cref="object"/></param>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="object"/></returns>
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
                        if (index < 0 || index >= arr.Count)
                            throw new VMException($"Runtime error: index {index} out of range", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        return arr[index];
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
                        if (dict.TryGetValue(key, out object? val))
                            return val;
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
                            if (obj.Fields.TryGetValue("__outer", out object? outerVal))
                                return outerVal;

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
                                string m = args[0]?.ToString() ?? "";
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
        /// <param name="target">The target<see cref="object"/></param>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <param name="value">The value<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <param name="allowReservedRuntimeSlotWrites">The allowReservedRuntimeSlotWrites<see cref="bool"/></param>
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
                        if (index < 0 || index >= arr.Count)
                            throw new VMException($"Runtime error: index {index} out of range (0..{arr.Count - 1})", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        arr[index] = value;
                        break;
                    }

                case string _:
                    throw new VMException("Runtime error: INDEX_SET on string. Strings are immutable.", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

                case Dictionary<string, object> dict:
                    {
                        MarkAsyncHazardForMutableCollection(dict);
                        string key = NormalizeDictionaryWriteKey(dict, idxObj, instr);
                        dict[key] = value;
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

                        obj.Fields[key] = value;
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

                        st.Fields[key] = value;
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
        /// The DeleteSliceOnTarget
        /// </summary>
        /// <param name="target">The target<see cref="object"/></param>
        /// <param name="startObj">The startObj<see cref="object"/></param>
        /// <param name="endObj">The endObj<see cref="object"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        private static void DeleteSliceOnTarget(ref object target, object startObj, object endObj, Instruction instr)
        {
            if (target is string)
                throw new VMException("Runtime error: delete on strings is not allowed", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            switch (target)
            {
                case List<object> arr:
                    {
                        (int start, int end) = NormalizeSliceBounds(startObj, endObj, arr.Count, instr);

                        if (start < end)
                            arr.RemoveRange(start, end - start);
                        return;
                    }

                case Dictionary<string, object> dict:
                    {
                        List<string> keys = dict.Keys.ToList();
                        (int start, int end) = NormalizeSliceBounds(startObj, endObj, keys.Count, instr);

                        for (int i = end - 1; i >= start; i--)
                            dict.Remove(keys[i]);

                        return;
                    }

                default:
                    throw new VMException($"Runtime error: delete slice target must be array, dictionary, or string", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }
        }

        /// <summary>
        /// The NormalizeSliceBounds
        /// </summary>
        /// <param name="startObj">The startObj<see cref="object?"/></param>
        /// <param name="endObj">The endObj<see cref="object?"/></param>
        /// <param name="len">The len<see cref="int"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="(int start, int endEx)"/></returns>
        private static (int start, int endEx) NormalizeSliceBounds(object? startObj, object? endObj, int len, Instruction instr)
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
        /// Gets the Intrinsics
        /// </summary>
        public IntrinsicRegistry Intrinsics { get; } = new();

        /// <summary>
        /// The TryBindIntrinsic
        /// </summary>
        /// <param name="receiver">The receiver<see cref="object"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="bound">The bound<see cref="IntrinsicBound"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryBindIntrinsic(object receiver, string name, out IntrinsicBound bound, Instruction instr)
        {
            Type t = receiver?.GetType() ?? typeof(object);
            if (Intrinsics.TryGet(t, name, out IntrinsicDescriptor? desc))
            {
                IntrinsicMethod adapted = new(
                    desc.Name,
                    desc.ArityMin,
                    desc.ArityMax,
                    (recv, args, ins) => desc.Invoke(recv, args, ins),
                    smartAwait: desc.SmartAwait,
                    nonBlocking: desc.NonBlocking
                );
                bound = new IntrinsicBound(adapted, receiver!);
                return true;
            }
            bound = null!;
            return false;
        }

        /// <summary>
        /// The NormalizeDictionaryWriteKey
        /// </summary>
        /// <param name="dict">The dict<see cref="Dictionary{string, object}"/></param>
        /// <param name="idxObj">The idxObj<see cref="object?"/></param>
        /// <param name="instr">The instr<see cref="Instruction"/></param>
        /// <returns>The <see cref="string"/></returns>
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
        /// The IsReservedIntrinsicName
        /// </summary>
        /// <param name="receiverType">The receiverType<see cref="Type"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool IsReservedIntrinsicName(Type receiverType, string name)
            => Intrinsics.TryGet(receiverType, name, out _);

        /// <summary>
        /// The IsReservedIntrinsicName
        /// </summary>
        /// <param name="receiver">The receiver<see cref="object"/></param>
        /// <param name="idxObj">The idxObj<see cref="object"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool IsReservedIntrinsicName(object receiver, object idxObj)
        {
            if (idxObj is not string name) return false;
            Type t = receiver?.GetType() ?? typeof(object);
            return IsReservedIntrinsicName(t, name);
        }

        /// <summary>
        /// The IsReservedRuntimeSlotName
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsReservedRuntimeSlotName(string name)
            => name.StartsWith("__", StringComparison.Ordinal) ||
               string.Equals(name, "new", StringComparison.Ordinal);

        /// <summary>
        /// Initializes a new instance of the <see cref="VM"/> class.
        /// </summary>
        public VM()
        {
            DebugStream = new MemoryStream();
        }
    }
}
