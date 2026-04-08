using CFGS_VM.VMCore.Extensions;
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

        private static bool IsSignedIntegral(object x) => x is sbyte or short or int or long or nint;

        private static bool IsUnsignedIntegral(object x) => x is byte or ushort or uint or ulong or nuint or char;

        private static bool IsNegative(object x) => x switch
        {
            sbyte v => v < 0,
            short v => v < 0,
            int v => v < 0,
            long v => v < 0,
            nint v => v < 0,
            _ => false
        };

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
            if (a is nuint || b is nuint) return NumKind.Int64;
            if (a is long || b is long || a is nint || b is nint) return NumKind.Int64;
            return NumKind.Int32;
        }

        internal static object CharToNumeric(object o)
            => o is char ch ? (char.IsDigit(ch) ? (int)(ch - '0') : (int)ch) : o;

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
                    try { return (ToUInt64Invariant(a), ToUInt64Invariant(b), k); }
                    catch (OverflowException) { return (ToBigIntegerInvariant(a), ToBigIntegerInvariant(b), NumKind.Maximal); }
                case NumKind.Int64:
                    try { return (ToInt64Invariant(a), ToInt64Invariant(b), k); }
                    catch (OverflowException) { return (ToBigIntegerInvariant(a), ToBigIntegerInvariant(b), NumKind.Maximal); }
                default:
                    try { return (ToInt32Invariant(a), ToInt32Invariant(b), NumKind.Int32); }
                    catch (OverflowException)
                    {
                        try { return (ToInt64Invariant(a), ToInt64Invariant(b), NumKind.Int64); }
                        catch (OverflowException) { return (ToBigIntegerInvariant(a), ToBigIntegerInvariant(b), NumKind.Maximal); }
                    }
            }
        }

        private static int ToInt32Invariant(object x) => x switch
        {
            string s => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture),
            IConvertible _ => Convert.ToInt32(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        private static long ToInt64Invariant(object x) => x switch
        {
            string s => long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture),
            IConvertible _ => Convert.ToInt64(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        private static ulong ToUInt64Invariant(object x) => x switch
        {
            string s => ulong.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture),
            IConvertible _ => Convert.ToUInt64(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        private static double ToDoubleInvariant(object x) => x switch
        {
            string s => double.Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
            IConvertible _ => Convert.ToDouble(x, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Not numeric: {x?.GetType().Name ?? "null"}")
        };

        private static decimal ToDecimalInvariant(object x) => x switch
        {
            string s => decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture),
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

        private object PowBigIntegerOrThrow(BigInteger a, BigInteger b, Instruction instr)
        {
            if (b.Sign < 0)
                throw new VMException("EXPO with BigInteger requires non-negative exponent", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            if (b > int.MaxValue)
                throw new VMException("EXPO exponent too large for BigInteger.Pow", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            return BigInteger.Pow(a, (int)b);
        }

        private int ToInt32ForRepeat(object n)
        {
            BigInteger bi = n is BigInteger b ? b : ToBigInt(n);
            if (bi.Sign < 0) throw new VMException("repeat count must be non-negative", 0, 0, null, IsDebugging, DebugStream!);
            return checked((int)bi);
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

        private StepResult HandleAddInstruction(Instruction instr)
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
                return StepResult.Next;
            }

            if (l is null || r is null)
            {
                if (l is string || r is string)
                    _stack.Push((l as string ?? "") + (r as string ?? ""));
                else
                    _stack.Push(null!);
                return StepResult.Next;
            }

            if (l is List<object> || r is List<object> ||
                l is Dictionary<string, object> || r is Dictionary<string, object>)
            {
                string ls;
                string rs;
                using (StringWriter lw = new()) { PrintValue(l, lw); ls = lw.ToString(); }
                using (StringWriter rw = new()) { PrintValue(r, rw); rs = rw.ToString(); }
                _stack.Push(ls + rs);
                return StepResult.Next;
            }

            _stack.Push(l.ToString() + r.ToString());
            return StepResult.Next;
        }

        private StepResult HandleSubInstruction(Instruction instr)
        {
            RequireStack(2, instr, "SUB");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null)
            {
                _stack.Push(null!);
                return StepResult.Next;
            }

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
            return StepResult.Next;
        }

        private StepResult HandleMulInstruction(Instruction instr)
        {
            RequireStack(2, instr, "MUL");
            object r = _stack.Pop();
            object l = _stack.Pop();

            if (l is null || r is null)
            {
                if (l is string && IsNumber(r!))
                {
                    _stack.Push(string.Concat(Enumerable.Repeat(l as string ?? "", checked((int)ToInt32ForRepeat(r!)))));
                    return StepResult.Next;
                }

                if (r is string && IsNumber(l!))
                {
                    _stack.Push(string.Concat(Enumerable.Repeat(r as string ?? "", checked((int)ToInt32ForRepeat(l!)))));
                    return StepResult.Next;
                }

                _stack.Push(null!);
                return StepResult.Next;
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
                return StepResult.Next;
            }

            if (l is string && IsNumber(r))
            {
                _stack.Push(string.Concat(Enumerable.Repeat(l as string ?? "", checked((int)ToInt32ForRepeat(r)))));
                return StepResult.Next;
            }

            if (r is string && IsNumber(l))
            {
                _stack.Push(string.Concat(Enumerable.Repeat(r as string ?? "", checked((int)ToInt32ForRepeat(l)))));
                return StepResult.Next;
            }

            throw new VMException("MUL on non-numeric types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private StepResult HandleModInstruction(Instruction instr)
        {
            RequireStack(2, instr, "MOD");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null)
            {
                _stack.Push(null!);
                return StepResult.Next;
            }

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
            return StepResult.Next;
        }

        private StepResult HandleDivInstruction(Instruction instr)
        {
            RequireStack(2, instr, "DIV");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null)
            {
                _stack.Push(null!);
                return StepResult.Next;
            }

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
            return StepResult.Next;
        }

        private StepResult HandleExpoInstruction(Instruction instr)
        {
            RequireStack(2, instr, "EXPO");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null)
            {
                _stack.Push(null!);
                return StepResult.Next;
            }

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
            return StepResult.Next;
        }

        private StepResult HandleBitAndInstruction(Instruction instr)
        {
            RequireStack(2, instr, "BIT_AND");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null) { _stack.Push(null!); return StepResult.Next; }
            r = r is char ? CharToNumeric(r) : r;
            l = l is char ? CharToNumeric(l) : l;
            if (!(IsIntegralLike(l) && IsIntegralLike(r)))
                throw new VMException("BIT_AND requires integral types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            if (l is BigInteger || r is BigInteger) _stack.Push(ToBigInt(l) & ToBigInt(r));
            else if (l is long or ulong || r is long or ulong) _stack.Push(Convert.ToInt64(l) & Convert.ToInt64(r));
            else _stack.Push(Convert.ToInt32(l) & Convert.ToInt32(r));
            return StepResult.Next;
        }

        private StepResult HandleBitOrInstruction(Instruction instr)
        {
            RequireStack(2, instr, "BIT_OR");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null) { _stack.Push(null!); return StepResult.Next; }
            r = r is char ? CharToNumeric(r) : r;
            l = l is char ? CharToNumeric(l) : l;
            if (!(IsIntegralLike(l) && IsIntegralLike(r)))
                throw new VMException("BIT_OR requires integral types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            if (l is BigInteger || r is BigInteger) _stack.Push(ToBigInt(l) | ToBigInt(r));
            else if (l is long or ulong || r is long or ulong) _stack.Push(Convert.ToInt64(l) | Convert.ToInt64(r));
            else _stack.Push(Convert.ToInt32(l) | Convert.ToInt32(r));
            return StepResult.Next;
        }

        private StepResult HandleBitXorInstruction(Instruction instr)
        {
            RequireStack(2, instr, "BIT_XOR");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null) { _stack.Push(null!); return StepResult.Next; }
            r = r is char ? CharToNumeric(r) : r;
            l = l is char ? CharToNumeric(l) : l;
            if (!(IsIntegralLike(l) && IsIntegralLike(r)))
                throw new VMException("BIT_XOR requires integral types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            if (l is BigInteger || r is BigInteger) _stack.Push(ToBigInt(l) ^ ToBigInt(r));
            else if (l is long or ulong || r is long or ulong) _stack.Push(Convert.ToInt64(l) ^ Convert.ToInt64(r));
            else _stack.Push(Convert.ToInt32(l) ^ Convert.ToInt32(r));
            return StepResult.Next;
        }

        private StepResult HandleShiftLeftInstruction(Instruction instr)
        {
            RequireStack(2, instr, "SHL");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null) { _stack.Push(null!); return StepResult.Next; }
            r = r is char ? CharToNumeric(r) : r;
            l = l is char ? CharToNumeric(l) : l;
            if (!(IsIntegralLike(l) && IsNumber(r)))
                throw new VMException("SHL requires (integral) << int", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            int sh = Convert.ToInt32(r) & 0x3F;
            if (l is BigInteger) _stack.Push(((BigInteger)l) << sh);
            else if (l is long or ulong) _stack.Push(Convert.ToInt64(l) << sh);
            else _stack.Push(Convert.ToInt32(l) << (sh & 0x1F));
            return StepResult.Next;
        }

        private StepResult HandleShiftRightInstruction(Instruction instr)
        {
            RequireStack(2, instr, "SHR");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null) { _stack.Push(null!); return StepResult.Next; }
            r = r is char ? CharToNumeric(r) : r;
            l = l is char ? CharToNumeric(l) : l;
            if (!(IsIntegralLike(l) && IsNumber(r)))
                throw new VMException("SHR requires (integral) >> int", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            int sh = Convert.ToInt32(r) & 0x3F;
            if (l is BigInteger) _stack.Push(((BigInteger)l) >> sh);
            else if (l is long or ulong) _stack.Push(Convert.ToInt64(l) >> sh);
            else _stack.Push(Convert.ToInt32(l) >> (sh & 0x1F));
            return StepResult.Next;
        }

        private StepResult HandleEqualInstruction(Instruction instr)
        {
            RequireStack(2, instr, "EQ");
            object r = _stack.Pop();
            object l = _stack.Pop();
            bool res = IsNumber(l) && IsNumber(r)
                ? CompareAsDecimal(l) == CompareAsDecimal(r)
                : l is string ls && r is string rs
                    ? string.Equals(ls, rs, StringComparison.Ordinal)
                    : Equals(l, r);
            _stack.Push(res);
            return StepResult.Next;
        }

        private StepResult HandleNotEqualInstruction(Instruction instr)
        {
            RequireStack(2, instr, "NEQ");
            object r = _stack.Pop();
            object l = _stack.Pop();
            bool res = IsNumber(l) && IsNumber(r)
                ? CompareAsDecimal(l) != CompareAsDecimal(r)
                : l is string ls && r is string rs
                    ? !string.Equals(ls, rs, StringComparison.Ordinal)
                    : !Equals(l, r);
            _stack.Push(res);
            return StepResult.Next;
        }

        private StepResult HandleIsTypeInstruction(Instruction instr)
        {
            RequireStack(2, instr, "IS_TYPE");
            object? right = _stack.Pop();
            object? left = _stack.Pop();
            if (left is not ClassInstance ci || right is not StaticInstance target)
            {
                _stack.Push(false);
                return StepResult.Next;
            }

            bool found = false;
            if (TryGetStaticType(ci, out StaticInstance instType))
            {
                found = IsInterfaceType(target)
                    ? ImplementsInterface(instType, target)
                    : IsSameOrDerivedStaticType(instType, target);
            }
            _stack.Push(found);
            return StepResult.Next;
        }

        private StepResult HandleLessThanInstruction(Instruction instr)
        {
            RequireStack(2, instr, "LT");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null) { _stack.Push(null!); return StepResult.Next; }
            if (IsNumber(l) && IsNumber(r))
            {
                (object A, object B, NumKind K) = CoercePair(l, r);
                _stack.Push(K switch
                {
                    NumKind.Decimal => (decimal)A < (decimal)B,
                    NumKind.Double => (double)A < (double)B,
                    NumKind.UInt64 => (ulong)A < (ulong)B,
                    NumKind.Int64 => (long)A < (long)B,
                    _ => (int)A < (int)B,
                });
                return StepResult.Next;
            }
            if (l is string ls && r is string rs) { _stack.Push(string.CompareOrdinal(ls, rs) < 0); return StepResult.Next; }
            throw new VMException("Runtime error: LT on non-comparable types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private StepResult HandleGreaterThanInstruction(Instruction instr)
        {
            RequireStack(2, instr, "GT");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null) { _stack.Push(null!); return StepResult.Next; }
            if (IsNumber(l) && IsNumber(r))
            {
                (object A, object B, NumKind K) = CoercePair(l, r);
                _stack.Push(K switch
                {
                    NumKind.Decimal => (decimal)A > (decimal)B,
                    NumKind.Double => (double)A > (double)B,
                    NumKind.UInt64 => (ulong)A > (ulong)B,
                    NumKind.Int64 => (long)A > (long)B,
                    _ => (int)A > (int)B,
                });
                return StepResult.Next;
            }
            if (l is string ls && r is string rs) { _stack.Push(string.CompareOrdinal(ls, rs) > 0); return StepResult.Next; }
            throw new VMException("Runtime error: GT on non-comparable types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private StepResult HandleLessThanOrEqualInstruction(Instruction instr)
        {
            RequireStack(2, instr, "LE");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null) { _stack.Push(null!); return StepResult.Next; }
            if (IsNumber(l) && IsNumber(r))
            {
                (object A, object B, NumKind K) = CoercePair(l, r);
                _stack.Push(K switch
                {
                    NumKind.Decimal => (decimal)A <= (decimal)B,
                    NumKind.Double => (double)A <= (double)B,
                    NumKind.UInt64 => (ulong)A <= (ulong)B,
                    NumKind.Int64 => (long)A <= (long)B,
                    _ => (int)A <= (int)B,
                });
                return StepResult.Next;
            }
            if (l is string ls && r is string rs) { _stack.Push(string.CompareOrdinal(ls, rs) <= 0); return StepResult.Next; }
            throw new VMException("Runtime error: LE on non-comparable types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private StepResult HandleGreaterThanOrEqualInstruction(Instruction instr)
        {
            RequireStack(2, instr, "GE");
            object r = _stack.Pop();
            object l = _stack.Pop();
            if (l is null || r is null) { _stack.Push(null!); return StepResult.Next; }
            if (IsNumber(l) && IsNumber(r))
            {
                (object A, object B, NumKind K) = CoercePair(l, r);
                _stack.Push(K switch
                {
                    NumKind.Decimal => (decimal)A >= (decimal)B,
                    NumKind.Double => (double)A >= (double)B,
                    NumKind.UInt64 => (ulong)A >= (ulong)B,
                    NumKind.Int64 => (long)A >= (long)B,
                    _ => (int)A >= (int)B,
                });
                return StepResult.Next;
            }
            if (l is string ls && r is string rs) { _stack.Push(string.CompareOrdinal(ls, rs) >= 0); return StepResult.Next; }
            throw new VMException("Runtime error: GE on non-comparable types", instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
        }

        private StepResult HandleNotInstruction(Instruction instr)
        {
            RequireStack(1, instr, "NOT");
            object v = _stack.Pop();
            if (v is null) { _stack.Push(true); return StepResult.Next; }
            _stack.Push(!ToBool(v));
            return StepResult.Next;
        }

        private StepResult HandleNegateInstruction(Instruction instr)
        {
            RequireStack(1, instr, "NEG");
            object? v = _stack.Pop();
            if (v is null) { _stack.Push(null!); return StepResult.Next; }
            if (!IsNumber(v))
            {
                throw new VMException(
                    $"NEG only works on numeric types (got {v ?? "null"} of type {v?.GetType().Name ?? "null"})",
                    instr.Line, instr.Col, instr.OriginFile, IsDebugging, DebugStream!);
            }

            object res =
                v is decimal md ? (object)(-md) :
                v is double dd ? (object)(-dd) :
                v is float ff ? (object)(-ff) :
                v is long ll ? (object)(-ll) :
                v is ulong uu ? (object)unchecked((long)-(long)uu) :
                (object)(-Convert.ToInt32(v));

            _stack.Push(res);
            return StepResult.Next;
        }

        private StepResult HandleAndInstruction(Instruction instr)
        {
            RequireStack(2, instr, "AND");
            object r = _stack.Pop();
            object l = _stack.Pop();
            _stack.Push(ToBool(l) && ToBool(r));
            return StepResult.Next;
        }

        private StepResult HandleOrInstruction(Instruction instr)
        {
            RequireStack(2, instr, "OR");
            object r = _stack.Pop();
            object l = _stack.Pop();
            _stack.Push(ToBool(l) || ToBool(r));
            return StepResult.Next;
        }
    }
}
