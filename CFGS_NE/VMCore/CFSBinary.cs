using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extension;
using System.Text;

namespace CFGS_VM.VMCore.IO
{
    /// <summary>
    /// Defines the <see cref="CFSBinary" />
    /// </summary>
    public static class CFSBinary
    {
        /// <summary>
        /// Defines the MAGIC
        /// </summary>
        private const string MAGIC = "CFB2";

        /// <summary>
        /// Defines the VERSION
        /// </summary>
        private const int VERSION = 2;

        /// <summary>
        /// The Save
        /// </summary>
        /// <param name="path">The path<see cref="string"/></param>
        /// <param name="insns">The insns<see cref="List{Instruction}"/></param>
        /// <param name="funcs">The funcs<see cref="Dictionary{string, FunctionInfo}"/></param>
        public static void Save(string path, List<Instruction> insns, Dictionary<string, FunctionInfo> funcs)
        {
            if (insns is null) throw new ArgumentNullException(nameof(insns));
            if (funcs is null) throw new ArgumentNullException(nameof(funcs));

            using FileStream fs = new(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            using BinaryWriter w = new(fs, Encoding.UTF8, leaveOpen: false);

            w.Write(MAGIC);
            w.Write(VERSION);

            w.Write(funcs.Count);
            foreach (KeyValuePair<string, FunctionInfo> kv in funcs)
            {
                w.Write(kv.Key);
                WriteStringList(w, kv.Value.Parameters);
                w.Write(kv.Value.Address);
            }

            w.Write(insns.Count);
            foreach (Instruction ins in insns)
            {
                w.Write((int)ins.Code);
                WriteOperand(w, ins.Operand);
                w.Write(ins.Line);
                w.Write(ins.Col);
                w.Write(string.Empty);
            }
        }

        /// <summary>
        /// The Load
        /// </summary>
        /// <param name="path">The path<see cref="string"/></param>
        /// <returns>The <see cref="(List{Instruction} insns, Dictionary{string, FunctionInfo} funcs)"/></returns>
        public static (List<Instruction> insns, Dictionary<string, FunctionInfo> funcs) Load(string path)
        {
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader r = new(fs, Encoding.UTF8, leaveOpen: false);

            string magic = r.ReadString();
            if (!string.Equals(magic, MAGIC, StringComparison.Ordinal))
                throw new InvalidDataException("Couldnt execute binary");

            int version = r.ReadInt32();
            if (version != VERSION)
                throw new InvalidDataException($"Unsupported bytecode version {version}");

            int fcount = r.ReadInt32();
            Dictionary<string, FunctionInfo> funcs = new(StringComparer.Ordinal);
            for (int i = 0; i < fcount; i++)
            {
                string name = r.ReadString();
                List<string> pars = ReadStringList(r);
                int addr = r.ReadInt32();
                funcs[name] = new FunctionInfo(pars, addr);
            }

            int icount = r.ReadInt32();
            List<Instruction> insns = new(icount);
            for (int i = 0; i < icount; i++)
            {
                OpCode op = (OpCode)r.ReadInt32();
                object? operand = ReadOperand(r);
                int line = r.ReadInt32();
                int col = r.ReadInt32();
                string origin = r.ReadString();
                insns.Add(new Instruction(op, operand, line, col, origin));
            }

            return (insns, funcs);
        }

        /// <summary>
        /// Defines the Tag
        /// </summary>
        private enum Tag : byte
        {
            /// <summary>
            /// Defines the Null
            /// </summary>
            Null = 0,

            /// <summary>
            /// Defines the Int32
            /// </summary>
            Int32 = 1,

            /// <summary>
            /// Defines the Int64
            /// </summary>
            Int64 = 2,

            /// <summary>
            /// Defines the Single
            /// </summary>
            Single = 3,

            /// <summary>
            /// Defines the Double
            /// </summary>
            Double = 4,

            /// <summary>
            /// Defines the Decimal
            /// </summary>
            Decimal = 5,

            /// <summary>
            /// Defines the Boolean
            /// </summary>
            Boolean = 6,

            /// <summary>
            /// Defines the Char
            /// </summary>
            Char = 7,

            /// <summary>
            /// Defines the String
            /// </summary>
            String = 8,

            /// <summary>
            /// Defines the Int32Array
            /// </summary>
            Int32Array = 9,

            /// <summary>
            /// Defines the ObjectArray
            /// </summary>
            ObjectArray = 10
        }

        /// <summary>
        /// The WriteOperand
        /// </summary>
        /// <param name="w">The w<see cref="BinaryWriter"/></param>
        /// <param name="operand">The operand<see cref="object?"/></param>
        private static void WriteOperand(BinaryWriter w, object? operand)
        {
            if (operand is null) { w.Write((byte)Tag.Null); return; }

            switch (operand)
            {
                case int i: w.Write((byte)Tag.Int32); w.Write(i); return;
                case long l: w.Write((byte)Tag.Int64); w.Write(l); return;
                case float f: w.Write((byte)Tag.Single); w.Write(f); return;
                case double d: w.Write((byte)Tag.Double); w.Write(d); return;
                case decimal m: w.Write((byte)Tag.Decimal); w.Write(m); return;
                case bool b: w.Write((byte)Tag.Boolean); w.Write(b); return;
                case char c: w.Write((byte)Tag.Char); w.Write(c); return;
                case string s: w.Write((byte)Tag.String); w.Write(s); return;
                case int[] arrI:
                    w.Write((byte)Tag.Int32Array);
                    w.Write(arrI.Length);
                    for (int k = 0; k < arrI.Length; k++) w.Write(arrI[k]);
                    return;

                case object[] arrO:
                    w.Write((byte)Tag.ObjectArray);
                    w.Write(arrO.Length);
                    foreach (object elem in arrO) WriteOperand(w, elem);
                    return;

                default:
                    throw new NotSupportedException($"Operand type not supported for serialization: {operand.GetType().Name}");
            }
        }

        /// <summary>
        /// The ReadOperand
        /// </summary>
        /// <param name="r">The r<see cref="BinaryReader"/></param>
        /// <returns>The <see cref="object?"/></returns>
        private static object? ReadOperand(BinaryReader r)
        {
            Tag tag = (Tag)r.ReadByte();
            return tag switch
            {
                Tag.Null => null,
                Tag.Int32 => r.ReadInt32(),
                Tag.Int64 => r.ReadInt64(),
                Tag.Single => r.ReadSingle(),
                Tag.Double => r.ReadDouble(),
                Tag.Decimal => r.ReadDecimal(),
                Tag.Boolean => r.ReadBoolean(),
                Tag.Char => r.ReadChar(),
                Tag.String => r.ReadString(),
                Tag.Int32Array => ReadInt32Array(r),
                Tag.ObjectArray => ReadObjectArray(r),
                _ => throw new InvalidDataException($"Unknown operand tag: {tag}")
            };
        }

        /// <summary>
        /// The ReadInt32Array
        /// </summary>
        /// <param name="r">The r<see cref="BinaryReader"/></param>
        /// <returns>The <see cref="int[]"/></returns>
        private static int[] ReadInt32Array(BinaryReader r)
        {
            int len = r.ReadInt32();
            int[] a = new int[len];
            for (int i = 0; i < len; i++) a[i] = r.ReadInt32();
            return a;
        }

        /// <summary>
        /// The ReadObjectArray
        /// </summary>
        /// <param name="r">The r<see cref="BinaryReader"/></param>
        /// <returns>The <see cref="object[]"/></returns>
        private static object[] ReadObjectArray(BinaryReader r)
        {
            int len = r.ReadInt32();
            object?[] a = new object?[len];
            for (int i = 0; i < len; i++) a[i] = ReadOperand(r);
            object[] ret = new object[len];
            for (int i = 0; i < len; i++) ret[i] = a[i]!;
            return ret;
        }

        /// <summary>
        /// The WriteStringList
        /// </summary>
        /// <param name="w">The w<see cref="BinaryWriter"/></param>
        /// <param name="items">The items<see cref="List{string}"/></param>
        private static void WriteStringList(BinaryWriter w, List<string> items)
        {
            w.Write(items.Count);
            foreach (string s in items) w.Write(s ?? string.Empty);
        }

        /// <summary>
        /// The ReadStringList
        /// </summary>
        /// <param name="r">The r<see cref="BinaryReader"/></param>
        /// <returns>The <see cref="List{string}"/></returns>
        private static List<string> ReadStringList(BinaryReader r)
        {
            int n = r.ReadInt32();
            List<string> list = new(n);
            for (int i = 0; i < n; i++) list.Add(r.ReadString());
            return list;
        }
    }
}
