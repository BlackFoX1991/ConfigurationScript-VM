using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace CFGS_VM.VMCore
{
    /// <summary>
    /// Discriminates the inline storage of a <see cref="Value"/>.
    /// </summary>
    public enum ValueKind : byte
    {
        /// <summary>The language <c>null</c>.</summary>
        Null,

        /// <summary>A <see cref="bool"/> stored inline.</summary>
        Bool,

        /// <summary>A 32-bit <see cref="int"/> stored inline.</summary>
        Int,

        /// <summary>A 64-bit <see cref="long"/> stored inline.</summary>
        Long,

        /// <summary>A <see cref="double"/> stored inline (as raw bits).</summary>
        Double,

        /// <summary>A <see cref="char"/> stored inline.</summary>
        Char,

        /// <summary>
        /// A reference- or rarely-used value (string, float, decimal, BigInteger, collections,
        /// class/static/enum instances, closures, handles, …) carried in <see cref="_ref"/>.
        /// </summary>
        Ref
    }

    /// <summary>
    /// Tagged runtime value for the CFGS evaluation stack and data model. The five hottest scalar
    /// kinds (int, long, double, bool, char) are stored inline so pushing/popping them does not
    /// allocate a boxed object; every other runtime type is carried unchanged in the reference
    /// slot. <see cref="FromObject"/> / <see cref="ToObject"/> bridge to the legacy <see cref="object"/>
    /// representation during the incremental migration and at the host-interop boundary.
    /// </summary>
    public readonly struct Value : IEquatable<Value>
    {
        private readonly ValueKind _kind;
        private readonly long _bits;
        private readonly object? _ref;

        private Value(ValueKind kind, long bits, object? reference)
        {
            _kind = kind;
            _bits = bits;
            _ref = reference;
        }

        /// <summary>The language <c>null</c> value.</summary>
        public static readonly Value Null = new(ValueKind.Null, 0, null);

        /// <summary>Gets the storage discriminator.</summary>
        public ValueKind Kind => _kind;

        public bool IsNull => _kind == ValueKind.Null;

        public bool IsReference => _kind == ValueKind.Ref;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Value FromBool(bool v) => new(ValueKind.Bool, v ? 1L : 0L, null);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Value FromInt(int v) => new(ValueKind.Int, v, null);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Value FromLong(long v) => new(ValueKind.Long, v, null);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Value FromDouble(double v) => new(ValueKind.Double, BitConverter.DoubleToInt64Bits(v), null);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Value FromChar(char v) => new(ValueKind.Char, v, null);

        /// <summary>Wraps a reference-or-other value (never used for the inline scalar kinds).</summary>
        public static Value FromRef(object? reference)
            => reference is null ? Null : new(ValueKind.Ref, 0, reference);

        public bool AsBool => _bits != 0L;

        public int AsInt => (int)_bits;

        public long AsLong => _bits;

        public double AsDouble => BitConverter.Int64BitsToDouble(_bits);

        public char AsChar => (char)_bits;

        /// <summary>Gets the carried reference for <see cref="ValueKind.Ref"/> values; otherwise null.</summary>
        public object? Reference => _ref;

        /// <summary>
        /// Lifts a legacy <see cref="object"/> runtime value into a <see cref="Value"/>, storing the
        /// hot scalar kinds inline and carrying everything else (including <c>byte</c>, <c>float</c>,
        /// <c>decimal</c>, <see cref="BigInteger"/>, strings, collections and instances) by reference
        /// so the original type round-trips exactly.
        /// </summary>
        public static Value FromObject(object? value)
        {
            switch (value)
            {
                case null: return Null;
                case bool b: return FromBool(b);
                case int i: return FromInt(i);
                case long l: return FromLong(l);
                case double d: return FromDouble(d);
                case char c: return FromChar(c);
                default: return new Value(ValueKind.Ref, 0, value);
            }
        }

        /// <summary>
        /// Lowers a <see cref="Value"/> back to the legacy <see cref="object"/> representation,
        /// boxing the inline scalar kinds. Used at boundaries with code that has not yet been
        /// migrated to operate on <see cref="Value"/> directly.
        /// </summary>
        public object? ToObject()
            => _kind switch
            {
                ValueKind.Null => null,
                ValueKind.Bool => AsBool,
                ValueKind.Int => AsInt,
                ValueKind.Long => AsLong,
                ValueKind.Double => AsDouble,
                ValueKind.Char => AsChar,
                _ => _ref
            };

        /// <summary>
        /// Structural identity of the inline storage: two values are equal only when they share the
        /// same <see cref="ValueKind"/> and identical bits (or reference). This is deliberately NOT
        /// the language's numeric equality — <c>Int 1</c> does not equal <c>Long 1</c> here, and two
        /// <c>NaN</c> doubles with the same bit pattern compare equal. Language-level <c>==</c>/<c>!=</c>
        /// (cross-kind numeric coercion, IEEE NaN semantics) lives in <c>ValueOps.RuntimeValuesEqual</c>;
        /// for raw value identity in a keyed collection prefer the explicit <see cref="StructuralComparer"/>
        /// so the intent is visible at the call site. Never use this as the script-visible equality operator.
        /// </summary>
        public bool Equals(Value other)
        {
            if (_kind != other._kind)
                return false;

            return _kind == ValueKind.Ref
                ? Equals(_ref, other._ref)
                : _bits == other._bits;
        }

        public override bool Equals(object? obj) => obj is Value v && Equals(v);

        public override int GetHashCode()
            => _kind == ValueKind.Ref
                ? (_ref?.GetHashCode() ?? 0)
                : HashCode.Combine(_kind, _bits);

        public override string ToString()
            => _kind switch
            {
                ValueKind.Null => "null",
                ValueKind.Bool => AsBool ? "true" : "false",
                ValueKind.Int => AsInt.ToString(CultureInfo.InvariantCulture),
                ValueKind.Long => AsLong.ToString(CultureInfo.InvariantCulture),
                ValueKind.Double => AsDouble.ToString(CultureInfo.InvariantCulture),
                ValueKind.Char => AsChar.ToString(),
                _ => _ref?.ToString() ?? "null"
            };

        /// <summary>
        /// The explicit comparer for the struct's raw structural identity (kind plus bits or reference).
        /// Use this by name when a keyed collection genuinely needs value identity (for example a
        /// <c>HashSet&lt;Value&gt;</c> or <c>Dictionary&lt;Value, …&gt;</c>), so the call site documents that it
        /// is NOT the script-visible equality. Script-level <c>==</c>/<c>!=</c> semantics (cross-kind numeric
        /// coercion, IEEE NaN) are provided by <c>ValueOps.RuntimeValuesEqual</c>, never by this comparer.
        /// </summary>
        public static IEqualityComparer<Value> StructuralComparer { get; } = new StructuralValueComparer();

        private sealed class StructuralValueComparer : IEqualityComparer<Value>
        {
            public bool Equals(Value x, Value y) => x.Equals(y);

            public int GetHashCode(Value obj) => obj.GetHashCode();
        }
    }
}
