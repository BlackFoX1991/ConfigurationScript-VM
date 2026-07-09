using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace CFGS_VM.VMCore
{
    public partial class VM
    {
        public static bool IsNumber(object x) =>
            x is sbyte or byte or short or ushort or int or uint or long or ulong
              or float or double or decimal or char or BigInteger;

        private static int ToInt32Invariant(object x) => x switch
        {
            string s => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture),
            double d => DoubleToInt32(d),
            float f => DoubleToInt32(f),
            decimal m => checked((int)decimal.Truncate(m)),
            BigInteger bi => checked((int)bi),
            IConvertible _ => Convert.ToInt32(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        private static long ToInt64Invariant(object x) => x switch
        {
            string s => long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture),
            double d => DoubleToInt64(d),
            float f => DoubleToInt64(f),
            decimal m => checked((long)decimal.Truncate(m)),
            BigInteger bi => checked((long)bi),
            IConvertible _ => Convert.ToInt64(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        private static ulong ToUInt64Invariant(object x) => x switch
        {
            string s => ulong.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture),
            double d => DoubleToUInt64(d),
            float f => DoubleToUInt64(f),
            decimal m => DecimalToUInt64(m),
            BigInteger bi => checked((ulong)bi),
            IConvertible _ => Convert.ToUInt64(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        private static double ToDoubleInvariant(object x) => x switch
        {
            string s => double.Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
            BigInteger bi => (double)bi,
            IConvertible _ => Convert.ToDouble(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        private static decimal ToDecimalInvariant(object x) => x switch
        {
            string s => decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture),
            BigInteger bi => bi >= (BigInteger)decimal.MinValue && bi <= (BigInteger)decimal.MaxValue
                ? (decimal)bi
                : throw new OverflowException("BigInteger value is outside the representable decimal range."),
            IConvertible _ => Convert.ToDecimal(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

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
            BigInteger v when v >= (BigInteger)decimal.MinValue && v <= (BigInteger)decimal.MaxValue => (decimal)v,
            BigInteger _ => throw new OverflowException("BigInteger value is outside the representable decimal range."),
            null => throw new InvalidOperationException("Not numeric: null"),
            _ => throw new InvalidOperationException($"Not numeric: {x.GetType().Name}")
        };

        private static bool IsIntegralLike(object x) =>
            x is sbyte or byte or short or ushort or int or uint or long or ulong or char or BigInteger;

        private static bool TryGetDecimalBigIntegerPair(
            Value left,
            Value right,
            out decimal decimalValue,
            out BigInteger bigIntegerValue,
            out bool decimalIsLeft)
        {
            if (left.Kind == ValueKind.Ref && left.Reference is decimal leftDecimal &&
                right.Kind == ValueKind.Ref && right.Reference is BigInteger rightBigInteger)
            {
                decimalValue = leftDecimal;
                bigIntegerValue = rightBigInteger;
                decimalIsLeft = true;
                return true;
            }

            if (right.Kind == ValueKind.Ref && right.Reference is decimal rightDecimal &&
                left.Kind == ValueKind.Ref && left.Reference is BigInteger leftBigInteger)
            {
                decimalValue = rightDecimal;
                bigIntegerValue = leftBigInteger;
                decimalIsLeft = false;
                return true;
            }

            decimalValue = default;
            bigIntegerValue = default;
            decimalIsLeft = false;
            return false;
        }

        private static BigInteger DecimalIntegralToBigInteger(decimal value)
            => BigInteger.Parse(decimal.Truncate(value).ToString(CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture);

        private static int CompareDecimalAndBigInteger(decimal decimalValue, BigInteger bigIntegerValue)
        {
            BigInteger decimalIntegral = DecimalIntegralToBigInteger(decimalValue);
            int integralComparison = decimalIntegral.CompareTo(bigIntegerValue);
            if (integralComparison != 0)
                return integralComparison;

            decimal fractional = decimalValue - decimal.Truncate(decimalValue);
            if (fractional > 0m)
                return 1;
            if (fractional < 0m)
                return -1;
            return 0;
        }

        private static bool TryCompareDecimalBigIntegerPair(Value left, Value right, out int comparison)
        {
            if (TryGetDecimalBigIntegerPair(left, right, out decimal decimalValue, out BigInteger bigIntegerValue, out bool decimalIsLeft))
            {
                comparison = CompareDecimalAndBigInteger(decimalValue, bigIntegerValue);
                if (!decimalIsLeft)
                    comparison = -comparison;
                return true;
            }

            comparison = 0;
            return false;
        }

        private static bool TryEqualDecimalBigIntegerPair(Value left, Value right, out bool equals)
        {
            if (TryCompareDecimalBigIntegerPair(left, right, out int comparison))
            {
                equals = comparison == 0;
                return true;
            }

            equals = false;
            return false;
        }

        private static bool TryGetFloatingDouble(Value value, out double floatingValue)
        {
            switch (value.Kind)
            {
                case ValueKind.Double:
                    floatingValue = value.AsDouble;
                    return true;
                case ValueKind.Ref when value.Reference is float f:
                    floatingValue = f;
                    return true;
                default:
                    floatingValue = default;
                    return false;
            }
        }

        private static bool RequiresSpecialDecimalFloatingComparison(double floatingValue)
            => double.IsNaN(floatingValue) ||
               double.IsInfinity(floatingValue) ||
               floatingValue > (double)decimal.MaxValue ||
               floatingValue < (double)decimal.MinValue;

        private static bool TryGetDecimalFloatingSpecialPair(
            Value left,
            Value right,
            out double leftAsDouble,
            out double rightAsDouble)
        {
            if (left.Kind == ValueKind.Ref && left.Reference is decimal leftDecimal &&
                TryGetFloatingDouble(right, out double rightFloating) &&
                RequiresSpecialDecimalFloatingComparison(rightFloating))
            {
                leftAsDouble = (double)leftDecimal;
                rightAsDouble = rightFloating;
                return true;
            }

            if (right.Kind == ValueKind.Ref && right.Reference is decimal rightDecimal &&
                TryGetFloatingDouble(left, out double leftFloating) &&
                RequiresSpecialDecimalFloatingComparison(leftFloating))
            {
                leftAsDouble = leftFloating;
                rightAsDouble = (double)rightDecimal;
                return true;
            }

            leftAsDouble = default;
            rightAsDouble = default;
            return false;
        }

        private static bool TryCompareDecimalFloatingSpecialPair(Value left, Value right, out int comparison)
        {
            if (TryGetDecimalFloatingSpecialPair(left, right, out double leftAsDouble, out double rightAsDouble))
            {
                comparison = leftAsDouble.CompareTo(rightAsDouble);
                return true;
            }

            comparison = 0;
            return false;
        }

        private static bool TryEqualDecimalFloatingSpecialPair(Value left, Value right, out bool equals)
        {
            if (TryGetDecimalFloatingSpecialPair(left, right, out double leftAsDouble, out double rightAsDouble))
            {
                equals = leftAsDouble == rightAsDouble;
                return true;
            }

            equals = false;
            return false;
        }

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

        private Value PowBigInteger(BigInteger a, BigInteger b, Instruction instr)
        {
            if (b.Sign < 0)
                return Value.FromDouble(Math.Pow((double)a, (double)b));
            if (b > int.MaxValue)
                throw new VMException("EXPO exponent too large for BigInteger.Pow", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            return Value.FromRef(BigInteger.Pow(a, (int)b));
        }

        /// <summary>
        /// Narrows a <see cref="BigInteger"/> to the smallest of int/long/BigInteger that can hold it.
        /// </summary>
        private static Value NarrowIntegerValue(BigInteger bi)
        {
            if (bi >= int.MinValue && bi <= int.MaxValue) return Value.FromInt((int)bi);
            if (bi >= long.MinValue && bi <= long.MaxValue) return Value.FromLong((long)bi);
            return Value.FromRef(bi);
        }

        /// <summary>Stores a 64-bit result as the narrowest of int/long that can hold it.</summary>
        private static Value NarrowInt64Value(long value)
            => value >= int.MinValue && value <= int.MaxValue ? Value.FromInt((int)value) : Value.FromLong(value);

        // Overflow-safe integer arithmetic. The hot scalar paths and the coerced fallback share these
        // so that an int/long/ulong result that overflows its kind is promoted (int -> long ->
        // BigInteger) instead of silently wrapping. This keeps +, -, * precision-exact and consistent
        // with ** (IntegerExpo), rather than producing a wrapped two's-complement result.

        private static Value AddInt32(int a, int b) => NarrowInt64Value((long)a + b);

        private static Value SubInt32(int a, int b) => NarrowInt64Value((long)a - b);

        private static Value MulInt32(int a, int b) => NarrowInt64Value((long)a * b);

        private static Value AddInt64(long a, long b)
        {
            long r = unchecked(a + b);
            // Overflow iff the operands share a sign and the result's sign differs from both.
            return ((a ^ r) & (b ^ r)) < 0 ? NarrowIntegerValue((BigInteger)a + b) : Value.FromLong(r);
        }

        private static Value SubInt64(long a, long b)
        {
            long r = unchecked(a - b);
            // Overflow iff the operands differ in sign and the result's sign differs from the minuend.
            return ((a ^ b) & (a ^ r)) < 0 ? NarrowIntegerValue((BigInteger)a - b) : Value.FromLong(r);
        }

        private static Value MulInt64(long a, long b)
        {
            if (a == 0 || b == 0) return Value.FromLong(0L);
            // long.MinValue * -1 overflows and would also fault the r/a check below.
            if ((a == -1 && b == long.MinValue) || (b == -1 && a == long.MinValue))
                return NarrowIntegerValue((BigInteger)a * b);
            long r = unchecked(a * b);
            return r / a != b ? NarrowIntegerValue((BigInteger)a * b) : Value.FromLong(r);
        }

        private static Value AddUInt64(ulong a, ulong b)
        {
            ulong r = unchecked(a + b);
            return r < a ? Value.FromRef((BigInteger)a + b) : Value.FromRef(r);
        }

        private static Value SubUInt64(ulong a, ulong b)
            => a >= b ? Value.FromRef(a - b) : NarrowIntegerValue((BigInteger)a - b);

        private static Value MulUInt64(ulong a, ulong b)
        {
            if (a == 0UL || b == 0UL) return Value.FromRef(0UL);
            ulong r = unchecked(a * b);
            return r / a != b ? Value.FromRef((BigInteger)a * b) : Value.FromRef(r);
        }

        // Integer division/remainder. Callers guarantee a non-zero divisor. The only overflowing
        // case is MinValue / -1 (the quotient overflows the signed range); it is promoted to the
        // next-wider type, consistent with the +, -, * overflow handling. MinValue % -1 is
        // mathematically 0 but would fault the hardware divide, so it is short-circuited.

        private static Value DivInt32(int a, int b)
            => b == -1 && a == int.MinValue ? Value.FromLong(-(long)a) : Value.FromInt(a / b);

        private static Value DivInt64(long a, long b)
            => b == -1L && a == long.MinValue ? NarrowIntegerValue(-(BigInteger)a) : Value.FromLong(a / b);

        private static Value ModInt32(int a, int b)
            => b == -1 ? Value.FromInt(0) : Value.FromInt(a % b);

        private static Value ModInt64(long a, long b)
            => b == -1L ? Value.FromLong(0L) : Value.FromLong(a % b);

        private VMException DecimalOverflow(Instruction instr)
            => new("Runtime error: arithmetic overflow (result is outside the representable decimal range)",
                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

        /// <summary>
        /// Error for a null operand in an arithmetic/bitwise operator. Null no longer silently
        /// propagates (which masked bugs); it is a clean runtime error, consistent across operators.
        /// </summary>
        private VMException NullOperand(Instruction instr, string op)
            => new($"Runtime error: {op} on null is not allowed",
                instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

        private Value AddDecimal(decimal a, decimal b, Instruction instr)
        {
            try { return Value.FromRef(a + b); }
            catch (OverflowException) { throw DecimalOverflow(instr); }
        }

        private Value SubDecimal(decimal a, decimal b, Instruction instr)
        {
            try { return Value.FromRef(a - b); }
            catch (OverflowException) { throw DecimalOverflow(instr); }
        }

        private Value MulDecimal(decimal a, decimal b, Instruction instr)
        {
            try { return Value.FromRef(a * b); }
            catch (OverflowException) { throw DecimalOverflow(instr); }
        }

        /// <summary>
        /// Integer exponentiation that preserves full precision (promotes to BigInteger on overflow).
        /// A negative exponent yields a fractional result and falls back to floating point.
        /// </summary>
        private Value IntegerExpo(BigInteger baseVal, BigInteger exp, Instruction instr)
        {
            if (exp.Sign < 0)
                return Value.FromDouble(Math.Pow((double)baseVal, (double)exp));
            if (exp > int.MaxValue)
                throw new VMException("EXPO exponent too large", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            return NarrowIntegerValue(BigInteger.Pow(baseVal, (int)exp));
        }

        /// <summary>
        /// Decimal exponentiation via double, with an explicit range check so an out-of-range
        /// result raises a clean VM error instead of a native OverflowException on the cast.
        /// </summary>
        private Value DecimalPow(decimal a, decimal b, Instruction instr)
        {
            double result = Math.Pow((double)a, (double)b);
            if (double.IsNaN(result) || double.IsInfinity(result) ||
                result > (double)decimal.MaxValue || result < (double)decimal.MinValue)
                throw new VMException("EXPO result is outside the representable decimal range", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            return Value.FromRef((decimal)result);
        }

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
                case List<object> list: return GetListCount(list) != 0;
                case Dictionary<string, object> dict: return GetDictionaryCount(dict) != 0;
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

        private readonly struct HotNumericPair
        {
            public HotNumericPair(int left, int right)
            {
                Kind = NumKind.Int32;
                Int32Left = left;
                Int32Right = right;
                Int64Left = 0L;
                Int64Right = 0L;
                DoubleLeft = 0.0;
                DoubleRight = 0.0;
            }

            public HotNumericPair(long left, long right)
            {
                Kind = NumKind.Int64;
                Int32Left = 0;
                Int32Right = 0;
                Int64Left = left;
                Int64Right = right;
                DoubleLeft = 0.0;
                DoubleRight = 0.0;
            }

            public HotNumericPair(double left, double right)
            {
                Kind = NumKind.Double;
                Int32Left = 0;
                Int32Right = 0;
                Int64Left = 0L;
                Int64Right = 0L;
                DoubleLeft = left;
                DoubleRight = right;
            }

            public NumKind Kind { get; }

            public int Int32Left { get; }

            public int Int32Right { get; }

            public long Int64Left { get; }

            public long Int64Right { get; }

            public double DoubleLeft { get; }

            public double DoubleRight { get; }
        }

        // A char coerces to its Unicode code point uniformly ('5' -> 53, 'A' -> 65). Previously digit
        // chars were special-cased to their digit value, which made char arithmetic and equality
        // inconsistent ('5'+1 == 6 but 'A'+1 == 66). Explicit string/char->number conversions are
        // handled elsewhere and are unaffected.
        private static int CharToNumeric(char ch) => ch;

        private static bool ToBool(Value v)
            => v.Kind switch
            {
                ValueKind.Null => false,
                ValueKind.Bool => v.AsBool,
                ValueKind.Int => v.AsInt != 0,
                ValueKind.Long => v.AsLong != 0L,
                ValueKind.Double => v.AsDouble != 0.0 || double.IsNaN(v.AsDouble),
                ValueKind.Char => v.AsChar != '\0',
                _ => ToBool(v.Reference)
            };

        private static bool TryGetHotInt32(Value value, out int result)
        {
            switch (value.Kind)
            {
                case ValueKind.Int:
                    result = value.AsInt;
                    return true;
                case ValueKind.Char:
                    result = CharToNumeric(value.AsChar);
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool TryGetHotInt64(Value value, out long result)
        {
            switch (value.Kind)
            {
                case ValueKind.Int:
                    result = value.AsInt;
                    return true;
                case ValueKind.Long:
                    result = value.AsLong;
                    return true;
                case ValueKind.Char:
                    result = CharToNumeric(value.AsChar);
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool TryGetHotDouble(Value value, out double result)
        {
            switch (value.Kind)
            {
                case ValueKind.Int:
                    result = value.AsInt;
                    return true;
                case ValueKind.Long:
                    result = value.AsLong;
                    return true;
                case ValueKind.Double:
                    result = value.AsDouble;
                    return true;
                case ValueKind.Char:
                    result = CharToNumeric(value.AsChar);
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        private static bool TryCoerceHotNumericPair(Value left, Value right, out HotNumericPair pair)
        {
            if (left.Kind == ValueKind.Double || right.Kind == ValueKind.Double)
            {
                if (TryGetHotDouble(left, out double leftDouble) && TryGetHotDouble(right, out double rightDouble))
                {
                    pair = new HotNumericPair(leftDouble, rightDouble);
                    return true;
                }

                pair = default;
                return false;
            }

            if (left.Kind == ValueKind.Long || right.Kind == ValueKind.Long)
            {
                if (TryGetHotInt64(left, out long leftLong) && TryGetHotInt64(right, out long rightLong))
                {
                    pair = new HotNumericPair(leftLong, rightLong);
                    return true;
                }

                pair = default;
                return false;
            }

            if (TryGetHotInt32(left, out int leftInt) && TryGetHotInt32(right, out int rightInt))
            {
                pair = new HotNumericPair(leftInt, rightInt);
                return true;
            }

            pair = default;
            return false;
        }

        private static bool TryCoerceHotIntegralPair(Value left, Value right, out HotNumericPair pair)
        {
            if (left.Kind == ValueKind.Double || right.Kind == ValueKind.Double)
            {
                pair = default;
                return false;
            }

            if (left.Kind == ValueKind.Long || right.Kind == ValueKind.Long)
            {
                if (TryGetHotInt64(left, out long leftLong) && TryGetHotInt64(right, out long rightLong))
                {
                    pair = new HotNumericPair(leftLong, rightLong);
                    return true;
                }

                pair = default;
                return false;
            }

            if (TryGetHotInt32(left, out int leftInt) && TryGetHotInt32(right, out int rightInt))
            {
                pair = new HotNumericPair(leftInt, rightInt);
                return true;
            }

            pair = default;
            return false;
        }

        private static bool TryGetHotShiftCount(Value value, out int shiftCount)
        {
            switch (value.Kind)
            {
                case ValueKind.Int:
                    shiftCount = value.AsInt;
                    return true;
                case ValueKind.Long:
                    shiftCount = checked((int)value.AsLong);
                    return true;
                case ValueKind.Char:
                    shiftCount = CharToNumeric(value.AsChar);
                    return true;
                default:
                    shiftCount = default;
                    return false;
            }
        }

        private static bool IsSignedIntegral(Value value)
            => value.Kind switch
            {
                ValueKind.Int => true,
                ValueKind.Long => true,
                ValueKind.Ref => value.Reference is sbyte or short,
                _ => false
            };

        private static bool IsNegative(Value value)
            => value.Kind switch
            {
                ValueKind.Int => value.AsInt < 0,
                ValueKind.Long => value.AsLong < 0L,
                ValueKind.Ref => value.Reference switch
                {
                    sbyte v => v < 0,
                    short v => v < 0,
                    _ => false
                },
                _ => false
            };

        private static NumKind PromoteKind(Value a, Value b)
        {
            if ((a.Kind == ValueKind.Ref && a.Reference is decimal) ||
                (b.Kind == ValueKind.Ref && b.Reference is decimal))
                return NumKind.Decimal;
            if (a.Kind == ValueKind.Double ||
                b.Kind == ValueKind.Double ||
                (a.Kind == ValueKind.Ref && a.Reference is float) ||
                (b.Kind == ValueKind.Ref && b.Reference is float))
                return NumKind.Double;
            if ((a.Kind == ValueKind.Ref && a.Reference is BigInteger) ||
                (b.Kind == ValueKind.Ref && b.Reference is BigInteger))
                return NumKind.Maximal;

            bool aUWide = a.Kind == ValueKind.Ref && a.Reference is ulong;
            bool bUWide = b.Kind == ValueKind.Ref && b.Reference is ulong;
            if ((aUWide && IsSignedIntegral(b) && IsNegative(b)) ||
                (bUWide && IsSignedIntegral(a) && IsNegative(a)))
                return NumKind.Maximal;

            if ((a.Kind == ValueKind.Ref && a.Reference is ulong) ||
                (b.Kind == ValueKind.Ref && b.Reference is ulong))
                return NumKind.UInt64;
            if (a.Kind == ValueKind.Long || b.Kind == ValueKind.Long)
                return NumKind.Int64;
            return NumKind.Int32;
        }

        // Floating-point -> integer coercion truncates toward zero (the conventional cast semantic
        // used by most languages), rather than Convert.ToInt32's banker's rounding, while still
        // range-checking so an out-of-range magnitude raises a clean error instead of wrapping.
        private static int DoubleToInt32(double d)
        {
            double t = Math.Truncate(d);
            if (double.IsNaN(t) || t < int.MinValue || t > int.MaxValue)
                throw new OverflowException("Value was either too large or too small for an Int32.");
            return (int)t;
        }

        private static long DoubleToInt64(double d)
        {
            double t = Math.Truncate(d);
            if (double.IsNaN(t) || t < long.MinValue || t > long.MaxValue)
                throw new OverflowException("Value was either too large or too small for an Int64.");
            return (long)t;
        }

        private static ulong DoubleToUInt64(double d)
        {
            double t = Math.Truncate(d);
            if (double.IsNaN(t) || t < 0 || t > ulong.MaxValue)
                throw new OverflowException("Value was either too large or too small for a UInt64.");
            return (ulong)t;
        }

        private static ulong DecimalToUInt64(decimal value)
        {
            decimal truncated = decimal.Truncate(value);
            if (truncated < 0m || truncated > ulong.MaxValue)
                throw new OverflowException("Value was either too large or too small for a UInt64.");
            return (ulong)truncated;
        }

        private static int ToInt32Invariant(Value x) => x.Kind switch
        {
            ValueKind.Int => x.AsInt,
            ValueKind.Long => checked((int)x.AsLong),
            ValueKind.Double => DoubleToInt32(x.AsDouble),
            ValueKind.Char => CharToNumeric(x.AsChar),
            ValueKind.Ref => ToInt32Invariant(x.Reference!),
            _ => throw new InvalidOperationException($"Not numeric: {x.Kind}")
        };

        private static long ToInt64Invariant(Value x) => x.Kind switch
        {
            ValueKind.Int => x.AsInt,
            ValueKind.Long => x.AsLong,
            ValueKind.Double => DoubleToInt64(x.AsDouble),
            ValueKind.Char => CharToNumeric(x.AsChar),
            ValueKind.Ref => ToInt64Invariant(x.Reference!),
            _ => throw new InvalidOperationException($"Not numeric: {x.Kind}")
        };

        private static ulong ToUInt64Invariant(Value x) => x.Kind switch
        {
            ValueKind.Int => Convert.ToUInt64(x.AsInt),
            ValueKind.Long => Convert.ToUInt64(x.AsLong),
            ValueKind.Double => DoubleToUInt64(x.AsDouble),
            ValueKind.Char => Convert.ToUInt64(CharToNumeric(x.AsChar)),
            ValueKind.Ref => ToUInt64Invariant(x.Reference!),
            _ => throw new InvalidOperationException($"Not numeric: {x.Kind}")
        };

        private static double ToDoubleInvariant(Value x) => x.Kind switch
        {
            ValueKind.Int => x.AsInt,
            ValueKind.Long => x.AsLong,
            ValueKind.Double => x.AsDouble,
            ValueKind.Char => CharToNumeric(x.AsChar),
            ValueKind.Ref => ToDoubleInvariant(x.Reference!),
            _ => throw new InvalidOperationException($"Not numeric: {x.Kind}")
        };

        private static decimal ToDecimalInvariant(Value x) => x.Kind switch
        {
            ValueKind.Int => x.AsInt,
            ValueKind.Long => x.AsLong,
            ValueKind.Double => Convert.ToDecimal(x.AsDouble),
            ValueKind.Char => CharToNumeric(x.AsChar),
            ValueKind.Ref => ToDecimalInvariant(x.Reference!),
            _ => throw new InvalidOperationException($"Not numeric: {x.Kind}")
        };

        private static BigInteger ToBigIntegerInvariant(Value x) => x.Kind switch
        {
            ValueKind.Int => new BigInteger(x.AsInt),
            ValueKind.Long => new BigInteger(x.AsLong),
            ValueKind.Char => new BigInteger(CharToNumeric(x.AsChar)),
            ValueKind.Ref => ToBigIntegerInvariant(x.Reference!),
            ValueKind.Null => throw new InvalidOperationException("Not numeric: null"),
            ValueKind.Double => throw new InvalidOperationException("Cannot coerce floating-point to BigInteger without explicit rounding."),
            _ => throw new InvalidOperationException($"Not numeric: {x.Kind}")
        };

        private static BigInteger ToBigInt(Value x) => x.Kind switch
        {
            ValueKind.Int => new BigInteger(x.AsInt),
            ValueKind.Long => new BigInteger(x.AsLong),
            ValueKind.Char => new BigInteger(CharToNumeric(x.AsChar)),
            ValueKind.Ref => ToBigInt(x.Reference!),
            _ => throw new InvalidOperationException($"Not integral: {x.Kind}")
        };

        private int ToInt32ForRepeat(Value n)
        {
            BigInteger bi = n.Kind == ValueKind.Ref && n.Reference is BigInteger b ? b : ToBigInt(n);
            if (bi.Sign < 0) throw new VMException("repeat count must be non-negative", 0, 0, null, IsDebugging, DebugStream!);
            return checked((int)bi);
        }

        private static void CoerceFallbackNumericPair(
            Value leftValue,
            Value rightValue,
            out Value left,
            out Value right,
            out NumKind kind)
        {
            kind = PromoteKind(leftValue, rightValue);
            switch (kind)
            {
                case NumKind.Maximal:
                    left = Value.FromRef(ToBigIntegerInvariant(leftValue));
                    right = Value.FromRef(ToBigIntegerInvariant(rightValue));
                    return;
                case NumKind.Decimal:
                    left = Value.FromRef(ToDecimalInvariant(leftValue));
                    right = Value.FromRef(ToDecimalInvariant(rightValue));
                    return;
                case NumKind.Double:
                    left = Value.FromDouble(ToDoubleInvariant(leftValue));
                    right = Value.FromDouble(ToDoubleInvariant(rightValue));
                    return;
                case NumKind.UInt64:
                    try
                    {
                        left = Value.FromRef(ToUInt64Invariant(leftValue));
                        right = Value.FromRef(ToUInt64Invariant(rightValue));
                        return;
                    }
                    catch (OverflowException)
                    {
                        kind = NumKind.Maximal;
                        left = Value.FromRef(ToBigIntegerInvariant(leftValue));
                        right = Value.FromRef(ToBigIntegerInvariant(rightValue));
                        return;
                    }
                case NumKind.Int64:
                    try
                    {
                        left = Value.FromLong(ToInt64Invariant(leftValue));
                        right = Value.FromLong(ToInt64Invariant(rightValue));
                        return;
                    }
                    catch (OverflowException)
                    {
                        kind = NumKind.Maximal;
                        left = Value.FromRef(ToBigIntegerInvariant(leftValue));
                        right = Value.FromRef(ToBigIntegerInvariant(rightValue));
                        return;
                    }
                default:
                    try
                    {
                        left = Value.FromInt(ToInt32Invariant(leftValue));
                        right = Value.FromInt(ToInt32Invariant(rightValue));
                        kind = NumKind.Int32;
                        return;
                    }
                    catch (OverflowException)
                    {
                        try
                        {
                            left = Value.FromLong(ToInt64Invariant(leftValue));
                            right = Value.FromLong(ToInt64Invariant(rightValue));
                            kind = NumKind.Int64;
                            return;
                        }
                        catch (OverflowException)
                        {
                            kind = NumKind.Maximal;
                            left = Value.FromRef(ToBigIntegerInvariant(leftValue));
                            right = Value.FromRef(ToBigIntegerInvariant(rightValue));
                            return;
                        }
                    }
            }
        }

        private static bool TryGetFallbackNumericValue(Value value, out Value number)
        {
            switch (value.Kind)
            {
                case ValueKind.Int:
                case ValueKind.Long:
                case ValueKind.Double:
                    number = value;
                    return true;
                case ValueKind.Char:
                    number = Value.FromInt(CharToNumeric(value.AsChar));
                    return true;
                case ValueKind.Ref when value.Reference is not null && IsNumber(value.Reference):
                    number = value;
                    return true;
                default:
                    number = default;
                    return false;
            }
        }

        private static bool TryGetFallbackIntegralValue(Value value, out Value number)
        {
            switch (value.Kind)
            {
                case ValueKind.Int:
                case ValueKind.Long:
                    number = value;
                    return true;
                case ValueKind.Char:
                    number = Value.FromInt(CharToNumeric(value.AsChar));
                    return true;
                case ValueKind.Ref when value.Reference is not null && IsIntegralLike(value.Reference):
                    number = value;
                    return true;
                default:
                    number = default;
                    return false;
            }
        }

        private static bool TryGetStringReference(Value value, out string? text)
        {
            if (value.Kind == ValueKind.Ref && value.Reference is string s)
            {
                text = s;
                return true;
            }

            text = null;
            return false;
        }

        private static bool TryCoerceFallbackNumericPair(
            Value leftValue,
            Value rightValue,
            out Value left,
            out Value right,
            out NumKind kind)
        {
            if (TryGetFallbackNumericValue(leftValue, out Value l) && TryGetFallbackNumericValue(rightValue, out Value r))
            {
                CoerceFallbackNumericPair(l, r, out left, out right, out kind);
                return true;
            }

            left = default;
            right = default;
            kind = default;
            return false;
        }

        private static bool TryCoerceFallbackIntegralPair(
            Value leftValue,
            Value rightValue,
            out Value left,
            out Value right,
            out NumKind kind)
        {
            if (TryGetFallbackIntegralValue(leftValue, out Value l) && TryGetFallbackIntegralValue(rightValue, out Value r))
            {
                CoerceFallbackNumericPair(l, r, out left, out right, out kind);
                return true;
            }

            left = default;
            right = default;
            kind = default;
            return false;
        }

        private static bool IsCollectionReference(Value value)
            => value.Kind == ValueKind.Ref &&
               (value.Reference is List<object> || value.Reference is Dictionary<string, object>);

        private string FormatDefaultConcatOperand(Value value)
        {
            // Must match the canonical rendering used by Value.ToString()/PrintValue for non-null
            // scalars: lowercase boolean literals and culture-invariant numerics, so that string
            // concatenation does not leak the .NET "True"/"False" capitalization or a
            // locale-dependent decimal separator (e.g. "1,5" instead of "1.5"). Null is treated
            // like C# string concatenation and contributes an empty string.
            return value.Kind switch
            {
                ValueKind.Null => string.Empty,
                ValueKind.Bool => value.AsBool ? "true" : "false",
                ValueKind.Int => value.AsInt.ToString(CultureInfo.InvariantCulture),
                ValueKind.Long => value.AsLong.ToString(CultureInfo.InvariantCulture),
                ValueKind.Double => value.AsDouble.ToString(CultureInfo.InvariantCulture),
                ValueKind.Char => value.AsChar.ToString(),
                _ => value.Reference switch
                {
                    null => string.Empty,
                    StaticInstance staticInstance => staticInstance.ClassName,
                    BoundType boundType => boundType.Type.ClassName,
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    object reference => reference.ToString() ?? string.Empty
                }
            };
        }

        private string FormatPrintedConcatOperand(Value value)
        {
            using StringWriter writer = new();
            PrintValue(value, writer);
            return writer.ToString();
        }

        private StepResult HandleAddInstruction(Instruction instr)
        {
            RequireStack(2, instr, "ADD");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();

            if (TryCoerceHotNumericPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                switch (hotPair.Kind)
                {
                    case NumKind.Int32:
                        _valueStack.Push(AddInt32(hotPair.Int32Left, hotPair.Int32Right));
                        return StepResult.Next;
                    case NumKind.Int64:
                        _valueStack.Push(AddInt64(hotPair.Int64Left, hotPair.Int64Right));
                        return StepResult.Next;
                    case NumKind.Double:
                        _valueStack.Push(Value.FromDouble(hotPair.DoubleLeft + hotPair.DoubleRight));
                        return StepResult.Next;
                }
            }

            if (TryCoerceFallbackNumericPair(leftValue, rightValue, out Value A, out Value B, out NumKind K))
            {
                Value res = K switch
                {
                    NumKind.Decimal => AddDecimal((decimal)A.Reference!, (decimal)B.Reference!, instr),
                    NumKind.Double => Value.FromDouble(A.AsDouble + B.AsDouble),
                    NumKind.UInt64 => AddUInt64((ulong)A.Reference!, (ulong)B.Reference!),
                    NumKind.Int64 => AddInt64(A.AsLong, B.AsLong),
                    NumKind.Maximal => Value.FromRef((BigInteger)A.Reference! + (BigInteger)B.Reference!),
                    _ => AddInt32(A.AsInt, B.AsInt),
                };
                _valueStack.Push(res);
                return StepResult.Next;
            }

            // Beyond numerics, '+' is only a string-concatenation operator: it requires at least one
            // string operand. The other operand is stringified (collections via their printed form),
            // except null which contributes an empty string. This keeps '+' consistent with '-', '*',
            // '%' which reject non-numeric/non-string operands, instead of silently turning e.g.
            // true + false into "truefalse" or null + 5 into null.
            bool leftIsString = TryGetStringReference(leftValue, out _);
            bool rightIsString = TryGetStringReference(rightValue, out _);

            if (leftIsString || rightIsString)
            {
                if (IsCollectionReference(leftValue) || IsCollectionReference(rightValue))
                    _valueStack.Push(Value.FromObject(FormatPrintedConcatOperand(leftValue) + FormatPrintedConcatOperand(rightValue)));
                else
                    _valueStack.Push(Value.FromObject(FormatDefaultConcatOperand(leftValue) + FormatDefaultConcatOperand(rightValue)));
                return StepResult.Next;
            }

            // Array + Array concatenates into a new array (left elements then right). The result is a
            // fresh array and the operands are never mutated. Dictionaries are NOT merged with '+';
            // use the dedicated '<<<' / '>>>' merge operators for that.
            if (leftValue.Reference is List<object> leftList && rightValue.Reference is List<object> rightList)
            {
                List<object> mergedList = new(leftList.Count + rightList.Count);
                mergedList.AddRange(leftList);
                mergedList.AddRange(rightList);
                _valueStack.Push(Value.FromRef(mergedList));
                return StepResult.Next;
            }

            if (leftValue.IsNull || rightValue.IsNull)
                throw NullOperand(instr, "ADD");

            throw new VMException("ADD on incompatible types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private StepResult HandleSubInstruction(Instruction instr)
        {
            RequireStack(2, instr, "SUB");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();

            if (TryCoerceHotNumericPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                switch (hotPair.Kind)
                {
                    case NumKind.Int32:
                        _valueStack.Push(SubInt32(hotPair.Int32Left, hotPair.Int32Right));
                        return StepResult.Next;
                    case NumKind.Int64:
                        _valueStack.Push(SubInt64(hotPair.Int64Left, hotPair.Int64Right));
                        return StepResult.Next;
                    case NumKind.Double:
                        _valueStack.Push(Value.FromDouble(hotPair.DoubleLeft - hotPair.DoubleRight));
                        return StepResult.Next;
                }
            }

            if (leftValue.IsNull || rightValue.IsNull)
                throw NullOperand(instr, "SUB");

            if (!TryCoerceFallbackNumericPair(leftValue, rightValue, out Value A, out Value B, out NumKind K))
                throw new VMException("SUB on non-numeric types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            Value res = K switch
            {
                NumKind.Decimal => SubDecimal((decimal)A.Reference!, (decimal)B.Reference!, instr),
                NumKind.Double => Value.FromDouble(A.AsDouble - B.AsDouble),
                NumKind.UInt64 => SubUInt64((ulong)A.Reference!, (ulong)B.Reference!),
                NumKind.Int64 => SubInt64(A.AsLong, B.AsLong),
                NumKind.Maximal => Value.FromRef((BigInteger)A.Reference! - (BigInteger)B.Reference!),
                _ => SubInt32(A.AsInt, B.AsInt),
            };
            _valueStack.Push(res);
            return StepResult.Next;
        }

        private StepResult HandleMulInstruction(Instruction instr)
        {
            RequireStack(2, instr, "MUL");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();

            if (TryCoerceHotNumericPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                switch (hotPair.Kind)
                {
                    case NumKind.Int32:
                        _valueStack.Push(MulInt32(hotPair.Int32Left, hotPair.Int32Right));
                        return StepResult.Next;
                    case NumKind.Int64:
                        _valueStack.Push(MulInt64(hotPair.Int64Left, hotPair.Int64Right));
                        return StepResult.Next;
                    case NumKind.Double:
                        _valueStack.Push(Value.FromDouble(hotPair.DoubleLeft * hotPair.DoubleRight));
                        return StepResult.Next;
                }
            }

            if (leftValue.IsNull || rightValue.IsNull)
                throw NullOperand(instr, "MUL");

            if (TryCoerceFallbackNumericPair(leftValue, rightValue, out Value A, out Value B, out NumKind K))
            {
                Value res = K switch
                {
                    NumKind.Decimal => MulDecimal((decimal)A.Reference!, (decimal)B.Reference!, instr),
                    NumKind.Double => Value.FromDouble(A.AsDouble * B.AsDouble),
                    NumKind.UInt64 => MulUInt64((ulong)A.Reference!, (ulong)B.Reference!),
                    NumKind.Int64 => MulInt64(A.AsLong, B.AsLong),
                    NumKind.Maximal => Value.FromRef((BigInteger)A.Reference! * (BigInteger)B.Reference!),
                    _ => MulInt32(A.AsInt, B.AsInt),
                };
                _valueStack.Push(res);
                return StepResult.Next;
            }

            if (TryGetStringReference(leftValue, out string? leftString) && TryGetFallbackNumericValue(rightValue, out Value repeatRight))
            {
                _valueStack.Push(Value.FromObject(string.Concat(Enumerable.Repeat(leftString ?? string.Empty, ToInt32ForRepeat(repeatRight)))));
                return StepResult.Next;
            }

            if (TryGetStringReference(rightValue, out string? rightString) && TryGetFallbackNumericValue(leftValue, out Value repeatLeft))
            {
                _valueStack.Push(Value.FromObject(string.Concat(Enumerable.Repeat(rightString ?? string.Empty, ToInt32ForRepeat(repeatLeft)))));
                return StepResult.Next;
            }

            throw new VMException("MUL on non-numeric types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private StepResult HandleModInstruction(Instruction instr)
        {
            RequireStack(2, instr, "MOD");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();

            if (TryCoerceHotNumericPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                switch (hotPair.Kind)
                {
                    case NumKind.Int32:
                        if (hotPair.Int32Right == 0)
                            throw new VMException("division by zero in MOD", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        _valueStack.Push(ModInt32(hotPair.Int32Left, hotPair.Int32Right));
                        return StepResult.Next;
                    case NumKind.Int64:
                        if (hotPair.Int64Right == 0L)
                            throw new VMException("division by zero in MOD", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        _valueStack.Push(ModInt64(hotPair.Int64Left, hotPair.Int64Right));
                        return StepResult.Next;
                    case NumKind.Double:
                        if (hotPair.DoubleRight == 0.0)
                            throw new VMException("division by zero in MOD", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        _valueStack.Push(Value.FromDouble(hotPair.DoubleLeft % hotPair.DoubleRight));
                        return StepResult.Next;
                }
            }

            if (leftValue.IsNull || rightValue.IsNull)
                throw NullOperand(instr, "MOD");

            if (!TryCoerceFallbackNumericPair(leftValue, rightValue, out Value A, out Value B, out NumKind K))
                throw new VMException("MOD on non-numeric types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            bool isZero = K switch
            {
                NumKind.Int32 => B.AsInt == 0,
                NumKind.Int64 => B.AsLong == 0L,
                NumKind.UInt64 => (ulong)B.Reference! == 0UL,
                NumKind.Decimal => (decimal)B.Reference! == 0m,
                NumKind.Double => B.AsDouble == 0.0,
                NumKind.Maximal => ((BigInteger)B.Reference!).IsZero,
                _ => false
            };
            if (isZero)
                throw new VMException("division by zero in MOD", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            Value res = K switch
            {
                NumKind.Decimal => Value.FromRef((decimal)A.Reference! % (decimal)B.Reference!),
                NumKind.Double => Value.FromDouble(A.AsDouble % B.AsDouble),
                NumKind.UInt64 => Value.FromRef((ulong)A.Reference! % (ulong)B.Reference!),
                NumKind.Int64 => ModInt64(A.AsLong, B.AsLong),
                NumKind.Maximal => Value.FromRef((BigInteger)A.Reference! % (BigInteger)B.Reference!),
                _ => ModInt32(A.AsInt, B.AsInt),
            };
            _valueStack.Push(res);
            return StepResult.Next;
        }

        private StepResult HandleDivInstruction(Instruction instr)
        {
            RequireStack(2, instr, "DIV");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();

            if (TryCoerceHotNumericPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                switch (hotPair.Kind)
                {
                    case NumKind.Int32:
                        if (hotPair.Int32Right == 0)
                            throw new VMException("division by zero", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        _valueStack.Push(DivInt32(hotPair.Int32Left, hotPair.Int32Right));
                        return StepResult.Next;
                    case NumKind.Int64:
                        if (hotPair.Int64Right == 0L)
                            throw new VMException("division by zero", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        _valueStack.Push(DivInt64(hotPair.Int64Left, hotPair.Int64Right));
                        return StepResult.Next;
                    case NumKind.Double:
                        if (hotPair.DoubleRight == 0.0)
                            throw new VMException("division by zero", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
                        _valueStack.Push(Value.FromDouble(hotPair.DoubleLeft / hotPair.DoubleRight));
                        return StepResult.Next;
                }
            }

            if (leftValue.IsNull || rightValue.IsNull)
                throw NullOperand(instr, "DIV");

            if (!TryCoerceFallbackNumericPair(leftValue, rightValue, out Value A, out Value B, out NumKind K))
                throw new VMException(
                    $"Runtime error: cannot DIV {(leftValue.Kind == ValueKind.Ref ? leftValue.Reference?.GetType() : leftValue.Kind)} and {(rightValue.Kind == ValueKind.Ref ? rightValue.Reference?.GetType() : rightValue.Kind)}",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            bool isZero = K switch
            {
                NumKind.Int32 => B.AsInt == 0,
                NumKind.Int64 => B.AsLong == 0L,
                NumKind.UInt64 => (ulong)B.Reference! == 0UL,
                NumKind.Decimal => (decimal)B.Reference! == 0m,
                NumKind.Double => B.AsDouble == 0.0,
                NumKind.Maximal => ((BigInteger)B.Reference!).IsZero,
                _ => false
            };
            if (isZero)
                throw new VMException("division by zero", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            Value res = K switch
            {
                NumKind.Decimal => Value.FromRef((decimal)A.Reference! / (decimal)B.Reference!),
                NumKind.Double => Value.FromDouble(A.AsDouble / B.AsDouble),
                NumKind.UInt64 => Value.FromRef((ulong)A.Reference! / (ulong)B.Reference!),
                NumKind.Int64 => DivInt64(A.AsLong, B.AsLong),
                NumKind.Maximal => Value.FromRef((BigInteger)A.Reference! / (BigInteger)B.Reference!),
                _ => DivInt32(A.AsInt, B.AsInt),
            };
            _valueStack.Push(res);
            return StepResult.Next;
        }

        private StepResult HandleExpoInstruction(Instruction instr)
        {
            RequireStack(2, instr, "EXPO");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();

            if (TryCoerceHotNumericPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                switch (hotPair.Kind)
                {
                    case NumKind.Double:
                        _valueStack.Push(Value.FromDouble(Math.Pow(hotPair.DoubleLeft, hotPair.DoubleRight)));
                        return StepResult.Next;
                    case NumKind.Int64:
                        _valueStack.Push(IntegerExpo(hotPair.Int64Left, hotPair.Int64Right, instr));
                        return StepResult.Next;
                    case NumKind.Int32:
                        _valueStack.Push(IntegerExpo(hotPair.Int32Left, hotPair.Int32Right, instr));
                        return StepResult.Next;
                }
            }

            if (leftValue.IsNull || rightValue.IsNull)
                throw NullOperand(instr, "EXPO");

            if (!TryCoerceFallbackNumericPair(leftValue, rightValue, out Value A, out Value B, out NumKind K))
                throw new VMException("EXPO on non-numeric types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);

            Value res = K switch
            {
                NumKind.Decimal => DecimalPow((decimal)A.Reference!, (decimal)B.Reference!, instr),
                NumKind.Double => Value.FromDouble(Math.Pow(A.AsDouble, B.AsDouble)),
                NumKind.UInt64 => IntegerExpo((ulong)A.Reference!, (ulong)B.Reference!, instr),
                NumKind.Int64 => IntegerExpo(A.AsLong, B.AsLong, instr),
                NumKind.Maximal => PowBigInteger((BigInteger)A.Reference!, (BigInteger)B.Reference!, instr),
                _ => IntegerExpo(A.AsInt, B.AsInt, instr),
            };
            _valueStack.Push(res);
            return StepResult.Next;
        }

        private StepResult HandleBitAndInstruction(Instruction instr)
        {
            RequireStack(2, instr, "BIT_AND");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();

            if (TryCoerceHotIntegralPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                if (hotPair.Kind == NumKind.Int64)
                    _valueStack.Push(Value.FromLong(hotPair.Int64Left & hotPair.Int64Right));
                else
                    _valueStack.Push(Value.FromInt(hotPair.Int32Left & hotPair.Int32Right));
                return StepResult.Next;
            }

            if (leftValue.IsNull || rightValue.IsNull) throw NullOperand(instr, "BIT_AND");
            if (!TryCoerceFallbackIntegralPair(leftValue, rightValue, out Value l, out Value r, out NumKind kind))
                throw new VMException("BIT_AND requires integral types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            Value result = kind switch
            {
                NumKind.Maximal => Value.FromRef((BigInteger)l.Reference! & (BigInteger)r.Reference!),
                NumKind.UInt64 => Value.FromRef((ulong)l.Reference! & (ulong)r.Reference!),
                NumKind.Int64 => Value.FromLong(l.AsLong & r.AsLong),
                _ => Value.FromInt(l.AsInt & r.AsInt)
            };
            _valueStack.Push(result);
            return StepResult.Next;
        }

        private StepResult HandleBitOrInstruction(Instruction instr)
        {
            RequireStack(2, instr, "BIT_OR");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();

            if (TryCoerceHotIntegralPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                if (hotPair.Kind == NumKind.Int64)
                    _valueStack.Push(Value.FromLong(hotPair.Int64Left | hotPair.Int64Right));
                else
                    _valueStack.Push(Value.FromInt(hotPair.Int32Left | hotPair.Int32Right));
                return StepResult.Next;
            }

            if (leftValue.IsNull || rightValue.IsNull) throw NullOperand(instr, "BIT_OR");
            if (!TryCoerceFallbackIntegralPair(leftValue, rightValue, out Value l, out Value r, out NumKind kind))
                throw new VMException("BIT_OR requires integral types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            Value result = kind switch
            {
                NumKind.Maximal => Value.FromRef((BigInteger)l.Reference! | (BigInteger)r.Reference!),
                NumKind.UInt64 => Value.FromRef((ulong)l.Reference! | (ulong)r.Reference!),
                NumKind.Int64 => Value.FromLong(l.AsLong | r.AsLong),
                _ => Value.FromInt(l.AsInt | r.AsInt)
            };
            _valueStack.Push(result);
            return StepResult.Next;
        }

        private StepResult HandleBitXorInstruction(Instruction instr)
        {
            RequireStack(2, instr, "BIT_XOR");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();

            if (TryCoerceHotIntegralPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                if (hotPair.Kind == NumKind.Int64)
                    _valueStack.Push(Value.FromLong(hotPair.Int64Left ^ hotPair.Int64Right));
                else
                    _valueStack.Push(Value.FromInt(hotPair.Int32Left ^ hotPair.Int32Right));
                return StepResult.Next;
            }

            if (leftValue.IsNull || rightValue.IsNull) throw NullOperand(instr, "BIT_XOR");
            if (!TryCoerceFallbackIntegralPair(leftValue, rightValue, out Value l, out Value r, out NumKind kind))
                throw new VMException("BIT_XOR requires integral types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            Value result = kind switch
            {
                NumKind.Maximal => Value.FromRef((BigInteger)l.Reference! ^ (BigInteger)r.Reference!),
                NumKind.UInt64 => Value.FromRef((ulong)l.Reference! ^ (ulong)r.Reference!),
                NumKind.Int64 => Value.FromLong(l.AsLong ^ r.AsLong),
                _ => Value.FromInt(l.AsInt ^ r.AsInt)
            };
            _valueStack.Push(result);
            return StepResult.Next;
        }

        private StepResult HandleShiftLeftInstruction(Instruction instr)
        {
            RequireStack(2, instr, "SHL");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();

            // Fast path: an int/char left operand with a small non-negative count shifts inside a
            // long without overflow; NarrowInt64Value promotes to long only if needed.
            if (TryGetHotShiftCount(rightValue, out int hotShift) &&
                hotShift is >= 0 and <= 31 &&
                (leftValue.Kind == ValueKind.Int || leftValue.Kind == ValueKind.Char))
            {
                int leftInt = leftValue.Kind == ValueKind.Int ? leftValue.AsInt : CharToNumeric(leftValue.AsChar);
                _valueStack.Push(NarrowInt64Value((long)leftInt << hotShift));
                return StepResult.Next;
            }

            if (leftValue.IsNull || rightValue.IsNull) throw NullOperand(instr, "SHL");
            if (!(TryGetFallbackIntegralValue(leftValue, out Value l) && TryGetFallbackNumericValue(rightValue, out Value r)))
                throw new VMException("SHL requires (integral) << int", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            int sh = ToInt32Invariant(r);
            if (sh < 0)
                throw new VMException("SHL requires a non-negative shift count", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            // Shift through BigInteger so the count is never masked (& 0x1F/0x3F) and the value never
            // wraps; NarrowIntegerValue stores the narrowest of int/long/BigInteger. Consistent with
            // +, -, *, ** which all auto-promote on overflow.
            _valueStack.Push(NarrowIntegerValue(ToBigIntegerInvariant(l.ToObject()!) << sh));
            return StepResult.Next;
        }

        private StepResult HandleShiftRightInstruction(Instruction instr)
        {
            RequireStack(2, instr, "SHR");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();

            // Fast path: right shift never grows the value. Native shifts are exact while the count
            // stays within the type's bit width; beyond that the BigInteger path yields the correct
            // 0 (non-negative) or -1 (negative arithmetic shift).
            if (TryGetHotShiftCount(rightValue, out int hotShift) && hotShift >= 0)
            {
                if ((leftValue.Kind == ValueKind.Int || leftValue.Kind == ValueKind.Char) && hotShift <= 31)
                {
                    int leftInt = leftValue.Kind == ValueKind.Int ? leftValue.AsInt : CharToNumeric(leftValue.AsChar);
                    _valueStack.Push(Value.FromInt(leftInt >> hotShift));
                    return StepResult.Next;
                }

                if (leftValue.Kind == ValueKind.Long && hotShift <= 63)
                {
                    _valueStack.Push(NarrowInt64Value(leftValue.AsLong >> hotShift));
                    return StepResult.Next;
                }
            }

            if (leftValue.IsNull || rightValue.IsNull) throw NullOperand(instr, "SHR");
            if (!(TryGetFallbackIntegralValue(leftValue, out Value l) && TryGetFallbackNumericValue(rightValue, out Value r)))
                throw new VMException("SHR requires (integral) >> int", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            int sh = ToInt32Invariant(r);
            if (sh < 0)
                throw new VMException("SHR requires a non-negative shift count", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            // Shift through BigInteger so the count is never masked; large counts collapse to the
            // correct 0/-1 instead of `>> (sh & 0x1F)`. NarrowIntegerValue stores the narrowest kind.
            _valueStack.Push(NarrowIntegerValue(ToBigIntegerInvariant(l.ToObject()!) >> sh));
            return StepResult.Next;
        }

        /// <summary>
        /// Dictionary merge operators '&lt;&lt;&lt;' (left wins) and '&gt;&gt;&gt;' (right wins). Both operands
        /// must be dictionaries; the result is a new dictionary (operands are never mutated) that keeps
        /// the left operand's key order, with new keys from the right appended. On a key collision the
        /// winning side (per <paramref name="rightWins"/>) supplies the value.
        /// </summary>
        private StepResult HandleMergeInstruction(Instruction instr, bool rightWins)
        {
            string op = rightWins ? ">>>" : "<<<";
            RequireStack(2, instr, op);
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();

            if (leftValue.Reference is not Dictionary<string, object> leftDict ||
                rightValue.Reference is not Dictionary<string, object> rightDict)
            {
                throw new VMException(
                    $"Runtime error: '{op}' requires two dictionaries",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }

            Dictionary<string, object> merged = new(leftDict, leftDict.Comparer);
            foreach (KeyValuePair<string, object> entry in rightDict)
            {
                if (rightWins || !merged.ContainsKey(entry.Key))
                    merged[entry.Key] = entry.Value;
            }

            _valueStack.Push(Value.FromRef(merged));
            return StepResult.Next;
        }

        private static bool NumericOrValueEquals(Value left, Value right)
        {
            if (TryCoerceHotNumericPair(left, right, out HotNumericPair hotPair))
            {
                return hotPair.Kind switch
                {
                    NumKind.Int64 => hotPair.Int64Left == hotPair.Int64Right,
                    NumKind.Double => hotPair.DoubleLeft == hotPair.DoubleRight,
                    _ => hotPair.Int32Left == hotPair.Int32Right,
                };
            }

            if (TryEqualDecimalBigIntegerPair(left, right, out bool decimalBigIntegerEquals))
                return decimalBigIntegerEquals;

            if (TryEqualDecimalFloatingSpecialPair(left, right, out bool decimalFloatingEquals))
                return decimalFloatingEquals;

            // Alle numerischen Cross-Kind-Faelle sind oben bereits abgehandelt; hier bleiben nur
            // gleichartige Nicht-Zahl-Skalare (Bool, Char, Null) uebrig, fuer die die strukturelle
            // Bit-Gleichheit GENAU der Sprach-Gleichheit entspricht. Der Aufruf ist daher bewusst.
            if (left.Kind != ValueKind.Ref && right.Kind != ValueKind.Ref)
                return left.Equals(right);

            if (TryCoerceFallbackNumericPair(left, right, out Value A, out Value B, out NumKind K))
            {
                return K switch
                {
                    NumKind.Decimal => (decimal)A.Reference! == (decimal)B.Reference!,
                    NumKind.Double => A.AsDouble == B.AsDouble,
                    NumKind.UInt64 => (ulong)A.Reference! == (ulong)B.Reference!,
                    NumKind.Int64 => A.AsLong == B.AsLong,
                    NumKind.Maximal => (BigInteger)A.Reference! == (BigInteger)B.Reference!,
                    _ => A.AsInt == B.AsInt,
                };
            }

            if (left.Kind == ValueKind.Ref && right.Kind == ValueKind.Ref)
            {
                object? l = left.Reference;
                object? r = right.Reference;
                if (l is string ls && r is string rs)
                    return string.Equals(ls, rs, StringComparison.Ordinal);
                return Equals(l, r);
            }

            return false;
        }

        private static bool IsNumericObject(object? x)
            => x is not null && (IsIntegralLike(x) || x is float or double or decimal);

        private static int CategoryRank(object? x) => x switch
        {
            null => 0,
            _ when IsNumericObject(x) => 1,
            bool => 2,
            string => 3,
            _ => 4
        };

        private static string FormatComparisonFallbackValue(object? value)
        {
            using StringWriter writer = new();
            PrintValue(value, writer);
            return writer.ToString();
        }

        /// <summary>
        /// Numeric-aware ordering of two coerced values, reusing the exact same coercion the
        /// &lt; / &gt; operators use. Returns false when the pair is not numeric.
        /// </summary>
        private static bool TryCompareNumericValues(Value left, Value right, out int cmp)
        {
            if (TryCoerceHotNumericPair(left, right, out HotNumericPair hotPair))
            {
                cmp = hotPair.Kind switch
                {
                    NumKind.Int64 => hotPair.Int64Left.CompareTo(hotPair.Int64Right),
                    NumKind.Double => hotPair.DoubleLeft.CompareTo(hotPair.DoubleRight),
                    _ => hotPair.Int32Left.CompareTo(hotPair.Int32Right),
                };
                return true;
            }

            if (TryCompareDecimalBigIntegerPair(left, right, out cmp))
                return true;

            if (TryCompareDecimalFloatingSpecialPair(left, right, out cmp))
                return true;

            if (TryCoerceFallbackNumericPair(left, right, out Value A, out Value B, out NumKind K))
            {
                cmp = K switch
                {
                    NumKind.Decimal => ((decimal)A.Reference!).CompareTo((decimal)B.Reference!),
                    NumKind.Double => A.AsDouble.CompareTo(B.AsDouble),
                    NumKind.UInt64 => ((ulong)A.Reference!).CompareTo((ulong)B.Reference!),
                    NumKind.Int64 => A.AsLong.CompareTo(B.AsLong),
                    NumKind.Maximal => ((BigInteger)A.Reference!).CompareTo((BigInteger)B.Reference!),
                    _ => A.AsInt.CompareTo(B.AsInt),
                };
                return true;
            }

            cmp = 0;
            return false;
        }

        /// <summary>
        /// Total order over runtime values for collection intrinsics (sort, min, max). Numeric values
        /// compare by value across kinds (int/long/BigInteger/double/decimal), consistent with
        /// &lt;/&gt;/==; mixed categories use a stable rank so List.Sort never sees an inconsistent
        /// comparer or a CompareTo that throws on differing CLR types. Never throws.
        /// </summary>
        public static int CompareRuntimeValues(object? x, object? y)
        {
            if (x is null && y is null) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            if (IsNumericObject(x) && IsNumericObject(y)
                && TryCompareNumericValues(Value.FromObject(x), Value.FromObject(y), out int ncmp))
                return ncmp;

            int rx = CategoryRank(x), ry = CategoryRank(y);
            if (rx != ry) return rx.CompareTo(ry);

            if (x is string sx && y is string sy) return string.CompareOrdinal(sx, sy);
            if (x is bool bx && y is bool by) return bx.CompareTo(by);
            return string.CompareOrdinal(FormatComparisonFallbackValue(x), FormatComparisonFallbackValue(y));
        }

        /// <summary>
        /// Numeric-aware equality for collection intrinsics (indexOf, includes), matching the ==
        /// operator so values that are numerically equal across CLR kinds (e.g. long 1 and int 1)
        /// compare equal.
        /// </summary>
        public static bool RuntimeValuesEqual(object? x, object? y)
            => NumericOrValueEquals(Value.FromObject(x), Value.FromObject(y));

        private StepResult HandleEqualInstruction(Instruction instr)
        {
            RequireStack(2, instr, "EQ");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();
            _valueStack.Push(Value.FromBool(NumericOrValueEquals(leftValue, rightValue)));
            return StepResult.Next;
        }

        private StepResult HandleNotEqualInstruction(Instruction instr)
        {
            RequireStack(2, instr, "NEQ");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();
            _valueStack.Push(Value.FromBool(!NumericOrValueEquals(leftValue, rightValue)));
            return StepResult.Next;
        }

        private StepResult HandleIsTypeInstruction(Instruction instr)
        {
            RequireStack(2, instr, "IS_TYPE");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();
            if (leftValue.Reference is not ClassInstance ci || rightValue.Reference is not StaticInstance target)
            {
                _valueStack.Push(Value.FromBool(false));
                return StepResult.Next;
            }

            bool found = false;
            if (TryGetStaticType(ci, out StaticInstance instType))
            {
                // `is` is a nominal check: an interface target matches only when the class declares it
                // (directly, through a base class, or through interface inheritance). A value that merely
                // has a compatible shape is not an `is` match; use the implements_shape builtin for that.
                found = IsInterfaceType(target)
                    ? ImplementsInterface(instType, target)
                    : IsSameOrDerivedStaticType(instType, target);
            }
            _valueStack.Push(Value.FromBool(found));
            return StepResult.Next;
        }

        private StepResult HandleLessThanInstruction(Instruction instr)
        {
            RequireStack(2, instr, "LT");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();
            if (TryCoerceHotNumericPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                bool comparison = hotPair.Kind switch
                {
                    NumKind.Int64 => hotPair.Int64Left < hotPair.Int64Right,
                    NumKind.Double => hotPair.DoubleLeft < hotPair.DoubleRight,
                    _ => hotPair.Int32Left < hotPair.Int32Right,
                };
                _valueStack.Push(Value.FromBool(comparison));
                return StepResult.Next;
            }

            if (leftValue.IsNull || rightValue.IsNull) { _valueStack.Push(Value.Null); return StepResult.Next; }
            if (TryCompareDecimalBigIntegerPair(leftValue, rightValue, out int decimalBigIntegerComparison))
            {
                _valueStack.Push(Value.FromBool(decimalBigIntegerComparison < 0));
                return StepResult.Next;
            }
            if (TryGetDecimalFloatingSpecialPair(leftValue, rightValue, out double leftSpecialDouble, out double rightSpecialDouble))
            {
                _valueStack.Push(Value.FromBool(leftSpecialDouble < rightSpecialDouble));
                return StepResult.Next;
            }
            if (TryCoerceFallbackNumericPair(leftValue, rightValue, out Value A, out Value B, out NumKind K))
            {
                _valueStack.Push(Value.FromBool(K switch
                {
                    NumKind.Decimal => (decimal)A.Reference! < (decimal)B.Reference!,
                    NumKind.Double => A.AsDouble < B.AsDouble,
                    NumKind.UInt64 => (ulong)A.Reference! < (ulong)B.Reference!,
                    NumKind.Int64 => A.AsLong < B.AsLong,
                    NumKind.Maximal => (BigInteger)A.Reference! < (BigInteger)B.Reference!,
                    _ => A.AsInt < B.AsInt,
                }));
                return StepResult.Next;
            }
            if (TryGetStringReference(leftValue, out string? ls) && TryGetStringReference(rightValue, out string? rs)) { _valueStack.Push(Value.FromBool(string.CompareOrdinal(ls, rs) < 0)); return StepResult.Next; }
            throw new VMException("Runtime error: LT on non-comparable types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private StepResult HandleGreaterThanInstruction(Instruction instr)
        {
            RequireStack(2, instr, "GT");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();
            if (TryCoerceHotNumericPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                bool comparison = hotPair.Kind switch
                {
                    NumKind.Int64 => hotPair.Int64Left > hotPair.Int64Right,
                    NumKind.Double => hotPair.DoubleLeft > hotPair.DoubleRight,
                    _ => hotPair.Int32Left > hotPair.Int32Right,
                };
                _valueStack.Push(Value.FromBool(comparison));
                return StepResult.Next;
            }

            if (leftValue.IsNull || rightValue.IsNull) { _valueStack.Push(Value.Null); return StepResult.Next; }
            if (TryCompareDecimalBigIntegerPair(leftValue, rightValue, out int decimalBigIntegerComparison))
            {
                _valueStack.Push(Value.FromBool(decimalBigIntegerComparison > 0));
                return StepResult.Next;
            }
            if (TryGetDecimalFloatingSpecialPair(leftValue, rightValue, out double leftSpecialDouble, out double rightSpecialDouble))
            {
                _valueStack.Push(Value.FromBool(leftSpecialDouble > rightSpecialDouble));
                return StepResult.Next;
            }
            if (TryCoerceFallbackNumericPair(leftValue, rightValue, out Value A, out Value B, out NumKind K))
            {
                _valueStack.Push(Value.FromBool(K switch
                {
                    NumKind.Decimal => (decimal)A.Reference! > (decimal)B.Reference!,
                    NumKind.Double => A.AsDouble > B.AsDouble,
                    NumKind.UInt64 => (ulong)A.Reference! > (ulong)B.Reference!,
                    NumKind.Int64 => A.AsLong > B.AsLong,
                    NumKind.Maximal => (BigInteger)A.Reference! > (BigInteger)B.Reference!,
                    _ => A.AsInt > B.AsInt,
                }));
                return StepResult.Next;
            }
            if (TryGetStringReference(leftValue, out string? ls) && TryGetStringReference(rightValue, out string? rs)) { _valueStack.Push(Value.FromBool(string.CompareOrdinal(ls, rs) > 0)); return StepResult.Next; }
            throw new VMException("Runtime error: GT on non-comparable types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private StepResult HandleLessThanOrEqualInstruction(Instruction instr)
        {
            RequireStack(2, instr, "LE");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();
            if (TryCoerceHotNumericPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                bool comparison = hotPair.Kind switch
                {
                    NumKind.Int64 => hotPair.Int64Left <= hotPair.Int64Right,
                    NumKind.Double => hotPair.DoubleLeft <= hotPair.DoubleRight,
                    _ => hotPair.Int32Left <= hotPair.Int32Right,
                };
                _valueStack.Push(Value.FromBool(comparison));
                return StepResult.Next;
            }

            if (leftValue.IsNull || rightValue.IsNull) { _valueStack.Push(Value.Null); return StepResult.Next; }
            if (TryCompareDecimalBigIntegerPair(leftValue, rightValue, out int decimalBigIntegerComparison))
            {
                _valueStack.Push(Value.FromBool(decimalBigIntegerComparison <= 0));
                return StepResult.Next;
            }
            if (TryGetDecimalFloatingSpecialPair(leftValue, rightValue, out double leftSpecialDouble, out double rightSpecialDouble))
            {
                _valueStack.Push(Value.FromBool(leftSpecialDouble <= rightSpecialDouble));
                return StepResult.Next;
            }
            if (TryCoerceFallbackNumericPair(leftValue, rightValue, out Value A, out Value B, out NumKind K))
            {
                _valueStack.Push(Value.FromBool(K switch
                {
                    NumKind.Decimal => (decimal)A.Reference! <= (decimal)B.Reference!,
                    NumKind.Double => A.AsDouble <= B.AsDouble,
                    NumKind.UInt64 => (ulong)A.Reference! <= (ulong)B.Reference!,
                    NumKind.Int64 => A.AsLong <= B.AsLong,
                    NumKind.Maximal => (BigInteger)A.Reference! <= (BigInteger)B.Reference!,
                    _ => A.AsInt <= B.AsInt,
                }));
                return StepResult.Next;
            }
            if (TryGetStringReference(leftValue, out string? ls) && TryGetStringReference(rightValue, out string? rs)) { _valueStack.Push(Value.FromBool(string.CompareOrdinal(ls, rs) <= 0)); return StepResult.Next; }
            throw new VMException("Runtime error: LE on non-comparable types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private StepResult HandleGreaterThanOrEqualInstruction(Instruction instr)
        {
            RequireStack(2, instr, "GE");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();
            if (TryCoerceHotNumericPair(leftValue, rightValue, out HotNumericPair hotPair))
            {
                bool comparison = hotPair.Kind switch
                {
                    NumKind.Int64 => hotPair.Int64Left >= hotPair.Int64Right,
                    NumKind.Double => hotPair.DoubleLeft >= hotPair.DoubleRight,
                    _ => hotPair.Int32Left >= hotPair.Int32Right,
                };
                _valueStack.Push(Value.FromBool(comparison));
                return StepResult.Next;
            }

            if (leftValue.IsNull || rightValue.IsNull) { _valueStack.Push(Value.Null); return StepResult.Next; }
            if (TryCompareDecimalBigIntegerPair(leftValue, rightValue, out int decimalBigIntegerComparison))
            {
                _valueStack.Push(Value.FromBool(decimalBigIntegerComparison >= 0));
                return StepResult.Next;
            }
            if (TryGetDecimalFloatingSpecialPair(leftValue, rightValue, out double leftSpecialDouble, out double rightSpecialDouble))
            {
                _valueStack.Push(Value.FromBool(leftSpecialDouble >= rightSpecialDouble));
                return StepResult.Next;
            }
            if (TryCoerceFallbackNumericPair(leftValue, rightValue, out Value A, out Value B, out NumKind K))
            {
                _valueStack.Push(Value.FromBool(K switch
                {
                    NumKind.Decimal => (decimal)A.Reference! >= (decimal)B.Reference!,
                    NumKind.Double => A.AsDouble >= B.AsDouble,
                    NumKind.UInt64 => (ulong)A.Reference! >= (ulong)B.Reference!,
                    NumKind.Int64 => A.AsLong >= B.AsLong,
                    NumKind.Maximal => (BigInteger)A.Reference! >= (BigInteger)B.Reference!,
                    _ => A.AsInt >= B.AsInt,
                }));
                return StepResult.Next;
            }
            if (TryGetStringReference(leftValue, out string? ls) && TryGetStringReference(rightValue, out string? rs)) { _valueStack.Push(Value.FromBool(string.CompareOrdinal(ls, rs) >= 0)); return StepResult.Next; }
            throw new VMException("Runtime error: GE on non-comparable types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private StepResult HandleNotInstruction(Instruction instr)
        {
            RequireStack(1, instr, "NOT");
            Value value = _valueStack.Pop();
            _valueStack.Push(Value.FromBool(!ToBool(value)));
            return StepResult.Next;
        }

        private StepResult HandleNegateInstruction(Instruction instr)
        {
            RequireStack(1, instr, "NEG");
            Value value = _valueStack.Pop();
            switch (value.Kind)
            {
                case ValueKind.Null:
                    throw NullOperand(instr, "NEG");
                case ValueKind.Int:
                    if (value.AsInt == int.MinValue)
                        _valueStack.Push(Value.FromLong(-(long)value.AsInt));
                    else
                        _valueStack.Push(Value.FromInt(-value.AsInt));
                    return StepResult.Next;
                case ValueKind.Long:
                    if (value.AsLong == long.MinValue)
                        _valueStack.Push(Value.FromRef(-(BigInteger)value.AsLong));
                    else
                        _valueStack.Push(Value.FromLong(-value.AsLong));
                    return StepResult.Next;
                case ValueKind.Double:
                    _valueStack.Push(Value.FromDouble(-value.AsDouble));
                    return StepResult.Next;
                case ValueKind.Char:
                    _valueStack.Push(Value.FromInt(-CharToNumeric(value.AsChar)));
                    return StepResult.Next;
            }

            if (!TryGetFallbackNumericValue(value, out Value v))
            {
                throw new VMException(
                    $"NEG only works on numeric types (got {value.Reference ?? "null"} of type {value.Reference?.GetType().Name ?? value.Kind.ToString()})",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }

            Value res = v.Kind switch
            {
                ValueKind.Int => v.AsInt == int.MinValue ? Value.FromLong(-(long)v.AsInt) : Value.FromInt(-v.AsInt),
                ValueKind.Long => v.AsLong == long.MinValue ? Value.FromRef(-(BigInteger)v.AsLong) : Value.FromLong(-v.AsLong),
                ValueKind.Double => Value.FromDouble(-v.AsDouble),
                ValueKind.Char => Value.FromInt(-CharToNumeric(v.AsChar)),
                ValueKind.Ref => v.Reference switch
                {
                    decimal md => Value.FromRef(-md),
                    float ff => Value.FromRef(-ff),
                    BigInteger bg => NarrowIntegerValue(-bg),
                    ulong uu => NarrowIntegerValue(-(BigInteger)uu),
                    uint ui => Value.FromLong(-(long)ui),
                    short ss => Value.FromInt(-ss),
                    ushort us => Value.FromInt(-us),
                    byte ub => Value.FromInt(-ub),
                    sbyte sb => Value.FromInt(-sb),
                    _ => Value.FromInt(-Convert.ToInt32(v.Reference, CultureInfo.InvariantCulture))
                },
                _ => throw new InvalidOperationException($"Not numeric: {v.Kind}")
            };

            _valueStack.Push(res);
            return StepResult.Next;
        }

        private StepResult HandleAndInstruction(Instruction instr)
        {
            RequireStack(2, instr, "AND");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();
            _valueStack.Push(Value.FromBool(ToBool(leftValue) && ToBool(rightValue)));
            return StepResult.Next;
        }

        private StepResult HandleOrInstruction(Instruction instr)
        {
            RequireStack(2, instr, "OR");
            Value rightValue = _valueStack.Pop();
            Value leftValue = _valueStack.Pop();
            _valueStack.Push(Value.FromBool(ToBool(leftValue) || ToBool(rightValue)));
            return StepResult.Next;
        }
    }
}
