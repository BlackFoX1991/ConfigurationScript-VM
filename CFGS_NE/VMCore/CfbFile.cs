using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Interop;
using System.Numerics;
using System.Text;

namespace CFGS_VM.VMCore
{
    public static class CfbFile
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CFB1");

        private enum OperandKind : byte
        {
            Null = 0,
            Int32 = 1,
            Int64 = 2,
            Single = 3,
            Double = 4,
            Decimal = 5,
            BigInteger = 6,
            String = 7,
            Char = 8,
            Boolean = 9,
            ObjectArray = 10,
            RuntimePropertyDescriptor = 11,
            NativeAssemblyReference = 12,
            ScopeDescriptor = 13,
        }

        public static void Save(CompiledScript script, string path)
        {
            using FileStream stream = File.Create(path);
            using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: false);

            writer.Write(Magic);
            writer.Write(script.BytecodeVersion);
            writer.Write(SanitizeSourceName(script.Name));
            writer.Write(script.SourceHash);
            writer.Write(script.CompilerVersion);

            writer.Write(script.RequiredPlugins.Count);
            foreach (string plugin in script.RequiredPlugins)
                writer.Write(SanitizePluginReference(plugin));

            writer.Write(script.Functions.Count);
            foreach (KeyValuePair<string, FunctionInfo> kv in script.Functions.OrderBy(static x => x.Key, StringComparer.Ordinal))
            {
                FunctionInfo function = kv.Value;
                writer.Write(kv.Key);
                writer.Write(function.Address);
                writer.Write(function.MinArgs);
                writer.Write(function.RestParameter is not null);
                if (function.RestParameter is not null)
                    writer.Write(function.RestParameter);
                writer.Write(function.isAsync);
                writer.Write(function.Parameters.Count);
                foreach (string parameter in function.Parameters)
                    writer.Write(parameter);
            }

            writer.Write(script.RootScopeDescriptors.Count);
            foreach (KeyValuePair<int, ScopeDescriptor> kv in script.RootScopeDescriptors.OrderBy(static x => x.Key))
            {
                writer.Write(kv.Key);
                WriteScopeDescriptor(writer, kv.Value);
            }

            writer.Write(script.Instructions.Count);
            foreach (Instruction instruction in script.Instructions)
            {
                writer.Write((int)instruction.Code);
                WriteOperand(writer, instruction.Operand);
                writer.Write(instruction.Line);
                writer.Write(instruction.Col);
                writer.Write(SanitizeSourceName(instruction.OriginFile));
            }
        }

        public static CompiledScript Load(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);

            byte[] magic = reader.ReadBytes(Magic.Length);
            if (!magic.SequenceEqual(Magic))
                throw new InvalidDataException("invalid CFB file header");

            int bytecodeVersion = reader.ReadInt32();
            if (bytecodeVersion != CompiledScript.CurrentBytecodeVersion)
            {
                throw new InvalidDataException(
                    $"unsupported CFB bytecode version {bytecodeVersion}; expected {CompiledScript.CurrentBytecodeVersion}");
            }

            string name = reader.ReadString();
            string sourceHash = reader.ReadString();
            string compilerVersion = reader.ReadString();

            int pluginCount = ReadCount(reader);
            List<string> requiredPlugins = new(pluginCount);
            for (int i = 0; i < pluginCount; i++)
                requiredPlugins.Add(reader.ReadString());

            int functionCount = ReadCount(reader);
            Dictionary<string, FunctionInfo> functions = new(StringComparer.Ordinal);
            for (int i = 0; i < functionCount; i++)
            {
                string key = reader.ReadString();
                int address = reader.ReadInt32();
                int minArgs = reader.ReadInt32();
                string? restParameter = reader.ReadBoolean() ? reader.ReadString() : null;
                bool isAsync = reader.ReadBoolean();
                int parameterCount = ReadCount(reader);
                List<string> parameters = new(parameterCount);
                for (int p = 0; p < parameterCount; p++)
                    parameters.Add(reader.ReadString());

                functions[key] = new FunctionInfo(parameters, address, minArgs, restParameter, isAsync);
            }

            int rootDescriptorCount = ReadCount(reader);
            Dictionary<int, ScopeDescriptor> rootScopeDescriptors = new(rootDescriptorCount);
            for (int i = 0; i < rootDescriptorCount; i++)
            {
                int address = reader.ReadInt32();
                rootScopeDescriptors[address] = ReadScopeDescriptor(reader);
            }

            int instructionCount = ReadCount(reader);
            List<Instruction> instructions = new(instructionCount);
            for (int i = 0; i < instructionCount; i++)
            {
                int rawCode = reader.ReadInt32();
                if (!Enum.IsDefined(typeof(OpCode), rawCode))
                    throw new InvalidDataException($"corrupt CFB file: unknown opcode {rawCode}");
                OpCode code = (OpCode)rawCode;
                object? operand = ReadOperand(reader);
                int line = reader.ReadInt32();
                int col = reader.ReadInt32();
                string originFile = reader.ReadString();
                instructions.Add(new Instruction(code, operand, line, col, originFile));
            }

            return new CompiledScript(
                name,
                sourceHash,
                compilerVersion,
                bytecodeVersion,
                requiredPlugins,
                instructions,
                functions,
                rootScopeDescriptors);
        }

        private static void WriteOperand(BinaryWriter writer, object? operand)
        {
            switch (operand)
            {
                case null:
                    writer.Write((byte)OperandKind.Null);
                    break;
                case int value:
                    writer.Write((byte)OperandKind.Int32);
                    writer.Write(value);
                    break;
                case long value:
                    writer.Write((byte)OperandKind.Int64);
                    writer.Write(value);
                    break;
                case float value:
                    writer.Write((byte)OperandKind.Single);
                    writer.Write(value);
                    break;
                case double value:
                    writer.Write((byte)OperandKind.Double);
                    writer.Write(value);
                    break;
                case decimal value:
                    writer.Write((byte)OperandKind.Decimal);
                    foreach (int part in decimal.GetBits(value))
                        writer.Write(part);
                    break;
                case BigInteger value:
                    writer.Write((byte)OperandKind.BigInteger);
                    byte[] bytes = value.ToByteArray();
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                    break;
                case string value:
                    writer.Write((byte)OperandKind.String);
                    writer.Write(value);
                    break;
                case char value:
                    writer.Write((byte)OperandKind.Char);
                    // Write the raw UTF-16 code unit, not via the BinaryWriter's UTF-8 char path,
                    // which cannot encode lone surrogates (valid CFGS char literals).
                    writer.Write((ushort)value);
                    break;
                case bool value:
                    writer.Write((byte)OperandKind.Boolean);
                    writer.Write(value);
                    break;
                case object[] values:
                    writer.Write((byte)OperandKind.ObjectArray);
                    writer.Write(values.Length);
                    foreach (object? value in values)
                        WriteOperand(writer, value);
                    break;
                case RuntimePropertyDescriptor descriptor:
                    writer.Write((byte)OperandKind.RuntimePropertyDescriptor);
                    WriteRuntimePropertyDescriptor(writer, descriptor);
                    break;
                case NativeAssemblyReference nativeAssembly:
                    writer.Write((byte)OperandKind.NativeAssemblyReference);
                    writer.Write(nativeAssembly.Alias);
                    writer.Write(SanitizePluginReference(nativeAssembly.Path));
                    break;
                case ScopeDescriptor descriptor:
                    writer.Write((byte)OperandKind.ScopeDescriptor);
                    WriteScopeDescriptor(writer, descriptor);
                    break;
                default:
                    throw new InvalidDataException($"unsupported CFB operand type '{operand.GetType().FullName}'");
            }
        }

        /// <summary>
        /// Reads a length-prefixed element count and validates it against the bytes remaining in the
        /// stream. Every element serializes to at least one byte, so the remaining byte count is a safe
        /// upper bound. This prevents a corrupt or hostile <c>.cfb</c> from forcing a huge allocation
        /// (e.g. <c>count = int.MaxValue</c> → <see cref="OutOfMemoryException"/>) before any data is read.
        /// </summary>
        private static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            if (count < 0 || count > remaining)
                throw new InvalidDataException(
                    $"corrupt CFB file: element count {count} exceeds remaining data ({remaining} bytes)");
            return count;
        }

        private static object? ReadOperand(BinaryReader reader)
        {
            OperandKind kind = (OperandKind)reader.ReadByte();
            return kind switch
            {
                OperandKind.Null => null,
                OperandKind.Int32 => reader.ReadInt32(),
                OperandKind.Int64 => reader.ReadInt64(),
                OperandKind.Single => reader.ReadSingle(),
                OperandKind.Double => reader.ReadDouble(),
                OperandKind.Decimal => ReadDecimal(reader),
                OperandKind.BigInteger => new BigInteger(reader.ReadBytes(ReadCount(reader))),
                OperandKind.String => reader.ReadString(),
                OperandKind.Char => (char)reader.ReadUInt16(),
                OperandKind.Boolean => reader.ReadBoolean(),
                OperandKind.ObjectArray => ReadObjectArray(reader),
                OperandKind.RuntimePropertyDescriptor => ReadRuntimePropertyDescriptor(reader),
                OperandKind.NativeAssemblyReference => new NativeAssemblyReference(reader.ReadString(), reader.ReadString()),
                OperandKind.ScopeDescriptor => ReadScopeDescriptor(reader),
                _ => throw new InvalidDataException($"unsupported CFB operand kind '{kind}'")
            };
        }

        private static void WriteScopeDescriptor(BinaryWriter writer, ScopeDescriptor descriptor)
        {
            writer.Write(descriptor.SlotCount);
            foreach (string slotName in descriptor.SlotNames)
                writer.Write(slotName);
        }

        private static ScopeDescriptor ReadScopeDescriptor(BinaryReader reader)
        {
            int slotCount = ReadCount(reader);
            string[] slotNames = new string[slotCount];
            for (int i = 0; i < slotCount; i++)
                slotNames[i] = reader.ReadString();
            return ScopeDescriptor.FromSlotNames(slotNames);
        }

        private static decimal ReadDecimal(BinaryReader reader)
        {
            int lo = reader.ReadInt32();
            int mid = reader.ReadInt32();
            int hi = reader.ReadInt32();
            int flags = reader.ReadInt32();
            try
            {
                return new decimal([lo, mid, hi, flags]);
            }
            catch (ArgumentException ex)
            {
                // A corrupt artifact can carry invalid scale/flag bits; surface the loader's
                // documented clean failure instead of a raw ArgumentException.
                throw new InvalidDataException("corrupt CFB file: invalid decimal operand", ex);
            }
        }

        private static object?[] ReadObjectArray(BinaryReader reader)
        {
            int count = ReadCount(reader);
            object?[] values = new object?[count];
            for (int i = 0; i < count; i++)
                values[i] = ReadOperand(reader);
            return values;
        }

        private static void WriteNullableString(BinaryWriter writer, string? value)
        {
            writer.Write(value is not null);
            if (value is not null)
                writer.Write(value);
        }

        private static string? ReadNullableString(BinaryReader reader)
            => reader.ReadBoolean() ? reader.ReadString() : null;

        private static string SanitizePluginReference(string plugin)
        {
            string fileName = Path.GetFileName(plugin);
            return string.IsNullOrWhiteSpace(fileName) ? plugin : fileName;
        }

        private static string SanitizeSourceName(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName))
                return "<source>";

            if (sourceName.StartsWith("<", StringComparison.Ordinal) &&
                sourceName.EndsWith(">", StringComparison.Ordinal))
            {
                return sourceName;
            }

            if (Uri.TryCreate(sourceName, UriKind.Absolute, out Uri? uri) &&
                !string.IsNullOrWhiteSpace(uri.LocalPath))
            {
                string uriFileName = Path.GetFileName(uri.LocalPath);
                return string.IsNullOrWhiteSpace(uriFileName) ? uri.Host : uriFileName;
            }

            string fileName = Path.GetFileName(sourceName);
            return string.IsNullOrWhiteSpace(fileName) ? "<source>" : fileName;
        }

        private static void WriteRuntimePropertyDescriptor(BinaryWriter writer, RuntimePropertyDescriptor descriptor)
        {
            writer.Write(descriptor.Name);
            writer.Write(descriptor.IsStatic);
            writer.Write(descriptor.HasGetter);
            writer.Write(descriptor.HasSetter);
            writer.Write(descriptor.HasInit);
            writer.Write(descriptor.GetterVisibilityCode);
            writer.Write(descriptor.SetterVisibilityCode);
            writer.Write(descriptor.InitVisibilityCode);
            WriteNullableString(writer, descriptor.GetterSlotName);
            WriteNullableString(writer, descriptor.SetterSlotName);
            WriteNullableString(writer, descriptor.InitSlotName);
            WriteNullableString(writer, descriptor.BackingFieldName);
            writer.Write(descriptor.HasAutoStorage);
        }

        private static RuntimePropertyDescriptor ReadRuntimePropertyDescriptor(BinaryReader reader)
            => new(
                reader.ReadString(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                ReadNullableString(reader),
                ReadNullableString(reader),
                ReadNullableString(reader),
                ReadNullableString(reader),
                reader.ReadBoolean());
    }
}
